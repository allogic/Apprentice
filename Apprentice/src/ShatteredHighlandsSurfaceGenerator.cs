using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace Apprentice
{
    /// <summary>
    /// Gives the whole Level 7 realm a permanent cursed identity without
    /// changing terrain height, caves or the approved ground routes. Natural
    /// native Vintage Story hot springs and Apprentice cooling-magma lakes
    /// share a deterministic 30/70 feature plan. Apprentice never constructs
    /// hot springs: it only supplies GenHotSprings candidates and protects the
    /// completed native feature from the later Highlands corruption pass.
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
        private const ulong ThermalSalt = 0x544845524D414C37UL;
        private const ulong LiquidTypeSalt = 0x4C41564137303330UL;
        private const int LavaBasinPercent = 70;
        private const int HotSpringBasinPercent = 30;
        private const string HotSpringLocationsKey =
            "hotspringlocations";
        private const int LiquidFeatureCellSize = 256;
        private const int NativeHotSpringRadius = 4;
        private const int NativeHotSpringFootprintRadius =
            NativeHotSpringRadius * 2;
        private const int NativeHotSpringExclusionRadius = 20;
        private const int NativeHotSpringCandidateInset =
            NativeHotSpringFootprintRadius + 2;
        private const int MagmaShoreRadius = 2;
        private const byte NoHeatedLiquid = 0;
        private const byte CoolingMagmaLiquid = 1;

        private readonly ICoreServerAPI api;
        private readonly object liquidGenerationGate = new();
        private DangerWorldState? activeState;
        private bool initialized;
        private bool nativeHotSpringPlannerRegistered;
        private int loggedChunks;
        private int loggedNativeHotSpringPlans;
        private int basaltId;
        private int crackedBasaltId;
        private int basaltGravelId;
        private int obsidianId;
        private int blackVeinId;
        private int gloomId;
        private int ashenWeedId;
        private int wraithThornId;
        private int wraithWoodId;
        private int hotSpringWaterSourceId;
        private int coolingMagmaSourceId;
        private int legacyLavaSourceId;
        private static long affectedChunks;
        private static long exposedColumns;
        private static long transformedFlora;
        private static long generatedWraithTrees;
        private static long scheduledNativeHotSprings;
        private static long filteredNativeHotSprings;
        private static long protectedNativeHotSpringColumns;
        private static long suppressedOrdinaryWaterBlocks;
        private static long convertedMagmaWaterBlocks;
        private static long magmaShoreColumns;
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

        internal static long ScheduledNativeHotSprings =>
            System.Threading.Interlocked.Read(
                ref scheduledNativeHotSprings
            );

        internal static long FilteredNativeHotSprings =>
            System.Threading.Interlocked.Read(
                ref filteredNativeHotSprings
            );

        internal static long ProtectedNativeHotSpringColumns =>
            System.Threading.Interlocked.Read(
                ref protectedNativeHotSpringColumns
            );

        internal static long SuppressedOrdinaryWaterBlocks =>
            System.Threading.Interlocked.Read(
                ref suppressedOrdinaryWaterBlocks
            );

        internal static long ConvertedMagmaWaterBlocks =>
            System.Threading.Interlocked.Read(
                ref convertedMagmaWaterBlocks
            );

        internal static long MagmaShoreColumns =>
            System.Threading.Interlocked.Read(
                ref magmaShoreColumns
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
            if (!HighlandsNativeHotSpringBlocks.TryResolve(
                    api,
                    out HighlandsNativeHotSpringBlocks?
                        nativeHotSpringBlocks,
                    out string nativeHotSpringError) ||
                nativeHotSpringBlocks == null)
            {
                error =
                    "base-game hot-spring blocks did not resolve: " +
                    nativeHotSpringError;
                ResetResolvedBlocks();
                return false;
            }
            hotSpringWaterSourceId =
                nativeHotSpringBlocks.BoilingWater.Id;
            coolingMagmaSourceId = ResolveBlockId(
                "apprenticehighlands:coolingmagma-still-7"
            );
            legacyLavaSourceId = ResolveBlockId(
                "game:lava-still-7"
            );
            if (basaltId <= 0 ||
                crackedBasaltId <= 0 ||
                basaltGravelId <= 0 ||
                obsidianId <= 0 ||
                blackVeinId <= 0 ||
                gloomId <= 0 ||
                ashenWeedId <= 0 ||
                wraithThornId <= 0 ||
                wraithWoodId <= 0 ||
                hotSpringWaterSourceId <= 0 ||
                coolingMagmaSourceId <= 0 ||
                legacyLavaSourceId <= 0)
            {
                error =
                    "one or more realm-wide Highlands or base-game hot-spring blocks did not load";
                ResetResolvedBlocks();
                return false;
            }

            activeState = state;
            initialized = true;
            if (!nativeHotSpringPlannerRegistered)
            {
                api.Event.ChunkColumnGeneration(
                    OnTerrainFeaturesPlanNativeHotSprings,
                    EnumWorldGenPass.TerrainFeatures,
                    "standard"
                );
                nativeHotSpringPlannerRegistered = true;
            }
            error = string.Empty;
            return true;
        }

        internal void Reset()
        {
            activeState = null;
            initialized = false;
            loggedChunks = 0;
            loggedNativeHotSpringPlans = 0;
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
                ref scheduledNativeHotSprings,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref filteredNativeHotSprings,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref protectedNativeHotSpringColumns,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref suppressedOrdinaryWaterBlocks,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref convertedMagmaWaterBlocks,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref magmaShoreColumns,
                0
            );
            System.Threading.Interlocked.Exchange(
                ref generatorTicks,
                0
            );
        }

        private void OnTerrainFeaturesPlanNativeHotSprings(
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

            IMapChunk mapChunk =
                request.Chunks[0].MapChunk;
            Dictionary<Vec3i, HotSpringGenData>?
                locations = mapChunk.GetModdata<
                    Dictionary<Vec3i, HotSpringGenData>>(
                        HotSpringLocationsKey,
                        null
                    );
            bool changed = false;
            int removed = 0;
            if (locations != null &&
                locations.Count > 0)
            {
                List<Vec3i> rejected = new();
                foreach (Vec3i localPosition
                    in locations.Keys)
                {
                    int worldX =
                        request.ChunkX * ChunkSize +
                        localPosition.X;
                    int worldZ =
                        request.ChunkZ * ChunkSize +
                        localPosition.Z;
                    if (!WorldZoneLayout.IsInsideLevelCore(
                            state,
                            ShatteredHighlandsLevel,
                            BoundaryTransitionWidth +
                                NativeHotSpringExclusionRadius,
                            worldX + 0.5,
                            worldZ + 0.5))
                    {
                        continue;
                    }
                    if (IsMagmaFeatureCell(
                            worldX,
                            worldZ) ||
                        ShatteredHighlandsRuinsGenerator
                            .IsWithinPlannedCityFootprint(
                                state,
                                worldX,
                                worldZ,
                                NativeHotSpringExclusionRadius
                            ))
                    {
                        rejected.Add(localPosition);
                    }
                }
                foreach (Vec3i localPosition
                    in rejected)
                {
                    if (locations.Remove(localPosition))
                    {
                        removed++;
                        changed = true;
                    }
                }
            }

            GetLiquidFeatureCellPlan(
                request.ChunkX,
                request.ChunkZ,
                out int cellX,
                out int cellZ,
                out int ownerChunkX,
                out int ownerChunkZ,
                out int preferredLocalX,
                out int preferredLocalZ
            );
            bool ownsNativeSpringCell =
                request.ChunkX == ownerChunkX &&
                request.ChunkZ == ownerChunkZ &&
                !IsMagmaFeatureCellCoordinates(
                    cellX,
                    cellZ
                );
            if (ownsNativeSpringCell)
            {
                int preferredWorldX =
                    request.ChunkX * ChunkSize +
                    preferredLocalX;
                int preferredWorldZ =
                    request.ChunkZ * ChunkSize +
                    preferredLocalZ;
                bool nearExistingCandidate =
                    locations?.Keys.Any(local =>
                    {
                        int dx =
                            local.X - preferredLocalX;
                        int dz =
                            local.Z - preferredLocalZ;
                        return dx * dx + dz * dz <=
                            NativeHotSpringExclusionRadius *
                            NativeHotSpringExclusionRadius;
                    }) == true;
                if (!nearExistingCandidate &&
                    WorldZoneLayout.IsInsideLevelCore(
                        state,
                        ShatteredHighlandsLevel,
                        BoundaryTransitionWidth +
                            NativeHotSpringExclusionRadius,
                        preferredWorldX + 0.5,
                        preferredWorldZ + 0.5) &&
                    !ShatteredHighlandsRuinsGenerator
                        .IsWithinPlannedCityFootprint(
                            state,
                            preferredWorldX,
                            preferredWorldZ,
                            NativeHotSpringExclusionRadius
                        ) &&
                    TryChooseNativeHotSpringSite(
                        request,
                        preferredLocalX,
                        preferredLocalZ,
                        out Vec3i localPosition))
                {
                    locations ??=
                        new Dictionary<
                            Vec3i,
                            HotSpringGenData
                        >();
                    locations[localPosition] =
                        new HotSpringGenData
                        {
                            horRadius =
                                NativeHotSpringRadius,
                            verRadiusSq =
                                NativeHotSpringRadius *
                                NativeHotSpringRadius
                        };
                    changed = true;
                    System.Threading.Interlocked.Increment(
                        ref scheduledNativeHotSprings
                    );
                    if (System.Threading.Interlocked.Increment(
                            ref loggedNativeHotSpringPlans
                        ) <= 12)
                    {
                        api.Logger.Notification(
                            "[Apprentice] Scheduled native Vintage Story Level 7 hot spring at {0},{1}; GenHotSprings owns validation and construction.",
                            request.ChunkX * ChunkSize +
                                localPosition.X,
                            request.ChunkZ * ChunkSize +
                                localPosition.Z
                        );
                    }
                }
            }

            if (removed > 0)
            {
                System.Threading.Interlocked.Add(
                    ref filteredNativeHotSprings,
                    removed
                );
            }
            if (changed && locations != null)
            {
                mapChunk.SetModdata(
                    HotSpringLocationsKey,
                    locations
                );
            }
        }

        private static void GetLiquidFeatureCellPlan(
            int chunkX,
            int chunkZ,
            out int cellX,
            out int cellZ,
            out int ownerChunkX,
            out int ownerChunkZ,
            out int preferredLocalX,
            out int preferredLocalZ)
        {
            int worldCenterX =
                chunkX * ChunkSize + ChunkSize / 2;
            int worldCenterZ =
                chunkZ * ChunkSize + ChunkSize / 2;
            cellX = FloorDiv(
                worldCenterX,
                LiquidFeatureCellSize
            );
            cellZ = FloorDiv(
                worldCenterZ,
                LiquidFeatureCellSize
            );
            ulong hash = StableHash(
                cellX,
                cellZ,
                LiquidTypeSalt ^
                    0x4E41544956454745UL
            );
            int chunksPerCell =
                LiquidFeatureCellSize / ChunkSize;
            int interiorChunkCount =
                chunksPerCell - 2;
            int originChunkX =
                cellX * chunksPerCell;
            int originChunkZ =
                cellZ * chunksPerCell;
            ownerChunkX =
                originChunkX + 1 +
                (int)(hash %
                    (ulong)interiorChunkCount);
            ownerChunkZ =
                originChunkZ + 1 +
                (int)((hash >> 11) %
                    (ulong)interiorChunkCount);
            int localRange =
                ChunkSize -
                NativeHotSpringCandidateInset * 2;
            preferredLocalX =
                NativeHotSpringCandidateInset +
                (int)((hash >> 22) %
                    (ulong)localRange);
            preferredLocalZ =
                NativeHotSpringCandidateInset +
                (int)((hash >> 33) %
                    (ulong)localRange);
        }

        private bool TryChooseNativeHotSpringSite(
            IChunkColumnGenerateRequest request,
            int preferredLocalX,
            int preferredLocalZ,
            out Vec3i localPosition)
        {
            ushort[] heights =
                request.Chunks[0]
                    .MapChunk
                    .WorldGenTerrainHeightMap;
            int bestRelief = int.MaxValue;
            int bestDistance = int.MaxValue;
            int bestX = 0;
            int bestY = 0;
            int bestZ = 0;
            for (int offsetZ = -3;
                offsetZ <= 3;
                offsetZ++)
            {
                for (int offsetX = -3;
                    offsetX <= 3;
                    offsetX++)
                {
                    int localX =
                        preferredLocalX + offsetX;
                    int localZ =
                        preferredLocalZ + offsetZ;
                    if (!TryMeasureNativeHotSpringSite(
                            request.Chunks,
                            heights,
                            localX,
                            localZ,
                            out int surfaceY,
                            out int relief))
                    {
                        continue;
                    }
                    int distance =
                        offsetX * offsetX +
                        offsetZ * offsetZ;
                    if (relief > bestRelief ||
                        (relief == bestRelief &&
                         distance >= bestDistance))
                    {
                        continue;
                    }
                    bestRelief = relief;
                    bestDistance = distance;
                    bestX = localX;
                    bestY = surfaceY;
                    bestZ = localZ;
                }
            }

            if (bestRelief == int.MaxValue)
            {
                localPosition = new Vec3i(0, 0, 0);
                return false;
            }
            localPosition =
                new Vec3i(bestX, bestY, bestZ);
            return true;
        }

        private bool TryMeasureNativeHotSpringSite(
            IServerChunk[] chunks,
            ushort[] heights,
            int centerX,
            int centerZ,
            out int surfaceY,
            out int relief)
        {
            surfaceY = 0;
            relief = int.MaxValue;
            if (centerX <
                    NativeHotSpringCandidateInset ||
                centerX >=
                    ChunkSize -
                    NativeHotSpringCandidateInset ||
                centerZ <
                    NativeHotSpringCandidateInset ||
                centerZ >=
                    ChunkSize -
                    NativeHotSpringCandidateInset)
            {
                return false;
            }

            int minimumY = int.MaxValue;
            int maximumY = int.MinValue;
            long totalY = 0;
            int samples = 0;
            int radiusSquared =
                NativeHotSpringFootprintRadius *
                NativeHotSpringFootprintRadius;
            for (int dz =
                    -NativeHotSpringFootprintRadius;
                dz <=
                    NativeHotSpringFootprintRadius;
                dz++)
            {
                for (int dx =
                        -NativeHotSpringFootprintRadius;
                    dx <=
                        NativeHotSpringFootprintRadius;
                    dx++)
                {
                    if (dx * dx + dz * dz >
                        radiusSquared)
                    {
                        continue;
                    }
                    int localX = centerX + dx;
                    int localZ = centerZ + dz;
                    int terrainY = heights[
                        localZ * ChunkSize + localX
                    ];
                    int fluidId = GetGeneratedFluidId(
                        chunks,
                        localX,
                        terrainY + 1,
                        localZ
                    );
                    if (fluidId > 0)
                    {
                        return false;
                    }
                    minimumY = Math.Min(
                        minimumY,
                        terrainY
                    );
                    maximumY = Math.Max(
                        maximumY,
                        terrainY
                    );
                    totalY += terrainY;
                    samples++;
                }
            }
            if (samples == 0 ||
                maximumY - minimumY >= 4 ||
                minimumY <
                    api.World.SeaLevel + 2 ||
                maximumY >=
                    api.WorldManager.MapSizeY *
                    0.88f)
            {
                return false;
            }
            surfaceY =
                (int)Math.Round(
                    totalY / (double)samples
                );
            relief = maximumY - minimumY;
            return true;
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
            int suppressedWaterBlocks = 0;
            int changedMagmaWaterBlocks = 0;
            int protectedSpringColumns = 0;
            int changedMagmaShoreColumns = 0;
            bool[] nativeHotSpringReserved =
                new bool[ChunkSize * ChunkSize];
            byte[] heatedLiquidKinds =
                new byte[ChunkSize * ChunkSize];
            IWorldGenBlockAccessor? blockAccessor =
                ShatteredHighlandsRuinsGenerator
                    .SharedWorldgenBlockAccessor;
            if (blockAccessor == null)
            {
                return;
            }
            lock (liquidGenerationGate)
            {
                blockAccessor.BeginColumn();
                BuildNativeHotSpringReservation(
                    request,
                    heights,
                    blockAccessor,
                    nativeHotSpringReserved
                );
                ConvertNaturalWaterBasins(
                    request,
                    state,
                    heights,
                    rainHeights,
                    nativeHotSpringReserved,
                    heatedLiquidKinds,
                    out suppressedWaterBlocks,
                    out changedMagmaWaterBlocks
                );
            }

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
                    if (nativeHotSpringReserved[mapIndex])
                    {
                        protectedSpringColumns++;
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
                    byte nearbyHeatedLiquid =
                        GetNearbyHeatedLiquidKind(
                            heatedLiquidKinds,
                            localX,
                            localZ,
                            worldX,
                            terrainY,
                            worldZ,
                            blockAccessor,
                            out _
                        );
                    bool heatedLiquidInfluence =
                        nearbyHeatedLiquid !=
                            NoHeatedLiquid;
                    if (!heatedLiquidInfluence &&
                        realmStrength < 0.999 &&
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
                    changedFlora +=
                        TransformOrdinaryVegetation(
                            request.Chunks,
                            localX,
                            localZ,
                            terrainY,
                            rainHeights[mapIndex],
                            surfaceHash
                        );
                    if (transformedSurface)
                    {
                        changedColumns++;

                        if (nearbyHeatedLiquid ==
                            CoolingMagmaLiquid)
                        {
                            ulong magmaShoreHash =
                                StableHash(
                                    FloorDiv(worldX, 4),
                                    FloorDiv(worldZ, 4),
                                    ThermalSalt ^
                                        0x4D41474D41534852UL
                                );
                            SetGeneratedBlock(
                                request.Chunks,
                                localX,
                                terrainY,
                                localZ,
                                magmaShoreHash % 7 == 0
                                    ? obsidianId
                                    : magmaShoreHash % 5 == 0
                                        ? crackedBasaltId
                                        : basaltId
                            );
                            changedMagmaShoreColumns++;
                        }

                        int openY = terrainY + 1;
                        if (!heatedLiquidInfluence &&
                            GetGeneratedBlockId(
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

                        if (!heatedLiquidInfluence &&
                            realmStrength >= 0.82 &&
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
            }

            if (changedColumns <= 0 &&
                suppressedWaterBlocks <= 0 &&
                changedMagmaWaterBlocks <= 0 &&
                protectedSpringColumns <= 0)
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
                ref protectedNativeHotSpringColumns,
                protectedSpringColumns
            );
            System.Threading.Interlocked.Add(
                ref suppressedOrdinaryWaterBlocks,
                suppressedWaterBlocks
            );
            System.Threading.Interlocked.Add(
                ref convertedMagmaWaterBlocks,
                changedMagmaWaterBlocks
            );
            System.Threading.Interlocked.Add(
                ref magmaShoreColumns,
                changedMagmaShoreColumns
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
                    "[Apprentice] Cursed Level 7 landscape in chunk {0},{1}: surfaces={2}, flora transformed={3}, wraith trees={4}, cooling magma={5}, ordinary water removed={6}, native spring columns protected={7}, scorched magma shore={8}, generator={9:0.0} ms.",
                    request.ChunkX,
                    request.ChunkZ,
                    changedColumns,
                    changedFlora,
                    treeCount,
                    changedMagmaWaterBlocks,
                    suppressedWaterBlocks,
                    protectedSpringColumns,
                    changedMagmaShoreColumns,
                    Stopwatch.GetElapsedTime(started)
                        .TotalMilliseconds
                );
            }
        }

        private void BuildNativeHotSpringReservation(
            IChunkColumnGenerateRequest request,
            ushort[] heights,
            IWorldGenBlockAccessor blockAccessor,
            bool[] reserved)
        {
            int chunkOriginX =
                request.ChunkX * ChunkSize;
            int chunkOriginZ =
                request.ChunkZ * ChunkSize;
            HashSet<(int X, int Z)> hotWaterColumns =
                new();
            BlockPos sample = new(0);
            for (int dz =
                    -NativeHotSpringExclusionRadius;
                dz <
                    ChunkSize +
                    NativeHotSpringExclusionRadius;
                dz++)
            {
                int worldZ = chunkOriginZ + dz;
                for (int dx =
                        -NativeHotSpringExclusionRadius;
                    dx <
                        ChunkSize +
                        NativeHotSpringExclusionRadius;
                    dx++)
                {
                    int worldX = chunkOriginX + dx;
                    if (!TryGetWorldgenTerrainHeight(
                            blockAccessor,
                            worldX,
                            worldZ,
                            out int terrainY))
                    {
                        continue;
                    }
                    for (int y = Math.Max(
                            1,
                            terrainY - 1);
                        y <= Math.Min(
                            api.WorldManager.MapSizeY - 2,
                            terrainY + 1);
                        y++)
                    {
                        sample.Set(worldX, y, worldZ);
                        Block fluid =
                            blockAccessor.GetBlock(
                                sample,
                                BlockLayersAccess.Fluid
                            );
                        if (fluid.Id !=
                            hotSpringWaterSourceId)
                        {
                            continue;
                        }
                        hotWaterColumns.Add(
                            (worldX, worldZ)
                        );
                        break;
                    }
                }
            }

            int radiusSquared =
                NativeHotSpringExclusionRadius *
                NativeHotSpringExclusionRadius;
            foreach ((int hotX, int hotZ)
                in hotWaterColumns)
            {
                int minimumLocalX = Math.Max(
                    0,
                    hotX -
                        NativeHotSpringExclusionRadius -
                        chunkOriginX
                );
                int maximumLocalX = Math.Min(
                    ChunkSize - 1,
                    hotX +
                        NativeHotSpringExclusionRadius -
                        chunkOriginX
                );
                int minimumLocalZ = Math.Max(
                    0,
                    hotZ -
                        NativeHotSpringExclusionRadius -
                        chunkOriginZ
                );
                int maximumLocalZ = Math.Min(
                    ChunkSize - 1,
                    hotZ +
                        NativeHotSpringExclusionRadius -
                        chunkOriginZ
                );
                for (int localZ = minimumLocalZ;
                    localZ <= maximumLocalZ;
                    localZ++)
                {
                    int worldZ =
                        chunkOriginZ + localZ;
                    for (int localX = minimumLocalX;
                        localX <= maximumLocalX;
                        localX++)
                    {
                        int worldX =
                            chunkOriginX + localX;
                        int distanceX =
                            worldX - hotX;
                        int distanceZ =
                            worldZ - hotZ;
                        if (distanceX * distanceX +
                                distanceZ * distanceZ <=
                            radiusSquared)
                        {
                            reserved[
                                localZ * ChunkSize +
                                localX
                            ] = true;
                        }
                    }
                }
            }

            // A native spring updates the map height itself. Keep the current
            // chunk's exact spring columns reserved even if a neighbouring map
            // chunk was unavailable during the safety-margin scan.
            for (int localZ = 0;
                localZ < ChunkSize;
                localZ++)
            {
                for (int localX = 0;
                    localX < ChunkSize;
                    localX++)
                {
                    int index =
                        localZ * ChunkSize + localX;
                    int terrainY = heights[index];
                    sample.Set(
                        chunkOriginX + localX,
                        terrainY,
                        chunkOriginZ + localZ
                    );
                    Block fluid =
                        blockAccessor.GetBlock(
                            sample,
                            BlockLayersAccess.Fluid
                        );
                    if (fluid.Id ==
                        hotSpringWaterSourceId)
                    {
                        reserved[index] = true;
                    }
                }
            }
        }

        private static bool TryGetWorldgenTerrainHeight(
            IWorldGenBlockAccessor blockAccessor,
            int worldX,
            int worldZ,
            out int terrainY)
        {
            int chunkX =
                FloorDiv(worldX, ChunkSize);
            int chunkZ =
                FloorDiv(worldZ, ChunkSize);
            IMapChunk? mapChunk =
                blockAccessor.GetMapChunk(
                    chunkX,
                    chunkZ
                );
            ushort[]? heights =
                mapChunk?.WorldGenTerrainHeightMap;
            if (heights == null ||
                heights.Length <
                    ChunkSize * ChunkSize)
            {
                terrainY = 0;
                return false;
            }
            int localX =
                worldX - chunkX * ChunkSize;
            int localZ =
                worldZ - chunkZ * ChunkSize;
            terrainY = heights[
                localZ * ChunkSize + localX
            ];
            return true;
        }

        private void ConvertNaturalWaterBasins(
            IChunkColumnGenerateRequest request,
            DangerWorldState state,
            ushort[] heights,
            ushort[] rainHeights,
            bool[] nativeHotSpringReserved,
            byte[] heatedLiquidKinds,
            out int suppressedWaterBlocks,
            out int changedMagmaWaterBlocks)
        {
            int columnCount = ChunkSize * ChunkSize;
            int[] minimumWaterY = new int[columnCount];
            int[] maximumWaterY = new int[columnCount];
            bool[] visited = new bool[columnCount];
            Array.Fill(minimumWaterY, -1);
            suppressedWaterBlocks = 0;
            changedMagmaWaterBlocks = 0;

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
                    int mapIndex = row + localX;
                    int terrainY = heights[mapIndex];
                    int worldX =
                        request.ChunkX * ChunkSize +
                        localX;
                    if (terrainY <= SurfaceDepth ||
                        GetRealmStrength(
                            state,
                            worldX + 0.5,
                            worldZ + 0.5
                        ) <= 0 ||
                        !TryFindNaturalWaterRange(
                            request.Chunks,
                            localX,
                            localZ,
                            terrainY,
                            rainHeights[mapIndex],
                            out int minimumY,
                            out int maximumY))
                    {
                        continue;
                    }

                    minimumWaterY[mapIndex] = minimumY;
                    maximumWaterY[mapIndex] = maximumY;
                }
            }

            int[] queue = new int[columnCount];
            List<int> component =
                new(columnCount);
            for (int startIndex = 0;
                startIndex < columnCount;
                startIndex++)
            {
                if (visited[startIndex] ||
                    minimumWaterY[startIndex] < 0)
                {
                    continue;
                }

                component.Clear();
                int queueRead = 0;
                int queueWrite = 0;
                queue[queueWrite++] = startIndex;
                visited[startIndex] = true;
                int canonicalWorldX =
                    request.ChunkX * ChunkSize +
                    startIndex % ChunkSize;
                int canonicalWorldZ =
                    request.ChunkZ * ChunkSize +
                    startIndex / ChunkSize;
                while (queueRead < queueWrite)
                {
                    int current = queue[queueRead++];
                    component.Add(current);
                    int currentX = current % ChunkSize;
                    int currentZ = current / ChunkSize;
                    int worldX =
                        request.ChunkX * ChunkSize +
                        currentX;
                    int worldZ =
                        request.ChunkZ * ChunkSize +
                        currentZ;
                    if (worldX < canonicalWorldX ||
                        (worldX == canonicalWorldX &&
                         worldZ < canonicalWorldZ))
                    {
                        canonicalWorldX = worldX;
                        canonicalWorldZ = worldZ;
                    }

                    TryQueueConnectedWaterColumn(
                        currentX - 1,
                        currentZ,
                        current,
                        minimumWaterY,
                        maximumWaterY,
                        visited,
                        queue,
                        ref queueWrite
                    );
                    TryQueueConnectedWaterColumn(
                        currentX + 1,
                        currentZ,
                        current,
                        minimumWaterY,
                        maximumWaterY,
                        visited,
                        queue,
                        ref queueWrite
                    );
                    TryQueueConnectedWaterColumn(
                        currentX,
                        currentZ - 1,
                        current,
                        minimumWaterY,
                        maximumWaterY,
                        visited,
                        queue,
                        ref queueWrite
                    );
                    TryQueueConnectedWaterColumn(
                        currentX,
                        currentZ + 1,
                        current,
                        minimumWaterY,
                        maximumWaterY,
                        visited,
                        queue,
                        ref queueWrite
                    );
                }

                bool suppressForNativeSpring =
                    !IsMagmaFeatureCell(
                        canonicalWorldX,
                        canonicalWorldZ
                    );
                if (!suppressForNativeSpring)
                {
                    foreach (int mapIndex in component)
                    {
                        if (nativeHotSpringReserved[
                                mapIndex
                            ])
                        {
                            suppressForNativeSpring =
                                true;
                            break;
                        }
                    }
                }
                int replacementFluidId =
                    suppressForNativeSpring
                        ? 0
                        : coolingMagmaSourceId;
                foreach (int mapIndex in component)
                {
                    int localX = mapIndex % ChunkSize;
                    int localZ = mapIndex / ChunkSize;
                    int converted =
                        ConvertNaturalWaterColumn(
                            request.Chunks,
                            localX,
                            localZ,
                            minimumWaterY[mapIndex],
                            maximumWaterY[mapIndex],
                            replacementFluidId
                        );
                    if (converted <= 0)
                    {
                        continue;
                    }
                    if (suppressForNativeSpring)
                    {
                        suppressedWaterBlocks +=
                            converted;
                    }
                    else
                    {
                        heatedLiquidKinds[mapIndex] =
                            CoolingMagmaLiquid;
                        changedMagmaWaterBlocks +=
                            converted;
                    }
                }
            }
        }

        private bool TryFindNaturalWaterRange(
            IServerChunk[] chunks,
            int localX,
            int localZ,
            int terrainY,
            int rainHeight,
            out int minimumY,
            out int maximumY)
        {
            minimumY = -1;
            maximumY = -1;
            int scanMaximumY = Math.Min(
                api.WorldManager.MapSizeY - 2,
                Math.Max(terrainY + 1, rainHeight + 1)
            );
            int scanMinimumY = Math.Max(
                0,
                terrainY - 3
            );
            for (int y = scanMaximumY;
                y >= scanMinimumY;
                y--)
            {
                int fluidId = GetGeneratedFluidId(
                    chunks,
                    localX,
                    y,
                    localZ
                );
                bool ordinaryWater =
                    fluidId > 0 &&
                    IsOrdinaryWaterFluid(
                        BlockPath(fluidId)
                    );
                if (!ordinaryWater)
                {
                    if (maximumY >= 0)
                    {
                        break;
                    }
                    continue;
                }
                maximumY = Math.Max(maximumY, y);
                minimumY = y;
            }
            return minimumY >= 0;
        }

        private static void TryQueueConnectedWaterColumn(
            int localX,
            int localZ,
            int sourceIndex,
            int[] minimumWaterY,
            int[] maximumWaterY,
            bool[] visited,
            int[] queue,
            ref int queueWrite)
        {
            if (!IsInsideChunkBounds(localX, localZ))
            {
                return;
            }
            int targetIndex =
                localZ * ChunkSize + localX;
            if (visited[targetIndex] ||
                minimumWaterY[targetIndex] < 0 ||
                Math.Max(
                    minimumWaterY[sourceIndex],
                    minimumWaterY[targetIndex]
                ) >
                Math.Min(
                    maximumWaterY[sourceIndex],
                    maximumWaterY[targetIndex]
                ))
            {
                return;
            }
            visited[targetIndex] = true;
            queue[queueWrite++] = targetIndex;
        }

        private int ConvertNaturalWaterColumn(
            IServerChunk[] chunks,
            int localX,
            int localZ,
            int minimumY,
            int maximumY,
            int replacementFluidId)
        {
            int converted = 0;
            for (int y = minimumY;
                y <= maximumY;
                y++)
            {
                int fluidId = GetGeneratedFluidId(
                    chunks,
                    localX,
                    y,
                    localZ
                );
                if (fluidId <= 0 ||
                    !IsOrdinaryWaterFluid(
                        BlockPath(fluidId)))
                {
                    continue;
                }
                SetGeneratedFluid(
                    chunks,
                    localX,
                    y,
                    localZ,
                    replacementFluidId
                );
                converted++;
            }
            return converted;
        }

        private byte GetNearbyHeatedLiquidKind(
            byte[] heatedLiquidKinds,
            int localX,
            int localZ,
            int worldX,
            int terrainY,
            int worldZ,
            IWorldGenBlockAccessor? blockAccessor,
            out int selectedDistanceSquared)
        {
            int nearestMagma = int.MaxValue;
            int magmaRadiusSquared =
                MagmaShoreRadius *
                MagmaShoreRadius;
            for (int dz = -MagmaShoreRadius;
                dz <= MagmaShoreRadius;
                dz++)
            {
                for (int dx = -MagmaShoreRadius;
                    dx <= MagmaShoreRadius;
                    dx++)
                {
                    int distanceSquared =
                        dx * dx + dz * dz;
                    if (distanceSquared >
                        magmaRadiusSquared)
                    {
                        continue;
                    }

                    int sampleLocalX = localX + dx;
                    int sampleLocalZ = localZ + dz;
                    byte kind;
                    if (IsInsideChunkBounds(
                            sampleLocalX,
                            sampleLocalZ))
                    {
                        kind = heatedLiquidKinds[
                            sampleLocalZ * ChunkSize +
                            sampleLocalX
                        ];
                    }
                    else if (blockAccessor != null)
                    {
                        kind = GetWorldgenHeatedLiquidKind(
                            blockAccessor,
                            worldX + dx,
                            terrainY,
                            worldZ + dz
                        );
                    }
                    else
                    {
                        kind = NoHeatedLiquid;
                    }

                    if (kind == CoolingMagmaLiquid)
                    {
                        nearestMagma = Math.Min(
                            nearestMagma,
                            distanceSquared
                        );
                    }
                }
            }

            if (nearestMagma != int.MaxValue)
            {
                selectedDistanceSquared =
                    nearestMagma;
                return CoolingMagmaLiquid;
            }
            selectedDistanceSquared = int.MaxValue;
            return NoHeatedLiquid;
        }

        private byte GetWorldgenHeatedLiquidKind(
            IWorldGenBlockAccessor blockAccessor,
            int worldX,
            int terrainY,
            int worldZ)
        {
            BlockPos sample = new(worldX, 0, worldZ);
            int minimumY = Math.Max(
                0,
                terrainY - 4
            );
            int maximumY = Math.Min(
                api.WorldManager.MapSizeY - 1,
                terrainY + 4
            );
            for (int y = minimumY;
                y <= maximumY;
                y++)
            {
                sample.Y = y;
                Block fluid = blockAccessor.GetBlock(
                    sample,
                    BlockLayersAccess.Fluid
                );
                if (fluid.Id == coolingMagmaSourceId ||
                    fluid.Id == legacyLavaSourceId)
                {
                    return CoolingMagmaLiquid;
                }
            }
            return NoHeatedLiquid;
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
                    terrainY + 48,
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

                int fluidId = GetGeneratedFluidId(
                    chunks,
                    localX,
                    y,
                    localZ
                );
                if (fluidId == hotSpringWaterSourceId)
                {
                    continue;
                }
                int restoredHeatedFluidId =
                    fluidId == coolingMagmaSourceId ||
                    fluidId == legacyLavaSourceId
                        ? fluidId
                        : 0;
                SetGeneratedBlock(
                    chunks,
                    localX,
                    y,
                    localZ,
                    0
                );
                if (restoredHeatedFluidId > 0)
                {
                    SetGeneratedFluid(
                        chunks,
                        localX,
                        y,
                        localZ,
                        restoredHeatedFluidId
                    );
                }
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

        private static bool IsInsideChunkBounds(
            int localX,
            int localZ) =>
            localX >= 0 &&
            localX < ChunkSize &&
            localZ >= 0 &&
            localZ < ChunkSize;

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

        private static bool IsOrdinaryWaterFluid(
            string path) =>
            path.StartsWith(
                "water-",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "saltwater-",
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
                "shortgrass",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "grass-",
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
                "berrybush",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "vine",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "moss",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "lichen",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "bamboo",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "cattail",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "reed",
                StringComparison.Ordinal) ||
            path.StartsWith(
                "waterlily",
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
            return chunk.Data.GetFluid(
                ChunkIndex3d(
                    localX,
                    y % ChunkSize,
                    localZ
                )
            );
        }

        private static void SetGeneratedFluid(
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
            chunk.Data.SetFluid(
                ChunkIndex3d(
                    localX,
                    y % ChunkSize,
                    localZ
                ),
                blockId
            );
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

        private static bool IsMagmaFeatureCell(
            int worldX,
            int worldZ) =>
            IsMagmaFeatureCellCoordinates(
                FloorDiv(
                    worldX,
                    LiquidFeatureCellSize
                ),
                FloorDiv(
                    worldZ,
                    LiquidFeatureCellSize
                )
            );

        private static bool IsMagmaFeatureCellCoordinates(
            int cellX,
            int cellZ)
        {
            ulong typeHash = StableHash(
                cellX,
                cellZ,
                LiquidTypeSalt
            );
            return typeHash % (ulong)(
                LavaBasinPercent +
                HotSpringBasinPercent
            ) < (ulong)LavaBasinPercent;
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
            hotSpringWaterSourceId = 0;
            coolingMagmaSourceId = 0;
            legacyLavaSourceId = 0;
        }
    }
}
