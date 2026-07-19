using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;


namespace Apprentice
{
	/// <summary>
	/// Result-aware adapters for the remaining base-game interaction types.
	///
	/// All callbacks are installed only on the server. Dynamic reflection
	/// keeps VSSurvivalMod implementation types out of compile-time
	/// signatures while preserving normal API type safety for players,
	/// entities, inventories, blocks and item stacks.
	/// </summary>
	internal static class RemainingInteractionPatches
	{
		private const string PendingInteractionKey =
			"apprentice:pendingInteraction";

		private const string PendingTargetKey =
			"apprentice:pendingTarget";

		private const string PendingOperationsKey =
			"apprentice:pendingOperations";

		private const string BreederUidKey =
			"apprentice:lastBreederUid";

		private const string BreederTimeKey =
			"apprentice:lastBreederTime";

		private const long TroughAttributionLifetimeMs =
			30 * 60 * 1000;

		private static readonly object troughSync =
			new();

		private static readonly Dictionary<
			string,
			TroughFeedRecord
		> troughFeeders =
			new(StringComparer.Ordinal);

		private static ICoreServerAPI? serverApi;
		private static ExperienceManager? experienceManager;

		public static void Configure(
			ICoreServerAPI api,
			ExperienceManager manager)
		{
			serverApi = api;
			experienceManager = manager;
		}

		public static void Clear()
		{
			lock (troughSync)
			{
				troughFeeders.Clear();
			}

			serverApi = null;
			experienceManager = null;
		}

		// -----------------------------------------------------------------
		// Pan
		// -----------------------------------------------------------------

		public static void PanCompletePostfix(
			object[] __args)
		{
			EntityAgent? panner = null;
			string? sourceCode = null;

			foreach (object? argument in __args)
			{
				if (argument is EntityAgent entityAgent)
				{
					panner = entityAgent;
				}
				else if (argument is string text)
				{
					sourceCode = text;
				}
			}

			if (panner is not EntityPlayer pannerPlayer ||
				pannerPlayer.Player is not
					IServerPlayer player ||
				string.IsNullOrWhiteSpace(sourceCode))
			{
				return;
			}

			AssetLocation? targetCode =
				TryParseAssetLocation(
					sourceCode
				);

			if (targetCode == null)
			{
				return;
			}

			Award(
				new InteractionContext(
					player,
					InteractionNames.Pan,
					targetCode
				)
			);
		}

		// -----------------------------------------------------------------
		// Fish
		// -----------------------------------------------------------------

		public static void FishingPrefix(
			object __instance,
			object[] __args,
			out FishingState? __state)
		{
			__state = null;

			EntityAgent? fisherEntity = null;

			foreach (object? argument in __args)
			{
				if (argument is EntityAgent entityAgent)
				{
					fisherEntity = entityAgent;
					break;
				}
			}

			if (fisherEntity is not
					EntityPlayer fisherPlayer ||
				fisherPlayer.Player is not
					IServerPlayer player)
			{
				return;
			}

			Entity? realFish =
				GetMemberValue(
					__instance,
					"fishEntity",
					"FishEntity",
					"hookedEntity",
					"HookedEntity"
				) as Entity;

			__state =
				new FishingState(
					player,
					SnapshotPlayerInventory(player),
					realFish?.Code,
					GetMemberValue(
						__instance,
						"bobberState",
						"BobberState"
					)?.ToString()
				);
		}

		public static void FishingPostfix(
			object __instance,
			FishingState? __state)
		{
			if (__state == null)
			{
				return;
			}

			List<InventoryGain> gains =
				GetPositiveInventoryGains(
					__state.Player,
					__state.InventoryBefore
				);

			bool awardedFish = false;

			foreach (InventoryGain gain in gains)
			{
				if (!gain.Code.Path.Contains(
					"fish",
					StringComparison.OrdinalIgnoreCase
				))
				{
					continue;
				}

				awardedFish = true;

				Award(
					new InteractionContext(
						__state.Player,
						InteractionNames.Fish,
						gain.Code,
						quantity:
							gain.Quantity
					)
				);
			}

			if (awardedFish)
			{
				GrantYieldBonuses(
					__state.Player,
					gains,
					"FishYield"
				);
				return;
			}

			// Real fish can be dropped beside a full inventory. The catch
			// method is the completion boundary, so retain entity-code
			// attribution when a real fish was attached.
			if (__state.RealFishCode != null)
			{
				Award(
					new InteractionContext(
						__state.Player,
						InteractionNames.Fish,
						__state.RealFishCode
					)
				);

				return;
			}

			string stateBefore =
				__state.BobberStateBefore ??
				string.Empty;

			string stateAfter =
				GetMemberValue(
					__instance,
					"bobberState",
					"BobberState"
				)?.ToString() ??
				string.Empty;

			// Virtual fish catches have no fish entity. Use a synthetic
			// target that still matches the Fisher configuration.
			if (stateBefore.Contains(
					"NoEntityFishCatch",
					StringComparison.OrdinalIgnoreCase
				) ||
				stateAfter.Contains(
					"FishCatch",
					StringComparison.OrdinalIgnoreCase
				))
			{
				Award(
					new InteractionContext(
						__state.Player,
						InteractionNames.Fish,
						new AssetLocation(
							"game",
							"fish-catch"
						)
					)
				);
			}
		}

