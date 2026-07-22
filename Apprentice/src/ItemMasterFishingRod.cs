using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Apprentice
{
    public sealed class ItemMasterFishingRod : ItemFishingPole
    {
        // Centre of the modelled final guide eye, converted from shape units
        // (1/16 block) to item-model units.
        private static readonly Vec3f ModelTipEye = new Vec3f(
            1.856239f / 16f,
            35.106346f / 16f,
            8f / 16f);

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // Base ItemFishingPole overwrites the JSON FP offset in OnLoaded.
            // Restore the complete custom-model point, including X and Z.
            offsetToPoleTipFp = ModelTipEye.Clone();
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            // Preserve the complete native fishing lifecycle. In particular,
            // do not perform a missing-bobber watchdog here: priority-spawned
            // bobbers may not be indexed yet during the first idle tick.
            base.OnHeldIdle(slot, byEntity);
            RemoveOrphanedOwnedBobbers(slot, byEntity);
            PinThirdPersonRopeToRenderedTip(slot, byEntity);
        }

        private void RemoveOrphanedOwnedBobbers(ItemSlot slot, EntityAgent byEntity)
        {
            if (api.Side != EnumAppSide.Server)
            {
                return;
            }

            long activeBobberId =
                slot.Itemstack?.Attributes.GetLong("bobberEntityId", 0L) ?? 0L;

            Entity[] nearbyBobbers = api.World.GetEntitiesAround(
                byEntity.Pos.XYZ,
                256f,
                256f,
                entity => entity is EntityBobber bobber &&
                    bobber.AttachedToEntityId == byEntity.EntityId);

            foreach (Entity entity in nearbyBobbers)
            {
                if (entity.EntityId != activeBobberId)
                {
                    entity.Die(EnumDespawnReason.Death, null);
                }
            }
        }

        public override bool OnHeldInteractStep(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel)
        {
            bool result = base.OnHeldInteractStep(
                secondsUsed,
                slot,
                byEntity,
                blockSel,
                entitySel);

            PinThirdPersonRopeToRenderedTip(slot, byEntity);
            return result;
        }

        private void PinThirdPersonRopeToRenderedTip(
            ItemSlot slot,
            EntityAgent byEntity)
        {
            if (api is not ICoreClientAPI capi ||
                capi.World.Player.CameraMode == EnumCameraMode.FirstPerson)
            {
                return;
            }

            int clothId = slot.Itemstack?.Attributes.GetInt("clothId", 0) ?? 0;
            ClothSystem? rope = cm.GetClothSystem(clothId);
            if (rope == null)
            {
                return;
            }

            Matrixf modelMatrix = new Matrixf();
            if (!LoadHeldItemModelMatrix(modelMatrix, byEntity, slot, capi))
            {
                return;
            }

            Vec4f renderedTip = modelMatrix.TransformVector(new Vec4f(
                ModelTipEye.X,
                ModelTipEye.Y,
                ModelTipEye.Z,
                1f));

            rope.FirstPoint.pinnedToOffset = renderedTip.XYZ;
            rope.FirstPoint.NoAttachTransform = true;
        }
    }
}
