using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Apprentice
{
    /// <summary>
    /// Bounded, deterministic new-chunk placement for Apprentice-owned plants.
    /// It never scans or modifies existing chunks.
    /// </summary>
    internal sealed class EcologyWorldgenSystem : IDisposable
    {
        private const int ChunkSize = GlobalConstants.ChunkSize;
        private const int ProbeCandidatesPerDefinition = 12;
        private const int ProbeSearchRadiusChunks = 96;
        private readonly ICoreServerAPI api;
        private readonly IReadOnlyList<EcologyDefinition> definitions;
        private readonly Dictionary<string, int> blockIds =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<long, ProbeChunkTrace>
            probeTraces = new();
        private readonly object probeGate = new();
        private EcologyProbeRun? activeProbe;
        private bool initialized;

        public EcologyWorldgenSystem(
            ICoreServerAPI api,
            ApprenticeContentRegistry registry)
        {
            this.api = api;
            definitions = registry.Ecology
                .Where(value => !string.IsNullOrWhiteSpace(value.WorldgenBlockCode))
                .ToArray();
            if (definitions.Count == 0) return;

            api.Event.InitWorldGenerator(OnInitWorldGenerator, "standard");
            api.Event.ChunkColumnGeneration(
                OnChunkColumnGeneration,
                EnumWorldGenPass.Vegetation,
                "standard"
            );
        }

        private void OnInitWorldGenerator()
        {
            blockIds.Clear();
            foreach (EcologyDefinition definition in definitions)
            {
                Block? block = api.World.GetBlock(
                    new AssetLocation(definition.WorldgenBlockCode!)
                );
                if (block != null)
                {
                    blockIds[definition.Id] = block.Id;
                }
            }
            initialized = true;
        }

        private void OnChunkColumnGeneration(IChunkColumnGenerateRequest request)
        {
            long chunkKey = ChunkKey(request.ChunkX, request.ChunkZ);
            probeTraces.TryGetValue(
                chunkKey,
                out ProbeChunkTrace? trace
            );
            if (trace != null)
            {
                lock (trace.Sync)
                {
                    trace.HandlerObserved = true;
                }
            }

            DangerWorldState? state = DangerTierRuntime.WorldState;
            if (!initialized || state?.Enabled != true || blockIds.Count == 0)
            {
                return;
            }

            int worldX = request.ChunkX * ChunkSize + ChunkSize / 2;
            int worldZ = request.ChunkZ * ChunkSize + ChunkSize / 2;
            int tier = WorldZoneLayout.GetLevelAt(state, worldX, worldZ);
            if (trace != null)
            {
                lock (trace.Sync)
                {
                    trace.ActualLevel = tier;
                }
            }
            if (tier <= 0) return;

            IMapChunk mapChunk = request.Chunks[0].MapChunk;
            foreach (EcologyDefinition definition in definitions)
            {
                bool isTracedDefinition =
                    trace != null &&
                    definition.Id.Equals(
                        trace.DefinitionId,
                        StringComparison.OrdinalIgnoreCase
                    );
                int allowedLevelOrdinal =
                    definition.GetAllowedLevelOrdinal(tier);
                if (isTracedDefinition)
                {
                    lock (trace!.Sync)
                    {
                        trace.DefinitionObserved = true;
                        trace.AllowedLevelOrdinal =
                            allowedLevelOrdinal;
                    }
                }

                if (allowedLevelOrdinal <= 0 ||
                    !blockIds.TryGetValue(definition.Id, out int blockId))
                {
                    continue;
                }
                if (isTracedDefinition)
                {
                    lock (trace!.Sync)
                    {
                        trace.BlockResolved = true;
                    }
                }

                LCGRandom random = new(
                    api.WorldManager.Seed ^ StableHash(definition.Id)
                );
                random.InitPositionSeed(request.ChunkX, request.ChunkZ);
                double chance = GetWorldgenChance(
                    definition,
                    allowedLevelOrdinal
                );

                for (int attempt = 0;
                    attempt < definition.WorldgenAttemptsPerChunk;
                    attempt++)
                {
                    if (random.NextDouble() >= chance)
                    {
                        if (isTracedDefinition)
                        {
                            lock (trace!.Sync)
                            {
                                trace.RollMisses++;
                            }
                        }
                        continue;
                    }

                    int x = random.NextInt(ChunkSize);
                    int z = random.NextInt(ChunkSize);
                    if (isTracedDefinition)
                    {
                        lock (trace!.Sync)
                        {
                            trace.RollHits++;
                        }
                    }

                    int y =
                        mapChunk.WorldGenTerrainHeightMap[
                            z * ChunkSize + x
                        ] + 1;
                    if (y <= 0 || y >= api.WorldManager.MapSizeY)
                    {
                        if (isTracedDefinition)
                        {
                            lock (trace!.Sync)
                            {
                                trace.InvalidHeights++;
                            }
                        }
                        continue;
                    }

                    int chunkY = y / ChunkSize;
                    int localY = y % ChunkSize;
                    if (chunkY < 0 || chunkY >= request.Chunks.Length)
                    {
                        if (isTracedDefinition)
                        {
                            lock (trace!.Sync)
                            {
                                trace.InvalidHeights++;
                            }
                        }
                        continue;
                    }

                    int index =
                        (ChunkSize * localY + z) * ChunkSize + x;
                    IChunkBlocks data = request.Chunks[chunkY].Data;
                    if (data.GetBlockIdUnsafe(index) != 0)
                    {
                        if (isTracedDefinition)
                        {
                            lock (trace!.Sync)
                            {
                                trace.OccupiedTargets++;
                            }
                        }
                        continue;
                    }

                    // The surface can be the top block of a chunk section,
                    // putting the plant in a previously empty section. The
                    // indexer allocates that palette safely; SetBlockUnsafe
                    // does not.
                    data[index] = blockId;
                    if (isTracedDefinition)
                    {
                        lock (trace!.Sync)
                        {
                            trace.Writes++;
                            trace.WrittenPositions.Add(
                                new BlockPos(
                                    request.ChunkX * ChunkSize + x,
                                    y,
                                    request.ChunkZ * ChunkSize + z
                                )
                            );
                        }
                    }
                }
            }
        }

        internal TextCommandResult StartWorldgenProbe(
            TextCommandCallingArgs args)
        {
            IServerPlayer? player =
                args.Caller.Player as IServerPlayer;

            DangerWorldState? state = DangerTierRuntime.WorldState;
            if (state == null ||
                !state.Enabled ||
                !state.RealmWorldgenEnabled ||
                state.WorldgenProfile !=
                    WorldZoneLayout.ConcentricRealmsProfile)
            {
                return TextCommandResult.Error(
                    "Realm world generation is not active for this save.",
                    "apprentice-ecology-worldgen-disabled"
                );
            }
            if (!initialized)
            {
                return TextCommandResult.Error(
                    "The ecology world generator is not initialized.",
                    "apprentice-ecology-not-initialized"
                );
            }

            EcologyDefinition[] probeDefinitions = definitions
                .Where(definition =>
                    blockIds.ContainsKey(definition.Id))
                .ToArray();
            if (probeDefinitions.Length == 0)
            {
                return TextCommandResult.Error(
                    "No ecology worldgen blocks resolved.",
                    "apprentice-ecology-no-blocks"
                );
            }

            List<ProbeChunkTarget> targets = new();
            foreach (EcologyDefinition definition in probeDefinitions)
            {
                List<ProbeChunkTarget> definitionTargets =
                    FindProbeTargets(state, definition);
                if (definitionTargets.Count <
                    ProbeCandidatesPerDefinition)
                {
                    return TextCommandResult.Error(
                        $"Could only find {definitionTargets.Count} " +
                        $"deterministic {definition.Id} candidates; " +
                        $"{ProbeCandidatesPerDefinition} are required.",
                        "apprentice-ecology-candidates-missing"
                    );
                }
                targets.AddRange(definitionTargets);
            }

            EcologyProbeRun run;
            lock (probeGate)
            {
                if (activeProbe != null)
                {
                    return TextCommandResult.Error(
                        "An ecology worldgen probe is already running.",
                        "apprentice-ecology-probe-active"
                    );
                }

                run = new EcologyProbeRun(
                    player,
                    targets,
                    probeDefinitions
                );
                activeProbe = run;
            }

            api.Logger.Notification(
                "[Apprentice] Starting non-destructive ecology worldgen " +
                "probe: {0} scratch chunks for {1}.",
                targets.Count,
                string.Join(
                    ", ",
                    probeDefinitions.Select(ProbeLabel)
                )
            );
            api.Event.EnqueueMainThreadTask(
                () => RunNextProbe(run),
                "apprentice-ecology-probe-start"
            );

            return TextCommandResult.Success(
                $"Ecology probe started: {targets.Count} scratch chunks. " +
                "It uses the real worldgen pipeline, changes no saved " +
                "chunks, and will report PASS/FAIL here when complete."
            );
        }

        private List<ProbeChunkTarget> FindProbeTargets(
            DangerWorldState state,
            EcologyDefinition definition)
        {
            List<ProbeChunkTarget> targets = new();
            int level = definition.AllowedLevels[0];
            int allowedLevelOrdinal =
                definition.GetAllowedLevelOrdinal(level);
            double inner = WorldZoneLayout.GetInnerRadius(state, level);
            double outer = WorldZoneLayout.GetOuterRadius(state, level);
            if (double.IsPositiveInfinity(outer))
            {
                outer = inner + state.RingWidth;
            }

            double targetRadius = (inner + outer) / 2;
            int originChunkX = (int)Math.Floor(
                (state.AnchorX + targetRadius) / ChunkSize
            );
            int originChunkZ = (int)Math.Floor(
                state.AnchorZ / ChunkSize
            );

            for (int ring = 0;
                ring <= ProbeSearchRadiusChunks &&
                targets.Count < ProbeCandidatesPerDefinition;
                ring++)
            {
                for (int offsetZ = -ring;
                    offsetZ <= ring &&
                    targets.Count < ProbeCandidatesPerDefinition;
                    offsetZ++)
                {
                    for (int offsetX = -ring;
                        offsetX <= ring &&
                        targets.Count < ProbeCandidatesPerDefinition;
                        offsetX++)
                    {
                        if (ring > 0 &&
                            Math.Max(
                                Math.Abs(offsetX),
                                Math.Abs(offsetZ)
                            ) != ring)
                        {
                            continue;
                        }

                        int chunkX = originChunkX + offsetX;
                        int chunkZ = originChunkZ + offsetZ;
                        int worldX =
                            chunkX * ChunkSize + ChunkSize / 2;
                        int worldZ =
                            chunkZ * ChunkSize + ChunkSize / 2;
                        if (worldX < ChunkSize ||
                            worldX >=
                                api.WorldManager.MapSizeX - ChunkSize ||
                            worldZ < ChunkSize ||
                            worldZ >=
                                api.WorldManager.MapSizeZ - ChunkSize ||
                            WorldZoneLayout.GetLevelAt(
                                state,
                                worldX,
                                worldZ
                            ) != level ||
                            !HasPredictedRoll(
                                definition,
                                allowedLevelOrdinal,
                                chunkX,
                                chunkZ
                            ))
                        {
                            continue;
                        }

                        targets.Add(
                            new ProbeChunkTarget(
                                definition.Id,
                                blockIds[definition.Id],
                                level,
                                chunkX,
                                chunkZ
                            )
                        );
                    }
                }
            }

            return targets;
        }

        private bool HasPredictedRoll(
            EcologyDefinition definition,
            int allowedLevelOrdinal,
            int chunkX,
            int chunkZ)
        {
            LCGRandom random = new(
                api.WorldManager.Seed ^ StableHash(definition.Id)
            );
            random.InitPositionSeed(chunkX, chunkZ);
            double chance = GetWorldgenChance(
                definition,
                allowedLevelOrdinal
            );
            for (int attempt = 0;
                attempt < definition.WorldgenAttemptsPerChunk;
                attempt++)
            {
                if (random.NextDouble() < chance)
                {
                    return true;
                }
            }
            return false;
        }

        private static double GetWorldgenChance(
            EcologyDefinition definition,
            int allowedLevelOrdinal) =>
            Math.Clamp(
                definition.WorldgenChancePerTier *
                    allowedLevelOrdinal,
                0,
                0.3
            );

        private void RunNextProbe(EcologyProbeRun run)
        {
            if (!IsActive(run))
            {
                return;
            }

            ProbeChunkTarget? target;
            lock (run.Sync)
            {
                target = run.NextTarget();
            }
            if (target == null)
            {
                FinishProbe(run);
                return;
            }

            ProbeChunkTrace trace = new(target.DefinitionId);
            long chunkKey = ChunkKey(target.ChunkX, target.ChunkZ);
            probeTraces[chunkKey] = trace;
            try
            {
                api.WorldManager.PeekChunkColumn(
                    target.ChunkX,
                    target.ChunkZ,
                    new ChunkPeekOptions
                    {
                        OnGenerated = columns =>
                            OnProbeChunkGenerated(
                                run,
                                target,
                                trace,
                                columns
                            )
                    }
                );
            }
            catch (Exception exception)
            {
                probeTraces.TryRemove(chunkKey, out _);
                RecordProbeError(
                    run,
                    target,
                    "PeekChunkColumn failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
                ScheduleNextProbe(run);
            }
        }

        private void OnProbeChunkGenerated(
            EcologyProbeRun run,
            ProbeChunkTarget target,
            ProbeChunkTrace trace,
            Dictionary<Vec2i, IServerChunk[]> columns)
        {
            probeTraces.TryRemove(
                ChunkKey(target.ChunkX, target.ChunkZ),
                out _
            );
            if (!IsActive(run))
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
                    RecordProbeError(
                        run,
                        target,
                        "Peek callback omitted the requested chunk column."
                    );
                }
                else
                {
                    List<BlockPos> foundPositions =
                        ScanGeneratedColumn(
                            targetColumn,
                            target,
                            out int foundPlants,
                            out int trunkBlocks,
                            out int treeCoveredColumns
                        );
                    RecordProbeResult(
                        run,
                        target,
                        trace,
                        foundPlants,
                        foundPositions,
                        trunkBlocks,
                        treeCoveredColumns
                    );
                }
            }
            catch (Exception exception)
            {
                RecordProbeError(
                    run,
                    target,
                    "Scratch-column scan failed: " +
                        exception.GetType().Name + ": " +
                        exception.Message
                );
            }

            ScheduleNextProbe(run);
        }

        private List<BlockPos> ScanGeneratedColumn(
            IServerChunk[] chunks,
            ProbeChunkTarget target,
            out int foundPlants,
            out int trunkBlocks,
            out int treeCoveredColumns)
        {
            foundPlants = 0;
            trunkBlocks = 0;
            treeCoveredColumns = 0;
            bool measureShadowForest = target.ExpectedLevel == 4;
            bool[]? treeColumns = measureShadowForest
                ? new bool[ChunkSize * ChunkSize]
                : null;
            List<BlockPos> examples = new();
            for (int chunkY = 0; chunkY < chunks.Length; chunkY++)
            {
                IServerChunk? chunk = chunks[chunkY];
                if (chunk == null || chunk.Disposed)
                {
                    continue;
                }

                chunk.Unpack_ReadOnly();
                IChunkBlocks data = chunk.Data;
                if (!measureShadowForest &&
                    !data.ContainsBlock(target.BlockId))
                {
                    continue;
                }

                for (int index = 0; index < data.Length; index++)
                {
                    int blockId = data.GetBlockIdUnsafe(index);
                    int localX = index % ChunkSize;
                    int yz = index / ChunkSize;
                    int localZ = yz % ChunkSize;
                    if (blockId == target.BlockId)
                    {
                        foundPlants++;
                        if (examples.Count < 3)
                        {
                            int localY = yz / ChunkSize;
                            examples.Add(
                                new BlockPos(
                                    target.ChunkX * ChunkSize + localX,
                                    chunkY * ChunkSize + localY,
                                    target.ChunkZ * ChunkSize + localZ
                                )
                            );
                        }
                    }

                    if (!measureShadowForest || blockId <= 0 ||
                        blockId >= api.World.Blocks.Count)
                    {
                        continue;
                    }

                    string? path =
                        api.World.Blocks[blockId]?.Code?.Path;
                    bool isTrunk =
                        path?.StartsWith(
                            "log-grown-",
                            StringComparison.Ordinal
                        ) == true;
                    bool isTreeBlock = isTrunk ||
                        path?.StartsWith(
                            "leaves",
                            StringComparison.Ordinal
                        ) == true;
                    if (!isTreeBlock)
                    {
                        continue;
                    }

                    treeColumns![localZ * ChunkSize + localX] = true;
                    if (isTrunk)
                    {
                        trunkBlocks++;
                    }
                }
            }

            if (treeColumns != null)
            {
                treeCoveredColumns = treeColumns.Count(value => value);
            }
            return examples;
        }

        private void RecordProbeResult(
            EcologyProbeRun run,
            ProbeChunkTarget target,
            ProbeChunkTrace trace,
            int foundPlants,
            IReadOnlyList<BlockPos> foundPositions,
            int trunkBlocks,
            int treeCoveredColumns)
        {
            lock (run.Sync)
            {
                ProbeDefinitionResult result =
                    run.Results[target.DefinitionId];
                result.CompletedChunks++;
                result.FoundPlants += foundPlants;
                if (target.ExpectedLevel == 4)
                {
                    result.MinimumTrunkBlocks = Math.Min(
                        result.MinimumTrunkBlocks,
                        trunkBlocks
                    );
                    result.MaximumTrunkBlocks = Math.Max(
                        result.MaximumTrunkBlocks,
                        trunkBlocks
                    );
                    result.MinimumTreeCoveredColumns = Math.Min(
                        result.MinimumTreeCoveredColumns,
                        treeCoveredColumns
                    );
                    result.MaximumTreeCoveredColumns = Math.Max(
                        result.MaximumTreeCoveredColumns,
                        treeCoveredColumns
                    );
                }
                AddExamples(result.Examples, foundPositions);

                lock (trace.Sync)
                {
                    if (trace.HandlerObserved)
                    {
                        result.HandlerObservations++;
                    }
                    if (trace.DefinitionObserved)
                    {
                        result.DefinitionObservations++;
                    }
                    if (trace.BlockResolved)
                    {
                        result.ResolvedBlockObservations++;
                    }
                    if (trace.ActualLevel != target.ExpectedLevel)
                    {
                        result.LevelMismatches++;
                    }
                    result.RollHits += trace.RollHits;
                    result.RollMisses += trace.RollMisses;
                    result.InvalidHeights += trace.InvalidHeights;
                    result.OccupiedTargets +=
                        trace.OccupiedTargets;
                    result.Writes += trace.Writes;
                    AddExamples(
                        result.WrittenExamples,
                        trace.WrittenPositions
                    );
                }
            }
        }

        private static void AddExamples(
            List<BlockPos> destination,
            IEnumerable<BlockPos> source)
        {
            foreach (BlockPos position in source)
            {
                if (destination.Count >= 3)
                {
                    return;
                }
                destination.Add(position);
            }
        }

        private static void RecordProbeError(
            EcologyProbeRun run,
            ProbeChunkTarget target,
            string message)
        {
            lock (run.Sync)
            {
                ProbeDefinitionResult result =
                    run.Results[target.DefinitionId];
                result.CompletedChunks++;
                result.Errors.Add(
                    $"chunk {target.ChunkX},{target.ChunkZ}: {message}"
                );
            }
        }

        private void ScheduleNextProbe(EcologyProbeRun run)
        {
            api.Event.EnqueueMainThreadTask(
                () => RunNextProbe(run),
                "apprentice-ecology-probe-next"
            );
        }

        private bool IsActive(EcologyProbeRun run)
        {
            lock (probeGate)
            {
                return ReferenceEquals(activeProbe, run);
            }
        }

        private void FinishProbe(EcologyProbeRun run)
        {
            lock (probeGate)
            {
                if (!ReferenceEquals(activeProbe, run))
                {
                    return;
                }
                activeProbe = null;
            }

            string summary = BuildProbeSummary(run, out bool passed);
            run.Player?.SendMessage(
                GlobalConstants.GeneralChatGroup,
                summary,
                EnumChatType.Notification
            );
            if (passed)
            {
                api.Logger.Notification(summary);
            }
            else
            {
                api.Logger.Error(summary);
            }
        }

        private static string BuildProbeSummary(
            EcologyProbeRun run,
            out bool passed)
        {
            passed = run.Results.Values.All(result =>
                result.Errors.Count == 0 &&
                result.CompletedChunks == result.RequestedChunks &&
                result.HandlerObservations == result.RequestedChunks &&
                result.DefinitionObservations == result.RequestedChunks &&
                result.ResolvedBlockObservations ==
                    result.RequestedChunks &&
                result.LevelMismatches == 0 &&
                result.RollHits > 0 &&
                result.Writes > 0 &&
                result.FoundPlants > 0);

            StringBuilder builder = new();
            builder.Append("[Apprentice] Ecology worldgen probe ");
            builder.Append(passed ? "PASS" : "FAIL");
            builder.AppendLine(".");
            foreach (ProbeDefinitionResult result in
                run.Results.Values.OrderBy(value => value.DefinitionId))
            {
                bool definitionPassed =
                    result.Errors.Count == 0 &&
                    result.CompletedChunks == result.RequestedChunks &&
                    result.HandlerObservations ==
                        result.RequestedChunks &&
                    result.DefinitionObservations ==
                        result.RequestedChunks &&
                    result.ResolvedBlockObservations ==
                        result.RequestedChunks &&
                    result.LevelMismatches == 0 &&
                    result.RollHits > 0 &&
                    result.Writes > 0 &&
                    result.FoundPlants > 0;
                builder.Append(
                    $"{result.PlantName} " +
                    $"({result.WorldgenBlockCode}): " +
                    $"{(definitionPassed ? "PASS" : "FAIL")} — " +
                    $"{result.CompletedChunks}/" +
                    $"{result.RequestedChunks} scratch chunks, " +
                    $"{result.RollHits} eligible rolls, " +
                    $"{result.OccupiedTargets} occupied targets, " +
                    $"{result.InvalidHeights} invalid heights, " +
                    $"{result.Writes} writes, " +
                    $"{result.FoundPlants} wild blocks found"
                );
                if (result.Examples.Count > 0)
                {
                    builder.Append(
                        "; examples " +
                        string.Join(
                            ", ",
                            result.Examples.Select(FormatPosition)
                        )
                    );
                }
                if (result.MinimumTrunkBlocks != int.MaxValue)
                {
                    builder.Append(
                        $"; Shadow Forest trunks " +
                        $"{result.MinimumTrunkBlocks}-" +
                        $"{result.MaximumTrunkBlocks}/chunk, " +
                        $"tree-covered columns " +
                        $"{result.MinimumTreeCoveredColumns}-" +
                        $"{result.MaximumTreeCoveredColumns}/1024"
                    );
                }
                if (result.Errors.Count > 0)
                {
                    builder.Append(
                        "; error " + result.Errors[0]
                    );
                }
                builder.AppendLine(".");
            }
            builder.Append(
                "The probe used temporary PeekChunkColumn output; " +
                "no saved or loaded chunk was changed."
            );
            return builder.ToString();
        }

        private static string PlantName(string definitionId) =>
            definitionId.Equals(
                "venomberry",
                StringComparison.OrdinalIgnoreCase
            )
                ? "Wild Venomberry bush"
                : definitionId.Equals(
                    "gloamcap",
                    StringComparison.OrdinalIgnoreCase
                )
                    ? "Wild Gloamcap"
                    : definitionId;

        private static string ProbeLabel(EcologyDefinition definition) =>
            $"{PlantName(definition.Id)} " +
            $"({definition.WorldgenBlockCode})";

        private static string FormatPosition(BlockPos position) =>
            $"X={position.X}, Y={position.Y}, Z={position.Z}";

        private static long ChunkKey(int chunkX, int chunkZ) =>
            unchecked(
                ((long)(uint)chunkX << 32) |
                (uint)chunkZ
            );

        private static long StableHash(string value)
        {
            unchecked
            {
                long hash = 1469598103934665603L;
                foreach (char character in value)
                {
                    hash ^= character;
                    hash *= 1099511628211L;
                }
                return hash;
            }
        }

        public void Dispose()
        {
            lock (probeGate)
            {
                activeProbe = null;
            }
            probeTraces.Clear();
            blockIds.Clear();
            initialized = false;
        }

        private sealed class ProbeChunkTarget
        {
            internal ProbeChunkTarget(
                string definitionId,
                int blockId,
                int expectedLevel,
                int chunkX,
                int chunkZ)
            {
                DefinitionId = definitionId;
                BlockId = blockId;
                ExpectedLevel = expectedLevel;
                ChunkX = chunkX;
                ChunkZ = chunkZ;
            }

            internal string DefinitionId { get; }
            internal int BlockId { get; }
            internal int ExpectedLevel { get; }
            internal int ChunkX { get; }
            internal int ChunkZ { get; }
        }

        private sealed class ProbeChunkTrace
        {
            internal ProbeChunkTrace(string definitionId)
            {
                DefinitionId = definitionId;
            }

            internal object Sync { get; } = new();
            internal string DefinitionId { get; }
            internal bool HandlerObserved { get; set; }
            internal bool DefinitionObserved { get; set; }
            internal bool BlockResolved { get; set; }
            internal int ActualLevel { get; set; } = -1;
            internal int AllowedLevelOrdinal { get; set; }
            internal int RollHits { get; set; }
            internal int RollMisses { get; set; }
            internal int InvalidHeights { get; set; }
            internal int OccupiedTargets { get; set; }
            internal int Writes { get; set; }
            internal List<BlockPos> WrittenPositions { get; } =
                new();
        }

        private sealed class EcologyProbeRun
        {
            private int nextIndex;

            internal EcologyProbeRun(
                IServerPlayer? player,
                IReadOnlyList<ProbeChunkTarget> targets,
                IReadOnlyList<EcologyDefinition> definitions)
            {
                Player = player;
                Targets = targets;
                Results = definitions.ToDictionary(
                    definition => definition.Id,
                    definition => new ProbeDefinitionResult(
                        definition.Id,
                        PlantName(definition.Id),
                        definition.WorldgenBlockCode!,
                        targets.Count(target =>
                            target.DefinitionId.Equals(
                                definition.Id,
                                StringComparison.OrdinalIgnoreCase
                            ))
                    ),
                    StringComparer.OrdinalIgnoreCase
                );
            }

            internal object Sync { get; } = new();
            internal IServerPlayer? Player { get; }
            internal IReadOnlyList<ProbeChunkTarget> Targets { get; }
            internal Dictionary<string, ProbeDefinitionResult>
                Results { get; }

            internal ProbeChunkTarget? NextTarget()
            {
                if (nextIndex >= Targets.Count)
                {
                    return null;
                }
                return Targets[nextIndex++];
            }
        }

        private sealed class ProbeDefinitionResult
        {
            internal ProbeDefinitionResult(
                string definitionId,
                string plantName,
                string worldgenBlockCode,
                int requestedChunks)
            {
                DefinitionId = definitionId;
                PlantName = plantName;
                WorldgenBlockCode = worldgenBlockCode;
                RequestedChunks = requestedChunks;
            }

            internal string DefinitionId { get; }
            internal string PlantName { get; }
            internal string WorldgenBlockCode { get; }
            internal int RequestedChunks { get; }
            internal int CompletedChunks { get; set; }
            internal int HandlerObservations { get; set; }
            internal int DefinitionObservations { get; set; }
            internal int ResolvedBlockObservations { get; set; }
            internal int LevelMismatches { get; set; }
            internal int RollHits { get; set; }
            internal int RollMisses { get; set; }
            internal int InvalidHeights { get; set; }
            internal int OccupiedTargets { get; set; }
            internal int Writes { get; set; }
            internal int FoundPlants { get; set; }
            internal int MinimumTrunkBlocks { get; set; } =
                int.MaxValue;
            internal int MaximumTrunkBlocks { get; set; }
            internal int MinimumTreeCoveredColumns { get; set; } =
                int.MaxValue;
            internal int MaximumTreeCoveredColumns { get; set; }
            internal List<BlockPos> WrittenExamples { get; } =
                new();
            internal List<BlockPos> Examples { get; } =
                new();
            internal List<string> Errors { get; } =
                new();
        }
    }
}