		// -----------------------------------------------------------------
		// Breed
		// -----------------------------------------------------------------

		public static void TroughFillPrefix(
			object __instance,
			object[] __args,
			out TroughFillState? __state)
		{
			__state = null;

			IServerPlayer? player =
				FindServerPlayer(__args);

			BlockPos? position =
				ResolveBlockPosition(__instance);

			InventoryBase? inventory =
				ResolveInventory(__instance);

			if (player == null ||
				position == null ||
				inventory == null)
			{
				return;
			}

			__state =
				new TroughFillState(
					player.PlayerUID,
					GetPositionKey(position),
					CountInventoryItems(inventory)
				);
		}

		public static void TroughFillPostfix(
			object __instance,
			bool __result,
			TroughFillState? __state)
		{
			if (!__result ||
				__state == null)
			{
				return;
			}

			InventoryBase? inventory =
				ResolveInventory(__instance);

			if (inventory == null ||
				CountInventoryItems(inventory) <=
					__state.ItemCountBefore)
			{
				return;
			}

			long now =
				Environment.TickCount64;

			lock (troughSync)
			{
				troughFeeders[
					__state.PositionKey
				] = new TroughFeedRecord(
					__state.PlayerUid,
					now +
					TroughAttributionLifetimeMs
				);

				RemoveExpiredTroughFeeders(now);
			}
		}

		public static void TroughConsumePostfix(
			object __instance,
			object[] __args,
			float __result)
		{
			if (__result <= 0 ||
				__args.Length < 1 ||
				__args[0] is not Entity animal)
			{
				return;
			}

			BlockPos? position =
				ResolveBlockPosition(__instance);

			if (position == null)
			{
				return;
			}

			TroughFeedRecord? feeder = null;
			long now =
				Environment.TickCount64;

			lock (troughSync)
			{
				string positionKey =
					GetPositionKey(position);

				if (troughFeeders.TryGetValue(
					positionKey,
					out TroughFeedRecord? found) &&
					found.ExpiresAtMs >= now)
				{
					feeder = found;
				}

				RemoveExpiredTroughFeeders(now);
			}

			if (feeder == null)
			{
				return;
			}

			animal.WatchedAttributes.SetString(
				BreederUidKey,
				feeder.PlayerUid
			);

			animal.WatchedAttributes.SetLong(
				BreederTimeKey,
				now
			);
		}

		public static void PregnancyPostfix(
			object __instance,
			bool __result)
		{
			if (!__result)
			{
				return;
			}

			Entity? mother =
				GetMemberValue(
					__instance,
					"entity",
					"Entity"
				) as Entity;

			if (mother?.Code == null)
			{
				return;
			}

			string? playerUid =
				mother.WatchedAttributes.GetString(
					BreederUidKey,
					null
				);

			long attributedAt =
				mother.WatchedAttributes.GetLong(
					BreederTimeKey,
					0
				);

			mother.WatchedAttributes.RemoveAttribute(
				BreederUidKey
			);

			mother.WatchedAttributes.RemoveAttribute(
				BreederTimeKey
			);

			if (string.IsNullOrWhiteSpace(playerUid) ||
				attributedAt <= 0 ||
				Environment.TickCount64 -
					attributedAt >
					TroughAttributionLifetimeMs)
			{
				return;
			}

			IServerPlayer? player =
				serverApi?.World.PlayerByUid(
					playerUid
				) as IServerPlayer;

			if (player == null)
			{
				return;
			}

			Award(
				new InteractionContext(
					player,
					InteractionNames.Breed,
					mother.Code
				)
			);
		}

		// -----------------------------------------------------------------
		// Shear and right-click Harvest
		// -----------------------------------------------------------------

