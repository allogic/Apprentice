using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Apprentice
{
    public class ItemLegendaryMelee : Item
    {
        public override void OnBeforeRender(
            ICoreClientAPI capi,
            ItemStack itemstack,
            EnumItemRenderTarget target,
            ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(
                capi,
                itemstack,
                target,
                ref renderinfo
            );
            if (target != EnumItemRenderTarget.HandTp ||
                itemstack.Attributes.GetLong(
                    LegendaryWeaponRuntime
                        .InvisibleHeldUntilAttribute,
                    0) <=
                    capi.World.ElapsedMilliseconds)
            {
                return;
            }

            ModelTransform hidden =
                renderinfo.Transform?.Clone() ??
                ModelTransform.NoTransform;
            hidden.Scale = 0.001f;
            renderinfo.Transform = hidden;
        }

        public override float OnBlockBreaking(
            IPlayer player,
            BlockSelection blockSel,
            ItemSlot itemslot,
            float remainingResistance,
            float dt,
            int counter) =>
            remainingResistance;

        public override bool OnBlockBrokenWith(
            IWorldAccessor world,
            Entity byEntity,
            ItemSlot itemslot,
            BlockSelection blockSel,
            float dropQuantityMultiplier = 1f) =>
            false;

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

            float duration =
                Attributes?["swingDuration"]
                    .AsFloat(0.7f) ?? 0.7f;
            OneShotMeleeAnimation.ScheduleStop(
                api,
                byEntity,
                HeldTpHitAnimation,
                duration
            );
        }
    }

    public sealed class ItemHiddenPoisonDagger :
        ItemLegendaryMelee
    {
        private const string DrawnUntilAttribute =
            "apprentice:hiddenDaggerDrawnUntilMs";
        private const int DrawnMilliseconds = 720;

        public override void OnHeldAttackStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            ref EnumHandHandling handHandling)
        {
            ItemStack? stack = slot.Itemstack;
            if (stack != null)
            {
                stack.Attributes.SetLong(
                    DrawnUntilAttribute,
                    byEntity.World.ElapsedMilliseconds +
                        DrawnMilliseconds
                );
                slot.MarkDirty();
            }

            base.OnHeldAttackStart(
                slot,
                byEntity,
                blockSel,
                entitySel,
                ref handHandling
            );
        }

        public override void OnBeforeRender(
            ICoreClientAPI capi,
            ItemStack itemstack,
            EnumItemRenderTarget target,
            ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(
                capi,
                itemstack,
                target,
                ref renderinfo
            );
            if (target != EnumItemRenderTarget.HandTp ||
                itemstack.Attributes.GetLong(
                    DrawnUntilAttribute,
                    0) >
                    capi.World.ElapsedMilliseconds)
            {
                return;
            }

            ModelTransform hidden =
                renderinfo.Transform?.Clone() ??
                ModelTransform.NoTransform;
            hidden.Scale = 0.001f;
            renderinfo.Transform = hidden;
        }
    }

    public sealed class ItemStealthDagger :
        ItemLegendaryMelee
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handHandling)
        {
            if (!byEntity.Controls.ShiftKey ||
                byEntity is not EntityPlayer player)
            {
                base.OnHeldInteractStart(
                    slot,
                    byEntity,
                    blockSel,
                    entitySel,
                    firstEvent,
                    ref handHandling
                );
                return;
            }

            handHandling =
                EnumHandHandling.PreventDefaultAction;
            if (byEntity.World.Side ==
                EnumAppSide.Server)
            {
                LegendaryWeaponRuntime.ActivateStealth(
                    player
                );
            }
        }
    }

    public sealed class ItemShapeSplitter :
        ItemLegendaryMelee
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handHandling)
        {
            if (!byEntity.Controls.ShiftKey ||
                byEntity is not EntityPlayer player)
            {
                base.OnHeldInteractStart(
                    slot,
                    byEntity,
                    blockSel,
                    entitySel,
                    firstEvent,
                    ref handHandling
                );
                return;
            }

            handHandling =
                EnumHandHandling.PreventDefaultAction;
            if (byEntity.World.Side ==
                EnumAppSide.Server)
            {
                LegendaryWeaponRuntime
                    .ActivateShapeSplitter(player);
            }
        }
    }

    public class LegendarySpearItem : ItemSpear
    {
        internal const string NuibariOutAttribute =
            "apprentice:nuibariOut";

        protected virtual LegendaryProjectileKind
            ProjectileKind =>
            LegendaryProjectileKind.UnendingDespair;

        protected virtual bool RestoreAfterThrow =>
            false;

        protected virtual bool SupportsRecall =>
            false;

        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handHandling)
        {
            if (byEntity is EntityPlayer player &&
                byEntity.Controls.ShiftKey &&
                SupportsRecall)
            {
                handHandling =
                    EnumHandHandling.PreventDefaultAction;
                if (byEntity.World.Side ==
                    EnumAppSide.Server)
                {
                    if (ProjectileKind ==
                        LegendaryProjectileKind
                            .UnendingDespair)
                    {
                        LegendaryWeaponRuntime
                            .RecallUnendingDespair(
                                player
                            );
                    }
                    else if (ProjectileKind ==
                        LegendaryProjectileKind
                            .Nuibari)
                    {
                        LegendaryWeaponRuntime
                            .RecallNuibari(player);
                    }
                }
                return;
            }

            if (ProjectileKind ==
                    LegendaryProjectileKind.Nuibari &&
                slot.Itemstack?.Attributes.GetBool(
                    NuibariOutAttribute,
                    false) == true)
            {
                handHandling =
                    EnumHandHandling.PreventDefaultAction;
                return;
            }

            base.OnHeldInteractStart(
                slot,
                byEntity,
                blockSel,
                entitySel,
                firstEvent,
                ref handHandling
            );
        }

        public override void OnHeldInteractStop(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel)
        {
            string entityCode =
                Attributes?["spearEntityCode"]
                    .AsString(string.Empty) ??
                string.Empty;
            EntityProperties? entityType =
                entityCode.Length == 0
                    ? null
                    : byEntity.World.GetEntityType(
                        new AssetLocation(entityCode)
                    );
            if (entityType == null)
            {
                byEntity.Attributes.SetInt("aiming", 0);
                byEntity.Attributes.SetInt(
                    "aimingCancel",
                    1
                );
                byEntity.StopAnimation("aim");
                api.Logger.Error(
                    "[Apprentice] Legendary spear throw cancelled: projectile entity '{0}' is not registered.",
                    entityCode
                );
                return;
            }

            ItemStack? original =
                slot.Itemstack?.Clone();
            base.OnHeldInteractStop(
                secondsUsed,
                slot,
                byEntity,
                blockSel,
                entitySel
            );

            if (secondsUsed < 0.35f ||
                original == null)
            {
                return;
            }

            ItemStack? restored = null;
            if (RestoreAfterThrow && slot.Empty)
            {
                slot.Itemstack = original;
                restored = slot.Itemstack;
                if (ProjectileKind ==
                    LegendaryProjectileKind.Nuibari)
                {
                    restored.Attributes.SetBool(
                        NuibariOutAttribute,
                        true
                    );
                }
                slot.MarkDirty();
            }
            else if (RestoreAfterThrow)
            {
                restored = slot.Itemstack;
            }

            if (byEntity.World.Side ==
                EnumAppSide.Server)
            {
                LegendaryWeaponRuntime
                    .RegisterThrownProjectile(
                        byEntity,
                        entityCode,
                        ProjectileKind,
                        restored
                    );
            }
        }

        public override void OnBeforeRender(
            ICoreClientAPI capi,
            ItemStack itemstack,
            EnumItemRenderTarget target,
            ref ItemRenderInfo renderinfo)
        {
            base.OnBeforeRender(
                capi,
                itemstack,
                target,
                ref renderinfo
            );
            if (ProjectileKind !=
                    LegendaryProjectileKind.Nuibari ||
                target != EnumItemRenderTarget.HandTp ||
                !itemstack.Attributes.GetBool(
                    NuibariOutAttribute,
                    false))
            {
                return;
            }

            ModelTransform hidden =
                renderinfo.Transform?.Clone() ??
                ModelTransform.NoTransform;
            hidden.Scale = 0.001f;
            renderinfo.Transform = hidden;
        }
    }

    public sealed class ItemUnendingDespair :
        LegendarySpearItem
    {
        protected override bool RestoreAfterThrow =>
            true;

        protected override bool SupportsRecall =>
            true;
    }

    public sealed class ItemFireLance :
        LegendarySpearItem
    {
        protected override LegendaryProjectileKind
            ProjectileKind =>
            LegendaryProjectileKind.FireLance;
    }

    public sealed class ItemNuibari :
        LegendarySpearItem
    {
        protected override LegendaryProjectileKind
            ProjectileKind =>
            LegendaryProjectileKind.Nuibari;

        protected override bool RestoreAfterThrow =>
            true;

        protected override bool SupportsRecall =>
            true;
    }
}
