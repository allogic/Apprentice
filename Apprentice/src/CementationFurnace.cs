using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Apprentice
{
    internal static class CementationRuntime
    {
        public static ApprenticeContentRegistry Registry { get; set; } =
            ApprenticeContentRegistry.Empty;
    }

    public sealed class BlockCementationFurnace : Block
    {
        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is
                BlockEntityCementationFurnace furnace)
            {
                return furnace.OnPlayerInteract(byPlayer);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }

    /// <summary>
    /// Exact-charge, server-owned processor for Apprentice metals only. No
    /// vanilla or foreign furnace method is patched.
    /// </summary>
    public sealed class BlockEntityCementationFurnace : BlockEntity
    {
        private const int PersistenceSchema = 2;
        private readonly Dictionary<string, int> inputs =
            new(StringComparer.OrdinalIgnoreCase);

        private string recipeId = string.Empty;
        private string operatorUid = string.Empty;
        private bool sealedCharge;
        private bool complete;
        private bool outputClaimed;
        private int fuelConsumed;
        private int refractoryConsumed;
        private double startedAtDays;
        private double requiredDurationDays;
        private long tickListenerId;

        private CementationChargeDefinition? Definition =>
            CementationRuntime.Registry.Charges.FirstOrDefault(value =>
                value.Id.Equals(recipeId, StringComparison.OrdinalIgnoreCase));

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
            {
                tickListenerId = RegisterGameTickListener(OnServerTick, 5000);
            }
        }

        public bool OnPlayerInteract(IPlayer player)
        {
            if (Api.Side != EnumAppSide.Server || player is not IServerPlayer serverPlayer)
            {
                return true;
            }

            if (complete && !outputClaimed)
            {
                ClaimOutput(serverPlayer);
                return true;
            }

            if (sealedCharge)
            {
                Send(serverPlayer, BuildProgressText());
                return true;
            }

            ItemSlot? activeSlot = player.InventoryManager.ActiveHotbarSlot;
            if (activeSlot == null)
            {
                Send(serverPlayer, "No active hotbar slot is available.");
                return true;
            }

            ItemStack? held = activeSlot.Itemstack;
            if (player.Entity.Controls.ShiftKey &&
                held?.Collectible?.Tool == EnumTool.Hammer)
            {
                TrySeal(serverPlayer, activeSlot);
                return true;
            }

            if (held == null)
            {
                if (player.Entity.Controls.ShiftKey && inputs.Count > 0)
                {
                    RefundInputs(serverPlayer);
                }
                else
                {
                    Send(serverPlayer, BuildProgressText());
                }
                return true;
            }

            TryInsert(serverPlayer, activeSlot);
            return true;
        }

        private void TryInsert(IServerPlayer player, ItemSlot activeSlot)
        {
            ItemStack? stack = activeSlot.Itemstack;
            string code = CanonicalCode(stack?.Collectible?.Code);
            if (stack == null || string.IsNullOrWhiteSpace(code)) return;

            CementationChargeDefinition? definition = Definition;
            if (definition == null)
            {
                CementationChargeDefinition[] candidates = CementationRuntime.Registry.Charges
                    .Where(value => value.Inputs.Any(input =>
                        input.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();
                if (candidates.Length != 1)
                {
                    Send(
                        player,
                        "Begin a charge with its primary metal: steel for Starsteel or Starsteel for Aethersteel. Shared alloying metals are accepted after that."
                    );
                    return;
                }

                recipeId = candidates[0].Id;
                definition = candidates[0];
            }

            CementationIngredientDefinition? required = definition.Inputs
                .FirstOrDefault(input =>
                    input.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (required == null)
            {
                Send(player, $"{stack.GetName()} is not part of the selected {definition.Id} charge.");
                return;
            }

            int current = inputs.TryGetValue(code, out int stored) ? stored : 0;
            int remaining = required.Quantity - current;
            if (remaining <= 0)
            {
                Send(player, $"The exact {required.Quantity}x {stack.GetName()} requirement is already filled.");
                return;
            }

            int moved = Math.Min(remaining, stack.StackSize);
            activeSlot.TakeOut(moved);
            activeSlot.MarkDirty();
            inputs[required.Code] = current + moved;
            MarkDirty(true);
            Send(player, $"Added {moved}x {stack.GetName()} ({inputs[required.Code]}/{required.Quantity}).");
        }

        private void TrySeal(IServerPlayer player, ItemSlot hammerSlot)
        {
            if (!SkillTreeRuntime.HasCapstone(player, "blacksmith"))
            {
                Send(player, "Sealing a cementation charge requires the Blacksmith Grandmaster skill.");
                return;
            }

            CementationChargeDefinition? definition = Definition;
            if (definition == null)
            {
                Send(player, "The furnace has no selected charge. Add steel or Starsteel first.");
                return;
            }

            if (!IsExactCharge(definition))
            {
                Send(player, "The furnace rejects the charge: every input and quantity must match the complete recipe exactly.");
                return;
            }

            foreach (string discovery in definition.RequiredDiscoveries)
            {
                if (!HiddenClassData.IsUnlocked(player.Entity, discovery))
                {
                    Send(player, $"Sealing this charge requires the {discovery} discovery.");
                    return;
                }
            }

            foreach (string code in definition.RequiredItems)
            {
                int quantity = code.Equals(
                    definition.RefractoryCode,
                    StringComparison.OrdinalIgnoreCase)
                    ? definition.RefractoryQuantity
                    : 1;
                if (CountInventory(player, code) < quantity)
                {
                    Send(
                        player,
                        $"Sealing this charge requires {quantity}x {code} in your inventory."
                    );
                    return;
                }
            }

            if (CountInventory(player, definition.FuelCode) <
                definition.FuelQuantity)
            {
                Send(
                    player,
                    $"Sealing this charge requires {definition.FuelQuantity}x {definition.FuelCode}."
                );
                return;
            }

            // Grandmaster authorization comes from the server-owned skill
            // tree. The hammer is only the physical sealing tool; refractory
            // components are consumed after every validation check passes.
            hammerSlot.Itemstack?.Collectible.DamageItem(
                Api.World,
                player.Entity,
                hammerSlot,
                1
            );
            hammerSlot.MarkDirty();
            ConsumeInventory(
                player,
                definition.RefractoryCode,
                definition.RefractoryQuantity
            );
            ConsumeInventory(player, definition.FuelCode, definition.FuelQuantity);
            refractoryConsumed = definition.RefractoryQuantity;
            fuelConsumed = definition.FuelQuantity;

            sealedCharge = true;
            complete = false;
            outputClaimed = false;
            operatorUid = player.PlayerUID;
            startedAtDays = Api.World.Calendar.TotalDays;
            double speedBonus = Math.Clamp(
                SkillTreeRuntime.GetEffectValue(
                    player,
                    "CementationSpeed"
                ),
                0,
                0.5
            );
            requiredDurationDays = definition.DurationDays * (1 - speedBonus);
            MarkDirty(true);
            Send(
                player,
                $"The exact {definition.Id} charge is sealed for {requiredDurationDays:0.##} in-game days."
            );
        }

        private bool IsExactCharge(CementationChargeDefinition definition)
        {
            if (inputs.Count != definition.Inputs.Count) return false;
            return definition.Inputs.All(required =>
                inputs.TryGetValue(required.Code, out int quantity) &&
                quantity == required.Quantity);
        }

        private void OnServerTick(float deltaTime)
        {
            CementationChargeDefinition? definition = Definition;
            if (!sealedCharge || complete || outputClaimed || definition == null)
            {
                return;
            }

            double duration = EffectiveDurationDays(definition);
            if (Math.Max(0, Api.World.Calendar.TotalDays - startedAtDays) >= duration)
            {
                complete = true;
                MarkDirty(true);
            }
        }

        private void ClaimOutput(IServerPlayer player)
        {
            CementationChargeDefinition? definition = Definition;
            if (definition == null || outputClaimed) return;

            ItemSlot? activeSlot = player.InventoryManager.ActiveHotbarSlot;
            if (activeSlot?.Itemstack?.Collectible is not ItemTongs)
            {
                Send(
                    player,
                    "The completed blister is forge-hot. Hold any type of tongs to remove it safely."
                );
                return;
            }

            CollectibleObject? collectible = Api.World.GetItem(new AssetLocation(definition.Output));
            if (collectible == null)
            {
                Send(player, $"Output {definition.Output} is unavailable; the charge remains safely unclaimed.");
                return;
            }

            // Flip the persisted guard before exposing output. MarkDirty and
            // inventory/world spawn are processed on the same server thread.
            outputClaimed = true;
            MarkDirty(true);
            ItemStack result = new(collectible, definition.OutputQuantity);
            collectible.SetTemperature(
                Api.World,
                result,
                OutputTemperature(definition)
            );
            if (!player.InventoryManager.TryGiveItemstack(result, slotNotifyEffect: true))
            {
                Api.World.SpawnItemEntity(result, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
            }

            inputs.Clear();
            sealedCharge = false;
            complete = false;
            recipeId = string.Empty;
            operatorUid = string.Empty;
            fuelConsumed = 0;
            refractoryConsumed = 0;
            startedAtDays = 0;
            requiredDurationDays = 0;
            MarkDirty(true);
            Send(player, $"Claimed {definition.OutputQuantity}x {definition.Output}.");
        }

        private static float OutputTemperature(
            CementationChargeDefinition definition) =>
            definition.Output.Contains(
                "aethersteel",
                StringComparison.OrdinalIgnoreCase
            ) ? 1200f : 1100f;

        private void RefundInputs(IServerPlayer player)
        {
            foreach ((string code, int quantity) in inputs.ToArray())
            {
                CollectibleObject? collectible = Api.World.GetItem(new AssetLocation(code)) ??
                    (CollectibleObject?)Api.World.GetBlock(new AssetLocation(code));
                if (collectible == null) continue;
                ItemStack stack = new(collectible, quantity);
                if (!player.InventoryManager.TryGiveItemstack(stack, slotNotifyEffect: true))
                {
                    Api.World.SpawnItemEntity(stack, Pos.ToVec3d().Add(0.5, 1.1, 0.5));
                }
            }

            inputs.Clear();
            recipeId = string.Empty;
            operatorUid = string.Empty;
            fuelConsumed = 0;
            refractoryConsumed = 0;
            startedAtDays = 0;
            requiredDurationDays = 0;
            MarkDirty(true);
            Send(player, "The unsealed charge was returned without loss.");
        }

        private static int CountInventory(IServerPlayer player, string code)
        {
            int count = 0;
            player.Entity.WalkInventory(slot =>
            {
                if (CanonicalCode(slot.Itemstack?.Collectible?.Code)
                    .Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    count += slot.StackSize;
                }
                return true;
            });
            return count;
        }

        private static void ConsumeInventory(
            IServerPlayer player,
            string code,
            int quantity)
        {
            int remaining = quantity;
            player.Entity.WalkInventory(slot =>
            {
                if (remaining <= 0) return false;
                if (!CanonicalCode(slot.Itemstack?.Collectible?.Code)
                    .Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                int removed = Math.Min(remaining, slot.StackSize);
                slot.TakeOut(removed);
                slot.MarkDirty();
                remaining -= removed;
                return remaining > 0;
            });
        }

        private string BuildProgressText()
        {
            CementationChargeDefinition? definition = Definition;
            if (definition == null) return "Cementation furnace: empty.";
            if (sealedCharge)
            {
                double elapsed = Math.Max(
                    0,
                    Api.World.Calendar.TotalDays - startedAtDays
                );
                double duration = EffectiveDurationDays(definition);
                return complete
                    ? $"{definition.Id}: complete; right-click to claim once."
                    : $"{definition.Id}: sealed, {Math.Clamp(elapsed / duration * 100, 0, 100):0}% complete.";
            }

            string contents = string.Join(", ", definition.Inputs.Select(required =>
                $"{required.Code} {inputs.GetValueOrDefault(required.Code)}/{required.Quantity}"));
            return $"{definition.Id} charge (unsealed): {contents}. Sneak-right-click with a hammer to seal; sneak-right-click empty-handed to refund.";
        }

        private static void Send(IServerPlayer player, string message)
        {
            player.SendMessage(
                Vintagestory.API.Config.GlobalConstants.GeneralChatGroup,
                message,
                EnumChatType.Notification
            );
        }

        private static string CanonicalCode(AssetLocation? location) =>
            location == null
                ? string.Empty
                : $"{location.Domain}:{location.Path}";

        private double EffectiveDurationDays(
            CementationChargeDefinition definition)
        {
            // Schema-1 furnaces did not persist the effective duration. They
            // retain the definition's original value; every newly sealed
            // charge stores its duration so later balance edits cannot move a
            // running completion deadline.
            return requiredDurationDays > 0
                ? requiredDurationDays
                : Math.Max(0.001, definition.DurationDays);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("apprenticeSchema", PersistenceSchema);
            tree.SetString("recipeId", recipeId);
            tree.SetString("operatorUid", operatorUid);
            tree.SetBool("sealed", sealedCharge);
            tree.SetBool("complete", complete);
            tree.SetBool("outputClaimed", outputClaimed);
            tree.SetInt("fuelConsumed", fuelConsumed);
            tree.SetInt("refractoryConsumed", refractoryConsumed);
            tree.SetDouble("startedAtDays", startedAtDays);
            tree.SetDouble("requiredDurationDays", requiredDurationDays);
            tree.SetString("inputsJson", JsonConvert.SerializeObject(inputs));
        }

        public override void FromTreeAttributes(
            ITreeAttribute tree,
            IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            recipeId = tree.GetString("recipeId", string.Empty);
            operatorUid = tree.GetString("operatorUid", string.Empty);
            sealedCharge = tree.GetBool("sealed", false);
            complete = tree.GetBool("complete", false);
            outputClaimed = tree.GetBool("outputClaimed", false);
            fuelConsumed = tree.GetInt("fuelConsumed", 0);
            refractoryConsumed = tree.GetInt("refractoryConsumed", 0);
            startedAtDays = tree.GetDouble("startedAtDays", 0);
            requiredDurationDays = tree.GetDouble("requiredDurationDays", 0);
            inputs.Clear();
            try
            {
                Dictionary<string, int>? restored =
                    JsonConvert.DeserializeObject<Dictionary<string, int>>(
                        tree.GetString("inputsJson", "{}"));
                foreach ((string code, int quantity) in restored ?? new())
                {
                    if (quantity > 0) inputs[code] = quantity;
                }
            }
            catch
            {
                // Fail closed: do not process an unprovable legacy/corrupt
                // charge. Existing raw state remains visible in the save.
                sealedCharge = false;
                complete = false;
                outputClaimed = true;
            }
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            dsc.AppendLine(BuildProgressText());
            if (!string.IsNullOrWhiteSpace(operatorUid))
            {
                dsc.AppendLine("Sealed by: " + operatorUid);
            }
            base.GetBlockInfo(forPlayer, dsc);
        }

        public override void OnBlockRemoved()
        {
            if (Api.Side == EnumAppSide.Server && !outputClaimed)
            {
                CementationChargeDefinition? definition = Definition;
                if (complete && definition != null)
                {
                    CollectibleObject? output = Api.World.GetItem(new AssetLocation(definition.Output));
                    if (output != null)
                    {
                        outputClaimed = true;
                        ItemStack result = new(
                            output,
                            definition.OutputQuantity
                        );
                        output.SetTemperature(
                            Api.World,
                            result,
                            OutputTemperature(definition)
                        );
                        Api.World.SpawnItemEntity(
                            result,
                            Pos.ToVec3d().Add(0.5, 0.5, 0.5)
                        );
                    }
                }
                else
                {
                    foreach ((string code, int quantity) in inputs)
                    {
                        CollectibleObject? collectible = Api.World.GetItem(new AssetLocation(code));
                        if (collectible != null)
                        {
                            Api.World.SpawnItemEntity(
                                new ItemStack(collectible, quantity),
                                Pos.ToVec3d().Add(0.5, 0.5, 0.5)
                            );
                        }
                    }
                }
            }
            base.OnBlockRemoved();
        }
    }

    /// <summary>
    /// A cementation blister is intentionally its own anvil input instead of
    /// pretending to be a vanilla metal variant.  That keeps the Apprentice
    /// namespace isolated while still using the game's authoritative smithing
    /// recipe and voxel systems.
    /// </summary>
    public sealed class ItemCementationBlister : Item, IAnvilWorkable
    {
        private int RequiredAnvilTier(ItemStack stack) =>
            stack.Collectible.Attributes?["requiresAnvilTier"].AsInt(5) ?? 5;

        public int GetRequiredAnvilTier(ItemStack stack) =>
            RequiredAnvilTier(stack);

        public List<SmithingRecipe> GetMatchingRecipes(ItemStack stack) =>
            api.GetSmithingRecipes()
                .Where(recipe =>
                    recipe.Ingredient?.SatisfiesAsIngredient(stack) == true &&
                    recipe.Output?.ResolvedItemstack?.Collectible?.Code != null)
                .OrderBy(recipe =>
                    recipe.Output!.ResolvedItemstack!.Collectible.Code.ToString())
                .ToList();

        public bool CanWork(ItemStack stack)
        {
            float temperature = stack.Collectible.GetTemperature(api.World, stack);
            float meltingPoint = stack.Collectible.GetMeltingPoint(
                api.World,
                null,
                new DummySlot(stack)
            );
            float workable = stack.Collectible.Attributes?["workableTemperature"]
                .AsFloat(meltingPoint / 2f) ?? meltingPoint / 2f;
            return temperature >= workable;
        }

        public ItemStack? TryPlaceOn(ItemStack stack, BlockEntityAnvil anvil)
        {
            if (anvil.WorkItemStack != null || !CanWork(stack)) return null;
            if (Variant["metal"] == "aethersteel" &&
                $"{anvil.Block.Code.Domain}:{anvil.Block.Code.Path}" !=
                    "apprentice:anvil-starsteel")
            {
                return null;
            }

            if (stack.Attributes.HasAttribute("voxels"))
            {
                try
                {
                    anvil.Voxels = BlockEntityAnvil.deserializeVoxels(
                        stack.Attributes.GetBytes("voxels")
                    );
                    anvil.SelectedRecipeId = stack.Attributes.GetInt(
                        "selectedRecipeId",
                        -1
                    );
                }
                catch
                {
                    ItemIngot.CreateVoxelsFromIngot(api, ref anvil.Voxels, true);
                }
            }
            else
            {
                ItemIngot.CreateVoxelsFromIngot(api, ref anvil.Voxels, true);
            }

            ItemStack placed = stack.Clone();
            placed.StackSize = 1;
            return placed;
        }

        public int VoxelCountForHandbook(ItemStack stack) => ItemIngot.VoxelCount;

        public ItemStack GetBaseMaterial(ItemStack stack)
        {
            ItemStack material = stack.Clone();
            material.StackSize = 1;
            material.Attributes.RemoveAttribute("voxels");
            material.Attributes.RemoveAttribute("selectedRecipeId");
            material.Attributes.RemoveAttribute("rotation");
            return material;
        }

        public EnumHelveWorkableMode GetHelveWorkableMode(
            ItemStack stack,
            BlockEntityAnvil anvil) => EnumHelveWorkableMode.FullyWorkable;
    }
}
