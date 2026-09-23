const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { execFileSync } = require('node:child_process');

const root = path.resolve(__dirname, '..');
const compiler = path.join(process.env.WINDIR || 'C:/Windows', 'Microsoft.NET/Framework64/v4.0.30319/csc.exe');
function run(sources, fixture) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'fog-native-vision-'));
  try {
    if (fixture) {
      fs.writeFileSync(path.join(dir, 'Fixture.cs'), fixture);
      sources = [...sources, path.join(dir, 'Fixture.cs')];
    }
    const exe = path.join(dir, 'Test.exe');
    execFileSync(compiler, ['/nologo', '/optimize+', '/target:exe', `/out:${exe}`, ...sources]);
    return execFileSync(exe, { encoding: 'utf8' });
  } finally { fs.rmSync(dir, { recursive: true, force: true }); }
}
const sourceFile = name => path.join(root, 'src', name);

test('authoritative unit owner ignores the former unrelated field', () => {
  assert.match(run(['NativeUnitVisionReader.cs', 'NativeBuildingVisionReader.cs', 'NativeFogCoordinates.cs'].map(sourceFile)
    .concat(path.join(__dirname, 'NativeUnitOwnerTests.cs'))), /NATIVE_UNIT_OWNER_OK/);
});

test('bounded native building table discovers late spawns and removes stale records', () => {
  const result = run([sourceFile('NativeBuildingVisionReader.cs'), path.join(__dirname, 'NativeBuildingVisionTests.cs')]);
  assert.match(result, /NATIVE_BUILDING_VISION_OK/);
  console.log(result.trim());
});

test('actual reconciliation filters teams and walls, replaces sources, and remaps rotation', () => {
  const source = fs.readFileSync(sourceFile('SCDEFogOfWarPlugin.cs'), 'utf8');
  const section = (a, b) => source.slice(source.indexOf(a), source.indexOf(b));
  const refresh = section('        private void RefreshStructureSources()', '        private void ReconcileNativeBuildings()');
  assert.match(refresh, /_nextStructureScan = Time\.unscaledTime \+ 0\.75f;/);
  assert.ok(refresh.indexOf('TryReadBuildings') < refresh.indexOf('EngineInterface.FlattenedLandscape'));
  assert.match(refresh, /ReconcileNativeBuildings\(\);\s+return;/);
  const reset = section('        private void ResetMapState()', '        private void DestroyFogObjects()');
  assert.match(reset, /_nativeVisionBuildings.Clear\(\)/);
  const fixture = `using System;
using System.Collections.Generic;
using SCDEFogOfWar;
struct Vector2Int { internal int x,y; internal Vector2Int(int x,int y) {this.x=x;this.y=y;} }
class GameMap { internal const int RAW_MAP_SIZE=800; internal bool rotated;
 internal void mapGameTileToTilemapCoord(int x,int y,out int tx,out int ty) {tx=rotated?799-y:x;ty=rotated?x:y;} }
class Fixture {
 const int StructureSourceSpacing=6;
 readonly List<NativeVisionBuilding> _nativeVisionBuildings=new List<NativeVisionBuilding>();
 readonly HashSet<int> _friendlyStructureIds=new HashSet<int>();
 readonly Dictionary<int,int> _friendlyStructureOwners=new Dictionary<int,int>(), _friendlyTowerTypes=new Dictionary<int,int>();
 readonly Dictionary<int,Vector2Int> _friendlyStructurePositions=new Dictionary<int,Vector2Int>();
 readonly List<Vector2Int> _structureSources=new List<Vector2Int>();
 readonly List<TowerVisionSource> _towerSources=new List<TowerVisionSource>();
 readonly HashSet<int> _occupiedFriendlyTowerIds=new HashSet<int>();
 readonly List<int> _pendingPlacements=new List<int>();
 readonly HashSet<int> teams=new HashSet<int>{1,2};
 readonly GameMap _activeMap=new GameMap(); readonly byte[,] _activeTiles=new byte[800,800];
 float _nextTowerOccupancyScan;
 struct TowerVisionSource {internal int StructureId,Type,TileX,TileY;}
 bool IsVisionPlayer(int id){return teams.Contains(id);}
 bool IsLogicCoordinateOnMap(int x,int y){return x>=0&&x<800&&y>=0&&y<800;}
 ${section('        private static bool IsTowerType(', '        private static bool IsStructureTileCandidate(')}
 ${section('        private void RememberFriendlyStructure(', '        private void RemoveFriendlyStructure(')}
 ${section('        private void AddStructureSource(', '        private void ClaimPendingPlacements(')}
 ${section('        private void ReconcileNativeBuildings()', '        private void SeedFriendlyStructuresNearBases(')}
 static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);}
 void Put(int id,int owner,int type,int x,int y) {_nativeVisionBuildings.Add(new NativeVisionBuilding{Id=id,Owner=owner,Type=type,LogicX=x,LogicY=y});}
 void Run(){
  Put(1,1,3,400,400); Put(50,2,3,700,710); Put(2,3,3,300,300);
  Put(3,1,90,400,410); Put(4,1,110,400,410); Put(5,1,79,400,410);
  for(int t=74;t<=78;t++)Put(t,2,t,100+t,200);
  ReconcileNativeBuildings();
  Check(_structureSources.Count==2&&_towerSources.Count==5,"team/type filtering");
  Check(!_friendlyStructureIds.Contains(2)&&!_friendlyStructureIds.Contains(3),"enemy/wall leakage");
  Put(100,1,3,600,610); ReconcileNativeBuildings();
  Check(_friendlyStructureIds.Contains(100)&&_structureSources.Count==3,"late remote prefab missed");
  _nativeVisionBuildings.RemoveAll(b=>b.Id==50||b.Id==100); Put(100,3,3,600,610);
  _occupiedFriendlyTowerIds.Add(74); ReconcileNativeBuildings();
  Check(!_friendlyStructureIds.Contains(50)&&!_friendlyStructureIds.Contains(100),"stale deletion/reused enemy slot");
  Check(_occupiedFriendlyTowerIds.Count==0&&_nextTowerOccupancyScan==0,"stale garrison cache");
  _activeMap.rotated=true; ReconcileNativeBuildings();
  Check(_friendlyStructurePositions[74].x==599&&_friendlyStructurePositions[74].y==174,"rotation mapping");
  teams.Remove(2); ReconcileNativeBuildings(); Check(_towerSources.Count==0,"alliance change");
  _nativeVisionBuildings.Clear(); ReconcileNativeBuildings();
  Check(_friendlyStructureIds.Count==0&&_structureSources.Count==0&&_towerSources.Count==0,"empty/new map retains sources");
  Console.WriteLine("BUILDING_RECONCILIATION_OK");
 }
 static void Main(){try{new Fixture().Run();}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}}
}`;
  assert.match(run([sourceFile('NativeBuildingVisionReader.cs')], fixture), /BUILDING_RECONCILIATION_OK/);
});

