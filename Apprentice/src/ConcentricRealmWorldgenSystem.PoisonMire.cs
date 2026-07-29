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
        private const int PoisonMireLevel = 6;
        private const int PoisonMireTemperatureCelsius = 20;
        private const int PoisonMireRainfall = 210;
        private const int PoisonMireForestDensity = 0;
        private const int PoisonMireShrubDensity = 0;
        private const int PoisonMireUpheaval = 32;
        private const int PoisonMireTransitionWidth = 192;
        private const string PoisonMireLandformCode = "marsh";

        private static int rewrittenPoisonMireMapRegions;
        private int poisonMireLandformIndex = -1;
        private int loggedPoisonMireRegions;
        private readonly object poisonMireProbeGate = new();
        private PoisonMireProbeRun? activePoisonMireProbe;

        internal static int RewrittenPoisonMireMapRegions =>
            System.Threading.Volatile.Read(
                ref rewrittenPoisonMireMapRegions
            );

        private void RewritePoisonMireMaps(
            IMapRegion mapRegion,
            DangerWorldState state,
            int regionX,
            int regionZ)
        {
            int regionSize = api.WorldManager.RegionSize;
            int climate = ApplyPoisonMireClimateMap(
                mapRegion.ClimateMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int forest = ApplyPoisonMireDensityMap(
                mapRegion.ForestMap,
                state,
                regionX,
                regionZ,
                regionSize,
                PoisonMireForestDensity
            );
            int shrubs = ApplyPoisonMireDensityMap(
                mapRegion.ShrubMap,
                state,
                regionX,
                regionZ,
                regionSize,
                PoisonMireShrubDensity
            );
            // Oceanicity would turn the mire into a salt-water sea. Clearing
            // it keeps every naturally flooded lowland fresh water.
            int ocean = ClearLevelMap(
                mapRegion.OceanMap,
                state,
                PoisonMireLevel,
                regionX,
                regionZ,
                regionSize
            );
            int upheaval = ApplyPoisonMireUpheavalMap(
                mapRegion.UpheavelMap,
                state,
                regionX,
                regionZ,
                regionSize
            );
            int landform = poisonMireLandformIndex >= 0
                ? ApplyPoisonMireLandformMap(
                    mapRegion.LandformMap,
                    state,
                    regionX,
                    regionZ,
                    regionSize,
                    poisonMireLandformIndex
                )
                : 0;

            mapRegion.SetModdata(
                PoisonMireMapMarker,
                MapMarkerValue
            );
            mapRegion.DirtyForSaving = true;

            if (climate + forest + shrubs + ocean + upheaval + landform <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(
                ref rewrittenPoisonMireMapRegions
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedPoisonMireRegions
                ) <= 12)
            {
                api.Logger.Notification(
                    "[Apprentice] Rewrote Level 6 map region {0},{1}: climate={2}, forest={3}, shrubs={4}, ocean={5}, upheaval={6}, landform={7} ({8}), transition={9} blocks.",
                    regionX,
                    regionZ,
                    climate,
                    forest,
                    shrubs,
                    ocean,
                    upheaval,
                    landform,
                    PoisonMireLandformCode,
                    PoisonMireTransitionWidth
                );
            }
        }

        private static int ApplyPoisonMireClimateMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize)
        {
            int targetTemperature = Math.Clamp(
                Climate.DescaleTemperature(
                    PoisonMireTemperatureCelsius
                ),
                0,
                255
            );
            return TransformLevelMap(
                map,
                state,
                PoisonMireLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                {
                    double blend = GetPoisonMireBlend(
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
                        PoisonMireRainfall,
                        blend
                    );
                    return (temperature << 16) |
                        (rainfall << 8) |
                        (existing & 0xff);
                }
            );
        }

        private static int ApplyPoisonMireDensityMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize,
            int targetDensity) =>
            TransformLevelMap(
                map,
                state,
                PoisonMireLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    BlendByte(
                        existing,
                        targetDensity,
                        GetPoisonMireBlend(
                            state,
                            worldX,
                            worldZ
                        )
                    )
            );

        private static int ApplyPoisonMireUpheavalMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize) =>
            TransformLevelMap(
                map,
                state,
                PoisonMireLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    BlendByte(
                        existing,
                        PoisonMireUpheaval,
                        GetPoisonMireBlend(
                            state,
                            worldX,
                            worldZ
                        )
                    )
            );

        private static int ApplyPoisonMireLandformMap(
            IntDataMap2D map,
            DangerWorldState state,
            int regionX,
            int regionZ,
            int regionSize,
            int landformIndex) =>
            TransformLevelMap(
                map,
                state,
                PoisonMireLevel,
                regionX,
                regionZ,
                regionSize,
                (existing, worldX, worldZ) =>
                    GetPoisonMireBlend(
                        state,
                        worldX,
                        worldZ
                    ) >= 0.5
                        ? landformIndex
                        : existing
            );

        private static double GetPoisonMireBlend(
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
                    PoisonMireLevel
                );
            double outerRadius =
                WorldZoneLayout.GetOuterRadius(
                    state,
                    PoisonMireLevel
                );
            double distanceFromEdge = Math.Min(
                distance - innerRadius,
                outerRadius - distance
            );
            double normalized = Math.Clamp(
                distanceFromEdge /
                    PoisonMireTransitionWidth,
                0,
                1
            );
            return normalized * normalized * (3 - 2 * normalized);
        }

        private void ResetPoisonMireWorldgen()
        {
            poisonMireLandformIndex = -1;
            loggedPoisonMireRegions = 0;
            System.Threading.Interlocked.Exchange(
                ref rewrittenPoisonMireMapRegions,
                0
            );
            lock (poisonMireProbeGate)
            {
                activePoisonMireProbe = null;
            }
        }

        internal TextCommandResult StartPoisonMireProbe(
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
                    "Poison Mire world generation is not active for this save.",
                    "apprentice-poison-mire-worldgen-disabled"
                );
            }
            if (!poisonMireEnvironmentGenerator.Initialized)
            {
                return TextCommandResult.Error(
                    "Poison Mire terrain is active, but its environment layer did not initialize. Check the server log for the missing block code.",
                    "apprentice-poison-mire-environment-disabled"
                );
            }

            List<PoisonMireProbeTarget> targets =
                BuildPoisonMireProbeTargets(state);
            if (targets.Count < PoisonMireProbeRun.RequiredChunks)
            {
                return TextCommandResult.Error(
                    $"Only {targets.Count} safe Poison Mire scratch chunks " +
                    $"fit inside this world; {PoisonMireProbeRun.RequiredChunks} are required.",
                    "apprentice-poison-mire-probe-space"
                );
            }

            PoisonMireProbeRun run;
            lock (poisonMireProbeGate)
            {
                if (activePoisonMireProbe != null)
                {
                    return TextCommandResult.Error(
                        "A Poison Mire probe is already running.",
                        "apprentice-poison-mire-probe-active"
                    );
                }

                List<PoisonMireProbeTarget> preparedTargets = new();
                foreach (PoisonMireProbeTarget target in targets)
                {
                    if (poisonMireEnvironmentGenerator.PrepareProbeChunk(
                            target.ChunkX,
                            target.ChunkZ))
                    {
                        preparedTargets.Add(target);
                        continue;
                    }

                    foreach (PoisonMireProbeTarget prepared in preparedTargets)
                    {
                        poisonMireEnvironmentGenerator.CancelProbeChunk(
                            prepared.ChunkX,
                            prepared.ChunkZ
                        );
                    }
                    return TextCommandResult.Error(
                        $"Could not reserve scratch chunk {target.ChunkX},{target.ChunkZ} for the Poison Mire environment probe.",
                        "apprentice-poison-mire-environment-probe-reservation"
                    );
                }

                run = new PoisonMireProbeRun(
                    args.Caller.Player as IServerPlayer,
                    targets
                );
                activePoisonMireProbe = run;
            }

            api.Logger.Notification(
                "[Apprentice] Starting non-destructive Poison Mire probe: {0} scratch chunks in Level 6.",
                targets.Count
            );
            api.Event.EnqueueMainThreadTask(
                () => RunNextPoisonMireProbe(run),
                "apprentice-poison-mire-probe-start"
            );
            return TextCommandResult.Success(
                $"Poison Mire probe started: {targets.Count} scratch chunks. " +
                "It changes no saved or loaded chunk and will report PASS/FAIL here."
            );
        }

        private List<PoisonMireProbeTarget> BuildPoisonMireProbeTargets(
            DangerWorldState state)
        {
            double inner = WorldZoneLayout.GetInnerRadius(
                state,
                PoisonMireLevel
            );
            double outer = WorldZoneLayout.GetOuterRadius(
                state,
                PoisonMireLevel
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
            List<PoisonMireProbeTarget> targets = new();
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
                    ) != PoisonMireLevel ||
                    !selected.Add(key))
                {
                    continue;
                }

                targets.Add(
                    new PoisonMireProbeTarget(chunkX, chunkZ)
                );
            }
            return targets;
        }

        private void RunNextPoisonMireProbe(PoisonMireProbeRun run)
        {
            if (!IsActivePoisonMireProbe(run))
            {
                return;
            }

            PoisonMireProbeTarget? target;
            lock (run.Sync)
            {
                target = run.NextTarget();
            }
            if (target == null)
            {
                FinishPoisonMireProbe(run);
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
                            OnPoisonMireProbeChunkGenerated(
                                run,
                                target,
                                columns
                            )
                    }
                );
            }
            catch (Exception exception)
            {
                poisonMireEnvironmentGenerator.CancelProbeChunk(
                    target.ChunkX,
                    target.ChunkZ
                );
                RecordPoisonMireProbeError(
                    run,
                    target,
                    "PeekChunkColumn failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
                ScheduleNextPoisonMireProbe(run);
            }
        }

        private void OnPoisonMireProbeChunkGenerated(
            PoisonMireProbeRun run,
            PoisonMireProbeTarget target,
            Dictionary<Vec2i, IServerChunk[]> columns)
        {
            if (!IsActivePoisonMireProbe(run))
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
                    poisonMireEnvironmentGenerator.CancelProbeChunk(
                        target.ChunkX,
                        target.ChunkZ
                    );
                    RecordPoisonMireProbeError(
                        run,
                        target,
                        "Peek callback omitted the requested chunk column."
                    );
                }
                else
                {
                    PoisonMireChunkMetrics metrics =
                        ScanPoisonMireProbeColumn(
                            target,
                            targetColumn
                        );
                    bool hasEnvironmentTrace =
                        poisonMireEnvironmentGenerator.TryTakeProbeTrace(
                            target.ChunkX,
                            target.ChunkZ,
                            out MireEnvironmentChunkTrace?
                                environmentTrace
                        );
                    lock (run.Sync)
                    {
                        run.CompletedChunks++;
                        run.LevelMismatches +=
                            metrics.LevelMismatch ? 1 : 0;
                        run.DryLandColumns +=
                            metrics.DryLandColumns;
                        run.TraversableDryColumns +=
                            metrics.TraversableDryColumns;
                        run.FreshWaterColumns +=
                            metrics.FreshWaterColumns;
                        run.VanillaFreshWaterColumns +=
                            metrics.VanillaFreshWaterColumns;
                        run.ShallowFreshWaterColumns +=
                            metrics.ShallowFreshWaterColumns;
                        run.DeepFreshWaterColumns +=
                            metrics.DeepFreshWaterColumns;
                        run.SaltWaterColumns +=
                            metrics.SaltWaterColumns;
                        run.TreeCoveredColumns +=
                            metrics.TreeCoveredColumns;
                        run.MinimumTerrainY = Math.Min(
                            run.MinimumTerrainY,
                            metrics.MinimumTerrainY
                        );
                        run.MaximumTerrainY = Math.Max(
                            run.MaximumTerrainY,
                            metrics.MaximumTerrainY
                        );
                        run.ScannedDeadLogBlocks +=
                            metrics.DeadLogBlocks;
                        run.ScannedMirePlantBlocks +=
                            metrics.MirePlantBlocks;
                        run.ScannedPeatBlocks +=
                            metrics.PeatBlocks;
                        run.ScannedLivingFloraBlocks +=
                            metrics.LivingFloraBlocks;
                        run.ScannedHealthyGrassSurfaces +=
                            metrics.HealthyGrassSurfaces;
                        run.ScannedMistEmitters +=
                            metrics.MistEmitters;
                        if (hasEnvironmentTrace &&
                            environmentTrace != null)
                        {
                            run.EnvironmentTraceChunks++;
                            run.GeneratedDeadTrees +=
                                environmentTrace.DeadTrees;
                            run.GeneratedDeadLogBlocks +=
                                environmentTrace.DeadLogBlocks;
                            run.GeneratedMirePlantBlocks +=
                                environmentTrace.MirePlantBlocks;
                            run.GeneratedToxicFloorColumns +=
                                environmentTrace.ToxicFloorColumns;
                            run.GeneratedToxicWaterBlocks +=
                                environmentTrace.ToxicWaterBlocks;
                            run.RemovedLivingFlora +=
                                environmentTrace.RemovedLivingFlora;
                            run.GeneratedMistEmitters +=
                                environmentTrace.MistEmitters;
                            run.EnvironmentGeneratorMilliseconds +=
                                environmentTrace.GeneratorMilliseconds;
                        }
                    }
                    if (!hasEnvironmentTrace)
                    {
                        RecordPoisonMireProbeTraceWarning(
                            run,
                            target
                        );
                    }
                }
            }
            catch (Exception exception)
            {
                poisonMireEnvironmentGenerator.CancelProbeChunk(
                    target.ChunkX,
                    target.ChunkZ
                );
                RecordPoisonMireProbeError(
                    run,
                    target,
                    "Scratch-column scan failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
            }

            ScheduleNextPoisonMireProbe(run);
        }

        private PoisonMireChunkMetrics ScanPoisonMireProbeColumn(
            PoisonMireProbeTarget target,
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
            int deadLogBlocks = 0;
            int mirePlantBlocks = 0;
            int peatBlocks = 0;
            int livingFloraBlocks = 0;
            int mistEmitters = 0;
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
                    Block? block = blockId > 0 &&
                        blockId < api.World.Blocks.Count
                            ? api.World.Blocks[blockId]
                            : null;
                    if (path.StartsWith(
                            "debarkedlog-rotten-",
                            StringComparison.Ordinal) ||
                        path.StartsWith(
                            "debarkedlog-veryrotten-",
                            StringComparison.Ordinal))
                    {
                        deadLogBlocks++;
                    }
                    else if (path.Equals(
                            "deadgrass",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "deadreeds",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "thornbush",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "rottedstump",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "fallenbranch",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "fungalcrust",
                            StringComparison.Ordinal))
                    {
                        mirePlantBlocks++;
                    }
                    else if (path.Equals(
                            "mirepeat",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "miremud",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "mireash",
                            StringComparison.Ordinal) ||
                        path.Equals(
                            "miresulfur",
                            StringComparison.Ordinal))
                    {
                        peatBlocks++;
                    }
                    else if (path.Equals(
                            "miremist",
                            StringComparison.Ordinal))
                    {
                        mistEmitters++;
                    }
                    if (block?.Code?.Domain.Equals(
                            "apprenticemire",
                            StringComparison.OrdinalIgnoreCase) != true &&
                        (block?.BlockMaterial ==
                            EnumBlockMaterial.Plant ||
                         block?.BlockMaterial ==
                            EnumBlockMaterial.Leaves))
                    {
                        livingFloraBlocks++;
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

            bool[] dryColumns = new bool[ChunkSize * ChunkSize];
            int dryLandColumns = 0;
            int freshWaterColumns = 0;
            int vanillaFreshWaterColumns = 0;
            int shallowFreshWaterColumns = 0;
            int deepFreshWaterColumns = 0;
            int saltWaterColumns = 0;
            int minimumTerrainY = int.MaxValue;
            int maximumTerrainY = 0;
            int healthyGrassSurfaces = 0;

            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                for (int localX = 0; localX < ChunkSize; localX++)
                {
                    int mapIndex = localZ * ChunkSize + localX;
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

                    string surfacePath = BlockPath(
                        GetGeneratedBlockId(
                            chunks,
                            localX,
                            terrainY,
                            localZ
                        ));
                    if (surfacePath.StartsWith(
                            "soil-",
                            StringComparison.Ordinal) &&
                        surfacePath.Contains(
                            "grass",
                            StringComparison.Ordinal))
                    {
                        healthyGrassSurfaces++;
                    }

                    int freshDepth = 0;
                    int toxicDepth = 0;
                    int saltDepth = 0;
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
                        if (IsFreshWater(path))
                        {
                            freshDepth++;
                        }
                        else if (IsToxicWater(path))
                        {
                            toxicDepth++;
                        }
                        else if (IsSaltWater(path))
                        {
                            saltDepth++;
                        }
                    }

                    if (saltDepth > 0)
                    {
                        saltWaterColumns++;
                    }
                    else if (toxicDepth > 0)
                    {
                        freshWaterColumns++;
                        if (toxicDepth <= 4)
                        {
                            shallowFreshWaterColumns++;
                        }
                        else
                        {
                            deepFreshWaterColumns++;
                        }
                    }
                    else if (freshDepth > 0)
                    {
                        vanillaFreshWaterColumns++;
                    }
                    else
                    {
                        dryColumns[mapIndex] = true;
                        dryLandColumns++;
                    }
                }
            }

            int traversableDryColumns = 0;
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
                    bool gentle =
                        Math.Abs(
                            height -
                            terrainHeights[mapIndex - 1]
                        ) <= 1 &&
                        Math.Abs(
                            height -
                            terrainHeights[mapIndex + 1]
                        ) <= 1 &&
                        Math.Abs(
                            height -
                            terrainHeights[mapIndex - ChunkSize]
                        ) <= 1 &&
                        Math.Abs(
                            height -
                            terrainHeights[mapIndex + ChunkSize]
                        ) <= 1;
                    if (gentle)
                    {
                        traversableDryColumns++;
                    }
                }
            }

            int worldX =
                target.ChunkX * ChunkSize + ChunkSize / 2;
            int worldZ =
                target.ChunkZ * ChunkSize + ChunkSize / 2;
            return new PoisonMireChunkMetrics(
                WorldZoneLayout.GetLevelAt(
                    activeState,
                    worldX,
                    worldZ
                ) != PoisonMireLevel,
                dryLandColumns,
                traversableDryColumns,
                freshWaterColumns,
                vanillaFreshWaterColumns,
                shallowFreshWaterColumns,
                deepFreshWaterColumns,
                saltWaterColumns,
                treeColumns.Count(value => value),
                minimumTerrainY,
                maximumTerrainY,
                deadLogBlocks,
                mirePlantBlocks,
                peatBlocks,
                livingFloraBlocks,
                healthyGrassSurfaces,
                mistEmitters
            );
        }

        private static bool IsFreshWater(string path) =>
            path.Equals("water", StringComparison.Ordinal) ||
            path.StartsWith("water-", StringComparison.Ordinal);

        private static bool IsToxicWater(string path) =>
            path.Equals("toxicwater", StringComparison.Ordinal) ||
            path.StartsWith(
                "toxicwater-",
                StringComparison.Ordinal);

        private static bool IsSaltWater(string path) =>
            path.Equals("saltwater", StringComparison.Ordinal) ||
            path.StartsWith("saltwater-", StringComparison.Ordinal);

        private static int GetGeneratedFluidId(
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
            return chunk.Data.GetFluid(index);
        }

        private static void RecordPoisonMireProbeError(
            PoisonMireProbeRun run,
            PoisonMireProbeTarget target,
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

        private static void RecordPoisonMireProbeTraceWarning(
            PoisonMireProbeRun run,
            PoisonMireProbeTarget target)
        {
            lock (run.Sync)
            {
                run.MissingEnvironmentTraces++;
                run.Warnings.Add(
                    $"chunk {target.ChunkX},{target.ChunkZ}: environment generator trace was unavailable"
                );
            }
        }

        private void ScheduleNextPoisonMireProbe(
            PoisonMireProbeRun run)
        {
            api.Event.EnqueueMainThreadTask(
                () => RunNextPoisonMireProbe(run),
                "apprentice-poison-mire-probe-next"
            );
        }

        private bool IsActivePoisonMireProbe(
            PoisonMireProbeRun run)
        {
            lock (poisonMireProbeGate)
            {
                return ReferenceEquals(activePoisonMireProbe, run);
            }
        }

        private void FinishPoisonMireProbe(PoisonMireProbeRun run)
        {
            lock (poisonMireProbeGate)
            {
                if (!ReferenceEquals(activePoisonMireProbe, run))
                {
                    return;
                }
                activePoisonMireProbe = null;
            }
            foreach (PoisonMireProbeTarget target in run.Targets)
            {
                poisonMireEnvironmentGenerator.CancelProbeChunk(
                    target.ChunkX,
                    target.ChunkZ
                );
            }

            int totalColumns =
                run.CompletedChunks * ChunkSize * ChunkSize;
            int waterColumns =
                run.FreshWaterColumns + run.SaltWaterColumns;
            int terrainRange =
                run.MaximumTerrainY - run.MinimumTerrainY;
            bool passed =
                run.Errors.Count == 0 &&
                run.CompletedChunks == run.Targets.Count &&
                run.LevelMismatches == 0 &&
                run.VanillaFreshWaterColumns == 0 &&
                run.FreshWaterColumns >= totalColumns / 5 &&
                run.DryLandColumns >= totalColumns * 2 / 5 &&
                run.DryLandColumns <= totalColumns * 4 / 5 &&
                run.ShallowFreshWaterColumns >=
                    run.FreshWaterColumns * 2 / 3 &&
                run.SaltWaterColumns <= totalColumns / 100 &&
                run.DeepFreshWaterColumns <=
                    run.FreshWaterColumns / 3 &&
                run.TraversableDryColumns >=
                    run.DryLandColumns / 5 &&
                run.TreeCoveredColumns <= totalColumns / 3 &&
                terrainRange >= 4 &&
                terrainRange <= 96 &&
                run.EnvironmentTraceChunks ==
                    run.CompletedChunks &&
                run.MissingEnvironmentTraces == 0 &&
                run.GeneratedDeadTrees >= 1 &&
                run.GeneratedDeadLogBlocks >= 8 &&
                run.GeneratedMirePlantBlocks >= 8 &&
                run.GeneratedToxicFloorColumns >= 8 &&
                run.GeneratedToxicWaterBlocks >= 8 &&
                run.GeneratedMistEmitters >= 1 &&
                run.ScannedLivingFloraBlocks == 0 &&
                run.ScannedHealthyGrassSurfaces == 0 &&
                run.ScannedMistEmitters >=
                    run.GeneratedMistEmitters &&
                run.EnvironmentGeneratorMilliseconds <=
                    run.CompletedChunks * 20;

            StringBuilder summary = new();
            summary.Append("[Apprentice] Poison Mire probe ");
            summary.Append(passed ? "PASS" : "FAIL");
            summary.Append(": ");
            summary.Append(
                $"{run.CompletedChunks}/{run.Targets.Count} scratch chunks; " +
                $"dry land {run.DryLandColumns}/{totalColumns}; " +
                $"gentle dry route columns {run.TraversableDryColumns}; " +
                $"toxic-water columns {run.FreshWaterColumns}/{totalColumns}; " +
                $"unconverted fresh-water columns {run.VanillaFreshWaterColumns}; " +
                $"shallow/deep toxic water " +
                $"{run.ShallowFreshWaterColumns}/{run.DeepFreshWaterColumns}; " +
                $"salt-water columns {run.SaltWaterColumns}; " +
                $"tree-covered columns {run.TreeCoveredColumns}/{totalColumns}; " +
                $"terrain Y={run.MinimumTerrainY}-{run.MaximumTerrainY}; " +
                $"water total {waterColumns}; " +
                $"level mismatches {run.LevelMismatches}; " +
                $"environment generated " +
                $"{run.GeneratedDeadTrees} dead trees/" +
                $"{run.GeneratedDeadLogBlocks} logs, " +
                $"{run.GeneratedMirePlantBlocks} dead plants, " +
                $"{run.GeneratedToxicFloorColumns} corrupted-ground columns, " +
                $"{run.GeneratedToxicWaterBlocks} toxic-water blocks, " +
                $"{run.RemovedLivingFlora} living-flora blocks removed, " +
                $"{run.GeneratedMistEmitters} mist emitters; " +
                $"environment scan " +
                $"{run.ScannedDeadLogBlocks} logs/" +
                $"{run.ScannedMirePlantBlocks} dead plants/" +
                $"{run.ScannedPeatBlocks} corrupted-ground blocks/" +
                $"{run.ScannedLivingFloraBlocks} living flora/" +
                $"{run.ScannedHealthyGrassSurfaces} healthy grass surfaces/" +
                $"{run.ScannedMistEmitters} mist emitters; " +
                $"environment generator " +
                $"{run.EnvironmentGeneratorMilliseconds:0.0} ms total; " +
                $"environment traces " +
                $"{run.EnvironmentTraceChunks}/{run.CompletedChunks}."
            );
            if (run.Errors.Count > 0)
            {
                summary.Append(" Error: ");
                summary.Append(run.Errors[0]);
            }
            else if (run.Warnings.Count > 0)
            {
                summary.Append(" Warning: ");
                summary.Append(run.Warnings[0]);
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

        private sealed class PoisonMireProbeTarget
        {
            internal PoisonMireProbeTarget(int chunkX, int chunkZ)
            {
                ChunkX = chunkX;
                ChunkZ = chunkZ;
            }

            internal int ChunkX { get; }
            internal int ChunkZ { get; }
        }

        private sealed class PoisonMireProbeRun
        {
            internal const int RequiredChunks = 8;
            private int nextIndex;

            internal PoisonMireProbeRun(
                IServerPlayer? player,
                IReadOnlyList<PoisonMireProbeTarget> targets)
            {
                Player = player;
                Targets = targets;
            }

            internal object Sync { get; } = new();
            internal IServerPlayer? Player { get; }
            internal IReadOnlyList<PoisonMireProbeTarget> Targets { get; }
            internal int CompletedChunks { get; set; }
            internal int LevelMismatches { get; set; }
            internal int DryLandColumns { get; set; }
            internal int TraversableDryColumns { get; set; }
            internal int FreshWaterColumns { get; set; }
            internal int VanillaFreshWaterColumns { get; set; }
            internal int ShallowFreshWaterColumns { get; set; }
            internal int DeepFreshWaterColumns { get; set; }
            internal int SaltWaterColumns { get; set; }
            internal int TreeCoveredColumns { get; set; }
            internal int MinimumTerrainY { get; set; } = int.MaxValue;
            internal int MaximumTerrainY { get; set; }
            internal int EnvironmentTraceChunks { get; set; }
            internal int MissingEnvironmentTraces { get; set; }
            internal int GeneratedDeadTrees { get; set; }
            internal int GeneratedDeadLogBlocks { get; set; }
            internal int GeneratedMirePlantBlocks { get; set; }
            internal int GeneratedToxicFloorColumns { get; set; }
            internal int GeneratedToxicWaterBlocks { get; set; }
            internal int RemovedLivingFlora { get; set; }
            internal int GeneratedMistEmitters { get; set; }
            internal int ScannedDeadLogBlocks { get; set; }
            internal int ScannedMirePlantBlocks { get; set; }
            internal int ScannedPeatBlocks { get; set; }
            internal int ScannedLivingFloraBlocks { get; set; }
            internal int ScannedHealthyGrassSurfaces { get; set; }
            internal int ScannedMistEmitters { get; set; }
            internal double EnvironmentGeneratorMilliseconds { get; set; }
            internal List<string> Errors { get; } = new();
            internal List<string> Warnings { get; } = new();

            internal PoisonMireProbeTarget? NextTarget()
            {
                if (nextIndex >= Targets.Count)
                {
                    return null;
                }

                return Targets[nextIndex++];
            }
        }

        private readonly record struct PoisonMireChunkMetrics(
            bool LevelMismatch,
            int DryLandColumns,
            int TraversableDryColumns,
            int FreshWaterColumns,
            int VanillaFreshWaterColumns,
            int ShallowFreshWaterColumns,
            int DeepFreshWaterColumns,
            int SaltWaterColumns,
            int TreeCoveredColumns,
            int MinimumTerrainY,
            int MaximumTerrainY,
            int DeadLogBlocks,
            int MirePlantBlocks,
            int PeatBlocks,
            int LivingFloraBlocks,
            int HealthyGrassSurfaces,
            int MistEmitters
        );
    }
}
