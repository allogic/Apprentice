using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Apprentice
{
    internal static class SkillMasteryPatches
    {
        public static void GetMiningSpeedPostfix(
            Block block,
            IPlayer forPlayer,
            ref float __result)
        {
            if (block == null || forPlayer == null) return;

            EnumBlockMaterial material = block.BlockMaterial;
            if (material == EnumBlockMaterial.Wood)
            {
                __result *= (float)SkillTreeRuntime.GetWoodMiningMultiplier(forPlayer);
            }
        }

        public static void DamageItemPrefix(
            IWorldAccessor world,
            Entity byEntity,
            ItemSlot itemSlot,
            ref int amount)
        {
            if (world.Side != EnumAppSide.Server ||
                byEntity is not EntityPlayer entityPlayer ||
                entityPlayer.Player is not IServerPlayer player ||
                amount <= 0)
            {
                return;
            }

            string? code =
                itemSlot?.Itemstack?.Collectible?.Code?
                    .ToShortString();

            if (string.IsNullOrWhiteSpace(code))
            {
                return;
            }

            double saveChance =
                SkillTreeRuntime.GetEffectValue(
                    player,
                    "DurabilitySave",
                    code
                );

            int saved = 0;

            for (int index = 0;
                 index < amount;
                 index++)
            {
                if (world.Rand.NextDouble() < saveChance)
                {
                    saved++;
                }
            }

            amount = Math.Max(
                0,
                amount - saved
            );
        }

        public static void BlockGetDropsPostfix(
            Block __instance,
            IWorldAccessor world,
            IPlayer byPlayer,
            ref ItemStack[] __result)
        {
            if (world.Side != EnumAppSide.Server ||
                byPlayer is not IServerPlayer player ||
                __result == null ||
                __result.Length == 0)
            {
                return;
            }

            string blockCode =
                __instance.Code?.ToShortString() ??
                string.Empty;

            double rate =
                SkillTreeRuntime.GetEffectValue(
                    player,
                    "BlockDropYield",
                    blockCode
                ) +
                SkillTreeRuntime.GetEffectValue(
                    player,
                    "OreYield",
                    blockCode
                ) +
                SkillTreeRuntime.GetEffectValue(
                    player,
                    "CrystalYield",
                    blockCode
                );

            if (rate <= 0)
            {
                return;
            }

            foreach (ItemStack stack in __result)
            {
                if (stack?.Collectible?.Code == null ||
                    stack.StackSize <= 0)
                {
                    continue;
                }

                double expected =
                    stack.StackSize * rate;

                int bonus =
                    (int)Math.Floor(expected);

                if (world.Rand.NextDouble() <
                    expected - bonus)
                {
                    bonus++;
                }

                stack.StackSize += bonus;
            }
        }

        public static void ReceiveSaturationPrefix(
            EntityAgent __instance,
            ref float saturation)
        {
            if (__instance is not EntityPlayer entityPlayer ||
                entityPlayer.Player is not IServerPlayer player ||
                saturation <= 0)
            {
                return;
            }

            double bonus =
                SkillTreeRuntime.GetEffectValue(
                    player,
                    "FoodSatiety"
                );

            saturation *= (float)Math.Max(
                0,
                1 + bonus
            );
        }

        public static bool GridRecipeConsumeInputPrefix(
            GridRecipe __instance,
            IPlayer byPlayer,
            ref bool __result)
        {
            AssetLocation? outputCode = ResolveOutputCode(__instance.Output);
            if (outputCode == null || SkillTreeRuntime.CanCraftLockedRecipe(
                byPlayer,
                outputCode,
                out string requiredNode))
            {
                return true;
            }

            __result = false;
            if (byPlayer is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(
                    GlobalConstants.GeneralChatGroup,
                    $"This recipe requires the Apprentice capstone '{requiredNode}'.",
                    EnumChatType.Notification
                );
            }
            return false;
        }

        private static AssetLocation? ResolveOutputCode(object? output)
        {
            object? current = output;
            for (int depth = 0; depth < 5 && current != null; depth++)
            {
                if (current is ItemStack stack)
                {
                    return stack.Collectible?.Code;
                }

                Type type = current.GetType();
                PropertyInfo? property = type.GetProperty("ResolvedItemStack") ??
                    type.GetProperty("Itemstack") ?? type.GetProperty("ItemStack");
                FieldInfo? field = type.GetField("ResolvedItemStack") ??
                    type.GetField("Itemstack") ?? type.GetField("ItemStack");
                current = property?.GetValue(current) ?? field?.GetValue(current);
            }
            return null;
        }
    }
}
