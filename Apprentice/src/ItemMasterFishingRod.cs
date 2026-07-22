using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Apprentice
{
    public sealed class ItemMasterFishingRod : ItemFishingPole
    {
        // Centre of the modelled tip eye: y = 35.45634560676611 / 16.
        // ItemFishingPole overwrites JSON pole-tip attributes with the vanilla
        // pole's +X offsets, so the custom +Y model must restore its own value
        // after base.OnLoaded().
        private const float ModelTipY = 2.216021f;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            offsetToPoleTipFp = new Vec3f(0f, ModelTipY, 0f);
            offsetToPoleTipTp = new Vec3f(0f, ModelTipY, 0f);
        }

        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection? entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            bool wasFishing = slot.Itemstack?.Attributes.GetBool("fishing") == true;
            long previousBobberId = slot.Itemstack?.Attributes
                .GetLong("bobberEntityId", 0L) ?? 0L;

            base.OnHeldInteractStart(
                slot,
                byEntity,
                blockSel,
                entitySel,
                firstEvent,
                ref handling);

            // Native ItemFishingPole normally clears these values itself. If
            // its bobber lookup misses or the client/server state is stale,
            // however, the hook and fishing state can survive collection and
            // permanently block the next cast. A click made while a cast was
            // active always means "collect", so finish that old session fully.
            if (wasFishing)
            {
                FinishCollectedSession(slot, byEntity, previousBobberId);
                handling = EnumHandHandling.PreventDefault;
            }
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            base.OnHeldIdle(slot, byEntity);

            // The client can receive the item state one packet before the
            // freshly spawned bobber entity. Let the authoritative server run
            // the missing-entity watchdog so that normal casts are never
            // cancelled by that harmless client-side arrival order.
            if (api.Side != EnumAppSide.Server)
            {
                return;
            }

            ItemStack? stack = slot.Itemstack;
            if (stack == null || !stack.Attributes.GetBool("fishing"))
            {
                return;
            }

            long bobberId = stack.Attributes.GetLong("bobberEntityId", 0L);
            if (bobberId == 0L || api.World.GetEntityById(bobberId) == null)
            {
                FinishCollectedSession(slot, byEntity, bobberId);
            }
        }

        private void FinishCollectedSession(
            ItemSlot slot,
            EntityAgent byEntity,
            long bobberId)
        {
            ItemStack? stack = slot.Itemstack;
            if (stack == null)
            {
                return;
            }

            byEntity.AnimManager.StopAnimation("fishingpole-idle");
            byEntity.AnimManager.StopAnimation(aimAnimation);

            int clothId = stack.Attributes.GetInt("clothId", 0);
            if (clothId != 0)
            {
                cm.UnregisterCloth(clothId);
            }

            // The native collect path already catches fish and kills the
            // bobber on the server. This is only a fallback for an entity that
            // survived that path; it must never call TryCatchFish a second time.
            if (api.Side == EnumAppSide.Server && bobberId != 0L)
            {
                Entity? bobber = api.World.GetEntityById(bobberId);
                bobber?.Die(EnumDespawnReason.Death, null);
            }

            stack.Attributes.SetBool("fishing", false);
            stack.Attributes.RemoveAttribute("bobberEntityId");
            stack.Attributes.RemoveAttribute("clothId");
            stack.Attributes.RemoveAttribute("fishingEntityId");
            slot.MarkDirty();
        }
    }
}
