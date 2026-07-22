using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Apprentice
{
    /// <summary>
    /// Uses Vintage Story's normal sword attack and adds a bounded cleanup for
    /// the authoritative hit track, preventing a single click from looping.
    /// </summary>
    public sealed class ItemManaburnSword : Item
    {
        private const float SwordSwingDurationSeconds = 0.7f;

        public override float OnBlockBreaking(
            IPlayer player,
            BlockSelection blockSel,
            ItemSlot itemslot,
            float remainingResistance,
            float dt,
            int counter)
        {
            // Manaburn is a combat weapon, never a mining or harvesting tool.
            // Returning the unchanged resistance prevents any block progress.
            return remainingResistance;
        }

        public override bool OnBlockBrokenWith(
            IWorldAccessor world,
            Entity byEntity,
            ItemSlot itemslot,
            BlockSelection blockSel,
            float dropQuantityMultiplier = 1f)
        {
            // Final server-side guard. Creative mode and a few block paths can
            // bypass the progressive OnBlockBreaking callback; refusing the
            // completion callback keeps Manaburn combat-only in every mode.
            return false;
        }

        public override void OnHeldAttackStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            ref EnumHandHandling handHandling)
        {
            base.OnHeldAttackStart(
                slot,
                byEntity,
                blockSel,
                entitySel,
                ref handHandling
            );

            OneShotMeleeAnimation.ScheduleStop(
                api,
                byEntity,
                HeldTpHitAnimation,
                SwordSwingDurationSeconds
            );
        }
    }
}
