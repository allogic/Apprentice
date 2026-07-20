using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace Apprentice
{
    internal sealed record PoisonProjectileHit(
        string ArrowCode,
        string PoisonId,
        long AttackerEntityId
    );

    internal static class PoisonRuntime
    {
        public static PoisonEffectSystem? System { get; set; }

        public static void ApplyConfirmedProjectileHit(
            Entity target,
            PoisonProjectileHit? hit)
        {
            System?.ApplyConfirmedProjectileHit(target, hit);
        }

        public static PoisonProjectileHit? CaptureProjectileHit(
            DamageSource damageSource) =>
            PoisonEffectSystem.CaptureProjectileHit(damageSource);

        public static void ApplyConsumedPoison(
            Entity target,
            string poisonId,
            long poisonerEntityId) =>
            System?.ApplyPoison(target, poisonId, poisonerEntityId, false);
    }

    internal sealed class ActivePoison
    {
        public long TargetEntityId { get; set; }
        public long AttackerEntityId { get; set; }
        public string PoisonId { get; set; } = string.Empty;
        public double DamagePerSecond { get; set; }
        public double RemainingSeconds { get; set; }
        public double MaximumRemainingSeconds { get; set; }
    }

    /// <summary>
    /// Server-authoritative, arrow-only poison scheduler. Dedicated arrow
    /// variants avoid relying on unverified arbitrary stack-attribute transfer
    /// through bow/projectile/pickup serialization.
    /// </summary>
    internal sealed class PoisonEffectSystem : IDisposable
    {
        private const string RootKey = "apprentice:poison";
        private readonly ICoreServerAPI api;
        private readonly Dictionary<string, PoisonDefinition> byArrowCode;
        private readonly Dictionary<long, ActivePoison> active = new();
        private readonly long listenerId;
        private bool disposed;

        public PoisonEffectSystem(
            ICoreServerAPI api,
            ApprenticeContentRegistry registry)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            byArrowCode = registry.Poisons.ToDictionary(
                poison => poison.ArrowCode,
                StringComparer.OrdinalIgnoreCase
            );
            api.Event.OnEntityLoaded += OnEntityLoaded;
            listenerId = api.Event.RegisterGameTickListener(OnPoisonTick, 1000);
            PoisonRuntime.System = this;
        }

        internal static PoisonProjectileHit? CaptureProjectileHit(
            DamageSource damageSource)
        {
            if (damageSource == null) return null;

            Entity? sourceEntity = damageSource.SourceEntity;
            Entity? causeEntity = damageSource.GetCauseEntity();
            IProjectile? projectile = sourceEntity as IProjectile ??
                causeEntity as IProjectile;
            ItemStack? projectileStack = projectile?.ProjectileStack ??
                ReadProjectileStack(sourceEntity) ??
                ReadProjectileStack(causeEntity) ??
                ReadProjectileStack(damageSource);
            AssetLocation? arrowLocation = projectileStack?.Collectible?.Code;
            if (arrowLocation == null) return null;
            string arrowCode =
                $"{arrowLocation.Domain}:{arrowLocation.Path}";
            string poisonId = projectileStack?.Collectible?.Attributes?
                ["apprenticePoison"].AsString(string.Empty) ?? string.Empty;
            if (string.IsNullOrEmpty(poisonId) &&
                !arrowCode.StartsWith("apprentice:arrow-poison-", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            Entity? attacker = projectile?.FiredBy ??
                (sourceEntity is IProjectile ? null : sourceEntity) ??
                (causeEntity is IProjectile ? null : causeEntity);
            return new PoisonProjectileHit(
                arrowCode,
                poisonId,
                attacker?.EntityId ?? 0
            );
        }

        public void ApplyConfirmedProjectileHit(
            Entity target,
            PoisonProjectileHit? hit)
        {
            if (target == null || !target.Alive || hit == null)
            {
                return;
            }

            string arrowCode = hit.ArrowCode;
            if (!byArrowCode.TryGetValue(arrowCode, out PoisonDefinition? poison))
            {
                poison = byArrowCode.Values.FirstOrDefault(value =>
                    string.Equals(
                        value.Id,
                        hit.PoisonId,
                        StringComparison.OrdinalIgnoreCase
                    ));
                if (poison == null) return;
            }

            Entity? attacker = api.World.GetEntityById(hit.AttackerEntityId);
            if (attacker?.EntityId == target.EntityId ||
                target.Properties.Attributes?["apprenticePoisonImmune"].AsBool(false) == true ||
                target.Properties.Attributes?["poisonImmune"].AsBool(false) == true)
            {
                return;
            }

            if (target is EntityPlayer && attacker is EntityPlayer &&
                !api.Server.Config.AllowPvP)
            {
                return;
            }

            ApplyDefinition(target, poison, attacker?.EntityId ?? 0);
            api.Logger.Notification(
                "[Apprentice] Applied {0} poison from {1} to entity {2}.",
                poison.Id, arrowCode, target.EntityId
            );
        }

        public void ApplyPoison(
            Entity target,
            string poisonId,
            long poisonerEntityId,
            bool requireUnlock)
        {
            PoisonDefinition? poison = byArrowCode.Values.FirstOrDefault(value =>
                string.Equals(value.Id, poisonId, StringComparison.OrdinalIgnoreCase));
            if (poison == null || target == null || !target.Alive) return;

            Entity? poisoner = api.World.GetEntityById(poisonerEntityId);
            if (target.Properties.Attributes?["apprenticePoisonImmune"].AsBool(false) == true ||
                target.Properties.Attributes?["poisonImmune"].AsBool(false) == true)
            {
                return;
            }

            if (target is EntityPlayer && poisoner is EntityPlayer &&
                poisoner.EntityId != target.EntityId && !api.Server.Config.AllowPvP)
            {
                return;
            }

            if (requireUnlock && !string.IsNullOrWhiteSpace(poison.RequiredDiscovery) &&
                ((poisoner as EntityPlayer)?.Player is not IServerPlayer player ||
                 !HiddenClassData.IsUnlocked(player.Entity, poison.RequiredDiscovery!)))
            {
                return;
            }

            ApplyDefinition(target, poison, poisonerEntityId);
        }

        private void ApplyDefinition(
            Entity target,
            PoisonDefinition poison,
            long attackerEntityId)
        {
            ActivePoison? current = GetOrLoad(target);

            if (current != null && current.RemainingSeconds > 0)
            {
                if (poison.DamagePerSecond < current.DamagePerSecond)
                {
                    return;
                }

                if (Math.Abs(poison.DamagePerSecond - current.DamagePerSecond) < 0.00001)
                {
                    current.RemainingSeconds = Math.Min(
                        current.MaximumRemainingSeconds,
                        Math.Max(current.RemainingSeconds, poison.DurationSeconds)
                    );
                    current.AttackerEntityId = attackerEntityId;
                    Save(target, current, poison.SchemaVersion);
                    active[target.EntityId] = current;
                    return;
                }
            }

            ActivePoison replacement = new()
            {
                TargetEntityId = target.EntityId,
                AttackerEntityId = attackerEntityId,
                PoisonId = poison.Id,
                DamagePerSecond = poison.DamagePerSecond,
                RemainingSeconds = poison.DurationSeconds,
                MaximumRemainingSeconds = poison.MaximumDurationSeconds
            };
            active[target.EntityId] = replacement;
            Save(target, replacement, poison.SchemaVersion);
            DamagePulse(target, replacement);
        }

        private static ItemStack? ReadProjectileStack(object? entity)
        {
            if (entity == null) return null;

            Type type = entity.GetType();
            foreach (string name in new[] { "ProjectileStack", "projectileStack" })
            {
                PropertyInfo? property = type.GetProperty(
                    name,
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.IgnoreCase
                );
                if (property?.GetValue(entity) is ItemStack propertyStack)
                {
                    return propertyStack;
                }

                FieldInfo? field = type.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.IgnoreCase
                );
                if (field?.GetValue(entity) is ItemStack fieldStack)
                {
                    return fieldStack;
                }
            }

            return null;
        }

        private void OnPoisonTick(float deltaTime)
        {
            if (active.Count == 0) return;
            List<long> remove = new();

            foreach (ActivePoison poison in active.Values.ToArray())
            {
                Entity? target = api.World.GetEntityById(poison.TargetEntityId);
                if (target == null)
                {
                    // The entity state is persisted on the entity itself and
                    // will be rehydrated by OnEntityLoaded. Do not scan chunks.
                    remove.Add(poison.TargetEntityId);
                    continue;
                }

                if (!target.Alive || poison.RemainingSeconds <= 0)
                {
                    Clear(target);
                    remove.Add(poison.TargetEntityId);
                    continue;
                }

                try
                {
                    bool damaged = DamagePulse(target, poison);
                    poison.RemainingSeconds = Math.Max(0, poison.RemainingSeconds - deltaTime);
                    Save(target, poison, 1);
                    api.Logger.Debug(
                        "[Apprentice] Poison tick {0}: entity {1}, damage accepted={2}, remaining={3:0.0}s.",
                        poison.PoisonId, poison.TargetEntityId, damaged, poison.RemainingSeconds
                    );
                }
                catch (Exception exception)
                {
                    // One malformed/unloaded entity must not terminate the
                    // global tick listener and silently disable all poisons.
                    api.Logger.Error(
                        "[Apprentice] Poison tick failed for entity {0}: {1}",
                        poison.TargetEntityId, exception
                    );
                    remove.Add(poison.TargetEntityId);
                }
            }

            foreach (long entityId in remove)
            {
                active.Remove(entityId);
            }
        }

        private bool DamagePulse(Entity target, ActivePoison poison)
        {
            // Do not attach the shooter to a DOT pulse. Vintage Story treats
            // attributed pulses as repeat attacks and can reject them during
            // the entity's post-projectile invulnerability window.
            bool accepted = target.ReceiveDamage(
                new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = EnumDamageType.Poison,
                    DamageTier = 0
                },
                (float)poison.DamagePerSecond
            );
            api.Logger.Notification(
                "[Apprentice] Poison pulse {0} -> entity {1}: {2:0.##} damage, accepted={3}.",
                poison.PoisonId,
                target.EntityId,
                poison.DamagePerSecond,
                accepted
            );
            return accepted;
        }

        private void OnEntityLoaded(Entity entity)
        {
            ActivePoison? poison = GetOrLoad(entity);
            if (poison == null) return;

            if (!entity.Alive || poison.RemainingSeconds <= 0)
            {
                Clear(entity);
                return;
            }

            active[entity.EntityId] = poison;
        }

        private static ActivePoison? GetOrLoad(Entity entity)
        {
            ITreeAttribute? tree = entity.WatchedAttributes.GetTreeAttribute(RootKey);
            if (tree == null || tree.GetInt("schema", 0) < 1)
            {
                return null;
            }

            double damage = tree.GetDouble("damagePerSecond", 0);
            double remaining = tree.GetDouble("remainingSeconds", 0);
            if (damage <= 0 || remaining <= 0) return null;

            return new ActivePoison
            {
                TargetEntityId = entity.EntityId,
                AttackerEntityId = tree.GetLong("attackerEntityId", 0),
                PoisonId = tree.GetString("id", string.Empty),
                DamagePerSecond = damage,
                RemainingSeconds = remaining,
                MaximumRemainingSeconds = tree.GetDouble("maximumRemainingSeconds", remaining)
            };
        }

        private static void Save(
            Entity target,
            ActivePoison poison,
            int schemaVersion)
        {
            ITreeAttribute tree = target.WatchedAttributes
                .GetOrAddTreeAttribute(RootKey);
            tree.SetInt("schema", schemaVersion);
            tree.SetString("id", poison.PoisonId);
            tree.SetLong("attackerEntityId", poison.AttackerEntityId);
            tree.SetDouble("damagePerSecond", poison.DamagePerSecond);
            tree.SetDouble("remainingSeconds", poison.RemainingSeconds);
            tree.SetDouble("maximumRemainingSeconds", poison.MaximumRemainingSeconds);
            target.WatchedAttributes.MarkPathDirty(RootKey);
        }

        private static void Clear(Entity target)
        {
            target.WatchedAttributes.RemoveAttribute(RootKey);
            target.WatchedAttributes.MarkPathDirty(RootKey);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            api.Event.OnEntityLoaded -= OnEntityLoaded;
            api.Event.UnregisterGameTickListener(listenerId);
            active.Clear();
            if (ReferenceEquals(PoisonRuntime.System, this))
            {
                PoisonRuntime.System = null;
            }
        }
    }

    internal static class PoisonInfoPatch
    {
        public static void Postfix(Entity __instance, ref string __result)
        {
            ITreeAttribute? poison = __instance?.WatchedAttributes?
                .GetTreeAttribute("apprentice:poison");
            if (poison == null) return;

            double remaining = poison.GetDouble("remainingSeconds", 0);
            double damage = poison.GetDouble("damagePerSecond", 0);
            if (remaining <= 0 || damage <= 0) return;

            string id = poison.GetString("id", "poison");
            __result += $"\nPoisoned ({id}): {damage:0.##} damage/sec, {Math.Ceiling(remaining)} sec remaining";
        }
    }
}
