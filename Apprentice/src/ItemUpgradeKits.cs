using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Apprentice
{
    internal static class DurabilityUpgradeRuntime
    {
        internal const string AttributeKey = "apprentice:durabilityUpgrade20";

        public static void GetMaxDurabilityPostfix(
            ItemStack itemstack,
            ref int __result)
        {
            if (__result <= 0 || itemstack?.Attributes?.GetBool(
                AttributeKey,
                false
            ) != true)
            {
                return;
            }

            __result = Math.Max(
                __result + 1,
                (int)Math.Ceiling(__result * 1.2)
            );
        }
    }

    public sealed class ItemUpgradeKit : Item
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            if (byEntity is not EntityPlayer player)
            {
                return;
            }

            ItemSlot targetSlot = player.LeftHandItemSlot;
            ItemStack? target = targetSlot?.Itemstack;
            string category = Attributes?["upgradeCategory"]
                .AsString(string.Empty) ?? string.Empty;

            if (target == null || !MatchesCategory(target, category) ||
                target.Attributes.GetBool(
                    DurabilityUpgradeRuntime.AttributeKey,
                    false
                ))
            {
                return;
            }

            int baseMaximum = target.Collectible.GetMaxDurability(target);
            if (baseMaximum <= 0)
            {
                return;
            }

            handling = EnumHandHandling.PreventDefault;
            if (byEntity.World.Side != EnumAppSide.Server)
            {
                return;
            }

            int remaining = target.Collectible.GetRemainingDurability(target);
            target.Attributes.SetBool(
                DurabilityUpgradeRuntime.AttributeKey,
                true
            );
            int upgradedMaximum = target.Collectible.GetMaxDurability(target);
            target.Collectible.SetDurability(
                target,
                Math.Min(
                    upgradedMaximum,
                    remaining + upgradedMaximum - baseMaximum
                )
            );
            targetSlot.MarkDirty();

            if (player.Player?.WorldData?.CurrentGameMode !=
                EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
        }

        private static bool MatchesCategory(
            ItemStack stack,
            string category)
        {
            CollectibleObject collectible = stack.Collectible;
            string path = collectible.Code?.Path?.ToLowerInvariant() ??
                string.Empty;
            string type = collectible.GetType().Name.ToLowerInvariant();
            string tool = collectible.Tool?.ToString().ToLowerInvariant() ??
                string.Empty;

            bool shield = type.Contains("shield") ||
                path.Contains("shield") ||
                collectible.Attributes?["shield"].Exists == true;
            bool armor = shield || type.Contains("armor") ||
                collectible.Attributes?["protectionModifiers"].Exists == true;
            bool weapon = type.Contains("weapon") ||
                path.Contains("sword") || path.Contains("spear") ||
                path.Contains("bow") || path.Contains("crossbow") ||
                path.Contains("sling") || path.Contains("club") ||
                path.Contains("mace") || path.Contains("halberd") ||
                path.Contains("polearm") || tool is "sword" or "spear" or
                "bow" or "sling";
            bool usableTool = tool.Length > 0 && tool != "none" &&
                !weapon && !shield;

            return category.ToLowerInvariant() switch
            {
                "armor" => armor,
                "weapon" => weapon,
                "tool" => usableTool,
                _ => false
            };
        }
    }

    public sealed class ItemFirstAidKit : Item
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            Entity? target = entitySel?.Entity;
            EntityBehaviorHealth? health =
                target?.GetBehavior<EntityBehaviorHealth>();
            float healAmount = Attributes?["healAmount"].AsFloat(8) ?? 8;

            if (target == null || health == null || !target.Alive ||
                health.Health >= health.MaxHealth || healAmount <= 0)
            {
                return;
            }

            handling = EnumHandHandling.PreventDefault;
            if (byEntity.World.Side != EnumAppSide.Server)
            {
                return;
            }

            health.Health = Math.Min(
                health.MaxHealth,
                health.Health + healAmount
            );
            target.WatchedAttributes.MarkPathDirty("health");

            if (byEntity is not EntityPlayer player ||
                player.Player?.WorldData?.CurrentGameMode !=
                    EnumGameMode.Creative)
            {
                slot.Itemstack?.Collectible.DamageItem(
                    byEntity.World,
                    byEntity,
                    slot,
                    1
                );
                slot.MarkDirty();
            }
        }
    }
}
