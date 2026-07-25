using System;
using System.Collections.Generic;
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
    public class ItemWarScythe : Item
    {
        private const string TimelineTimeAttribute =
            "apprenticeWarScytheTimelineTime";
        private const string AttackWindowAttribute =
            "apprenticeWarScytheAttackWindow";
        private const string ReleaseDeferredAttribute =
            "apprenticeWarScytheReleaseDeferred";

        private readonly Dictionary<long, SweepAttackState>
            sweepStates = new();

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

            // The native shoulder-rest track owns the Rest state. Stop it for
            // the committed action so the reference controller can move from
            // the last captured Rest pose into Ready without two pose owners
            // competing for the same arm and item-anchor elements.
            OneShotMeleeAnimation.Stop(
                byEntity,
                HeldRightTpIdleAnimation
            );
            OneShotMeleeAnimation.Stop(
                byEntity,
                "scytheIdle-fp"
            );

            byEntity.Attributes.SetFloat(TimelineTimeAttribute, -0.0001f);
            byEntity.Attributes.SetBool(AttackWindowAttribute, false);
            byEntity.Attributes.SetBool(ReleaseDeferredAttribute, false);
            if (api.Side == EnumAppSide.Server)
            {
                sweepStates.Remove(byEntity.EntityId);
            }

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
            if (api.Side == EnumAppSide.Server)
            {
                sweepStates.Remove(byEntity.EntityId);
            }
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
            if (api.Side == EnumAppSide.Server)
            {
                sweepStates.Remove(byEntity.EntityId);
            }

            if (api.Side == EnumAppSide.Client)
            {
                ApprenticeModSystem system = GetApprenticeSystem();
                system.NoteWarScytheLifecycle(byEntity, "held-stop");
                system.CompleteWarScytheAnimation(byEntity);
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
            float cursor = previous;

            foreach (ApprenticeAnimationCallback callback in
                definition.Callbacks)
            {
                float callbackTime =
                    definition.EaseInSeconds + callback.TimeSeconds;
                if (previous < callbackTime && current >= callbackTime)
                {
                    AdvanceSweepDamage(
                        cursor,
                        callbackTime,
                        byEntity,
                        slot
                    );
                    ProcessTimelineCallback(
                        callback.Code,
                        byEntity,
                        definition
                    );
                    cursor = callbackTime;
                }
            }

            AdvanceSweepDamage(cursor, current, byEntity, slot);
            byEntity.Attributes.SetFloat(TimelineTimeAttribute, current);
        }

        private void ProcessTimelineCallback(
            string callback,
            EntityAgent byEntity,
            ApprenticeAnimationDefinition definition)
        {
            switch (callback)
            {
                case "attack-start":
                    byEntity.Attributes.SetBool(AttackWindowAttribute, true);
                    if (api.Side == EnumAppSide.Server)
                    {
                        BeginSweep(byEntity, definition);
                    }
                    break;

                case "attack-sample":
                    // Damage is sampled continuously across the cutting arc.
                    // This callback remains in the authored contract as the
                    // center-of-sweep diagnostic marker.
                    break;

                case "attack-stop":
                case "ready":
                    byEntity.Attributes.SetBool(
                        AttackWindowAttribute,
                        false
                    );
                    if (api.Side == EnumAppSide.Server)
                    {
                        sweepStates.Remove(byEntity.EntityId);
                    }
                    break;
            }
        }

        private void BeginSweep(
            EntityAgent byEntity,
            ApprenticeAnimationDefinition definition)
        {
            ApprenticeAnimationCallback? start =
                definition.Callbacks.FirstOrDefault(callback =>
                    callback.Code == "attack-start"
                );
            ApprenticeAnimationCallback? stop =
                definition.Callbacks.FirstOrDefault(callback =>
                    callback.Code == "attack-stop"
                );
            if (start == null || stop == null ||
                stop.TimeSeconds <= start.TimeSeconds)
            {
                sweepStates.Remove(byEntity.EntityId);
                return;
            }

            sweepStates[byEntity.EntityId] = new SweepAttackState(
                definition.EaseInSeconds + start.TimeSeconds,
                definition.EaseInSeconds + stop.TimeSeconds
            );
        }

        private void AdvanceSweepDamage(
            float fromActionSeconds,
            float toActionSeconds,
            EntityAgent byEntity,
            ItemSlot slot)
        {
            if (api.Side != EnumAppSide.Server ||
                toActionSeconds <= fromActionSeconds ||
                !byEntity.Attributes.GetBool(AttackWindowAttribute) ||
                !sweepStates.TryGetValue(
                    byEntity.EntityId,
                    out SweepAttackState? state))
            {
                return;
            }

            float from = Math.Max(
                fromActionSeconds,
                state.StartActionSeconds
            );
            float to = Math.Min(
                toActionSeconds,
                state.StopActionSeconds
            );
            if (to <= from) return;

            ApplySweepDamage(byEntity, slot, state, from, to);
        }

        private void ApplySweepDamage(
            EntityAgent byEntity,
            ItemSlot slot,
            SweepAttackState state,
            float fromActionSeconds,
            float toActionSeconds)
        {
            if (api.Side != EnumAppSide.Server || slot.Empty ||
                slot.Itemstack?.Item is not ItemWarScythe)
            {
                return;
            }

            float damage = Attributes?["aoeDamage"].AsFloat(7.5f) ?? 7.5f;
            float radius = Attributes?["aoeRadius"].AsFloat(3.5f) ?? 3.5f;
            float arcDegrees = Math.Clamp(
                Attributes?["aoeArcDegrees"].AsFloat(180f) ?? 180f,
                1f,
                180f
            );
            int maxTargets = Math.Max(
                1,
                Attributes?["aoeMaxTargets"].AsInt(6) ?? 6
            );
            if (state.AcceptedHits >= maxTargets) return;

            float halfArc = arcDegrees * 0.5f;
            float sweepStartDegrees = Math.Clamp(
                Attributes?["aoeSweepStartDegrees"]
                    .AsFloat(-halfArc) ?? -halfArc,
                -halfArc,
                halfArc
            );
            float sweepEndDegrees = Math.Clamp(
                Attributes?["aoeSweepEndDegrees"]
                    .AsFloat(halfArc) ?? halfArc,
                -halfArc,
                halfArc
            );
            if (Math.Abs(sweepEndDegrees - sweepStartDegrees) < 0.001f)
            {
                sweepStartDegrees = -halfArc;
                sweepEndDegrees = halfArc;
            }
            float sweepHalfWidthDegrees = Math.Clamp(
                Attributes?["aoeSweepHalfWidthDegrees"]
                    .AsFloat(8f) ?? 8f,
                0.5f,
                30f
            );
            int damageTier = Attributes?["damageTier"].AsInt(5) ?? 5;

            double duration = Math.Max(
                0.001,
                state.StopActionSeconds -
                    state.StartActionSeconds
            );
            double fromProgress = Math.Clamp(
                (fromActionSeconds -
                    state.StartActionSeconds) / duration,
                0,
                1
            );
            double toProgress = Math.Clamp(
                (toActionSeconds -
                    state.StartActionSeconds) / duration,
                0,
                1
            );
            double fromAngle =
                sweepStartDegrees +
                (sweepEndDegrees - sweepStartDegrees) *
                fromProgress;
            double toAngle =
                sweepStartDegrees +
                (sweepEndDegrees - sweepStartDegrees) *
                toProgress;
            double minimumSweepAngle = Math.Max(
                -halfArc,
                Math.Min(fromAngle, toAngle) -
                    sweepHalfWidthDegrees
            );
            double maximumSweepAngle = Math.Min(
                halfArc,
                Math.Max(fromAngle, toAngle) +
                    sweepHalfWidthDegrees
            );

            Vec3d origin = byEntity.Pos.XYZ.AddCopy(
                0,
                byEntity.LocalEyePos.Y * 0.45,
                0
            );
            Vec3f look3f = byEntity.Pos.GetViewVector();
            Vec3d forward = new Vec3d(look3f.X, 0, look3f.Z);
            if (forward.LengthSq() < 0.0001) return;
            forward.Normalize();

            bool ascending =
                sweepEndDegrees > sweepStartDegrees;
            var targets = api.World.GetEntitiesAround(
                    origin,
                    radius,
                    radius,
                    entity => IsValidTarget(byEntity, entity)
                )
                .Select(entity => new
                {
                    Entity = entity,
                    Offset = entity.Pos.XYZ.SubCopy(origin),
                })
                .Select(candidate =>
                {
                    Vec3d horizontal = new Vec3d(
                        candidate.Offset.X,
                        0,
                        candidate.Offset.Z
                    );
                    double distance = horizontal.Length();
                    if (distance < 0.001 || distance > radius)
                    {
                        return new
                        {
                            candidate.Entity,
                            Angle = double.NaN,
                            Distance = distance
                        };
                    }
                    horizontal.Mul(1 / distance);
                    double dot = Math.Clamp(
                        horizontal.Dot(forward),
                        -1,
                        1
                    );
                    double cross =
                        forward.X * horizontal.Z -
                        forward.Z * horizontal.X;
                    double angle =
                        Math.Atan2(cross, dot) *
                        GameMath.RAD2DEG;
                    return new
                    {
                        candidate.Entity,
                        Angle = angle,
                        Distance = distance
                    };
                })
                .Where(candidate =>
                    !double.IsNaN(candidate.Angle) &&
                    candidate.Angle >= minimumSweepAngle &&
                    candidate.Angle <= maximumSweepAngle &&
                    !state.HitEntityIds.Contains(
                        candidate.Entity.EntityId
                    )
                )
                .OrderBy(candidate =>
                    ascending
                        ? candidate.Angle
                        : -candidate.Angle
                )
                .ThenBy(candidate => candidate.Distance)
                .ToArray();

            DamageSource source = new DamageSource
            {
                Source = EnumDamageSource.Player,
                SourceEntity = byEntity,
                Type = EnumDamageType.SlashingAttack,
                DamageTier = damageTier
            };

            foreach (var candidate in targets)
            {
                if (state.AcceptedHits >= maxTargets) break;

                Entity target = candidate.Entity;
                state.HitEntityIds.Add(target.EntityId);
                if (!target.ReceiveDamage(source, damage)) continue;

                state.AcceptedHits++;
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

        private sealed class SweepAttackState
        {
            public SweepAttackState(
                float startActionSeconds,
                float stopActionSeconds)
            {
                StartActionSeconds = startActionSeconds;
                StopActionSeconds = stopActionSeconds;
            }

            public float StartActionSeconds { get; }
            public float StopActionSeconds { get; }
            public HashSet<long> HitEntityIds { get; } = new();
            public int AcceptedHits { get; set; }
        }
    }
}