		public static void ShearsBreakPrefix(
			object[] __args,
			out ShearsBreakState? __state)
		{
			__state = null;

			if (__args.Length < 4 ||
				__args[0] is not
					IWorldAccessor world ||
				__args[1] is not
					EntityPlayer playerEntity ||
				__args[3] is not
					BlockSelection selection ||
				selection.Position == null ||
				world.Side != EnumAppSide.Server ||
				playerEntity.Player is not
					IServerPlayer player)
			{
				return;
			}

			Block blockBefore =
				world.BlockAccessor.GetBlock(
					selection.Position
				);

			AssetLocation? targetCode =
				player.InventoryManager
					.ActiveHotbarSlot?
					.Itemstack?
					.Collectible?
					.Code ??
				blockBefore?.Code;

			if (blockBefore?.Code == null ||
				targetCode == null)
			{
				return;
			}

			__state =
				new ShearsBreakState(
					player,
					world,
					selection.Position.Copy(),
					blockBefore.Id,
					targetCode
				);
		}

		public static void ShearsBreakPostfix(
			bool __result,
			ShearsBreakState? __state)
		{
			if (!__result ||
				__state == null)
			{
				return;
			}

			Block blockAfter =
				__state.World.BlockAccessor.GetBlock(
					__state.Position
				);

			if (blockAfter.Id ==
				__state.BlockIdBefore)
			{
				return;
			}

			Award(
				new InteractionContext(
					__state.Player,
					InteractionNames.Shear,
					__state.TargetCode
				)
			);
		}

		public static void HarvestInteractPrefix(
			Block __instance,
			IWorldAccessor world,
			IPlayer byPlayer,
			BlockSelection blockSel,
			out HarvestInteractState? __state)
		{
			__state = null;

			if (world.Side != EnumAppSide.Server ||
				byPlayer is not IServerPlayer player ||
				blockSel?.Position == null ||
				!IsHarvestLikeBlock(__instance))
			{
				return;
			}

			ItemStack? activeStack =
				player.InventoryManager
					.ActiveHotbarSlot?
					.Itemstack;

			bool usingShears =
				activeStack?.Collectible?.Code?.Path
					.Contains(
						"shears",
						StringComparison.OrdinalIgnoreCase
					) == true;

			Block before =
				world.BlockAccessor.GetBlock(
					blockSel.Position
				);

			__state =
				new HarvestInteractState(
					player,
					world,
					blockSel.Position.Copy(),
					before.Id,
					before.Code,
					SnapshotPlayerInventory(player),
					usingShears,
					activeStack?.Collectible?.Code
				);
		}

		public static void HarvestInteractPostfix(
			bool __result,
			HarvestInteractState? __state)
		{
			if (!__result ||
				__state == null)
			{
				return;
			}

			List<InventoryGain> gains =
				GetPositiveInventoryGains(
					__state.Player,
					__state.InventoryBefore
				);

			foreach (InventoryGain gain in gains)
			{
				Award(
					new InteractionContext(
						__state.Player,
						InteractionNames.Harvest,
						gain.Code,
						quantity:
							gain.Quantity
					)
				);
			}

			GrantYieldBonuses(
				__state.Player,
				gains,
				"CropYield",
				"SeedYield",
				"HoneyYield",
				"WoolYield",
				"MilkYield"
			);

			if (!__state.UsingShears)
			{
				return;
			}

			Block after =
				__state.World.BlockAccessor.GetBlock(
					__state.Position
				);

			bool changed =
				after.Id != __state.BlockIdBefore ||
				!AssetCodesEqual(
					after.Code,
					__state.BlockCodeBefore
				);

			if (!changed)
			{
				return;
			}

			Award(
				new InteractionContext(
					__state.Player,
					InteractionNames.Shear,
					__state.ShearsCode ??
					__state.BlockCodeBefore ??
					new AssetLocation(
						"game",
						"shears-use"
					)
				)
			);
		}

		// -----------------------------------------------------------------
		// Carcass Harvest
		// -----------------------------------------------------------------

		public static void CarcassHarvestPrefix(
			object __instance,
			object[] __args,
			out CarcassHarvestState? __state)
		{
			__state = null;

			IServerPlayer? player =
				FindServerPlayer(__args);

			Entity? entity =
				GetMemberValue(
					__instance,
					"entity",
					"Entity"
				) as Entity;

			if (player == null ||
				entity?.Code == null ||
				entity.WatchedAttributes.GetBool(
					"harvested",
					false
				))
			{
				return;
			}

			__state =
				new CarcassHarvestState(
					player,
					entity,
					SnapshotInventory(
						ResolveInventory(__instance)
					)
				);
		}

