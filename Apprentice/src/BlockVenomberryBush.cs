using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Apprentice
{
    /// <summary>
    /// Compatibility behavior for bushes placed by pre-fruiting-bush builds.
    /// New world generation uses the native game fruiting-bush variant.
    /// Interacting with a legacy bush harvests it and migrates the position to
    /// that native implementation so subsequent growth is engine-owned.
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

            Item? berry = world.GetItem(
                new AssetLocation("apprentice", "venomberry")
            );
            if (berry != null)
            {
                world.SpawnItemEntity(
                    new ItemStack(berry, 4),
                    blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5)
                );
            }

            Block? nativeBush = world.GetBlock(
                new AssetLocation(
                    "game",
                    "fruitingbush-wild-venomberry-free"
                )
            );
            if (nativeBush != null && nativeBush.Id != 0)
            {
                world.BlockAccessor.SetBlock(nativeBush.Id, blockSel.Position);
            }

            return true;
        }
    }
}
