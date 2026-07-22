using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    /// <summary>
    /// Combat-only scythe. It deliberately does not inherit ItemScythe, so
    /// Vintage Story's grass/crop harvesting sweep is never invoked.
    /// </summary>
    public sealed class ItemWarScythe : Item
    {
        private const float SwingDurationSeconds = 1.10f;
        private const float ImpactDelaySeconds = 0.67f;
        private const int AnimationStopGraceMilliseconds = 80;

        private sealed class SwingState
        {
            public bool Active;
            public bool AwaitingRelease;
            public int Sequence;
        }

        private readonly Dictionary<long, SwingState> swingStates = new();

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);

            HeldTpHitAnimation = "apprenticescytheswing";
            HeldRightTpIdleAnimation = "apprenticescytheshoulder";
            HeldLeftTpIdleAnimation = "apprenticescytheshoulder";
            HeldRightReadyAnimation = "apprenticescytheshoulder";
            HeldLeftReadyAnimation = "apprenticescytheshoulder";
        }

        public override void OnHeldAttackStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            ref EnumHandHandling handHandling)
        {
            handHandling = EnumHandHandling.PreventDefaultAction;

            SwingState state = GetSwingState(byEntity);
            if (state.Active || state.AwaitingRelease)
            {
                state.AwaitingRelease = true;
                return;
            }

            state.Active = true;
            state.AwaitingRelease = true;
            int sequence = ++state.Sequence;

            OneShotMeleeAnimation.Stop(byEntity, HeldRightTpIdleAnimation);
            OneShotMeleeAnimation.Stop(byEntity, HeldLeftTpIdleAnimation);

            bool started = byEntity.AnimManager.StartAnimation(HeldTpHitAnimation);
            if (!started)
            {
                state.Active = false;
                api.Logger.Warning(
                    "[Apprentice] War Scythe animation '{0}' could not be started for entity {1}.",
                    HeldTpHitAnimation,
                    byEntity.EntityId
                );
                ResumeShoulderPose(byEntity);
                return;
            }

            api.Event.RegisterCallback(
                _ => CompleteSwing(byEntity, slot, state, sequence),
                (int)Math.Ceiling(SwingDurationSeconds * 1000f) +
                    AnimationStopGraceMilliseconds
            );

            if (api.Side == EnumAppSide.Server)
            {
                api.Event.RegisterCallback(
                    _ => ApplyScheduledImpact(byEntity, slot, state, sequence),
                    (int)Math.Ceiling(ImpactDelaySeconds * 1000f)
                );
            }
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            SwingState state = GetSwingState(byEntity);
            if (!byEntity.Controls.LeftMouseDown)
            {
                state.AwaitingRelease = false;
            }

            if (state.Active)
            {
                OneShotMeleeAnimation.Stop(byEntity, HeldRightTpIdleAnimation);
                return;
            }

            base.OnHeldIdle(slot, byEntity);

            if (!byEntity.AnimManager.IsAnimationActive(HeldTpHitAnimation) &&
                !byEntity.AnimManager.IsAnimationActive(HeldRightTpIdleAnimation))
            {
                ResumeShoulderPose(byEntity);
            }
        }

        public override bool OnHeldAttackStep(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel)
        {
            return GetSwingState(byEntity).Active &&
                secondsUsed < SwingDurationSeconds;
        }

        public override void OnHeldAttackStop(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel)
        {
            SwingState state = GetSwingState(byEntity);
            if (!byEntity.Controls.LeftMouseDown)
            {
                state.AwaitingRelease = false;
            }
        }

        private SwingState GetSwingState(EntityAgent entity)
        {
            if (!swingStates.TryGetValue(entity.EntityId, out SwingState? state))
            {
                state = new SwingState();
                swingStates[entity.EntityId] = state;
            }

            return state;
        }

        private void CompleteSwing(
            EntityAgent byEntity,
            ItemSlot slot,
            SwingState state,
            int sequence)
        {
            if (state.Sequence != sequence) return;

            OneShotMeleeAnimation.Stop(byEntity, HeldTpHitAnimation);
            state.Active = false;

            if (!byEntity.Controls.LeftMouseDown)
            {
                state.AwaitingRelease = false;
            }

            if (IsScytheStillHeld(byEntity, slot))
            {
                ResumeShoulderPose(byEntity);
            }
        }

        private void ApplyScheduledImpact(
            EntityAgent byEntity,
            ItemSlot slot,
            SwingState state,
            int sequence)
        {
            if (state.Sequence != sequence ||
                !state.Active ||
                !IsScytheStillHeld(byEntity, slot))
            {
                return;
            }

            ApplyAreaDamage(byEntity, slot);
        }

        private bool IsScytheStillHeld(EntityAgent byEntity, ItemSlot startedSlot)
        {
            if (startedSlot.Empty || startedSlot.Itemstack?.Collectible != this)
            {
                return false;
            }

            return byEntity.RightHandItemSlot?.Itemstack?.Collectible == this;
        }

        private void ResumeShoulderPose(EntityAgent byEntity)
        {
            if (!byEntity.AnimManager.IsAnimationActive(HeldRightTpIdleAnimation))
            {
                byEntity.AnimManager.StartAnimation(HeldRightTpIdleAnimation);
            }
        }

        private void ApplyAreaDamage(EntityAgent byEntity, ItemSlot slot)
        {
            if (api.Side != EnumAppSide.Server || slot.Empty) return;

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
    }
}