		public static void CarcassHarvestPostfix(
			object __instance,
			CarcassHarvestState? __state)
		{
			if (__state == null ||
				!__state.Entity.WatchedAttributes
					.GetBool(
						"harvested",
						false
					))
			{
				return;
			}

			InventoryBase? inventory =
				ResolveInventory(__instance);

			List<InventoryGain> gains =
				GetPositiveInventoryGains(
					inventory,
					__state.InventoryBefore
				);

			foreach (InventoryGain gain in gains)
			{
				Award(
					new InteractionContext(
						__state.Player,
						InteractionNames.Harvest,
						gain.Code,
						quantity:
							gain.Quantity
					)
				);
			}

			GrantYieldBonuses(
				__state.Player,
				gains,
				"MeatYield",
				"HideYield",
				"BoneYield"
			);
		}

		// -----------------------------------------------------------------
		// Smelt expansion
		// -----------------------------------------------------------------

		public static void BloomerySmeltPostfix(
			object __instance)
		{
			InventoryBase? inventory =
				ResolveInventory(__instance);

			ItemStack? output =
				GetInventorySlot(
					inventory,
					2
				)?.Itemstack;

			if (output?.Collectible?.Code == null)
			{
				return;
			}

			AddPendingOperations(
				output,
				InteractionNames.Smelt,
				output.Collectible.Code,
				Math.Max(
					1,
					output.StackSize
				)
			);
		}

		public static void BloomeryBreakPrefix(
			object __instance,
			object[] __args)
		{
			IServerPlayer? player =
				FindServerPlayer(__args);

			InventoryBase? inventory =
				ResolveInventory(__instance);

			ItemStack? output =
				GetInventorySlot(
					inventory,
					2
				)?.Itemstack;

			PendingMarker? marker =
				ReadPendingMarker(output);

			if (player == null ||
				output == null ||
				marker == null)
			{
				return;
			}

			AssetLocation? targetCode =
				TryParseAssetLocation(
					marker.TargetCode
				);

			if (targetCode == null)
			{
				return;
			}

			ClearPendingMarker(output);

			Award(
				new InteractionContext(
					player,
					marker.Interaction,
					targetCode,
					quantity:
						marker.Operations
				)
			);
		}

		public static void PitKilnFinishedPrefix(
			object __instance,
			object[] __args)
		{
			bool consumeFuel = false;

			foreach (object? argument in __args)
			{
				if (argument is bool value)
				{
					consumeFuel = value;
					break;
				}
			}

			if (!consumeFuel)
			{
				return;
			}

			InventoryBase? inventory =
				ResolveInventory(__instance);

			if (inventory == null)
			{
				return;
			}

			int slotCount =
				Math.Min(
					4,
					inventory.Count
				);

			for (int index = 0;
				 index < slotCount;
				 index++)
			{
				ItemStack? stack =
					inventory[index]?.Itemstack;

				MarkSmeltedStack(stack);
			}
		}

		public static void BeeHiveKilnConvertPostfix(
			object[] __args)
		{
			foreach (object? argument in __args)
			{
				if (argument is ItemSlot slot)
				{
					MarkSmeltedStack(
						slot.Itemstack
					);

					return;
				}
			}
		}

		private static void MarkSmeltedStack(
			ItemStack? stack)
		{
			if (stack?.Collectible?.Code == null)
			{
				return;
			}

			AddPendingOperations(
				stack,
				InteractionNames.Smelt,
				stack.Collectible.Code,
				Math.Max(
					1,
					stack.StackSize
				)
			);
		}

		// -----------------------------------------------------------------
		// Barrel and fruit-press Process
		// -----------------------------------------------------------------

		public static void BarrelCraftPrefix(
			object[] __args,
			out BarrelCraftState? __state)
		{
			__state = null;

			ItemSlot[]? slots = null;

			foreach (object? argument in __args)
			{
				if (argument is ItemSlot[] foundSlots)
				{
					slots = foundSlots;
					break;
				}
			}

			if (slots == null)
			{
				return;
			}

			__state =
				new BarrelCraftState(
					slots,
					SnapshotSlots(slots)
				);
		}

		public static void BarrelCraftPostfix(
			bool __result,
			BarrelCraftState? __state)
		{
			if (!__result ||
				__state == null)
			{
				return;
			}

			for (int index = 0;
				 index < __state.Slots.Length;
				 index++)
			{
				ItemStack? after =
					__state.Slots[index]
						?.Itemstack;

				if (after?.Collectible?.Code == null)
				{
					continue;
				}

				SlotSnapshot before =
					index <
					__state.Before.Count
						? __state.Before[index]
						: new SlotSnapshot(
							null,
							0
						);

				string afterCode =
					after.Collectible.Code
						.ToShortString();

				bool outputChanged =
					!string.Equals(
						before.Code,
						afterCode,
						StringComparison.OrdinalIgnoreCase
					) ||
					after.StackSize >
						before.StackSize;

				if (!outputChanged)
				{
					continue;
				}

				AddPendingOperations(
					after,
					InteractionNames.Process,
					after.Collectible.Code,
					1
				);
			}
		}

