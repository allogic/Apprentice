using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Apprentice
{
    internal sealed partial class ConcentricRealmWorldgenSystem
    {
        private const int ShatteredHighlandsLevel = 7;
        private const int ShatteredHighlandsTemperatureCelsius = 8;
        private const int ShatteredHighlandsRainfall = 72;
        private const int ShatteredHighlandsForestDensity = 0;
        private const int ShatteredHighlandsShrubDensity = 0;
        private const int ShatteredHighlandsUpheaval = 255;
        private const int ShatteredHighlandsTransitionWidth = 192;
        private const int ShatteredHighlandsLandformCellSize = 768;
        private const int ShatteredHighlandsRiftPercent = 34;
        private const string ShatteredHighlandsPlateauLandformCode =
            "realisticmountains-quintupleledged";
        private const string ShatteredHighlandsRiftLandformCode =
            "steppedsinkholes";

        private static int rewrittenShatteredHighlandsMapRegions;
        private int shatteredHighlandsPlateauLandformIndex = -1;
        private int shatteredHighlandsRiftLandformIndex = -1;
        private int loggedShatteredHighlandsRegions;
        private readonly object shatteredHighlandsProbeGate = new();
        private ShatteredHighlandsProbeRun?
            activeShatteredHighlandsProbe;

        internal static int RewrittenShatteredHighlandsMapRegions =>
            System.Threading.Volatile.Read(
                ref rewrittenShatteredHighlandsMapRegions
            );

        private bool TryInitializeShatteredHighlandsWorldgen(
            out string error)
        {
            shatteredHighlandsPlateauLandformIndex =
                ResolveLandformIndex(
                    ShatteredHighlandsPlateauLandformCode
                );
            if (shatteredHighlandsPlateauLandformIndex < 0)
            {
                error =
                    $"required Level 7 plateau landform " +
                    $"'{ShatteredHighlandsPlateauLandformCode}' was not loaded";
                return false;
            }

            shatteredHighlandsRiftLandformIndex =
                ResolveLandformIndex(
                    ShatteredHighlandsRiftLandformCode
                );
            if (shatteredHighlandsRiftLandformIndex < 0)
            {
                error =
                    $"required Level 7 rift landform " +
                    $"'{ShatteredHighlandsRiftLandformCode}' was not loaded";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private void ResetShatteredHighlandsWorldgen()
        {
            shatteredHighlandsPlateauLandformIndex = -1;
            shatteredHighlandsRiftLandformIndex = -1;
            loggedShatteredHighlandsRegions = 0;
            System.Threading.Interlocked.Exchange(
                ref rewrittenShatteredHighlandsMapRegions,
                0
            );
            lock (shatteredHighlandsProbeGate)
            {
                activeShatteredHighlandsProbe = null;
            }
        }

        private void RewriteShatteredHighlandsMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            int regionSize = api.WorldManager.RegionSize;
            int climate = ApplyShatteredHighlandsClimateMap(
                mapRegion.ClimateMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int forest = ApplyShatteredHighlandsDensityMap(
                mapRegion.ForestMap,
                state,
                regionX,
                regionZ,
                regionSize,
                ShatteredHighlandsForestDensity
            );
            int shrubs = ApplyShatteredHighlandsDensityMap(
                mapRegion.ShrubMap,
                state,
                regionX,
                regionZ,
                regionSize,
                ShatteredHighlandsShrubDensity
            );
            int ocean = ApplyShatteredHighlandsDensityMap(
                mapRegion.OceanMap,
                state,
                regionX,
                regionZ,
                regionSize,
                0
            );
            int upheaval = ApplyShatteredHighlandsUpheavalMap(
                mapRegion.UpheavelMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int landform = ApplyShatteredHighlandsLandformMap(
                mapRegion.LandformMap,
                state,
                regionX,
                regionZ,
                regionSize
            );

            mapRegion.SetModdata(
                ShatteredHighlandsMapMarker,
                MapMarkerValue
            );
            mapRegion.DirtyForSaving = true;

            if (climate + forest + shrubs + ocean + upheaval + landform <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(
                ref rewrittenShatteredHighlandsMapRegions
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedShatteredHighlandsRegions
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Rewrote Level 7 map region {0},{1}: climate={2}, forest={3}, shrubs={4}, ocean={5}, upheaval={6}, landform={7} ({8}/{9}), transition={10} blocks.",
                    regionX,
                    regionZ,
                    climate,
                    forest,
                    shrubs,
                    ocean,
                    upheaval,
                    landform,
                    ShatteredHighlandsPlateauLandformCode,
                    ShatteredHighlandsRiftLandformCode,
                    ShatteredHighlandsTransitionWidth
                );
            }
        }

        private static int ApplyShatteredHighlandsClimateMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize)
        {
            int targetTemperature = Math.Clamp(
                Climate.DescaleTemperature(
                    ShatteredHighlandsTemperatureCelsius
                ),
                0,
                255
            );
            return TransformLevelMap(
                map,
                state,
                ShatteredHighlandsLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                {
                    double blend = GetShatteredHighlandsBlend(
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
                        ShatteredHighlandsRainfall,
                        blend
                    );
                    return (temperature << 16) |
                        (rainfall << 8) |
                        (existing & 0xff);
                }
            );
        }

        private static int ApplyShatteredHighlandsDensityMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize,
            int targetDensity) =>
            TransformLevelMap(
                map,
                state,
                ShatteredHighlandsLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    BlendByte(
                        existing,
                        targetDensity,
                        GetShatteredHighlandsBlend(
                            state,
                            worldX,
                            worldZ
                        )
                    )
            );

        private static int ApplyShatteredHighlandsUpheavalMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize) =>
            TransformLevelMap(
                map,
                state,
                ShatteredHighlandsLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    BlendByte(
                        existing,
                        ShatteredHighlandsUpheaval,
                        GetShatteredHighlandsBlend(
                            state,
                            worldX,
                            worldZ
                        )
                    )
            );

        private int ApplyShatteredHighlandsLandformMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize) =>
            TransformLevelMap(
                map,
                state,
                ShatteredHighlandsLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    GetShatteredHighlandsBlend(
                        state,
                        worldX,
                        worldZ
                    ) >= 0.5
                        ? SelectShatteredHighlandsLandform(
                            worldX,
                            worldZ
                        )
                        : existing
            );

        private int SelectShatteredHighlandsLandform(
            double worldX,
            double worldZ)
        {
            int cellX = (int)Math.Floor(
                worldX / ShatteredHighlandsLandformCellSize
            );
            int cellZ = (int)Math.Floor(
                worldZ / ShatteredHighlandsLandformCellSize
            );
            ulong hash = StableShatteredHighlandsCellHash(
                cellX,
                cellZ
            );
            return hash % 100 < ShatteredHighlandsRiftPercent
                ? shatteredHighlandsRiftLandformIndex
                : shatteredHighlandsPlateauLandformIndex;
        }

        private static double GetShatteredHighlandsBlend(
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
                    ShatteredHighlandsLevel
                );
            double outerRadius =
                WorldZoneLayout.GetOuterRadius(
                    state,
                    ShatteredHighlandsLevel
                );
            double distanceFromEdge = Math.Min(
                distance - innerRadius,
                outerRadius - distance
            );
            double normalized = Math.Clamp(
                distanceFromEdge /
                    ShatteredHighlandsTransitionWidth,
                0,
                1
            );
            return normalized * normalized * (3 - 2 * normalized);
        }

        private static ulong StableShatteredHighlandsCellHash(
            int cellX,
            int cellZ)
        {
            unchecked
            {
                ulong value =
                    (uint)cellX * 0x9E3779B185EBCA87UL ^
                    (uint)cellZ * 0xC2B2AE3D27D4EB4FUL ^
                    0x5348415454455245UL;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        internal TextCommandResult StartShatteredHighlandsProbe(
            TextCommandCallingArgs args)
        {
            DangerWorldState? state = activeState;
            if (state == null ||
                !state.Enabled ||
                !state.RealmWorldgenEnabled ||
                state.WorldgenProfile !=
                    WorldZoneLayout.ConcentricRealmsProfile ||
                shatteredHighlandsPlateauLandformIndex < 0 ||
                shatteredHighlandsRiftLandformIndex < 0)
            {
                return TextCommandResult.Error(
                    "Shattered Highlands world generation is not active for this save.",
                    "apprentice-shattered-highlands-worldgen-disabled"
                );
            }

            List<ShatteredHighlandsProbeTarget> targets =
                BuildShatteredHighlandsProbeTargets(state);
            if (targets.Count <
                ShatteredHighlandsProbeRun.RequiredChunks)
            {
                return TextCommandResult.Error(
                    $"Only {targets.Count} safe Shattered Highlands scratch " +
                    $"chunks fit inside this world; " +
                    $"{ShatteredHighlandsProbeRun.RequiredChunks} are required.",
                    "apprentice-shattered-highlands-probe-space"
                );
            }

            ShatteredHighlandsProbeRun run;
            lock (shatteredHighlandsProbeGate)
            {
                if (activeShatteredHighlandsProbe != null)
                {
                    return TextCommandResult.Error(
                        "A Shattered Highlands probe is already running.",
                        "apprentice-shattered-highlands-probe-active"
                    );
                }

                run = new ShatteredHighlandsProbeRun(
                    args.Caller.Player as IServerPlayer,
                    targets,
                    ShatteredHighlandsRuinsGenerator.GeneratedCities,
                    ShatteredHighlandsRuinsGenerator.GeneratedModules
                );
                activeShatteredHighlandsProbe = run;
            }

            api.Logger.Notification(
                "[Apprentice] Starting non-destructive Shattered Highlands probe: {0} scratch chunks in Level 7.",
                targets.Count
            );
            api.Event.EnqueueMainThreadTask(
                () => RunNextShatteredHighlandsProbe(run),
                "apprentice-shattered-highlands-probe-start"
            );
            return TextCommandResult.Success(
                $"Shattered Highlands probe started: {targets.Count} " +
                "scratch chunks. It changes no saved or loaded chunk and " +
                "will report PASS/FAIL here."
            );
        }

        internal TextCommandResult StartShatteredHighlandsRuinsProbe(
            TextCommandCallingArgs args) =>
            StartShatteredHighlandsProbe(args);

        internal TextCommandResult LocateNearestShatteredHighlandsRuins(
            TextCommandCallingArgs args) =>
            shatteredHighlandsRuinsGenerator
                .LocateNearestPlannedCity(
                args,
                activeState
            );

        private List<ShatteredHighlandsProbeTarget>
            BuildShatteredHighlandsProbeTargets(
                DangerWorldState state)
        {
            double inner = WorldZoneLayout.GetInnerRadius(
                state,
                ShatteredHighlandsLevel
            );
            double outer = WorldZoneLayout.GetOuterRadius(
                state,
                ShatteredHighlandsLevel
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
            List<ShatteredHighlandsProbeTarget> targets = new();
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
                    ) != ShatteredHighlandsLevel ||
                    !selected.Add(key))
                {
                    continue;
                }

                targets.Add(
                    new ShatteredHighlandsProbeTarget(
                        chunkX,
                        chunkZ
                    )
                );
            }

            if (shatteredHighlandsRuinsGenerator
                    .TryGetProbeCityCenter(
                        state,
                        out int cityChunkX,
                        out int cityChunkZ))
            {
                int[] cityOffsets =
                {
                    -4,
                    0,
                    4
                };
                foreach (int offsetZ in cityOffsets)
                {
                    foreach (int offsetX in cityOffsets)
                    {
                        int chunkX =
                            cityChunkX + offsetX;
                        int chunkZ =
                            cityChunkZ + offsetZ;
                        long key =
                            ((long)(uint)chunkX << 32) |
                            (uint)chunkZ;
                        if (!selected.Add(key))
                        {
                            continue;
                        }
                        targets.Add(
                            new ShatteredHighlandsProbeTarget(
                                chunkX,
                                chunkZ
                            )
                        );
                    }
                }
            }
            return targets;
        }

        private void RunNextShatteredHighlandsProbe(
            ShatteredHighlandsProbeRun run)
        {
            if (!IsActiveShatteredHighlandsProbe(run))
            {
                return;
            }

            ShatteredHighlandsProbeTarget? target;
            lock (run.Sync)
            {
                target = run.NextTarget();
            }
            if (target == null)
            {
                FinishShatteredHighlandsProbe(run);
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
                            OnShatteredHighlandsProbeChunkGenerated(
                                run,
                                target,
                                columns
                            )
                    }
                );
            }
            catch (Exception exception)
            {
                RecordShatteredHighlandsProbeError(
                    run,
                    target,
                    "PeekChunkColumn failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
                ScheduleNextShatteredHighlandsProbe(run);
            }
        }

        private void OnShatteredHighlandsProbeChunkGenerated(
            ShatteredHighlandsProbeRun run,
            ShatteredHighlandsProbeTarget target,
            Dictionary<Vec2i, IServerChunk[]> columns)
        {
            if (!IsActiveShatteredHighlandsProbe(run))
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
                    RecordShatteredHighlandsProbeError(
                        run,
                        target,
                        "Peek callback omitted the requested chunk column."
                    );
                }
                else
                {
                    ShatteredHighlandsChunkMetrics metrics =
                        ScanShatteredHighlandsProbeColumn(
                            target,
                            targetColumn
                        );
                    lock (run.Sync)
                    {
                        run.CompletedChunks++;
                        run.LevelMismatches +=
                            metrics.LevelMismatch ? 1 : 0;
                        run.DryLandColumns +=
                            metrics.DryLandColumns;
                        run.WaterColumns +=
                            metrics.WaterColumns;
                        run.GentleGroundColumns +=
                            metrics.GentleGroundColumns;
                        run.PlateauColumns +=
                            metrics.PlateauColumns;
                        run.CliffEdgeColumns +=
                            metrics.CliffEdgeColumns;
                        run.DeepRiftColumns +=
                            metrics.DeepRiftColumns;
                        run.ExposedRockColumns +=
                            metrics.ExposedRockColumns;
                        run.TreeCoveredColumns +=
                            metrics.TreeCoveredColumns;
                        run.RouteChunks +=
                            metrics.HasGroundRoute ? 1 : 0;
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
                RecordShatteredHighlandsProbeError(
                    run,
                    target,
                    "Scratch-column scan failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
            }

            ScheduleNextShatteredHighlandsProbe(run);
        }

        private ShatteredHighlandsChunkMetrics
            ScanShatteredHighlandsProbeColumn(
                ShatteredHighlandsProbeTarget target,
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
            ushort[] terrainHeights =
                mapChunk.WorldGenTerrainHeightMap;
            ushort[] rainHeights = mapChunk.RainHeightMap;
            bool[] treeColumns = new bool[ChunkSize * ChunkSize];
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
                    string path = BlockPath(
                        data.GetBlockIdUnsafe(index)
                    );
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

            bool[] dryColumns = new bool[ChunkSize * ChunkSize];
            int dryLandColumns = 0;
            int waterColumns = 0;
            int exposedRockColumns = 0;
            int minimumTerrainY = int.MaxValue;
            int maximumTerrainY = 0;
            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                int row = localZ * ChunkSize;
                for (int localX = 0;
                    localX < ChunkSize;
                    localX++)
                {
                    int mapIndex = row + localX;
                    int terrainY = terrainHeights[mapIndex];
                    int rainY = Math.Max(
                        terrainY,
                        rainHeights[mapIndex]
                    );
                    minimumTerrainY = Math.Min(
                        minimumTerrainY,
                        terrainY
                    );
                    maximumTerrainY = Math.Max(
                        maximumTerrainY,
                        terrainY
                    );

                    bool hasWater = false;
                    int maximumScanY = Math.Min(
                        rainY,
                        api.WorldManager.MapSizeY - 1
                    );
                    for (int y = terrainY + 1;
                        y <= maximumScanY;
                        y++)
                    {
                        int fluidId = GetGeneratedFluidId(
                            chunks,
                            localX,
                            y,
                            localZ
                        );
                        int solidId = GetGeneratedBlockId(
                            chunks,
                            localX,
                            y,
                            localZ
                        );
                        string path = BlockPath(
                            fluidId > 0 ? fluidId : solidId
                        );
                        if (IsAnyWater(path))
                        {
                            hasWater = true;
                            break;
                        }
                    }

                    if (hasWater)
                    {
                        waterColumns++;
                    }
                    else
                    {
                        dryColumns[mapIndex] = true;
                        dryLandColumns++;
                    }

                    if (ColumnExposesRock(
                            chunks,
                            localX,
                            terrainY,
                            localZ))
                    {
                        exposedRockColumns++;
                    }
                }
            }

            int gentleGroundColumns = 0;
            int plateauColumns = 0;
            int cliffEdgeColumns = 0;
            int deepRiftColumns = 0;
            for (int localZ = 1; localZ < ChunkSize - 1; localZ++)
            {
                for (int localX = 1;
                    localX < ChunkSize - 1;
                    localX++)
                {
                    int mapIndex = localZ * ChunkSize + localX;
                    if (!dryColumns[mapIndex])
                    {
                        continue;
                    }

                    int height = terrainHeights[mapIndex];
                    int maximumDifference = Math.Max(
                        Math.Max(
                            Math.Abs(
                                height -
                                terrainHeights[mapIndex - 1]
                            ),
                            Math.Abs(
                                height -
                                terrainHeights[mapIndex + 1]
                            )
                        ),
                        Math.Max(
                            Math.Abs(
                                height -
                                terrainHeights[
                                    mapIndex - ChunkSize
                                ]
                            ),
                            Math.Abs(
                                height -
                                terrainHeights[
                                    mapIndex + ChunkSize
                                ]
                            )
                        )
                    );
                    if (maximumDifference <= 2)
                    {
                        gentleGroundColumns++;
                    }
                    if (maximumDifference <= 3)
                    {
                        plateauColumns++;
                    }
                    if (maximumDifference >= 8)
                    {
                        cliffEdgeColumns++;
                    }
                    if (height <= maximumTerrainY - 24)
                    {
                        deepRiftColumns++;
                    }
                }
            }

            int worldX =
                target.ChunkX * ChunkSize + ChunkSize / 2;
            int worldZ =
                target.ChunkZ * ChunkSize + ChunkSize / 2;
            return new ShatteredHighlandsChunkMetrics(
                WorldZoneLayout.GetLevelAt(
                    activeState,
                    worldX,
                    worldZ
                ) != ShatteredHighlandsLevel,
                dryLandColumns,
                waterColumns,
                gentleGroundColumns,
                plateauColumns,
                cliffEdgeColumns,
                deepRiftColumns,
                exposedRockColumns,
                treeColumns.Count(value => value),
                minimumTerrainY,
                maximumTerrainY,
                HasGroundRouteAcrossChunk(
                    dryColumns,
                    terrainHeights
                )
            );
        }

        private bool ColumnExposesRock(
            IServerChunk[] chunks,
            int localX,
            int terrainY,
            int localZ)
        {
            for (int offsetY = 0; offsetY >= -2; offsetY--)
            {
                string path = BlockPath(
                    GetGeneratedBlockId(
                        chunks,
                        localX,
                        terrainY + offsetY,
                        localZ
                    )
                );
                if (IsRockPath(path))
                {
                    return true;
                }
                if (!path.StartsWith(
                        "snow",
                        StringComparison.Ordinal) &&
                    !path.StartsWith(
                        "loose",
                        StringComparison.Ordinal))
                {
                    break;
                }
            }
            return false;
        }

        private static bool IsRockPath(string path) =>
            path.StartsWith("rock-", StringComparison.Ordinal) ||
            path.StartsWith("crackedrock-", StringComparison.Ordinal) ||
            path.StartsWith("ore-", StringComparison.Ordinal) ||
            path.StartsWith("looseores-", StringComparison.Ordinal);

        private static bool IsAnyWater(string path) =>
            path.Equals("water", StringComparison.Ordinal) ||
            path.StartsWith("water-", StringComparison.Ordinal) ||
            path.Equals("saltwater", StringComparison.Ordinal) ||
            path.StartsWith("saltwater-", StringComparison.Ordinal) ||
            path.Equals("toxicwater", StringComparison.Ordinal) ||
            path.StartsWith("toxicwater-", StringComparison.Ordinal);

        private static bool HasGroundRouteAcrossChunk(
            bool[] dryColumns,
            ushort[] heights) =>
            CanReachOppositeEdge(
                dryColumns,
                heights,
                horizontal: true
            ) ||
            CanReachOppositeEdge(
                dryColumns,
                heights,
                horizontal: false
            );

        private static bool CanReachOppositeEdge(
            bool[] dryColumns,
            ushort[] heights,
            bool horizontal)
        {
            bool[] visited = new bool[dryColumns.Length];
            Queue<int> pending = new();
            for (int offset = 0; offset < ChunkSize; offset++)
            {
                int index = horizontal
                    ? offset * ChunkSize
                    : offset;
                if (!dryColumns[index])
                {
                    continue;
                }
                visited[index] = true;
                pending.Enqueue(index);
            }

            int[] offsetX = { -1, 1, 0, 0 };
            int[] offsetZ = { 0, 0, -1, 1 };
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                int currentX = current % ChunkSize;
                int currentZ = current / ChunkSize;
                if ((horizontal &&
                        currentX == ChunkSize - 1) ||
                    (!horizontal &&
                        currentZ == ChunkSize - 1))
                {
                    return true;
                }

                for (int direction = 0;
                    direction < offsetX.Length;
                    direction++)
                {
                    int nextX = currentX + offsetX[direction];
                    int nextZ = currentZ + offsetZ[direction];
                    if (nextX < 0 || nextX >= ChunkSize ||
                        nextZ < 0 || nextZ >= ChunkSize)
                    {
                        continue;
                    }

                    int next = nextZ * ChunkSize + nextX;
                    if (visited[next] ||
                        !dryColumns[next] ||
                        Math.Abs(
                            heights[current] - heights[next]
                        ) > 2)
                    {
                        continue;
                    }

                    visited[next] = true;
                    pending.Enqueue(next);
                }
            }

            return false;
        }

        private static void RecordShatteredHighlandsProbeError(
            ShatteredHighlandsProbeRun run,
            ShatteredHighlandsProbeTarget target,
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

        private void ScheduleNextShatteredHighlandsProbe(
            ShatteredHighlandsProbeRun run)
        {
            api.Event.EnqueueMainThreadTask(
                () => RunNextShatteredHighlandsProbe(run),
                "apprentice-shattered-highlands-probe-next"
            );
        }

        private bool IsActiveShatteredHighlandsProbe(
            ShatteredHighlandsProbeRun run)
        {
            lock (shatteredHighlandsProbeGate)
            {
                return ReferenceEquals(
                    activeShatteredHighlandsProbe,
                    run
                );
            }
        }

        private void FinishShatteredHighlandsProbe(
            ShatteredHighlandsProbeRun run)
        {
            lock (shatteredHighlandsProbeGate)
            {
                if (!ReferenceEquals(
                        activeShatteredHighlandsProbe,
                        run))
                {
                    return;
                }
                activeShatteredHighlandsProbe = null;
            }

            int totalColumns =
                run.CompletedChunks * ChunkSize * ChunkSize;
            int terrainRange =
                run.MaximumTerrainY - run.MinimumTerrainY;
            long generatedCities =
                ShatteredHighlandsRuinsGenerator.GeneratedCities -
                run.InitialGeneratedCities;
            long generatedModules =
                ShatteredHighlandsRuinsGenerator.GeneratedModules -
                run.InitialGeneratedModules;
            bool passed =
                run.Errors.Count == 0 &&
                run.CompletedChunks == run.Targets.Count &&
                run.LevelMismatches == 0 &&
                run.DryLandColumns >= totalColumns * 9 / 10 &&
                run.WaterColumns <= totalColumns / 20 &&
                run.GentleGroundColumns >= totalColumns / 12 &&
                run.PlateauColumns >= totalColumns / 10 &&
                run.CliffEdgeColumns >= totalColumns / 25 &&
                run.DeepRiftColumns >= totalColumns / 25 &&
                run.ExposedRockColumns >= totalColumns / 6 &&
                run.TreeCoveredColumns <= totalColumns / 10 &&
                run.RouteChunks >=
                    ShatteredHighlandsProbeRun.RequiredChunks / 2 &&
                terrainRange >= 56 &&
                terrainRange <= api.WorldManager.MapSizeY - 8 &&
                generatedCities >= 1 &&
                generatedModules >= 8;

            StringBuilder summary = new();
            summary.Append(
                "[Apprentice] Shattered Highlands probe "
            );
            summary.Append(passed ? "PASS" : "FAIL");
            summary.Append(": ");
            summary.Append(
                $"{run.CompletedChunks}/{run.Targets.Count} scratch chunks; " +
                $"dry land {run.DryLandColumns}/{totalColumns}; " +
                $"water columns {run.WaterColumns}; " +
                $"gentle ground {run.GentleGroundColumns}; " +
                $"plateau columns {run.PlateauColumns}; " +
                $"cliff-edge columns {run.CliffEdgeColumns}; " +
                $"deep-rift columns {run.DeepRiftColumns}; " +
                $"exposed-rock columns {run.ExposedRockColumns}; " +
                $"tree-covered columns {run.TreeCoveredColumns}; " +
                $"ground-route chunks {run.RouteChunks}/" +
                $"{run.CompletedChunks}; " +
                $"ruin modules generated " +
                $"{generatedModules}; " +
                $"city landmarks generated " +
                $"{generatedCities}; " +
                $"terrain Y={run.MinimumTerrainY}-" +
                $"{run.MaximumTerrainY}; " +
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

        private sealed class ShatteredHighlandsProbeTarget
        {
            internal ShatteredHighlandsProbeTarget(
                int chunkX,
                int chunkZ)
            {
                ChunkX = chunkX;
                ChunkZ = chunkZ;
            }

            internal int ChunkX { get; }
            internal int ChunkZ { get; }
        }

        private sealed class ShatteredHighlandsProbeRun
        {
            internal const int RequiredChunks = 8;
            private int nextIndex;

            internal ShatteredHighlandsProbeRun(
                IServerPlayer? player,
                IReadOnlyList<ShatteredHighlandsProbeTarget> targets,
                long initialGeneratedCities,
                long initialGeneratedModules)
            {
                Player = player;
                Targets = targets;
                InitialGeneratedCities =
                    initialGeneratedCities;
                InitialGeneratedModules =
                    initialGeneratedModules;
            }

            internal object Sync { get; } = new();
            internal IServerPlayer? Player { get; }
            internal IReadOnlyList<ShatteredHighlandsProbeTarget>
                Targets { get; }
            internal long InitialGeneratedCities { get; }
            internal long InitialGeneratedModules { get; }
            internal int CompletedChunks { get; set; }
            internal int LevelMismatches { get; set; }
            internal int DryLandColumns { get; set; }
            internal int WaterColumns { get; set; }
            internal int GentleGroundColumns { get; set; }
            internal int PlateauColumns { get; set; }
            internal int CliffEdgeColumns { get; set; }
            internal int DeepRiftColumns { get; set; }
            internal int ExposedRockColumns { get; set; }
            internal int TreeCoveredColumns { get; set; }
            internal int RouteChunks { get; set; }
            internal int MinimumTerrainY { get; set; } = int.MaxValue;
            internal int MaximumTerrainY { get; set; }
            internal List<string> Errors { get; } = new();

            internal ShatteredHighlandsProbeTarget? NextTarget()
            {
                if (nextIndex >= Targets.Count)
                {
                    return null;
                }

                return Targets[nextIndex++];
            }
        }

        private readonly record struct
            ShatteredHighlandsChunkMetrics(
                bool LevelMismatch,
                int DryLandColumns,
                int WaterColumns,
                int GentleGroundColumns,
                int PlateauColumns,
                int CliffEdgeColumns,
                int DeepRiftColumns,
                int ExposedRockColumns,
                int TreeCoveredColumns,
                int MinimumTerrainY,
                int MaximumTerrainY,
                bool HasGroundRoute);
    }
}
