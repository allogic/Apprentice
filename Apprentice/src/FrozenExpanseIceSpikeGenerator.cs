using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Apprentice
{
    /// <summary>
    /// Adds deterministic ice-spike fields to the core of Level 5 after
    /// Vintage Story has finished terrain, caves and block layers. Every
    /// chunk derives the same intersecting fields from world coordinates, so
    /// spikes crossing chunk borders do not depend on generation order.
    /// </summary>
    internal sealed class FrozenExpanseIceSpikeGenerator
    {
        internal const int FrozenExpanseLevel = 5;
        internal const int BoundaryExclusionWidth = 192;
        internal const int FieldCellSize = 640;
        internal const int FieldMinimumRadius = 150;
        internal const int FieldMaximumRadius = 205;
        internal const int MainSpikeMinimumHeight = 35;
        internal const int MainSpikeMaximumHeight = 60;
        internal const int MediumSpikeMinimumHeight = 22;
        internal const int MediumSpikeMaximumHeight = 36;
        internal const int SmallSpikeMinimumHeight = 8;
        internal const int SmallSpikeMaximumHeight = 20;
        internal const int MaximumSpikeRadius = 14;
        internal const int MaximumLean = 4;
        internal const double ExpectedFieldCoverageMinimum = 0.20;
        internal const double ExpectedFieldCoverageMaximum = 0.30;

        private const int ChunkSize = GlobalConstants.ChunkSize;
        private const int NeighbourCellPadding =
            FieldMaximumRadius + MaximumSpikeRadius + MaximumLean + 2;
        private const ulong FieldSalt = 0x4943454649454C44UL;
        private const ulong SpikeSalt = 0x4943455350494B45UL;
        private const double GoldenAngle = 2.39996322972865332;

        private readonly ICoreServerAPI api;
        private readonly ConcurrentDictionary<long, ProbeSlot> probeSlots =
            new();
        private DangerWorldState? activeState;
        private long worldSeed;
        private int glacierIceBlockId;
        private int packedGlacierIceBlockId;
        private bool initialized;
        private static long affectedChunks;
        private static long placedBlocks;

        internal FrozenExpanseIceSpikeGenerator(ICoreServerAPI api)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
        }

        internal bool Initialized => initialized;

        internal static long AffectedChunks =>
            System.Threading.Interlocked.Read(ref affectedChunks);

        internal static long PlacedBlocks =>
            System.Threading.Interlocked.Read(ref placedBlocks);

        internal bool Initialize(
            DangerWorldState state,
            out string error)
        {
            Reset();
            Block? glacierIce = api.World.GetBlock(
                new AssetLocation("game:glacierice")
            );
            Block? packedGlacierIce = api.World.GetBlock(
                new AssetLocation("game:packedglacierice")
            );
            if (glacierIce == null || glacierIce.Id <= 0)
            {
                error = "required vanilla block game:glacierice is missing";
                return false;
            }
            if (packedGlacierIce == null || packedGlacierIce.Id <= 0)
            {
                error =
                    "required vanilla block game:packedglacierice is missing";
                return false;
            }

            activeState = state;
            worldSeed = api.WorldManager.Seed;
            glacierIceBlockId = glacierIce.Id;
            packedGlacierIceBlockId = packedGlacierIce.Id;
            initialized = true;
            error = string.Empty;
            return true;
        }

        internal void Reset()
        {
            activeState = null;
            glacierIceBlockId = 0;
            packedGlacierIceBlockId = 0;
            initialized = false;
            probeSlots.Clear();
            System.Threading.Interlocked.Exchange(ref affectedChunks, 0);
            System.Threading.Interlocked.Exchange(ref placedBlocks, 0);
        }

        internal void OnChunkColumnGeneration(
            IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = activeState;
            if (!initialized || state == null ||
                !WorldZoneLayout.ChunkIntersectsLevel(
                    state,
                    FrozenExpanseLevel,
                    request.ChunkX,
                    request.ChunkZ,
                    ChunkSize))
            {
                return;
            }

            long chunkKey = ChunkKey(request.ChunkX, request.ChunkZ);
            probeSlots.TryGetValue(chunkKey, out ProbeSlot? probeSlot);
            long started = Stopwatch.GetTimestamp();
            IceSpikeChunkTrace trace = GenerateChunk(
                request,
                state,
                probeSlot?.TargetSpikeId
            );
            trace.GeneratorMilliseconds =
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            if (probeSlot != null)
            {
                probeSlot.Trace = trace;
            }

            if (trace.PlacedBlocks <= 0)
            {
                return;
            }

            System.Threading.Interlocked.Increment(ref affectedChunks);
            System.Threading.Interlocked.Add(
                ref placedBlocks,
                trace.PlacedBlocks
            );
        }

        internal bool TryFindNearestField(
            double worldX,
            double worldZ,
            bool requireBoundaryCrossingMain,
            out IceSpikeField? selectedField,
            out IceSpikeDefinition? selectedMain)
        {
            DangerWorldState? state = activeState;
            selectedField = null;
            selectedMain = null;
            if (!initialized || state == null)
            {
                return false;
            }

            int originCellX = FloorDiv(worldX, FieldCellSize);
            int originCellZ = FloorDiv(worldZ, FieldCellSize);
            double bestDistanceSquared = double.PositiveInfinity;
            for (int radius = 0; radius <= 12; radius++)
            {
                int minimumCellX = originCellX - radius;
                int maximumCellX = originCellX + radius;
                int minimumCellZ = originCellZ - radius;
                int maximumCellZ = originCellZ + radius;
                for (int cellZ = minimumCellZ;
                    cellZ <= maximumCellZ;
                    cellZ++)
                {
                    for (int cellX = minimumCellX;
                        cellX <= maximumCellX;
                        cellX++)
                    {
                        if (radius > 0 &&
                            cellX != minimumCellX &&
                            cellX != maximumCellX &&
                            cellZ != minimumCellZ &&
                            cellZ != maximumCellZ)
                        {
                            continue;
                        }

                        IceSpikeField field = BuildField(
                            worldSeed,
                            cellX,
                            cellZ
                        );
                        if (!FieldFitsRealmCore(state, field))
                        {
                            continue;
                        }

                        IceSpikeDefinition? main = field.Spikes
                            .Where(spike => spike.Size ==
                                IceSpikeSize.Main)
                            .Where(spike =>
                                !requireBoundaryCrossingMain ||
                                CrossesChunkBoundary(spike))
                            .OrderByDescending(spike => spike.Height)
                            .FirstOrDefault();
                        if (main == null)
                        {
                            continue;
                        }

                        double dx = field.CenterX - worldX;
                        double dz = field.CenterZ - worldZ;
                        double distanceSquared = dx * dx + dz * dz;
                        if (distanceSquared >= bestDistanceSquared)
                        {
                            continue;
                        }

                        bestDistanceSquared = distanceSquared;
                        selectedField = field;
                        selectedMain = main;
                    }
                }

                if (selectedField != null &&
                    bestDistanceSquared <=
                        (radius * FieldCellSize) *
                        (radius * FieldCellSize))
                {
                    break;
                }
            }

            return selectedField != null && selectedMain != null;
        }

        internal IReadOnlyList<IceSpikeProbeChunk> BuildProbeChunks(
            IceSpikeDefinition spike)
        {
            int centerChunkX = FloorDiv(spike.CenterX, ChunkSize);
            int centerChunkZ = FloorDiv(spike.CenterZ, ChunkSize);
            List<IceSpikeProbeChunk> chunks = new(9);
            for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
            {
                for (int offsetX = -1; offsetX <= 1; offsetX++)
                {
                    chunks.Add(
                        new IceSpikeProbeChunk(
                            centerChunkX + offsetX,
                            centerChunkZ + offsetZ
                        )
                    );
                }
            }
            return chunks;
        }

        internal IReadOnlySet<long> GetIntersectingChunkKeys(
            IceSpikeDefinition spike)
        {
            int extent = (int)Math.Ceiling(
                spike.BaseRadius *
                Math.Max(spike.AspectX, spike.AspectZ)
            ) +
                Math.Max(
                    Math.Abs(spike.LeanX),
                    Math.Abs(spike.LeanZ)
                ) + 1;
            int minimumChunkX = FloorDiv(
                spike.CenterX - extent,
                ChunkSize
            );
            int maximumChunkX = FloorDiv(
                spike.CenterX + extent,
                ChunkSize
            );
            int minimumChunkZ = FloorDiv(
                spike.CenterZ - extent,
                ChunkSize
            );
            int maximumChunkZ = FloorDiv(
                spike.CenterZ + extent,
                ChunkSize
            );
            HashSet<long> keys = new();
            for (int chunkZ = minimumChunkZ;
                chunkZ <= maximumChunkZ;
                chunkZ++)
            {
                for (int chunkX = minimumChunkX;
                    chunkX <= maximumChunkX;
                    chunkX++)
                {
                    if (SpikeTouchesChunkHorizontally(
                            spike,
                            chunkX,
                            chunkZ))
                    {
                        keys.Add(ChunkKey(chunkX, chunkZ));
                    }
                }
            }
            return keys;
        }

        internal bool PrepareProbeChunk(
            int chunkX,
            int chunkZ,
            ulong targetSpikeId)
        {
            return probeSlots.TryAdd(
                ChunkKey(chunkX, chunkZ),
                new ProbeSlot(targetSpikeId)
            );
        }

        internal bool TryTakeProbeTrace(
            int chunkX,
            int chunkZ,
            out IceSpikeChunkTrace? trace)
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

        internal static double ExpectedFieldCoverageFraction
        {
            get
            {
                double averageRadius =
                    (FieldMinimumRadius + FieldMaximumRadius) / 2d;
                return Math.PI * averageRadius * averageRadius /
                    (FieldCellSize * FieldCellSize);
            }
        }

        private IceSpikeChunkTrace GenerateChunk(
            IChunkColumnGenerateRequest request,
            DangerWorldState state,
            ulong? targetSpikeId)
        {
            IceSpikeChunkTrace trace = new();
            List<IceSpikeDefinition> spikes = GetIntersectingSpikes(
                state,
                request.ChunkX,
                request.ChunkZ
            );
            if (spikes.Count == 0)
            {
                return trace;
            }

            IMapChunk mapChunk = request.Chunks[0].MapChunk;
            ushort[] terrainHeights =
                mapChunk.WorldGenTerrainHeightMap;
            ushort[] rainHeights = mapChunk.RainHeightMap;
            ushort[] baseHeights =
                (ushort[])terrainHeights.Clone();
            bool[] changedColumns = new bool[ChunkSize * ChunkSize];
            int[] generatedTops = new int[ChunkSize * ChunkSize];
            int mapSizeY = Math.Min(
                api.WorldManager.MapSizeY,
                request.Chunks.Length * ChunkSize
            );
            int chunkOriginX = request.ChunkX * ChunkSize;
            int chunkOriginZ = request.ChunkZ * ChunkSize;

            foreach (IceSpikeDefinition spike in spikes)
            {
                int blocksBefore = trace.PlacedBlocks;
                GenerateSpikePortion(
                    request,
                    state,
                    spike,
                    baseHeights,
                    changedColumns,
                    generatedTops,
                    mapSizeY,
                    chunkOriginX,
                    chunkOriginZ,
                    trace
                );
                if (trace.PlacedBlocks <= blocksBefore)
                {
                    continue;
                }

                trace.RecordIntersectingSpike(spike.Id);
                trace.MinimumSpikeHeight = Math.Min(
                    trace.MinimumSpikeHeight,
                    spike.Height
                );
                trace.MaximumSpikeHeight = Math.Max(
                    trace.MaximumSpikeHeight,
                    spike.Height
                );
                if (targetSpikeId.HasValue &&
                    spike.Id == targetSpikeId.Value)
                {
                    trace.TargetSpikeBlocks +=
                        trace.PlacedBlocks - blocksBefore;
                    trace.TargetSpikeHeight = spike.Height;
                }
            }

            ushort yMax = mapChunk.YMax;
            for (int index = 0;
                index < changedColumns.Length;
                index++)
            {
                if (!changedColumns[index])
                {
                    continue;
                }

                int top = generatedTops[index];
                terrainHeights[index] = (ushort)Math.Max(
                    terrainHeights[index],
                    top
                );
                rainHeights[index] = (ushort)Math.Max(
                    rainHeights[index],
                    top
                );
                yMax = (ushort)Math.Max(yMax, top);
                trace.ModifiedColumns++;
            }
            mapChunk.YMax = yMax;
            if (trace.MinimumSpikeHeight == int.MaxValue)
            {
                trace.MinimumSpikeHeight = 0;
            }
            return trace;
        }

        private void GenerateSpikePortion(
            IChunkColumnGenerateRequest request,
            DangerWorldState state,
            IceSpikeDefinition spike,
            ushort[] baseHeights,
            bool[] changedColumns,
            int[] generatedTops,
            int mapSizeY,
            int chunkOriginX,
            int chunkOriginZ,
            IceSpikeChunkTrace trace)
        {
            double aspectX = spike.AspectX;
            double aspectZ = spike.AspectZ;
            for (int layer = -2; layer < spike.Height; layer++)
            {
                double normalized = layer <= 0
                    ? 0
                    : (double)layer / Math.Max(1, spike.Height - 1);
                double leanProgress = Math.Pow(normalized, 1.25);
                int centerX = spike.CenterX + (int)Math.Round(
                    spike.LeanX * leanProgress,
                    MidpointRounding.AwayFromZero
                );
                int centerZ = spike.CenterZ + (int)Math.Round(
                    spike.LeanZ * leanProgress,
                    MidpointRounding.AwayFromZero
                );
                double taper = Math.Pow(
                    Math.Max(0, 1 - normalized),
                    0.72
                );
                double nominalRadius = Math.Max(
                    0.85,
                    Math.Floor(spike.BaseRadius * taper * 2) / 2
                );
                int bounds = (int)Math.Ceiling(
                    nominalRadius *
                    Math.Max(aspectX, aspectZ) + 1
                );
                int minimumWorldX = Math.Max(
                    chunkOriginX,
                    centerX - bounds
                );
                int maximumWorldX = Math.Min(
                    chunkOriginX + ChunkSize - 1,
                    centerX + bounds
                );
                int minimumWorldZ = Math.Max(
                    chunkOriginZ,
                    centerZ - bounds
                );
                int maximumWorldZ = Math.Min(
                    chunkOriginZ + ChunkSize - 1,
                    centerZ + bounds
                );
                if (minimumWorldX > maximumWorldX ||
                    minimumWorldZ > maximumWorldZ)
                {
                    continue;
                }

                for (int worldZ = minimumWorldZ;
                    worldZ <= maximumWorldZ;
                    worldZ++)
                {
                    int localZ = worldZ - chunkOriginZ;
                    int dz = worldZ - centerZ;
                    for (int worldX = minimumWorldX;
                        worldX <= maximumWorldX;
                        worldX++)
                    {
                        if (!WorldZoneLayout.IsInsideLevelCore(
                            state,
                            FrozenExpanseLevel,
                            BoundaryExclusionWidth,
                            worldX + 0.5,
                            worldZ + 0.5))
                        {
                            continue;
                        }

                        int localX = worldX - chunkOriginX;
                        int dx = worldX - centerX;
                        double edgeBias = ColumnEdgeBias(
                            spike.Id,
                            worldX,
                            worldZ
                        );
                        double radius = Math.Max(
                            0.75,
                            nominalRadius + edgeBias
                        );
                        double normalizedX =
                            dx / (radius * aspectX);
                        double normalizedZ =
                            dz / (radius * aspectZ);
                        double radialSquared =
                            normalizedX * normalizedX +
                            normalizedZ * normalizedZ;
                        if (radialSquared > 1)
                        {
                            continue;
                        }

                        int mapIndex =
                            localZ * ChunkSize + localX;
                        int baseY = baseHeights[mapIndex];
                        int y = baseY + layer;
                        if (baseY < 2 || y < 1 || y >= mapSizeY - 1)
                        {
                            continue;
                        }

                        int blockId = layer <= 1 ||
                            (normalized < 0.72 &&
                             radialSquared < 0.28)
                                ? packedGlacierIceBlockId
                                : glacierIceBlockId;
                        IChunkBlocks data =
                            request.Chunks[y / ChunkSize].Data;
                        int blockIndex = ChunkIndex3d(
                            localX,
                            y % ChunkSize,
                            localZ
                        );
                        int previousBlockId =
                            data.GetBlockIdUnsafe(blockIndex);
                        data[blockIndex] = blockId;
                        data.SetFluid(blockIndex, 0);
                        if (previousBlockId != blockId)
                        {
                            trace.PlacedBlocks++;
                        }
                        changedColumns[mapIndex] = true;
                        generatedTops[mapIndex] = Math.Max(
                            generatedTops[mapIndex],
                            y
                        );
                    }
                }
            }
        }

        private List<IceSpikeDefinition> GetIntersectingSpikes(
            DangerWorldState state,
            int chunkX,
            int chunkZ)
        {
            int minimumWorldX = chunkX * ChunkSize;
            int minimumWorldZ = chunkZ * ChunkSize;
            int maximumWorldX = minimumWorldX + ChunkSize - 1;
            int maximumWorldZ = minimumWorldZ + ChunkSize - 1;
            int minimumCellX = FloorDiv(
                minimumWorldX - NeighbourCellPadding,
                FieldCellSize
            );
            int maximumCellX = FloorDiv(
                maximumWorldX + NeighbourCellPadding,
                FieldCellSize
            );
            int minimumCellZ = FloorDiv(
                minimumWorldZ - NeighbourCellPadding,
                FieldCellSize
            );
            int maximumCellZ = FloorDiv(
                maximumWorldZ + NeighbourCellPadding,
                FieldCellSize
            );
            List<IceSpikeDefinition> spikes = new();
            for (int cellZ = minimumCellZ;
                cellZ <= maximumCellZ;
                cellZ++)
            {
                for (int cellX = minimumCellX;
                    cellX <= maximumCellX;
                    cellX++)
                {
                    IceSpikeField field = BuildField(
                        worldSeed,
                        cellX,
                        cellZ
                    );
                    if (!FieldFitsRealmCore(state, field) ||
                        !EllipseIntersectsRectangle(
                            field,
                            minimumWorldX,
                            minimumWorldZ,
                            maximumWorldX,
                            maximumWorldZ))
                    {
                        continue;
                    }

                    foreach (IceSpikeDefinition spike in field.Spikes)
                    {
                        int extent = (int)Math.Ceiling(
                            spike.BaseRadius *
                            Math.Max(spike.AspectX, spike.AspectZ)
                        ) +
                            Math.Max(
                                Math.Abs(spike.LeanX),
                                Math.Abs(spike.LeanZ)
                            ) + 1;
                        if (spike.CenterX + extent < minimumWorldX ||
                            spike.CenterX - extent > maximumWorldX ||
                            spike.CenterZ + extent < minimumWorldZ ||
                            spike.CenterZ - extent > maximumWorldZ)
                        {
                            continue;
                        }
                        spikes.Add(spike);
                    }
                }
            }
            spikes.Sort((left, right) => left.Id.CompareTo(right.Id));
            return spikes;
        }

        private static IceSpikeField BuildField(
            long seed,
            int cellX,
            int cellZ)
        {
            ulong fieldHash = StableHash(
                seed,
                cellX,
                cellZ,
                FieldSalt
            );
            double centerUnitX = Unit(fieldHash);
            double centerUnitZ = Unit(Mix(fieldHash + 1));
            int centerX = (int)Math.Round(
                cellX * (double)FieldCellSize +
                FieldCellSize * (0.38 + centerUnitX * 0.24),
                MidpointRounding.AwayFromZero
            );
            int centerZ = (int)Math.Round(
                cellZ * (double)FieldCellSize +
                FieldCellSize * (0.38 + centerUnitZ * 0.24),
                MidpointRounding.AwayFromZero
            );
            int radiusX = Range(
                Mix(fieldHash + 2),
                FieldMinimumRadius,
                FieldMaximumRadius
            );
            int radiusZ = Range(
                Mix(fieldHash + 3),
                FieldMinimumRadius,
                FieldMaximumRadius
            );
            double angle = Unit(Mix(fieldHash + 4)) * Math.PI * 2;
            int mainCount = 1 + (int)(Mix(fieldHash + 5) & 1);
            int mediumCount = 3 + (int)(Mix(fieldHash + 6) & 1);
            int smallCount = 7 + (int)(Mix(fieldHash + 7) % 4);
            List<IceSpikeDefinition> spikes =
                new(mainCount + mediumCount + smallCount);
            int index = 0;
            for (int main = 0; main < mainCount; main++, index++)
            {
                spikes.Add(
                    BuildSpike(
                        seed,
                        cellX,
                        cellZ,
                        fieldHash,
                        centerX,
                        centerZ,
                        Math.Min(radiusX, radiusZ),
                        angle,
                        index,
                        IceSpikeSize.Main,
                        main
                    )
                );
            }
            for (int medium = 0;
                medium < mediumCount;
                medium++, index++)
            {
                spikes.Add(
                    BuildSpike(
                        seed,
                        cellX,
                        cellZ,
                        fieldHash,
                        centerX,
                        centerZ,
                        Math.Min(radiusX, radiusZ),
                        angle,
                        index,
                        IceSpikeSize.Medium,
                        medium
                    )
                );
            }
            for (int small = 0;
                small < smallCount;
                small++, index++)
            {
                spikes.Add(
                    BuildSpike(
                        seed,
                        cellX,
                        cellZ,
                        fieldHash,
                        centerX,
                        centerZ,
                        Math.Min(radiusX, radiusZ),
                        angle,
                        index,
                        IceSpikeSize.Small,
                        small
                    )
                );
            }

            return new IceSpikeField(
                cellX,
                cellZ,
                centerX,
                centerZ,
                radiusX,
                radiusZ,
                angle,
                spikes
            );
        }

        private static IceSpikeDefinition BuildSpike(
            long seed,
            int cellX,
            int cellZ,
            ulong fieldHash,
            int fieldCenterX,
            int fieldCenterZ,
            int fieldRadius,
            double fieldAngle,
            int index,
            IceSpikeSize size,
            int sizeIndex)
        {
            ulong hash = StableHash(
                seed,
                cellX,
                cellZ,
                SpikeSalt + (ulong)index
            );
            double jitter =
                (Unit(Mix(hash + 1)) - 0.5) * 0.55;
            double angle =
                fieldAngle + index * GoldenAngle + jitter;
            double radialScale = size switch
            {
                IceSpikeSize.Main => sizeIndex == 0
                    ? 0.08 + Unit(Mix(hash + 2)) * 0.08
                    : 0.27 + Unit(Mix(hash + 2)) * 0.10,
                IceSpikeSize.Medium =>
                    0.34 + Unit(Mix(hash + 2)) * 0.24,
                _ => 0.56 + Unit(Mix(hash + 2)) * 0.27
            };
            int centerX = fieldCenterX + (int)Math.Round(
                Math.Cos(angle) * fieldRadius * radialScale,
                MidpointRounding.AwayFromZero
            );
            int centerZ = fieldCenterZ + (int)Math.Round(
                Math.Sin(angle) * fieldRadius * radialScale,
                MidpointRounding.AwayFromZero
            );
            int height;
            int baseRadius;
            int maximumLean;
            switch (size)
            {
                case IceSpikeSize.Main:
                    height = sizeIndex == 0
                        ? Range(
                            Mix(hash + 3),
                            44,
                            MainSpikeMaximumHeight)
                        : Range(
                            Mix(hash + 3),
                            MainSpikeMinimumHeight,
                            50);
                    baseRadius = sizeIndex == 0
                        ? Range(Mix(hash + 4), 10, MaximumSpikeRadius)
                        : Range(Mix(hash + 4), 8, 12);
                    maximumLean = MaximumLean;
                    break;
                case IceSpikeSize.Medium:
                    height = Range(
                        Mix(hash + 3),
                        MediumSpikeMinimumHeight,
                        MediumSpikeMaximumHeight
                    );
                    baseRadius = Range(Mix(hash + 4), 5, 9);
                    maximumLean = 3;
                    break;
                default:
                    height = Range(
                        Mix(hash + 3),
                        SmallSpikeMinimumHeight,
                        SmallSpikeMaximumHeight
                    );
                    baseRadius = Range(Mix(hash + 4), 3, 5);
                    maximumLean = 2;
                    break;
            }

            double leanAngle = Unit(Mix(hash + 5)) * Math.PI * 2;
            int leanDistance = Range(
                Mix(hash + 6),
                0,
                maximumLean
            );
            int leanX = (int)Math.Round(
                Math.Cos(leanAngle) * leanDistance,
                MidpointRounding.AwayFromZero
            );
            int leanZ = (int)Math.Round(
                Math.Sin(leanAngle) * leanDistance,
                MidpointRounding.AwayFromZero
            );
            double aspectX =
                0.88 + Unit(Mix(hash + 7)) * 0.24;
            double aspectZ =
                0.88 + Unit(Mix(hash + 8)) * 0.24;
            ulong id = StableHash(
                seed,
                cellX,
                cellZ,
                SpikeSalt ^ (ulong)(index + 1) *
                    0x9E3779B185EBCA87UL
            );
            return new IceSpikeDefinition(
                id,
                centerX,
                centerZ,
                height,
                baseRadius,
                leanX,
                leanZ,
                aspectX,
                aspectZ,
                size
            );
        }

        private static bool FieldFitsRealmCore(
            DangerWorldState state,
            IceSpikeField field) =>
            WorldZoneLayout.IsInsideLevelCore(
                state,
                FrozenExpanseLevel,
                BoundaryExclusionWidth +
                    Math.Max(field.RadiusX, field.RadiusZ) +
                    MaximumSpikeRadius +
                    MaximumLean,
                field.CenterX,
                field.CenterZ
            );

        private static bool EllipseIntersectsRectangle(
            IceSpikeField field,
            int minimumX,
            int minimumZ,
            int maximumX,
            int maximumZ)
        {
            int nearestX = Math.Clamp(
                field.CenterX,
                minimumX,
                maximumX
            );
            int nearestZ = Math.Clamp(
                field.CenterZ,
                minimumZ,
                maximumZ
            );
            double dx = nearestX - field.CenterX;
            double dz = nearestZ - field.CenterZ;
            return dx * dx /
                    (field.RadiusX * (double)field.RadiusX) +
                dz * dz /
                    (field.RadiusZ * (double)field.RadiusZ) <= 1.15;
        }

        private static bool CrossesChunkBoundary(
            IceSpikeDefinition spike) =>
            CountIntersectingChunks(spike) > 1;

        private static int CountIntersectingChunks(
            IceSpikeDefinition spike)
        {
            int extent = (int)Math.Ceiling(
                spike.BaseRadius *
                Math.Max(spike.AspectX, spike.AspectZ)
            ) +
                Math.Max(
                    Math.Abs(spike.LeanX),
                    Math.Abs(spike.LeanZ)
                ) + 1;
            int minimumChunkX = FloorDiv(
                spike.CenterX - extent,
                ChunkSize
            );
            int maximumChunkX = FloorDiv(
                spike.CenterX + extent,
                ChunkSize
            );
            int minimumChunkZ = FloorDiv(
                spike.CenterZ - extent,
                ChunkSize
            );
            int maximumChunkZ = FloorDiv(
                spike.CenterZ + extent,
                ChunkSize
            );
            int count = 0;
            for (int chunkZ = minimumChunkZ;
                chunkZ <= maximumChunkZ;
                chunkZ++)
            {
                for (int chunkX = minimumChunkX;
                    chunkX <= maximumChunkX;
                    chunkX++)
                {
                    if (SpikeTouchesChunkHorizontally(
                            spike,
                            chunkX,
                            chunkZ))
                    {
                        count++;
                    }
                }
            }
            return count;
        }

        private static bool SpikeTouchesChunkHorizontally(
            IceSpikeDefinition spike,
            int chunkX,
            int chunkZ)
        {
            int minimumWorldX = chunkX * ChunkSize;
            int maximumWorldX = minimumWorldX + ChunkSize - 1;
            int minimumWorldZ = chunkZ * ChunkSize;
            int maximumWorldZ = minimumWorldZ + ChunkSize - 1;
            for (int layer = -2; layer < spike.Height; layer++)
            {
                double normalized = layer <= 0
                    ? 0
                    : (double)layer / Math.Max(1, spike.Height - 1);
                double leanProgress = Math.Pow(normalized, 1.25);
                int centerX = spike.CenterX + (int)Math.Round(
                    spike.LeanX * leanProgress,
                    MidpointRounding.AwayFromZero
                );
                int centerZ = spike.CenterZ + (int)Math.Round(
                    spike.LeanZ * leanProgress,
                    MidpointRounding.AwayFromZero
                );
                double taper = Math.Pow(
                    Math.Max(0, 1 - normalized),
                    0.72
                );
                double nominalRadius = Math.Max(
                    0.85,
                    Math.Floor(spike.BaseRadius * taper * 2) / 2
                );
                int bounds = (int)Math.Ceiling(
                    nominalRadius *
                    Math.Max(spike.AspectX, spike.AspectZ) + 1
                );
                int firstX = Math.Max(
                    minimumWorldX,
                    centerX - bounds
                );
                int lastX = Math.Min(
                    maximumWorldX,
                    centerX + bounds
                );
                int firstZ = Math.Max(
                    minimumWorldZ,
                    centerZ - bounds
                );
                int lastZ = Math.Min(
                    maximumWorldZ,
                    centerZ + bounds
                );
                for (int worldZ = firstZ; worldZ <= lastZ; worldZ++)
                {
                    int dz = worldZ - centerZ;
                    for (int worldX = firstX;
                        worldX <= lastX;
                        worldX++)
                    {
                        int dx = worldX - centerX;
                        double radius = Math.Max(
                            0.75,
                            nominalRadius + ColumnEdgeBias(
                                spike.Id,
                                worldX,
                                worldZ
                            )
                        );
                        double normalizedX =
                            dx / (radius * spike.AspectX);
                        double normalizedZ =
                            dz / (radius * spike.AspectZ);
                        if (normalizedX * normalizedX +
                                normalizedZ * normalizedZ <= 1)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static double ColumnEdgeBias(
            ulong spikeId,
            int worldX,
            int worldZ)
        {
            unchecked
            {
                ulong value = spikeId ^
                    (uint)worldX * 0x9E3779B185EBCA87UL ^
                    (uint)worldZ * 0xC2B2AE3D27D4EB4FUL;
                double unit = Unit(Mix(value));
                return -0.55 + unit * 1.25;
            }
        }

        private static ulong StableHash(
            long seed,
            int x,
            int z,
            ulong salt)
        {
            unchecked
            {
                ulong value = (ulong)seed ^ salt ^
                    (uint)x * 0x9E3779B185EBCA87UL ^
                    (uint)z * 0xC2B2AE3D27D4EB4FUL;
                return Mix(value);
            }
        }

        private static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private static double Unit(ulong value) =>
            (value >> 11) * (1d / (1UL << 53));

        private static int Range(
            ulong value,
            int minimum,
            int maximum) =>
            minimum + (int)(value %
                (ulong)(maximum - minimum + 1));

        private static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        private static int FloorDiv(double value, int divisor) =>
            (int)Math.Floor(value / divisor);

        private static int ChunkIndex3d(
            int x,
            int y,
            int z) =>
            (y * ChunkSize + z) * ChunkSize + x;

        private static long ChunkKey(int chunkX, int chunkZ) =>
            ((long)(uint)chunkX << 32) | (uint)chunkZ;

        private sealed class ProbeSlot
        {
            internal ProbeSlot(ulong targetSpikeId)
            {
                TargetSpikeId = targetSpikeId;
            }

            internal ulong TargetSpikeId { get; }
            internal IceSpikeChunkTrace? Trace { get; set; }
        }
    }

    internal enum IceSpikeSize
    {
        Main,
        Medium,
        Small
    }

    internal sealed class IceSpikeField
    {
        internal IceSpikeField(
            int cellX,
            int cellZ,
            int centerX,
            int centerZ,
            int radiusX,
            int radiusZ,
            double angle,
            IReadOnlyList<IceSpikeDefinition> spikes)
        {
            CellX = cellX;
            CellZ = cellZ;
            CenterX = centerX;
            CenterZ = centerZ;
            RadiusX = radiusX;
            RadiusZ = radiusZ;
            Angle = angle;
            Spikes = spikes;
        }

        internal int CellX { get; }
        internal int CellZ { get; }
        internal int CenterX { get; }
        internal int CenterZ { get; }
        internal int RadiusX { get; }
        internal int RadiusZ { get; }
        internal double Angle { get; }
        internal IReadOnlyList<IceSpikeDefinition> Spikes { get; }
    }

    internal sealed class IceSpikeDefinition
    {
        internal IceSpikeDefinition(
            ulong id,
            int centerX,
            int centerZ,
            int height,
            int baseRadius,
            int leanX,
            int leanZ,
            double aspectX,
            double aspectZ,
            IceSpikeSize size)
        {
            Id = id;
            CenterX = centerX;
            CenterZ = centerZ;
            Height = height;
            BaseRadius = baseRadius;
            LeanX = leanX;
            LeanZ = leanZ;
            AspectX = aspectX;
            AspectZ = aspectZ;
            Size = size;
        }

        internal ulong Id { get; }
        internal int CenterX { get; }
        internal int CenterZ { get; }
        internal int Height { get; }
        internal int BaseRadius { get; }
        internal int LeanX { get; }
        internal int LeanZ { get; }
        internal double AspectX { get; }
        internal double AspectZ { get; }
        internal IceSpikeSize Size { get; }
    }

    internal sealed class IceSpikeProbeChunk
    {
        internal IceSpikeProbeChunk(int chunkX, int chunkZ)
        {
            ChunkX = chunkX;
            ChunkZ = chunkZ;
        }

        internal int ChunkX { get; }
        internal int ChunkZ { get; }
        internal long StartedTimestamp { get; set; }
        internal long Key =>
            ((long)(uint)ChunkX << 32) | (uint)ChunkZ;
    }

    internal sealed class IceSpikeChunkTrace
    {
        private HashSet<ulong>? intersectingSpikeIds;

        internal IReadOnlyCollection<ulong> IntersectingSpikeIds =>
            intersectingSpikeIds is { } ids
                ? ids
                : Array.Empty<ulong>();
        internal int PlacedBlocks { get; set; }
        internal int ModifiedColumns { get; set; }
        internal int MinimumSpikeHeight { get; set; } = int.MaxValue;
        internal int MaximumSpikeHeight { get; set; }
        internal int TargetSpikeBlocks { get; set; }
        internal int TargetSpikeHeight { get; set; }
        internal double GeneratorMilliseconds { get; set; }

        internal void RecordIntersectingSpike(ulong spikeId)
        {
            intersectingSpikeIds ??= new HashSet<ulong>();
            intersectingSpikeIds.Add(spikeId);
        }
    }
}