		public static void FruitPressPrefix(
			object __instance,
			object[] __args,
			out FruitPressState? __state)
		{
			__state = null;

			IServerPlayer? player =
				FindServerPlayer(__args);

			ItemSlot? mashSlot =
				GetMemberValue(
					__instance,
					"MashSlot",
					"mashSlot"
				) as ItemSlot;

			ItemStack? mash =
				mashSlot?.Itemstack;

			double juiceLeft =
				ReadDoubleMember(
					__instance,
					"juiceableLitresLeft",
					"JuiceableLitresLeft"
				);

			if (player == null ||
				mashSlot == null ||
				mash?.Collectible?.Code == null ||
				double.IsNaN(juiceLeft) ||
				juiceLeft >= 0.01)
			{
				return;
			}

			object? juiceableProps =
				InvokeMethod(
					__instance,
					"getJuiceableProps",
					mash
				);
			if (juiceableProps == null)
			{
				return;
			}

			ItemStack? liquidStack =
				ResolveItemStack(
					GetMemberValue(
						juiceableProps,
						"LiquidStack",
						"liquidStack"
					)
				);

			AssetLocation? targetCode =
				liquidStack?.Collectible?.Code;

			if (targetCode == null)
			{
				return;
			}

			__state =
				new FruitPressState(
					player,
					mashSlot,
					mash.Collectible.Code
						.ToShortString(),
					mash.StackSize,
					targetCode
				);
		}

		public static void FruitPressPostfix(
			bool __result,
			FruitPressState? __state)
		{
			if (!__result ||
				__state == null)
			{
				return;
			}

			ItemStack? after =
				__state.MashSlot.Itemstack;

			bool removed =
				after == null ||
				!string.Equals(
					after.Collectible?.Code
						?.ToShortString(),
					__state.MashCodeBefore,
					StringComparison.OrdinalIgnoreCase
				) ||
				after.StackSize <
					__state.MashSizeBefore;

			if (!removed)
			{
				return;
			}

			Award(
				new InteractionContext(
					__state.Player,
					InteractionNames.Process,
					__state.TargetCode
				)
			);
		}

		// -----------------------------------------------------------------
		// Shared result, inventory and reflection helpers
		// -----------------------------------------------------------------

		private static void Award(
			InteractionContext context)
		{
			ExperienceManager? manager =
				experienceManager;

			ICoreServerAPI? api =
				serverApi;

			if (manager == null ||
				api == null)
			{
				return;
			}

			try
			{
				manager.HandleInteraction(
					context
				);
			}
			catch (Exception exception)
			{
				api.Logger.Error(
					"[Apprentice] Remaining interaction " +
					$"'{context.Interaction}' failed."
				);

				api.Logger.Error(exception);
			}
		}

		private static void GrantYieldBonuses(
			IServerPlayer player,
			IEnumerable<InventoryGain> gains,
			params string[] effectTypes)
		{
			foreach (InventoryGain gain in gains)
			{
				string code = gain.Code.ToShortString();

				foreach (string effectType in effectTypes)
				{
					if (!MatchesYieldCategory(
							effectType,
							code
						))
					{
						continue;
					}

					double rate =
						SkillTreeRuntime.GetEffectValue(
							player,
							effectType,
							code
						);

					int bonusQuantity =
						RollBonusQuantity(
							player,
							gain.Quantity,
							rate
						);

					if (bonusQuantity > 0)
					{
						GiveBonusItem(
							player,
							gain.Code,
							bonusQuantity
						);
					}
				}
			}
		}

		private static bool MatchesYieldCategory(
			string effectType,
			string code)
		{
			string lower =
				code.ToLowerInvariant();

			return effectType switch
			{
				"CropYield" =>
					!lower.Contains("seed") &&
					(
						lower.Contains("crop") ||
						lower.Contains("grain") ||
						lower.Contains("vegetable") ||
						lower.Contains("fruit") ||
						lower.Contains("berry") ||
						lower.Contains("turnip") ||
						lower.Contains("carrot") ||
						lower.Contains("onion") ||
						lower.Contains("cabbage")
					),
				"SeedYield" =>
					lower.Contains("seed"),
				"HoneyYield" =>
					lower.Contains("honey") ||
					lower.Contains("comb"),
				"FishYield" =>
					lower.Contains("fish"),
				"MeatYield" =>
					lower.Contains("meat"),
				"HideYield" =>
					lower.Contains("hide") ||
					lower.Contains("pelt"),
				"BoneYield" =>
					lower.Contains("bone"),
				"WoolYield" =>
					lower.Contains("wool") ||
					lower.Contains("fiber"),
				"MilkYield" =>
					lower.Contains("milk"),
				_ => true
			};
		}

