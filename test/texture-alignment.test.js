const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

test('unit markers reuse authoritative ownership, radar projection and the throttled output', () => {
  const source = fs.readFileSync(path.join(__dirname, '../src/SCDEFogOfWarPlugin.cs'), 'utf8');
  const paint = source.slice(source.indexOf('private void PaintRadarUnitMarkers('), source.indexOf('internal struct RadarSubmitState'));
  assert.match(paint, /foreach \(NativeVisionUnit unit in _nativeVisionUnits\)/);
  assert.match(paint, /unit.Owner < 1 \|\| unit.Owner > 8/);
  assert.match(paint, /IsVisionPlayer\(unit.Owner\)/);
  assert.match(paint, /_visible\[fogIndex\] >= VisibleThreshold/);
  assert.doesNotMatch(paint, /TryReadActive|GetDictionary|\.color/);
  const filter = source.slice(source.indexOf('private void FilterRadarMap('), source.indexOf('private void PaintRadarUnitMarkers('));
  assert.ok(filter.indexOf('Time.unscaledTime < _nextRadarFogUpdate') < filter.indexOf('PaintRadarUnitMarkers'));
  assert.match(filter, /PaintRadarUnitMarkers\(filtered, width, height\);\s+radarMap = filtered/);
  assert.match(source, /P00X = radarP00.x, P00Y = radarP00.y/);
  assert.match(source, /MinX = radarMinX, MinY = radarMinY/);
  const reset = source.slice(source.indexOf('private void ResetMapState()'), source.indexOf('private void DestroyFogObjects()'));
  assert.match(reset, /_radarUnitMarkers.Clear\(\)/);
});

test('resource census is one-shot per initialized map and excludes its FPS window', () => {
  const source = fs.readFileSync(path.join(__dirname, '../src/SCDEFogOfWarPlugin.cs'), 'utf8');
  assert.match(source, /_resourceAuditAt = Time\.unscaledTime \+ 20f/);
  assert.match(source, /_resourceAuditAt = 0f;\s+LogResourceAudit\(\);/);
  const audit = source.slice(source.indexOf('private void LogFrameAudit()'), source.indexOf('private void LogResourceAudit()'));
  assert.ok(audit.indexOf('_frameAuditStarted = 0;') < audit.indexOf('long now ='));
  const reset = source.slice(source.indexOf('private void ResetMapState()'), source.indexOf('private void DestroyFogObjects()'));
  assert.match(reset, /_resourceAuditAt = 0f;/);
  assert.match(reset, /Array.Clear\(_scopeSamples/);
});

test('fog quad endpoints sample texel centres at every map size', () => {
  const source = fs.readFileSync(path.join(__dirname, '../src/SCDEFogOfWarPlugin.cs'), 'utf8');
  assert.match(source, /float uvMin = 0\.5f \/ _resolution/);
  assert.match(source, /float uvMax = 1f - uvMin/);
  assert.match(source, /new Vector2\(uvMin, uvMin\), new Vector2\(uvMax, uvMin\)/);
  assert.match(source, /new Vector2\(uvMin, uvMax\), new Vector2\(uvMax, uvMax\)/);
  for (const size of [296, 300, 400, 800]) {
    for (const resolution of [256, 512]) {
      for (const local of [0, 1, size / 4, size / 2, size - 2, size - 1]) {
        const t = local / (size - 1);
        const uv = 0.5 / resolution + t * (1 - 1 / resolution);
        const sampledTexel = uv * resolution - 0.5;
        const expected = local * (resolution - 1) / (size - 1);
        assert.ok(Math.abs(sampledTexel - expected) < 1e-10);
      }
      // Old UV=0 at the first map point addressed the edge, half a texel off.
      assert.equal(0 * resolution - 0.5, -0.5);
    }
  }
});
