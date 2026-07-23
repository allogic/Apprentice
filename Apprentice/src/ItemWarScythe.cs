using System;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    /// <summary>
    /// Combat-only scythe. It uses the vanilla scythe presentation lifecycle
    /// without inheriting ItemScythe's grass and crop harvesting behavior.
    /// </summary>
    public sealed class ItemWarScythe : Item
    {
        private const string TimelineTimeAttribute =
            "apprenticeWarScytheTimelineTime";
        private const string AttackWindowAttribute =
            "apprenticeWarScytheAttackWindow";
        private const string ImpactAppliedAttribute =
            "apprenticeWarScytheImpactApplied";
        private const string ReleaseDeferredAttribute =
            "apprenticeWarScytheReleaseDeferred";

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            // scytheIdle remains the delayed out-of-combat shoulder rest only.
            // The Apprentice runtime is the sole hit-pose owner.
            HeldTpHitAnimation = null;
            HeldRightTpIdleAnimation = "scytheIdle";
            HeldRightReadyAnimation = null;

            api.Logger.Notification(
                "[Apprentice] War Scythe animation ownership verified: hit=none; idle=scytheIdle; attack=apprentice-mainhand."
            );
        }

        public override void OnHeldAttackStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            ref EnumHandHandling handHandling)
        {
            if (api.Side == EnumAppSide.Client &&
                GetApprenticeSystem().IsWarScytheEditorPreviewActive)
            {
                // The development editor and gameplay attack never own the
                // same category at once. Closing the editor restores the
                // ordinary attack path without changing the held stack.
                handHandling = EnumHandHandling.PreventDefaultAction;
                return;
            }

            base.OnHeldAttackStart(
                slot,
                byEntity,
                blockSel,
                entitySel,
                ref handHandling
            );

            byEntity.Attributes.SetFloat(TimelineTimeAttribute, -0.0001f);
            byEntity.Attributes.SetBool(AttackWindowAttribute, false);
            byEntity.Attributes.SetBool(ImpactAppliedAttribute, false);
            byEntity.Attributes.SetBool(ReleaseDeferredAttribute, false);

            if (api.Side == EnumAppSide.Client)
            {
                GetApprenticeSystem().StartWarScytheAnimation(byEntity);
            }

            // Vintage Story owns input duration and stop propagation. The
            // Apprentice runtime exclusively owns the attack pose and impact.
            handHandling = EnumHandHandling.PreventDefaultAction;
        }

        public override bool OnHeldAttackCancel(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            EnumItemUseCancelReason cancelReason)
        {
            if (ShouldDeferAttackCancel(cancelReason))
            {
                // A War Scythe swing is a committed one-shot action. Denying
                // the early mouse-up cancel keeps the vanilla idle controller
                // paused until OnHeldAttackStep reaches the authored end.
                if (!byEntity.Attributes.GetBool(ReleaseDeferredAttribute))
                {
                    byEntity.Attributes.SetBool(
                        ReleaseDeferredAttribute,
                        true
                    );
                    if (api.Side == EnumAppSide.Client)
                    {
                        GetApprenticeSystem().NoteWarScytheLifecycle(
                            byEntity,
                            "release-deferred"
                        );
                    }
                }

                return false;
            }

            byEntity.Attributes.SetBool(AttackWindowAttribute, false);
            byEntity.Attributes.SetBool(ReleaseDeferredAttribute, false);
            if (api.Side == EnumAppSide.Client)
            {
                ApprenticeModSystem system = GetApprenticeSystem();
                system.NoteWarScytheLifecycle(
                    byEntity,
                    $"cancel-{cancelReason}"
                );
                system.StopWarScytheAnimation(byEntity);
            }

            return true;
        }

        internal static bool ShouldDeferAttackCancel(
            EnumItemUseCancelReason cancelReason) =>
            cancelReason == EnumItemUseCancelReason.ReleasedMouse;

        public override bool OnHeldAttackStep(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel)
        {
            ApprenticeAnimationDefinition definition =
                GetApprenticeSystem().WarScytheAnimation;
            AdvanceTimeline(secondsUsed, byEntity, slot, definition);

            // The client ends after the definition's ease-in, authored track,
            // and ease-out. The server follows the normal held-action stop
            // packet and remains authoritative for callback-owned damage.
            return api.Side == EnumAppSide.Server ||
                secondsUsed < definition.TotalActionSeconds;
        }

        public override void OnHeldAttackStop(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel)
        {
            ApprenticeAnimationDefinition definition =
                GetApprenticeSystem().WarScytheAnimation;
            AdvanceTimeline(secondsUsed, byEntity, slot, definition);
            byEntity.Attributes.SetBool(AttackWindowAttribute, false);
            byEntity.Attributes.SetBool(ReleaseDeferredAttribute, false);

            if (api.Side == EnumAppSide.Client)
            {
                ApprenticeModSystem system = GetApprenticeSystem();
                system.NoteWarScytheLifecycle(byEntity, "held-stop");
                system.StopWarScytheAnimation(byEntity);
            }
        }

        private void AdvanceTimeline(
            float secondsUsed,
            EntityAgent byEntity,
            ItemSlot slot,
            ApprenticeAnimationDefinition definition)
        {
            float previous = byEntity.Attributes.GetFloat(
                TimelineTimeAttribute,
                -0.0001f
            );
            float current = Math.Max(previous, secondsUsed);

            foreach (ApprenticeAnimationCallback callback in
                definition.Callbacks)
            {
                float callbackTime =
                    definition.EaseInSeconds + callback.TimeSeconds;
                if (previous < callbackTime && current >= callbackTime)
                {
                    ProcessTimelineCallback(
                        callback.Code,
                        byEntity,
                        slot
                    );
                }
            }

            byEntity.Attributes.SetFloat(TimelineTimeAttribute, current);
        }

        private void ProcessTimelineCallback(
            string callback,
            EntityAgent byEntity,
            ItemSlot slot)
        {
            switch (callback)
            {
                case "attack-start":
                    byEntity.Attributes.SetBool(AttackWindowAttribute, true);
                    break;

                case "attack-sample":
                    if (api.Side == EnumAppSide.Server &&
                        byEntity.Attributes.GetBool(AttackWindowAttribute) &&
                        !byEntity.Attributes.GetBool(ImpactAppliedAttribute))
                    {
                        ApplyAreaDamage(byEntity, slot);
                        byEntity.Attributes.SetBool(
                            ImpactAppliedAttribute,
                            true
                        );
                    }
                    break;

                case "attack-stop":
                case "ready":
                    byEntity.Attributes.SetBool(
                        AttackWindowAttribute,
                        false
                    );
                    break;
            }
        }

        private void ApplyAreaDamage(EntityAgent byEntity, ItemSlot slot)
        {
            if (api.Side != EnumAppSide.Server || slot.Empty ||
                slot.Itemstack?.Item is not ItemWarScythe)
            {
                return;
            }

            float damage = Attributes?["aoeDamage"].AsFloat(7.5f) ?? 7.5f;
            float radius = Attributes?["aoeRadius"].AsFloat(3.5f) ?? 3.5f;
            float arcDegrees = Attributes?["aoeArcDegrees"].AsFloat(120f) ?? 120f;
            int maxTargets = Math.Max(
                1,
                Attributes?["aoeMaxTargets"].AsInt(6) ?? 6
            );
            int damageTier = Attributes?["damageTier"].AsInt(5) ?? 5;

            Vec3d origin = byEntity.Pos.XYZ.AddCopy(0, byEntity.LocalEyePos.Y * 0.45, 0);
            Vec3f look3f = byEntity.Pos.GetViewVector();
            Vec3d forward = new Vec3d(look3f.X, 0, look3f.Z);
            if (forward.LengthSq() < 0.0001) return;
            forward.Normalize();

            double minimumDot = Math.Cos(arcDegrees * GameMath.DEG2RAD * 0.5);
            Entity[] targets = api.World.GetEntitiesAround(
                    origin,
                    radius,
                    radius,
                    entity => IsValidTarget(byEntity, entity)
                )
                .Select(entity => new
                {
                    Entity = entity,
                    Offset = entity.Pos.XYZ.SubCopy(origin)
                })
                .Where(candidate =>
                {
                    Vec3d horizontal = new Vec3d(
                        candidate.Offset.X,
                        0,
                        candidate.Offset.Z
                    );
                    double distance = horizontal.Length();
                    if (distance < 0.001) return false;
                    horizontal.Mul(1 / distance);
                    return horizontal.Dot(forward) >= minimumDot;
                })
                .OrderBy(candidate => candidate.Offset.LengthSq())
                .Take(maxTargets)
                .Select(candidate => candidate.Entity)
                .ToArray();

            int acceptedHits = 0;
            DamageSource source = new DamageSource
            {
                Source = EnumDamageSource.Player,
                SourceEntity = byEntity,
                Type = EnumDamageType.SlashingAttack,
                DamageTier = damageTier
            };

            foreach (Entity target in targets)
            {
                if (target.ReceiveDamage(source, damage)) acceptedHits++;
            }

            if (acceptedHits > 0)
            {
                DamageItem(api.World, byEntity, slot, 1);
            }
        }

        private static bool IsValidTarget(EntityAgent wielder, Entity entity)
        {
            return entity.EntityId != wielder.EntityId &&
                entity.Alive &&
                entity.IsInteractable &&
                entity is EntityAgent;
        }

        private ApprenticeModSystem GetApprenticeSystem() =>
            api.ModLoader.GetModSystem<ApprenticeModSystem>(true);
    }
}