		internal static void GrantBonusOutput(
			IServerPlayer player,
			AssetLocation code,
			int baseQuantity,
			string effectType)
		{
			double rate =
				SkillTreeRuntime.GetEffectValue(
					player,
					effectType,
					code.ToShortString()
				);

			int bonusQuantity =
				RollBonusQuantity(
					player,
					baseQuantity,
					rate
				);

			if (bonusQuantity > 0)
			{
				GiveBonusItem(
					player,
					code,
					bonusQuantity
				);
			}
		}

		private static int RollBonusQuantity(
			IServerPlayer player,
			int baseQuantity,
			double rate)
		{
			if (baseQuantity <= 0 ||
				rate <= 0)
			{
				return 0;
			}

			double expected =
				baseQuantity * rate;

			int whole =
				(int)Math.Floor(expected);

			double fraction =
				expected - whole;

			if (fraction > 0 &&
				player.Entity.World.Rand.NextDouble() < fraction)
			{
				whole++;
			}

			return whole;
		}

		private static void GiveBonusItem(
			IServerPlayer player,
			AssetLocation code,
			int quantity)
		{
			if (quantity <= 0)
			{
				return;
			}

			CollectibleObject? collectible =
				player.Entity.World.GetItem(code) ??
				(CollectibleObject?)player.Entity.World.GetBlock(code);

			if (collectible == null)
			{
				return;
			}

			var stack =
				new ItemStack(
					collectible,
					quantity
				);

			bool completelyGiven =
				player.InventoryManager.TryGiveItemstack(
					stack,
					true
				);

			if (!completelyGiven &&
				stack.StackSize > 0)
			{
				player.Entity.World.SpawnItemEntity(
					stack,
					player.Entity.Pos.XYZ
				);
			}
		}

		private static Dictionary<string, int>
			SnapshotPlayerInventory(
				IServerPlayer player)
		{
			Dictionary<string, int> snapshot =
				new(
					StringComparer.OrdinalIgnoreCase
				);

			foreach (
				InventoryBase inventory
				in player.InventoryManager
					.InventoriesOrdered)
			{
				if (inventory is not
					InventoryBasePlayer)
				{
					continue;
				}

				AccumulateInventory(
					inventory,
					snapshot
				);
			}

			return snapshot;
		}

		private static Dictionary<string, int>
			SnapshotInventory(
				InventoryBase? inventory)
		{
			Dictionary<string, int> snapshot =
				new(
					StringComparer.OrdinalIgnoreCase
				);

			if (inventory != null)
			{
				AccumulateInventory(
					inventory,
					snapshot
				);
			}

			return snapshot;
		}

		private static void AccumulateInventory(
			InventoryBase inventory,
			Dictionary<string, int> totals)
		{
			foreach (ItemSlot slot in inventory)
			{
				ItemStack? stack =
					slot?.Itemstack;

				AssetLocation? code =
					stack?.Collectible?.Code;

				if (code == null ||
					stack!.StackSize <= 0)
				{
					continue;
				}

				string key =
					code.ToShortString();

				totals.TryGetValue(
					key,
					out int current
				);

				totals[key] =
					current +
					stack.StackSize;
			}
		}

		private static List<InventoryGain>
			GetPositiveInventoryGains(
				IServerPlayer player,
				Dictionary<string, int> before)
		{
			Dictionary<string, int> after =
				SnapshotPlayerInventory(player);

			return CalculateGains(
				before,
				after
			);
		}

		private static List<InventoryGain>
			GetPositiveInventoryGains(
				InventoryBase? inventory,
				Dictionary<string, int> before)
		{
			Dictionary<string, int> after =
				SnapshotInventory(inventory);

			return CalculateGains(
				before,
				after
			);
		}

		private static List<InventoryGain>
			CalculateGains(
				Dictionary<string, int> before,
				Dictionary<string, int> after)
		{
			List<InventoryGain> gains =
				new();

			foreach (
				KeyValuePair<string, int> entry
				in after)
			{
				before.TryGetValue(
					entry.Key,
					out int previous
				);

				int gained =
					entry.Value -
					previous;

				if (gained <= 0)
				{
					continue;
				}

				AssetLocation? code =
					TryParseAssetLocation(
						entry.Key
					);

				if (code == null)
				{
					continue;
				}

				gains.Add(
					new InventoryGain(
						code,
						gained
					)
				);
			}

			return gains;
		}

