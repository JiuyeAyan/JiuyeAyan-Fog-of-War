using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace SCDEFogOfWar
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class SCDEFogOfWarPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "scde.sc2-fog-of-war";
        public const string PluginName = "JiuyeAyan's Fog of War";
        public const string PluginVersion = "0.2.55";

        private const string DefaultUnexplored = "#050810F2";
        private const string DefaultExplored = "#17202B9E";
        private const string PackedConfigResource = "SCDEFogOfWar.PackedSettings.toml";

        private static SCDEFogOfWarPlugin _instance;
        private static readonly FieldInfo ChimpsField = AccessTools.Field(typeof(GameMap), "chimps");
        private static readonly FieldInfo BuildingsField = AccessTools.Field(typeof(GameMap), "buildingAnims");
        private static readonly FieldInfo FliesField = AccessTools.Field(typeof(GameMap), "flies");
        private static readonly FieldInfo OrgsField = AccessTools.Field(typeof(GameMap), "orgs");
        private static readonly FieldInfo PixiesField = AccessTools.Field(typeof(GameMap), "pixies");
        private static readonly FieldInfo TilesField = AccessTools.Field(typeof(GameMap), "gameMap");
        private static readonly FieldInfo RadarTextureSizeField =
            AccessTools.Field(typeof(GameMap), "RADAR_TEXTURE_SIZE");
        private static readonly MethodInfo SetRadarTexturePixelSizeMethod =
            AccessTools.Method(typeof(GameMap), "SetRadarTexturePixelSize");
        private static readonly FieldInfo RenderBoundsLeftField =
            AccessTools.Field(typeof(GameMap), "cachedRenderBoundsLeft");
        private static readonly FieldInfo RenderBoundsRightField =
            AccessTools.Field(typeof(GameMap), "cachedRenderBoundsRight");
        private static readonly FieldInfo RenderBoundsTopField =
            AccessTools.Field(typeof(GameMap), "cachedRenderBoundsTop");
        private static readonly FieldInfo RenderBoundsBottomField =
            AccessTools.Field(typeof(GameMap), "cachedRenderBoundsBottom");
        private static readonly FieldInfo KeepLocationsField =
            AccessTools.Field(typeof(GameData), "Keep_Locations");
        private static readonly FieldInfo[] RenderBoundsFields = {
            RenderBoundsLeftField, RenderBoundsRightField,
            RenderBoundsTopField, RenderBoundsBottomField };
        private static readonly FieldInfo CurrentRotationField =
            AccessTools.Field(typeof(GameMap), "currentRotation");
        private static readonly FieldInfo PendingRotationField =
            AccessTools.Field(typeof(GameMap), "pendingRotation");
        private const int StructureSourceSpacing = 6;
        private const int FriendlyBaseSeedRadius = 28;
        private const int PlacementClaimRadius = 10;
        private const int MapUpdateWidth = 21;
        private const int RotationCount = 4;
        private const int UnitIdentitySearchRadius = 4;
        private const float UnitVisionGraceSeconds = 1.5f;
        private const float InitialMapSettleSeconds = 5f;
        private const float VisibleThreshold = 0.35f;
        private const float RendererCachePruneInterval = 5f;
        private const float PerformanceLogInterval = 10f;
        // Assembly-CSharp internal eChimps enum values (Steam build 24816905).
        private const int ChimpTypeWoodcutter = 3;
        private const int ChimpTypeHunter = 6;
        private const int ChimpTypeArcher = 22;
        private const int ChimpTypeEngineer = 30;
        private const int ChimpTypeMonk = 37;
        private const int ChimpTypeArcherDebug = 38;
        private const int ChimpTypeMangonel = 41;
        private const int ChimpTypeSiegeTent = 50;
        private const int ChimpTypeLord = 55;
        private const int ChimpTypeSiegeTower = 58;
        private const int ChimpTypeBallista = 61;
        private const int ChimpTypeChicken = 62;
        private const int ChimpTypeWarDog = 67;
        private const int ChimpTypeArabBow = 70;
        private const int ChimpTypeBedouinDemolisher = 85;

        private bool _enabled = true;
        private float _unitSightRadius = 13f;
        private float _civilianSightRadius = 10f;
        private float _buildingSightRadius = 16f;
        private float _wallSightRadius = 6f;
        private float _initialLordBaseSightRadius = 140f;
        private readonly float[] _towerSightRadii = { 16f, 16f, 16f, 16f, 16f };
        private float _softEdgeWidth = 4f;
        private float _updateInterval = 0.12f;
        private float _radarUpdatesPerSecond = 3.33f;
        private Color _unexploredColor;
        private Color _exploredColor;
        private bool _loggedFirstRefresh;
        private bool _loggedSourceDiagnostics;
        private bool _initialMapSnapshotAccepted;
        private float _tileFilterReadyAt;
        private bool _localAnchorResolved;
        private bool _loggedRadarFilter;
        private bool _loggedFullMapRadar;
        private bool _loggedTileFilter;
        private bool _loggedSelectionFilter;
        private bool _loggedDirectorLoop;
        private bool _loggedUpdateLoop;
        private int _lastDriveFrame = -1;
        private int _signaledMapSize;
        private float _nextHookErrorLog;
        private float _nextMapWaitLog;
        private float _nextRendererCachePrune;
        private float _nextPerformanceLog;
        private string _mapWaitReason;
        private Harmony _harmony;
        private FogOfWarRuntime _runtime;
        private bool _applicationQuitting;
        private double _profileFogMilliseconds;
        private double _profileFogMaxMilliseconds;
        private double _profileBuildingMilliseconds;
        private double _profileUnitMilliseconds;
        private double _profileComposeMilliseconds;
        private double _profileUploadMilliseconds;
        private double _profileVisibilityMilliseconds;
        private double _profileRadarMilliseconds;
        private double _profileTileFilterMilliseconds;
        private double _profileTileFilterMaxMilliseconds;
        private int _profileFogSamples;
        private int _profileVisibilitySamples;
        private int _profileRadarSamples;
        private int _profileTileFilterSamples;
        private readonly double[] _scopeMilliseconds = new double[3];
        private readonly int[] _scopeSamples = new int[3];
        private float _resourceAuditAt;

        internal static void RecordScope(int scope, long started)
        {
            if (ReferenceEquals(_instance, null) || _instance._activeMap == null) return;
            _instance._scopeMilliseconds[scope] += ElapsedMilliseconds(started);
            _instance._scopeSamples[scope]++;
        }

        private GameMap _activeMap;
        private long _frameAuditStarted;
        private int _frameAuditFirstFrame;
        private int _frameAuditGcCount;
        private int _matchAuditId;
        private bool _waitingForNextMatch;
        private GameMap _expandedBoundsMap;
        private readonly int[] _nativeBounds = new int[4];
        private readonly int[] _expandedBounds = new int[4];
        private GameMapTile[,] _activeTiles;
        private int _activePlayer = -1;
        private int _mapSize;
        private int _mapOffset;
        private int _resolution;
        private int _activeRotationIndex;
        private int _coordinateAuditRotation = -1;
        private float _nextFogUpdate;
        private float _nextNoSourceWarning;
        private float _nextStructureScan;
        private Vector2Int _localAnchor;
        private float[] _visible;
        private float[] _permanentInitialLordVision;
        private bool _permanentInitialLordVisionDirty = true;
        private byte[] _explored;
        private Color32[] _pixels;
        private Color32[] _fogColorLookup;
        private Color32 _opaqueUnexploredPixel;
        private readonly byte[][] _radarUnexploredBlend =
            { new byte[256], new byte[256], new byte[256] };
        private readonly byte[][] _radarExploredBlend =
            { new byte[256], new byte[256], new byte[256] };
        private int[] _radarFogIndices;
        private int _radarMappingWidth = -1;
        private int _radarMappingHeight = -1;
        private int _radarMappingRotation = -1;
        private RadarMarkerProjection _radarMarkerProjection;
        private readonly RadarUnitMarkers _radarUnitMarkers = new RadarUnitMarkers();
        private readonly HashSet<int> _radarSelectedUnitIds = new HashSet<int>();
        private readonly byte[][] _radarFilterBuffers = new byte[2][];
        private int _nextRadarFilterBuffer;
        private byte[] _radarSourceMap;
        private byte[] _cachedFilteredRadarMap;
        private long _radarOutputGeneration;
        private long _uploadedRadarGeneration = -1;
        private Texture2D _uploadedRadarTexture;
        private int _radarUploads;
        private int _radarUploadsSkipped;
        private double _radarSubmitMilliseconds;
        private int _cachedRadarWidth = -1;
        private int _cachedRadarHeight = -1;
        private int _cachedRadarRotation = -1;
        private float _nextRadarFogUpdate;
        private short[][] _displayedTileUpdatesByRotation;
        private byte[][] _displayedTileUpdateValidByRotation;
        private byte[][] _observedFogPixelsByRotation;
        private int _visibilityGeneration;
        private readonly int[] _lastRevealVisibilityGenerationByRotation =
            { -1, -1, -1, -1 };
        private readonly List<long> _revealedHiddenTileKeys = new List<long>();
        private Texture2D _fogTexture;
        private GameObject _fogObject;
        private Mesh _fogMesh;
        private Material _fogMaterial;
        private MeshRenderer _fogRenderer;

        private readonly List<Vector2Int> _structureSources = new List<Vector2Int>();
        private readonly List<TowerVisionSource> _towerSources = new List<TowerVisionSource>();
        private readonly HashSet<int> _visionPlayerIds = new HashSet<int>();
        private readonly HashSet<int> _friendlyUnitIds = new HashSet<int>();
        private readonly HashSet<long> _unitVisionStampKeys = new HashSet<long>();
        private readonly Dictionary<int, Vector2> _initialLordLogicByPlayer =
            new Dictionary<int, Vector2>();
        private readonly List<Vector2> _activeInitialLordVisionSources =
            new List<Vector2>();
        private readonly List<int> _targetableUnitBuffer = new List<int>();
        private volatile int[] _targetableUnitIdsSnapshot;
        private readonly Dictionary<int, UnitOwnerCacheEntry> _unitOwnerCache =
            new Dictionary<int, UnitOwnerCacheEntry>();
        private readonly Dictionary<int, UnitVisionSource> _friendlyUnitSources =
            new Dictionary<int, UnitVisionSource>();
        private readonly NativeUnitVisionReader _nativeUnitVisionReader =
            new NativeUnitVisionReader();
        private readonly List<NativeVisionUnit> _nativeVisionUnits =
            new List<NativeVisionUnit>();
        private bool _loggedNativeUnitVision;
        private bool _loggedNativeUnitVisionFallback;
        private readonly List<NativeVisionBuilding> _nativeVisionBuildings =
            new List<NativeVisionBuilding>();
        private bool _loggedNativeBuildingFallback;
        private readonly HashSet<int> _friendlyStructureIds = new HashSet<int>();
        private readonly Dictionary<int, int> _friendlyStructureOwners =
            new Dictionary<int, int>();
        private readonly Dictionary<int, Vector2Int> _friendlyStructurePositions =
            new Dictionary<int, Vector2Int>();
        private readonly Dictionary<int, int> _friendlyTowerTypes =
            new Dictionary<int, int>();
        private readonly HashSet<int> _occupiedFriendlyTowerIds = new HashSet<int>();
        private float _nextTowerOccupancyScan;
        private readonly HashSet<int> _seededBasePlayers = new HashSet<int>();
        private readonly List<PendingPlacement> _pendingPlacements = new List<PendingPlacement>();
        private readonly Dictionary<long, short[]>[] _hiddenTileUpdatesByRotation =
        {
            new Dictionary<long, short[]>(),
            new Dictionary<long, short[]>(),
            new Dictionary<long, short[]>(),
            new Dictionary<long, short[]>()
        };
        private readonly Dictionary<GameSprite, Renderer[]> _rendererCache =
            new Dictionary<GameSprite, Renderer[]>();
        private readonly Dictionary<GameSprite, bool> _spriteHiddenState =
            new Dictionary<GameSprite, bool>();
        private readonly HashSet<Renderer> _forcedOff = new HashSet<Renderer>();
        private readonly Dictionary<long, SightKernelSample[]> _sightKernelCache =
            new Dictionary<long, SightKernelSample[]>();

        private struct PendingPlacement
        {
            internal int PlayerId;
            internal int LogicX;
            internal int LogicY;
            internal HashSet<int> ExistingStructureIds;
            internal float ExpiresAt;
        }

        private struct UnitVisionSource
        {
            internal int Owner;
            internal int TileX;
            internal int TileY;
            internal int UnitType;
            internal float LastConfirmed;
        }

        private struct UnitOwnerCacheEntry
        {
            internal Chimp Unit;
            internal int Owner;
        }

        private struct TowerVisionSource
        {
            internal int StructureId;
            internal int Type;
            internal int TileX;
            internal int TileY;
        }

        private struct SightKernelSample
        {
            internal short DeltaX;
            internal short DeltaY;
            internal float Sight;
        }

        private void Awake()
        {
            _instance = this;
            LoadPackedSettings();
            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(SCDEFogOfWarPlugin).Assembly);
            CreateDetachedRuntime();
            Application.quitting += OnApplicationQuitting;

            bool mapHookInstalled = HasHarmonyHook(AccessTools.Method(typeof(GameMap), "processTestMap"));
            bool directorHookInstalled = HasHarmonyHook(AccessTools.Method(typeof(Director), "Update"));
            Logger.LogInfo(string.Format(
                "Fog loaded with embedded build settings; processTestMap hook={0}; Director.Update hook={1}; persistent runtime=True.",
                mapHookInstalled, directorHookInstalled));
        }

        private void CreateDetachedRuntime()
        {
            GameObject runtimeObject = new GameObject("SC2 Fog of War Runtime");
            runtimeObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(runtimeObject);
            _runtime = runtimeObject.AddComponent<FogOfWarRuntime>();
            _runtime.Owner = this;
        }

        private static bool HasHarmonyHook(MethodInfo method)
        {
            Patches patchInfo = method == null ? null : Harmony.GetPatchInfo(method);
            if (patchInfo != null)
            {
                foreach (string owner in patchInfo.Owners)
                {
                    if (owner == PluginGuid)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void Update()
        {
            RuntimeUpdate();
        }

        private void OnDestroy()
        {
            if (_runtime != null)
            {
                Logger.LogWarning(
                    "BepInEx plugin object was destroyed; detached fog runtime will remain active.");
            }
        }

        internal void RuntimeUpdate()
        {
            if (!_loggedUpdateLoop)
            {
                _loggedUpdateLoop = true;
                Logger.LogInfo(
                    "Detached fog runtime update loop active across game scenes.");
            }
            DriveFromMap(GameMap.instance);
        }

        private void OnApplicationQuitting()
        {
            _applicationQuitting = true;
            Logger.LogInfo("Application quitting observed by the detached fog runtime.");
        }

        internal void RuntimeDestroyed(FogOfWarRuntime runtime)
        {
            if (!ReferenceEquals(_runtime, runtime))
            {
                return;
            }
            _runtime.Owner = null;
            _runtime = null;
            Application.quitting -= OnApplicationQuitting;
            if (!_applicationQuitting)
            {
                ResetMapState();
            }
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
            }
        }

        private void DriveFromMap(GameMap map)
        {
            int frame = Time.frameCount;
            if (_lastDriveFrame == frame)
            {
                return;
            }
            _lastDriveFrame = frame;
            long driveStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (!_enabled)
                {
                    if (_activeMap != null)
                    {
                        ResetMapState();
                    }
                    return;
                }

                GameMapTile[,] tiles = map == null || TilesField == null
                    ? null
                    : TilesField.GetValue(map) as GameMapTile[,];
                string waitReason = GetMapWaitReason(map, tiles);
                if (waitReason != null)
                {
                    if (_activeMap != null)
                    {
                        ResetMapState();
                    }
                    LogMapWait(waitReason);
                    return;
                }

                if (_mapWaitReason != null)
                {
                    Logger.LogInfo("Map data ready; fog initialization can begin.");
                    _mapWaitReason = null;
                }

                int player = EditorDirector.instance.ActivePlayerID;
                if (_activeMap != map || _activeTiles != tiles ||
                    _activePlayer != player || _mapSize != GameMap.tilemapSize)
                {
                    InitializeMap(map, tiles, player);
                }

                EnsureFullMapRadar();

                if (_activeMap != null && Time.unscaledTime >= _nextFogUpdate)
                {
                    _nextFogUpdate = Time.unscaledTime + _updateInterval;
                    long fogStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    RefreshFog();
                    double fogMilliseconds = ElapsedMilliseconds(fogStarted);
                    _profileFogMilliseconds += fogMilliseconds;
                    _profileFogMaxMilliseconds = Math.Max(
                        _profileFogMaxMilliseconds, fogMilliseconds);
                    _profileFogSamples++;
                }
                if (_activeMap != null && _visible != null)
                {
                    long visibilityStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                    ApplyDynamicVisibility();
                    _profileVisibilityMilliseconds += ElapsedMilliseconds(visibilityStarted);
                    _profileVisibilitySamples++;
                    LogPerformanceProfile();
                }
            }
            catch (Exception exception)
            {
                if (Time.unscaledTime >= _nextHookErrorLog)
                {
                    _nextHookErrorLog = Time.unscaledTime + 10f;
                    Logger.LogError("Fog map hook failed: " + exception);
                }
            }
            finally
            {
                RecordScope(0, driveStarted);
            }
        }

        private string GetMapWaitReason(GameMap map, GameMapTile[,] tiles)
        {
            if (_waitingForNextMatch) return "waiting for next match load";
            if (map == null) return "GameMap.instance is null";
            if (GameMap.tilemapSize <= 0) return "tilemapSize is not ready";
            if (GameMap.tilemapSize == GameMap.RAW_MAP_SIZE &&
                _signaledMapSize != GameMap.RAW_MAP_SIZE)
                return "raw 800x800 backing map has not received a playable-map signal";
            if (_signaledMapSize > 0 && GameMap.tilemapSize != _signaledMapSize)
                return "tilemapSize does not match the latest playable-map signal";
            if (TilemapManager.instance == null) return "TilemapManager.instance is null";
            if (TilemapManager.instance.gameTileMap == null) return "gameTileMap is null";
            if (EditorDirector.instance == null) return "EditorDirector.instance is null";
            if (TilesField == null) return "GameMap.gameMap field was not found";
            if (tiles == null) return "GameMap.gameMap data is null";
            return null;
        }

        private static double ElapsedMilliseconds(long started)
        {
            return (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 /
                System.Diagnostics.Stopwatch.Frequency;
        }

        private void LogPerformanceProfile()
        {
            if (Time.unscaledTime < _nextPerformanceLog)
            {
                return;
            }
            _nextPerformanceLog = Time.unscaledTime + PerformanceLogInterval;
            LogFrameAudit();
            Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Fog performance: refresh={0:0.00}ms avg/{1:0.00}ms max/{2}, buildings={3:0.00}ms, units={4:0.00}ms, compose={5:0.00}ms, upload={6:0.00}ms, visibility={7:0.00}ms/{8}, radar={9:0.00}ms/{10}, tileFilter={11:0.00}ms avg/{12:0.00}ms max/{13}; map={14}, radarSize={15}x{16}, rendererCache={17}, ownerCache={18}, sightKernels={19}, forcedOff={20}, deferredTiles={21}/{22}/{23}/{24}.",
                AverageMilliseconds(_profileFogMilliseconds, _profileFogSamples),
                _profileFogMaxMilliseconds,
                _profileFogSamples,
                AverageMilliseconds(_profileBuildingMilliseconds, _profileFogSamples),
                AverageMilliseconds(_profileUnitMilliseconds, _profileFogSamples),
                AverageMilliseconds(_profileComposeMilliseconds, _profileFogSamples),
                AverageMilliseconds(_profileUploadMilliseconds, _profileFogSamples),
                AverageMilliseconds(_profileVisibilityMilliseconds, _profileVisibilitySamples),
                _profileVisibilitySamples,
                AverageMilliseconds(_profileRadarMilliseconds, _profileRadarSamples),
                _profileRadarSamples,
                AverageMilliseconds(
                    _profileTileFilterMilliseconds, _profileTileFilterSamples),
                _profileTileFilterMaxMilliseconds,
                _profileTileFilterSamples,
                _mapSize,
                _activeMap == null ? 0 : _activeMap.RadarMapWidth,
                _activeMap == null ? 0 : _activeMap.RadarMapHeight,
                _rendererCache.Count,
                _unitOwnerCache.Count,
                _sightKernelCache.Count, _forcedOff.Count,
                _hiddenTileUpdatesByRotation[0].Count,
                _hiddenTileUpdatesByRotation[1].Count,
                _hiddenTileUpdatesByRotation[2].Count,
                _hiddenTileUpdatesByRotation[3].Count));
            _profileFogMilliseconds = 0.0;
            _profileFogMaxMilliseconds = 0.0;
            _profileBuildingMilliseconds = 0.0;
            _profileUnitMilliseconds = 0.0;
            _profileComposeMilliseconds = 0.0;
            _profileUploadMilliseconds = 0.0;
            _profileVisibilityMilliseconds = 0.0;
            _profileRadarMilliseconds = 0.0;
            _profileTileFilterMilliseconds = 0.0;
            _profileTileFilterMaxMilliseconds = 0.0;
            _profileFogSamples = 0;
            _profileVisibilitySamples = 0;
            _profileRadarSamples = 0;
            _profileTileFilterSamples = 0;
        }

        private static double AverageMilliseconds(double total, int samples)
        {
            return samples <= 0 ? 0.0 : total / samples;
        }

        private void LogFrameAudit()
        {
            if (_resourceAuditAt > 0f && Time.unscaledTime >= _resourceAuditAt)
            {
                _resourceAuditAt = 0f;
                LogResourceAudit();
                // The one-off global census can stall a frame. Do not mix it into
                // a normal FPS window, or repeat it on every performance report.
                _frameAuditStarted = 0;
            }
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double seconds = (now - _frameAuditStarted) /
                (double)System.Diagnostics.Stopwatch.Frequency;
            if (_frameAuditStarted != 0 && seconds >= 1.0)
            {
                int frames = Time.frameCount - _frameAuditFirstFrame;
                int active = 0;
                int inactive = 0;
                CountDisplayObjects(GetDictionary<int, Chimp>(ChimpsField), ref active, ref inactive);
                CountDisplayObjects(GetDictionary<int, BuildingAnim>(BuildingsField), ref active, ref inactive);
                CountDisplayObjects(GetDictionary<int, Org>(OrgsField), ref active, ref inactive);
                CountDisplayObjects(GetDictionary<int, Fly>(FliesField), ref active, ref inactive);
                CountDisplayObjects(GetDictionary<int, Pixie>(PixiesField), ref active, ref inactive);
                Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                    "Fog frame audit: match={0}, seconds={1:0.00}, realFps={2:0.0}, frameMs={3:0.00}, nativeFps={4}, activeSprites={5}, inactiveSprites={6}, managedMiB={7:0.0}, gc0={8}, bounds={9}/{10}/{11}/{12}, targetFps={13}, vsync={14}, timeScale={15:0.###}.",
                    _matchAuditId, seconds, frames / seconds,
                    frames > 0 ? seconds * 1000.0 / frames : 0,
                    EditorDirector.instance.CurrentFPS, active, inactive,
                    GC.GetTotalMemory(false) / 1048576.0,
                    GC.CollectionCount(0) - _frameAuditGcCount,
                    RenderBoundsLeftField.GetValue(_activeMap),
                    RenderBoundsRightField.GetValue(_activeMap),
                    RenderBoundsTopField.GetValue(_activeMap),
                    RenderBoundsBottomField.GetValue(_activeMap),
                    Application.targetFrameRate, QualitySettings.vSyncCount, Time.timeScale));
                Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                    "Fog radar submit audit: match={0}, seconds={1:0.00}, uploads={2}, skipped={3}, submitMs={4:0.###}.",
                    _matchAuditId, seconds, _radarUploads, _radarUploadsSkipped,
                    AverageMilliseconds(_radarSubmitMilliseconds, _radarUploads)));
                Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                    "Fog scope audit: match={0}, driveMs={1:0.###}/{2}, directorMs={3:0.###}/{4}, processMapMs={5:0.###}/{6}; nested scopes, do not sum.",
                    _matchAuditId, AverageMilliseconds(_scopeMilliseconds[0], _scopeSamples[0]), _scopeSamples[0],
                    AverageMilliseconds(_scopeMilliseconds[1], _scopeSamples[1]), _scopeSamples[1],
                    AverageMilliseconds(_scopeMilliseconds[2], _scopeSamples[2]), _scopeSamples[2]));
            }
            Array.Clear(_scopeMilliseconds, 0, _scopeMilliseconds.Length);
            Array.Clear(_scopeSamples, 0, _scopeSamples.Length);
            _radarUploads = 0;
            _radarUploadsSkipped = 0;
            _radarSubmitMilliseconds = 0;
            _frameAuditStarted = now;
            _frameAuditFirstFrame = Time.frameCount;
            _frameAuditGcCount = GC.CollectionCount(0);
        }

        private void LogResourceAudit()
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
            int active = 0, off = 0, visible = 0, fog = 0;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.gameObject.activeInHierarchy || !renderer.enabled) continue;
                active++;
                if (renderer.forceRenderingOff) off++;
                if (renderer.isVisible) visible++;
                if (renderer.gameObject.name == "SC2 Fog of War") fog++;
            }
            Texture2D[] textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            int fogTextures = 0, radarSized = 0;
            foreach (Texture2D texture in textures)
            {
                if (texture.name == "SC2 Fog of War Mask") fogTextures++;
                if (texture.width == _activeMap.RadarMapWidth &&
                    texture.height == _activeMap.RadarMapHeight) radarSized++;
            }
            int fogMaterials = 0, fogMeshes = 0;
            foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
                if (material.name == "SC2 Fog of War Material") fogMaterials++;
            foreach (Mesh mesh in Resources.FindObjectsOfTypeAll<Mesh>())
                if (mesh.name == "SC2 Fog of War Map Quad") fogMeshes++;
            Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Fog resource audit: match={0}, renderers={1}, active={2}, forcedOff={3}, cameraVisible={4}, fogRenderers={5}, textures={6}, radarSizedCandidates={7}, fogTextures={8}, fogMaterials={9}, fogMeshes={10}, runners={11}, censusMs={12:0.###}; one-off global census, candidates are not ownership proof.",
                _matchAuditId, renderers.Length, active, off, visible, fog, textures.Length,
                radarSized, fogTextures, fogMaterials, fogMeshes,
                Resources.FindObjectsOfTypeAll<FogOfWarRuntime>().Length, ElapsedMilliseconds(started)));
        }

        private static void CountDisplayObjects<TSprite>(
            Dictionary<int, TSprite> sprites, ref int active, ref int inactive)
            where TSprite : GameSprite
        {
            if (sprites == null) return;
            foreach (TSprite sprite in sprites.Values)
            {
                if (sprite != null && sprite.gameObject != null && sprite.gameObject.activeInHierarchy)
                    active++;
                else inactive++;
            }
        }

        private void LogMapWait(string reason)
        {
            if (_mapWaitReason != reason || Time.unscaledTime >= _nextMapWaitLog)
            {
                _mapWaitReason = reason;
                _nextMapWaitLog = Time.unscaledTime + 15f;
                Logger.LogInfo("Waiting for playable map: " + reason + ".");
            }
        }

        private void InitializeMap(GameMap map, GameMapTile[,] tiles, int player)
        {
            ResetMapState();
            _activeMap = map;
            _resourceAuditAt = Time.unscaledTime + 20f;
            _activeTiles = tiles;
            _activePlayer = player;
            _visionPlayerIds.Add(player);
            _mapSize = Math.Max(1, GameMap.tilemapSize);
            _mapOffset = (GameMap.RAW_MAP_SIZE - _mapSize) / 2;
            _activeRotationIndex = ReadRotationIndex(CurrentRotationField, map, 0);
            _resolution = Mathf.Clamp(_mapSize, 128, 384);
            _visible = new float[_resolution * _resolution];
            _explored = new byte[_visible.Length];
            _pixels = new Color32[_visible.Length];
            BuildFogColorLookup();
            _displayedTileUpdatesByRotation = new short[RotationCount][];
            _displayedTileUpdateValidByRotation = new byte[RotationCount][];
            _observedFogPixelsByRotation = new byte[RotationCount][];

            _fogTexture = new Texture2D(_resolution, _resolution, TextureFormat.RGBA32, false);
            _fogTexture.name = "SC2 Fog of War Mask";
            _fogTexture.filterMode = FilterMode.Bilinear;
            _fogTexture.wrapMode = TextureWrapMode.Clamp;

            _fogObject = new GameObject("SC2 Fog of War");
            _fogObject.hideFlags = HideFlags.HideAndDontSave;
            _fogObject.layer = TilemapManager.instance.gameTileMap.gameObject.layer;
            MeshFilter filter = _fogObject.AddComponent<MeshFilter>();
            _fogRenderer = _fogObject.AddComponent<MeshRenderer>();
            _fogMesh = new Mesh();
            _fogMesh.name = "SC2 Fog of War Map Quad";
            filter.sharedMesh = _fogMesh;

            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }
            if (shader == null)
            {
                Logger.LogError("Sprites/Default shader was not found; fog overlay cannot be created.");
                ResetMapState();
                return;
            }
            _fogMaterial = new Material(shader);
            _fogMaterial.name = "SC2 Fog of War Material";
            _fogMaterial.mainTexture = _fogTexture;
            _fogMaterial.color = Color.white;
            _fogMaterial.renderQueue = 3999;
            _fogRenderer.sharedMaterial = _fogMaterial;
            _fogRenderer.sortingOrder = 32760;
            _fogRenderer.enabled = true;

            TilemapRenderer tileRenderer = TilemapManager.instance.gameTileMap.GetComponent<TilemapRenderer>();
            if (tileRenderer != null)
            {
                _fogRenderer.sortingLayerID = tileRenderer.sortingLayerID;
            }

            UpdateMeshGeometry();
            Logger.LogInfo(string.Format(
                "Fog map initialized: map={0}, player={1}, texture={2}x{2}; colour ownership inference disabled.",
                _mapSize, _activePlayer, _resolution));
        }

        private void RefreshFog()
        {
            EnsurePermanentInitialLordVision();
            if (_permanentInitialLordVision != null &&
                _permanentInitialLordVision.Length == _visible.Length)
            {
                Buffer.BlockCopy(
                    _permanentInitialLordVision, 0, _visible, 0,
                    _visible.Length * sizeof(float));
            }
            else
            {
                Array.Clear(_visible, 0, _visible.Length);
            }
            if (Time.unscaledTime >= _nextRendererCachePrune)
            {
                _nextRendererCachePrune = Time.unscaledTime + RendererCachePruneInterval;
                PruneRendererCache();
            }
            long sectionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            int buildingSources =
                _activeInitialLordVisionSources.Count + AddFriendlyBuildings();
            _profileBuildingMilliseconds += ElapsedMilliseconds(sectionStarted);
            sectionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            int unitSources = AddFriendlyUnits();
            _profileUnitMilliseconds += ElapsedMilliseconds(sectionStarted);
            if (!_loggedSourceDiagnostics && unitSources > 0)
            {
                LogSourceDiagnostics();
            }
            int visiblePixels = 0;
            sectionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            byte[] observedFogPixels = EnsureObservedFogPixels(_activeRotationIndex);
            bool textureChanged = !_loggedFirstRefresh;

            for (int i = 0; i < _visible.Length; i++)
            {
                float current = _visible[i];
                if (current > 0.01f)
                {
                    visiblePixels++;
                    if (observedFogPixels != null)
                    {
                        observedFogPixels[i] = 1;
                    }
                }
                byte currentByte = current >= 1f
                    ? (byte)255
                    : current <= 0f
                        ? (byte)0
                        : (byte)(current * 255f + 0.5f);
                if (currentByte > _explored[i])
                {
                    _explored[i] = currentByte;
                }

                Color32 nextPixel;
                if (current <= 0.01f &&
                    (observedFogPixels == null || observedFogPixels[i] == 0))
                {
                    nextPixel = _opaqueUnexploredPixel;
                }
                else
                {
                    nextPixel = _fogColorLookup[(_explored[i] << 8) | currentByte];
                }
                Color32 previousPixel = _pixels[i];
                textureChanged |= previousPixel.r != nextPixel.r ||
                    previousPixel.g != nextPixel.g || previousPixel.b != nextPixel.b ||
                    previousPixel.a != nextPixel.a;
                _pixels[i] = nextPixel;
            }
            _profileComposeMilliseconds += ElapsedMilliseconds(sectionStarted);
            _visibilityGeneration++;

            sectionStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (textureChanged)
            {
                _fogTexture.SetPixels32(_pixels);
                _fogTexture.Apply(false, false);
            }
            _profileUploadMilliseconds += ElapsedMilliseconds(sectionStarted);

            if (!_loggedFirstRefresh)
            {
                _loggedFirstRefresh = true;
                Logger.LogInfo(string.Format(
                    "Fog first frame: units={0}, buildings={1}, visiblePixels={2}, renderer={3}, shader={4}.",
                    unitSources, buildingSources, visiblePixels,
                    _fogRenderer != null && _fogRenderer.enabled,
                    _fogMaterial == null || _fogMaterial.shader == null ? "none" : _fogMaterial.shader.name));
            }

            if (unitSources + buildingSources == 0 &&
                Time.unscaledTime >= _nextNoSourceWarning)
            {
                Logger.LogWarning("No friendly vision sources were found for player " + _activePlayer + ".");
                _nextNoSourceWarning = Time.unscaledTime + 10f;
            }
        }

        private void BuildFogColorLookup()
        {
            _fogColorLookup = new Color32[256 * 256];
            for (int explored = 0; explored < 256; explored++)
            {
                float history = explored / 255f;
                history = history * history * (3f - 2f * history);
                Color baseFog = Color.Lerp(_unexploredColor, _exploredColor, history);
                for (int visible = 0; visible < 256; visible++)
                {
                    Color fog = baseFog;
                    fog.a *= 1f - visible / 255f;
                    _fogColorLookup[(explored << 8) | visible] = fog;
                }
            }
            Color opaqueUnexplored = _unexploredColor;
            opaqueUnexplored.a = 1f;
            _opaqueUnexploredPixel = opaqueUnexplored;
            BuildRadarBlendLookup(_unexploredColor, _radarUnexploredBlend);
            BuildRadarBlendLookup(_exploredColor, _radarExploredBlend);
        }

        private static void BuildRadarBlendLookup(Color fog, byte[][] lookup)
        {
            float opacity = Mathf.Clamp01(fog.a);
            float[] fogChannels = { fog.b, fog.g, fog.r };
            for (int channel = 0; channel < 3; channel++)
            {
                for (int source = 0; source < 256; source++)
                {
                    lookup[channel][source] = (byte)Mathf.RoundToInt(
                        source * (1f - opacity) +
                        fogChannels[channel] * 255f * opacity);
                }
            }
        }

        private void PruneRendererCache()
        {
            // Secondary body renderers can be replaced after the initial sprite cache.
            _forcedOff.RemoveWhere(delegate(Renderer renderer) { return renderer == null; });
            HashSet<GameSprite> activeSprites = new HashSet<GameSprite>();
            Dictionary<int, Chimp> units = GetDictionary<int, Chimp>(ChimpsField);
            AddActiveSprites(units, activeSprites);
            AddActiveSprites(GetDictionary<int, BuildingAnim>(BuildingsField), activeSprites);
            AddActiveSprites(GetDictionary<int, Fly>(FliesField), activeSprites);
            AddActiveSprites(GetDictionary<int, Org>(OrgsField), activeSprites);
            AddActiveSprites(GetDictionary<int, Pixie>(PixiesField), activeSprites);

            List<GameSprite> expired = new List<GameSprite>();
            foreach (GameSprite sprite in _rendererCache.Keys)
            {
                if (sprite == null || !activeSprites.Contains(sprite))
                {
                    expired.Add(sprite);
                }
            }
            foreach (GameSprite sprite in expired)
            {
                Renderer[] renderers;
                if (_rendererCache.TryGetValue(sprite, out renderers))
                {
                    foreach (Renderer renderer in renderers)
                    {
                        if (renderer != null)
                        {
                            renderer.forceRenderingOff = false;
                        }
                        _forcedOff.Remove(renderer);
                    }
                }
                _rendererCache.Remove(sprite);
                _spriteHiddenState.Remove(sprite);
            }

            if (units != null)
            {
                List<int> expiredOwners = new List<int>();
                foreach (KeyValuePair<int, UnitOwnerCacheEntry> entry in _unitOwnerCache)
                {
                    Chimp unit;
                    if (!units.TryGetValue(entry.Key, out unit) ||
                        !ReferenceEquals(entry.Value.Unit, unit))
                    {
                        expiredOwners.Add(entry.Key);
                    }
                }
                foreach (int objectId in expiredOwners)
                {
                    _unitOwnerCache.Remove(objectId);
                }
            }
        }

        private static void AddActiveSprites<TSprite>(
            Dictionary<int, TSprite> sprites, HashSet<GameSprite> activeSprites)
            where TSprite : GameSprite
        {
            if (sprites == null)
            {
                return;
            }
            foreach (TSprite sprite in sprites.Values)
            {
                if (sprite != null)
                {
                    activeSprites.Add(sprite);
                }
            }
        }

        private void UpdateVisionPlayers(EngineInterface.PlayState state)
        {
            if (state == null || state.teams == null || _activePlayer < 1 ||
                _activePlayer >= state.teams.Length)
            {
                return;
            }

            HashSet<int> players = new HashSet<int> { _activePlayer };
            int activeTeam = state.teams[_activePlayer];
            int lastPlayer = Math.Min(8, state.teams.Length - 1);
            for (int player = 1; player <= lastPlayer; player++)
            {
                if (player == _activePlayer)
                {
                    continue;
                }
                if ((state.is_valid_player(player) || state.is_skirmish_player(player)) &&
                    state.teams[player] == activeTeam)
                {
                    players.Add(player);
                }
            }
            if (_visionPlayerIds.SetEquals(players))
            {
                return;
            }

            _visionPlayerIds.Clear();
            foreach (int player in players)
            {
                _visionPlayerIds.Add(player);
            }
            _permanentInitialLordVisionDirty = true;

            List<int> staleUnits = new List<int>();
            foreach (KeyValuePair<int, UnitVisionSource> entry in _friendlyUnitSources)
            {
                if (!_visionPlayerIds.Contains(entry.Value.Owner))
                {
                    staleUnits.Add(entry.Key);
                }
            }
            foreach (int objectId in staleUnits)
            {
                _friendlyUnitSources.Remove(objectId);
            }

            List<int> staleStructures = new List<int>();
            foreach (KeyValuePair<int, int> entry in _friendlyStructureOwners)
            {
                if (!_visionPlayerIds.Contains(entry.Value))
                {
                    staleStructures.Add(entry.Key);
                }
            }
            foreach (int structureId in staleStructures)
            {
                RemoveFriendlyStructure(structureId);
            }
            _seededBasePlayers.RemoveWhere(
                delegate(int player) { return !_visionPlayerIds.Contains(player); });
            _pendingPlacements.RemoveAll(
                delegate(PendingPlacement pending)
                {
                    return !_visionPlayerIds.Contains(pending.PlayerId);
                });

            List<int> orderedPlayers = new List<int>(_visionPlayerIds);
            orderedPlayers.Sort();
            Logger.LogInfo(string.Format(
                "Alliance vision updated from native teams: localPlayer={0}, team={1}, visionPlayers={2}.",
                _activePlayer, activeTeam, string.Join(",", orderedPlayers.ToArray())));
        }

        private bool IsVisionPlayer(int player)
        {
            return player > 0 && _visionPlayerIds.Contains(player);
        }

        private int AddFriendlyUnits()
        {
            _unitVisionStampKeys.Clear();
            if (_nativeUnitVisionReader.TryReadActive(_nativeVisionUnits))
            {
                LogUnitCoordinateSample();
                CaptureInitialLordPositions(_nativeVisionUnits);
                if (!_loggedNativeUnitVision)
                {
                    Logger.LogInfo(
                        "Unit vision now uses the authoritative native global unit table; off-screen units remain tracked.");
                    _loggedNativeUnitVision = true;
                }
                _friendlyUnitIds.Clear();
                int nativeCount = 0;
                foreach (NativeVisionUnit unit in _nativeVisionUnits)
                {
                    if (unit.Owner == _activePlayer)
                    {
                        _friendlyUnitIds.Add(unit.Id);
                    }
                    if (!IsVisionPlayer(unit.Owner) ||
                        unit.UnitType == ChimpTypeChicken)
                    {
                        continue;
                    }
                    float sightRadius = IsReducedVisionCivilian(unit.UnitType)
                        ? _civilianSightRadius
                        : _unitSightRadius;
                    if (IsSightContainedByPermanentInitialLordVision(
                            unit.LogicX, unit.LogicY, sightRadius) ||
                        !TryReserveUnitVisionStamp(
                            unit.LogicX, unit.LogicY, sightRadius))
                    {
                        continue;
                    }
                    AddSightAtLogic(unit.LogicX, unit.LogicY, sightRadius);
                    nativeCount++;
                }
                return nativeCount;
            }

            if (!_loggedNativeUnitVisionFallback)
            {
                Logger.LogWarning(
                    "Authoritative native unit vision is unavailable (" +
                    _nativeUnitVisionReader.FailureReason +
                    "); using the camera-local managed fallback.");
                _loggedNativeUnitVisionFallback = true;
            }
            Dictionary<int, Chimp> units = GetDictionary<int, Chimp>(ChimpsField);
            if (units == null)
            {
                return 0;
            }

            UpdateFriendlyUnitOwnership(units);
            int count = 0;
            List<int> expired = new List<int>();
            foreach (KeyValuePair<int, UnitVisionSource> entry in _friendlyUnitSources)
            {
                if (Time.unscaledTime - entry.Value.LastConfirmed > UnitVisionGraceSeconds)
                {
                    expired.Add(entry.Key);
                    continue;
                }
                if (entry.Value.UnitType == ChimpTypeChicken)
                {
                    continue;
                }
                float sightRadius = IsReducedVisionCivilian(entry.Value.UnitType)
                    ? _civilianSightRadius
                    : _unitSightRadius;
                AddSight(entry.Value.TileX, entry.Value.TileY, sightRadius);
                count++;
            }
            foreach (int objectId in expired)
            {
                _friendlyUnitSources.Remove(objectId);
            }
            return count;
        }

        private void LogUnitCoordinateSample()
        {
            if (_coordinateAuditRotation == _activeRotationIndex) return;
            Dictionary<int, Chimp> sprites = GetDictionary<int, Chimp>(ChimpsField);
            if (sprites == null) return;
            int samples = 0;
            foreach (NativeVisionUnit unit in _nativeVisionUnits)
            {
                Chimp sprite;
                if (unit.Owner != _activePlayer ||
                    !sprites.TryGetValue(unit.Id, out sprite) || sprite == null) continue;
                int logicX;
                int logicY;
                if (!TryTileMapToLogic(sprite.mapX, sprite.mapY, out logicX, out logicY)) continue;
                int last = _mapOffset + _mapSize - 1;
                Vector3 origin = GameToWorld(_mapOffset, _mapOffset);
                float pixelScale = (_resolution - 1f) / Math.Max(1f, _mapSize - 1f);
                float u = Mathf.RoundToInt((unit.LogicX - _mapOffset) * pixelScale) / (_resolution - 1f);
                float v = Mathf.RoundToInt((unit.LogicY - _mapOffset) * pixelScale) / (_resolution - 1f);
                Vector3 fogWorld = origin +
                    (GameToWorld(last, _mapOffset) - origin) * u +
                    (GameToWorld(_mapOffset, last) - origin) * v;
                Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                    "Fog coordinate sample: rotation={0}, unit={1}, type={2}, nativeCentre=({3:0.###},{4:0.###}), displayedCell=({5},{6}), spriteWorld=({7:0.###},{8:0.###}), fogWorld=({9:0.###},{10:0.###}).",
                    _activeRotationIndex, unit.Id, unit.UnitType, unit.LogicX, unit.LogicY,
                    logicX, logicY, sprite.position.x, sprite.position.y, fogWorld.x, fogWorld.y));
                if (++samples >= 4) break;
            }
            if (samples > 0) _coordinateAuditRotation = _activeRotationIndex;
        }

        private void CaptureInitialLordPositions(
            List<NativeVisionUnit> units)
        {
            foreach (NativeVisionUnit unit in units)
            {
                if (unit.UnitType != ChimpTypeLord ||
                    unit.Owner <= 0 || unit.Owner > 8 ||
                    _initialLordLogicByPlayer.ContainsKey(unit.Owner) ||
                    unit.LogicX < _mapOffset || unit.LogicY < _mapOffset ||
                    unit.LogicX >= _mapOffset + _mapSize ||
                    unit.LogicY >= _mapOffset + _mapSize)
                {
                    continue;
                }
                _initialLordLogicByPlayer[unit.Owner] =
                    new Vector2(unit.LogicX, unit.LogicY);
                if (IsVisionPlayer(unit.Owner))
                {
                    _permanentInitialLordVisionDirty = true;
                    Logger.LogInfo(string.Format(
                        "Initial lord position frozen for permanent base vision: player={0}, logic=({1:0.###},{2:0.###}), radius={3}.",
                        unit.Owner, unit.LogicX, unit.LogicY,
                        _initialLordBaseSightRadius));
                }
            }
        }

        private void EnsurePermanentInitialLordVision()
        {
            if (!_permanentInitialLordVisionDirty || _visible == null)
            {
                return;
            }
            if (_permanentInitialLordVision == null ||
                _permanentInitialLordVision.Length != _visible.Length)
            {
                _permanentInitialLordVision = new float[_visible.Length];
            }
            else
            {
                Array.Clear(
                    _permanentInitialLordVision, 0,
                    _permanentInitialLordVision.Length);
            }
            _activeInitialLordVisionSources.Clear();

            List<int> players = new List<int>(_visionPlayerIds);
            players.Sort();
            foreach (int player in players)
            {
                Vector2 position;
                if (!_initialLordLogicByPlayer.TryGetValue(
                        player, out position))
                {
                    continue;
                }
                AddSightAtLogic(
                    _permanentInitialLordVision, position.x, position.y,
                    _initialLordBaseSightRadius);
                _activeInitialLordVisionSources.Add(position);
            }
            _permanentInitialLordVisionDirty = false;
            Logger.LogInfo(string.Format(
                "Permanent initial-lord base vision rebuilt: radius={0}, sources={1}, capturedLords={2}, visionPlayers={3}.",
                _initialLordBaseSightRadius,
                _activeInitialLordVisionSources.Count,
                _initialLordLogicByPlayer.Count,
                _visionPlayerIds.Count));
        }

        private bool IsSightContainedByPermanentInitialLordVision(
            float logicX, float logicY, float radiusInCells)
        {
            float containmentRadius = _initialLordBaseSightRadius - radiusInCells;
            if (containmentRadius < 0f)
            {
                return false;
            }
            float containmentSquared = containmentRadius * containmentRadius;
            foreach (Vector2 initialLord in _activeInitialLordVisionSources)
            {
                float deltaX = logicX - initialLord.x;
                float deltaY = logicY - initialLord.y;
                if (deltaX * deltaX + deltaY * deltaY <= containmentSquared)
                {
                    return true;
                }
            }
            return false;
        }

        private bool TryReserveUnitVisionStamp(
            float logicX, float logicY, float radiusInCells)
        {
            if (logicX < _mapOffset || logicY < _mapOffset ||
                logicX >= _mapOffset + _mapSize ||
                logicY >= _mapOffset + _mapSize)
            {
                return false;
            }
            float scale = (_resolution - 1f) / Math.Max(1f, _mapSize - 1f);
            int pixelX = Mathf.RoundToInt((logicX - _mapOffset) * scale);
            int pixelY = Mathf.RoundToInt((logicY - _mapOffset) * scale);
            int radiusKey = Mathf.RoundToInt(
                Mathf.Max(1f, radiusInCells * scale) * 256f);
            int pixelIndex = pixelY * _resolution + pixelX;
            long stampKey = ((long)radiusKey << 32) | (uint)pixelIndex;
            return _unitVisionStampKeys.Add(stampKey);
        }

        private bool IsFriendlyUnit(Chimp unit)
        {
            return unit != null &&
                _friendlyUnitIds.Contains(unit.objectID);
        }

        private void UpdateFriendlyUnitOwnership(Dictionary<int, Chimp> units)
        {
            _friendlyUnitIds.Clear();
            foreach (Chimp unit in units.Values)
            {
                if (unit == null)
                {
                    continue;
                }
                int owner;
                if (!TryGetCachedNativeUnitOwner(unit, out owner))
                {
                    continue;
                }
                if (owner == _activePlayer)
                {
                    _friendlyUnitIds.Add(unit.objectID);
                }
                if (IsVisionPlayer(owner))
                {
                    _friendlyUnitSources[unit.objectID] = new UnitVisionSource
                    {
                        Owner = owner,
                        TileX = unit.mapX,
                        TileY = unit.mapY,
                        UnitType = unit.gameObjectType,
                        LastConfirmed = Time.unscaledTime
                    };
                }
                else if (owner > 0)
                {
                    _friendlyUnitSources.Remove(unit.objectID);
                }
            }
        }

        private static bool IsReducedVisionCivilian(int unitType)
        {
            if (unitType == ChimpTypeWoodcutter ||
                unitType == ChimpTypeHunter ||
                unitType == ChimpTypeLord ||
                unitType == ChimpTypeMonk ||
                unitType == ChimpTypeSiegeTent ||
                unitType == ChimpTypeWarDog)
            {
                return false;
            }
            if ((unitType >= ChimpTypeArcher && unitType <= ChimpTypeEngineer) ||
                (unitType >= ChimpTypeArcherDebug && unitType <= ChimpTypeMangonel) ||
                (unitType >= ChimpTypeSiegeTower && unitType <= ChimpTypeBallista) ||
                (unitType >= ChimpTypeArabBow &&
                    unitType <= ChimpTypeBedouinDemolisher))
            {
                return false;
            }
            return true;
        }

        private bool TryGetCachedNativeUnitOwner(Chimp unit, out int owner)
        {
            owner = 0;
            if (unit == null)
            {
                return false;
            }
            UnitOwnerCacheEntry cached;
            if (_unitOwnerCache.TryGetValue(unit.objectID, out cached) &&
                ReferenceEquals(cached.Unit, unit))
            {
                owner = cached.Owner;
                return owner > 0;
            }
            if (!TryResolveNativeUnitOwner(unit, out owner))
            {
                return false;
            }
            _unitOwnerCache[unit.objectID] = new UnitOwnerCacheEntry
            {
                Unit = unit,
                Owner = owner
            };
            return true;
        }

        private bool TryResolveNativeUnitOwner(Chimp unit, out int owner)
        {
            owner = 0;
            int centreX;
            int centreY;
            if (unit == null ||
                !TryTileMapToLogic(unit.mapX, unit.mapY, out centreX, out centreY))
            {
                return false;
            }

            for (int radius = 0; radius <= UnitIdentitySearchRadius; radius++)
            {
                for (int logicY = centreY - radius; logicY <= centreY + radius; logicY++)
                {
                    for (int logicX = centreX - radius; logicX <= centreX + radius; logicX++)
                    {
                        if (radius > 0 && logicX != centreX - radius &&
                            logicX != centreX + radius && logicY != centreY - radius &&
                            logicY != centreY + radius)
                        {
                            continue;
                        }
                        if (logicX < 0 || logicY < 0 ||
                            logicX >= GameMap.RAW_MAP_SIZE || logicY >= GameMap.RAW_MAP_SIZE)
                        {
                            continue;
                        }
                        EngineInterface.LogicDebugInfo debug =
                            EngineInterface.GetLayerDebug(logicX, logicY);
                        if (debug.chimp_layer == unit.objectID)
                        {
                            owner = debug.occupancy_layer;
                            return owner > 0;
                        }
                    }
                }
            }
            return false;
        }

        private void RememberFriendlyStructure(
            int structureId, int owner, int tileX, int tileY)
        {
            if (structureId <= 0 || !IsVisionPlayer(owner))
            {
                return;
            }
            _friendlyStructureIds.Add(structureId);
            _friendlyStructureOwners[structureId] = owner;
            if (!_friendlyStructurePositions.ContainsKey(structureId))
            {
                _friendlyStructurePositions[structureId] = new Vector2Int(tileX, tileY);
            }
        }

        private void RemoveFriendlyStructure(int structureId)
        {
            _friendlyStructureIds.Remove(structureId);
            _friendlyStructureOwners.Remove(structureId);
            _friendlyStructurePositions.Remove(structureId);
            _friendlyTowerTypes.Remove(structureId);
        }

        private static int TileDistanceSquared(int x1, int y1, int x2, int y2)
        {
            int dx = x1 - x2;
            int dy = y1 - y2;
            return dx * dx + dy * dy;
        }

        private void LogSourceDiagnostics()
        {
            _loggedSourceDiagnostics = true;
            Dictionary<int, Chimp> chimps = GetDictionary<int, Chimp>(ChimpsField);
            Dictionary<int, Org> orgs = GetDictionary<int, Org>(OrgsField);
            Dictionary<int, Fly> flies = GetDictionary<int, Fly>(FliesField);
            Dictionary<int, Pixie> pixies = GetDictionary<int, Pixie>(PixiesField);
            Dictionary<int, BuildingAnim> buildings = GetDictionary<int, BuildingAnim>(BuildingsField);

            Dictionary<int, int> chimpTypes = new Dictionary<int, int>();
            Dictionary<int, int> chimpPrimaryColours = new Dictionary<int, int>();
            Dictionary<int, int> chimpSecondaryColours = new Dictionary<int, int>();
            Dictionary<int, int> buildingTypes = new Dictionary<int, int>();
            Dictionary<int, int> buildingColours = new Dictionary<int, int>();
            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            if (chimps != null)
            {
                foreach (Chimp chimp in chimps.Values)
                {
                    if (chimp == null)
                    {
                        continue;
                    }
                    IncrementCount(chimpTypes, chimp.gameObjectType);
                    IncrementCount(chimpPrimaryColours, chimp.colour1);
                    IncrementCount(chimpSecondaryColours, chimp.colour2);
                    minX = Math.Min(minX, chimp.mapX);
                    minY = Math.Min(minY, chimp.mapY);
                    maxX = Math.Max(maxX, chimp.mapX);
                    maxY = Math.Max(maxY, chimp.mapY);
                }
            }

            if (buildings != null)
            {
                foreach (BuildingAnim building in buildings.Values)
                {
                    if (building == null)
                    {
                        continue;
                    }
                    IncrementCount(buildingTypes, building.type);
                    IncrementCount(buildingColours, building.colour);
                }
            }

            Logger.LogInfo(string.Format(
                "Source diagnostics: chimps={0}, orgs={1}, flies={2}, pixies={3}, buildingAnims={4}, chimpColours1=[{5}], chimpColours2=[{6}], chimpTypes=[{7}], buildingColours=[{8}], buildingTypes=[{9}], chimpRange=({10},{11})-({12},{13}).",
                chimps == null ? 0 : chimps.Count,
                orgs == null ? 0 : orgs.Count,
                flies == null ? 0 : flies.Count,
                pixies == null ? 0 : pixies.Count,
                buildings == null ? 0 : buildings.Count,
                FormatCounts(chimpPrimaryColours), FormatCounts(chimpSecondaryColours),
                FormatCounts(chimpTypes), FormatCounts(buildingColours), FormatCounts(buildingTypes),
                minX == int.MaxValue ? -1 : minX, minY == int.MaxValue ? -1 : minY,
                maxX == int.MinValue ? -1 : maxX, maxY == int.MinValue ? -1 : maxY));
        }

        private static void IncrementCount(Dictionary<int, int> counts, int value)
        {
            int current;
            counts.TryGetValue(value, out current);
            counts[value] = current + 1;
        }

        private static string FormatCounts(Dictionary<int, int> counts)
        {
            List<int> keys = new List<int>(counts.Keys);
            keys.Sort();
            List<string> values = new List<string>();
            foreach (int key in keys)
            {
                values.Add(key + ":" + counts[key]);
            }
            return string.Join(",", values.ToArray());
        }

        private int AddFriendlyBuildings()
        {
            RefreshStructureSources();
            foreach (Vector2Int source in _structureSources)
            {
                AddSight(source.x, source.y, _buildingSightRadius);
            }

            HashSet<int> occupiedTowerIds = FindOccupiedFriendlyTowerIds();
            foreach (TowerVisionSource source in _towerSources)
            {
                float radius = occupiedTowerIds.Contains(source.StructureId)
                    ? _towerSightRadii[source.Type - 74]
                    : _wallSightRadius;
                AddSight(source.TileX, source.TileY, radius);
            }
            return _structureSources.Count + _towerSources.Count;
        }

        private HashSet<int> FindOccupiedFriendlyTowerIds()
        {
            if (_friendlyTowerTypes.Count == 0)
            {
                _occupiedFriendlyTowerIds.Clear();
                return _occupiedFriendlyTowerIds;
            }
            if (Time.unscaledTime < _nextTowerOccupancyScan)
            {
                return _occupiedFriendlyTowerIds;
            }
            _nextTowerOccupancyScan = Time.unscaledTime + 0.5f;
            _occupiedFriendlyTowerIds.Clear();
            Dictionary<int, Chimp> units = GetDictionary<int, Chimp>(ChimpsField);
            if (units == null)
            {
                return _occupiedFriendlyTowerIds;
            }

            foreach (Chimp unit in units.Values)
            {
                int owner;
                int centreX;
                int centreY;
                if (unit == null ||
                    !TryGetCachedNativeUnitOwner(unit, out owner) ||
                    !IsVisionPlayer(owner) ||
                    !TryTileMapToLogic(unit.mapX, unit.mapY, out centreX, out centreY))
                {
                    continue;
                }

                for (int radius = 0; radius <= UnitIdentitySearchRadius; radius++)
                {
                    bool matchedUnit = false;
                    for (int logicY = centreY - radius;
                        logicY <= centreY + radius && !matchedUnit; logicY++)
                    {
                        for (int logicX = centreX - radius;
                            logicX <= centreX + radius; logicX++)
                        {
                            if (radius > 0 && logicX != centreX - radius &&
                                logicX != centreX + radius && logicY != centreY - radius &&
                                logicY != centreY + radius)
                            {
                                continue;
                            }
                            if (logicX < 0 || logicY < 0 ||
                                logicX >= GameMap.RAW_MAP_SIZE ||
                                logicY >= GameMap.RAW_MAP_SIZE)
                            {
                                continue;
                            }
                            EngineInterface.LogicDebugInfo debug =
                                EngineInterface.GetLayerDebug(logicX, logicY);
                            if (debug.chimp_layer == unit.objectID)
                            {
                                matchedUnit = true;
                                if (_friendlyTowerTypes.ContainsKey(debug.structure_layer))
                                {
                                    _occupiedFriendlyTowerIds.Add(debug.structure_layer);
                                }
                                break;
                            }
                        }
                    }
                    if (matchedUnit)
                    {
                        break;
                    }
                }
            }
            return _occupiedFriendlyTowerIds;
        }

        private void RefreshStructureSources()
        {
            if (Time.unscaledTime < _nextStructureScan)
            {
                return;
            }
            _nextStructureScan = Time.unscaledTime + 0.75f;
            if (_nativeUnitVisionReader.TryReadBuildings(_nativeVisionBuildings))
            {
                _nextStructureScan = Time.unscaledTime + 1f;
                ReconcileNativeBuildings();
                return;
            }
            if (!_loggedNativeBuildingFallback)
            {
                _loggedNativeBuildingFallback = true;
                Logger.LogWarning("Native building reconciliation unavailable; using placement/base fallback: " +
                    _nativeUnitVisionReader.FailureReason);
            }
            ClaimPendingPlacements();
            if (EngineInterface.FlattenedLandscape)
            {
                return;
            }
            int candidates = 0;
            int baseStructureMatches = 0;
            SeedFriendlyStructuresNearBases(ref candidates, ref baseStructureMatches);
            RebuildTrackedStructureSources();

            if (!_loggedFirstRefresh)
            {
                Logger.LogInfo(string.Format(
                    "Bounded structure vision scan: baseCandidates={0}, buildingSources={1}, towerSources={2}, player={3}, visionPlayers={4}, cachedStructureIDs={5}, baseClaims={6}, wallVision=False.",
                    candidates, _structureSources.Count,
                    _towerSources.Count, _activePlayer, _visionPlayerIds.Count,
                    _friendlyStructureIds.Count, baseStructureMatches));
            }
        }

        private void ReconcileNativeBuildings()
        {
            // Authoritative live snapshot, not a spawn-only cache: covers Script Extender
            // CreatePrefab, loaded maps, deletions, ownership changes and reused slots.
            _friendlyStructureIds.Clear();
            _friendlyStructureOwners.Clear();
            _friendlyStructurePositions.Clear();
            _friendlyTowerTypes.Clear();
            _structureSources.Clear();
            _towerSources.Clear();
            _pendingPlacements.Clear();
            HashSet<int> buckets = new HashSet<int>();
            foreach (NativeVisionBuilding building in _nativeVisionBuildings)
            {
                int type = building.Type;
                if (!IsVisionPlayer(building.Owner) || type == 90 ||
                    (type >= 110 && type <= 117) || IsDestroyedTowerType(type) ||
                    !IsLogicCoordinateOnMap(building.LogicX, building.LogicY)) continue;
                int tileX;
                int tileY;
                _activeMap.mapGameTileToTilemapCoord(
                    building.LogicX, building.LogicY, out tileX, out tileY);
                if (tileX < 0 || tileY < 0 || tileX >= _activeTiles.GetLength(0) ||
                    tileY >= _activeTiles.GetLength(1)) continue;
                RememberFriendlyStructure(building.Id, building.Owner, tileX, tileY);
                if (IsTowerType(type))
                {
                    _friendlyTowerTypes[building.Id] = type;
                    _towerSources.Add(new TowerVisionSource
                    {
                        StructureId = building.Id, Type = type, TileX = tileX, TileY = tileY
                    });
                }
                else AddStructureSource(tileX, tileY, buckets, _structureSources);
            }
            _occupiedFriendlyTowerIds.Clear();
            _nextTowerOccupancyScan = 0f;
        }

        private void SeedFriendlyStructuresNearBases(
            ref int candidates, ref int baseStructureMatches)
        {
            Dictionary<int, Vector2Int> baseAnchors =
                new Dictionary<int, Vector2Int>();
            List<int> players = new List<int>(_visionPlayerIds);
            players.Sort();
            foreach (int player in players)
            {
                Vector2Int anchor;
                if (!_seededBasePlayers.Contains(player) &&
                    TryGetPlayerAnchorTile(player, out anchor))
                {
                    baseAnchors[player] = anchor;
                }
            }
            if (baseAnchors.Count == 0)
            {
                return;
            }

            HashSet<int> scannedTiles = new HashSet<int>();
            HashSet<int> matchedPlayers = new HashSet<int>();
            int width = _activeTiles.GetLength(0);
            int height = _activeTiles.GetLength(1);
            foreach (KeyValuePair<int, Vector2Int> anchor in baseAnchors)
            {
                int minX = Math.Max(0, anchor.Value.x - FriendlyBaseSeedRadius);
                int maxX = Math.Min(width - 1, anchor.Value.x + FriendlyBaseSeedRadius);
                int minY = Math.Max(0, anchor.Value.y - FriendlyBaseSeedRadius);
                int maxY = Math.Min(height - 1, anchor.Value.y + FriendlyBaseSeedRadius);
                for (int tileY = minY; tileY <= maxY; tileY++)
                {
                    for (int tileX = minX; tileX <= maxX; tileX++)
                    {
                        int tileIndex = tileY * width + tileX;
                        if (!scannedTiles.Add(tileIndex))
                        {
                            continue;
                        }
                        GameMapTile tile = _activeTiles[tileX, tileY];
                        if (!IsStructureTileCandidate(tile))
                        {
                            continue;
                        }
                        candidates++;
                        EngineInterface.LogicDebugInfo debug =
                            EngineInterface.GetLayerDebug(tile.gameMapX, tile.gameMapY);
                        if (debug.structure_layer <= 0 || IsWallTile(debug) ||
                            IsDestroyedTowerType(debug.structure_was_layer))
                        {
                            continue;
                        }
                        int owner = FindNearbyAnchorOwner(
                            tileX, tileY, baseAnchors, FriendlyBaseSeedRadius);
                        if (owner <= 0)
                        {
                            continue;
                        }
                        bool newlyClaimed =
                            !_friendlyStructureIds.Contains(debug.structure_layer);
                        RememberFriendlyStructure(
                            debug.structure_layer, owner, tileX, tileY);
                        matchedPlayers.Add(owner);
                        if (newlyClaimed)
                        {
                            baseStructureMatches++;
                        }
                    }
                }
            }
            foreach (int player in matchedPlayers)
            {
                _seededBasePlayers.Add(player);
            }
        }

        private void RebuildTrackedStructureSources()
        {
            _structureSources.Clear();
            _towerSources.Clear();
            HashSet<int> structureBuckets = new HashSet<int>();
            List<int> removed = new List<int>();
            foreach (KeyValuePair<int, Vector2Int> entry in _friendlyStructurePositions)
            {
                int logicX;
                int logicY;
                if (!TryTileMapToLogic(entry.Value.x, entry.Value.y, out logicX, out logicY))
                {
                    removed.Add(entry.Key);
                    continue;
                }
                EngineInterface.LogicDebugInfo debug =
                    EngineInterface.GetLayerDebug(logicX, logicY);
                int structureType = debug.structure_was_layer;
                if (debug.structure_layer != entry.Key || IsWallTile(debug) ||
                    IsDestroyedTowerType(structureType))
                {
                    removed.Add(entry.Key);
                    continue;
                }
                if (IsTowerType(structureType))
                {
                    _friendlyTowerTypes[entry.Key] = structureType;
                    _towerSources.Add(new TowerVisionSource
                    {
                        StructureId = entry.Key,
                        Type = structureType,
                        TileX = entry.Value.x,
                        TileY = entry.Value.y
                    });
                }
                else
                {
                    _friendlyTowerTypes.Remove(entry.Key);
                    AddStructureSource(
                        entry.Value.x, entry.Value.y,
                        structureBuckets, _structureSources);
                }
            }
            foreach (int structureId in removed)
            {
                RemoveFriendlyStructure(structureId);
            }
            _nextTowerOccupancyScan = 0f;
        }

        private void AddStructureSource(
            int tileX, int tileY, HashSet<int> occupiedBuckets,
            List<Vector2Int> sources)
        {
            int bucketX = tileX / StructureSourceSpacing;
            int bucketY = tileY / StructureSourceSpacing;
            int bucket = bucketY * GameMap.RAW_MAP_SIZE + bucketX;
            if (occupiedBuckets.Add(bucket))
            {
                sources.Add(new Vector2Int(tileX, tileY));
            }
        }

        private void ClaimPendingPlacements()
        {
            for (int index = _pendingPlacements.Count - 1; index >= 0; index--)
            {
                PendingPlacement pending = _pendingPlacements[index];
                if (!IsVisionPlayer(pending.PlayerId))
                {
                    _pendingPlacements.RemoveAt(index);
                    continue;
                }
                if (Time.unscaledTime > pending.ExpiresAt)
                {
                    _pendingPlacements.RemoveAt(index);
                    continue;
                }

                for (int logicY = pending.LogicY - PlacementClaimRadius;
                    logicY <= pending.LogicY + PlacementClaimRadius; logicY++)
                {
                    for (int logicX = pending.LogicX - PlacementClaimRadius;
                        logicX <= pending.LogicX + PlacementClaimRadius; logicX++)
                    {
                        if (logicX < 0 || logicY < 0 ||
                            logicX >= GameMap.RAW_MAP_SIZE || logicY >= GameMap.RAW_MAP_SIZE)
                        {
                            continue;
                        }
                        EngineInterface.LogicDebugInfo debug =
                            EngineInterface.GetLayerDebug(logicX, logicY);
                        if (debug.structure_layer <= 0 ||
                            pending.ExistingStructureIds.Contains(debug.structure_layer))
                        {
                            continue;
                        }
                        int tileX;
                        int tileY;
                        _activeMap.mapGameTileToTilemapCoord(
                            logicX, logicY, out tileX, out tileY);
                        bool newlyClaimed = !_friendlyStructureIds.Contains(debug.structure_layer);
                        RememberFriendlyStructure(
                            debug.structure_layer, pending.PlayerId, tileX, tileY);
                        if (newlyClaimed)
                        {
                            Logger.LogInfo(string.Format(
                                "Vision-team building claimed: player={0}, structure={1}, logic=({2},{3}), tile=({4},{5}).",
                                pending.PlayerId, debug.structure_layer,
                                logicX, logicY, tileX, tileY));
                        }
                    }
                }
            }
        }

        private bool TryGetPlayerAnchorTile(int player, out Vector2Int position)
        {
            if (player == _activePlayer)
            {
                return TryGetLocalAnchorTile(out position);
            }
            position = Vector2Int.zero;
            if (_activeMap == null || _activeTiles == null || !IsVisionPlayer(player))
            {
                return false;
            }
            int[,] keepLocations = KeepLocationsField == null || GameData.Instance == null
                ? null
                : KeepLocationsField.GetValue(GameData.Instance) as int[,];
            int keepIndex = player - 1;
            if (keepLocations == null || keepIndex < 0 ||
                keepIndex >= keepLocations.GetLength(0) ||
                keepLocations.GetLength(1) < 2)
            {
                return false;
            }
            int logicX = keepLocations[keepIndex, 0];
            int logicY = keepLocations[keepIndex, 1];
            if (!IsLogicCoordinateOnMap(logicX, logicY))
            {
                return false;
            }
            int tileX;
            int tileY;
            _activeMap.mapGameTileToTilemapCoord(logicX, logicY, out tileX, out tileY);
            if (tileX < 0 || tileY < 0 ||
                tileX >= _activeTiles.GetLength(0) ||
                tileY >= _activeTiles.GetLength(1))
            {
                return false;
            }
            position = new Vector2Int(tileX, tileY);
            return true;
        }

        private bool TryGetLocalAnchorTile(out Vector2Int position)
        {
            if (_localAnchorResolved)
            {
                position = _localAnchor;
                return true;
            }
            position = Vector2Int.zero;
            if (_activeMap == null || _activeTiles == null || _activePlayer < 1)
            {
                return false;
            }

            int[,] keepLocations = KeepLocationsField == null || GameData.Instance == null
                ? null
                : KeepLocationsField.GetValue(GameData.Instance) as int[,];
            int keepIndex = _activePlayer - 1;
            if (keepLocations != null && keepIndex >= 0 &&
                keepIndex < keepLocations.GetLength(0) &&
                keepLocations.GetLength(1) >= 2 &&
                TryCacheLocalAnchor(
                    keepLocations[keepIndex, 0], keepLocations[keepIndex, 1],
                    "keep index " + keepIndex, out position))
            {
                return true;
            }

            if (!_initialMapSnapshotAccepted)
            {
                return false;
            }

            return TryGetVerifiedFriendlyUnitAnchor(out position);
        }

        private bool TryGetVerifiedFriendlyUnitAnchor(out Vector2Int position)
        {
            position = Vector2Int.zero;
            Dictionary<int, Chimp> units = GetDictionary<int, Chimp>(ChimpsField);
            if (units == null)
            {
                return false;
            }

            Chimp candidate = null;
            foreach (Chimp unit in units.Values)
            {
                int owner;
                if (unit == null ||
                    !(TryGetCachedNativeUnitOwner(unit, out owner) && owner == _activePlayer))
                {
                    continue;
                }
                if (candidate == null)
                {
                    candidate = unit;
                }
                if (unit.gameObjectType == ChimpTypeLord)
                {
                    candidate = unit;
                    break;
                }
            }
            if (candidate == null)
            {
                return false;
            }

            int logicX;
            int logicY;
            if (!TryTileMapToLogic(candidate.mapX, candidate.mapY, out logicX, out logicY))
            {
                return false;
            }
            string source = candidate.gameObjectType == ChimpTypeLord
                ? "native friendly lord"
                : "native friendly unit";
            return TryCacheLocalAnchor(logicX, logicY, source, out position);
        }

        private bool TryCacheLocalAnchor(
            int logicX, int logicY, string source, out Vector2Int position)
        {
            position = Vector2Int.zero;
            if (!IsLogicCoordinateOnMap(logicX, logicY))
            {
                return false;
            }
            int tileX;
            int tileY;
            _activeMap.mapGameTileToTilemapCoord(logicX, logicY, out tileX, out tileY);
            if (tileX < 0 || tileY < 0 ||
                tileX >= _activeTiles.GetLength(0) || tileY >= _activeTiles.GetLength(1))
            {
                return false;
            }
            _localAnchor = new Vector2Int(tileX, tileY);
            _localAnchorResolved = true;
            position = _localAnchor;
            Logger.LogInfo(string.Format(
                "Local vision anchor resolved from {0}: player={1}, logic=({2},{3}), tile=({4},{5}).",
                source, _activePlayer, logicX, logicY, tileX, tileY));
            return true;
        }

        private static int FindNearbyAnchorOwner(
            int tileX, int tileY, Dictionary<int, Vector2Int> anchors, int radius)
        {
            int limit = radius * radius;
            int nearestPlayer = 0;
            int nearestDistance = int.MaxValue;
            foreach (KeyValuePair<int, Vector2Int> entry in anchors)
            {
                int distance = TileDistanceSquared(
                    tileX, tileY, entry.Value.x, entry.Value.y);
                if (distance <= limit && distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestPlayer = entry.Key;
                }
            }
            return nearestPlayer;
        }

        private static bool IsWallTile(EngineInterface.LogicDebugInfo debug)
        {
            int type = debug.structure_was_layer;
            return type == 90 || (type >= 110 && type <= 117);
        }

        private static bool IsTowerType(int type)
        {
            return type >= 74 && type <= 78;
        }

        private static bool IsDestroyedTowerType(int type)
        {
            return type == 79 || (type >= 86 && type <= 89);
        }

        private static bool IsStructureTileCandidate(GameMapTile tile)
        {
            return tile != null &&
                (tile.buildingHeight > 0.01f || tile.lastChevronFile > 0 ||
                 tile.constructionOrigImage != null);
        }

        private bool TryTileMapToLogic(
            int tileX, int tileY, out int logicX, out int logicY)
        {
            logicX = 0;
            logicY = 0;
            if (_activeTiles == null || tileX < 0 || tileY < 0 ||
                tileX >= _activeTiles.GetLength(0) || tileY >= _activeTiles.GetLength(1))
            {
                return false;
            }
            GameMapTile tile = _activeTiles[tileX, tileY];
            if (tile == null)
            {
                return false;
            }
            logicX = tile.gameMapX;
            logicY = tile.gameMapY;
            return true;
        }

        private void AddSight(int mapX, int mapY, float radiusInCells)
        {
            float scale = (_resolution - 1f) / Math.Max(1f, _mapSize - 1f);
            float centreX;
            float centreY;
            if (!TryMapToFogPixel(mapX, mapY, scale, out centreX, out centreY))
            {
                return;
            }
            AddSightAtFogPixel(centreX, centreY, radiusInCells, scale);
        }

        private void AddSightAtLogic(float logicX, float logicY, float radiusInCells)
        {
            AddSightAtLogic(_visible, logicX, logicY, radiusInCells);
        }

        private void AddSightAtLogic(
            float[] target, float logicX, float logicY, float radiusInCells)
        {
            if (target == null || logicX < _mapOffset || logicY < _mapOffset ||
                logicX >= _mapOffset + _mapSize ||
                logicY >= _mapOffset + _mapSize)
            {
                return;
            }
            float scale = (_resolution - 1f) / Math.Max(1f, _mapSize - 1f);
            AddSightAtFogPixel(
                target,
                (logicX - _mapOffset) * scale,
                (logicY - _mapOffset) * scale,
                radiusInCells, scale);
        }

        private void AddSightAtFogPixel(
            float centreX, float centreY, float radiusInCells, float scale)
        {
            AddSightAtFogPixel(
                _visible, centreX, centreY, radiusInCells, scale);
        }

        private void AddSightAtFogPixel(
            float[] target, float centreX, float centreY,
            float radiusInCells, float scale)
        {
            float radius = Mathf.Max(1f, radiusInCells * scale);
            float softness = Mathf.Clamp(_softEdgeWidth * scale, 0.5f, radius);
            SightKernelSample[] kernel = GetSightKernel(radius, softness);
            int centrePixelX = Mathf.RoundToInt(centreX);
            int centrePixelY = Mathf.RoundToInt(centreY);
            foreach (SightKernelSample sample in kernel)
            {
                int x = centrePixelX + sample.DeltaX;
                int y = centrePixelY + sample.DeltaY;
                if (x < 0 || y < 0 || x >= _resolution || y >= _resolution)
                {
                    continue;
                }
                int index = y * _resolution + x;
                if (sample.Sight > target[index])
                {
                    target[index] = sample.Sight;
                }
            }
        }

        private SightKernelSample[] GetSightKernel(float radius, float softness)
        {
            int radiusKey = Mathf.RoundToInt(radius * 256f);
            int softnessKey = Mathf.RoundToInt(softness * 256f);
            long key = ((long)radiusKey << 32) | (uint)softnessKey;
            SightKernelSample[] cached;
            if (_sightKernelCache.TryGetValue(key, out cached))
            {
                return cached;
            }

            radius = radiusKey / 256f;
            softness = softnessKey / 256f;
            float inner = Mathf.Max(0f, radius - softness);
            float radiusSquared = radius * radius;
            float innerSquared = inner * inner;
            int extent = Mathf.CeilToInt(radius);
            List<SightKernelSample> samples = new List<SightKernelSample>();
            for (int deltaY = -extent; deltaY <= extent; deltaY++)
            {
                float deltaYSquared = deltaY * deltaY;
                for (int deltaX = -extent; deltaX <= extent; deltaX++)
                {
                    float distanceSquared = deltaX * deltaX + deltaYSquared;
                    if (distanceSquared > radiusSquared)
                    {
                        continue;
                    }
                    float sight = 1f;
                    if (distanceSquared > innerSquared)
                    {
                        float distance = Mathf.Sqrt(distanceSquared);
                        sight = 1f - Mathf.SmoothStep(
                            0f, 1f, (distance - inner) / softness);
                    }
                    samples.Add(new SightKernelSample
                    {
                        DeltaX = (short)deltaX,
                        DeltaY = (short)deltaY,
                        Sight = sight
                    });
                }
            }
            cached = samples.ToArray();
            _sightKernelCache[key] = cached;
            return cached;
        }

        private bool TryMapToFogPixel(
            int mapX, int mapY, float scale, out float pixelX, out float pixelY)
        {
            pixelX = 0f;
            pixelY = 0f;
            if (_activeTiles == null || mapX < 0 || mapY < 0 ||
                mapX >= _activeTiles.GetLength(0) || mapY >= _activeTiles.GetLength(1))
            {
                return false;
            }

            GameMapTile tile = _activeTiles[mapX, mapY];
            if (tile == null)
            {
                return false;
            }

            pixelX = (tile.gameMapX - _mapOffset) * scale;
            pixelY = (tile.gameMapY - _mapOffset) * scale;
            return true;
        }

        private void UpdateMeshGeometry()
        {
            if (_fogMesh == null || _activeMap == null || TilemapManager.instance == null)
            {
                return;
            }

            int last = _mapOffset + _mapSize - 1;
            Vector3 p00 = GameToWorld(_mapOffset, _mapOffset);
            Vector3 p10 = GameToWorld(last, _mapOffset);
            Vector3 p01 = GameToWorld(_mapOffset, last);
            Vector3 p11 = GameToWorld(last, last);

            _fogMesh.Clear();
            _fogMesh.vertices = new[] { p00, p10, p01, p11 };
            // Logic endpoints represent texel centres, not the outer texture edges.
            float uvMin = 0.5f / _resolution;
            float uvMax = 1f - uvMin;
            _fogMesh.uv = new[]
            {
                new Vector2(uvMin, uvMin), new Vector2(uvMax, uvMin),
                new Vector2(uvMin, uvMax), new Vector2(uvMax, uvMax)
            };
            _fogMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            _fogMesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            _fogMesh.RecalculateBounds();
        }

        private Vector3 GameToWorld(int gameX, int gameY)
        {
            int tileX;
            int tileY;
            _activeMap.mapGameTileToTilemapCoord(gameX, gameY, out tileX, out tileY);
            return TilemapManager.instance.gameTileMap.GetCellCenterWorld(new Vector3Int(tileX, tileY, 0));
        }

        private void ApplyDynamicVisibility()
        {
            Dictionary<int, Chimp> units = GetDictionary<int, Chimp>(ChimpsField);
            if (units != null)
            {
                _targetableUnitBuffer.Clear();
                foreach (Chimp sprite in units.Values)
                {
                    ApplySpriteVisibility(sprite);
                    if (sprite != null &&
                        (IsFriendlyUnit(sprite) ||
                         IsCurrentlyVisible(sprite.mapX, sprite.mapY)))
                        _targetableUnitBuffer.Add(sprite.objectID);
                }
                _targetableUnitIdsSnapshot =
                    TargetableUnitSnapshot.CreateIfChanged(
                        _targetableUnitBuffer, _targetableUnitIdsSnapshot);
            }

            Dictionary<int, BuildingAnim> buildings = GetDictionary<int, BuildingAnim>(BuildingsField);
            if (buildings != null)
            {
                foreach (BuildingAnim sprite in buildings.Values)
                {
                    ApplySpriteVisibility(sprite);
                }
            }

            Dictionary<int, Fly> flies = GetDictionary<int, Fly>(FliesField);
            if (flies != null)
            {
                foreach (Fly sprite in flies.Values)
                {
                    ApplySpriteVisibility(sprite);
                }
            }

            Dictionary<int, Org> orgs = GetDictionary<int, Org>(OrgsField);
            if (orgs != null)
            {
                foreach (Org sprite in orgs.Values)
                {
                    ApplySpriteVisibility(sprite);
                }
            }

            Dictionary<int, Pixie> pixies = GetDictionary<int, Pixie>(PixiesField);
            if (pixies != null)
            {
                foreach (Pixie sprite in pixies.Values)
                {
                    ApplySpriteVisibility(sprite);
                }
            }
        }

        private void ApplySpriteVisibility(GameSprite sprite)
        {
            if (sprite == null)
            {
                return;
            }
            bool forceOff = !IsCurrentlyVisible(sprite.mapX, sprite.mapY);
            ForceRenderersOff(sprite, forceOff);
            SyncDynamicRenderer(sprite.sprRenderer, forceOff);
            Chimp unit = sprite as Chimp;
            if (unit != null)
            {
                SyncDynamicRenderer(unit.sprRenderer2, forceOff);
                SyncDynamicRenderer(unit.sprRenderer3, forceOff);
                SyncDynamicRenderer(unit.sprRenderer4, forceOff);
                SyncDynamicRenderer(unit.sprRenderer5, forceOff);
                SyncDynamicRenderer(unit.sprRenderer6, forceOff);
                SyncDynamicRenderer(unit.sprRenderer7, forceOff);
            }
        }

        private void SyncDynamicRenderer(Renderer renderer, bool forceOff)
        {
            if (renderer == null)
            {
                return;
            }
            if (renderer.forceRenderingOff != forceOff)
            {
                renderer.forceRenderingOff = forceOff;
            }
            if (forceOff)
            {
                _forcedOff.Add(renderer);
            }
            else
            {
                _forcedOff.Remove(renderer);
            }
        }

        private bool IsCurrentlyVisible(int mapX, int mapY)
        {
            float scale = (_resolution - 1f) / Math.Max(1f, _mapSize - 1f);
            float pixelX;
            float pixelY;
            if (!TryMapToFogPixel(mapX, mapY, scale, out pixelX, out pixelY))
            {
                return false;
            }
            int x = Mathf.RoundToInt(pixelX);
            int y = Mathf.RoundToInt(pixelY);
            if (x < 0 || y < 0 || x >= _resolution || y >= _resolution)
            {
                return false;
            }
            return _visible[y * _resolution + x] >= VisibleThreshold;
        }

        private bool TryLogicToFogIndex(int logicX, int logicY, out int index)
        {
            index = -1;
            if (_visible == null || _resolution <= 0 || _mapSize <= 0 ||
                logicX < _mapOffset || logicY < _mapOffset ||
                logicX >= _mapOffset + _mapSize || logicY >= _mapOffset + _mapSize)
            {
                return false;
            }
            float scale = (_resolution - 1f) / Math.Max(1f, _mapSize - 1f);
            int x = Mathf.RoundToInt((logicX - _mapOffset) * scale);
            int y = Mathf.RoundToInt((logicY - _mapOffset) * scale);
            if (x < 0 || y < 0 || x >= _resolution || y >= _resolution)
            {
                return false;
            }
            index = y * _resolution + x;
            return true;
        }

        private bool IsLogicCurrentlyVisible(int logicX, int logicY)
        {
            int index;
            return TryLogicToFogIndex(logicX, logicY, out index) &&
                _visible[index] >= VisibleThreshold;
        }

        private bool IsLogicExplored(int logicX, int logicY)
        {
            int index;
            return TryLogicToFogIndex(logicX, logicY, out index) &&
                _explored != null && _explored[index] > 0;
        }

        private void EnsureFullMapRadar()
        {
            if (_activeMap == null || RadarTextureSizeField == null ||
                SetRadarTexturePixelSizeMethod == null)
            {
                return;
            }
            int targetSize = Math.Min(GameMap.RAW_MAP_SIZE, Math.Max(124, _mapSize - 4));
            int currentSize = (int)RadarTextureSizeField.GetValue(_activeMap);
            if (currentSize != targetSize)
            {
                SetRadarTexturePixelSizeMethod.Invoke(
                    _activeMap, new object[] { targetSize });
            }
            if (!_loggedFullMapRadar && currentSize == targetSize)
            {
                _loggedFullMapRadar = true;
                Logger.LogInfo(string.Format(
                    "Full-map radar locked: map={0}, radar={1}, clickScale={2:0.000}.",
                    _mapSize, targetSize, targetSize / 124f));
            }
        }

        private static void ExpandNativeRenderBounds(GameMap map)
        {
            if (ReferenceEquals(_instance, null) || map == null ||
                _instance._activeMap != map || RenderBoundsLeftField == null ||
                RenderBoundsRightField == null || RenderBoundsTopField == null ||
                RenderBoundsBottomField == null)
            {
                return;
            }
            int margin = Mathf.CeilToInt(
                _instance._unitSightRadius + _instance._softEdgeWidth);
            int mapSize = Math.Max(1, GameMap.tilemapSize);
            FieldInfo[] fields = RenderBoundsFields;
            for (int index = 0; index < fields.Length; index++)
                _instance._nativeBounds[index] = (int)fields[index].GetValue(map);
            RenderBoundsLeftField.SetValue(map, Math.Max(
                1, (int)RenderBoundsLeftField.GetValue(map) - margin));
            RenderBoundsRightField.SetValue(map, Math.Min(
                mapSize, (int)RenderBoundsRightField.GetValue(map) + margin));
            RenderBoundsTopField.SetValue(map, Math.Max(
                1, (int)RenderBoundsTopField.GetValue(map) - margin));
            RenderBoundsBottomField.SetValue(map, Math.Min(
                mapSize, (int)RenderBoundsBottomField.GetValue(map) + margin));
            for (int index = 0; index < fields.Length; index++)
                _instance._expandedBounds[index] = (int)fields[index].GetValue(map);
            _instance._expandedBoundsMap = map;
        }

        internal static void RestoreNativeRenderBounds(GameMap map)
        {
            if (ReferenceEquals(_instance, null) || map == null ||
                !ReferenceEquals(_instance._expandedBoundsMap, map)) return;
            FieldInfo[] fields = RenderBoundsFields;
            // Restore only values still owned by this Mod. The original callback
            // may then recalculate them or deliberately retain its native bounds.
            for (int index = 0; index < fields.Length; index++)
            {
                int current = (int)fields[index].GetValue(map);
                int restored = RenderBoundsPolicy.Restore(current,
                    _instance._expandedBounds[index], _instance._nativeBounds[index]);
                if (current != restored) fields[index].SetValue(map, restored);
            }
            _instance._expandedBoundsMap = null;
        }

        internal static void KeepFullMapRadar(GameMap map)
        {
            if (!ReferenceEquals(_instance, null) && _instance._activeMap == map)
            {
                _instance.EnsureFullMapRadar();
            }
        }

        internal static bool PreferFullMapRadarOnReset(bool configuredZoomOut)
        {
            // Only affects resetRadarZoom's local branch, never the saved setting.
            // _activeMap is intentionally not required: newMapLoaded resets radar
            // before the fog map is initialized.
            return (!ReferenceEquals(_instance, null) && _instance._enabled) || configuredZoomOut;
        }

        internal static bool PreventRadarZoom(GameMap map)
        {
            if (ReferenceEquals(_instance, null) || !_instance._enabled ||
                !ReferenceEquals(_instance._activeMap, map))
            {
                return true;
            }
            _instance.EnsureFullMapRadar();
            return false;
        }

        internal static void ExtendRenderBounds(GameMap map)
        {
            ExpandNativeRenderBounds(map);
        }

        private void FilterFrameData(
            GameMap map, EngineInterface.PlayState state, ref short[] sourceMap,
            ref int numElements, ref byte[] radarMap)
        {
            UpdateVisionPlayers(state);
            int frameRotation = ReadRotationIndex(
                CurrentRotationField, map, _activeRotationIndex);
            int applyingRotation;
            bool rotationApplying = TryGetApplyingRotation(
                map, state, out applyingRotation);
            if (rotationApplying)
            {
                frameRotation = applyingRotation;
            }
            long tileFilterStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            FilterHiddenTileUpdates(
                ref sourceMap, ref numElements, frameRotation, rotationApplying);
            double tileFilterMilliseconds = ElapsedMilliseconds(tileFilterStarted);
            _profileTileFilterMilliseconds += tileFilterMilliseconds;
            _profileTileFilterMaxMilliseconds = Math.Max(
                _profileTileFilterMaxMilliseconds, tileFilterMilliseconds);
            _profileTileFilterSamples++;
            FilterRadarMap(ref radarMap, frameRotation);
        }

        private void FilterHiddenTileUpdates(
            ref short[] sourceMap, ref int numElements,
            int frameRotation, bool rotationApplying)
        {
            if (sourceMap == null || numElements < 0 || _visible == null ||
                frameRotation < 0 || frameRotation >= RotationCount)
            {
                return;
            }
            Dictionary<long, short[]> hiddenTileUpdates =
                _hiddenTileUpdatesByRotation[frameRotation];
            int inputCount = Math.Min(numElements, sourceMap.Length / MapUpdateWidth);
            int fullSnapshotSize = _mapSize * _mapSize;
            if (!_initialMapSnapshotAccepted)
            {
                if (inputCount >= fullSnapshotSize)
                {
                    _initialMapSnapshotAccepted = true;
                    _tileFilterReadyAt = Time.unscaledTime + InitialMapSettleSeconds;
                    Logger.LogInfo(string.Format(
                        "Initial map snapshot accepted without fog filtering: tiles={0}; settling={1:0.0}s.",
                        inputCount, InitialMapSettleSeconds));
                }
                RememberDisplayedTileUpdates(sourceMap, inputCount, frameRotation);
                return;
            }
            if (!rotationApplying && Time.unscaledTime < _tileFilterReadyAt)
            {
                RememberDisplayedTileUpdates(sourceMap, inputCount, frameRotation);
                return;
            }
            int hidden = 0;
            for (int rowIndex = 0; rowIndex < inputCount; rowIndex++)
            {
                int offset = rowIndex * MapUpdateWidth;
                if (sourceMap[offset] != 0)
                {
                    continue;
                }

                int logicX = sourceMap[offset + 1];
                int logicY = sourceMap[offset + 2];
                if (!IsLogicCoordinateOnMap(logicX, logicY) ||
                    IsLogicCurrentlyVisible(logicX, logicY))
                {
                    RememberDisplayedTileUpdate(sourceMap, offset, frameRotation);
                    hiddenTileUpdates.Remove(MapUpdateKey(logicX, logicY));
                    continue;
                }

                long key = MapUpdateKey(logicX, logicY);
                short[] update;
                if (!hiddenTileUpdates.TryGetValue(key, out update))
                {
                    update = new short[MapUpdateWidth];
                    hiddenTileUpdates.Add(key, update);
                }
                Array.Copy(sourceMap, offset, update, 0, MapUpdateWidth);
                hidden++;
            }

            _revealedHiddenTileKeys.Clear();
            if (rotationApplying ||
                _lastRevealVisibilityGenerationByRotation[frameRotation] !=
                    _visibilityGeneration)
            {
                foreach (KeyValuePair<long, short[]> entry in hiddenTileUpdates)
                {
                    int logicX = (int)(entry.Key >> 32);
                    int logicY = (int)entry.Key;
                    if (IsLogicCurrentlyVisible(logicX, logicY))
                    {
                        _revealedHiddenTileKeys.Add(entry.Key);
                    }
                }
                _lastRevealVisibilityGenerationByRotation[frameRotation] =
                    _visibilityGeneration;
            }

            if (hidden == 0 && _revealedHiddenTileKeys.Count == 0 &&
                !rotationApplying)
            {
                return;
            }

            int visibleInputCount = Math.Max(0, inputCount - hidden);
            List<short> output = new List<short>(
                (visibleInputCount + _revealedHiddenTileKeys.Count) *
                    MapUpdateWidth);
            for (int rowIndex = 0; rowIndex < inputCount; rowIndex++)
            {
                int offset = rowIndex * MapUpdateWidth;
                if (sourceMap[offset] == 0)
                {
                    int logicX = sourceMap[offset + 1];
                    int logicY = sourceMap[offset + 2];
                    if (IsLogicCoordinateOnMap(logicX, logicY) &&
                        !IsLogicCurrentlyVisible(logicX, logicY))
                    {
                        continue;
                    }
                }
                AppendMapUpdate(output, sourceMap, offset);
            }

            int rotationReplays = rotationApplying
                ? AppendRotationDisplayedMapUpdates(output, frameRotation)
                : 0;
            int rotationMissing = rotationApplying
                ? CountMissingRotationHistory(frameRotation)
                : 0;
            foreach (long key in _revealedHiddenTileKeys)
            {
                short[] update;
                if (!hiddenTileUpdates.TryGetValue(key, out update))
                {
                    continue;
                }
                AppendMapUpdate(output, update, 0);
                RememberDisplayedTileUpdate(update, 0, frameRotation);
                hiddenTileUpdates.Remove(key);
            }

            sourceMap = output.ToArray();
            numElements = output.Count / MapUpdateWidth;
            if (!_loggedTileFilter &&
                (hidden > 0 || _revealedHiddenTileKeys.Count > 0))
            {
                _loggedTileFilter = true;
                Logger.LogInfo(string.Format(
                    "Hidden tile updates are filtered: deferred={0}, revealed={1}.",
                    hidden, _revealedHiddenTileKeys.Count));
            }
            if (rotationApplying)
            {
                Logger.LogInfo(string.Format(
                    "Rotation history replay: rotation={0}, cached={1}, missing={2}, deferred={3}, visible={4}.",
                    frameRotation, rotationReplays, rotationMissing,
                    hidden, _revealedHiddenTileKeys.Count));
            }
        }

        private void RememberDisplayedTileUpdates(
            short[] sourceMap, int inputCount, int rotation)
        {
            for (int rowIndex = 0; rowIndex < inputCount; rowIndex++)
            {
                RememberDisplayedTileUpdate(
                    sourceMap, rowIndex * MapUpdateWidth, rotation);
            }
        }

        private void RememberDisplayedTileUpdate(
            short[] source, int offset, int rotation)
        {
            if (source == null || offset < 0 || offset + MapUpdateWidth > source.Length ||
                source[offset] != 0 || rotation < 0 || rotation >= RotationCount ||
                _displayedTileUpdatesByRotation == null ||
                _displayedTileUpdateValidByRotation == null ||
                !EnsureDisplayedTileHistory(rotation))
            {
                return;
            }
            short[] updates = _displayedTileUpdatesByRotation[rotation];
            byte[] valid = _displayedTileUpdateValidByRotation[rotation];
            if (updates == null || valid == null)
            {
                return;
            }
            int tileIndex;
            if (!TryLogicToTileUpdateIndex(
                source[offset + 1], source[offset + 2], out tileIndex))
            {
                return;
            }
            Array.Copy(
                source, offset, updates,
                tileIndex * MapUpdateWidth, MapUpdateWidth);
            valid[tileIndex] = 1;
        }

        private bool EnsureDisplayedTileHistory(int rotation)
        {
            if (rotation < 0 || rotation >= RotationCount ||
                _displayedTileUpdatesByRotation == null ||
                _displayedTileUpdateValidByRotation == null)
            {
                return false;
            }
            if (_displayedTileUpdatesByRotation[rotation] == null)
            {
                _displayedTileUpdatesByRotation[rotation] =
                    new short[_mapSize * _mapSize * MapUpdateWidth];
                _displayedTileUpdateValidByRotation[rotation] =
                    new byte[_mapSize * _mapSize];
            }
            return true;
        }

        private bool AppendDisplayedMapUpdate(
            List<short> output, int logicX, int logicY, int rotation)
        {
            int tileIndex;
            if (rotation < 0 || rotation >= RotationCount ||
                _displayedTileUpdatesByRotation == null ||
                _displayedTileUpdateValidByRotation == null ||
                _displayedTileUpdatesByRotation[rotation] == null ||
                _displayedTileUpdateValidByRotation[rotation] == null ||
                !TryLogicToTileUpdateIndex(logicX, logicY, out tileIndex) ||
                _displayedTileUpdateValidByRotation[rotation][tileIndex] == 0)
            {
                return false;
            }
            AppendMapUpdate(
                output, _displayedTileUpdatesByRotation[rotation],
                tileIndex * MapUpdateWidth);
            return true;
        }

        private int AppendRotationDisplayedMapUpdates(
            List<short> output, int rotation)
        {
            int appended = 0;
            int last = _mapOffset + _mapSize;
            for (int logicY = _mapOffset; logicY < last; logicY++)
            {
                for (int logicX = _mapOffset; logicX < last; logicX++)
                {
                    if (!IsLogicCurrentlyVisible(logicX, logicY) &&
                        AppendDisplayedMapUpdate(output, logicX, logicY, rotation))
                    {
                        appended++;
                    }
                }
            }
            return appended;
        }

        private int CountMissingRotationHistory(int rotation)
        {
            if (rotation < 0 || rotation >= RotationCount ||
                _displayedTileUpdateValidByRotation == null ||
                _displayedTileUpdateValidByRotation[rotation] == null)
            {
                return 0;
            }
            int missing = 0;
            byte[] valid = _displayedTileUpdateValidByRotation[rotation];
            int last = _mapOffset + _mapSize;
            for (int logicY = _mapOffset; logicY < last; logicY++)
            {
                for (int logicX = _mapOffset; logicX < last; logicX++)
                {
                    int tileIndex;
                    if (!IsLogicCurrentlyVisible(logicX, logicY) &&
                        TryLogicToTileUpdateIndex(logicX, logicY, out tileIndex) &&
                        valid[tileIndex] == 0)
                    {
                        missing++;
                    }
                }
            }
            return missing;
        }

        private bool TryLogicToTileUpdateIndex(
            int logicX, int logicY, out int index)
        {
            index = -1;
            if (!IsLogicCoordinateOnMap(logicX, logicY))
            {
                return false;
            }
            index = (logicY - _mapOffset) * _mapSize + logicX - _mapOffset;
            return index >= 0 && index < _mapSize * _mapSize;
        }

        private byte[] EnsureObservedFogPixels(int rotation)
        {
            if (rotation < 0 || rotation >= RotationCount ||
                _observedFogPixelsByRotation == null || _visible == null)
            {
                return null;
            }
            byte[] observed = _observedFogPixelsByRotation[rotation];
            if (observed == null || observed.Length != _visible.Length)
            {
                observed = new byte[_visible.Length];
                _observedFogPixelsByRotation[rotation] = observed;
            }
            return observed;
        }

        private static void AppendMapUpdate(List<short> output, short[] source, int offset)
        {
            for (int index = 0; index < MapUpdateWidth; index++)
            {
                output.Add(source[offset + index]);
            }
        }

        private static long MapUpdateKey(int logicX, int logicY)
        {
            return ((long)logicX << 32) | (uint)logicY;
        }

        private bool IsLogicCoordinateOnMap(int logicX, int logicY)
        {
            return logicX >= _mapOffset && logicY >= _mapOffset &&
                logicX < _mapOffset + _mapSize && logicY < _mapOffset + _mapSize;
        }

        private static bool TryGetApplyingRotation(
            GameMap map, EngineInterface.PlayState state, out int rotation)
        {
            rotation = -1;
            if (map == null || state == null || state.rotateHappened <= 0 ||
                PendingRotationField == null)
            {
                return false;
            }
            rotation = ReadRotationIndex(PendingRotationField, map, -1);
            return rotation >= 0;
        }

        private static int ReadRotationIndex(
            FieldInfo field, object instance, int fallback)
        {
            if (field == null || instance == null)
            {
                return fallback;
            }
            try
            {
                int direction = Convert.ToInt32(field.GetValue(instance));
                if (direction >= 1 && direction <= 7 && (direction & 1) == 1)
                {
                    return (direction - 1) / 2;
                }
            }
            catch
            {
            }
            return fallback;
        }

        private void HandleMapRotationFinished(GameMap map)
        {
            if (_activeMap != map)
            {
                return;
            }
            _activeRotationIndex = ReadRotationIndex(
                CurrentRotationField, map, _activeRotationIndex);
            _localAnchorResolved = false;
            _localAnchor = Vector2Int.zero;
            _structureSources.Clear();
            _towerSources.Clear();
            _friendlyStructurePositions.Clear();
            _friendlyTowerTypes.Clear();
            _friendlyUnitIds.Clear();
            _unitVisionStampKeys.Clear();
            _friendlyUnitSources.Clear();
            _targetableUnitIdsSnapshot = null;
            RestoreAllRenderers();
            _rendererCache.Clear();
            _radarFogIndices = null;
            _radarMappingWidth = -1;
            _radarMappingHeight = -1;
            _radarMappingRotation = -1;
            _radarSourceMap = null;
            _cachedFilteredRadarMap = null;
            _cachedRadarWidth = -1;
            _cachedRadarHeight = -1;
            _cachedRadarRotation = -1;
            _nextRadarFogUpdate = 0f;
            _nextStructureScan = 0f;
            _nextFogUpdate = 0f;
            UpdateMeshGeometry();
            Logger.LogInfo(string.Format(
                "Map rotation completed; active history orientation={0}.",
                _activeRotationIndex));
        }

        private void FilterRadarMap(ref byte[] radarMap, int frameRotation)
        {
            if (radarMap == null || radarMap.Length < 4 || _visible == null ||
                _activeMap == null)
            {
                return;
            }
            int width = _activeMap.RadarMapWidth;
            int height = _activeMap.RadarMapHeight;
            long requiredBytes = (long)width * height * 4;
            if (width <= 1 || height <= 1 || requiredBytes > radarMap.Length)
            {
                return;
            }
            int requiredByteCount = (int)requiredBytes;
            bool sourceIsFiltered = IsRadarFilterBuffer(radarMap);
            if (!sourceIsFiltered)
            {
                if (_radarSourceMap == null ||
                    _radarSourceMap.Length != radarMap.Length)
                {
                    _radarSourceMap = new byte[radarMap.Length];
                }
                Buffer.BlockCopy(
                    radarMap, 0, _radarSourceMap, 0, requiredByteCount);
            }
            if (_radarSourceMap == null ||
                _radarSourceMap.Length < requiredByteCount)
            {
                return;
            }

            bool cachedFrameMatches = _cachedFilteredRadarMap != null &&
                _cachedFilteredRadarMap.Length == radarMap.Length &&
                _cachedRadarWidth == width && _cachedRadarHeight == height &&
                _cachedRadarRotation == frameRotation;
            if (cachedFrameMatches &&
                Time.unscaledTime < _nextRadarFogUpdate)
            {
                radarMap = _cachedFilteredRadarMap;
                return;
            }

            long profileStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            if (!EnsureRadarFogIndexMap(width, height, frameRotation))
            {
                return;
            }
            byte[] filtered = GetRadarFilterBuffer(radarMap);
            Buffer.BlockCopy(
                _radarSourceMap, 0, filtered, 0, requiredByteCount);
            int pixelCount = width * height;
            for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
            {
                int fogIndex = _radarFogIndices[pixelIndex];
                if (fogIndex < 0 || _visible[fogIndex] >= VisibleThreshold)
                {
                    continue;
                }
                int offset = pixelIndex * 4;
                byte[][] blend = _explored[fogIndex] > 0
                    ? _radarExploredBlend
                    : _radarUnexploredBlend;
                filtered[offset] = blend[0][_radarSourceMap[offset]];
                filtered[offset + 1] = blend[1][_radarSourceMap[offset + 1]];
                filtered[offset + 2] = blend[2][_radarSourceMap[offset + 2]];
            }
            PaintRadarUnitMarkers(filtered, width, height);
            radarMap = filtered;
            _cachedFilteredRadarMap = filtered;
            _radarOutputGeneration++;
            _cachedRadarWidth = width;
            _cachedRadarHeight = height;
            _cachedRadarRotation = frameRotation;
            _nextRadarFogUpdate = Time.unscaledTime + 1f / _radarUpdatesPerSecond;
            _profileRadarMilliseconds += ElapsedMilliseconds(profileStarted);
            _profileRadarSamples++;
            if (!_loggedRadarFilter)
            {
                _loggedRadarFilter = true;
                Logger.LogInfo(string.Format(
                    "Radar fog filtering active at {0}x{1}; bufferCapacity={2} pixels; activeCopy={3} bytes; updateRate={4:0.##}/s; projection cache active.",
                    width, height, radarMap.Length / 4, requiredBytes,
                    _radarUpdatesPerSecond));
            }
        }

        private void PaintRadarUnitMarkers(byte[] filtered, int width, int height)
        {
            int displaySize = FatControler.instance == null ? 124 : FatControler.instance.SHRadarRectSize;
            _radarUnitMarkers.Begin(displaySize > 1 ? displaySize : 124);
            _radarSelectedUnitIds.Clear();
            EngineInterface.PlayState state = GameData.Instance == null ? null : GameData.Instance.lastGameState;
            if (state != null && state.selectedChimps != null)
            {
                int count = Math.Min(state.numSelectedChimps, state.selectedChimps.Length);
                for (int i = 0; i < count; i++)
                    if (state.selectedChimps[i] > 0) _radarSelectedUnitIds.Add(state.selectedChimps[i]);
            }
            // Reuse the authoritative full-map snapshot gathered for vision, not
            // camera-local sprites or native radar colours. No new native scan.
            foreach (NativeVisionUnit unit in _nativeVisionUnits)
            {
                if (unit.Owner < 1 || unit.Owner > 8) continue;
                int fogIndex;
                if (!TryLogicToFogIndex(Mathf.RoundToInt(unit.LogicX),
                    Mathf.RoundToInt(unit.LogicY), out fogIndex)) continue;
                float x, y;
                if (!_radarMarkerProjection.TryProject(unit.LogicX, unit.LogicY, out x, out y)) continue;
                _radarUnitMarkers.Add(x, y, IsVisionPlayer(unit.Owner),
                    _visible[fogIndex] >= VisibleThreshold,
                    unit.Owner == _activePlayer && _radarSelectedUnitIds.Contains(unit.Id));
            }
            _radarUnitMarkers.Paint(filtered, width, height, _radarFogIndices, _visible, VisibleThreshold);
        }

        internal struct RadarSubmitState
        {
            internal SCDEFogOfWarPlugin Owner;
            internal Texture2D Texture;
            internal long Generation;
            internal long Started;
        }

        internal static bool BeginRadarSubmit(
            GameMap map, ref byte[] pending, out RadarSubmitState state)
        {
            state = new RadarSubmitState();
            SCDEFogOfWarPlugin owner = _instance;
            if (ReferenceEquals(owner, null) || !owner._enabled ||
                !ReferenceEquals(owner._activeMap, map) || pending == null ||
                !ReferenceEquals(pending, owner._cachedFilteredRadarMap))
                return true;

            Texture2D texture = map.getRadarTexture();
            // Generation, not array identity: the two output buffers are reused.
            if (owner._uploadedRadarGeneration == owner._radarOutputGeneration &&
                ReferenceEquals(owner._uploadedRadarTexture, texture) && texture != null)
            {
                // Consume the pending frame like the original, without uploading
                // identical pixels or constructing another Noesis TextureSource.
                pending = null;
                owner._radarUploadsSkipped++;
                return false;
            }
            state.Owner = owner;
            state.Texture = texture;
            state.Generation = owner._radarOutputGeneration;
            state.Started = System.Diagnostics.Stopwatch.GetTimestamp();
            return true;
        }

        internal static void EndRadarSubmit(GameMap map, RadarSubmitState state)
        {
            SCDEFogOfWarPlugin owner = state.Owner;
            if (ReferenceEquals(owner, null) || !ReferenceEquals(owner._activeMap, map))
                return;
            owner._uploadedRadarGeneration = state.Generation;
            owner._uploadedRadarTexture = state.Texture;
            owner._radarUploads++;
            owner._radarSubmitMilliseconds += ElapsedMilliseconds(state.Started);
        }

        private bool EnsureRadarFogIndexMap(
            int width, int height, int frameRotation)
        {
            int pixelCount = width * height;
            if (_radarFogIndices != null && _radarFogIndices.Length == pixelCount &&
                _radarMappingWidth == width && _radarMappingHeight == height &&
                _radarMappingRotation == frameRotation)
            {
                return true;
            }

            int[] mapping = new int[pixelCount];
            int radarLogicWidth = Math.Min(_mapSize, width);
            int radarLogicHeight = Math.Min(_mapSize, height);
            int radarStartLogicX = _mapOffset + Math.Max(0, (_mapSize - radarLogicWidth) / 2);
            int radarStartLogicY = _mapOffset + Math.Max(0, (_mapSize - radarLogicHeight) / 2);
            int radarEndLogicX = radarStartLogicX + radarLogicWidth - 1;
            int radarEndLogicY = radarStartLogicY + radarLogicHeight - 1;

            Vector3 radarP00 = GameToWorld(radarStartLogicX, radarStartLogicY);
            Vector3 radarP10 = GameToWorld(radarEndLogicX, radarStartLogicY);
            Vector3 radarP01 = GameToWorld(radarStartLogicX, radarEndLogicY);
            Vector3 radarP11 = GameToWorld(radarEndLogicX, radarEndLogicY);
            float projectedMinX = Mathf.Min(radarP00.x, radarP10.x, radarP01.x, radarP11.x);
            float projectedMaxX = Mathf.Max(radarP00.x, radarP10.x, radarP01.x, radarP11.x);
            float projectedMinY = Mathf.Min(radarP00.y, radarP10.y, radarP01.y, radarP11.y);
            float projectedMaxY = Mathf.Max(radarP00.y, radarP10.y, radarP01.y, radarP11.y);
            float radarMinX = Mathf.Lerp(projectedMinX, projectedMaxX, 0.25f);
            float radarMaxX = Mathf.Lerp(projectedMinX, projectedMaxX, 0.75f);
            float radarMinY = Mathf.Lerp(projectedMinY, projectedMaxY, 0.25f);
            float radarMaxY = Mathf.Lerp(projectedMinY, projectedMaxY, 0.75f);
            float radarAxisUX = radarP10.x - radarP00.x;
            float radarAxisUY = radarP10.y - radarP00.y;
            float radarAxisVX = radarP01.x - radarP00.x;
            float radarAxisVY = radarP01.y - radarP00.y;
            float radarDeterminant = radarAxisUX * radarAxisVY - radarAxisUY * radarAxisVX;
            if (Mathf.Abs(radarDeterminant) < 0.0001f ||
                radarMaxX <= radarMinX || radarMaxY <= radarMinY)
            {
                return false;
            }

            for (int y = 0; y < height; y++)
            {
                float worldY = Mathf.Lerp(radarMinY, radarMaxY, y / (height - 1f));
                for (int x = 0; x < width; x++)
                {
                    float worldX = Mathf.Lerp(radarMinX, radarMaxX, x / (width - 1f));
                    float deltaX = worldX - radarP00.x;
                    float deltaY = worldY - radarP00.y;
                    float fogU =
                        (deltaX * radarAxisVY - deltaY * radarAxisVX) / radarDeterminant;
                    float fogV =
                        (radarAxisUX * deltaY - radarAxisUY * deltaX) / radarDeterminant;
                    int logicX = Mathf.RoundToInt(Mathf.Lerp(
                        radarStartLogicX, radarEndLogicX, Mathf.Clamp01(fogU)));
                    int logicY = Mathf.RoundToInt(Mathf.Lerp(
                        radarStartLogicY, radarEndLogicY, Mathf.Clamp01(fogV)));
                    int fogIndex;
                    mapping[y * width + x] = TryLogicToFogIndex(
                        logicX, logicY, out fogIndex) ? fogIndex : -1;
                }
            }

            _radarFogIndices = mapping;
            _radarMarkerProjection = new RadarMarkerProjection
            {
                StartX = radarStartLogicX, StartY = radarStartLogicY,
                SpanX = radarEndLogicX - radarStartLogicX, SpanY = radarEndLogicY - radarStartLogicY,
                P00X = radarP00.x, P00Y = radarP00.y,
                UX = radarAxisUX, UY = radarAxisUY, VX = radarAxisVX, VY = radarAxisVY,
                MinX = radarMinX, MinY = radarMinY,
                Width = radarMaxX - radarMinX, Height = radarMaxY - radarMinY
            };
            _radarMappingWidth = width;
            _radarMappingHeight = height;
            _radarMappingRotation = frameRotation;
            Logger.LogInfo(string.Format(
                "Radar projection cache rebuilt: size={0}x{1}, rotation={2}, entries={3}.",
                width, height, frameRotation, mapping.Length));
            return true;
        }

        private byte[] GetRadarFilterBuffer(byte[] source)
        {
            int bufferIndex = _nextRadarFilterBuffer;
            if (ReferenceEquals(_radarFilterBuffers[bufferIndex], source))
            {
                bufferIndex = 1 - bufferIndex;
            }
            if (_radarFilterBuffers[bufferIndex] == null ||
                _radarFilterBuffers[bufferIndex].Length != source.Length)
            {
                _radarFilterBuffers[bufferIndex] = new byte[source.Length];
            }
            _nextRadarFilterBuffer = 1 - bufferIndex;
            return _radarFilterBuffers[bufferIndex];
        }

        private bool IsRadarFilterBuffer(byte[] candidate)
        {
            return ReferenceEquals(candidate, _radarFilterBuffers[0]) ||
                ReferenceEquals(candidate, _radarFilterBuffers[1]);
        }

        private int[] FilterVisibleUnitIds(int[] unitIds)
        {
            int[] filtered = TargetableUnitSnapshot.Filter(
                unitIds, _targetableUnitIdsSnapshot);
            if (!ReferenceEquals(filtered, unitIds) &&
                !_loggedSelectionFilter)
            {
                _loggedSelectionFilter = true;
                Logger.LogInfo(
                    "Hidden units are excluded from mouse targeting and selection through an immutable cross-thread snapshot.");
            }
            return filtered;
        }

        private void ForceRenderersOff(GameSprite sprite, bool forceOff)
        {
            if (sprite == null)
            {
                return;
            }
            bool previousState;
            if (_spriteHiddenState.TryGetValue(sprite, out previousState) &&
                previousState == forceOff)
            {
                return;
            }

            Renderer[] renderers;
            if (!_rendererCache.TryGetValue(sprite, out renderers))
            {
                List<Renderer> found = new List<Renderer>();
                Type type = sprite.GetType();
                while (type != null && typeof(GameSprite).IsAssignableFrom(type))
                {
                    FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
                    foreach (FieldInfo field in fields)
                    {
                        if (typeof(Renderer).IsAssignableFrom(field.FieldType))
                        {
                            Renderer renderer = field.GetValue(sprite) as Renderer;
                            if (renderer != null && !found.Contains(renderer))
                            {
                                found.Add(renderer);
                            }
                        }
                        else if (field.FieldType == typeof(GameObject))
                        {
                            GameObject gameObject = field.GetValue(sprite) as GameObject;
                            if (gameObject == null)
                            {
                                continue;
                            }
                            foreach (Renderer renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                            {
                                if (renderer != null && !found.Contains(renderer))
                                {
                                    found.Add(renderer);
                                }
                            }
                        }
                    }
                    type = type.BaseType;
                }
                renderers = found.ToArray();
                _rendererCache[sprite] = renderers;
            }

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                renderer.forceRenderingOff = forceOff;
                if (forceOff)
                {
                    _forcedOff.Add(renderer);
                }
                else
                {
                    _forcedOff.Remove(renderer);
                }
            }
            _spriteHiddenState[sprite] = forceOff;
        }

        private TDictionary GetDictionary<TKey, TValue, TDictionary>(FieldInfo field)
            where TDictionary : class
        {
            return field == null || _activeMap == null ? null : field.GetValue(_activeMap) as TDictionary;
        }

        private Dictionary<TKey, TValue> GetDictionary<TKey, TValue>(FieldInfo field)
        {
            return GetDictionary<TKey, TValue, Dictionary<TKey, TValue>>(field);
        }

        private void RestoreAllRenderers()
        {
            foreach (Renderer renderer in _forcedOff)
            {
                if (renderer != null)
                {
                    renderer.forceRenderingOff = false;
                }
            }
            _forcedOff.Clear();
            _spriteHiddenState.Clear();
        }

        private void ResetMapState()
        {
            _resourceAuditAt = 0f;
            Array.Clear(_scopeMilliseconds, 0, _scopeMilliseconds.Length);
            Array.Clear(_scopeSamples, 0, _scopeSamples.Length);
            _frameAuditStarted = 0;
            RestoreNativeRenderBounds(_expandedBoundsMap);
            RestoreAllRenderers();
            _rendererCache.Clear();
            DestroyFogObjects();
            _activeMap = null;
            _activeTiles = null;
            _activePlayer = -1;
            _mapSize = 0;
            _nextFogUpdate = 0f;
            _activeRotationIndex = 0;
            _coordinateAuditRotation = -1;
            _nextStructureScan = 0f;
            _nextTowerOccupancyScan = 0f;
            _nextRendererCachePrune = 0f;
            _nextPerformanceLog = 0f;
            _profileFogMilliseconds = 0.0;
            _profileFogMaxMilliseconds = 0.0;
            _profileBuildingMilliseconds = 0.0;
            _profileUnitMilliseconds = 0.0;
            _profileComposeMilliseconds = 0.0;
            _profileUploadMilliseconds = 0.0;
            _profileVisibilityMilliseconds = 0.0;
            _profileRadarMilliseconds = 0.0;
            _profileTileFilterMilliseconds = 0.0;
            _profileTileFilterMaxMilliseconds = 0.0;
            _profileFogSamples = 0;
            _profileVisibilitySamples = 0;
            _profileRadarSamples = 0;
            _profileTileFilterSamples = 0;
            _structureSources.Clear();
            _towerSources.Clear();
            _loggedFirstRefresh = false;
            _loggedSourceDiagnostics = false;
            _initialMapSnapshotAccepted = false;
            _tileFilterReadyAt = 0f;
            _localAnchorResolved = false;
            _localAnchor = Vector2Int.zero;
            _loggedRadarFilter = false;
            _loggedFullMapRadar = false;
            _loggedTileFilter = false;
            _loggedSelectionFilter = false;
            _visionPlayerIds.Clear();
            _friendlyUnitIds.Clear();
            _unitVisionStampKeys.Clear();
            _initialLordLogicByPlayer.Clear();
            _activeInitialLordVisionSources.Clear();
            _targetableUnitBuffer.Clear();
            _targetableUnitIdsSnapshot = null;
            _unitOwnerCache.Clear();
            _friendlyUnitSources.Clear();
            _nativeVisionUnits.Clear();
            _loggedNativeUnitVision = false;
            _loggedNativeUnitVisionFallback = false;
            _nativeVisionBuildings.Clear();
            _loggedNativeBuildingFallback = false;
            _friendlyStructureIds.Clear();
            _friendlyStructureOwners.Clear();
            _friendlyStructurePositions.Clear();
            _friendlyTowerTypes.Clear();
            _occupiedFriendlyTowerIds.Clear();
            _seededBasePlayers.Clear();
            _pendingPlacements.Clear();
            foreach (Dictionary<long, short[]> hiddenUpdates in
                _hiddenTileUpdatesByRotation)
            {
                hiddenUpdates.Clear();
            }
            _visible = null;
            _permanentInitialLordVision = null;
            _permanentInitialLordVisionDirty = true;
            _explored = null;
            _pixels = null;
            _fogColorLookup = null;
            _sightKernelCache.Clear();
            _radarFogIndices = null;
            _radarMappingWidth = -1;
            _radarMappingHeight = -1;
            _radarMappingRotation = -1;
            _radarFilterBuffers[0] = null;
            _radarUnitMarkers.Clear();
            _radarSelectedUnitIds.Clear();
            _radarMarkerProjection = new RadarMarkerProjection();
            _radarFilterBuffers[1] = null;
            _radarOutputGeneration = 0;
            _uploadedRadarGeneration = -1;
            _uploadedRadarTexture = null;
            _radarUploads = 0;
            _radarUploadsSkipped = 0;
            _radarSubmitMilliseconds = 0;
            _nextRadarFilterBuffer = 0;
            _radarSourceMap = null;
            _cachedFilteredRadarMap = null;
            _cachedRadarWidth = -1;
            _cachedRadarHeight = -1;
            _cachedRadarRotation = -1;
            _nextRadarFogUpdate = 0f;
            _displayedTileUpdatesByRotation = null;
            _displayedTileUpdateValidByRotation = null;
            _observedFogPixelsByRotation = null;
            _visibilityGeneration = 0;
            _revealedHiddenTileKeys.Clear();
            for (int rotation = 0; rotation < RotationCount; rotation++)
            {
                _lastRevealVisibilityGenerationByRotation[rotation] = -1;
            }
        }

        private void DestroyFogObjects()
        {
            if (_fogObject != null) Destroy(_fogObject);
            if (_fogMesh != null) Destroy(_fogMesh);
            if (_fogMaterial != null) Destroy(_fogMaterial);
            if (_fogTexture != null) Destroy(_fogTexture);
            _fogObject = null;
            _fogMesh = null;
            _fogMaterial = null;
            _fogTexture = null;
            _fogRenderer = null;
        }

        private void LoadPackedSettings()
        {
            _unexploredColor = ParseColor(DefaultUnexplored, DefaultUnexplored);
            _exploredColor = ParseColor(DefaultExplored, DefaultExplored);
            using (Stream stream = typeof(SCDEFogOfWarPlugin).Assembly.GetManifestResourceStream(PackedConfigResource))
            {
                if (stream == null)
                    throw new InvalidOperationException("Embedded fog settings are missing; rebuild the Mod.");
                using (StreamReader reader = new StreamReader(stream))
                {
                    string section = string.Empty;
                    string rawLine;
                    while ((rawLine = reader.ReadLine()) != null)
                    {
                        string line = rawLine.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        if (line.StartsWith("[") && line.EndsWith("]"))
                        {
                            section = line.Substring(1, line.Length - 2).Trim().ToLowerInvariant();
                            continue;
                        }
                        int equals = line.IndexOf('=');
                        if (equals <= 0) continue;
                        string key = section + "." + line.Substring(0, equals).Trim().ToLowerInvariant();
                        ApplyTomlValue(key, Unquote(line.Substring(equals + 1).Trim()));
                    }
                }
            }

            _unitSightRadius = Mathf.Clamp(_unitSightRadius, 1f, 80f);
            _civilianSightRadius = Mathf.Clamp(_civilianSightRadius, 1f, 80f);
            _buildingSightRadius = Mathf.Clamp(_buildingSightRadius, 1f, 80f);
            _wallSightRadius = Mathf.Clamp(_wallSightRadius, 1f, 80f);
            _initialLordBaseSightRadius = Mathf.Clamp(
                _initialLordBaseSightRadius, 1f, 400f);
            for (int index = 0; index < _towerSightRadii.Length; index++)
            {
                _towerSightRadii[index] = Mathf.Clamp(_towerSightRadii[index], 1f, 80f);
            }
            _softEdgeWidth = Mathf.Clamp(_softEdgeWidth, 0.1f, 30f);
            _updateInterval = Mathf.Clamp(_updateInterval, 0.03f, 1f);
            _radarUpdatesPerSecond = Mathf.Clamp(_radarUpdatesPerSecond, 1f, 30f);
            Logger.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "Embedded build settings: enabled={0}, unitRadius={1}, civilianRadius={2}, buildingRadius={3}, wallRadius={4}, towerRadii={5}/{6}/{7}/{8}/{9}, lordBaseRadius={10}, softEdge={11}, interval={12}, radarUpdatesPerSecond={13}.",
                _enabled, _unitSightRadius, _civilianSightRadius,
                _buildingSightRadius, _wallSightRadius,
                _towerSightRadii[0], _towerSightRadii[1], _towerSightRadii[2],
                _towerSightRadii[3], _towerSightRadii[4],
                _initialLordBaseSightRadius, _softEdgeWidth,
                _updateInterval, _radarUpdatesPerSecond));
        }

        private void ApplyTomlValue(string key, string value)
        {
            bool boolValue;
            float floatValue;
            switch (key)
            {
                case "general.enabled":
                    if (bool.TryParse(value, out boolValue)) _enabled = boolValue;
                    break;
                case "colors.unexplored":
                    _unexploredColor = ParseColor(value, DefaultUnexplored);
                    break;
                case "colors.explored":
                    _exploredColor = ParseColor(value, DefaultExplored);
                    break;
                case "vision.unit_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _unitSightRadius = floatValue;
                    break;
                case "civilian_vision.radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _civilianSightRadius = floatValue;
                    break;
                case "lord_base_vision.radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _initialLordBaseSightRadius = floatValue;
                    break;
                case "vision.building_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _buildingSightRadius = floatValue;
                    break;
                case "tower_vision.wall_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _wallSightRadius = floatValue;
                    break;
                case "tower_vision.tower1_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _towerSightRadii[0] = floatValue;
                    break;
                case "tower_vision.tower2_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _towerSightRadii[1] = floatValue;
                    break;
                case "tower_vision.tower3_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _towerSightRadii[2] = floatValue;
                    break;
                case "tower_vision.tower4_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _towerSightRadii[3] = floatValue;
                    break;
                case "tower_vision.tower5_radius":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _towerSightRadii[4] = floatValue;
                    break;
                case "vision.soft_edge":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _softEdgeWidth = floatValue;
                    break;
                case "vision.update_interval":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _updateInterval = floatValue;
                    break;
                case "minimap.fog_updates_per_second":
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue))
                        _radarUpdatesPerSecond = floatValue;
                    break;
            }
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2);
            }
            return value;
        }

        private static Color ParseColor(string value, string fallback)
        {
            Color color;
            if (!string.IsNullOrEmpty(value) && ColorUtility.TryParseHtmlString(value, out color))
            {
                return color;
            }
            ColorUtility.TryParseHtmlString(fallback, out color);
            return color;
        }

        internal static void NotifyMapLoaded(int mapSize)
        {
            if (!ReferenceEquals(_instance, null))
            {
                _instance._waitingForNextMatch = false;
                _instance._matchAuditId++;
                _instance._signaledMapSize = mapSize > 0 && mapSize <= GameMap.RAW_MAP_SIZE
                    ? mapSize
                    : 0;
                _instance._lastDriveFrame = -1;
                _instance.Logger.LogInfo(
                    "Map load signal observed: size=" + mapSize +
                    "; map identity polling will decide whether reinitialization is required.");
            }
        }

        internal static void NotifyMatchEnding()
        {
            if (ReferenceEquals(_instance, null)) return;
            _instance._waitingForNextMatch = true;
            _instance._signaledMapSize = 0;
            _instance.ResetMapState();
            _instance.Logger.LogInfo("Fog match state released; waiting for a new map load.");
        }

        internal static void NotifyMapRotationFinished(GameMap map)
        {
            if (!ReferenceEquals(_instance, null))
            {
                _instance.HandleMapRotationFinished(map);
            }
        }

        internal static void NotifyMapFrame(GameMap map)
        {
            if (!ReferenceEquals(_instance, null))
            {
                _instance.DriveFromMap(map);
            }
        }

        internal static void FilterMapFrame(
            GameMap map, EngineInterface.PlayState state, ref short[] sourceMap,
            ref int numElements, ref byte[] radarMap)
        {
            if (!ReferenceEquals(_instance, null) && _instance._enabled &&
                _instance._activeMap == map)
            {
                _instance.FilterFrameData(
                    map, state, ref sourceMap, ref numElements, ref radarMap);
            }
        }

        internal static void FilterTroopCandidates(
            ref int[] underCursorUnits, ref int[] onScreenUnits)
        {
            if (!ReferenceEquals(_instance, null) && _instance._enabled &&
                _instance._activeMap != null)
            {
                underCursorUnits = _instance.FilterVisibleUnitIds(underCursorUnits);
                onScreenUnits = _instance.FilterVisibleUnitIds(onScreenUnits);
            }
        }

        internal static void FilterGrabbedUnits(ref int[] units)
        {
            if (!ReferenceEquals(_instance, null) && _instance._enabled &&
                _instance._activeMap != null)
            {
                units = _instance.FilterVisibleUnitIds(units);
            }
        }

        internal static void NotifyDirectorFrame()
        {
            if (!ReferenceEquals(_instance, null))
            {
                if (!_instance._loggedDirectorLoop)
                {
                    _instance._loggedDirectorLoop = true;
                    _instance.Logger.LogInfo("Director.Update hook active; game-loop map polling started.");
                }
                _instance.DriveFromMap(GameMap.instance);
            }
        }

        internal static void NotifyLocalPlacement(
            int item, int logicX, int logicY, int player,
            bool inGameNotEditor, int mouseState)
        {
            if (ReferenceEquals(_instance, null) || !inGameNotEditor || mouseState <= 0 ||
                _instance._activeMap == null || !_instance.IsVisionPlayer(player))
            {
                return;
            }
            int structureType = GameData.getStructFromMapper(item);
            if (structureType <= 0)
            {
                return;
            }

            for (int index = 0; index < _instance._pendingPlacements.Count; index++)
            {
                PendingPlacement existing = _instance._pendingPlacements[index];
                if (existing.LogicX == logicX && existing.LogicY == logicY)
                {
                    existing.PlayerId = player;
                    existing.ExpiresAt = Time.unscaledTime + 6f;
                    _instance._pendingPlacements[index] = existing;
                    return;
                }
            }
            HashSet<int> existingStructureIds = new HashSet<int>();
            for (int scanY = logicY - PlacementClaimRadius;
                scanY <= logicY + PlacementClaimRadius; scanY++)
            {
                for (int scanX = logicX - PlacementClaimRadius;
                    scanX <= logicX + PlacementClaimRadius; scanX++)
                {
                    if (scanX < 0 || scanY < 0 ||
                        scanX >= GameMap.RAW_MAP_SIZE || scanY >= GameMap.RAW_MAP_SIZE)
                    {
                        continue;
                    }
                    int structureId =
                        EngineInterface.GetLayerDebug(scanX, scanY).structure_layer;
                    if (structureId > 0)
                    {
                        existingStructureIds.Add(structureId);
                    }
                }
            }
            _instance._pendingPlacements.Add(new PendingPlacement
            {
                PlayerId = player,
                LogicX = logicX,
                LogicY = logicY,
                ExistingStructureIds = existingStructureIds,
                ExpiresAt = Time.unscaledTime + 6f
            });
            _instance.Logger.LogInfo(string.Format(
                "Vision-team building placement captured: mapper={0}, structureType={1}, logic=({2},{3}), player={4}, mouseState={5}, priorStructures={6}.",
                item, structureType, logicX, logicY, player, mouseState,
                existingStructureIds.Count));
        }

    }

    internal sealed class FogOfWarRuntime : MonoBehaviour
    {
        internal SCDEFogOfWarPlugin Owner;

        private void Update()
        {
            SCDEFogOfWarPlugin owner = Owner;
            if (!ReferenceEquals(owner, null))
            {
                owner.RuntimeUpdate();
            }
        }

        private void OnDestroy()
        {
            SCDEFogOfWarPlugin owner = Owner;
            if (!ReferenceEquals(owner, null))
            {
                owner.RuntimeDestroyed(this);
            }
            Owner = null;
        }
    }

    [HarmonyPatch(typeof(GameMap), "finishPendingRotate")]
    internal static class FinishPendingRotatePatch
    {
        private static void Postfix(GameMap __instance)
        {
            SCDEFogOfWarPlugin.NotifyMapRotationFinished(__instance);
        }
    }

    [HarmonyPatch(typeof(GameMap), "ApplyRadarMap")]
    internal static class ApplyRadarMapPatch
    {
        private static bool Prefix(GameMap __instance, ref byte[] ___lastRadarMap,
            out SCDEFogOfWarPlugin.RadarSubmitState __state)
        {
            return SCDEFogOfWarPlugin.BeginRadarSubmit(
                __instance, ref ___lastRadarMap, out __state);
        }

        private static void Postfix(GameMap __instance,
            SCDEFogOfWarPlugin.RadarSubmitState __state)
        {
            SCDEFogOfWarPlugin.EndRadarSubmit(__instance, __state);
        }
    }

    [HarmonyPatch(typeof(GameMap), "resetRadarZoom")]
    internal static class ResetRadarZoomPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo getter = AccessTools.Method(typeof(ConfigSettings), "get_Settings_RadarDefaultZoomedOut");
            MethodInfo replacement = AccessTools.Method(typeof(SCDEFogOfWarPlugin), "PreferFullMapRadarOnReset");
            int matches = 0;
            foreach (CodeInstruction instruction in instructions)
            {
                yield return instruction;
                if (instruction.Calls(getter))
                {
                    matches++;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, replacement);
                }
            }
            if (matches != 1)
                throw new InvalidOperationException("Unexpected resetRadarZoom setting getter count: " + matches);
        }

        private static void Postfix(GameMap __instance)
        {
            SCDEFogOfWarPlugin.KeepFullMapRadar(__instance);
        }
    }

    [HarmonyPatch(typeof(GameMap), "changeRadarMapSize")]
    internal static class ChangeRadarMapSizePatch
    {
        private static bool Prefix(GameMap __instance)
        {
            return SCDEFogOfWarPlugin.PreventRadarZoom(__instance);
        }
    }

    [HarmonyPatch(typeof(GameMap), "PreCalcScreenCentre")]
    internal static class PreCalcScreenCentrePatch
    {
        private static void Prefix(GameMap __instance)
        {
            SCDEFogOfWarPlugin.RestoreNativeRenderBounds(__instance);
        }
        private static void Postfix(GameMap __instance)
        {
            SCDEFogOfWarPlugin.ExtendRenderBounds(__instance);
        }
    }

    [HarmonyPatch(typeof(GameMap), "cameraMovedRecalcBounds")]
    internal static class CameraMovedRecalcBoundsPatch
    {
        private static void Prefix(GameMap __instance)
        {
            SCDEFogOfWarPlugin.RestoreNativeRenderBounds(__instance);
        }
        private static void Postfix(GameMap __instance)
        {
            SCDEFogOfWarPlugin.ExtendRenderBounds(__instance);
        }
    }

    [HarmonyPatch(typeof(GameMap), "newMapLoaded")]
    internal static class MapLoadedPatch
    {
        private static void Prefix()
        {
            SCDEFogOfWarPlugin.NotifyMatchEnding();
        }
        private static void Postfix(int __0)
        {
            SCDEFogOfWarPlugin.NotifyMapLoaded(__0);
        }
    }

    [HarmonyPatch(typeof(EditorDirector), "stopGameSim")]
    internal static class StopGameSimPatch
    {
        private static void Prefix()
        {
            SCDEFogOfWarPlugin.NotifyMatchEnding();
        }
    }

    [HarmonyPatch(typeof(GameMap), "processTestMap")]
    internal static class ProcessTestMapPatch
    {
        private static void Prefix(
            GameMap __instance, ref short[] __0, ref int __1,
            EngineInterface.PlayState __2, ref byte[] __3, out long __state)
        {
            __state = System.Diagnostics.Stopwatch.GetTimestamp();
            SCDEFogOfWarPlugin.FilterMapFrame(
                __instance, __2, ref __0, ref __1, ref __3);
        }

        private static void Postfix(GameMap __instance, long __state)
        {
            SCDEFogOfWarPlugin.NotifyMapFrame(__instance);
            SCDEFogOfWarPlugin.RecordScope(2, __state);
        }
    }

    [HarmonyPatch(typeof(GameMap), "grabTroopsOnScreen")]
    internal static class GrabTroopsOnScreenPatch
    {
        private static void Postfix(ref int[] __result)
        {
            SCDEFogOfWarPlugin.FilterGrabbedUnits(ref __result);
        }
    }

    [HarmonyPatch(typeof(EngineInterface), "TroopSelection")]
    internal static class TroopSelectionPatch
    {
        private static void Prefix(ref int[] __6, ref int[] __10)
        {
            SCDEFogOfWarPlugin.FilterTroopCandidates(ref __6, ref __10);
        }
    }

    [HarmonyPatch(typeof(Director), "Update")]
    internal static class DirectorUpdatePatch
    {
        private static void Prefix(out long __state)
        {
            __state = System.Diagnostics.Stopwatch.GetTimestamp();
        }
        private static void Postfix(long __state)
        {
            SCDEFogOfWarPlugin.NotifyDirectorFrame();
            SCDEFogOfWarPlugin.RecordScope(1, __state);
        }
    }

    [HarmonyPatch(typeof(EngineInterface), "PlaceMapperItem")]
    internal static class PlaceMapperItemPatch
    {
        private static void Prefix(
            int __0, int __1, int __2, int __4, bool __5, int __7)
        {
            SCDEFogOfWarPlugin.NotifyLocalPlacement(
                __0, __1, __2, __4, __5, __7);
        }
    }

}
