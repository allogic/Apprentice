using System;
using System.Diagnostics;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace Apprentice
{
    /// <summary>
    /// Gives the whole Level 7 realm a permanent cursed identity without
    /// changing terrain height, caves, fluids or the approved ground routes.
    /// Natural surface layers become basaltic/obsidian corruption, ordinary
    /// vegetation is killed or transformed, and sparse leafless wraith trees
    /// create the haunted skyline outside the ruined valleys as well.
    /// </summary>
    internal sealed class ShatteredHighlandsSurfaceGenerator
    {
        internal const int ShatteredHighlandsLevel = 7;
        internal const int BoundaryTransitionWidth = 192;
        internal const int SurfaceDepth = 4;
        internal const int CliffSlopeThreshold = 3;

        private const int ChunkSize = GlobalConstants.ChunkSize;
        private const ulong SurfaceSalt = 0x4355525345444C37UL;
        private const ulong FloraSalt = 0x5749544845524544UL;
        private const ulong TreeSalt = 0x5752414954485452UL;

        private readonly ICoreServerAPI api;
        private DangerWorldState? activeState;
        private bool initialized;
        private int loggedChunks;
        private int basaltId;
        private int crackedBasaltId;
        private int basaltGravelId;
        private int obsidianId;
        private int blackVeinId;
        private int gloomId;
        private int ashenWeedId;
        private int wraithThornId;
        private int wraithWoodId;
        private static long affectedChunks;
        private static long exposedColumns;
        private static long transformedFlora;
        private static long generatedWraithTrees;
        private static long generatorTicks;

        internal ShatteredHighlandsSurfaceGenerator(
            ICoreServerAPI api)
        {
            this.api = api ??
                throw new ArgumentNullException(nameof(api));
        }

        internal bool Initialized => initialized;

        internal static long AffectedChunks =>
            System.Threading.Interlocked.Read(
                ref affectedChunks
            );

        internal static long ExposedColumns =>
            System.Threading.Interlocked.Read(
                ref exposedColumns
            );

        internal static long TransformedFlora =>
            System.Threading.Interlocked.Read(
                ref transformedFlora
            );

        internal static long GeneratedWraithTrees =>
            System.Threading.Interlocked.Read(
                ref generatedWraithTrees
            );

        internal static double GeneratorMilliseconds =>
            System.Threading.Interlocked.Read(
                ref generatorTicks
            ) * 1000d / Stopwatch.Frequency;

        internal bool Initialize(
            DangerWorldState state,
            out string error)
        {
            Reset();
            if (state == null)
            {
                error = "the persisted realm layout is missing";
                return false;
            }

            basaltId = ResolveBlockId("game:rock-basalt");
            crackedBasaltId = ResolveBlockId(
                "game:crackedrock-basalt"
            );
            basaltGravelId = ResolveBlockId(
                "game:gravel-basalt"
            );
            obsidianId = ResolveBlockId(
                "game:rock-obsidian"
            );
            blackVeinId = ResolveBlockId(
                "apprenticehighlands:blackvein"
            );
            gloomId = ResolveBlockId(
                "apprenticehighlands:gloom"
            );
            ashenWeedId = ResolveBlockId(
                "apprenticehighlands:ashenweed"
            );
            wraithThornId = ResolveBlockId(
                "apprenticehighlands:wraiththorn"
            );
            wraithWoodId = ResolveBlockId(
                "apprenticehighlands:wraithwood"
            );
            if (basaltId <= 0 ||
                crackedBasaltId <= 0 ||
                basaltGravelId <= 0 ||
                obsidianId <= 0 ||
                blackVeinId <= 0 ||
                gloomId <= 0 ||
                ashenWeedId <= 0 ||
                wraithThornId <= 0 ||
                wraithWoodId <= 0)
            {
                error =
                    "one or more realm-wide Highlands corruption blocks did not load";
                ResetResolvedBlocks();
                return false;
            }

            activeState = state;
            initialized = true;
            error = string.Empty;
            return true;
        }

        internal void Reset()
        {
            activeState = null;
            initialized = false;
            loggedChunks = 0;
            ResetResolvedBlocks();
            System.Threading.Interlocked.Exchange(
                ref affectedChunks,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref exposedColumns,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref transformedFlora,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref generatedWraithTrees,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref generatorTicks,
                0
            );
        }

        internal void OnChunkColumnGeneration(
            IChunkColumnGenerateRequest request)
        {
            DangerWorldState? state = activeState;
            if (!initialized ||
                state == null ||
                !WorldZoneLayout.ChunkIntersectsLevel(
                    state,
                    ShatteredHighlandsLevel,
                    request.ChunkX,
                    request.ChunkZ,
                    ChunkSize))
            {
                return;
            }

            long started = Stopwatch.GetTimestamp();
            IMapChunk mapChunk = request.Chunks[0].MapChunk;
            ushort[] heights =
                mapChunk.WorldGenTerrainHeightMap;
            ushort[] rainHeights =
                mapChunk.RainHeightMap;
            if (heights == null ||
                heights.Length < ChunkSize * ChunkSize ||
                rainHeights == null ||
                rainHeights.Length <
                    ChunkSize * ChunkSize)
            {
                return;
            }

            int changedColumns = 0;
            int changedFlora = 0;
            int treeCount = 0;
            for (int localZ = 0;
                localZ < ChunkSize;
                localZ++)
            {
                int row = localZ * ChunkSize;
                int worldZ =
                    request.ChunkZ * ChunkSize + localZ;
                for (int localX = 0;
                    localX < ChunkSize;
                    localX++)
                {
                    int worldX =
                        request.ChunkX * ChunkSize + localX;
                    double realmStrength =
                        GetRealmStrength(
                            state,
                            worldX + 0.5,
                            worldZ + 0.5
                        );
                    if (realmStrength <= 0)
                    {
                        continue;
                    }

                    int mapIndex = row + localX;
                    int terrainY = heights[mapIndex];
                    if (terrainY <= SurfaceDepth ||
                        terrainY >=
                            api.WorldManager.MapSizeY - 34)
                    {
                        continue;
                    }

                    ulong surfaceHash = StableHash(
                        worldX,
                        worldZ,
                        SurfaceSalt
                    );
                    ulong surfacePatchHash = StableHash(
                        FloorDiv(worldX, 8),
                        FloorDiv(worldZ, 8),
                        SurfaceSalt ^
                            0x50415443484C3738UL
                    );
                    if (realmStrength < 0.999 &&
                        surfaceHash % 1000 >=
                            (ulong)(
                                realmStrength * 1000
                            ))
                    {
                        continue;
                    }

                    int maximumSlope = GetMaximumSlope(
                        heights,
                        localX,
                        localZ
                    );
                    bool transformedSurface =
                        TransformSurfaceColumn(
                            request.Chunks,
                            localX,
                            localZ,
                            terrainY,
                            maximumSlope,
                            surfaceHash,
                            surfacePatchHash
                        );
                    if (!transformedSurface)
                    {
                        continue;
                    }
                    changedColumns++;

                    changedFlora +=
                        TransformOrdinaryVegetation(
                            request.Chunks,
                            localX,
                            localZ,
                            terrainY,
                            rainHeights[mapIndex],
                            surfaceHash
                        );

                    int openY = terrainY + 1;
                    if (GetGeneratedBlockId(
                            request.Chunks,
                            localX,
                            openY,
                            localZ) == 0)
                    {
                        PlaceCursedGroundDetail(
                            request.Chunks,
                            localX,
                            openY,
                            localZ,
                            surfaceHash,
                            realmStrength
                        );
                    }

                    if (realmStrength >= 0.82 &&
                        maximumSlope <= 2 &&
                        IsWraithTreeAnchor(
                            localX,
                            localZ,
                            surfaceHash) &&
                        TryBuildWraithTree(
                            request.Chunks,
                            localX,
                            terrainY + 1,
                            localZ,
                            surfaceHash,
                            out int topY))
                    {
                        rainHeights[mapIndex] =
                            (ushort)Math.Max(
                                rainHeights[mapIndex],
                                topY
                            );
                        treeCount++;
                    }
                }
            }

            if (changedColumns <= 0)
            {
                return;
            }

            long elapsed =
                Stopwatch.GetTimestamp() - started;
            System.Threading.Interlocked.Increment(
                ref affectedChunks
            );
            System.Threading.Interlocked.Add(
                ref exposedColumns,
                changedColumns
            );
            System.Threading.Interlocked.Add(
                ref transformedFlora,
                changedFlora
            );
            System.Threading.Interlocked.Add(
                ref generatedWraithTrees,
                treeCount
            );
            System.Threading.Interlocked.Add(
                ref generatorTicks,
                elapsed
            );
            if (System.Threading.Interlocked.Increment(
                    ref loggedChunks
                ) <= 16)
            {
                api.Logger.Notification(
                    "[Apprentice] Cursed Level 7 landscape in chunk {0},{1}: surfaces={2}, flora transformed={3}, wraith trees={4}, generator={5:0.0} ms.",
                    request.ChunkX,
                    request.ChunkZ,
                    changedColumns,
                    changedFlora,
                    treeCount,
                    Stopwatch.GetElapsedTime(started)
                        .TotalMilliseconds
                );
            }
        }

        private bool TransformSurfaceColumn(
            IServerChunk[] chunks,
            int localX,
            int localZ,
            int terrainY,
            int maximumSlope,
            ulong detailHash,
            ulong patchHash)
        {
            int surfaceId = GetGeneratedBlockId(
                chunks,
                localX,
                terrainY,
                localZ
            );
            if (!IsReplaceableNaturalSurface(
                    BlockPath(surfaceId)))
            {
                return false;
            }

            int surfaceReplacement =
                SelectSurfaceBlock(
                    maximumSlope,
                    detailHash,
                    patchHash
                );
            for (int depth = 0;
                depth < SurfaceDepth;
                depth++)
            {
                int y = terrainY - depth;
                int existingId = GetGeneratedBlockId(
                    chunks,
                    localX,
                    y,
                    localZ
                );
                if (!IsReplaceableNaturalSurface(
                        BlockPath(existingId)))
                {
                    break;
                }

                int replacementId = depth == 0
                    ? surfaceReplacement
                    : SelectSubsurfaceBlock(
                        depth,
                        detailHash,
                        patchHash
                    );
                SetGeneratedBlock(
                    chunks,
                    localX,
                    y,
                    localZ,
                    replacementId
                );
            }
            return true;
        }

        private int TransformOrdinaryVegetation(
            IServerChunk[] chunks,
            int localX,
            int localZ,
            int terrainY,
            int rainHeight,
            ulong hash)
        {
            int changed = 0;
            int maximumY = Math.Min(
                api.WorldManager.MapSizeY - 2,
                Math.Max(
                    terrainY + 24,
                    rainHeight + 8
                )
            );
            for (int y = terrainY + 1;
                y <= maximumY;
                y++)
            {
                int blockId = GetGeneratedBlockId(
                    chunks,
                    localX,
                    y,
                    localZ
                );
                if (blockId <= 0)
                {
                    continue;
                }

                string path = BlockPath(blockId);
                if (IsTreeWood(path))
                {
                    SetGeneratedBlock(
                        chunks,
                        localX,
                        y,
                        localZ,
                        wraithWoodId
                    );
                    changed++;
                    continue;
                }
                if (!IsLivingFlora(path))
                {
                    continue;
                }

                SetGeneratedBlock(
                    chunks,
                    localX,
                    y,
                    localZ,
                    0
                );
                changed++;
            }

            if (changed > 0 &&
                (hash >> 32) % 32 == 0 &&
                GetGeneratedBlockId(
                    chunks,
                    localX,
                    terrainY + 1,
                    localZ) == 0)
            {
                SetGeneratedBlock(
                    chunks,
                    localX,
                    terrainY + 1,
                    localZ,
                    ashenWeedId
                );
            }
            return changed;
        }

        private void PlaceCursedGroundDetail(
            IServerChunk[] chunks,
            int localX,
            int y,
            int localZ,
            ulong hash,
            double realmStrength)
        {
            ulong roll = (hash >> 20) % 1000;
            int blockId = 0;
            if (roll < 4 * realmStrength)
            {
                blockId = wraithThornId;
            }
            else if (roll < 19 * realmStrength)
            {
                blockId = ashenWeedId;
            }
            else if (roll < 34 * realmStrength)
            {
                blockId = blackVeinId;
            }
            else if (roll < 36 * realmStrength)
            {
                blockId = gloomId;
            }

            if (blockId > 0)
            {
                SetGeneratedBlock(
                    chunks,
                    localX,
                    y,
                    localZ,
                    blockId
                );
            }
        }

        private bool TryBuildWraithTree(
            IServerChunk[] chunks,
            int rootX,
            int rootY,
            int rootZ,
            ulong hash,
            out int topY)
        {
            topY = rootY;
            int height = 8 + (int)((hash >> 18) % 8);
            for (int y = rootY;
                y <= rootY + height + 3;
                y++)
            {
                if (GetGeneratedBlockId(
                        chunks,
                        rootX,
                        y,
                        rootZ) != 0)
                {
                    return false;
                }
            }

            int directionX =
                ((hash >> 30) & 1) == 0
                    ? -1
                    : 1;
            int directionZ =
                ((hash >> 31) & 1) == 0
                    ? -1
                    : 1;
            int x = rootX;
            int z = rootZ;
            for (int offsetY = 0;
                offsetY < height;
                offsetY++)
            {
                if (offsetY == 4 ||
                    offsetY == 9)
                {
                    x += directionX;
                }
                if (offsetY == 6 ||
                    offsetY == 11)
                {
                    z += directionZ;
                }
                if (!IsInsideChunk(x, z))
                {
                    return false;
                }
                SetGeneratedBlock(
                    chunks,
                    x,
                    rootY + offsetY,
                    z,
                    wraithWoodId
                );

                if (offsetY == 5 ||
                    offsetY == 8 ||
                    offsetY == height - 2)
                {
                    int branchLength =
                        2 +
                        (int)(
                            StableHash(
                                x,
                                z,
                                TreeSalt ^
                                    (ulong)offsetY
                            ) % 4
                        );
                    for (int branch = 1;
                        branch <= branchLength;
                        branch++)
                    {
                        int branchX =
                            x + directionZ * branch;
                        int branchZ =
                            z - directionX * branch;
                        int branchY =
                            rootY +
                            offsetY +
                            branch / 2;
                        if (!IsInsideChunk(
                                branchX,
                                branchZ) ||
                            GetGeneratedBlockId(
                                chunks,
                                branchX,
                                branchY,
                                branchZ) != 0)
                        {
                            break;
                        }
                        SetGeneratedBlock(
                            chunks,
                            branchX,
                            branchY,
                            branchZ,
                            wraithWoodId
                        );
                        topY = Math.Max(
                            topY,
                            branchY
                        );
                    }
                }
                topY = Math.Max(
                    topY,
                    rootY + offsetY
                );
            }

            for (int root = 1;
                root <= 3;
                root++)
            {
                int rootOffsetX =
                    root == 2
                        ? -directionX
                        : directionX;
                int rootOffsetZ =
                    root == 3
                        ? -directionZ
                        : directionZ;
                int targetX =
                    rootX + rootOffsetX * root;
                int targetZ =
                    rootZ + rootOffsetZ * root;
                if (IsInsideChunk(targetX, targetZ))
                {
                    SetGeneratedBlock(
                        chunks,
                        targetX,
                        rootY,
                        targetZ,
                        wraithWoodId
                    );
                }
            }
            return true;
        }

        private int SelectSurfaceBlock(
            int maximumSlope,
            ulong detailHash,
            ulong patchHash)
        {
            int patchRoll = (int)(patchHash % 100);
            int detailRoll =
                (int)((detailHash >> 17) % 100);
            if (maximumSlope >= CliffSlopeThreshold)
            {
                if (patchRoll < 9)
                {
                    return detailRoll < 68
                        ? obsidianId
                        : crackedBasaltId;
                }
                return detailRoll < 24
                    ? crackedBasaltId
                    : basaltId;
            }
            if (patchRoll < 5)
            {
                return detailRoll < 72
                    ? obsidianId
                    : crackedBasaltId;
            }
            if (patchRoll < 24)
            {
                return detailRoll < 78
                    ? crackedBasaltId
                    : basaltId;
            }
            if (patchRoll < 34)
            {
                return detailRoll < 64
                    ? basaltGravelId
                    : basaltId;
            }
            return basaltId;
        }

        private int SelectSubsurfaceBlock(
            int depth,
            ulong detailHash,
            ulong patchHash)
        {
            ulong roll =
                (
                    detailHash ^
                    (patchHash >> (depth * 9))
                ) % 100;
            if (depth == 1 && roll < 16)
            {
                return crackedBasaltId;
            }
            if (roll < 3)
            {
                return obsidianId;
            }
            return basaltId;
        }

        private static int GetMaximumSlope(
            ushort[] heights,
            int localX,
            int localZ)
        {
            int mapIndex =
                localZ * ChunkSize + localX;
            int height = heights[mapIndex];
            int maximumSlope = 0;
            if (localX > 0)
            {
                maximumSlope = Math.Max(
                    maximumSlope,
                    Math.Abs(
                        height -
                        heights[mapIndex - 1]
                    )
                );
            }
            if (localX < ChunkSize - 1)
            {
                maximumSlope = Math.Max(
                    maximumSlope,
                    Math.Abs(
                        height -
                        heights[mapIndex + 1]
                    )
                );
            }
            if (localZ > 0)
            {
                maximumSlope = Math.Max(
                    maximumSlope,
                    Math.Abs(
                        height -
                        heights[mapIndex - ChunkSize]
                    )
                );
            }
            if (localZ < ChunkSize - 1)
            {
                maximumSlope = Math.Max(
                    maximumSlope,
                    Math.Abs(
                        height -
                        heights[mapIndex + ChunkSize]
                    )
                );
            }
            return maximumSlope;
        }

        private static bool IsWraithTreeAnchor(
            int localX,
            int localZ,
            ulong hash) =>
            localX >= 5 &&
            localX <= ChunkSize - 6 &&
            localZ >= 5 &&
            localZ <= ChunkSize - 6 &&
            (hash >> 44) % 1536 == 0;

        private static bool IsInsideChunk(
            int localX,
            int localZ) =>
            localX >= 1 &&
            localX < ChunkSize - 1 &&
            localZ >= 1 &&
            localZ < ChunkSize - 1;

        private static double GetRealmStrength(
            DangerWorldState state,
            double worldX,
            double worldZ)
        {
            double dx = worldX - state.AnchorX;
            double dz = worldZ - state.AnchorZ;
            double distance = Math.Sqrt(
                dx * dx + dz * dz
            );
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
            if (distance < innerRadius ||
                distance >= outerRadius)
            {
                return 0;
            }

            double distanceFromEdge = Math.Min(
                distance - innerRadius,
                outerRadius - distance
            );
            double normalized = Math.Clamp(
                distanceFromEdge /
                    BoundaryTransitionWidth,
                0,
                1
            );
            return normalized * normalized *
                (3 - 2 * normalized);
        }

        private int ResolveBlockId(string code) =>
            api.World.GetBlock(
                new AssetLocation(code)
            )?.Id ?? 0;

        private string BlockPath(int blockId) =>
            blockId > 0 &&
            blockId < api.World.Blocks.Count
                ? api.World.Blocks[blockId]
                    ?.Code?.Path ??
                    string.Empty
                : string.Empty;

        private static bool IsReplaceableNaturalSurface(
            string path) =>
            path.StartsWith(
                "soil-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "gravel-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "sand-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "clay-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "peat-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "rock-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "crackedrock-",
                StringComparison.Ordinal);

        private static bool IsTreeWood(
            string path) =>
            path.StartsWith(
                "log-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "rottenlog",
                StringComparison.Ordinal);

        private static bool IsLivingFlora(
            string path) =>
            path.StartsWith(
                "tallgrass",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "flower",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "plant-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "fern",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "sapling",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "leaves",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "branchy",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "bush",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "mushroom",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "deadcrop",
                StringComparison.Ordinal);

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
            if (chunkY < 0 ||
                chunkY >= chunks.Length)
            {
                return 0;
            }
            IServerChunk? chunk = chunks[chunkY];
            if (chunk == null || chunk.Disposed)
            {
                return 0;
            }
            return chunk.Data.GetBlockIdUnsafe(
                ChunkIndex3d(
                    localX,
                    y % ChunkSize,
                    localZ
                )
            );
        }

        private static void SetGeneratedBlock(
            IServerChunk[] chunks,
            int localX,
            int y,
            int localZ,
            int blockId)
        {
            if (y < 0)
            {
                return;
            }
            int chunkY = y / ChunkSize;
            if (chunkY < 0 ||
                chunkY >= chunks.Length)
            {
                return;
            }
            IServerChunk? chunk = chunks[chunkY];
            if (chunk == null || chunk.Disposed)
            {
                return;
            }
            int index = ChunkIndex3d(
                localX,
                y % ChunkSize,
                localZ
            );
            chunk.Data[index] = blockId;
            chunk.Data.SetFluid(index, 0);
        }

        private static int ChunkIndex3d(
            int x,
            int y,
            int z) =>
            (y * ChunkSize + z) *
                ChunkSize + x;

        private static ulong StableHash(
            int worldX,
            int worldZ,
            ulong salt)
        {
            unchecked
            {
                ulong value =
                    (uint)worldX *
                        0x9E3779B185EBCA87UL ^
                    (uint)worldZ *
                        0xC2B2AE3D27D4EB4FUL ^
                    salt ^
                    FloraSalt;
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value;
            }
        }

        private static int FloorDiv(
            int value,
            int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0
                ? quotient - 1
                : quotient;
        }

        private void ResetResolvedBlocks()
        {
            basaltId = 0;
            crackedBasaltId = 0;
            basaltGravelId = 0;
            obsidianId = 0;
            blackVeinId = 0;
            gloomId = 0;
            ashenWeedId = 0;
            wraithThornId = 0;
            wraithWoodId = 0;
        }
    }
}