		private static bool IsHarvestLikeBlock(
			Block block)
		{
			string typeName =
				block.GetType().Name;

			string path =
				block.Code?.Path ??
				string.Empty;

			string combined =
				typeName +
				" " +
				path;

			string[] harvestTokens =
			[
				"berry",
				"fruit",
				"mushroom",
				"beehive",
				"skep",
				"honey",
				"cattail",
				"reed",
				"resin",
				"bamboo",
				"crop"
			];

			foreach (string token in harvestTokens)
			{
				if (combined.Contains(
					token,
					StringComparison.OrdinalIgnoreCase
				))
				{
					return true;
				}
			}

			return false;
		}

		private static IServerPlayer?
			FindServerPlayer(
				object[] arguments)
		{
			foreach (object? argument in arguments)
			{
				if (argument is IServerPlayer player)
				{
					return player;
				}

				if (argument is EntityPlayer entityPlayer &&
					entityPlayer.Player is
						IServerPlayer entityServerPlayer)
				{
					return entityServerPlayer;
				}
			}

			return null;
		}

		private static InventoryBase?
			ResolveInventory(
				object instance)
		{
			return GetMemberValue(
				instance,
				"Inventory",
				"inventory"
			) as InventoryBase;
		}

		private static BlockPos?
			ResolveBlockPosition(
				object instance)
		{
			return GetMemberValue(
				instance,
				"Pos",
				"pos"
			) as BlockPos;
		}

		private static int CountInventoryItems(
			InventoryBase inventory)
		{
			int total = 0;

			foreach (ItemSlot slot in inventory)
			{
				total +=
					slot?.Itemstack?
						.StackSize ??
					0;
			}

			return total;
		}

		private static ItemSlot?
			GetInventorySlot(
				InventoryBase? inventory,
				int index)
		{
			if (inventory == null ||
				index < 0 ||
				index >= inventory.Count)
			{
				return null;
			}

			return inventory[index];
		}

		private static List<SlotSnapshot>
			SnapshotSlots(
				ItemSlot[] slots)
		{
			List<SlotSnapshot> snapshots =
				new();

			foreach (ItemSlot slot in slots)
			{
				ItemStack? stack =
					slot?.Itemstack;

				snapshots.Add(
					new SlotSnapshot(
						stack?.Collectible?.Code
							?.ToShortString(),
						stack?.StackSize ?? 0
					)
				);
			}

			return snapshots;
		}

		private static void AddPendingOperations(
			ItemStack stack,
			string interaction,
			AssetLocation targetCode,
			int operations)
		{
			if (operations < 1)
			{
				return;
			}

			PendingMarker? existing =
				ReadPendingMarker(stack);

			int totalOperations =
				operations;

			if (existing != null &&
				string.Equals(
					existing.Interaction,
					interaction,
					StringComparison.OrdinalIgnoreCase
				) &&
				string.Equals(
					existing.TargetCode,
					targetCode.ToShortString(),
					StringComparison.OrdinalIgnoreCase
				))
			{
				totalOperations +=
					existing.Operations;
			}

			ITreeAttribute attributes =
				stack.TempAttributes;

			attributes.SetString(
				PendingInteractionKey,
				interaction
			);

			attributes.SetString(
				PendingTargetKey,
				targetCode.ToShortString()
			);

			attributes.SetInt(
				PendingOperationsKey,
				totalOperations
			);
		}

		private static PendingMarker?
			ReadPendingMarker(
				ItemStack? stack)
		{
			if (stack == null)
			{
				return null;
			}

			ITreeAttribute attributes =
				stack.TempAttributes;

			string? interaction =
				attributes.GetString(
					PendingInteractionKey,
					null
				);

			string? target =
				attributes.GetString(
					PendingTargetKey,
					null
				);

			int operations =
				attributes.GetInt(
					PendingOperationsKey,
					0
				);

			if (string.IsNullOrWhiteSpace(
					interaction) ||
				string.IsNullOrWhiteSpace(
					target) ||
				operations < 1)
			{
				return null;
			}

			return new PendingMarker(
				interaction,
				target,
				operations
			);
		}

		private static void ClearPendingMarker(
			ItemStack stack)
		{
			ITreeAttribute attributes =
				stack.TempAttributes;

			attributes.RemoveAttribute(
				PendingInteractionKey
			);

			attributes.RemoveAttribute(
				PendingTargetKey
			);

			attributes.RemoveAttribute(
				PendingOperationsKey
			);
		}

