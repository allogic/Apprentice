using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
    /// <summary>
    /// Native completed block-event adapter.
    ///
    /// Classification is exclusive:
    ///
    /// - seed/sapling/crop placement -> Plant;
    /// - other placement -> PlaceBlock;
    /// - mature crop break -> Harvest;
    /// - other block break -> DestroyBlock.
    ///
    /// This prevents one physical action from receiving both the generic
    /// and specialized reward.
    /// </summary>
    internal sealed class BlockInteractionAdapter
        : IInteractionEventAdapter
    {
        private readonly ICoreServerAPI serverApi;
        private readonly ExperienceManager experienceManager;
        private bool disposed;

        public BlockInteractionAdapter(
            ICoreServerAPI serverApi,
            ExperienceManager experienceManager)
        {
            this.serverApi = serverApi
                ?? throw new ArgumentNullException(
                    nameof(serverApi)
                );

            this.experienceManager = experienceManager
                ?? throw new ArgumentNullException(
                    nameof(experienceManager)
                );

            serverApi.Event.DidBreakBlock +=
                OnDidBreakBlock;

            serverApi.Event.DidPlaceBlock +=
                OnDidPlaceBlock;
        }

        private void OnDidBreakBlock(
            IServerPlayer player,
            int oldBlockId,
            BlockSelection blockSelection)
        {
            try
            {
                if (player == null)
                {
                    return;
                }

                if (InteractionEventSuppression
                    .ConsumeSuppressedBlockBreak(
                        player.PlayerUID,
                        blockSelection?.Position
                    ))
                {
                    return;
                }

                Block block =
                    serverApi.World.GetBlock(oldBlockId);

                if (block?.Code == null)
                {
                    return;
                }

                bool isHarvest =
                    NativeInteractionClassifier
                        .IsMatureCrop(block) ||
                    NativeInteractionClassifier
                        .IsSpecialHarvestBlock(block);

                string interaction =
                    isHarvest
                        ? InteractionNames.Harvest
                        : InteractionNames.DestroyBlock;

                experienceManager.HandleInteraction(
                    new InteractionContext(
                        player,
                        interaction,
                        block.Code,
                        position:
                            blockSelection?.Position
                    )
                );
            }
            catch (Exception exception)
            {
                serverApi.Logger.Error(
                    "[Apprentice] Failed to process a completed " +
                    "block-break interaction."
                );

                serverApi.Logger.Error(exception);
            }
        }

        private void OnDidPlaceBlock(
            IServerPlayer player,
            int oldBlockId,
            BlockSelection blockSelection,
            ItemStack withItemStack)
        {
            try
            {
                if (player == null ||
                    blockSelection?.Position == null)
                {
                    return;
                }

                Block placedBlock =
                    serverApi.World.BlockAccessor.GetBlock(
                        blockSelection.Position
                    );

                if (placedBlock?.Code == null)
                {
                    return;
                }

                bool isPlanting =
                    NativeInteractionClassifier
                        .IsPlantingPlacement(
                            withItemStack,
                            placedBlock
                        );

                string interaction =
                    isPlanting
                        ? InteractionNames.Plant
                        : InteractionNames.PlaceBlock;

                AssetLocation? targetCode =
                    isPlanting
                        ? NativeInteractionClassifier
                            .ResolvePlantTarget(
                                withItemStack,
                                placedBlock
                            )
                        : placedBlock.Code;

                if (targetCode == null)
                {
                    return;
                }

                experienceManager.HandleInteraction(
                    new InteractionContext(
                        player,
                        interaction,
                        targetCode,
                        position:
                            blockSelection.Position
                    )
                );
            }
            catch (Exception exception)
            {
                serverApi.Logger.Error(
                    "[Apprentice] Failed to process a completed " +
                    "block-placement interaction."
                );

                serverApi.Logger.Error(exception);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            serverApi.Event.DidBreakBlock -=
                OnDidBreakBlock;

            serverApi.Event.DidPlaceBlock -=
                OnDidPlaceBlock;
        }
    }
}
