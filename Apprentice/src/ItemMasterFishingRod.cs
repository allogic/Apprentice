using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Apprentice
{
    public sealed class ItemMasterFishingRod : ItemFishingPole
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection? entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            ItemStack? stack = slot?.Itemstack;
            bool hasFishingState = stack != null &&
                (stack.Attributes.GetBool("fishing") ||
                 stack.Attributes.GetLong("fishingEntityId", 0L) != 0L ||
                 stack.Attributes.GetLong("bobberEntityId", 0L) != 0L);

            if (!hasFishingState)
            {
                base.OnHeldInteractStart(
                    slot,
                    byEntity,
                    blockSel,
                    entitySel,
                    firstEvent,
                    ref handling
                );
                return;
            }

            bool stopped = StopFishing(stack, byEntity);
            if (!stopped)
            {
                int clothId = stack!.Attributes.GetInt("clothId");
                if (clothId != 0)
                {
                    cm.UnregisterCloth(clothId);
                }

                stack.Attributes.SetBool("fishing", false);
                stack.Attributes.RemoveAttribute("bobberEntityId");
                stack.Attributes.RemoveAttribute("clothId");
                stack.Attributes.RemoveAttribute("fishingEntityId");
            }

            StopFishingAnimations(byEntity);
            slot.MarkDirty();
            handling = EnumHandHandling.PreventDefault;
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            base.OnHeldIdle(slot, byEntity);

            ItemStack? stack = slot?.Itemstack;
            if (stack == null ||
                (!stack.Attributes.GetBool("fishing") &&
                 stack.Attributes.GetLong("fishingEntityId", 0L) == 0L &&
                 stack.Attributes.GetLong("bobberEntityId", 0L) == 0L))
            {
                byEntity.AnimManager.StopAnimation("fishingpole-idle");
            }
        }

        public override void OnCollected(ItemStack stack, Entity entity)
        {
            base.OnCollected(stack, entity);
            if (entity is EntityAgent agent)
            {
                StopFishingAnimations(agent);
            }
        }

        public override void OnHeldDropped(
            IWorldAccessor world,
            IPlayer byPlayer,
            ItemSlot slot,
            int quantity,
            ref EnumHandling handling)
        {
            base.OnHeldDropped(
                world,
                byPlayer,
                slot,
                quantity,
                ref handling
            );
            StopFishingAnimations(byPlayer.Entity);
        }

        private void StopFishingAnimations(EntityAgent entity)
        {
            entity.AnimManager.StopAnimation(aimAnimation);
            entity.AnimManager.StopAnimation("fishingpole-idle");
        }
    }
}
