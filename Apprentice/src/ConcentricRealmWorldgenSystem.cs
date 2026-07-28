using System;
using System.Collections.Concurrent;
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
    /// difficult cliff-and-hill terrain. Later milestones extend this class
    /// one realm at a time.
    /// </summary>
    internal sealed class ConcentricRealmWorldgenSystem :
        IBlockPatchModifier,
        IDisposable
    {
        private const int DesertLevel = 1;
        private const int DeepSeaLevel = 2;
        private const int EndlessForestLevel = 3;
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
        private const string DesertMapMarker =
            "apprentice:concentricRealmsLevel1MapsV1";
        private const string DeepSeaMapMarker =
            "apprentice:concentricRealmsLevel2MapsV1";
        private const string EndlessForestMapMarker =
            "apprentice:concentricRealmsLevel3MapsV1";

        private static readonly BlockPatchConfig EmptyPatches = new()
        {
            Patches = Array.Empty<BlockPatch>(),
            PatchesNonTree = Array.Empty<BlockPatch>()
        };

        private readonly ICoreServerAPI api;
        private readonly DangerDefinition definition;
        private readonly GenStructures? structures;
        private readonly ConcurrentDictionary<long, byte>
            terrainVerifiedRegions = new();
        private DangerWorldState? activeState;
        private GlobalConfig? globalConfig;
        private NormalizedSimplexNoise? deepSeaFloorNoise;
        private static int rewrittenDesertMapRegions;
        private static int rewrittenDeepSeaMapRegions;
        private static int rewrittenEndlessForestMapRegions;
        private static int sculptedDeepSeaChunks;
        private static int sculptedDeepSeaColumns;
        private int endlessForestLandformIndex = -1;
        private int loggedDesertRegions;
        private int loggedDeepSeaRegions;
        private int loggedEndlessForestRegions;
        private int loggedDeepSeaChunks;
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
            mapRegion.SetModdata(DeepSeaMapMarker, new byte[] { 1 });
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
                new byte[] { 1 }
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

        private void OnInitWorldGenerator()
        {
            activeState = null;
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
            loggedDeepSeaChunks = 0;
            globalConfig = null;
            deepSeaFloorNoise = null;
            endlessForestLandformIndex = -1;
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
            api.Logger.Notification(
                "[Apprentice] Concentric realms profile active: Homeland 0-{0:0}, Barren Desert {0:0}-{1:0}, Deep Sea {1:0}-{2:0} (depth {3}, shore width {4}), Endless Forest {2:0}-{5:0} (landform {6}, sea-level climate {7} C/{8} rainfall).",
                state.BaseRadius,
                state.BaseRadius + state.RingWidth,
                state.BaseRadius + state.RingWidth * 2,
                state.DeepSeaDepth,
                state.DeepSeaShoreWidth,
                state.BaseRadius + state.RingWidth * 3,
                EndlessForestLandformCode,
                EndlessForestTemperatureCelsius,
                EndlessForestRainfall
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

            RewriteRealmMaps(mapRegion, state, regionX, regionZ);
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
            RewriteDesertMaps(mapRegion, state, regionX, regionZ);
            RewriteDeepSeaMaps(mapRegion, state, regionX, regionZ);
            RewriteEndlessForestMaps(
                mapRegion,
                state,
                regionX,
                regionZ
            );
        }

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
            mapRegion.SetModdata(DesertMapMarker, new byte[] { 1 });
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

        private static int TransformLevelMap(
            IntDataMap2D map,
            DangerWorldState state,
            int level,
            int regionX,
            int regionZ,
            int regionSize,
            System.Func<int, int> transform)
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
            for (int z = 0; z < map.Size; z++)
            {
                double worldZ = regionOriginZ +
                    (z - map.TopLeftPadding) * blockStep;
                int row = z * map.Size;
                for (int x = 0; x < map.Size; x++)
                {
                    double worldX = regionOriginX +
                        (x - map.TopLeftPadding) * blockStep;
                    if (!WorldZoneLayout.IsLevelAt(
                        state,
                        level,
                        worldX,
                        worldZ))
                    {
                        continue;
                    }

                    int index = row + x;
                    map.Data[index] = transform(map.Data[index]);
                    transformed++;
                }
            }
            return transformed;
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
            if (!insideDesert &&
                !insideDeepSea &&
                !insideEndlessForest)
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

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            activeState = null;
            globalConfig = null;
            deepSeaFloorNoise = null;
            endlessForestLandformIndex = -1;
            api.Event.OnTrySpawnEntity -= OnTrySpawnEntity;
            if (structures != null)
            {
                structures.OnPreventSchematicPlaceAt -=
                    OnPreventSchematicPlaceAt;
            }
        }
    }
}
