using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;
using Vintagestory.ServerMods.NoObf;

namespace Apprentice
{
    /// <summary>
    /// Concentric-realms world generation. Level 0 is left untouched. Level 1
    /// is a hot, dry, land-only desert. Level 2 is a continuous salt-water
    /// crossing with deterministic deep-sea floor geometry and natural shore
    /// transitions. Level 3 is a land-only, maximum-density forest over
    /// difficult cliff-and-hill terrain. Level 4 is a dense, older forest with
    /// lower overall relief, deterministic canopy clearings and valley terrain.
    /// Level 5 is an open frozen ring with real glacier block layers, long
    /// sight lines, deterministic ice-spike fields and eased transitions at
    /// both realm boundaries. Level 6 is a warm, fresh-water poison mire with
    /// shallow wetlands, bog islands and eased transitions at both realm
    /// boundaries. Level 7 is a cold, exposed highland ring of broken ledged
    /// plateaus and stepped rifts with eased transitions at both realm
    /// boundaries. Later milestones extend this class one realm at a time.
    /// </summary>
    internal sealed partial class ConcentricRealmWorldgenSystem :
        IBlockPatchModifier,
        IDisposable
    {
        private const int DesertLevel = 1;
        private const int DeepSeaLevel = 2;
        private const int EndlessForestLevel = 3;
        private const int ShadowForestLevel = 4;
        private const int FrozenExpanseLevel = 5;
        private const int ChunkSize = GlobalConstants.ChunkSize;
        private const int DeepSeaFloorVariation = 6;
        // These are sea-level inputs. Vintage Story lowers temperature and
        // raises rainfall with altitude before choosing a tree variant. This
        // profile keeps broadleaf trees viable in valleys, conifers viable
        // on the middle slopes, and larch viable on the highest plateaus.
        private const int EndlessForestTemperatureCelsius = 14;
        private const int EndlessForestRainfall = 100;
        private const int EndlessForestDensity = 255;
        private const int EndlessForestShrubDensity = 224;
        private const int EndlessForestUpheaval = 255;
        private const string EndlessForestLandformCode =
            "cliffy rolling hills";
        // Shadow Forest keeps a continuous canopy outside deterministic dead
        // clearings. Its lower upheaval and valley landform make darkness and
        // navigation the main pressure instead of repeating Level 3 climbing.
        private const int ShadowForestTemperatureCelsius = 15;
        private const int ShadowForestRainfall = 130;
        private const int ShadowForestDensity = 192;
        private const int ShadowForestShrubDensity = 196;
        private const int ShadowForestUpheaval = 176;
        private const int ShadowForestOuterTransitionWidth = 128;
        private const int ShadowForestClearingCellSize = 384;
        private const int ShadowForestClearingMinimumRadius = 48;
        private const int ShadowForestClearingRadiusVariation = 32;
        private const string ShadowForestLandformCode =
            "flathillvalley";
        // -20 C selects Vintage Story's glacier block layers. The dedicated
        // cold-glaciers landform supplies broad ridges and depressions while
        // zero forest/shrub density preserves the intended long sight lines.
        private const int FrozenExpanseTemperatureCelsius = -20;
        private const int FrozenExpanseRainfall = 96;
        private const int FrozenExpanseUpheaval = 128;
        private const int FrozenExpanseTransitionWidth = 192;
        private const string FrozenExpanseLandformCode =
            "cold glaciers";
        private const string DesertMapMarker =
            "apprentice:concentricRealmsLevel1MapsV1";
        private const string DeepSeaMapMarker =
            "apprentice:concentricRealmsLevel2MapsV1";
        private const string EndlessForestMapMarker =
            "apprentice:concentricRealmsLevel3MapsV1";
        private const string ShadowForestMapMarker =
            "apprentice:concentricRealmsLevel4MapsV1";
        private const string FrozenExpanseMapMarker =
            "apprentice:concentricRealmsLevel5MapsV1";
        private const string PoisonMireMapMarker =
            "apprentice:concentricRealmsLevel6MapsV1";
        private const string ShatteredHighlandsMapMarker =
            "apprentice:concentricRealmsLevel7MapsV1";
        private const string RealmMapsPerformanceMarker =
            "apprentice:concentricRealmsMapsV5";
        private static readonly byte[] MapMarkerValue = { 1 };

        private static readonly BlockPatchConfig EmptyPatches = new()
        {
            Patches = Array.Empty<BlockPatch>(),
            PatchesNonTree = Array.Empty<BlockPatch>()
        };

        private readonly ICoreServerAPI api;
        private readonly DangerDefinition definition;
        private readonly FrozenExpanseIceSpikeGenerator iceSpikeGenerator;
        private readonly PoisonMireEnvironmentGenerator
            poisonMireEnvironmentGenerator;
        private readonly ShatteredHighlandsSurfaceGenerator
            shatteredHighlandsSurfaceGenerator;
        private readonly ShatteredHighlandsRuinsGenerator
            shatteredHighlandsRuinsGenerator;
        private readonly GenStructures? structures;
        private readonly ConcurrentDictionary<long, byte>
            terrainVerifiedRegions = new();
        private DangerWorldState? activeState;
        private GlobalConfig? globalConfig;
        private NormalizedSimplexNoise? deepSeaFloorNoise;
        private static int rewrittenDesertMapRegions;
        private static int rewrittenDeepSeaMapRegions;
        private static int rewrittenEndlessForestMapRegions;
        private static int rewrittenShadowForestMapRegions;
        private static int rewrittenFrozenExpanseMapRegions;
        private static int sculptedDeepSeaChunks;
        private static int sculptedDeepSeaColumns;
        private int endlessForestLandformIndex = -1;
        private int shadowForestLandformIndex = -1;
        private int frozenExpanseLandformIndex = -1;
        private int loggedDesertRegions;
        private int loggedDeepSeaRegions;
        private int loggedEndlessForestRegions;
        private int loggedShadowForestRegions;
        private int loggedFrozenExpanseRegions;
        private int loggedDeepSeaChunks;
        private readonly object frozenProbeGate = new();
        private FrozenProbeRun? activeFrozenProbe;
        private readonly object iceSpikeProbeGate = new();
        private IceSpikeProbeRun? activeIceSpikeProbe;
        private bool disposed;
        private bool conflictLogged;

        internal static int RewrittenDesertMapRegions =>
            System.Threading.Volatile.Read(
                ref rewrittenDesertMapRegions
            );

        internal static int RewrittenDeepSeaMapRegions =>
            System.Threading.Volatile.Read(
                ref rewrittenDeepSeaMapRegions
            );

        internal static int RewrittenEndlessForestMapRegions =>
            System.Threading.Volatile.Read(
                ref rewrittenEndlessForestMapRegions
            );

        internal static int RewrittenShadowForestMapRegions =>
            System.Threading.Volatile.Read(
                ref rewrittenShadowForestMapRegions
            );

        internal static int RewrittenFrozenExpanseMapRegions =>
            System.Threading.Volatile.Read(
                ref rewrittenFrozenExpanseMapRegions
            );

        internal static int SculptedDeepSeaChunks =>
            System.Threading.Volatile.Read(
                ref sculptedDeepSeaChunks
            );

        internal static int SculptedDeepSeaColumns =>
            System.Threading.Volatile.Read(
                ref sculptedDeepSeaColumns
            );

        internal ConcentricRealmWorldgenSystem(
            ICoreServerAPI api,
            ApprenticeContentRegistry registry)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            definition = registry?.Danger ??
                throw new ArgumentNullException(nameof(registry));
            iceSpikeGenerator =
                new FrozenExpanseIceSpikeGenerator(api);
            poisonMireEnvironmentGenerator =
                new PoisonMireEnvironmentGenerator(api);
            shatteredHighlandsSurfaceGenerator =
                new ShatteredHighlandsSurfaceGenerator(api);
            shatteredHighlandsRuinsGenerator =
                new ShatteredHighlandsRuinsGenerator(api);
            if (!definition.Enabled || !definition.RealmWorldgenEnabled)
            {
                return;
            }

            api.Event.InitWorldGenerator(OnInitWorldGenerator, "standard");
            api.Event.MapRegionGeneration(
                OnMapRegionGeneration,
                "standard"
            );
            api.Event.ChunkColumnGeneration(
                EnsureRealmMapsForChunk,
                EnumWorldGenPass.Terrain,
                "standard"
            );
            api.Event.ChunkColumnGeneration(
                SculptDeepSeaBeforeVegetation,
                EnumWorldGenPass.Vegetation,
                "standard"
            );
            api.Event.ChunkColumnGeneration(
                iceSpikeGenerator.OnChunkColumnGeneration,
                EnumWorldGenPass.Vegetation,
                "standard"
            );
            api.Event.GetWorldgenBlockAccessor(
                shatteredHighlandsRuinsGenerator
                    .OnWorldgenBlockAccessor
            );
            api.Event.ChunkColumnGeneration(
                shatteredHighlandsRuinsGenerator
                    .OnCityChunkColumnGeneration,
                EnumWorldGenPass.TerrainFeatures,
                "standard"
            );
            api.Event.ChunkColumnGeneration(
                poisonMireEnvironmentGenerator.OnChunkColumnGeneration,
                EnumWorldGenPass.NeighbourSunLightFlood,
                "standard"
            );
            api.Event.ChunkColumnGeneration(
                shatteredHighlandsSurfaceGenerator
                    .OnChunkColumnGeneration,
                EnumWorldGenPass.NeighbourSunLightFlood,
                "standard"
            );
            api.Event.ChunkColumnGeneration(
                shatteredHighlandsRuinsGenerator
                    .OnCorruptionChunkColumnGeneration,
                EnumWorldGenPass.NeighbourSunLightFlood,
                "standard"
            );
            api.Event.OnTrySpawnEntity += OnTrySpawnEntity;

            GenVegetationAndPatches? vegetation = api.ModLoader
                .GetModSystem<GenVegetationAndPatches>();
            vegetation?.RegisterPatchModifier(this);

            structures = api.ModLoader.GetModSystem<GenStructures>();
            if (structures != null)
            {
                structures.OnPreventSchematicPlaceAt +=
                    OnPreventSchematicPlaceAt;
            }
        }

        private void RewriteDeepSeaMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            int regionSize = api.WorldManager.RegionSize;
            int ocean = SetLevelMap(
                mapRegion.OceanMap,
                state,
                DeepSeaLevel,
                regionX,
                regionZ,
                regionSize,
                255
            );
            int upheaval = ClearLevelMap(
                mapRegion.UpheavelMap,
                state,
                DeepSeaLevel,
                regionX,
                regionZ,
                regionSize
            );
            int forest = ClearLevelMap(
                mapRegion.ForestMap,
                state,
                DeepSeaLevel,
                regionX,
                regionZ,
                regionSize
            );
            int shrubs = ClearLevelMap(
                mapRegion.ShrubMap,
                state,
                DeepSeaLevel,
                regionX,
                regionZ,
                regionSize
            );
            int biomes = ClearLevelMap(
                mapRegion.BiomeMap,
                state,
                DeepSeaLevel,
                regionX,
                regionZ,
                regionSize
            );
            mapRegion.SetModdata(DeepSeaMapMarker, MapMarkerValue);
            mapRegion.DirtyForSaving = true;

