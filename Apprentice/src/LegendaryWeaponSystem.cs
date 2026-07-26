using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using HarmonyLib;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Apprentice
{
    internal static class LegendaryWeaponRuntime
    {
        internal const string LegendaryAttribute =
            "apprenticeLegendary";
        internal const string InvisibleUntilAttribute =
            "apprentice:legendaryInvisibleUntilMs";
        internal const string InvisibleSourceAttribute =
            "apprentice:legendaryInvisibleSource";
        internal const string InvisibleHeldUntilAttribute =
            "apprentice:legendaryInvisibleHeldUntilMs";

        private static LegendaryWeaponSystem? server;

        internal static void Configure(LegendaryWeaponSystem system) =>
            server = system;

        internal static void Clear(LegendaryWeaponSystem system)
        {
            if (ReferenceEquals(server, system))
            {
                server = null;
            }
        }

        internal static bool IsLegendary(ItemStack? stack) =>
            stack?.Collectible?.Attributes?[LegendaryAttribute]
                .AsBool(false) == true;

        internal static float ModifyDirectDamage(
            Entity damagedEntity,
            EntityPlayer? attacker,
            string? weaponCode,
            DamageSource damageSource,
            float damage) =>
            server?.ModifyDirectDamage(
                damagedEntity,
                attacker,
                weaponCode,
                damageSource,
                damage
            ) ?? damage;

        internal static void OnConfirmedDamage(
            Entity damagedEntity,
            DamageSource damageSource,
            string? weaponCode,
            double actualHealthLost)
        {
            server?.OnConfirmedDamage(
                damagedEntity,
                damageSource,
                weaponCode,
                actualHealthLost
            );
        }

        internal static void RegisterThrownProjectile(
            EntityAgent owner,
            string projectileCode,
            LegendaryProjectileKind kind,
            ItemStack? restoredStack = null)
        {
            server?.RegisterThrownProjectile(
                owner,
                projectileCode,
                kind,
                restoredStack
            );
        }

        internal static bool ActivateStealth(EntityPlayer player) =>
            server?.ActivateStealth(player) == true;

        internal static bool ActivateShapeSplitter(EntityPlayer player) =>
            server?.ActivateShapeSplitter(player) == true;

        internal static bool RecallUnendingDespair(EntityPlayer player) =>
            server?.RecallUnendingDespair(player) == true;

        internal static bool RecallNuibari(EntityPlayer player) =>
            server?.RecallNuibari(player) == true;

        internal static bool IsNuibariOut(EntityPlayer player) =>
            server?.IsNuibariOut(player) == true;
    }

    public enum LegendaryProjectileKind
    {
        UnendingDespair,
        FireLance,
        Nuibari
    }

    internal sealed class LegendaryWeaponSystem : IDisposable
    {
        private const string HiddenPoisonCode =
            "apprentice:hiddenpoisondagger";
        private const string WarAxeCode =
            "apprentice:waraxe";
        private const string UnendingDespairCode =
            "apprentice:unendingdespair";
        private const string FireLanceCode =
            "apprentice:firelance";
        private const string StealthDaggerCode =
            "apprentice:stealthdagger";
        private const string ShapeSplitterCode =
            "apprentice:shapesplitter";
        private const string NuibariCode =
            "apprentice:nuibari";

        private const string PoisonStacksAttribute =
            "apprentice:legendaryPoisonStacks";
        private const string BleedStacksAttribute =
            "apprentice:legendaryBleedStacks";
        private const string DespairSpearsAttribute =
            "apprentice:despairSpearStacks";
        private const string NuibariBoundAttribute =
            "apprentice:nuibariBound";

        private const int TickMilliseconds = 50;
        private const int PoisonDurationMilliseconds = 10000;
        private const int StealthDurationMilliseconds = 6000;
        private const int StealthCooldownMilliseconds = 18000;
        private const int ShapeSplitterDurationMilliseconds = 5000;
        private const int ShapeSplitterCooldownMilliseconds = 25000;
        private const int DespairLifetimeMilliseconds = 5000;

        private const float PoisonDamagePerStack = 0.55f;
        private const float BleedDamagePerStack = 0.8f;
        private const float DespairRecallDamagePerSpear = 4.5f;
        private const float NuibariPierceDamage = 8f;
        private const float NuibariMaximumRange = 24f;
        private const float NuibariHitRadius = 0.65f;
        private const float FireLanceRadius = 5.5f;
        private const float FireLanceMaximumAoeDamage = 16f;
        private const float FireLanceMinimumAoeDamage = 6f;

        private readonly ICoreServerAPI api;
        private readonly long tickListenerId;

        private readonly Dictionary<PoisonKey, PoisonState>
            poisonStates = new();
        private readonly Dictionary<BleedKey, BleedState>
            bleedStates = new();
        private readonly Dictionary<long, ProjectileState>
            projectileStates = new();
        private readonly Dictionary<long, NuibariState>
            nuibariStates = new();
        private readonly Dictionary<long, InvisibleState>
            invisibleStates = new();
        private readonly Dictionary<CooldownKey, long>
            cooldowns = new();
        private readonly Dictionary<BleedKey, long>
            suppressNextBleed = new();

        private bool disposed;

        internal LegendaryWeaponSystem(ICoreServerAPI api)
        {
            this.api = api;
            LegendaryWeaponRuntime.Configure(this);
            tickListenerId = api.Event.RegisterGameTickListener(
                OnTick,
                TickMilliseconds
            );
        }

        internal float ModifyDirectDamage(
            Entity damagedEntity,
            EntityPlayer? attacker,
            string? weaponCode,
            DamageSource damageSource,
            float damage)
        {
            if (disposed || attacker == null || damage <= 0 ||
                damageSource.Source is
                    EnumDamageSource.Internal or
                    EnumDamageSource.Bleed)
            {
                return damage;
            }

            if (weaponCode == StealthDaggerCode &&
                invisibleStates.TryGetValue(
                    attacker.EntityId,
                    out InvisibleState? invisible) &&
                invisible.Source == InvisibleSource.StealthDagger &&
                invisible.ExpiresAtMs >
                    api.World.ElapsedMilliseconds)
            {
                ClearInvisibility(attacker);
                damage *= 3f;
            }

            if (weaponCode != WarAxeCode)
            {
                return damage;
            }

            BleedKey key = new(
                attacker.EntityId,
                damagedEntity.EntityId
            );
            if (!bleedStates.TryGetValue(
                    key,
                    out BleedState? bleed) ||
                bleed.Stacks < 5)
            {
                return damage;
            }

            EntityBehaviorHealth? health =
                damagedEntity.GetBehavior<EntityBehaviorHealth>();
            if (health == null || health.MaxHealth <= 0 ||
                health.Health > health.MaxHealth * 0.2f)
            {
                return damage;
            }

            bleedStates.Remove(key);
            suppressNextBleed[key] =
                api.World.ElapsedMilliseconds + 500;
            SetWatchedInt(
                damagedEntity,
                BleedStacksAttribute,
                0
            );

            return Math.Max(
                damage,
                health.MaxHealth * 1000f
            );
        }

        internal void OnConfirmedDamage(
            Entity damagedEntity,
            DamageSource damageSource,
            string? weaponCode,
            double actualHealthLost)
        {
            if (disposed || actualHealthLost <= 0 ||
                damageSource.Source is
                    EnumDamageSource.Internal or
                    EnumDamageSource.Bleed)
            {
                return;
            }

            Entity? causingEntity = damageSource.GetCauseEntity();
            EntityPlayer? attacker =
                causingEntity as EntityPlayer;

            if (weaponCode == HiddenPoisonCode &&
                attacker != null)
            {
                AddPoisonStack(attacker, damagedEntity);
                return;
            }

            if (weaponCode == WarAxeCode && attacker != null)
            {
                AddBleedStack(attacker, damagedEntity);
                return;
            }

            if (damageSource.SourceEntity is not
                    Entity projectileEntity ||
                damageSource.SourceEntity is not IProjectile projectile)
            {
                if (weaponCode == NuibariCode &&
                    attacker != null &&
                    nuibariStates.TryGetValue(
                        attacker.EntityId,
                        out NuibariState? threaded))
                {
                    BindNuibariTarget(
                        threaded,
                        damagedEntity
                    );
                }
                return;
            }

            ProjectileState state = EnsureProjectileState(
                projectileEntity,
                projectile,
                weaponCode
            );
            switch (state.Kind)
            {
                case LegendaryProjectileKind.UnendingDespair:
                    EmbedDespairSpear(
                        state,
                        damagedEntity
                    );
                    break;

                case LegendaryProjectileKind.FireLance:
                    DetonateFireLance(
                        state,
                        damagedEntity.Pos.XYZ
                    );
                    break;

                case LegendaryProjectileKind.Nuibari:
                    ResolveNuibariPierce(
                        state,
                        damagedEntity
                    );
                    break;
            }
        }

        internal void RegisterThrownProjectile(
            EntityAgent owner,
            string projectileCode,
            LegendaryProjectileKind kind,
            ItemStack? restoredStack)
        {
            if (disposed ||
                owner.World.Side != EnumAppSide.Server)
            {
                return;
            }

            api.Event.RegisterCallback(
                _ => CaptureLatestProjectile(
                    owner,
                    projectileCode,
                    kind,
                    restoredStack
                ),
                1
            );
        }

        internal bool ActivateStealth(EntityPlayer player)
        {
            if (!CanActivate(
                    player,
                    "stealth-dagger",
                    StealthCooldownMilliseconds))
            {
                return false;
            }

            SetInvisibility(
                player,
                InvisibleSource.StealthDagger,
                StealthDurationMilliseconds
            );
            api.World.PlaySoundAt(
                new AssetLocation("game:sounds/player/rustle"),
                player,
                null,
                false,
                24,
                0.55f
            );
            return true;
        }

        internal bool ActivateShapeSplitter(EntityPlayer player)
        {
            if (!CanActivate(
                    player,
                    "shapesplitter",
                    ShapeSplitterCooldownMilliseconds))
            {
                return false;
            }

            SpawnShapeEcho(player);
            SetInvisibility(
                player,
                InvisibleSource.ShapeSplitter,
                ShapeSplitterDurationMilliseconds
            );
            api.World.PlaySoundAt(
                new AssetLocation("game:sounds/effect/teleport"),
                player,
                null,
                false,
                32,
                0.8f
            );
            return true;
        }

        internal bool RecallUnendingDespair(EntityPlayer player)
        {
            ProjectileState[] owned = projectileStates.Values
                .Where(state =>
                    state.Kind ==
                        LegendaryProjectileKind.UnendingDespair &&
                    state.OwnerEntityId == player.EntityId)
                .ToArray();
            if (owned.Length == 0)
            {
                return false;
            }

            foreach (IGrouping<long, ProjectileState> group in
                owned.Where(state => state.TargetEntityId != 0)
                    .GroupBy(state => state.TargetEntityId))
            {
                Entity? target =
                    api.World.GetEntityById(group.Key);
                if (target == null || !target.Alive)
                {
                    continue;
                }

                int count = group.Count();
                target.ReceiveDamage(
                    new DamageSource
                    {
                        Source = EnumDamageSource.Player,
                        SourceEntity = player,
                        Type = EnumDamageType.PiercingAttack,
                        DamageTier = 5
                    },
                    DespairRecallDamagePerSpear * count
                );
                SpawnBurstParticles(
                    target.Pos.XYZ,
                    unchecked((int)0xff6d35a8),
                    Math.Min(28, 7 + count * 4),
                    0.55f
                );
            }

            foreach (ProjectileState state in owned)
            {
                DespawnProjectile(state);
            }
            UpdateDespairStackCounts();
            return true;
        }

        internal bool RecallNuibari(EntityPlayer player)
        {
            if (!nuibariStates.TryGetValue(
                    player.EntityId,
                    out NuibariState? state))
            {
                return false;
            }

            EndNuibari(state, recalled: true);
            return true;
        }

        internal bool IsNuibariOut(EntityPlayer player) =>
            nuibariStates.ContainsKey(player.EntityId);

        private void AddPoisonStack(
            EntityPlayer attacker,
            Entity target)
        {
            PoisonKey key = new(
                attacker.EntityId,
                target.EntityId
            );
            long now = api.World.ElapsedMilliseconds;
            if (!poisonStates.TryGetValue(
                    key,
                    out PoisonState? state))
            {
                state = new PoisonState(
                    attacker.EntityId,
                    target.EntityId
                );
                poisonStates[key] = state;
            }

            if (state.Stacks < int.MaxValue)
            {
                state.Stacks++;
            }
            state.ExpiresAtMs =
                now + PoisonDurationMilliseconds;
            state.NextPulseAtMs =
                Math.Min(
                    state.NextPulseAtMs == 0
                        ? now + 1000
                        : state.NextPulseAtMs,
                    now + 1000
                );
            SetWatchedInt(
                target,
                PoisonStacksAttribute,
                SumPoisonStacks(target.EntityId)
            );
        }

        private void AddBleedStack(
            EntityPlayer attacker,
            Entity target)
        {
            BleedKey key = new(
                attacker.EntityId,
                target.EntityId
            );
            long now = api.World.ElapsedMilliseconds;
            if (suppressNextBleed.TryGetValue(
                    key,
                    out long suppressUntil) &&
                suppressUntil >= now)
            {
                suppressNextBleed.Remove(key);
                return;
            }

            suppressNextBleed.Remove(key);
            if (!bleedStates.TryGetValue(
                    key,
                    out BleedState? state))
            {
                state = new BleedState(
                    attacker.EntityId,
                    target.EntityId
                );
                bleedStates[key] = state;
            }

            state.Stacks = Math.Min(5, state.Stacks + 1);
            state.ExpiresAtMs =
                now + (2000 + state.Stacks * 1000);
            state.NextPulseAtMs =
                Math.Min(
                    state.NextPulseAtMs == 0
                        ? now + 1000
                        : state.NextPulseAtMs,
                    now + 1000
                );
            SetWatchedInt(
                target,
                BleedStacksAttribute,
                HighestBleedStacks(target.EntityId)
            );
        }

        private void CaptureLatestProjectile(
            EntityAgent owner,
            string projectileCode,
            LegendaryProjectileKind kind,
            ItemStack? restoredStack)
        {
            if (!owner.Alive)
            {
                return;
            }

            Entity? entity = api.World.GetEntitiesAround(
                    owner.Pos.XYZ,
                    64,
                    64,
                    candidate =>
                        candidate.Code?.ToString() ==
                            projectileCode &&
                        candidate is IProjectile projectile &&
                        projectile.FiredBy?.EntityId ==
                            owner.EntityId &&
                        !projectileStates.ContainsKey(
                            candidate.EntityId
                        )
                )
                .OrderByDescending(candidate =>
                    candidate.EntityId)
                .FirstOrDefault();
            if (entity is not IProjectile projectile)
            {
                api.Logger.Warning(
                    "[Apprentice] Legendary projectile '{0}' from entity {1} was not found after the throw.",
                    projectileCode,
                    owner.EntityId
                );
                return;
            }

            ProjectileState state = new(
                entity.EntityId,
                owner.EntityId,
                kind,
                api.World.ElapsedMilliseconds +
                    (kind ==
                        LegendaryProjectileKind.UnendingDespair
                            ? DespairLifetimeMilliseconds
                            : 30000),
                restoredStack
            );
            projectileStates[entity.EntityId] = state;

            if (kind == LegendaryProjectileKind.Nuibari)
            {
                if (nuibariStates.TryGetValue(
                        owner.EntityId,
                        out NuibariState? previous))
                {
                    EndNuibari(previous, recalled: false);
                }

                NuibariState threaded = new(
                    owner.EntityId,
                    entity.EntityId,
                    restoredStack
                );
                nuibariStates[owner.EntityId] = threaded;
                if (restoredStack != null)
                {
                    restoredStack.Attributes.SetBool(
                        LegendarySpearItem.NuibariOutAttribute,
                        true
                    );
                }
            }
        }

        private ProjectileState EnsureProjectileState(
            Entity entity,
            IProjectile projectile,
            string? weaponCode)
        {
            if (projectileStates.TryGetValue(
                    entity.EntityId,
                    out ProjectileState? existing))
            {
                return existing;
            }

            LegendaryProjectileKind kind =
                weaponCode switch
                {
                    FireLanceCode =>
                        LegendaryProjectileKind.FireLance,
                    NuibariCode =>
                        LegendaryProjectileKind.Nuibari,
                    _ =>
                        LegendaryProjectileKind.UnendingDespair
                };
            ProjectileState created = new(
                entity.EntityId,
                projectile.FiredBy?.EntityId ?? 0,
                kind,
                api.World.ElapsedMilliseconds +
                    (kind ==
                        LegendaryProjectileKind.UnendingDespair
                            ? DespairLifetimeMilliseconds
                            : 30000),
                projectile.WeaponStack
            );
            projectileStates[entity.EntityId] = created;

            if (kind == LegendaryProjectileKind.Nuibari &&
                created.OwnerEntityId != 0 &&
                !nuibariStates.ContainsKey(
                    created.OwnerEntityId))
            {
                nuibariStates[created.OwnerEntityId] =
                    new NuibariState(
                        created.OwnerEntityId,
                        entity.EntityId,
                        created.RestoredStack
                    );
            }

            return created;
        }

        private void EmbedDespairSpear(
            ProjectileState state,
            Entity target)
        {
            state.TargetEntityId = target.EntityId;
            long resetTo =
                api.World.ElapsedMilliseconds +
                DespairLifetimeMilliseconds;
            foreach (ProjectileState other in
                projectileStates.Values)
            {
                if (other.Kind ==
                        LegendaryProjectileKind.UnendingDespair &&
                    other.OwnerEntityId ==
                        state.OwnerEntityId &&
                    other.TargetEntityId ==
                        target.EntityId)
                {
                    other.ExpiresAtMs = resetTo;
                }
            }
            UpdateDespairStackCounts();
        }

        private void ResolveNuibariPierce(
            ProjectileState projectileState,
            Entity firstTarget)
        {
            if (projectileState.NuibariPierceResolved)
            {
                if (nuibariStates.TryGetValue(
                        projectileState.OwnerEntityId,
                        out NuibariState? existing))
                {
                    BindNuibariTarget(existing, firstTarget);
                }
                return;
            }
            projectileState.NuibariPierceResolved = true;

            Entity? projectileEntity =
                api.World.GetEntityById(
                    projectileState.ProjectileEntityId
                );
            EntityPlayer? owner =
                api.World.GetEntityById(
                    projectileState.OwnerEntityId
                ) as EntityPlayer;
            if (projectileEntity == null || owner == null)
            {
                return;
            }

            if (!nuibariStates.TryGetValue(
                    owner.EntityId,
                    out NuibariState? threaded))
            {
                threaded = new NuibariState(
                    owner.EntityId,
                    projectileEntity.EntityId,
                    projectileState.RestoredStack
                );
                nuibariStates[owner.EntityId] = threaded;
            }
            BindNuibariTarget(threaded, firstTarget);

            Vec3d start = owner.Pos.XYZ.AddCopy(
                owner.LocalEyePos.X,
                owner.LocalEyePos.Y,
                owner.LocalEyePos.Z
            );
            Vec3d direction =
                projectileEntity.Pos.XYZ.SubCopy(start);
            if (direction.LengthSq() < 0.001)
            {
                Vec3f view = owner.Pos.GetViewVector();
                direction.Set(view.X, view.Y, view.Z);
            }
            direction.Normalize();
            Vec3d end = start.AddCopy(
                direction.X * NuibariMaximumRange,
                direction.Y * NuibariMaximumRange,
                direction.Z * NuibariMaximumRange
            );

            BlockSelection? blockSelection = null;
            EntitySelection? ignoredEntity = null;
            api.World.RayTraceForSelection(
                start,
                end,
                ref blockSelection,
                ref ignoredEntity,
                (position, block) => true,
                entity => false
            );
            if (blockSelection != null)
            {
                Vec3d blockHit =
                    new(
                        blockSelection.Position.X +
                            blockSelection.HitPosition.X,
                        blockSelection.Position.Y +
                            blockSelection.HitPosition.Y,
                        blockSelection.Position.Z +
                            blockSelection.HitPosition.Z
                    );
                double blockDistance =
                    start.DistanceTo(blockHit);
                end = start.AddCopy(
                    direction.X * blockDistance,
                    direction.Y * blockDistance,
                    direction.Z * blockDistance
                );
            }

            double segmentLength = start.DistanceTo(end);
            Vec3d segmentCenter = new(
                (start.X + end.X) * 0.5,
                (start.Y + end.Y) * 0.5,
                (start.Z + end.Z) * 0.5
            );
            foreach (Entity target in api.World.GetEntitiesAround(
                    segmentCenter,
                    (float)(segmentLength * 0.5 + 1),
                    (float)(segmentLength * 0.5 + 1),
                    entity =>
                        IsCombatTarget(owner, entity) &&
                        entity.EntityId != firstTarget.EntityId
                )
                .Select(entity => new
                {
                    Entity = entity,
                    Projection = ProjectionAlongSegment(
                        start,
                        end,
                        entity.Pos.XYZ
                    ),
                    Distance = DistanceToSegment(
                        start,
                        end,
                        entity.Pos.XYZ
                    )
                })
                .Where(candidate =>
                    candidate.Projection >= 0 &&
                    candidate.Projection <= 1 &&
                    candidate.Distance <=
                        NuibariHitRadius)
                .OrderBy(candidate =>
                    candidate.Projection)
                .Select(candidate =>
                    candidate.Entity))
            {
                target.ReceiveDamage(
                    new DamageSource
                    {
                        Source = EnumDamageSource.Player,
                        SourceEntity = owner,
                        Type = EnumDamageType.PiercingAttack,
                        DamageTier = 5,
                        HitPosition =
                            target.Pos.XYZ.SubCopy(
                                owner.Pos.XYZ
                            )
                    },
                    NuibariPierceDamage
                );
            }

            projectileEntity.Pos.SetPosWithDimension(end);
            projectileEntity.PositionBeforeFalling.Set(
                end.X,
                end.Y,
                end.Z
            );
        }

        private void BindNuibariTarget(
            NuibariState state,
            Entity target)
        {
            if (!target.Alive ||
                state.BoundEntityIds.Contains(target.EntityId))
            {
                return;
            }

            state.BoundEntityIds.Add(target.EntityId);
            SetWatchedInt(
                target,
                NuibariBoundAttribute,
                1
            );
        }

        private void DetonateFireLance(
            ProjectileState state,
            Vec3d center)
        {
            if (state.Detonated)
            {
                return;
            }
            state.Detonated = true;

            Entity? owner =
                api.World.GetEntityById(state.OwnerEntityId);
            Entity? projectile =
                api.World.GetEntityById(
                    state.ProjectileEntityId
                );
            foreach (Entity target in api.World.GetEntitiesAround(
                center,
                FireLanceRadius,
                FireLanceRadius,
                entity =>
                    entity.Alive &&
                    entity.IsInteractable &&
                    entity is EntityAgent
            ))
            {
                double distance =
                    target.Pos.XYZ.DistanceTo(center);
                if (distance > FireLanceRadius)
                {
                    continue;
                }

                float progress = (float)Math.Clamp(
                    distance / FireLanceRadius,
                    0,
                    1
                );
                float damage = GameMath.Lerp(
                    FireLanceMaximumAoeDamage,
                    FireLanceMinimumAoeDamage,
                    progress
                );
                target.ReceiveDamage(
                    new DamageSource
                    {
                        Source = EnumDamageSource.Explosion,
                        SourceEntity = projectile ?? owner,
                        CauseEntity = owner,
                        SourcePos = center,
                        Type = EnumDamageType.Fire,
                        DamageTier = 5,
                        KnockbackStrength =
                            0.6f * (1f - progress)
                    },
                    damage
                );
            }

            SpawnBurstParticles(
                center,
                unchecked((int)0xffff5b19),
                90,
                1.4f
            );
            SpawnBurstParticles(
                center,
                unchecked((int)0xffffcf45),
                55,
                0.9f
            );
            api.World.PlaySoundAt(
                new AssetLocation("game:sounds/effect/large-explosion"),
                center.X,
                center.Y,
                center.Z,
                null,
                false,
                48,
                1.2f
            );
        }

        private void SpawnShapeEcho(EntityPlayer player)
        {
            EntityProperties? type = api.World.GetEntityType(
                new AssetLocation(
                    "apprentice",
                    "shapesplitter-echo"
                )
            );
            if (type == null)
            {
                api.Logger.Error(
                    "[Apprentice] Shapesplitter echo entity is missing."
                );
                return;
            }

            Entity entity =
                api.World.ClassRegistry.CreateEntity(type);
            entity.Pos.SetFrom(player.Pos);
            entity.Pos.Yaw = player.Pos.Yaw;
            entity.PositionBeforeFalling.Set(
                entity.Pos.X,
                entity.Pos.Y,
                entity.Pos.Z
            );

            Vec3f view = player.Pos.GetViewVector();
            Vec3d horizontal =
                new(view.X, 0, view.Z);
            if (horizontal.LengthSq() < 0.001)
            {
                horizontal.Set(0, 0, 1);
            }
            horizontal.Normalize();

            entity.WatchedAttributes.SetLong(
                EntityLegendaryEcho.ExpiresAtAttribute,
                api.World.ElapsedMilliseconds +
                    ShapeSplitterDurationMilliseconds
            );
            entity.WatchedAttributes.SetDouble(
                EntityLegendaryEcho.DirectionXAttribute,
                horizontal.X
            );
            entity.WatchedAttributes.SetDouble(
                EntityLegendaryEcho.DirectionZAttribute,
                horizontal.Z
            );
            entity.WatchedAttributes.SetLong(
                EntityLegendaryEcho.OwnerEntityIdAttribute,
                player.EntityId
            );
            ITreeAttribute? skinConfig =
                player.WatchedAttributes.GetTreeAttribute(
                    "skinConfig"
                );
            if (skinConfig != null)
            {
                entity.WatchedAttributes.SetAttribute(
                    "skinConfig",
                    skinConfig.Clone()
                );
            }
            api.World.SpawnEntity(entity);
        }

        private bool CanActivate(
            EntityPlayer player,
            string ability,
            int cooldownMilliseconds)
        {
            long now = api.World.ElapsedMilliseconds;
            CooldownKey key = new(
                player.EntityId,
                ability
            );
            if (cooldowns.TryGetValue(
                    key,
                    out long readyAt) &&
                readyAt > now)
            {
                return false;
            }

            cooldowns[key] =
                now + cooldownMilliseconds;
            return true;
        }

        private void SetInvisibility(
            EntityPlayer player,
            InvisibleSource source,
            int durationMilliseconds)
        {
            long expiresAt =
                api.World.ElapsedMilliseconds +
                durationMilliseconds;
            invisibleStates[player.EntityId] =
                new InvisibleState(
                    player.EntityId,
                    source,
                    expiresAt,
                    player.RightHandItemSlot?.Itemstack
                );
            ItemStack? heldStack =
                player.RightHandItemSlot?.Itemstack;
            if (heldStack != null)
            {
                heldStack.Attributes.SetLong(
                    LegendaryWeaponRuntime
                        .InvisibleHeldUntilAttribute,
                    expiresAt
                );
                player.RightHandItemSlot?.MarkDirty();
            }
            player.WatchedAttributes.SetLong(
                LegendaryWeaponRuntime
                    .InvisibleUntilAttribute,
                expiresAt
            );
            player.WatchedAttributes.SetString(
                LegendaryWeaponRuntime
                    .InvisibleSourceAttribute,
                source.ToString()
            );
            player.WatchedAttributes.MarkPathDirty(
                LegendaryWeaponRuntime
                    .InvisibleUntilAttribute
            );
            player.WatchedAttributes.MarkPathDirty(
                LegendaryWeaponRuntime
                    .InvisibleSourceAttribute
            );
        }

        private void ClearInvisibility(
            EntityPlayer player)
        {
            if (invisibleStates.Remove(
                    player.EntityId,
                    out InvisibleState? state) &&
                state.ActivatingStack != null)
            {
                state.ActivatingStack.Attributes.SetLong(
                    LegendaryWeaponRuntime
                        .InvisibleHeldUntilAttribute,
                    0
                );
            }
            player.WatchedAttributes.SetLong(
                LegendaryWeaponRuntime
                    .InvisibleUntilAttribute,
                0
            );
            player.WatchedAttributes.SetString(
                LegendaryWeaponRuntime
                    .InvisibleSourceAttribute,
                string.Empty
            );
            player.WatchedAttributes.MarkPathDirty(
                LegendaryWeaponRuntime
                    .InvisibleUntilAttribute
            );
            player.WatchedAttributes.MarkPathDirty(
                LegendaryWeaponRuntime
                    .InvisibleSourceAttribute
            );
            player.RightHandItemSlot?.MarkDirty();
        }

        private void OnTick(float deltaTime)
        {
            if (disposed)
            {
                return;
            }

            long now = api.World.ElapsedMilliseconds;
            TickPoison(now);
            TickBleeding(now);
            TickProjectiles(now);
            TickNuibari(now);
            TickInvisibility(now);
            RemoveExpiredCooldowns(now);
            RemoveExpiredBleedSuppressions(now);
        }

        private void TickPoison(long now)
        {
            foreach ((PoisonKey key, PoisonState state) in
                poisonStates.ToArray())
            {
                Entity? target =
                    api.World.GetEntityById(
                        state.TargetEntityId
                    );
                if (target == null || !target.Alive ||
                    state.ExpiresAtMs <= now)
                {
                    poisonStates.Remove(key);
                    if (target != null)
                    {
                        SetWatchedInt(
                            target,
                            PoisonStacksAttribute,
                            SumPoisonStacks(
                                target.EntityId
                            )
                        );
                    }
                    continue;
                }

                if (state.NextPulseAtMs > now)
                {
                    continue;
                }
                state.NextPulseAtMs = now + 1000;
                target.ReceiveDamage(
                    new DamageSource
                    {
                        Source =
                            EnumDamageSource.Internal,
                        Type = EnumDamageType.Poison,
                        DamageTier = 0
                    },
                    PoisonDamagePerStack *
                        state.Stacks
                );
            }
        }

        private void TickBleeding(long now)
        {
            foreach ((BleedKey key, BleedState state) in
                bleedStates.ToArray())
            {
                Entity? target =
                    api.World.GetEntityById(
                        state.TargetEntityId
                    );
                if (target == null || !target.Alive ||
                    state.ExpiresAtMs <= now)
                {
                    bleedStates.Remove(key);
                    if (target != null)
                    {
                        SetWatchedInt(
                            target,
                            BleedStacksAttribute,
                            HighestBleedStacks(
                                target.EntityId
                            )
                        );
                    }
                    continue;
                }

                if (state.NextPulseAtMs > now)
                {
                    continue;
                }
                state.NextPulseAtMs = now + 1000;
                target.ReceiveDamage(
                    new DamageSource
                    {
                        Source = EnumDamageSource.Bleed,
                        Type = EnumDamageType.PiercingAttack,
                        DamageTier = 0
                    },
                    BleedDamagePerStack *
                        state.Stacks
                );
            }
        }

        private void TickProjectiles(long now)
        {
            bool despairChanged = false;
            foreach (ProjectileState state in
                projectileStates.Values.ToArray())
            {
                Entity? entity =
                    api.World.GetEntityById(
                        state.ProjectileEntityId
                    );
                if (entity == null || !entity.Alive)
                {
                    projectileStates.Remove(
                        state.ProjectileEntityId
                    );
                    if (state.Kind ==
                        LegendaryProjectileKind
                            .UnendingDespair)
                    {
                        despairChanged = true;
                    }
                    continue;
                }

                if (state.Kind ==
                        LegendaryProjectileKind.FireLance &&
                    !state.Detonated &&
                    (entity.Collided ||
                     entity.OnGround ||
                     entity is IProjectile
                        {
                            Stuck: true
                        }))
                {
                    DetonateFireLance(
                        state,
                        entity.Pos.XYZ
                    );
                    projectileStates.Remove(
                        state.ProjectileEntityId
                    );
                    continue;
                }

                if (state.Kind ==
                        LegendaryProjectileKind.UnendingDespair &&
                    state.ExpiresAtMs <= now)
                {
                    DespawnProjectile(state);
                    despairChanged = true;
                }
            }

            if (despairChanged)
            {
                UpdateDespairStackCounts();
            }
        }

        private void TickNuibari(long now)
        {
            foreach (NuibariState state in
                nuibariStates.Values.ToArray())
            {
                EntityPlayer? owner =
                    api.World.GetEntityById(
                        state.OwnerEntityId
                    ) as EntityPlayer;
                Entity? projectile =
                    api.World.GetEntityById(
                        state.ProjectileEntityId
                    );
                string? heldCode =
                    owner?.RightHandItemSlot?.Itemstack?
                        .Collectible?.Code?.ToString();
                if (owner == null || !owner.Alive ||
                    projectile == null ||
                    heldCode != NuibariCode)
                {
                    EndNuibari(
                        state,
                        recalled: false
                    );
                    continue;
                }

                foreach (long targetId in
                    state.BoundEntityIds.ToArray())
                {
                    Entity? target =
                        api.World.GetEntityById(targetId);
                    if (target == null || !target.Alive)
                    {
                        state.BoundEntityIds.Remove(
                            targetId
                        );
                        continue;
                    }

                    target.Pos.Motion.X = 0;
                    target.Pos.Motion.Z = 0;
                    if (target is EntityAgent agent)
                    {
                        agent.Controls.StopAllMovement();
                    }
                }

                if (now - state.LastThreadParticleAtMs >=
                    100)
                {
                    state.LastThreadParticleAtMs = now;
                    SpawnThreadParticles(
                        owner,
                        projectile
                    );
                }
            }
        }

        private void TickInvisibility(long now)
        {
            foreach (InvisibleState state in
                invisibleStates.Values.ToArray())
            {
                EntityPlayer? player =
                    api.World.GetEntityById(
                        state.PlayerEntityId
                    ) as EntityPlayer;
                bool attacked =
                    state.Source ==
                        InvisibleSource.ShapeSplitter &&
                    player?.Controls.LeftMouseDown == true;
                string requiredItem =
                    state.Source ==
                        InvisibleSource.StealthDagger
                            ? StealthDaggerCode
                            : ShapeSplitterCode;
                bool swapped =
                    player?.RightHandItemSlot?.Itemstack?
                        .Collectible?.Code?.ToString() !=
                    requiredItem;
                if (player == null || !player.Alive ||
                    state.ExpiresAtMs <= now ||
                    attacked ||
                    swapped)
                {
                    if (player != null)
                    {
                        ClearInvisibility(player);
                    }
                    else
                    {
                        invisibleStates.Remove(
                            state.PlayerEntityId
                        );
                    }
                }
            }
        }

        private void EndNuibari(
            NuibariState state,
            bool recalled)
        {
            nuibariStates.Remove(state.OwnerEntityId);
            foreach (long targetId in
                state.BoundEntityIds)
            {
                Entity? target =
                    api.World.GetEntityById(targetId);
                if (target != null)
                {
                    SetWatchedInt(
                        target,
                        NuibariBoundAttribute,
                        0
                    );
                }
            }

            if (state.RestoredStack != null)
            {
                state.RestoredStack.Attributes.SetBool(
                    LegendarySpearItem.NuibariOutAttribute,
                    false
                );
            }

            if (projectileStates.TryGetValue(
                    state.ProjectileEntityId,
                    out ProjectileState? projectileState))
            {
                DespawnProjectile(projectileState);
            }
            else
            {
                Entity? projectile =
                    api.World.GetEntityById(
                        state.ProjectileEntityId
                    );
                projectile?.Die(
                    EnumDespawnReason.Expire,
                    null
                );
            }

            EntityPlayer? owner =
                api.World.GetEntityById(
                    state.OwnerEntityId
                ) as EntityPlayer;
            owner?.RightHandItemSlot?.MarkDirty();
            if (recalled && owner != null)
            {
                api.World.PlaySoundAt(
                    new AssetLocation(
                        "game:sounds/effect/swoosh"
                    ),
                    owner,
                    null,
                    false,
                    24,
                    0.7f
                );
            }
        }

        private void DespawnProjectile(
            ProjectileState state)
        {
            projectileStates.Remove(
                state.ProjectileEntityId
            );
            Entity? projectile =
                api.World.GetEntityById(
                    state.ProjectileEntityId
                );
            projectile?.Die(
                EnumDespawnReason.Expire,
                null
            );
        }

        private void SpawnThreadParticles(
            EntityPlayer owner,
            Entity projectile)
        {
            Vec3d from = owner.Pos.XYZ.AddCopy(
                0,
                owner.LocalEyePos.Y * 0.72,
                0
            );
            Vec3d to = projectile.Pos.XYZ;
            double length = from.DistanceTo(to);
            int samples = Math.Clamp(
                (int)Math.Ceiling(length * 2.5),
                2,
                48
            );
            for (int index = 1;
                index < samples;
                index++)
            {
                double t = index / (double)samples;
                Vec3d position = new(
                    GameMath.Lerp(from.X, to.X, t),
                    GameMath.Lerp(from.Y, to.Y, t),
                    GameMath.Lerp(from.Z, to.Z, t)
                );
                api.World.SpawnParticles(
                    1,
                    unchecked((int)0xffb51d2c),
                    position,
                    position,
                    new Vec3f(),
                    new Vec3f(),
                    0.18f,
                    0,
                    0.055f,
                    EnumParticleModel.Quad,
                    null
                );
            }
        }

        private void SpawnBurstParticles(
            Vec3d center,
            int color,
            int quantity,
            float scale)
        {
            api.World.SpawnParticles(
                quantity,
                color,
                center.AddCopy(-0.35, -0.15, -0.35),
                center.AddCopy(0.35, 0.35, 0.35),
                new Vec3f(-1.2f, 0.2f, -1.2f),
                new Vec3f(1.2f, 2.2f, 1.2f),
                1.15f,
                0.35f,
                scale,
                EnumParticleModel.Quad,
                null
            );
        }

        private void UpdateDespairStackCounts()
        {
            foreach (IGrouping<long, ProjectileState> group in
                projectileStates.Values
                    .Where(state =>
                        state.Kind ==
                            LegendaryProjectileKind
                                .UnendingDespair &&
                        state.TargetEntityId != 0)
                    .GroupBy(state =>
                        state.TargetEntityId))
            {
                Entity? target =
                    api.World.GetEntityById(group.Key);
                if (target != null)
                {
                    SetWatchedInt(
                        target,
                        DespairSpearsAttribute,
                        group.Count()
                    );
                }
            }

            foreach (Entity entity in
                api.World.LoadedEntities.Values)
            {
                if (entity.WatchedAttributes.GetInt(
                        DespairSpearsAttribute,
                        0) > 0 &&
                    !projectileStates.Values.Any(state =>
                        state.Kind ==
                            LegendaryProjectileKind
                                .UnendingDespair &&
                        state.TargetEntityId ==
                            entity.EntityId))
                {
                    SetWatchedInt(
                        entity,
                        DespairSpearsAttribute,
                        0
                    );
                }
            }
        }

        private int SumPoisonStacks(long targetEntityId) =>
            poisonStates.Values
                .Where(state =>
                    state.TargetEntityId ==
                        targetEntityId)
                .Aggregate(
                    0,
                    (total, state) =>
                        total > int.MaxValue -
                            state.Stacks
                            ? int.MaxValue
                            : total + state.Stacks
                );

        private int HighestBleedStacks(long targetEntityId) =>
            bleedStates.Values
                .Where(state =>
                    state.TargetEntityId ==
                        targetEntityId)
                .Select(state => state.Stacks)
                .DefaultIfEmpty(0)
                .Max();

        private static void SetWatchedInt(
            Entity entity,
            string path,
            int value)
        {
            entity.WatchedAttributes.SetInt(path, value);
            entity.WatchedAttributes.MarkPathDirty(path);
        }

        private static bool IsCombatTarget(
            Entity owner,
            Entity target) =>
            target.EntityId != owner.EntityId &&
            target.Alive &&
            target.IsInteractable &&
            target is EntityAgent;

        private static double ProjectionAlongSegment(
            Vec3d start,
            Vec3d end,
            Vec3d point)
        {
            Vec3d segment = end.SubCopy(start);
            double lengthSquared = segment.LengthSq();
            if (lengthSquared < 0.000001)
            {
                return 0;
            }
            return point.SubCopy(start).Dot(segment) /
                lengthSquared;
        }

        private static double DistanceToSegment(
            Vec3d start,
            Vec3d end,
            Vec3d point)
        {
            double t = Math.Clamp(
                ProjectionAlongSegment(start, end, point),
                0,
                1
            );
            Vec3d nearest = new(
                start.X + (end.X - start.X) * t,
                start.Y + (end.Y - start.Y) * t,
                start.Z + (end.Z - start.Z) * t
            );
            return nearest.DistanceTo(point);
        }

        private void RemoveExpiredCooldowns(long now)
        {
            foreach (CooldownKey key in cooldowns
                .Where(entry => entry.Value <= now)
                .Select(entry => entry.Key)
                .ToArray())
            {
                cooldowns.Remove(key);
            }
        }

        private void RemoveExpiredBleedSuppressions(long now)
        {
            foreach ((BleedKey key, long expiresAt) in
                suppressNextBleed.ToArray())
            {
                if (expiresAt < now)
                {
                    suppressNextBleed.Remove(key);
                }
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            api.Event.UnregisterGameTickListener(
                tickListenerId
            );

            foreach (NuibariState state in
                nuibariStates.Values.ToArray())
            {
                EndNuibari(state, recalled: false);
            }
            foreach (InvisibleState state in
                invisibleStates.Values.ToArray())
            {
                if (api.World.GetEntityById(
                        state.PlayerEntityId) is
                    EntityPlayer player)
                {
                    ClearInvisibility(player);
                }
            }

            poisonStates.Clear();
            bleedStates.Clear();
            projectileStates.Clear();
            nuibariStates.Clear();
            invisibleStates.Clear();
            cooldowns.Clear();
            suppressNextBleed.Clear();
            LegendaryWeaponRuntime.Clear(this);
        }

        private readonly record struct PoisonKey(
            long AttackerEntityId,
            long TargetEntityId
        );

        private readonly record struct BleedKey(
            long AttackerEntityId,
            long TargetEntityId
        );

        private readonly record struct CooldownKey(
            long PlayerEntityId,
            string Ability
        );

        private sealed class PoisonState
        {
            public PoisonState(
                long attackerEntityId,
                long targetEntityId)
            {
                AttackerEntityId = attackerEntityId;
                TargetEntityId = targetEntityId;
            }

            public long AttackerEntityId { get; }
            public long TargetEntityId { get; }
            public int Stacks { get; set; }
            public long ExpiresAtMs { get; set; }
            public long NextPulseAtMs { get; set; }
        }

        private sealed class BleedState
        {
            public BleedState(
                long attackerEntityId,
                long targetEntityId)
            {
                AttackerEntityId = attackerEntityId;
                TargetEntityId = targetEntityId;
            }

            public long AttackerEntityId { get; }
            public long TargetEntityId { get; }
            public int Stacks { get; set; }
            public long ExpiresAtMs { get; set; }
            public long NextPulseAtMs { get; set; }
        }

        private sealed class ProjectileState
        {
            public ProjectileState(
                long projectileEntityId,
                long ownerEntityId,
                LegendaryProjectileKind kind,
                long expiresAtMs,
                ItemStack? restoredStack)
            {
                ProjectileEntityId = projectileEntityId;
                OwnerEntityId = ownerEntityId;
                Kind = kind;
                ExpiresAtMs = expiresAtMs;
                RestoredStack = restoredStack;
            }

            public long ProjectileEntityId { get; }
            public long OwnerEntityId { get; }
            public LegendaryProjectileKind Kind { get; }
            public ItemStack? RestoredStack { get; }
            public long TargetEntityId { get; set; }
            public long ExpiresAtMs { get; set; }
            public bool Detonated { get; set; }
            public bool NuibariPierceResolved { get; set; }
        }

        private sealed class NuibariState
        {
            public NuibariState(
                long ownerEntityId,
                long projectileEntityId,
                ItemStack? restoredStack)
            {
                OwnerEntityId = ownerEntityId;
                ProjectileEntityId = projectileEntityId;
                RestoredStack = restoredStack;
            }

            public long OwnerEntityId { get; }
            public long ProjectileEntityId { get; }
            public ItemStack? RestoredStack { get; }
            public HashSet<long> BoundEntityIds { get; } =
                new();
            public long LastThreadParticleAtMs { get; set; }
        }

        private enum InvisibleSource
        {
            StealthDagger,
            ShapeSplitter
        }

        private sealed record InvisibleState(
            long PlayerEntityId,
            InvisibleSource Source,
            long ExpiresAtMs,
            ItemStack? ActivatingStack
        );
    }

    public sealed class EntityLegendaryEcho : EntityAgent
    {
        internal const string ExpiresAtAttribute =
            "apprentice:echoExpiresAtMs";
        internal const string DirectionXAttribute =
            "apprentice:echoDirectionX";
        internal const string DirectionZAttribute =
            "apprentice:echoDirectionZ";
        internal const string OwnerEntityIdAttribute =
            "apprentice:echoOwnerEntityId";

        private const double SpeedPerTick = 0.085;

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);
            if (World.Side != EnumAppSide.Server)
            {
                return;
            }

            long expiresAt =
                WatchedAttributes.GetLong(
                    ExpiresAtAttribute,
                    0
                );
            if (expiresAt == 0 ||
                World.ElapsedMilliseconds >= expiresAt)
            {
                Die(EnumDespawnReason.Expire, null);
                return;
            }

            double directionX =
                WatchedAttributes.GetDouble(
                    DirectionXAttribute,
                    0
                );
            double directionZ =
                WatchedAttributes.GetDouble(
                    DirectionZAttribute,
                    1
                );
            Pos.Motion.X =
                directionX * SpeedPerTick;
            Pos.Motion.Z =
                directionZ * SpeedPerTick;
            AnimManager.StartAnimation("run");
        }

        public override bool ReceiveDamage(
            DamageSource damageSource,
            float damage)
        {
            return false;
        }

        public override bool CanCollect(Entity byEntity) =>
            false;
    }

    public sealed class EntityUncollectableLegendaryProjectile :
        EntityProjectile
    {
        public override bool CanCollect(Entity byEntity) =>
            false;
    }

    internal static class LegendaryClientPatches
    {
        private static readonly string[] RenderMethods =
        {
            "DoRender3DOpaque",
            "DoRender3DAfterOIT",
            "DoRender3DOpaqueBatched"
        };

        internal static void Install(
            Harmony harmony,
            ICoreClientAPI api)
        {
            Type? rendererType =
                AccessTools.TypeByName(
                    "Vintagestory.GameContent.EntityShapeRenderer"
                );
            if (rendererType == null)
            {
                api.Logger.Warning(
                    "[Apprentice] Legendary invisibility rendering was disabled because EntityShapeRenderer was not found."
                );
                return;
            }

            foreach (string methodName in RenderMethods)
            {
                MethodInfo? method =
                    AccessTools.Method(
                        rendererType,
                        methodName
                    );
                if (method == null)
                {
                    continue;
                }

                harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(
                        typeof(LegendaryClientPatches),
                        nameof(EntityRenderPrefix)
                    )
                );
            }
        }

        public static bool EntityRenderPrefix(
            object __instance)
        {
            Entity? entity = Traverse.Create(__instance)
                .Field("entity")
                .GetValue<Entity>();
            if (entity == null)
            {
                return true;
            }

            long invisibleUntil =
                entity.WatchedAttributes.GetLong(
                    LegendaryWeaponRuntime
                        .InvisibleUntilAttribute,
                    0
                );
            return invisibleUntil <=
                entity.World.ElapsedMilliseconds;
        }
    }

    internal static class LegendaryInfoPatch
    {
        public static void Postfix(
            Entity __instance,
            ref string __result)
        {
            List<string> lines = new();
            int poison =
                __instance.WatchedAttributes.GetInt(
                    "apprentice:legendaryPoisonStacks",
                    0
                );
            int bleed =
                __instance.WatchedAttributes.GetInt(
                    "apprentice:legendaryBleedStacks",
                    0
                );
            int spears =
                __instance.WatchedAttributes.GetInt(
                    "apprentice:despairSpearStacks",
                    0
                );
            bool bound =
                __instance.WatchedAttributes.GetInt(
                    "apprentice:nuibariBound",
                    0
                ) > 0;

            if (poison > 0)
            {
                lines.Add(
                    $"Hidden poison stacks: {poison}"
                );
            }
            if (bleed > 0)
            {
                lines.Add(
                    $"War Axe bleeding: {bleed}/5"
                );
            }
            if (spears > 0)
            {
                lines.Add(
                    $"Unending Despair spears: {spears}"
                );
            }
            if (bound)
            {
                lines.Add("Bound by Nuibari");
            }

            if (lines.Count > 0)
            {
                __result +=
                    Environment.NewLine +
                    string.Join(
                        Environment.NewLine,
                        lines
                    );
            }
        }
    }
}
