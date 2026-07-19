using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace Apprentice
{
    internal static class PoisonedConsumable
    {
        private const string TreeKey = "apprentice:foodPoison";

        public static bool TryApply(ItemStack stack, EntityAgent poisoner, string tier)
        {
            if (stack?.Collectible == null || poisoner == null || !IsConsumable(stack, poisoner.World))
            {
                return false;
            }

            ITreeAttribute tree = stack.Attributes.GetOrAddTreeAttribute(TreeKey);
            tree.SetString("id", tier);
            tree.SetLong("poisonerEntityId", poisoner.EntityId);

            // Liquid containers keep their contents as a nested stack. Mark it
            // as well so pouring into bottles, bowls or tanks preserves poison.
            ItemStack? content = FindContainedStack(stack);
            if (content != null && !ReferenceEquals(content, stack))
            {
                ITreeAttribute contentTree = content.Attributes.GetOrAddTreeAttribute(TreeKey);
                contentTree.SetString("id", tier);
                contentTree.SetLong("poisonerEntityId", poisoner.EntityId);
            }

            return true;
        }

        public static bool TryRead(ItemStack? stack, out string tier, out long poisonerEntityId)
        {
            tier = string.Empty;
            poisonerEntityId = 0;
            if (stack == null) return false;

            ITreeAttribute? tree = stack.Attributes.GetTreeAttribute(TreeKey);
            if (tree == null)
            {
                ItemStack? content = FindContainedStack(stack);
                tree = content?.Attributes.GetTreeAttribute(TreeKey);
            }

            if (tree == null) return false;
            tier = tree.GetString("id", string.Empty);
            poisonerEntityId = tree.GetLong("poisonerEntityId", 0);
            return tier.Length > 0;
        }

        public static int MeasureAmount(ItemStack? stack)
        {
            if (stack == null) return 0;
            ItemStack? content = FindContainedStack(stack);
            return content != null && !ReferenceEquals(content, stack)
                ? content.StackSize
                : stack.StackSize;
        }

        private static bool IsConsumable(ItemStack stack, IWorldAccessor world)
        {
            ItemStack? content = FindContainedStack(stack);
            if (content != null && !ReferenceEquals(content, stack) && IsConsumable(content, world))
            {
                return true;
            }

            try
            {
                MethodInfo? method = stack.Collectible.GetType().GetMethod(
                    "GetNutritionProperties",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                );
                if (method != null)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    object?[] args = parameters.Length switch
                    {
                        2 => new object?[] { world, stack },
                        3 => new object?[] { world, stack, null },
                        _ => Array.Empty<object?>()
                    };
                    if (args.Length > 0 && method.Invoke(stack.Collectible, args) != null) return true;
                }
            }
            catch
            {
                // A third-party collectible may expose a differently shaped
                // nutrition method. Attribute and liquid checks below remain.
            }

            JsonObject attributes = stack.Collectible.Attributes;
            return attributes?["nutritionProps"].Exists == true ||
                attributes?["nutritionPropsByType"].Exists == true ||
                attributes?["waterTightContainerProps"]["nutritionPropsPerLitre"].Exists == true ||
                attributes?["waterTightContainerProps"]["nutritionPropsPerLitreByType"].Exists == true ||
                attributes?["waterTightContainerPropsByType"].Exists == true;
        }

        private static ItemStack? FindContainedStack(ItemStack stack)
        {
            try
            {
                foreach (MethodInfo method in stack.Collectible.GetType().GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.Name != "GetContent" || method.ReturnType != typeof(ItemStack)) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(ItemStack))
                    {
                        return method.Invoke(stack.Collectible, new object[] { stack }) as ItemStack;
                    }
                }
            }
            catch { }
            return null;
        }
    }

    public sealed class ItemApprenticePoisonPortion : Item
    {
        public override void OnHeldInteractStart(
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel,
            bool firstEvent,
            ref EnumHandHandling handling)
        {
            ItemSlot? targetSlot = (byEntity as EntityPlayer)?.LeftHandItemSlot;
            ItemStack? target = targetSlot?.Itemstack;
            string tier = Variant?["poison"] ?? string.Empty;
            if (target == null || tier.Length == 0 || !PoisonedConsumable.TryApply(target, byEntity, tier))
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefault;
            if (byEntity.World.Side != EnumAppSide.Server) return;

            targetSlot!.MarkDirty();
            if ((byEntity as EntityPlayer)?.Player?.WorldData?.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }
        }
    }

    internal sealed record PoisonConsumptionState(string Tier, long PoisonerEntityId, int Amount);

    internal static class PoisonConsumptionPatches
    {
        public static void HeldInteractStopPrefix(ItemSlot slot, out PoisonConsumptionState? __state)
        {
            __state = null;
            ItemStack? stack = slot?.Itemstack;
            if (stack != null && PoisonedConsumable.TryRead(stack, out string tier, out long poisoner))
            {
                __state = new PoisonConsumptionState(
                    tier,
                    poisoner,
                    PoisonedConsumable.MeasureAmount(stack)
                );
            }
        }

        public static void HeldInteractStopPostfix(
            ItemSlot slot,
            EntityAgent byEntity,
            PoisonConsumptionState? __state)
        {
            if (__state == null || byEntity?.World?.Side != EnumAppSide.Server) return;
            int after = PoisonedConsumable.MeasureAmount(slot?.Itemstack);
            if (after < __state.Amount)
            {
                PoisonRuntime.ApplyConsumedPoison(byEntity, __state.Tier, __state.PoisonerEntityId);
            }
        }
    }
}
