const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

test('actual radar submission methods consume repeats but upload new generations and textures', () => {
  const source = fs.readFileSync(path.join(__dirname, '../src/SCDEFogOfWarPlugin.cs'), 'utf8');
  const methods = source.slice(source.indexOf('        internal struct RadarSubmitState'),
    source.indexOf('        private bool EnsureRadarFogIndexMap('));
  assert.ok(methods.includes('BeginRadarSubmit'));
  const resetPreference = source.slice(source.indexOf('        internal static bool PreferFullMapRadarOnReset('),
    source.indexOf('        internal static bool PreventRadarZoom('));
  assert.ok(resetPreference.includes('configuredZoomOut'));
  assert.match(source, /_cachedFilteredRadarMap = filtered;\s+_radarOutputGeneration\+\+/);
  const reset = source.slice(source.indexOf('        private void ResetMapState()'),
    source.indexOf('        private void DestroyFogObjects()'));
  assert.match(reset, /_uploadedRadarGeneration = -1;/);
  assert.match(reset, /_uploadedRadarTexture = null;/);
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'scde-radar-submit-'));
  try {
    const fixture = `using System;
class Texture2D {}
class GameMap { public Texture2D texture = new Texture2D(); public Texture2D getRadarTexture() { return texture; } }
class SCDEFogOfWarPlugin {
 static SCDEFogOfWarPlugin _instance;
 bool _enabled = true; GameMap _activeMap; byte[] _cachedFilteredRadarMap;
 long _radarOutputGeneration; long _uploadedRadarGeneration = -1;
 Texture2D _uploadedRadarTexture; int _radarUploadsSkipped, _radarUploads;
 double _radarSubmitMilliseconds;
 static double ElapsedMilliseconds(long started) { return 0; }
 ${methods}
 ${resetPreference}
 static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
 static void Main() {
  var p = _instance = new SCDEFogOfWarPlugin();
  Check(PreferFullMapRadarOnReset(false), "fog forces full map even before map initialization");
  p._enabled = false;
  Check(!PreferFullMapRadarOnReset(false) && PreferFullMapRadarOnReset(true), "disabled preserves setting");
  _instance = null;
  Check(!PreferFullMapRadarOnReset(false) && PreferFullMapRadarOnReset(true), "absent preserves setting");
  _instance = p; p._enabled = true;
  var map = p._activeMap = new GameMap();
  var buffer = p._cachedFilteredRadarMap = new byte[4];
  p._radarOutputGeneration = 1;
  byte[] pending = buffer; RadarSubmitState state;
  Check(BeginRadarSubmit(map, ref pending, out state), "first upload");
  // A failed original never reaches Postfix and must remain retryable.
  Check(BeginRadarSubmit(map, ref pending, out state), "retry before success");
  EndRadarSubmit(map, state);
  Check(!BeginRadarSubmit(map, ref pending, out state) && pending == null, "consume repeat");
  EndRadarSubmit(map, state);
  Check(p._radarUploads == 1 && p._radarUploadsSkipped == 1, "postfix on skip");
  pending = buffer; p._radarOutputGeneration++;
  Check(BeginRadarSubmit(map, ref pending, out state), "reused buffer new generation");
  EndRadarSubmit(map, state);
  map.texture = new Texture2D(); pending = buffer;
  Check(BeginRadarSubmit(map, ref pending, out state), "new texture");
  EndRadarSubmit(map, state);
  pending = new byte[4];
  Check(BeginRadarSubmit(map, ref pending, out state) && pending != null && state.Owner == null, "foreign buffer unchanged");
  pending = null;
  Check(BeginRadarSubmit(map, ref pending, out state) && state.Owner == null, "empty unchanged");
  pending = buffer; p._enabled = false;
  Check(BeginRadarSubmit(map, ref pending, out state), "disabled unchanged");
  p._enabled = true;
  Check(BeginRadarSubmit(new GameMap(), ref pending, out state), "other map unchanged");
  p._uploadedRadarGeneration = -1; p._uploadedRadarTexture = null;
  Check(BeginRadarSubmit(map, ref pending, out state), "reset reuploads");
  p._activeMap = new GameMap(); EndRadarSubmit(map, state);
  Check(p._uploadedRadarGeneration == -1, "old completion ignored");
  Console.WriteLine("RADAR_SUBMIT_CONTRACT_OK");
 }
}`;
    fs.writeFileSync(path.join(dir, 'Test.cs'), fixture);
    execFileSync('C:/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe',
      ['/nologo', '/target:exe', `/out:${path.join(dir, 'Test.exe')}`, path.join(dir, 'Test.cs')]);
    assert.match(execFileSync(path.join(dir, 'Test.exe'), { encoding: 'utf8' }), /RADAR_SUBMIT_CONTRACT_OK/);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