test('native building reconciliation waits five seconds between reads', () => {
  const source = fs.readFileSync(sourceFile('SCDEFogOfWarPlugin.cs'), 'utf8');
  const refresh = source.slice(source.indexOf('        private void RefreshStructureSources()'),
    source.indexOf('        private void ReconcileNativeBuildings()'));
  const fixture = `using System;
using System.Collections.Generic;
static class Time { internal static float unscaledTime; }
static class EngineInterface { internal static bool FlattenedLandscape; }
static class Logger { internal static void LogInfo(string s) {} internal static void LogWarning(string s) {} }
class Reader {
 internal int calls; internal bool available = true; internal string FailureReason = "fixture";
 internal bool TryReadBuildings(List<int> output) {calls++;return available;}
}
class Fixture {
 float _nextStructureScan; bool _loggedNativeBuildingFallback, _loggedFirstRefresh=true;
 int _activePlayer=1, reconciliations, fallbacks;
 Reader _nativeUnitVisionReader=new Reader();
 List<int> _nativeVisionBuildings=new List<int>(), _structureSources=new List<int>(),
  _towerSources=new List<int>(), _visionPlayerIds=new List<int>(), _friendlyStructureIds=new List<int>();
 void ReconcileNativeBuildings(){reconciliations++;}
 void ClaimPendingPlacements(){fallbacks++;}
 void SeedFriendlyStructuresNearBases(ref int a, ref int b){}
 void RebuildTrackedStructureSources(){}
 ${refresh}
 static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);}
 void Run(){
  Time.unscaledTime=0; RefreshStructureSources();
  Check(_nativeUnitVisionReader.calls==1&&reconciliations==1,"first map scan must be immediate");
  foreach(float t in new float[]{0.12f,0.75f,1f,4.99f}){Time.unscaledTime=t;RefreshStructureSources();}
  Check(_nativeUnitVisionReader.calls==1,"native table re-read before five seconds");
  EngineInterface.FlattenedLandscape=true; Time.unscaledTime=5f; RefreshStructureSources();
  Check(_nativeUnitVisionReader.calls==2&&reconciliations==2&&fallbacks==0,"five-second/flattened scan missing");
  Time.unscaledTime=9.99f; RefreshStructureSources(); Check(_nativeUnitVisionReader.calls==2,"early repeat");
  Time.unscaledTime=10f; RefreshStructureSources(); Check(_nativeUnitVisionReader.calls==3,"second interval");
  _nextStructureScan=0; Time.unscaledTime=10.1f; RefreshStructureSources();
  Check(_nativeUnitVisionReader.calls==4,"explicit map/rotation reset must refresh immediately");
  _nativeUnitVisionReader.available=false; EngineInterface.FlattenedLandscape=false;
  _nextStructureScan=0; Time.unscaledTime=20f; RefreshStructureSources();
  Time.unscaledTime=20.74f; RefreshStructureSources(); Check(fallbacks==1,"early fallback");
  Time.unscaledTime=20.75f; RefreshStructureSources(); Check(fallbacks==2,"legacy fallback cadence changed");
  Console.WriteLine("BUILDING_FIVE_SECOND_CADENCE_OK");
 }
 static void Main(){try{new Fixture().Run();}catch(Exception e){Console.Error.WriteLine(e.Message);Environment.ExitCode=1;}}
}`;
  assert.match(run([], fixture), /BUILDING_FIVE_SECOND_CADENCE_OK/);
});