            if (ocean + upheaval + forest + shrubs + biomes <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(
                ref rewrittenDeepSeaMapRegions
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedDeepSeaRegions
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Rewrote Level 2 map region {0},{1}: ocean={2}, upheaval={3}, forest={4}, shrubs={5}, biomes={6}.",
                    regionX,
                    regionZ,
                    ocean,
                    upheaval,
                    forest,
                    shrubs,
                    biomes
                );
            }
        }

        private void RewriteEndlessForestMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            int regionSize = api.WorldManager.RegionSize;
            int climate = ApplyClimateMap(
                mapRegion.ClimateMap,
                state,
                EndlessForestLevel,
                regionX,
                regionZ,
                regionSize,
                EndlessForestTemperatureCelsius,
                EndlessForestRainfall
            );
            int forest = SetLevelMap(
                mapRegion.ForestMap,
                state,
                EndlessForestLevel,
                regionX,
                regionZ,
                regionSize,
                EndlessForestDensity
            );
            int shrubs = SetLevelMap(
                mapRegion.ShrubMap,
                state,
                EndlessForestLevel,
                regionX,
                regionZ,
                regionSize,
                EndlessForestShrubDensity
            );
            int ocean = ClearLevelMap(
                mapRegion.OceanMap,
                state,
                EndlessForestLevel,
                regionX,
                regionZ,
                regionSize
            );
            int upheaval = SetLevelMap(
                mapRegion.UpheavelMap,
                state,
                EndlessForestLevel,
                regionX,
                regionZ,
                regionSize,
                EndlessForestUpheaval
            );
            int landform = endlessForestLandformIndex >= 0
                ? SetLevelMap(
                    mapRegion.LandformMap,
                    state,
                    EndlessForestLevel,
                    regionX,
                    regionZ,
                    regionSize,
                    endlessForestLandformIndex
                )
                : 0;

            mapRegion.SetModdata(
                EndlessForestMapMarker,
                MapMarkerValue
            );
            mapRegion.DirtyForSaving = true;

            if (climate + forest + shrubs + ocean + upheaval + landform <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(
                ref rewrittenEndlessForestMapRegions
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedEndlessForestRegions
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Rewrote Level 3 map region {0},{1}: climate={2}, forest={3}, shrubs={4}, ocean={5}, upheaval={6}, landform={7} ({8}).",
                    regionX,
                    regionZ,
                    climate,
                    forest,
                    shrubs,
                    ocean,
                    upheaval,
                    landform,
                    EndlessForestLandformCode
                );
            }
        }

        private void RewriteShadowForestMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            int regionSize = api.WorldManager.RegionSize;
            int climate = ApplyClimateMap(
                mapRegion.ClimateMap,
                state,
                ShadowForestLevel,
                regionX,
                regionZ,
                regionSize,
                ShadowForestTemperatureCelsius,
                ShadowForestRainfall
            );
            int forest = ApplyShadowForestDensityMap(
                mapRegion.ForestMap,
                state,
                regionX,
                regionZ,
                regionSize,
                ShadowForestDensity
            );
            int shrubs = ApplyShadowForestDensityMap(
                mapRegion.ShrubMap,
                state,
                regionX,
                regionZ,
                regionSize,
                ShadowForestShrubDensity
            );
            int ocean = ClearLevelMap(
                mapRegion.OceanMap,
                state,
                ShadowForestLevel,
                regionX,
                regionZ,
                regionSize
            );
            int upheaval = ApplyShadowForestUpheavalMap(
                mapRegion.UpheavelMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int landform = shadowForestLandformIndex >= 0
                ? ApplyShadowForestLandformMap(
                    mapRegion.LandformMap,
                    state,
                    regionX,
                    regionZ,
                    regionSize,
                    shadowForestLandformIndex
                )
                : 0;

            mapRegion.SetModdata(
                ShadowForestMapMarker,
                MapMarkerValue
            );
            mapRegion.DirtyForSaving = true;

            if (climate + forest + shrubs + ocean + upheaval + landform <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(
                ref rewrittenShadowForestMapRegions
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedShadowForestRegions
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Rewrote Level 4 map region {0},{1}: climate={2}, forest={3}, shrubs={4}, ocean={5}, upheaval={6}, landform={7} ({8}), clearings={9}-block cells.",
                    regionX,
                    regionZ,
                    climate,
                    forest,
                    shrubs,
                    ocean,
                    upheaval,
                    landform,
                    ShadowForestLandformCode,
                    ShadowForestClearingCellSize
                );
            }
        }

        private void RewriteFrozenExpanseMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            int regionSize = api.WorldManager.RegionSize;
            int climate = ApplyFrozenExpanseClimateMap(
                mapRegion.ClimateMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int forest = ApplyFrozenExpanseDensityMap(
                mapRegion.ForestMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int shrubs = ApplyFrozenExpanseDensityMap(
                mapRegion.ShrubMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int ocean = ClearLevelMap(
                mapRegion.OceanMap,
                state,
                FrozenExpanseLevel,
                regionX,
                regionZ,
                regionSize
            );
            int upheaval = ApplyFrozenExpanseUpheavalMap(
                mapRegion.UpheavelMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int landform = frozenExpanseLandformIndex >= 0
                ? ApplyFrozenExpanseLandformMap(
                    mapRegion.LandformMap,
                    state,
                    regionX,
                    regionZ,
                    regionSize,
                    frozenExpanseLandformIndex
                )
                : 0;

            mapRegion.SetModdata(
                FrozenExpanseMapMarker,
                MapMarkerValue
            );
            mapRegion.DirtyForSaving = true;

            if (climate + forest + shrubs + ocean + upheaval + landform <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(
                ref rewrittenFrozenExpanseMapRegions
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedFrozenExpanseRegions
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Rewrote Level 5 map region {0},{1}: climate={2}, forest={3}, shrubs={4}, ocean={5}, upheaval={6}, landform={7} ({8}), transition={9} blocks.",
                    regionX,
                    regionZ,
                    climate,
                    forest,
                    shrubs,
                    ocean,
                    upheaval,
                    landform,
                    FrozenExpanseLandformCode,
                    FrozenExpanseTransitionWidth
                );
            }
        }

        private void OnInitWorldGenerator()
        {
            activeState = null;
            iceSpikeGenerator.Reset();
            poisonMireEnvironmentGenerator.Reset();
            shatteredHighlandsSurfaceGenerator.Reset();
            shatteredHighlandsRuinsGenerator.Reset();
            terrainVerifiedRegions.Clear();
            System.Threading.Interlocked.Exchange(
                ref rewrittenDesertMapRegions,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref rewrittenDeepSeaMapRegions,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref rewrittenEndlessForestMapRegions,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref rewrittenShadowForestMapRegions,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref rewrittenFrozenExpanseMapRegions,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref sculptedDeepSeaChunks,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref sculptedDeepSeaColumns,
                0
            );
            loggedDesertRegions = 0;
            loggedDeepSeaRegions = 0;
            loggedEndlessForestRegions = 0;
            loggedShadowForestRegions = 0;
            loggedFrozenExpanseRegions = 0;
            ResetPoisonMireWorldgen();
            ResetShatteredHighlandsWorldgen();
            loggedDeepSeaChunks = 0;
            globalConfig = null;
            deepSeaFloorNoise = null;
            endlessForestLandformIndex = -1;
            shadowForestLandformIndex = -1;
            frozenExpanseLandformIndex = -1;
            if (HasIncompatibleTerrainGenerator())
            {
                if (!conflictLogged)
                {
                    conflictLogged = true;
                    api.Logger.Error(
                        "[Apprentice] Concentric realm world generation is disabled because Watersheds is active. Danger scaling and the heatmap remain available, but Apprentice will not modify terrain maps."
                    );
                }
                return;
            }

            DangerWorldState? state = DangerTierRuntime.WorldState;
            if (state?.Enabled != true ||
                !state.RealmWorldgenEnabled ||
                state.WorldgenProfile !=
                    WorldZoneLayout.ConcentricRealmsProfile)
            {
                api.Logger.Notification(
                    "[Apprentice] Concentric realm world generation is disabled for this save. Existing chunks remain untouched."
                );
                return;
            }

            if (!WorldZoneLayout.TryValidate(state, out string error))
            {
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: {0}.",
                    error
                );
                return;
            }

            activeState = state;
            globalConfig = GlobalConfig.GetInstance(api);
            deepSeaFloorNoise =
                NormalizedSimplexNoise.FromDefaultOctaves(
                    4,
                    1d / 256d,
                    0.65,
                    api.WorldManager.Seed + 0x2D33EAL
                );
            endlessForestLandformIndex =
                ResolveLandformIndex(EndlessForestLandformCode);
            if (endlessForestLandformIndex < 0)
            {
                activeState = null;
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: required Level 3 landform '{0}' was not loaded.",
                    EndlessForestLandformCode
                );
                return;
            }
            shadowForestLandformIndex =
                ResolveLandformIndex(ShadowForestLandformCode);
            if (shadowForestLandformIndex < 0)
            {
                activeState = null;
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: required Level 4 landform '{0}' was not loaded.",
                    ShadowForestLandformCode
                );
                return;
            }
            frozenExpanseLandformIndex =
                ResolveLandformIndex(FrozenExpanseLandformCode);
            if (frozenExpanseLandformIndex < 0)
            {
                activeState = null;
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: required Level 5 landform '{0}' was not loaded.",
                    FrozenExpanseLandformCode
                );
                return;
            }
            if (!iceSpikeGenerator.Initialize(
                    state,
                    out string iceSpikeError))
            {
                activeState = null;
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: Frozen Expanse ice-spike fields could not initialize because {0}.",
                    iceSpikeError
                );
                return;
            }
            poisonMireLandformIndex =
                ResolveLandformIndex(PoisonMireLandformCode);
            if (poisonMireLandformIndex < 0)
            {
                activeState = null;
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: required Level 6 landform '{0}' was not loaded.",
                    PoisonMireLandformCode
                );
                return;
            }
            if (!poisonMireEnvironmentGenerator.Initialize(
                    state,
                    out string poisonMireEnvironmentError))
            {
                api.Logger.Error(
                    "[Apprentice] Poison Mire environment layer is disabled while Level 6 terrain remains active: {0}.",
                    poisonMireEnvironmentError
                );
            }
            if (!TryInitializeShatteredHighlandsWorldgen(
                    out string shatteredHighlandsError))
            {
                activeState = null;
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: {0}.",
                    shatteredHighlandsError
                );
                return;
            }
            if (!shatteredHighlandsSurfaceGenerator.Initialize(
                    state,
                    out string shatteredHighlandsSurfaceError))
            {
                activeState = null;
                api.Logger.Error(
                    "[Apprentice] Concentric realm world generation is disabled: Shattered Highlands surface generation could not initialize because {0}.",
                    shatteredHighlandsSurfaceError
                );
                return;
            }
            if (!shatteredHighlandsRuinsGenerator.Initialize(
                    state,
                    out string shatteredHighlandsRuinsError))
            {
                api.Logger.Error(
                    "[Apprentice] Shattered Highlands ruined-city layer is disabled while Level 7 terrain remains active: {0}.",
                    shatteredHighlandsRuinsError
                );
            }
            api.Logger.Notification(
                "[Apprentice] Concentric realms profile active: Homeland 0-{0:0}, Barren Desert {0:0}-{1:0}, Deep Sea {1:0}-{2:0} (depth {3}, shore width {4}), Endless Forest {2:0}-{5:0} (landform {6}, sea-level climate {7} C/{8} rainfall), Shadow Forest {5:0}-{9:0} (landform {10}, sea-level climate {11} C/{12} rainfall), Frozen Expanse {9:0}-{13:0} (landform {14}, sea-level climate {15} C/{16} rainfall, transition {17}, ice-spike fields {18:P1}).",
                state.BaseRadius,
                state.BaseRadius + state.RingWidth,
                state.BaseRadius + state.RingWidth * 2,
                state.DeepSeaDepth,
                state.DeepSeaShoreWidth,
                state.BaseRadius + state.RingWidth * 3,
                EndlessForestLandformCode,
                EndlessForestTemperatureCelsius,
                EndlessForestRainfall,
                state.BaseRadius + state.RingWidth * 4,
                ShadowForestLandformCode,
                ShadowForestTemperatureCelsius,
                ShadowForestRainfall,
                state.BaseRadius + state.RingWidth * 5,
                FrozenExpanseLandformCode,
                FrozenExpanseTemperatureCelsius,
                FrozenExpanseRainfall,
                FrozenExpanseTransitionWidth,
                FrozenExpanseIceSpikeGenerator
                    .ExpectedFieldCoverageFraction
            );
            api.Logger.Notification(
                "[Apprentice] Poison Mire {0:0}-{1:0}: landform {2}, sea-level climate {3} C/{4} rainfall, Toxicwater wetlands, transition {5} blocks, environment layer {6}.",
                state.BaseRadius + state.RingWidth * 5,
                state.BaseRadius + state.RingWidth * 6,
                PoisonMireLandformCode,
                PoisonMireTemperatureCelsius,
                PoisonMireRainfall,
                PoisonMireTransitionWidth,
                poisonMireEnvironmentGenerator.Initialized
                    ? "zero-green ground conversion, dead flora, dead-tree fields and mist active"
                    : "disabled"
            );
            api.Logger.Notification(
                "[Apprentice] Shattered Highlands {0:0}-{1:0}: landforms {2}/{3}, sea-level climate {4} C/{5} rainfall, forest/shrubs {6}/{7}, upheaval {8}, transition {9} blocks.",
                state.BaseRadius + state.RingWidth * 6,
                state.BaseRadius + state.RingWidth * 7,
                ShatteredHighlandsPlateauLandformCode,
                ShatteredHighlandsRiftLandformCode,
                ShatteredHighlandsTemperatureCelsius,
                ShatteredHighlandsRainfall,
                ShatteredHighlandsForestDensity,
                ShatteredHighlandsShrubDensity,
                ShatteredHighlandsUpheaval,
                ShatteredHighlandsTransitionWidth
            );
        }

        private static int ResolveLandformIndex(string code)
        {
            LandformVariant[]? landforms =
                NoiseLandforms.landforms?.LandFormsByIndex;
            if (landforms == null)
            {
                return -1;
            }

            for (int index = 0; index < landforms.Length; index++)
            {
                if (string.Equals(
                    landforms[index]?.Code?.Path,
                    code,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return index;
                }
            }

            return -1;
        }

        private bool HasIncompatibleTerrainGenerator() =>
            api.ModLoader.IsModEnabled("watersheds") ||
            api.ModLoader.IsModEnabled("watershed");

        private void OnMapRegionGeneration(
            IMapRegion mapRegion,
            int regionX,
            int regionZ,
            ITreeAttribute? chunkGenParams = null)
        {
            DangerWorldState? state = activeState;
            if (state == null || mapRegion == null) return;

            // The Terrain-pass safeguard below can run immediately after this
            // callback. Mark the same map-region instance current while it is
            // still locked so a freshly generated region is never transformed
            // twice.
            lock (mapRegion)
            {
                RewriteRealmMaps(mapRegion, state, regionX, regionZ);
            }
        }

        private void EnsureRealmMapsForChunk(
            IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = activeState;
            IMapRegion? mapRegion = request.Chunks[0].MapChunk.MapRegion;
            if (state == null || mapRegion == null)
            {
                return;
            }

            int regionSize = api.WorldManager.RegionSize;
            int worldX = request.ChunkX * ChunkSize;
            int worldZ = request.ChunkZ * ChunkSize;
            int regionX = worldX / regionSize;
            int regionZ = worldZ / regionSize;
            long regionKey =
                ((long)(uint)regionX << 32) |
                (uint)regionZ;
            if (!terrainVerifiedRegions.TryAdd(regionKey, 0))
            {
                return;
            }

            // A region map can already exist before a new chunk in it is
            // generated (for example after installing a corrected build).
            // Reassert the persisted realm masks at the Terrain pass so newly
            // generated realm chunks cannot inherit stale vanilla map data.
            lock (mapRegion)
            {
                if (HasCurrentRealmMaps(mapRegion))
                {
                    return;
                }

                RewriteRealmMaps(
                    mapRegion,
                    state,
                    regionX,
                    regionZ
                );
            }
        }

        private void RewriteRealmMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            // Accepted builds already contain the Level 1-5 maps. Upgrade
            // those regions by applying only the first missing realm instead
            // of repeating every complete map transform.
            if (!HasAcceptedLevelOneThroughFourMaps(mapRegion))
            {
                RewriteDesertMaps(mapRegion, state, regionX, regionZ);
                RewriteDeepSeaMaps(mapRegion, state, regionX, regionZ);
                RewriteEndlessForestMaps(
                    mapRegion,
                    state,
                    regionX,
                    regionZ
                );
                RewriteShadowForestMaps(
                    mapRegion,
                    state,
                    regionX,
                    regionZ
                );
            }
            if (mapRegion.GetModdata(FrozenExpanseMapMarker) == null)
            {
                RewriteFrozenExpanseMaps(
                    mapRegion,
                    state,
                    regionX,
                    regionZ
                );
            }
            if (mapRegion.GetModdata(PoisonMireMapMarker) == null)
            {
                RewritePoisonMireMaps(
                    mapRegion,
                    state,
                    regionX,
                    regionZ
                );
            }
            if (mapRegion.GetModdata(ShatteredHighlandsMapMarker) == null)
            {
                RewriteShatteredHighlandsMaps(
                    mapRegion,
                    state,
                    regionX,
                    regionZ
                );
            }
            mapRegion.SetModdata(
                RealmMapsPerformanceMarker,
                MapMarkerValue
            );
            mapRegion.DirtyForSaving = true;
        }

        private static bool HasCurrentRealmMaps(IMapRegion mapRegion)
        {
            if (mapRegion.GetModdata(RealmMapsPerformanceMarker) != null)
            {
                return true;
            }

            return false;
        }

        private static bool HasAcceptedLevelOneThroughFourMaps(
            IMapRegion mapRegion) =>
            mapRegion.GetModdata(DesertMapMarker) != null &&
            mapRegion.GetModdata(DeepSeaMapMarker) != null &&
            mapRegion.GetModdata(EndlessForestMapMarker) != null &&
            mapRegion.GetModdata(ShadowForestMapMarker) != null;

        private void RewriteDesertMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            int regionSize = api.WorldManager.RegionSize;
            int climate = ApplyClimateMap(
                mapRegion.ClimateMap,
                state,
                DesertLevel,
                regionX,
                regionZ,
                regionSize,
                state.DesertTemperatureCelsius,
                state.DesertRainfall
            );
            int forest = ClearLevelMap(
                mapRegion.ForestMap,
                state,
                DesertLevel,
                regionX,
                regionZ,
                regionSize
            );
            int shrubs = ClearLevelMap(
                mapRegion.ShrubMap,
                state,
                DesertLevel,
                regionX,
                regionZ,
                regionSize
            );
            int biomes = ClearLevelMap(
                mapRegion.BiomeMap,
                state,
                DesertLevel,
                regionX,
                regionZ,
                regionSize
            );
            int ocean = ClearLevelMap(
                mapRegion.OceanMap,
                state,
                DesertLevel,
                regionX,
                regionZ,
                regionSize
            );
            mapRegion.SetModdata(DesertMapMarker, MapMarkerValue);
            mapRegion.DirtyForSaving = true;

            if (climate + forest + shrubs + biomes + ocean <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(
                ref rewrittenDesertMapRegions
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedDesertRegions
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Rewrote Level 1 map region {0},{1}: climate={2}, forest={3}, shrubs={4}, biomes={5}, ocean={6}.",
                    regionX,
                    regionZ,
                    climate,
                    forest,
                    shrubs,
                    biomes,
                    ocean
                );
            }
        }

        private static int ApplyClimateMap(
            IntDataMap2D map,
            DangerWorldState state,
            int level,
            int regionX,
            int regionZ,
            int regionSize,
            int temperatureCelsius,
            int rainfall)
        {
            int temperature = Math.Clamp(
                Climate.DescaleTemperature(
                    temperatureCelsius
                ),
                0,
                255
            );
            return TransformLevelMap(
                map,
                state,
                level,
                regionX,
                regionZ,
                regionSize,
                existing =>
                    (temperature << 16) |
                    (Math.Clamp(rainfall, 0, 255) << 8) |
                    (existing & 0xff)
            );
        }

        private static int ClearLevelMap(
            IntDataMap2D map,
            DangerWorldState state,
            int level,
            int regionX,
            int regionZ,
            int regionSize) =>
            TransformLevelMap(
                map,
                state,
                level,
                regionX,
                regionZ,
                regionSize,
                _ => 0
            );

        private static int SetLevelMap(
            IntDataMap2D map,
            DangerWorldState state,
            int level,
            int regionX,
            int regionZ,
            int regionSize,
            int value) =>
            TransformLevelMap(
                map,
                state,
                level,
                regionX,
                regionZ,
                regionSize,
                _ => value
            );

        private static int ApplyShadowForestDensityMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize,
            int maximumDensity) =>
            TransformLevelMap(
                map,
                state,
                ShadowForestLevel,
                regionX,
                regionZ,
                regionSize,
                (_, worldX, worldZ) =>
                    GetShadowForestDensity(
                        worldX,
                        worldZ,
                        maximumDensity
                    )
            );

        private static int ApplyShadowForestUpheavalMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize) =>
            TransformLevelMap(
                map,
                state,
                ShadowForestLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                {
                    double blend = GetShadowForestOuterBlend(
                        state,
                        worldX,
                        worldZ
                    );
                    return Math.Clamp(
                        (int)Math.Round(
                            existing +
                            (ShadowForestUpheaval - existing) * blend,
                            MidpointRounding.AwayFromZero
                        ),
                        0,
                        255
                    );
                }
            );

        private static int ApplyShadowForestLandformMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize,
            int landformIndex) =>
            TransformLevelMap(
                map,
                state,
                ShadowForestLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    GetShadowForestOuterBlend(
                        state,
                        worldX,
                        worldZ
                    ) >= 1
                        ? landformIndex
                        : existing
            );

        private static int ApplyFrozenExpanseClimateMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize)
        {
            int targetTemperature = Math.Clamp(
                Climate.DescaleTemperature(
                    FrozenExpanseTemperatureCelsius
                ),
                0,
                255
            );
            return TransformLevelMap(
                map,
                state,
                FrozenExpanseLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                {
                    double blend = GetFrozenExpanseBlend(
                        state,
                        worldX,
                        worldZ
                    );
                    int existingTemperature =
                        (existing >> 16) & 0xff;
                    int existingRainfall =
                        (existing >> 8) & 0xff;
                    int temperature = BlendByte(
                        existingTemperature,
                        targetTemperature,
                        blend
                    );
                    int rainfall = BlendByte(
                        existingRainfall,
                        FrozenExpanseRainfall,
                        blend
                    );
                    return (temperature << 16) |
                        (rainfall << 8) |
                        (existing & 0xff);
                }
            );
        }

        private static int ApplyFrozenExpanseDensityMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize) =>
            TransformLevelMap(
                map,
                state,
                FrozenExpanseLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    BlendByte(
                        existing,
                        0,
                        GetFrozenExpanseBlend(
                            state,
                            worldX,
                            worldZ
                        )
                    )
            );

        private static int ApplyFrozenExpanseUpheavalMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize) =>
            TransformLevelMap(
                map,
                state,
                FrozenExpanseLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    BlendByte(
                        existing,
                        FrozenExpanseUpheaval,
                        GetFrozenExpanseBlend(
                            state,
                            worldX,
                            worldZ
                        )
                    )
            );

        private static int ApplyFrozenExpanseLandformMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize,
            int landformIndex) =>
            TransformLevelMap(
                map,
                state,
                FrozenExpanseLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    GetFrozenExpanseBlend(
                        state,
                        worldX,
                        worldZ
                    ) >= 0.5
                        ? landformIndex
                        : existing
            );

        private static int BlendByte(
            int existing,
            int target,
            double blend) =>
            Math.Clamp(
                (int)Math.Round(
                    existing + (target - existing) * blend,
                    MidpointRounding.AwayFromZero
                ),
                0,
                255
            );

        private static int TransformLevelMap(
            IntDataMap2D map,
            DangerWorldState state,
            int level,
            int regionX,
            int regionZ,
            int regionSize,
            System.Func<int, int> transform)
        {
            return TransformLevelMap(
                map,
                state,
                level,
                regionX,
                regionZ,
                regionSize,
                (existing, _, _) => transform(existing)
            );
        }

        private static int TransformLevelMap(
            IntDataMap2D map,
            DangerWorldState state,
            int level,
            int regionX,
            int regionZ,
            int regionSize,
            System.Func<int, double, double, int> transform)
        {
            if (map?.Data == null || map.Size <= 0 ||
                map.InnerSize <= 0)
            {
                return 0;
            }

            int transformed = 0;
            double blockStep = (double)regionSize / map.InnerSize;
            double regionOriginX = (double)regionX * regionSize;
            double regionOriginZ = (double)regionZ * regionSize;
            double firstWorldX = regionOriginX -
                map.TopLeftPadding * blockStep;
            double firstWorldZ = regionOriginZ -
                map.TopLeftPadding * blockStep;
            double lastWorldX = regionOriginX +
                (map.Size - 1 - map.TopLeftPadding) * blockStep;
            double lastWorldZ = regionOriginZ +
                (map.Size - 1 - map.TopLeftPadding) * blockStep;
            if (!WorldZoneLayout.RectangleIntersectsLevel(
                state,
                level,
                Math.Min(firstWorldX, lastWorldX),
                Math.Min(firstWorldZ, lastWorldZ),
                Math.Max(firstWorldX, lastWorldX),
                Math.Max(firstWorldZ, lastWorldZ)))
            {
                return 0;
            }

            double innerRadius =
                WorldZoneLayout.GetInnerRadius(state, level);
            double outerRadius =
                WorldZoneLayout.GetOuterRadius(state, level);
            double innerRadiusSquared = innerRadius * innerRadius;
            double outerRadiusSquared =
                double.IsPositiveInfinity(outerRadius)
                    ? double.PositiveInfinity
                    : outerRadius * outerRadius;
            for (int z = 0; z < map.Size; z++)
            {
                double worldZ = regionOriginZ +
                    (z - map.TopLeftPadding) * blockStep;
                int row = z * map.Size;
                for (int x = 0; x < map.Size; x++)
                {
                    double worldX = regionOriginX +
                        (x - map.TopLeftPadding) * blockStep;
                    double dx = worldX - state.AnchorX;
                    double dz = worldZ - state.AnchorZ;
                    double distanceSquared = dx * dx + dz * dz;
                    bool inside = level <= 0
                        ? distanceSquared <= outerRadiusSquared
                        : distanceSquared > innerRadiusSquared &&
                            distanceSquared <= outerRadiusSquared;
                    if (!inside)
                    {
                        continue;
                    }

                    int index = row + x;
                    map.Data[index] = transform(
                        map.Data[index],
                        worldX,
                        worldZ
                    );
                    transformed++;
                }
            }
            return transformed;
        }

        private static int GetShadowForestDensity(
            double worldX,
            double worldZ,
            int maximumDensity)
        {
            int cellX = (int)Math.Floor(
                worldX / ShadowForestClearingCellSize
            );
            int cellZ = (int)Math.Floor(
                worldZ / ShadowForestClearingCellSize
            );
            double densityScale = 1;

            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    int clearingCellX = cellX + offsetX;
                    int clearingCellZ = cellZ + offsetZ;
                    ulong hash = StableCellHash(
                        clearingCellX,
                        clearingCellZ
                    );
                    double offsetUnitX =
                        (hash & 0xffff) / 65535d;
                    double offsetUnitZ =
                        ((hash >> 16) & 0xffff) / 65535d;
                    double radiusUnit =
                        ((hash >> 32) & 0xffff) / 65535d;
                    double centerX =
                        (clearingCellX + 0.2 + offsetUnitX * 0.6) *
                        ShadowForestClearingCellSize;
                    double centerZ =
                        (clearingCellZ + 0.2 + offsetUnitZ * 0.6) *
                        ShadowForestClearingCellSize;
                    double radius =
                        ShadowForestClearingMinimumRadius +
                        radiusUnit *
                        ShadowForestClearingRadiusVariation;
                    double dx = worldX - centerX;
                    double dz = worldZ - centerZ;
                    double distanceSquared = dx * dx + dz * dz;
                    if (distanceSquared >= radius * radius)
                    {
                        continue;
                    }

                    double normalizedDistance =
                        Math.Sqrt(distanceSquared) / radius;
                    // Smoothly restore the canopy at the rim. The center
                    // remains open enough to read as a deliberate clearing,
                    // while interpolation cannot create a hard square seam.
                    double smoothEdge =
                        normalizedDistance * normalizedDistance *
                        (3 - 2 * normalizedDistance);
                    densityScale = Math.Min(
                        densityScale,
                        smoothEdge
                    );
                }
            }

            return Math.Clamp(
                (int)Math.Round(
                    maximumDensity * densityScale,
                    MidpointRounding.AwayFromZero
                ),
                0,
                maximumDensity
            );
        }

        private static double GetShadowForestOuterBlend(
            DangerWorldState state,
            double worldX,
            double worldZ)
        {
            double dx = worldX - state.AnchorX;
            double dz = worldZ - state.AnchorZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            double distanceFromOuterEdge =
                WorldZoneLayout.GetOuterRadius(
                    state,
                    ShadowForestLevel
                ) - distance;
            double normalized = Math.Clamp(
                distanceFromOuterEdge /
                    ShadowForestOuterTransitionWidth,
                0,
                1
            );
            return normalized * normalized * (3 - 2 * normalized);
        }

        private static double GetFrozenExpanseBlend(
            DangerWorldState state,
            double worldX,
            double worldZ)
        {
            double dx = worldX - state.AnchorX;
            double dz = worldZ - state.AnchorZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            double innerRadius =
                WorldZoneLayout.GetInnerRadius(
                    state,
                    FrozenExpanseLevel
                );
            double outerRadius =
                WorldZoneLayout.GetOuterRadius(
                    state,
                    FrozenExpanseLevel
                );
            double distanceFromEdge = Math.Min(
                distance - innerRadius,
                outerRadius - distance
            );
            double normalized = Math.Clamp(
                distanceFromEdge /
                    FrozenExpanseTransitionWidth,
                0,
                1
            );
            return normalized * normalized * (3 - 2 * normalized);
        }

        private static ulong StableCellHash(int cellX, int cellZ)
        {
            unchecked
            {
                ulong value =
                    (uint)cellX * 0x9E3779B185EBCA87UL ^
                    (uint)cellZ * 0xC2B2AE3D27D4EB4FUL ^
                    0x534841444F57464FUL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private void SculptDeepSeaBeforeVegetation(
            IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = activeState;
            GlobalConfig? config = globalConfig;
            NormalizedSimplexNoise? floorNoise = deepSeaFloorNoise;
            if (state == null || config == null || floorNoise == null ||
                !WorldZoneLayout.ChunkIntersectsLevel(
                    state,
                    DeepSeaLevel,
                    request.ChunkX,
                    request.ChunkZ,
                    ChunkSize))
            {
                return;
            }

            IMapChunk mapChunk = request.Chunks[0].MapChunk;
            ushort[] terrainHeights =
                mapChunk.WorldGenTerrainHeightMap;
            ushort[] rainHeights = mapChunk.RainHeightMap;
            int[] topRockIds = mapChunk.TopRockIdMap;
            int seaLevel = TerraGenConfig.seaLevel;
            int mapSizeY = Math.Min(
                api.WorldManager.MapSizeY,
                request.Chunks.Length * ChunkSize
            );
            int maximumFloor = Math.Max(1, seaLevel - 8);
            int coreColumns = 0;
            int minimumFloor = mapSizeY;
            int maximumGeneratedFloor = 0;

            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                int worldZ =
                    request.ChunkZ * ChunkSize + localZ;
                int row = localZ * ChunkSize;
                for (int localX = 0;
                     localX < ChunkSize;
                     localX++)
                {
                    int worldX =
                        request.ChunkX * ChunkSize + localX;
                    if (!WorldZoneLayout.IsInsideLevelCore(
                        state,
                        DeepSeaLevel,
                        state.DeepSeaShoreWidth,
                        worldX + 0.5,
                        worldZ + 0.5))
                    {
                        continue;
                    }

                    int mapIndex = row + localX;
                    double normalized =
                        floorNoise.Noise(worldX, worldZ);
                    int variation = (int)Math.Round(
                        (normalized - 0.5) *
                        DeepSeaFloorVariation * 2,
                        MidpointRounding.AwayFromZero
                    );
                    int desiredFloor = Math.Clamp(
                        seaLevel - state.DeepSeaDepth + variation,
                        1,
                        maximumFloor
                    );
                    int originalTerrain = Math.Clamp(
                        terrainHeights[mapIndex],
                        1,
                        mapSizeY - 1
                    );
                    int floor = Math.Min(
                        originalTerrain,
                        desiredFloor
                    );
                    int originalColumnTop = Math.Max(
                        terrainHeights[mapIndex],
                        rainHeights[mapIndex]
                    );
                    int columnTop = Math.Clamp(
                        Math.Max(
                            seaLevel,
                            originalColumnTop + 1
                        ),
                        floor + 1,
                        mapSizeY - 1
                    );

                    int floorChunkY = floor / ChunkSize;
                    int floorIndex = ChunkIndex3d(
                        localX,
                        floor % ChunkSize,
                        localZ
                    );
                    int rockId =
                        topRockIds != null &&
                        mapIndex < topRockIds.Length &&
                        topRockIds[mapIndex] > 0
                            ? topRockIds[mapIndex]
                            : config.defaultRockId;
                    IChunkBlocks floorChunkData =
                        request.Chunks[floorChunkY].Data;
                    floorChunkData[floorIndex] = rockId;
                    floorChunkData.SetFluid(floorIndex, 0);

                    for (int y = floor + 1;
                         y <= columnTop;
                         y++)
                    {
                        IChunkBlocks chunkData =
                            request.Chunks[y / ChunkSize].Data;
                        int index = ChunkIndex3d(
                            localX,
                            y % ChunkSize,
                            localZ
                        );
                        // Vegetation-pass chunks can legitimately contain an
                        // empty solid layer with no writable palette. The
                        // indexer initializes or safely skips that layer;
                        // SetBlockUnsafe assumes an existing writable palette
                        // and crashes while clearing such columns.
                        chunkData[index] = 0;
                        chunkData.SetFluid(
                            index,
                            y < seaLevel
                                ? config.saltWaterBlockId
                                : 0
                        );
                    }

                    terrainHeights[mapIndex] = (ushort)floor;
                    rainHeights[mapIndex] =
                        (ushort)(seaLevel - 1);
                    coreColumns++;
                    minimumFloor = Math.Min(minimumFloor, floor);
                    maximumGeneratedFloor = Math.Max(
                        maximumGeneratedFloor,
                        floor
                    );
                }
            }

            if (coreColumns <= 0)
            {
                return;
            }

            ushort yMax = 0;
            for (int index = 0;
                 index < rainHeights.Length;
                 index++)
            {
                yMax = Math.Max(yMax, rainHeights[index]);
            }
            mapChunk.YMax = yMax;

            System.Threading.Interlocked.Increment(
                ref sculptedDeepSeaChunks
            );
            System.Threading.Interlocked.Add(
                ref sculptedDeepSeaColumns,
                coreColumns
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedDeepSeaChunks
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Sculpted Level 2 chunk {0},{1}: core columns={2}, seabed Y={3}-{4}, salt-water surface Y={5}.",
                    request.ChunkX,
                    request.ChunkZ,
                    coreColumns,
                    minimumFloor,
                    maximumGeneratedFloor,
                    seaLevel - 1
                );
            }
        }

        private static int ChunkIndex3d(
            int x,
            int y,
            int z) =>
            (y * ChunkSize + z) * ChunkSize + x;

        public bool PreventPlacementAt(int x, int z, int category) =>
            WorldZoneLayout.IsLevelAt(
                activeState,
                DesertLevel,
                x,
                z) ||
            WorldZoneLayout.IsLevelAt(
                activeState,
                DeepSeaLevel,
                x,
                z);

        public bool PreventPlacementBroadlyAt(
            int chunkX,
            int chunkZ) =>
            WorldZoneLayout.ChunkFullyInsideLevel(
                activeState,
                DesertLevel,
                chunkX,
                chunkZ,
                ChunkSize
            ) ||
            WorldZoneLayout.ChunkFullyInsideLevel(
                activeState,
                DeepSeaLevel,
                chunkX,
                chunkZ,
                ChunkSize
            );

        public BlockPatchConfig? GetPatchProviderAt(
            int chunkX,
            int chunkZ,
            ref EnumHandling handling)
        {
            bool insideDesert =
                WorldZoneLayout.ChunkFullyInsideLevel(
                    activeState,
                    DesertLevel,
                    chunkX,
                    chunkZ,
                    ChunkSize
                );
            bool insideDeepSea =
                WorldZoneLayout.ChunkFullyInsideLevel(
                    activeState,
                    DeepSeaLevel,
                    chunkX,
                    chunkZ,
                    ChunkSize
                );
            bool insideEndlessForest =
                WorldZoneLayout.ChunkFullyInsideLevel(
                    activeState,
                    EndlessForestLevel,
                    chunkX,
                    chunkZ,
                    ChunkSize
                );
            bool insideShadowForest =
                WorldZoneLayout.ChunkFullyInsideLevel(
                    activeState,
                    ShadowForestLevel,
                    chunkX,
                    chunkZ,
                    ChunkSize
                );
            bool insideFrozenExpanse =
                WorldZoneLayout.ChunkFullyInsideLevel(
                    activeState,
                    FrozenExpanseLevel,
                    chunkX,
                    chunkZ,
                    ChunkSize
                );
            bool insidePoisonMire =
                WorldZoneLayout.ChunkFullyInsideLevel(
                    activeState,
                    PoisonMireLevel,
                    chunkX,
                    chunkZ,
                    ChunkSize
                );
            bool insideShatteredHighlands =
                WorldZoneLayout.ChunkFullyInsideLevel(
                    activeState,
                    ShatteredHighlandsLevel,
                    chunkX,
                    chunkZ,
                    ChunkSize
                );
            if (!insideDesert &&
                !insideDeepSea &&
                !insideEndlessForest &&
                !insideShadowForest &&
                !insideFrozenExpanse &&
                !insidePoisonMire &&
                !insideShatteredHighlands)
            {
                return null;
            }

            handling = EnumHandling.PreventSubsequent;
            return EmptyPatches;
        }

        private bool OnPreventSchematicPlaceAt(
            IBlockAccessor blockAccessor,
            BlockPos pos,
            Cuboidi schematicLocation,
            string locationCode)
        {
            DangerWorldState? state = activeState;
            if (state == null) return false;

            return WorldZoneLayout.RectangleIntersectsLevel(
                state,
                DesertLevel,
                schematicLocation.X1,
                schematicLocation.Z1,
                schematicLocation.X2,
                schematicLocation.Z2
            ) ||
            WorldZoneLayout.RectangleIntersectsLevel(
                state,
                DeepSeaLevel,
                schematicLocation.X1,
                schematicLocation.Z1,
                schematicLocation.X2,
                schematicLocation.Z2
            ) ||
            WorldZoneLayout.RectangleIntersectsLevel(
                state,
                EndlessForestLevel,
                schematicLocation.X1,
                schematicLocation.Z1,
                schematicLocation.X2,
                schematicLocation.Z2
            ) ||
            WorldZoneLayout.RectangleIntersectsLevel(
                state,
                ShadowForestLevel,
                schematicLocation.X1,
                schematicLocation.Z1,
                schematicLocation.X2,
                schematicLocation.Z2
            ) ||
            WorldZoneLayout.RectangleIntersectsLevel(
                state,
                FrozenExpanseLevel,
                schematicLocation.X1,
                schematicLocation.Z1,
                schematicLocation.X2,
                schematicLocation.Z2
            ) ||
            WorldZoneLayout.RectangleIntersectsLevel(
                state,
                PoisonMireLevel,
                schematicLocation.X1,
                schematicLocation.Z1,
                schematicLocation.X2,
                schematicLocation.Z2
            ) ||
            WorldZoneLayout.RectangleIntersectsLevel(
                state,
                ShatteredHighlandsLevel,
                schematicLocation.X1,
                schematicLocation.Z1,
                schematicLocation.X2,
                schematicLocation.Z2
            );
        }

        private bool OnTrySpawnEntity(
            IBlockAccessor blockAccessor,
            ref EntityProperties properties,
            Vec3d spawnPosition,
            long herdId)
        {
            if (WorldZoneLayout.IsLevelAt(
                activeState,
                DeepSeaLevel,
                spawnPosition.X,
                spawnPosition.Z))
            {
                // Sharks and other realm-specific sea life are a later
                // milestone. Suppress vanilla land spawns until that ecology
                // has an explicit, tested allowlist.
                return false;
            }

            if (!WorldZoneLayout.IsLevelAt(
                activeState,
                DesertLevel,
                spawnPosition.X,
                spawnPosition.Z))
            {
                return true;
            }

            string path = properties?.Code?.Path?.ToLowerInvariant() ??
                string.Empty;
            return path.StartsWith("drifter-", StringComparison.Ordinal) ||
                path.StartsWith("locust-", StringComparison.Ordinal) ||
                path.StartsWith("bell-", StringComparison.Ordinal) ||
                path.StartsWith("shiver-", StringComparison.Ordinal) ||
                path.StartsWith("bowtorn-", StringComparison.Ordinal) ||
                path.StartsWith("nightmare-", StringComparison.Ordinal);
        }

        internal TextCommandResult LocateNearestIceSpikeField(
            TextCommandCallingArgs args)
        {
            DangerWorldState? state = activeState;
            if (state == null || !iceSpikeGenerator.Initialized)
            {
                return TextCommandResult.Error(
                    "Frozen Expanse ice-spike generation is not active for this save.",
                    "apprentice-frozen-spikes-disabled"
                );
            }

            GetIceSpikeSearchOrigin(
                state,
                args.Caller.Player as IServerPlayer,
                out double searchX,
                out double searchZ
            );
            if (!iceSpikeGenerator.TryFindNearestField(
                    searchX,
                    searchZ,
                    requireBoundaryCrossingMain: false,
                    out IceSpikeField? field,
                    out IceSpikeDefinition? main) ||
                field == null ||
                main == null)
            {
                return TextCommandResult.Error(
                    "No safe ice-spike field fits inside the Frozen Expanse core.",
                    "apprentice-frozen-spikes-not-found"
                );
            }

            int mainCount = field.Spikes.Count(
                spike => spike.Size == IceSpikeSize.Main
            );
            int mediumCount = field.Spikes.Count(
                spike => spike.Size == IceSpikeSize.Medium
            );
            int smallCount = field.Spikes.Count(
                spike => spike.Size == IceSpikeSize.Small
            );
            double dx = field.CenterX - searchX;
            double dz = field.CenterZ - searchZ;
            double distance = Math.Sqrt(dx * dx + dz * dz);
            return TextCommandResult.Success(
                $"Nearest Level 5 Ice-Spike Field: X={field.CenterX}, " +
                $"Z={field.CenterZ}, about {distance:0} blocks from the " +
                $"search point. Field radius {field.RadiusX}x" +
                $"{field.RadiusZ}; spikes {mainCount} main, " +
                $"{mediumCount} medium and {smallCount} small; tallest " +
                $"{field.Spikes.Max(spike => spike.Height)} blocks. " +
                "Only unexplored chunks generate the landmark."
            );
        }

        internal TextCommandResult StartIceSpikeProbe(
            TextCommandCallingArgs args)
        {
            DangerWorldState? state = activeState;
            if (state == null || !iceSpikeGenerator.Initialized)
            {
                return TextCommandResult.Error(
                    "Frozen Expanse ice-spike generation is not active for this save.",
                    "apprentice-frozen-spikes-disabled"
                );
            }

            GetIceSpikeSearchOrigin(
                state,
                args.Caller.Player as IServerPlayer,
                out double searchX,
                out double searchZ
            );
            if (!iceSpikeGenerator.TryFindNearestField(
                    searchX,
                    searchZ,
                    requireBoundaryCrossingMain: true,
                    out IceSpikeField? field,
                    out IceSpikeDefinition? targetSpike) ||
                field == null ||
                targetSpike == null)
            {
                return TextCommandResult.Error(
                    "No chunk-crossing main ice spike fits inside the Frozen Expanse core.",
                    "apprentice-frozen-spikes-probe-target"
                );
            }

            IReadOnlyList<IceSpikeProbeChunk> targets =
                iceSpikeGenerator.BuildProbeChunks(targetSpike);
            IReadOnlySet<long> expectedTargetChunks =
                iceSpikeGenerator.GetIntersectingChunkKeys(targetSpike);
            if (targets.Count != IceSpikeProbeRun.RequiredChunks ||
                expectedTargetChunks.Count < 2 ||
                expectedTargetChunks.Any(key =>
                    !targets.Any(target => target.Key == key)))
            {
                return TextCommandResult.Error(
                    "The selected ice spike does not fit the continuity probe window.",
                    "apprentice-frozen-spikes-probe-window"
                );
            }

            IceSpikeProbeRun run;
            lock (iceSpikeProbeGate)
            {
                if (activeIceSpikeProbe != null)
                {
                    return TextCommandResult.Error(
                        "An Ice-Spike Field probe is already running.",
                        "apprentice-frozen-spikes-probe-active"
                    );
                }

                run = new IceSpikeProbeRun(
                    args.Caller.Player as IServerPlayer,
                    field,
                    targetSpike,
                    targets,
                    expectedTargetChunks
                );
                activeIceSpikeProbe = run;
            }

            api.Logger.Notification(
                "[Apprentice] Starting non-destructive Ice-Spike Field probe: field center {0},{1}, target height {2}, {3} scratch chunks.",
                field.CenterX,
                field.CenterZ,
                targetSpike.Height,
                targets.Count
            );
            api.Event.EnqueueMainThreadTask(
                () => RunNextIceSpikeProbe(run),
                "apprentice-frozen-spikes-probe-start"
            );
            return TextCommandResult.Success(
                $"Ice-Spike Field probe started at X={field.CenterX}, " +
                $"Z={field.CenterZ}: {targets.Count} scratch chunks. " +
                "It changes no saved or loaded chunk and will report " +
                "shape, open ground, continuity and timing here."
            );
        }

        private static void GetIceSpikeSearchOrigin(
            DangerWorldState state,
            IServerPlayer? player,
            out double worldX,
            out double worldZ)
        {
            double candidateX = player?.Entity.Pos.X ??
                state.AnchorX + 1;
            double candidateZ = player?.Entity.Pos.Z ??
                state.AnchorZ;
            if (WorldZoneLayout.IsInsideLevelCore(
                    state,
                    FrozenExpanseLevel,
                    FrozenExpanseIceSpikeGenerator
                        .BoundaryExclusionWidth,
                    candidateX,
                    candidateZ))
            {
                worldX = candidateX;
                worldZ = candidateZ;
                return;
            }

            double dx = candidateX - state.AnchorX;
            double dz = candidateZ - state.AnchorZ;
            double length = Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.001)
            {
                dx = 1;
                dz = 0;
                length = 1;
            }
            double radius = (
                WorldZoneLayout.GetInnerRadius(
                    state,
                    FrozenExpanseLevel
                ) +
                WorldZoneLayout.GetOuterRadius(
                    state,
                    FrozenExpanseLevel
                )
            ) / 2;
            worldX = state.AnchorX + dx / length * radius;
            worldZ = state.AnchorZ + dz / length * radius;
        }

        private void RunNextIceSpikeProbe(IceSpikeProbeRun run)
        {
            if (!IsActiveIceSpikeProbe(run))
            {
                return;
            }

            IceSpikeProbeChunk? target;
            lock (run.Sync)
            {
                target = run.NextTarget();
            }
            if (target == null)
            {
                FinishIceSpikeProbe(run);
                return;
            }

            if (!iceSpikeGenerator.PrepareProbeChunk(
                    target.ChunkX,
                    target.ChunkZ,
                    run.TargetSpike.Id))
            {
                RecordIceSpikeProbeError(
                    run,
                    target,
                    "A stale probe trace already existed."
                );
                ScheduleNextIceSpikeProbe(run);
                return;
            }

            target.StartedTimestamp = Stopwatch.GetTimestamp();
            try
            {
                api.WorldManager.PeekChunkColumn(
                    target.ChunkX,
                    target.ChunkZ,
                    new ChunkPeekOptions
                    {
                        OnGenerated = columns =>
                            OnIceSpikeProbeChunkGenerated(
                                run,
                                target,
                                columns
                            )
                    }
                );
            }
            catch (Exception exception)
            {
                iceSpikeGenerator.CancelProbeChunk(
                    target.ChunkX,
                    target.ChunkZ
                );
                RecordIceSpikeProbeError(
                    run,
                    target,
                    "PeekChunkColumn failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
                ScheduleNextIceSpikeProbe(run);
            }
        }

        private void OnIceSpikeProbeChunkGenerated(
            IceSpikeProbeRun run,
            IceSpikeProbeChunk target,
            Dictionary<Vec2i, IServerChunk[]> columns)
        {
            if (!IsActiveIceSpikeProbe(run))
            {
                iceSpikeGenerator.CancelProbeChunk(
                    target.ChunkX,
                    target.ChunkZ
                );
                return;
            }

            double pipelineMilliseconds =
                Stopwatch.GetElapsedTime(
                    target.StartedTimestamp
                ).TotalMilliseconds;
            try
            {
                bool columnPresent = columns.Any(entry =>
                    entry.Key.X == target.ChunkX &&
                    entry.Key.Y == target.ChunkZ);
                if (!columnPresent)
                {
                    iceSpikeGenerator.CancelProbeChunk(
                        target.ChunkX,
                        target.ChunkZ
                    );
                    RecordIceSpikeProbeError(
                        run,
                        target,
                        "Peek callback omitted the requested chunk column."
                    );
                }
                else if (!iceSpikeGenerator.TryTakeProbeTrace(
                        target.ChunkX,
                        target.ChunkZ,
                        out IceSpikeChunkTrace? trace) ||
                    trace == null)
                {
                    RecordIceSpikeProbeError(
                        run,
                        target,
                        "The ice-spike generator did not return a trace."
                    );
                }
                else
                {
                    lock (run.Sync)
                    {
                        run.CompletedChunks++;
                        run.PipelineMilliseconds +=
                            pipelineMilliseconds;
                        run.GeneratorMilliseconds +=
                            trace.GeneratorMilliseconds;
                        run.PlacedBlocks += trace.PlacedBlocks;
                        run.ModifiedColumns +=
                            trace.ModifiedColumns;
                        run.MinimumSpikeHeight = Math.Min(
                            run.MinimumSpikeHeight,
                            trace.MinimumSpikeHeight > 0
                                ? trace.MinimumSpikeHeight
                                : int.MaxValue
                        );
                        run.MaximumSpikeHeight = Math.Max(
                            run.MaximumSpikeHeight,
                            trace.MaximumSpikeHeight
                        );
                        run.IntersectingSpikeIds.UnionWith(
                            trace.IntersectingSpikeIds
                        );
                        if (trace.TargetSpikeBlocks > 0)
                        {
                            run.TargetSpikeBlocks +=
                                trace.TargetSpikeBlocks;
                            run.TargetSpikeHeight = Math.Max(
                                run.TargetSpikeHeight,
                                trace.TargetSpikeHeight
                            );
                            run.ObservedTargetChunkKeys.Add(
                                target.Key
                            );
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                iceSpikeGenerator.CancelProbeChunk(
                    target.ChunkX,
                    target.ChunkZ
                );
                RecordIceSpikeProbeError(
                    run,
                    target,
                    "Scratch-column spike scan failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
            }

            ScheduleNextIceSpikeProbe(run);
        }

        private static void RecordIceSpikeProbeError(
            IceSpikeProbeRun run,
            IceSpikeProbeChunk target,
            string error)
        {
            lock (run.Sync)
            {
                run.CompletedChunks++;
                run.Errors.Add(
                    $"chunk {target.ChunkX},{target.ChunkZ}: {error}"
                );
            }
        }

        private void ScheduleNextIceSpikeProbe(IceSpikeProbeRun run)
        {
            api.Event.EnqueueMainThreadTask(
                () => RunNextIceSpikeProbe(run),
                "apprentice-frozen-spikes-probe-next"
            );
        }

        private bool IsActiveIceSpikeProbe(IceSpikeProbeRun run)
        {
            lock (iceSpikeProbeGate)
            {
                return ReferenceEquals(activeIceSpikeProbe, run);
            }
        }

        private void FinishIceSpikeProbe(IceSpikeProbeRun run)
        {
            lock (iceSpikeProbeGate)
            {
                if (!ReferenceEquals(activeIceSpikeProbe, run))
                {
                    return;
                }
                activeIceSpikeProbe = null;
            }

            int totalColumns =
                run.CompletedChunks * ChunkSize * ChunkSize;
            double openGroundFraction = totalColumns > 0
                ? 1 - (double)run.ModifiedColumns / totalColumns
                : 0;
            double generatorShare = run.PipelineMilliseconds > 0
                ? run.GeneratorMilliseconds /
                    run.PipelineMilliseconds
                : 1;
            bool continuityPassed =
                run.ExpectedTargetChunkKeys.SetEquals(
                    run.ObservedTargetChunkKeys
                );
            bool passed =
                run.Errors.Count == 0 &&
                run.CompletedChunks == run.Targets.Count &&
                run.TargetSpikeBlocks > 0 &&
                run.TargetSpikeHeight >=
                    FrozenExpanseIceSpikeGenerator
                        .MainSpikeMinimumHeight &&
                run.TargetSpikeHeight <=
                    FrozenExpanseIceSpikeGenerator
                        .MainSpikeMaximumHeight &&
                continuityPassed &&
                run.IntersectingSpikeIds.Count > 0 &&
                openGroundFraction >= 0.60 &&
                generatorShare <= 0.15;

            int mainCount = run.Field.Spikes.Count(
                spike => spike.Size == IceSpikeSize.Main
            );
            int mediumCount = run.Field.Spikes.Count(
                spike => spike.Size == IceSpikeSize.Medium
            );
            int smallCount = run.Field.Spikes.Count(
                spike => spike.Size == IceSpikeSize.Small
            );
            string message =
                $"[Apprentice] Ice-Spike Field probe " +
                $"{(passed ? "PASS" : "FAIL")}: field X=" +
                $"{run.Field.CenterX}, Z={run.Field.CenterZ}; " +
                $"field spikes {mainCount} main/{mediumCount} medium/" +
                $"{smallCount} small; sampled spikes " +
                $"{run.IntersectingSpikeIds.Count}, height " +
                $"{(run.MinimumSpikeHeight == int.MaxValue ? 0 : run.MinimumSpikeHeight)}-" +
                $"{run.MaximumSpikeHeight}; glacier blocks added " +
                $"{run.PlacedBlocks}; open ground " +
                $"{openGroundFraction:P1}; target border continuity " +
                $"{run.ObservedTargetChunkKeys.Count}/" +
                $"{run.ExpectedTargetChunkKeys.Count}; generator " +
                $"{run.GeneratorMilliseconds:0.0} ms of " +
                $"{run.PipelineMilliseconds:0.0} ms pipeline " +
                $"({generatorShare:P1}); errors {run.Errors.Count}.";
            if (run.Errors.Count > 0)
            {
                message += " Error: " + run.Errors[0];
            }
            message +=
                " Temporary PeekChunkColumn output only; no saved or loaded chunk changed.";

            run.Player?.SendMessage(
                GlobalConstants.GeneralChatGroup,
                message,
                EnumChatType.Notification
            );
            if (passed)
            {
                api.Logger.Notification(message);
            }
            else
            {
                api.Logger.Error(message);
            }
        }

        internal TextCommandResult StartFrozenExpanseProbe(
            TextCommandCallingArgs args)
        {
            DangerWorldState? state = activeState;
            if (state == null ||
                !state.Enabled ||
                !state.RealmWorldgenEnabled ||
                state.WorldgenProfile !=
                    WorldZoneLayout.ConcentricRealmsProfile)
            {
                return TextCommandResult.Error(
                    "Frozen Expanse world generation is not active for this save.",
                    "apprentice-frozen-worldgen-disabled"
                );
            }

            List<FrozenProbeTarget> targets =
                BuildFrozenProbeTargets(state);
            if (targets.Count < FrozenProbeRun.RequiredChunks)
            {
                return TextCommandResult.Error(
                    $"Only {targets.Count} safe Frozen Expanse scratch chunks " +
                    $"fit inside this world; {FrozenProbeRun.RequiredChunks} are required.",
                    "apprentice-frozen-probe-space"
                );
            }

            FrozenProbeRun run;
            lock (frozenProbeGate)
            {
                if (activeFrozenProbe != null)
                {
                    return TextCommandResult.Error(
                        "A Frozen Expanse probe is already running.",
                        "apprentice-frozen-probe-active"
                    );
                }

                run = new FrozenProbeRun(
                    args.Caller.Player as IServerPlayer,
                    targets
                );
                activeFrozenProbe = run;
            }

            api.Logger.Notification(
                "[Apprentice] Starting non-destructive Frozen Expanse probe: {0} scratch chunks in Level 5.",
                targets.Count
            );
            api.Event.EnqueueMainThreadTask(
                () => RunNextFrozenProbe(run),
                "apprentice-frozen-probe-start"
            );
            return TextCommandResult.Success(
                $"Frozen Expanse probe started: {targets.Count} scratch chunks. " +
                "It changes no saved or loaded chunk and will report PASS/FAIL here."
            );
        }

        private List<FrozenProbeTarget> BuildFrozenProbeTargets(
            DangerWorldState state)
        {
            double inner = WorldZoneLayout.GetInnerRadius(
                state,
                FrozenExpanseLevel
            );
            double outer = WorldZoneLayout.GetOuterRadius(
                state,
                FrozenExpanseLevel
            );
            double[] radiusOffsets =
            {
                512,
                1024,
                1536,
                2048,
                2752,
                3264,
                3776,
                4480
            };
            List<FrozenProbeTarget> targets = new();
            HashSet<long> selected = new();
            for (int index = 0;
                index < radiusOffsets.Length;
                index++)
            {
                double radius = Math.Min(
                    outer - 256,
                    inner + radiusOffsets[index]
                );
                double angle =
                    index * (Math.PI * 2 / radiusOffsets.Length);
                int chunkX = (int)Math.Floor(
                    (state.AnchorX + Math.Cos(angle) * radius) /
                    ChunkSize
                );
                int chunkZ = (int)Math.Floor(
                    (state.AnchorZ + Math.Sin(angle) * radius) /
                    ChunkSize
                );
                int worldX = chunkX * ChunkSize + ChunkSize / 2;
                int worldZ = chunkZ * ChunkSize + ChunkSize / 2;
                long key =
                    ((long)(uint)chunkX << 32) | (uint)chunkZ;
                if (worldX < ChunkSize ||
                    worldX >= api.WorldManager.MapSizeX - ChunkSize ||
                    worldZ < ChunkSize ||
                    worldZ >= api.WorldManager.MapSizeZ - ChunkSize ||
                    WorldZoneLayout.GetLevelAt(
                        state,
                        worldX,
                        worldZ
                    ) != FrozenExpanseLevel ||
                    !selected.Add(key))
                {
                    continue;
                }

                targets.Add(
                    new FrozenProbeTarget(chunkX, chunkZ)
                );
            }
            return targets;
        }

        private void RunNextFrozenProbe(FrozenProbeRun run)
        {
            if (!IsActiveFrozenProbe(run))
            {
                return;
            }

            FrozenProbeTarget? target;
            lock (run.Sync)
            {
                target = run.NextTarget();
            }
            if (target == null)
            {
                FinishFrozenProbe(run);
                return;
            }

            try
            {
                api.WorldManager.PeekChunkColumn(
                    target.ChunkX,
                    target.ChunkZ,
                    new ChunkPeekOptions
                    {
                        OnGenerated = columns =>
                            OnFrozenProbeChunkGenerated(
                                run,
                                target,
                                columns
                            )
                    }
                );
            }
            catch (Exception exception)
            {
                RecordFrozenProbeError(
                    run,
                    target,
                    "PeekChunkColumn failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
                ScheduleNextFrozenProbe(run);
            }
        }

        private void OnFrozenProbeChunkGenerated(
            FrozenProbeRun run,
            FrozenProbeTarget target,
            Dictionary<Vec2i, IServerChunk[]> columns)
        {
            if (!IsActiveFrozenProbe(run))
            {
                return;
            }

            try
            {
                IServerChunk[]? targetColumn = columns
                    .FirstOrDefault(entry =>
                        entry.Key.X == target.ChunkX &&
                        entry.Key.Y == target.ChunkZ)
                    .Value;
                if (targetColumn == null)
                {
                    RecordFrozenProbeError(
                        run,
                        target,
                        "Peek callback omitted the requested chunk column."
                    );
                }
                else
                {
                    FrozenChunkMetrics metrics =
                        ScanFrozenProbeColumn(
                            target,
                            targetColumn
                        );
                    lock (run.Sync)
                    {
                        run.CompletedChunks++;
                        run.LevelMismatches +=
                            metrics.LevelMismatch ? 1 : 0;
                        run.FrozenSurfaceColumns +=
                            metrics.FrozenSurfaceColumns;
                        run.TreeCoveredColumns +=
                            metrics.TreeCoveredColumns;
                        run.OpenWaterSurfaceColumns +=
                            metrics.OpenWaterSurfaceColumns;
                        run.CaveColumns += metrics.CaveColumns;
                        run.GlacierBlocks += metrics.GlacierBlocks;
                        run.MinimumTerrainY = Math.Min(
                            run.MinimumTerrainY,
                            metrics.MinimumTerrainY
                        );
                        run.MaximumTerrainY = Math.Max(
                            run.MaximumTerrainY,
                            metrics.MaximumTerrainY
                        );
                    }
                }
            }
            catch (Exception exception)
            {
                RecordFrozenProbeError(
                    run,
                    target,
                    "Scratch-column scan failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
            }

            ScheduleNextFrozenProbe(run);
        }

        private FrozenChunkMetrics ScanFrozenProbeColumn(
            FrozenProbeTarget target,
            IServerChunk[] chunks)
        {
            foreach (IServerChunk? chunk in chunks)
            {
                if (chunk != null && !chunk.Disposed)
                {
                    chunk.Unpack_ReadOnly();
                }
            }

            IMapChunk mapChunk = chunks[0].MapChunk;
            ushort[] heights = mapChunk.WorldGenTerrainHeightMap;
            bool[] treeColumns = new bool[ChunkSize * ChunkSize];
            int glacierBlocks = 0;
            for (int chunkY = 0; chunkY < chunks.Length; chunkY++)
            {
                IServerChunk? chunk = chunks[chunkY];
                if (chunk == null || chunk.Disposed)
                {
                    continue;
                }

                IChunkBlocks data = chunk.Data;
                for (int index = 0; index < data.Length; index++)
                {
                    int blockId = data.GetBlockIdUnsafe(index);
                    string path = BlockPath(blockId);
                    if (IsFrozenBlock(path))
                    {
                        glacierBlocks++;
                    }
                    if (!IsTreeBlock(path))
                    {
                        continue;
                    }

                    int localX = index % ChunkSize;
                    int yz = index / ChunkSize;
                    int localZ = yz % ChunkSize;
                    treeColumns[localZ * ChunkSize + localX] = true;
                }
            }

            int frozenSurfaceColumns = 0;
            int openWaterSurfaceColumns = 0;
            int caveColumns = 0;
            int minimumTerrainY = int.MaxValue;
            int maximumTerrainY = 0;
            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                for (int localX = 0; localX < ChunkSize; localX++)
                {
                    int mapIndex = localZ * ChunkSize + localX;
                    int terrainY = heights[mapIndex];
                    minimumTerrainY = Math.Min(
                        minimumTerrainY,
                        terrainY
                    );
                    maximumTerrainY = Math.Max(
                        maximumTerrainY,
                        terrainY
                    );

                    bool frozenSurface = false;
                    bool openWaterSurface = false;
                    for (int offsetY = 1;
                        offsetY >= -3;
                        offsetY--)
                    {
                        string path = BlockPath(
                            GetGeneratedBlockId(
                                chunks,
                                localX,
                                terrainY + offsetY,
                                localZ
                            )
                        );
                        frozenSurface |= IsFrozenBlock(path);
                        openWaterSurface |= IsOpenWater(path);
                    }
                    if (frozenSurface)
                    {
                        frozenSurfaceColumns++;
                    }
                    if (openWaterSurface && !frozenSurface)
                    {
                        openWaterSurfaceColumns++;
                    }

                    int consecutiveAir = 0;
                    int lowestY = Math.Max(2, terrainY - 48);
                    for (int y = terrainY - 6; y >= lowestY; y--)
                    {
                        if (GetGeneratedBlockId(
                                chunks,
                                localX,
                                y,
                                localZ) == 0)
                        {
                            consecutiveAir++;
                            if (consecutiveAir >= 4)
                            {
                                caveColumns++;
                                break;
                            }
                        }
                        else
                        {
                            consecutiveAir = 0;
                        }
                    }
                }
            }

            int worldX =
                target.ChunkX * ChunkSize + ChunkSize / 2;
            int worldZ =
                target.ChunkZ * ChunkSize + ChunkSize / 2;
            return new FrozenChunkMetrics(
                WorldZoneLayout.GetLevelAt(
                    activeState,
                    worldX,
                    worldZ
                ) != FrozenExpanseLevel,
                frozenSurfaceColumns,
                treeColumns.Count(value => value),
                openWaterSurfaceColumns,
                caveColumns,
                glacierBlocks,
                minimumTerrainY,
                maximumTerrainY
            );
        }

        private string BlockPath(int blockId) =>
            blockId > 0 &&
            blockId < api.World.Blocks.Count
                ? api.World.Blocks[blockId]?.Code?.Path ??
                    string.Empty
                : string.Empty;

        private static bool IsFrozenBlock(string path) =>
            path.Contains("glacierice", StringComparison.Ordinal) ||
            path.StartsWith("snowblock", StringComparison.Ordinal) ||
            path.StartsWith("snowlayer-", StringComparison.Ordinal) ||
            path.StartsWith("lakeice", StringComparison.Ordinal);

        private static bool IsOpenWater(string path) =>
            path.StartsWith("water-", StringComparison.Ordinal) ||
            path.Equals("water", StringComparison.Ordinal);

        private static bool IsTreeBlock(string path) =>
            path.StartsWith("log-grown-", StringComparison.Ordinal) ||
            path.StartsWith("leaves", StringComparison.Ordinal);

        private static int GetGeneratedBlockId(
            IServerChunk[] chunks,
            int localX,
            int y,
            int localZ)
        {
            if (y < 0)
            {
                return 0;
            }

            int chunkY = y / ChunkSize;
            if (chunkY < 0 || chunkY >= chunks.Length)
            {
                return 0;
            }

            IServerChunk? chunk = chunks[chunkY];
            if (chunk == null || chunk.Disposed)
            {
                return 0;
            }

            int index =
                ((y % ChunkSize) * ChunkSize + localZ) *
                ChunkSize + localX;
            return chunk.Data.GetBlockIdUnsafe(index);
        }

        private static void RecordFrozenProbeError(
            FrozenProbeRun run,
            FrozenProbeTarget target,
            string error)
        {
            lock (run.Sync)
            {
                run.CompletedChunks++;
                run.Errors.Add(
                    $"chunk {target.ChunkX},{target.ChunkZ}: {error}"
                );
            }
        }

        private void ScheduleNextFrozenProbe(FrozenProbeRun run)
        {
            api.Event.EnqueueMainThreadTask(
                () => RunNextFrozenProbe(run),
                "apprentice-frozen-probe-next"
            );
        }

        private bool IsActiveFrozenProbe(FrozenProbeRun run)
        {
            lock (frozenProbeGate)
            {
                return ReferenceEquals(activeFrozenProbe, run);
            }
        }

        private void FinishFrozenProbe(FrozenProbeRun run)
        {
            lock (frozenProbeGate)
            {
                if (!ReferenceEquals(activeFrozenProbe, run))
                {
                    return;
                }
                activeFrozenProbe = null;
            }

            int totalColumns =
                run.CompletedChunks * ChunkSize * ChunkSize;
            int terrainRange =
                run.MaximumTerrainY - run.MinimumTerrainY;
            bool passed =
                run.Errors.Count == 0 &&
                run.CompletedChunks == run.Targets.Count &&
                run.LevelMismatches == 0 &&
                run.FrozenSurfaceColumns >= totalColumns / 2 &&
                run.TreeCoveredColumns <= totalColumns / 20 &&
                run.OpenWaterSurfaceColumns <= totalColumns / 50 &&
                run.GlacierBlocks > 0 &&
                terrainRange >= 16 &&
                run.CaveColumns > 0;

            StringBuilder summary = new();
            summary.Append("[Apprentice] Frozen Expanse probe ");
            summary.Append(passed ? "PASS" : "FAIL");
            summary.Append(": ");
            summary.Append(
                $"{run.CompletedChunks}/{run.Targets.Count} scratch chunks; " +
                $"frozen surface {run.FrozenSurfaceColumns}/{totalColumns}; " +
                $"glacier/snow blocks {run.GlacierBlocks}; " +
                $"tree-covered columns {run.TreeCoveredColumns}/{totalColumns}; " +
                $"open-water columns {run.OpenWaterSurfaceColumns}/{totalColumns}; " +
                $"terrain Y={run.MinimumTerrainY}-{run.MaximumTerrainY}; " +
                $"subsurface cave columns {run.CaveColumns}; " +
                $"level mismatches {run.LevelMismatches}."
            );
            if (run.Errors.Count > 0)
            {
                summary.Append(" Error: ");
                summary.Append(run.Errors[0]);
            }
            summary.Append(
                " Temporary PeekChunkColumn output only; no saved or loaded chunk changed."
            );

            string message = summary.ToString();
            run.Player?.SendMessage(
                GlobalConstants.GeneralChatGroup,
                message,
                EnumChatType.Notification
            );
            if (passed)
            {
                api.Logger.Notification(message);
            }
            else
            {
                api.Logger.Error(message);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            activeState = null;
            globalConfig = null;
            deepSeaFloorNoise = null;
            iceSpikeGenerator.Reset();
            poisonMireEnvironmentGenerator.Reset();
            shatteredHighlandsSurfaceGenerator.Reset();
            shatteredHighlandsRuinsGenerator.Reset();
            endlessForestLandformIndex = -1;
            shadowForestLandformIndex = -1;
            frozenExpanseLandformIndex = -1;
            ResetPoisonMireWorldgen();
            ResetShatteredHighlandsWorldgen();
            lock (frozenProbeGate)
            {
                activeFrozenProbe = null;
            }
            lock (iceSpikeProbeGate)
            {
                activeIceSpikeProbe = null;
            }
            api.Event.OnTrySpawnEntity -= OnTrySpawnEntity;
            if (structures != null)
            {
                structures.OnPreventSchematicPlaceAt -=
                    OnPreventSchematicPlaceAt;
            }
        }

        private sealed class FrozenProbeTarget
        {
            internal FrozenProbeTarget(int chunkX, int chunkZ)
            {
                ChunkX = chunkX;
                ChunkZ = chunkZ;
            }

            internal int ChunkX { get; }
            internal int ChunkZ { get; }
        }

        private sealed class FrozenProbeRun
        {
            internal const int RequiredChunks = 8;
            private int nextIndex;

            internal FrozenProbeRun(
                IServerPlayer? player,
                IReadOnlyList<FrozenProbeTarget> targets)
            {
                Player = player;
                Targets = targets;
            }

            internal object Sync { get; } = new();
            internal IServerPlayer? Player { get; }
            internal IReadOnlyList<FrozenProbeTarget> Targets { get; }
            internal int CompletedChunks { get; set; }
            internal int LevelMismatches { get; set; }
            internal int FrozenSurfaceColumns { get; set; }
            internal int TreeCoveredColumns { get; set; }
            internal int OpenWaterSurfaceColumns { get; set; }
            internal int CaveColumns { get; set; }
            internal int GlacierBlocks { get; set; }
            internal int MinimumTerrainY { get; set; } = int.MaxValue;
            internal int MaximumTerrainY { get; set; }
            internal List<string> Errors { get; } = new();

            internal FrozenProbeTarget? NextTarget()
            {
                if (nextIndex >= Targets.Count)
                {
                    return null;
                }
                return Targets[nextIndex++];
            }
        }

        private sealed class FrozenChunkMetrics
        {
            internal FrozenChunkMetrics(
                bool levelMismatch,
                int frozenSurfaceColumns,
                int treeCoveredColumns,
                int openWaterSurfaceColumns,
                int caveColumns,
                int glacierBlocks,
                int minimumTerrainY,
                int maximumTerrainY)
            {
                LevelMismatch = levelMismatch;
                FrozenSurfaceColumns = frozenSurfaceColumns;
                TreeCoveredColumns = treeCoveredColumns;
                OpenWaterSurfaceColumns = openWaterSurfaceColumns;
                CaveColumns = caveColumns;
                GlacierBlocks = glacierBlocks;
                MinimumTerrainY = minimumTerrainY;
                MaximumTerrainY = maximumTerrainY;
            }

            internal bool LevelMismatch { get; }
            internal int FrozenSurfaceColumns { get; }
            internal int TreeCoveredColumns { get; }
            internal int OpenWaterSurfaceColumns { get; }
            internal int CaveColumns { get; }
            internal int GlacierBlocks { get; }
            internal int MinimumTerrainY { get; }
            internal int MaximumTerrainY { get; }
        }

        private sealed class IceSpikeProbeRun
        {
            internal const int RequiredChunks = 9;
            private int nextIndex;

            internal IceSpikeProbeRun(
                IServerPlayer? player,
                IceSpikeField field,
                IceSpikeDefinition targetSpike,
                IReadOnlyList<IceSpikeProbeChunk> targets,
                IReadOnlySet<long> expectedTargetChunkKeys)
            {
                Player = player;
                Field = field;
                TargetSpike = targetSpike;
                Targets = targets;
                ExpectedTargetChunkKeys =
                    new HashSet<long>(expectedTargetChunkKeys);
            }

            internal object Sync { get; } = new();
            internal IServerPlayer? Player { get; }
            internal IceSpikeField Field { get; }
            internal IceSpikeDefinition TargetSpike { get; }
            internal IReadOnlyList<IceSpikeProbeChunk> Targets { get; }
            internal HashSet<long> ExpectedTargetChunkKeys { get; }
            internal HashSet<long> ObservedTargetChunkKeys { get; } = new();
            internal HashSet<ulong> IntersectingSpikeIds { get; } = new();
            internal int CompletedChunks { get; set; }
            internal int PlacedBlocks { get; set; }
            internal int ModifiedColumns { get; set; }
            internal int MinimumSpikeHeight { get; set; } = int.MaxValue;
            internal int MaximumSpikeHeight { get; set; }
            internal int TargetSpikeBlocks { get; set; }
            internal int TargetSpikeHeight { get; set; }
            internal double GeneratorMilliseconds { get; set; }
            internal double PipelineMilliseconds { get; set; }
            internal List<string> Errors { get; } = new();

            internal IceSpikeProbeChunk? NextTarget()
            {
                if (nextIndex >= Targets.Count)
                {
                    return null;
                }
                return Targets[nextIndex++];
            }
        }
    }
}
