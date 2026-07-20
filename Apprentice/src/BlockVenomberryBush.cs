using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Apprentice
{
    /// <summary>
    /// Stable Apprentice-owned venomberry bush behavior. Keeping the plant in
    /// this asset domain avoids fragile patches into vanilla fruiting-bush
    /// variant and runtime texture tables.
    /// </summary>
    public sealed class BlockVenomberryBush : Block
    {
        public override string GetPlacedBlockInfo(
            IWorldAccessor world,
            BlockPos pos,
            IPlayer forPlayer)
        {
            return base.GetPlacedBlockInfo(world, pos, forPlayer) +
                "\nHealth state: Healthy\nGrowth state: Mature";
        }

        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (world.Side != EnumAppSide.Server) return true;

            Block current = world.BlockAccessor.GetBlock(
                blockSel.Position
            );
            Item? berry = world.GetItem(
                new AssetLocation("apprentice", "venomberry")
            );
            Block? regrowingBush = world.GetBlock(
                new AssetLocation(
                    "apprentice",
                    "venomberrycutting-free"
                )
            );

            if (current.Id != Id ||
                berry == null ||
                regrowingBush == null)
            {
                return false;
            }

            // Consume the ripe state before granting any output. Server
            // interactions are sequential, so a repeated/stale click now
            // observes the cutting instead of this harvestable block. The
            // existing FruitingBushCutting behavior matures it again after
            // its configured two-to-four-month regrowth period.
            world.BlockAccessor.SetBlock(
                regrowingBush.Id,
                blockSel.Position
            );

            if (world.BlockAccessor.GetBlock(blockSel.Position).Id !=
                regrowingBush.Id)
            {
                return false;
            }

            ItemStack harvest = new(berry, 4);
            bool completelyGiven =
                byPlayer.InventoryManager.TryGiveItemstack(
                    harvest,
                    slotNotifyEffect: true
                );

            if (!completelyGiven && harvest.StackSize > 0)
            {
                world.SpawnItemEntity(
                    harvest,
                    blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5)
                );
            }

            return true;
        }
    }
}
