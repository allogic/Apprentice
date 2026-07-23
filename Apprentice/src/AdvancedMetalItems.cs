using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Apprentice
{
    public sealed class ItemApprenticeIngot : Item, IAnvilWorkable
    {
        public int GetRequiredAnvilTier(ItemStack stack)
        {
            int tier = Variant["metal"] == "aethersteel" ? 6 : 5;
            return stack.Collectible.Attributes?["requiresAnvilTier"]
                .AsInt(tier) ?? tier;
        }

        public List<SmithingRecipe> GetMatchingRecipes(ItemStack stack) =>
            api.GetSmithingRecipes()
                .Where(recipe =>
                    recipe.Ingredient?.SatisfiesAsIngredient(stack) == true &&
                    recipe.Output?.ResolvedItemstack?.Collectible?.Code != null)
                .OrderBy(recipe =>
                    recipe.Output!.ResolvedItemstack!.Collectible.Code)
                .ToList();

        public bool CanWork(ItemStack stack)
        {
            float temperature = stack.Collectible.GetTemperature(api.World, stack);
            float meltingPoint = stack.Collectible.GetMeltingPoint(
                api.World,
                null,
                new DummySlot(stack)
            );
            float workableTemperature = stack.Collectible.Attributes?
                ["workableTemperature"].AsFloat(meltingPoint / 2f)
                ?? meltingPoint / 2f;
            return temperature >= workableTemperature;
        }

        public ItemStack? TryPlaceOn(
            ItemStack stack,
            BlockEntityAnvil anvil)
        {
            if (!CanWork(stack)) return null;

            string metalCode = Variant["metal"];
            Item? workItem = api.World.GetItem(
                new AssetLocation("apprentice", $"workitem-{metalCode}")
            );
            if (workItem == null) return null;

            ItemStack placed = new(workItem);
            placed.Collectible.SetTemperature(
                api.World,
                placed,
                stack.Collectible.GetTemperature(api.World, stack)
            );

            if (anvil.WorkItemStack == null)
            {
                ItemIngot.CreateVoxelsFromIngot(api, ref anvil.Voxels);
                return placed;
            }

            if (!string.Equals(
                anvil.WorkItemStack.Collectible.Variant["metal"],
                metalCode))
            {
                if (api.Side == EnumAppSide.Client)
                {
                    ((ICoreClientAPI)api).TriggerIngameError(
                        this,
                        "notequal",
                        Lang.Get("Must be the same metal to add voxels")
                    );
                }
                return null;
            }

            if (ItemIngot.AddVoxelsFromIngot(ref anvil.Voxels) != 0)
            {
                return placed;
            }

            if (api.Side == EnumAppSide.Client)
            {
                ((ICoreClientAPI)api).TriggerIngameError(
                    this,
                    "requireshammering",
                    Lang.Get("Try hammering down before adding additional voxels")
                );
            }
            return null;
        }

        public int VoxelCountForHandbook(ItemStack stack) =>
            ItemIngot.VoxelCount;

        public ItemStack GetBaseMaterial(ItemStack stack) => stack;

        public EnumHelveWorkableMode GetHelveWorkableMode(
            ItemStack stack,
            BlockEntityAnvil anvil) => EnumHelveWorkableMode.NotWorkable;
    }
}