		private static ItemStack?
			ResolveItemStack(
				object? value,
				int depth = 0)
		{
			if (value == null ||
				depth > 4)
			{
				return null;
			}

			if (value is ItemStack stack)
			{
				return stack;
			}

			string[] memberNames =
			[
				"ResolvedItemStack",
				"ResolvedItemstack",
				"Itemstack",
				"ItemStack",
				"LiquidStack",
				"Output"
			];

			foreach (string memberName in memberNames)
			{
				object? nested =
					GetMemberValue(
						value,
						memberName
					);

				if (ReferenceEquals(
					nested,
					value))
				{
					continue;
				}

				ItemStack? resolved =
					ResolveItemStack(
						nested,
						depth + 1
					);

				if (resolved != null)
				{
					return resolved;
				}
			}

			return null;
		}

		private static object? GetMemberValue(
			object? instance,
			params string[] memberNames)
		{
			return ReflectionMemberAccessor.GetValue(
				instance,
				memberNames
			);
		}

		private static object? InvokeMethod(
			object instance,
			string methodName,
			params object?[] arguments)
		{
			MethodInfo? method =
				AccessTools.Method(
					instance.GetType(),
					methodName
				);

			if (method == null)
			{
				return null;
			}

			try
			{
				return method.Invoke(
					instance,
					arguments
				);
			}
			catch
			{
				return null;
			}
		}

		private static double ReadDoubleMember(
			object instance,
			params string[] memberNames)
		{
			object? value =
				GetMemberValue(
					instance,
					memberNames
				);

			try
			{
				return value == null
					? double.NaN
					: Convert.ToDouble(value);
			}
			catch
			{
				return double.NaN;
			}
		}

		private static AssetLocation?
			TryParseAssetLocation(
				string? code)
		{
			if (string.IsNullOrWhiteSpace(code))
			{
				return null;
			}

			try
			{
				return new AssetLocation(code);
			}
			catch
			{
				return null;
			}
		}

		private static bool AssetCodesEqual(
			AssetLocation? left,
			AssetLocation? right)
		{
			return string.Equals(
				left?.ToShortString(),
				right?.ToShortString(),
				StringComparison.OrdinalIgnoreCase
			);
		}

		private static string GetPositionKey(
			BlockPos position)
		{
			return
				$"{position.X}:{position.Y}:{position.Z}";
		}

		private static void RemoveExpiredTroughFeeders(
			long now)
		{
			List<string> expired =
				new();

			foreach (
				KeyValuePair<string, TroughFeedRecord>
					entry
				in troughFeeders)
			{
				if (entry.Value.ExpiresAtMs < now)
				{
					expired.Add(entry.Key);
				}
			}

			foreach (string key in expired)
			{
				troughFeeders.Remove(key);
			}
		}

		internal sealed record InventoryGain(
			AssetLocation Code,
			int Quantity
		);

		internal sealed record FishingState(
			IServerPlayer Player,
			Dictionary<string, int> InventoryBefore,
			AssetLocation? RealFishCode,
			string? BobberStateBefore
		);

		internal sealed record TroughFeedRecord(
			string PlayerUid,
			long ExpiresAtMs
		);

		internal sealed record TroughFillState(
			string PlayerUid,
			string PositionKey,
			int ItemCountBefore
		);

		internal sealed record ShearsBreakState(
			IServerPlayer Player,
			IWorldAccessor World,
			BlockPos Position,
			int BlockIdBefore,
			AssetLocation TargetCode
		);

		internal sealed record HarvestInteractState(
			IServerPlayer Player,
			IWorldAccessor World,
			BlockPos Position,
			int BlockIdBefore,
			AssetLocation? BlockCodeBefore,
			Dictionary<string, int> InventoryBefore,
			bool UsingShears,
			AssetLocation? ShearsCode
		);

		internal sealed record CarcassHarvestState(
			IServerPlayer Player,
			Entity Entity,
			Dictionary<string, int> InventoryBefore
		);

		internal sealed record SlotSnapshot(
			string? Code,
			int StackSize
		);

		internal sealed record BarrelCraftState(
			ItemSlot[] Slots,
			List<SlotSnapshot> Before
		);

		internal sealed record FruitPressState(
			IServerPlayer Player,
			ItemSlot MashSlot,
			string MashCodeBefore,
			int MashSizeBefore,
			AssetLocation TargetCode
		);

		internal sealed record PendingMarker(
			string Interaction,
			string TargetCode,
			int Operations
		);
	}
}
