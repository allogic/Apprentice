using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Apprentice
{
    /// <summary>
    /// Performs the final, one-time Level 6 environment conversion after
    /// vanilla vegetation. Terrain heights and route geometry are left
    /// untouched; only flora, natural surface blocks, fluids and realm-owned
    /// decorations are changed.
    /// </summary>
    internal sealed class PoisonMireEnvironmentGenerator
    {
        internal const int PoisonMireLevel = 6;
        internal const int BoundaryExclusionWidth = 192;
        internal const int DeadTreeFieldCellSize = 384;
        internal const int DeadTreeFieldMinimumRadius = 96;
        internal const int DeadTreeFieldMaximumRadius = 138;

        private const int ChunkSize = GlobalConstants.ChunkSize;
        private const int DeadTreeAttemptsPerChunk = 3;
        private const int MinimumTreeHeight = 6;
        private const int MaximumTreeHeight = 11;
        private const int TreeMargin = 5;
        private const int DeadPlantChancePercent = 28;
        private const int ShoreDeadPlantChancePercent = 54;
        private const int MistEmitterSpacing = 7;
        private const ulong TreeSalt = 0x4D49524554524545UL;
        private const ulong PlantSalt = 0x4D495245504C414EUL;
        private const ulong GroundSalt = 0x4D49524547524F55UL;
        private const ulong MistSalt = 0x4D4952454D495354UL;

        private readonly ICoreServerAPI api;
        private readonly ConcurrentDictionary<long, ProbeSlot> probeSlots =
            new();
        private readonly Dictionary<int, int> toxicWaterByFreshWater =
            new();
        private readonly HashSet<int> toxicWaterBlockIds = new();
        private DangerWorldState? activeState;
        private long worldSeed;
        private DeadLogPalette rottenLogs;
        private DeadLogPalette veryRottenLogs;
        private int mirePeatBlockId;
        private int mireMudBlockId;
        private int mireAshBlockId;
        private int mireSulfurBlockId;
        private int deadGrassBlockId;
        private int deadReedsBlockId;
        private int thornBushBlockId;
        private int rottedStumpBlockId;
        private int fallenBranchBlockId;
        private int fungalCrustBlockId;
        private int mistBlockId;
        private bool initialized;

        private static long affectedChunks;
        private static long deadTrees;
        private static long deadLogBlocks;
        private static long deadPlantBlocks;
        private static long corruptedGroundColumns;
        private static long toxicWaterBlocks;
        private static long removedLivingFlora;
        private static long mistEmitters;

        internal PoisonMireEnvironmentGenerator(ICoreServerAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
        }

        internal bool Initialized => initialized;
        internal static long AffectedChunks =>
            System.Threading.Interlocked.Read(ref affectedChunks);
        internal static long DeadTrees =>
            System.Threading.Interlocked.Read(ref deadTrees);
        internal static long DeadLogBlocks =>
            System.Threading.Interlocked.Read(ref deadLogBlocks);
        internal static long MirePlantBlocks =>
            System.Threading.Interlocked.Read(ref deadPlantBlocks);
        internal static long ToxicFloorColumns =>
            System.Threading.Interlocked.Read(ref corruptedGroundColumns);
        internal static long ToxicWaterBlocks =>
            System.Threading.Interlocked.Read(ref toxicWaterBlocks);
        internal static long RemovedLivingFlora =>
            System.Threading.Interlocked.Read(ref removedLivingFlora);
        internal static long MistEmitters =>
            System.Threading.Interlocked.Read(ref mistEmitters);

        internal bool Initialize(DangerWorldState state, out string error)
        {
            Reset();
            if (!TryResolveDeadLogPalette(
                    "rotten", out rottenLogs, out error) ||
                !TryResolveDeadLogPalette(
                    "veryrotten", out veryRottenLogs, out error) ||
                !TryResolveBlock(
                    "apprenticemire:mirepeat", out mirePeatBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:miremud", out mireMudBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:mireash", out mireAshBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:miresulfur", out mireSulfurBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:deadgrass", out deadGrassBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:deadreeds", out deadReedsBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:thornbush", out thornBushBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:rottedstump", out rottedStumpBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:fallenbranch", out fallenBranchBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:fungalcrust", out fungalCrustBlockId, out error) ||
                !TryResolveBlock(
                    "apprenticemire:miremist", out mistBlockId, out error))
            {
                Reset();
                return false;
            }

            foreach (Block? fresh in api.World.Blocks)
            {
                string path = fresh?.Code?.Path ?? string.Empty;
                if (fresh == null ||
                    fresh.Id <= 0 ||
                    !(path.Equals("water", StringComparison.Ordinal) ||
                      path.StartsWith("water-", StringComparison.Ordinal)))
                {
                    continue;
                }

                string suffix = path.Length == "water".Length
                    ? "-still-7"
                    : path.Substring("water".Length);
                Block? toxic = api.World.GetBlock(
                    new AssetLocation("apprenticemire", "toxicwater" + suffix)
                );
                if (toxic == null || toxic.Id <= 0)
                {
                    error =
                        $"required toxic-water variant apprenticemire:toxicwater{suffix} is missing";
                    Reset();
                    return false;
                }

                toxicWaterByFreshWater[fresh.Id] = toxic.Id;
                toxicWaterBlockIds.Add(toxic.Id);
            }

            if (toxicWaterByFreshWater.Count == 0)
            {
                error = "no vanilla fresh-water blocks were loaded";
                Reset();
                return false;
            }

            activeState = state;
            worldSeed = api.WorldManager.Seed;
            initialized = true;
            error = string.Empty;
            return true;
        }

        internal void Reset()
        {
            activeState = null;
            worldSeed = 0;
            rottenLogs = default;
            veryRottenLogs = default;
            mirePeatBlockId = 0;
            mireMudBlockId = 0;
            mireAshBlockId = 0;
            mireSulfurBlockId = 0;
            deadGrassBlockId = 0;
            deadReedsBlockId = 0;
            thornBushBlockId = 0;
            rottedStumpBlockId = 0;
            fallenBranchBlockId = 0;
            fungalCrustBlockId = 0;
            mistBlockId = 0;
            initialized = false;
            toxicWaterByFreshWater.Clear();
            toxicWaterBlockIds.Clear();
            probeSlots.Clear();
            System.Threading.Interlocked.Exchange(ref affectedChunks, 0);
            System.Threading.Interlocked.Exchange(ref deadTrees, 0);
            System.Threading.Interlocked.Exchange(ref deadLogBlocks, 0);
            System.Threading.Interlocked.Exchange(ref deadPlantBlocks, 0);
            System.Threading.Interlocked.Exchange(ref corruptedGroundColumns, 0);
            System.Threading.Interlocked.Exchange(ref toxicWaterBlocks, 0);
            System.Threading.Interlocked.Exchange(ref removedLivingFlora, 0);
            System.Threading.Interlocked.Exchange(ref mistEmitters, 0);
        }

        internal void OnChunkColumnGeneration(
            IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = activeState;
            if (!initialized ||
                state == null ||
                !WorldZoneLayout.ChunkIntersectsLevel(
                    state,
                    PoisonMireLevel,
                    request.ChunkX,
                    request.ChunkZ,
                    ChunkSize))
            {
                return;
            }

            long chunkKey = ChunkKey(request.ChunkX, request.ChunkZ);
            probeSlots.TryGetValue(chunkKey, out ProbeSlot? probeSlot);
            long started = Stopwatch.GetTimestamp();
            MireEnvironmentChunkTrace trace = GenerateChunk(request, state);
            trace.GeneratorMilliseconds =
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (probeSlot != null)
            {
                probeSlot.Trace = trace;
            }

            if (!trace.Changed)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref affectedChunks);
            System.Threading.Interlocked.Add(ref deadTrees, trace.DeadTrees);
            System.Threading.Interlocked.Add(
                ref deadLogBlocks, trace.DeadLogBlocks);
            System.Threading.Interlocked.Add(
                ref deadPlantBlocks, trace.MirePlantBlocks);
            System.Threading.Interlocked.Add(
                ref corruptedGroundColumns, trace.ToxicFloorColumns);
            System.Threading.Interlocked.Add(
                ref toxicWaterBlocks, trace.ToxicWaterBlocks);
            System.Threading.Interlocked.Add(
                ref removedLivingFlora, trace.RemovedLivingFlora);
            System.Threading.Interlocked.Add(
                ref mistEmitters, trace.MistEmitters);
        }

        internal bool PrepareProbeChunk(int chunkX, int chunkZ) =>
            initialized &&
            probeSlots.TryAdd(ChunkKey(chunkX, chunkZ), new ProbeSlot());

        internal bool TryTakeProbeTrace(
            int chunkX,
            int chunkZ,
            out MireEnvironmentChunkTrace? trace)
        {
            if (probeSlots.TryRemove(
                    ChunkKey(chunkX, chunkZ),
                    out ProbeSlot? slot))
            {
                trace = slot.Trace;
                return trace != null;
            }

            trace = null;
            return false;
        }

        internal void CancelProbeChunk(int chunkX, int chunkZ)
        {
            probeSlots.TryRemove(ChunkKey(chunkX, chunkZ), out _);
        }

        private MireEnvironmentChunkTrace GenerateChunk(
            IChunkColumnGenerateRequest request,
            DangerWorldState state)
        {
            MireEnvironmentChunkTrace trace = new();
            IMapChunk mapChunk = request.Chunks[0].MapChunk;
            ushort[] terrainHeights = mapChunk.WorldGenTerrainHeightMap;
            ushort[] rainHeights = mapChunk.RainHeightMap;
            bool[] waterColumns = new bool[ChunkSize * ChunkSize];
            bool[] coreColumns = new bool[ChunkSize * ChunkSize];
            int mapSizeY = Math.Min(
                api.WorldManager.MapSizeY,
                request.Chunks.Length * ChunkSize
            );
            int chunkOriginX = request.ChunkX * ChunkSize;
            int chunkOriginZ = request.ChunkZ * ChunkSize;
            ushort yMax = mapChunk.YMax;

            MarkCoreColumns(
                state,
                chunkOriginX,
                chunkOriginZ,
                coreColumns
            );
            RemoveLivingFlora(
                request,
                terrainHeights,
                rainHeights,
                coreColumns,
                mapSizeY,
                trace
            );
            ConvertWaterAndGround(
                request,
                terrainHeights,
                rainHeights,
                coreColumns,
                waterColumns,
                mapSizeY,
                chunkOriginX,
                chunkOriginZ,
                trace
            );
            GenerateDeadTrees(
                request,
                state,
                terrainHeights,
                rainHeights,
                waterColumns,
                mapSizeY,
                chunkOriginX,
                chunkOriginZ,
                trace,
                ref yMax
            );
            GenerateDeadPlantsAndMist(
                request,
                terrainHeights,
                rainHeights,
                coreColumns,
                waterColumns,
                mapSizeY,
                chunkOriginX,
                chunkOriginZ,
                trace,
                ref yMax
            );

            mapChunk.YMax = yMax;
            return trace;
        }

        private static void MarkCoreColumns(
            DangerWorldState state,
            int chunkOriginX,
            int chunkOriginZ,
            bool[] coreColumns)
        {
            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                for (int localX = 0; localX < ChunkSize; localX++)
                {
                    coreColumns[localZ * ChunkSize + localX] =
                        WorldZoneLayout.IsInsideLevelCore(
                            state,
                            PoisonMireLevel,
                            BoundaryExclusionWidth,
                            chunkOriginX + localX + 0.5,
                            chunkOriginZ + localZ + 0.5
                        );
                }
            }
        }

        private void RemoveLivingFlora(
            IChunkColumnGenerateRequest request,
            ushort[] terrainHeights,
            ushort[] rainHeights,
            bool[] coreColumns,
            int mapSizeY,
            MireEnvironmentChunkTrace trace)
        {
            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                for (int localX = 0; localX < ChunkSize; localX++)
                {
                    int mapIndex = localZ * ChunkSize + localX;
                    if (!coreColumns[mapIndex])
                    {
                        continue;
                    }

                    int terrainY = terrainHeights[mapIndex];
                    int scanTop = Math.Min(
                        mapSizeY - 1,
                        Math.Max(rainHeights[mapIndex], terrainY + 40)
                    );
                    int highestRemaining = terrainY;
                    for (int y = terrainY + 1; y <= scanTop; y++)
                    {
                        if (GetFluidId(
                                request,
                                localX,
                                y,
                                localZ) > 0)
                        {
                            highestRemaining = Math.Max(
                                highestRemaining,
                                y
                            );
                        }
                        int blockId = GetSolidBlockId(
                            request, localX, y, localZ);
                        if (blockId <= 0)
                        {
                            continue;
                        }

                        if (IsLivingFloraOrTree(blockId))
                        {
                            SetSolidBlock(request, localX, y, localZ, 0);
                            trace.RemovedLivingFlora++;
                            continue;
                        }

                        highestRemaining = y;
                    }

                    rainHeights[mapIndex] = (ushort)Math.Max(
                        terrainY,
                        highestRemaining
                    );
                }
            }
        }

        private void ConvertWaterAndGround(
            IChunkColumnGenerateRequest request,
            ushort[] terrainHeights,
            ushort[] rainHeights,
            bool[] coreColumns,
            bool[] waterColumns,
            int mapSizeY,
            int chunkOriginX,
            int chunkOriginZ,
            MireEnvironmentChunkTrace trace)
        {
            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                for (int localX = 0; localX < ChunkSize; localX++)
                {
                    int mapIndex = localZ * ChunkSize + localX;
                    if (!coreColumns[mapIndex])
                    {
                        continue;
                    }

                    int terrainY = terrainHeights[mapIndex];
                    int scanTop = Math.Min(
                        mapSizeY - 1,
                        Math.Max(rainHeights[mapIndex], terrainY + 8)
                    );
                    for (int y = Math.Max(0, terrainY); y <= scanTop; y++)
                    {
                        int fluidId = GetFluidId(
                            request, localX, y, localZ);
                        if (!toxicWaterByFreshWater.TryGetValue(
                                fluidId,
                                out int toxicId))
                        {
                            if (toxicWaterBlockIds.Contains(fluidId))
                            {
                                waterColumns[mapIndex] = true;
                            }
                            continue;
                        }

                        SetFluidBlock(
                            request, localX, y, localZ, toxicId);
                        waterColumns[mapIndex] = true;
                        trace.ToxicWaterBlocks++;
                    }

                    int floorId = GetSolidBlockId(
                        request, localX, terrainY, localZ);
                    if (!IsNaturalSurface(floorId))
                    {
                        continue;
                    }

                    int worldX = chunkOriginX + localX;
                    int worldZ = chunkOriginZ + localZ;
                    ulong hash = StableHash(
                        worldSeed, worldX, worldZ, GroundSalt);
                    int groundId = ChooseGround(
                        hash,
                        waterColumns[mapIndex],
                        HasAdjacentWater(waterColumns, localX, localZ)
                    );
                    if (floorId != groundId)
                    {
                        SetSolidBlock(
                            request, localX, terrainY, localZ, groundId);
                        trace.ToxicFloorColumns++;
                    }
                }
            }
        }

        private int ChooseGround(
            ulong hash,
            bool underwater,
            bool shore)
        {
            int roll = (int)(hash % 100);
            if (underwater)
            {
                return roll < 70
                    ? mirePeatBlockId
                    : roll < 92
                        ? mireMudBlockId
                        : mireSulfurBlockId;
            }
            if (shore)
            {
                return roll < 48
                    ? mireMudBlockId
                    : roll < 80
                        ? mirePeatBlockId
                        : roll < 92
                            ? mireAshBlockId
                            : mireSulfurBlockId;
            }
            return roll < 55
                ? mireAshBlockId
                : roll < 88
                    ? mireMudBlockId
                    : roll < 96
                        ? mirePeatBlockId
                        : mireSulfurBlockId;
        }

        private void GenerateDeadTrees(
            IChunkColumnGenerateRequest request,
            DangerWorldState state,
            ushort[] terrainHeights,
            ushort[] rainHeights,
            bool[] waterColumns,
            int mapSizeY,
            int chunkOriginX,
            int chunkOriginZ,
            MireEnvironmentChunkTrace trace,
            ref ushort yMax)
        {
            ulong chunkHash = StableHash(
                worldSeed,
                request.ChunkX,
                request.ChunkZ,
                TreeSalt
            );
            for (int attempt = 0;
                attempt < DeadTreeAttemptsPerChunk;
                attempt++)
            {
                ulong hash = Mix(
                    chunkHash + (ulong)(attempt * 0x9E3779B9));
                int localX = TreeMargin + Range(
                    hash, 0, ChunkSize - TreeMargin * 2 - 1);
                int localZ = TreeMargin + Range(
                    Mix(hash + 1), 0, ChunkSize - TreeMargin * 2 - 1);
                int worldX = chunkOriginX + localX;
                int worldZ = chunkOriginZ + localZ;
                int mapIndex = localZ * ChunkSize + localX;
                int baseY = terrainHeights[mapIndex];
                if (!WorldZoneLayout.IsInsideLevelCore(
                        state,
                        PoisonMireLevel,
                        BoundaryExclusionWidth,
                        worldX + 0.5,
                        worldZ + 0.5) ||
                    !IsInDeadTreeField(worldX, worldZ) ||
                    waterColumns[mapIndex] ||
                    baseY < 2 ||
                    baseY + MaximumTreeHeight + 2 >= mapSizeY ||
                    !HasGentleFoundation(
                        terrainHeights, localX, localZ, baseY) ||
                    GetFluidId(
                        request, localX, baseY + 1, localZ) != 0)
                {
                    continue;
                }

                DeadLogPalette logs = Mix(hash + 2) % 100 < 68
                    ? veryRottenLogs
                    : rottenLogs;
                int height = Range(
                    Mix(hash + 5), MinimumTreeHeight, MaximumTreeHeight);
                int blocksBefore = trace.DeadLogBlocks;
                for (int offsetY = 1; offsetY <= height; offsetY++)
                {
                    int y = baseY + offsetY;
                    if (!TryPlaceReplaceableBlock(
                            request,
                            localX,
                            y,
                            localZ,
                            logs.Vertical))
                    {
                        break;
                    }
                    trace.DeadLogBlocks++;
                    RaiseRainHeight(
                        rainHeights, localX, localZ, y, ref yMax);
                }

                int placedTrunkBlocks =
                    trace.DeadLogBlocks - blocksBefore;
                if (placedTrunkBlocks < MinimumTreeHeight - 1)
                {
                    continue;
                }

                int branchCount = Range(Mix(hash + 3), 1, 3);
                int firstDirection = Range(Mix(hash + 4), 0, 3);
                for (int branch = 0; branch < branchCount; branch++)
                {
                    ulong branchHash =
                        Mix(hash + (ulong)(8 + branch * 5));
                    int direction =
                        (firstDirection + branch +
                         (int)(branchHash & 1) * 2) & 3;
                    int directionX = direction == 0
                        ? 1
                        : direction == 2 ? -1 : 0;
                    int directionZ = direction == 1
                        ? 1
                        : direction == 3 ? -1 : 0;
                    int branchY = baseY + Range(
                        branchHash,
                        Math.Max(3, placedTrunkBlocks / 2),
                        placedTrunkBlocks - 1
                    );
                    int branchLength = Range(
                        Mix(branchHash + 1), 1, 2);
                    for (int step = 1; step <= branchLength; step++)
                    {
                        int branchX = localX + directionX * step;
                        int branchZ = localZ + directionZ * step;
                        if (branchX <= 0 ||
                            branchX >= ChunkSize - 1 ||
                            branchZ <= 0 ||
                            branchZ >= ChunkSize - 1 ||
                            !TryPlaceReplaceableBlock(
                                request,
                                branchX,
                                branchY,
                                branchZ,
                                directionX == 0
                                    ? logs.NorthSouth
                                    : logs.WestEast))
                        {
                            break;
                        }
                        trace.DeadLogBlocks++;
                        RaiseRainHeight(
                            rainHeights,
                            branchX,
                            branchZ,
                            branchY,
                            ref yMax
                        );
                    }
                }
                trace.DeadTrees++;
            }
        }

        private void GenerateDeadPlantsAndMist(
            IChunkColumnGenerateRequest request,
            ushort[] terrainHeights,
            ushort[] rainHeights,
            bool[] coreColumns,
            bool[] waterColumns,
            int mapSizeY,
            int chunkOriginX,
            int chunkOriginZ,
            MireEnvironmentChunkTrace trace,
            ref ushort yMax)
        {
            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                for (int localX = 0; localX < ChunkSize; localX++)
                {
                    int mapIndex = localZ * ChunkSize + localX;
                    if (!coreColumns[mapIndex])
                    {
                        continue;
                    }

                    int terrainY = terrainHeights[mapIndex];
                    int worldX = chunkOriginX + localX;
                    int worldZ = chunkOriginZ + localZ;
                    bool shore = HasAdjacentWater(
                        waterColumns, localX, localZ);
                    ulong hash = StableHash(
                        worldSeed, worldX, worldZ, PlantSalt);

                    if (waterColumns[mapIndex])
                    {
                        if (Mix(hash + MistSalt) %
                                MistEmitterSpacing == 0)
                        {
                            int mistY = Math.Max(
                                terrainY,
                                rainHeights[mapIndex]
                            ) + 1;
                            if (mistY < mapSizeY - 1 &&
                                TryPlaceReplaceableBlock(
                                    request,
                                    localX,
                                    mistY,
                                    localZ,
                                    mistBlockId))
                            {
                                trace.MistEmitters++;
                            }
                        }
                        continue;
                    }

                    int chance = shore
                        ? ShoreDeadPlantChancePercent
                        : DeadPlantChancePercent;
                    if (hash % 100 >= (ulong)chance)
                    {
                        continue;
                    }

                    int selection = (int)(Mix(hash + 1) % 100);
                    int blockId;
                    if (shore)
                    {
                        blockId = selection < 42
                            ? deadReedsBlockId
                            : selection < 66
                                ? deadGrassBlockId
                                : selection < 80
                                    ? fungalCrustBlockId
                                    : selection < 91
                                        ? thornBushBlockId
                                        : fallenBranchBlockId;
                    }
                    else
                    {
                        blockId = selection < 42
                            ? deadGrassBlockId
                            : selection < 65
                                ? thornBushBlockId
                                : selection < 78
                                    ? fungalCrustBlockId
                                    : selection < 90
                                        ? fallenBranchBlockId
                                        : rottedStumpBlockId;
                    }

                    int y = terrainY + 1;
                    if (y >= mapSizeY - 1 ||
                        GetFluidId(request, localX, y, localZ) != 0 ||
                        !TryPlaceReplaceableBlock(
                            request, localX, y, localZ, blockId))
                    {
                        continue;
                    }

                    trace.MirePlantBlocks++;
                    RaiseRainHeight(
                        rainHeights, localX, localZ, y, ref yMax);
                }
            }
        }

        private bool IsInDeadTreeField(int worldX, int worldZ) =>
            IsInsideAnyField(
                worldX,
                worldZ,
                DeadTreeFieldCellSize,
                DeadTreeFieldMinimumRadius,
                DeadTreeFieldMaximumRadius,
                0x4D49524544454144UL
            );

        private bool IsInsideAnyField(
            int worldX,
            int worldZ,
            int cellSize,
            int minimumRadius,
            int maximumRadius,
            ulong salt)
        {
            int originCellX = FloorDiv(worldX, cellSize);
            int originCellZ = FloorDiv(worldZ, cellSize);
            for (int cellZ = originCellZ - 1;
                cellZ <= originCellZ + 1;
                cellZ++)
            {
                for (int cellX = originCellX - 1;
                    cellX <= originCellX + 1;
                    cellX++)
                {
                    ulong hash = StableHash(
                        worldSeed, cellX, cellZ, salt);
                    if (Mix(hash + 5) % 8 >= 5)
                    {
                        continue;
                    }

                    int centerX = cellX * cellSize + Range(
                        hash, cellSize * 3 / 10, cellSize * 7 / 10);
                    int centerZ = cellZ * cellSize + Range(
                        Mix(hash + 1),
                        cellSize * 3 / 10,
                        cellSize * 7 / 10
                    );
                    int radiusX = Range(
                        Mix(hash + 2), minimumRadius, maximumRadius);
                    int radiusZ = Range(
                        Mix(hash + 3), minimumRadius, maximumRadius);
                    double normalizedX =
                        (worldX - centerX) / (double)radiusX;
                    double normalizedZ =
                        (worldZ - centerZ) / (double)radiusZ;
                    if (normalizedX * normalizedX +
                        normalizedZ * normalizedZ <= 1)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsLivingFloraOrTree(int blockId)
        {
            if (blockId <= 0 || blockId >= api.World.Blocks.Count)
            {
                return false;
            }

            Block? block = api.World.Blocks[blockId];
            if (block?.Code == null)
            {
                return false;
            }
            if (block.Code.Domain.Equals(
                    "apprenticemire",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = block.Code.Path;
            return block.BlockMaterial == EnumBlockMaterial.Plant ||
                block.BlockMaterial == EnumBlockMaterial.Leaves ||
                path.StartsWith("log-", StringComparison.Ordinal) ||
                path.StartsWith("debarkedlog-", StringComparison.Ordinal) ||
                path.StartsWith("branchy-", StringComparison.Ordinal) ||
                path.StartsWith("bamboo-", StringComparison.Ordinal);
        }

        private bool IsNaturalSurface(int blockId)
        {
            if (blockId <= 0 || blockId >= api.World.Blocks.Count)
            {
                return false;
            }

            Block? block = api.World.Blocks[blockId];
            if (block?.Code == null)
            {
                return false;
            }
            if (block.Code.Domain.Equals(
                    "apprenticemire",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string path = block.Code.Path;
            return path.StartsWith("soil-", StringComparison.Ordinal) ||
                path.Equals("soil", StringComparison.Ordinal) ||
                path.StartsWith("sand-", StringComparison.Ordinal) ||
                path.Equals("sand", StringComparison.Ordinal) ||
                path.StartsWith("gravel-", StringComparison.Ordinal) ||
                path.Equals("gravel", StringComparison.Ordinal) ||
                path.StartsWith("muddygravel-", StringComparison.Ordinal) ||
                path.Equals("muddygravel", StringComparison.Ordinal) ||
                path.StartsWith("rock-", StringComparison.Ordinal) ||
                path.StartsWith("clay", StringComparison.Ordinal) ||
                path.StartsWith("peat", StringComparison.Ordinal);
        }

        private static bool HasAdjacentWater(
            bool[] waterColumns,
            int localX,
            int localZ)
        {
            int mapIndex = localZ * ChunkSize + localX;
            return (localX > 0 && waterColumns[mapIndex - 1]) ||
                (localX < ChunkSize - 1 &&
                    waterColumns[mapIndex + 1]) ||
                (localZ > 0 &&
                    waterColumns[mapIndex - ChunkSize]) ||
                (localZ < ChunkSize - 1 &&
                    waterColumns[mapIndex + ChunkSize]);
        }

        private bool TryResolveBlock(
            string code,
            out int blockId,
            out string error)
        {
            Block? block = api.World.GetBlock(new AssetLocation(code));
            if (block == null || block.Id <= 0)
            {
                blockId = 0;
                error = $"required block {code} is missing";
                return false;
            }

            blockId = block.Id;
            error = string.Empty;
            return true;
        }

        private bool TryResolveDeadLogPalette(
            string decay,
            out DeadLogPalette palette,
            out string error)
        {
            if (!TryResolveBlock(
                    $"game:debarkedlog-{decay}-ud",
                    out int vertical,
                    out error) ||
                !TryResolveBlock(
                    $"game:debarkedlog-{decay}-ns",
                    out int northSouth,
                    out error) ||
                !TryResolveBlock(
                    $"game:debarkedlog-{decay}-we",
                    out int westEast,
                    out error))
            {
                palette = default;
                return false;
            }

            palette = new DeadLogPalette(
                vertical, northSouth, westEast);
            return true;
        }

        private static bool HasGentleFoundation(
            ushort[] terrainHeights,
            int localX,
            int localZ,
            int height)
        {
            int mapIndex = localZ * ChunkSize + localX;
            return Math.Abs(terrainHeights[mapIndex - 1] - height) <= 1 &&
                Math.Abs(terrainHeights[mapIndex + 1] - height) <= 1 &&
                Math.Abs(
                    terrainHeights[mapIndex - ChunkSize] - height) <= 1 &&
                Math.Abs(
                    terrainHeights[mapIndex + ChunkSize] - height) <= 1;
        }

        private bool TryPlaceReplaceableBlock(
            IChunkColumnGenerateRequest request,
            int localX,
            int y,
            int localZ,
            int blockId)
        {
            int previousBlockId = GetSolidBlockId(
                request, localX, y, localZ);
            if (previousBlockId != 0)
            {
                if (previousBlockId >= api.World.Blocks.Count ||
                    api.World.Blocks[previousBlockId] == null ||
                    api.World.Blocks[previousBlockId].Replaceable < 6000)
                {
                    return false;
                }
            }

            SetSolidBlock(request, localX, y, localZ, blockId);
            return true;
        }

        private static int GetSolidBlockId(
            IChunkColumnGenerateRequest request,
            int localX,
            int y,
            int localZ)
        {
            if (y < 0)
            {
                return 0;
            }
            int chunkY = y / ChunkSize;
            if (chunkY < 0 || chunkY >= request.Chunks.Length)
            {
                return 0;
            }
            IServerChunk? chunk = request.Chunks[chunkY];
            if (chunk == null || chunk.Disposed)
            {
                return 0;
            }
            return chunk.Data.GetBlockIdUnsafe(
                ChunkIndex3d(localX, y % ChunkSize, localZ));
        }

        private static int GetFluidId(
            IChunkColumnGenerateRequest request,
            int localX,
            int y,
            int localZ)
        {
            if (y < 0)
            {
                return 0;
            }
            int chunkY = y / ChunkSize;
            if (chunkY < 0 || chunkY >= request.Chunks.Length)
            {
                return 0;
            }
            IServerChunk? chunk = request.Chunks[chunkY];
            if (chunk == null || chunk.Disposed)
            {
                return 0;
            }
            return chunk.Data.GetFluid(
                ChunkIndex3d(localX, y % ChunkSize, localZ));
        }

        private static void SetSolidBlock(
            IChunkColumnGenerateRequest request,
            int localX,
            int y,
            int localZ,
            int blockId)
        {
            IChunkBlocks data = request.Chunks[y / ChunkSize].Data;
            data[ChunkIndex3d(localX, y % ChunkSize, localZ)] = blockId;
        }

        private static void SetFluidBlock(
            IChunkColumnGenerateRequest request,
            int localX,
            int y,
            int localZ,
            int blockId)
        {
            IChunkBlocks data = request.Chunks[y / ChunkSize].Data;
            data.SetFluid(
                ChunkIndex3d(localX, y % ChunkSize, localZ),
                blockId
            );
        }

        private static void RaiseRainHeight(
            ushort[] rainHeights,
            int localX,
            int localZ,
            int y,
            ref ushort yMax)
        {
            int mapIndex = localZ * ChunkSize + localX;
            rainHeights[mapIndex] = (ushort)Math.Max(
                rainHeights[mapIndex], y);
            yMax = (ushort)Math.Max(yMax, y);
        }

        private static int ChunkIndex3d(
            int localX,
            int localY,
            int localZ) =>
            (localY * ChunkSize + localZ) * ChunkSize + localX;

        private static long ChunkKey(int chunkX, int chunkZ) =>
            ((long)(uint)chunkX << 32) | (uint)chunkZ;

        private static int FloorDiv(double value, int divisor) =>
            (int)Math.Floor(value / divisor);

        private static int Range(
            ulong hash,
            int minimum,
            int maximum) =>
            minimum +
            (int)(hash % (ulong)(maximum - minimum + 1));

        private static ulong StableHash(
            long seed,
            int x,
            int z,
            ulong salt)
        {
            ulong value = unchecked((ulong)seed) ^ salt;
            value ^= unchecked((ulong)(long)x) *
                0x9E3779B185EBCA87UL;
            value = Mix(value);
            value ^= unchecked((ulong)(long)z) *
                0xC2B2AE3D27D4EB4FUL;
            return Mix(value);
        }

        private static ulong Mix(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        private sealed class ProbeSlot
        {
            internal MireEnvironmentChunkTrace? Trace { get; set; }
        }

        private readonly record struct DeadLogPalette(
            int Vertical,
            int NorthSouth,
            int WestEast
        );
    }

    internal sealed class MireEnvironmentChunkTrace
    {
        internal int DeadTrees { get; set; }
        internal int DeadLogBlocks { get; set; }
        internal int MirePlantBlocks { get; set; }
        internal int ToxicFloorColumns { get; set; }
        internal int ToxicWaterBlocks { get; set; }
        internal int RemovedLivingFlora { get; set; }
        internal int MistEmitters { get; set; }
        internal double GeneratorMilliseconds { get; set; }

        internal bool Changed =>
            DeadTrees > 0 ||
            DeadLogBlocks > 0 ||
            MirePlantBlocks > 0 ||
            ToxicFloorColumns > 0 ||
            ToxicWaterBlocks > 0 ||
            RemovedLivingFlora > 0 ||
            MistEmitters > 0;
    }
}
