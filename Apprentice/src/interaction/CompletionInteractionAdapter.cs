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
	/// Harmony-backed completion adapter for systems without a reliable
	/// public server event.
	///
	/// Patch failures are isolated: one unavailable vanilla method does
	/// not disable the other completion interactions.
	/// </summary>
	internal sealed class CompletionInteractionAdapter
		: IInteractionEventAdapter
	{
		private const string HarmonyId =
			"apprentice.interactions.completion";

		private readonly ICoreServerAPI serverApi;
		private readonly Harmony harmony;

		private int installedPatchCount;
		private bool disposed;

		public CompletionInteractionAdapter(
			ICoreServerAPI serverApi,
			ExperienceManager experienceManager)
		{
			this.serverApi = serverApi
				?? throw new ArgumentNullException(
					nameof(serverApi)
				);

			ArgumentNullException.ThrowIfNull(
				experienceManager
			);

			CompletionInteractionPatches.Configure(
				serverApi,
				experienceManager
			);

			RemainingInteractionPatches.Configure(
				serverApi,
				experienceManager
			);

			harmony = new Harmony(HarmonyId);

			InstallApiPatches();
			InstallSurvivalPatches();

			serverApi.Logger.Notification(
				"[Apprentice] Completion interaction foundation " +
				$"installed {installedPatchCount} Harmony patches."
			);
		}

		private void InstallApiPatches()
		{
			MethodInfo? consumeInput =
				typeof(GridRecipe).GetMethod(
					nameof(GridRecipe.ConsumeInput),
					BindingFlags.Instance |
					BindingFlags.Public,
					binder: null,
					types:
					[
						typeof(IPlayer),
						typeof(ItemSlot[]),
						typeof(int)
					],
					modifiers: null
				);

			TryPatch(
				consumeInput,
				prefixName:
					nameof(
						SkillMasteryPatches
							.GridRecipeConsumeInputPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.GridRecipeConsumeInputPostfix
					),
				description:
					"GridRecipe.ConsumeInput " +
					"(recipe gates and Craft/Repair)"
			);

			MethodInfo? blockInteract =
				typeof(Block).GetMethod(
					nameof(Block.OnBlockInteractStart),
					BindingFlags.Instance |
					BindingFlags.Public,
					binder: null,
					types:
					[
						typeof(IWorldAccessor),
						typeof(IPlayer),
						typeof(BlockSelection)
					],
					modifiers: null
				);

			TryPatch(
				blockInteract,
				prefixName:
					nameof(
						RemainingInteractionPatches
							.HarvestInteractPrefix
					),
				postfixName:
					nameof(
						RemainingInteractionPatches
							.HarvestInteractPostfix
					),
				description:
					"Block.OnBlockInteractStart " +
					"(result-aware right-click Harvest)"
			);

			MethodInfo? activateSlot =
				typeof(InventoryBase).GetMethod(
					nameof(InventoryBase.ActivateSlot),
					BindingFlags.Instance |
					BindingFlags.Public,
					binder: null,
					types:
					[
						typeof(int),
						typeof(ItemSlot),
						typeof(ItemStackMoveOperation)
							.MakeByRefType()
					],
					modifiers: null
				);

			TryPatch(
				activateSlot,
				prefixName:
					nameof(
						CompletionInteractionPatches
							.InventoryActivateSlotPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.InventoryActivateSlotPostfix
					),
				description:
					"InventoryBase.ActivateSlot " +
					"(machine-output pickup attribution)"
			);

			MethodInfo? miningSpeed =
				typeof(CollectibleObject).GetMethod(
					nameof(CollectibleObject.GetMiningSpeed),
					BindingFlags.Instance | BindingFlags.Public,
					binder: null,
					types:
					[
						typeof(IItemStack),
						typeof(BlockSelection),
						typeof(Block),
						typeof(IPlayer)
					],
					modifiers: null
				);

			TryPatch(
				miningSpeed,
				postfixName:
					nameof(
						SkillMasteryPatches
							.GetMiningSpeedPostfix
					),
				description:
					"CollectibleObject.GetMiningSpeed " +
					"(Woodworker mastery)"
			);

			MethodInfo? damageItem =
				typeof(CollectibleObject).GetMethod(
					nameof(CollectibleObject.DamageItem),
					BindingFlags.Instance |
					BindingFlags.Public,
					binder: null,
					types:
					[
						typeof(IWorldAccessor),
						typeof(Entity),
						typeof(ItemSlot),
						typeof(int),
						typeof(bool)
					],
					modifiers: null
				);

			TryPatch(
				damageItem,
				prefixName:
					nameof(
						SkillMasteryPatches
							.DamageItemPrefix
					),
				description:
					"CollectibleObject.DamageItem " +
					"(skill durability preservation)"
			);

			MethodInfo? blockDrops =
				typeof(Block).GetMethod(
					nameof(Block.GetDrops),
					BindingFlags.Instance |
					BindingFlags.Public,
					binder: null,
					types:
					[
						typeof(IWorldAccessor),
						typeof(BlockPos),
						typeof(IPlayer),
						typeof(float)
					],
					modifiers: null
				);

			TryPatch(
				blockDrops,
				postfixName:
					nameof(
						SkillMasteryPatches
							.BlockGetDropsPostfix
					),
				description:
					"Block.GetDrops " +
					"(skill-specific block yield bonuses)"
			);

			MethodInfo? receiveSaturation =
				typeof(EntityAgent).GetMethod(
					nameof(EntityAgent.ReceiveSaturation),
					BindingFlags.Instance |
					BindingFlags.Public,
					binder: null,
					types:
					[
						typeof(float),
						typeof(EnumFoodCategory),
						typeof(float),
						typeof(float)
					],
					modifiers: null
				);

			TryPatch(
				receiveSaturation,
				prefixName:
					nameof(
						SkillMasteryPatches
							.ReceiveSaturationPrefix
					),
				description:
					"EntityAgent.ReceiveSaturation " +
					"(food satiety bonuses)"
			);
		}

		private void InstallSurvivalPatches()
		{
			TryPatchDynamic(
				"Vintagestory.GameContent.EntityBehaviorHealth",
				"OnEntityReceiveDamage",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.HealthDamagePrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.HealthDamagePostfix
					),
				description:
					"EntityBehaviorHealth.OnEntityReceiveDamage " +
					"(weapon attribution, Shield and Tank)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.ItemAxe",
				"OnBlockBrokenWith",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.TreeFellingPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.TreeFellingPostfix
					),
				description:
					"ItemAxe.OnBlockBrokenWith " +
					"(per-log tree-felling XP)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityAnvil",
				"CheckIfFinished",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.SmithingPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.SmithingPostfix
					),
				description:
					"BlockEntityAnvil.CheckIfFinished (Smith)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityClayForm",
				"CheckIfFinished",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.ClayFormingPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.ClayFormingPostfix
					),
				description:
					"BlockEntityClayForm.CheckIfFinished " +
					"(clay-form Craft)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.ItemProspectingPick",
				"ProbeBlockNodeMode",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.ProspectNodePrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.ProspectNodePostfix
					),
				description:
					"ItemProspectingPick.ProbeBlockNodeMode " +
					"(Prospect node mode)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.ItemProspectingPick",
				"PrintProbeResults",
				postfixName:
					nameof(
						CompletionInteractionPatches
							.ProspectDensityPostfix
					),
				description:
					"ItemProspectingPick.PrintProbeResults " +
					"(Prospect density completion)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.EntityBehaviorMilkable",
				"MilkingComplete",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.MilkingPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.MilkingPostfix
					),
				description:
					"EntityBehaviorMilkable.MilkingComplete (Milk)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityFirepit",
				"smeltItems",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.FirepitSmeltPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.FirepitSmeltPostfix
					),
				description:
					"BlockEntityFirepit.smeltItems " +
					"(Smelt/Cook completion marker)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityQuern",
				"grindInput",
				prefixName:
					nameof(
						CompletionInteractionPatches
							.QuernProcessPrefix
					),
				postfixName:
					nameof(
						CompletionInteractionPatches
							.QuernProcessPostfix
					),
				description:
					"BlockEntityQuern.grindInput " +
					"(Process completion marker)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockPan",
				"CreateDrop",
				postfixName:
					nameof(
						RemainingInteractionPatches
							.PanCompletePostfix
					),
				description:
					"BlockPan.CreateDrop (Pan)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.EntityBobber",
				"TryCatchFish",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.FishingPrefix
					),
				postfixName:
					nameof(
						RemainingInteractionPatches
							.FishingPostfix
					),
				description:
					"EntityBobber.TryCatchFish (Fish)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityTrough",
				"OnInteract",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.TroughFillPrefix
					),
				postfixName:
					nameof(
						RemainingInteractionPatches
							.TroughFillPostfix
					),
				description:
					"BlockEntityTrough.OnInteract " +
					"(breeder attribution)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityTrough",
				"ConsumeOnePortion",
				postfixName:
					nameof(
						RemainingInteractionPatches
							.TroughConsumePostfix
					),
				description:
					"BlockEntityTrough.ConsumeOnePortion " +
					"(breeder attribution)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.EntityBehaviorMultiply",
				"TryGetPregnant",
				postfixName:
					nameof(
						RemainingInteractionPatches
							.PregnancyPostfix
					),
				description:
					"EntityBehaviorMultiply.TryGetPregnant (Breed)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.ItemShears",
				"OnBlockBrokenWith",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.ShearsBreakPrefix
					),
				postfixName:
					nameof(
						RemainingInteractionPatches
							.ShearsBreakPostfix
					),
				description:
					"ItemShears.OnBlockBrokenWith (Shear)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.EntityBehaviorHarvestable",
				"SetHarvested",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.CarcassHarvestPrefix
					),
				postfixName:
					nameof(
						RemainingInteractionPatches
							.CarcassHarvestPostfix
					),
				description:
					"EntityBehaviorHarvestable.SetHarvested " +
					"(carcass Harvest)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityBloomery",
				"DoSmelt",
				postfixName:
					nameof(
						RemainingInteractionPatches
							.BloomerySmeltPostfix
					),
				description:
					"BlockEntityBloomery.DoSmelt " +
					"(Smelt output marker)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityBloomery",
				"OnBlockBroken",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.BloomeryBreakPrefix
					),
				description:
					"BlockEntityBloomery.OnBlockBroken " +
					"(Smelt pickup attribution)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityPitKiln",
				"KillFire",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.PitKilnFinishedPrefix
					),
				description:
					"BlockEntityPitKiln.KillFire " +
					"(Smelt output marker)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityBeeHiveKiln",
				"ConvertItemToBurned",
				postfixName:
					nameof(
						RemainingInteractionPatches
							.BeeHiveKilnConvertPostfix
					),
				description:
					"BlockEntityBeeHiveKiln.ConvertItemToBurned " +
					"(Smelt output marker)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BarrelRecipe",
				"TryCraftNow",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.BarrelCraftPrefix
					),
				postfixName:
					nameof(
						RemainingInteractionPatches
							.BarrelCraftPostfix
					),
				description:
					"BarrelRecipe.TryCraftNow " +
					"(Process output marker)"
			);

			TryPatchDynamic(
				"Vintagestory.GameContent.BlockEntityFruitPress",
				"InteractMashContainer",
				prefixName:
					nameof(
						RemainingInteractionPatches
							.FruitPressPrefix
					),
				postfixName:
					nameof(
						RemainingInteractionPatches
							.FruitPressPostfix
					),
				description:
					"BlockEntityFruitPress.InteractMashContainer " +
					"(Process)"
			);

			// Derived harvest blocks can override the base API method.
			// Declared-only lookup prevents duplicate base-method patches.
			string[] harvestBlockTypes =
			[
				"Vintagestory.GameContent.BlockBerryBush",
				"Vintagestory.GameContent.BlockFruitTreeBranch",
				"Vintagestory.GameContent.BlockFruitTreeFoliage",
				"Vintagestory.GameContent.BlockMushroom",
				"Vintagestory.GameContent.BlockSkep"
			];

			foreach (string harvestBlockType in harvestBlockTypes)
			{
				TryPatchDeclaredDynamic(
					harvestBlockType,
					"OnBlockInteractStart",
					prefixName:
						nameof(
							RemainingInteractionPatches
								.HarvestInteractPrefix
						),
					postfixName:
						nameof(
							RemainingInteractionPatches
								.HarvestInteractPostfix
						),
					description:
						harvestBlockType +
						".OnBlockInteractStart " +
						"(right-click Harvest/Shear)"
				);
			}
		}

		private void TryPatchDynamic(
			string typeName,
			string methodName,
			string? prefixName = null,
			string? postfixName = null,
			string? description = null)
		{
			Type? targetType =
				AccessTools.TypeByName(typeName);

			MethodInfo? targetMethod =
				targetType == null
					? null
					: AccessTools.Method(
						targetType,
						methodName
					);

			TryPatch(
				targetMethod,
				prefixName,
				postfixName,
				description ??
					$"{typeName}.{methodName}"
			);
		}

		private void TryPatchDeclaredDynamic(
			string typeName,
			string methodName,
			string? prefixName = null,
			string? postfixName = null,
			string? description = null)
		{
			Type? targetType =
				AccessTools.TypeByName(typeName);

			MethodInfo? targetMethod =
				targetType?.GetMethod(
					methodName,
					BindingFlags.Instance |
					BindingFlags.Public |
					BindingFlags.NonPublic |
					BindingFlags.DeclaredOnly
				);

			TryPatch(
				targetMethod,
				prefixName,
				postfixName,
				description ??
					$"{typeName}.{methodName}"
			);
		}

		private void TryPatch(
			MethodBase? targetMethod,
			string? prefixName = null,
			string? postfixName = null,
			string? description = null)
		{
			string patchDescription =
				description ??
				targetMethod?.Name ??
				"unknown target";

			if (targetMethod == null)
			{
				serverApi.Logger.Warning(
					"[Apprentice] Could not find completion " +
					$"hook: {patchDescription}."
				);

				return;
			}

			try
			{
				HarmonyMethod? prefix =
					CreateHarmonyMethod(prefixName);

				HarmonyMethod? postfix =
					CreateHarmonyMethod(postfixName);

				harmony.Patch(
					targetMethod,
					prefix: prefix,
					postfix: postfix
				);

				installedPatchCount++;

				serverApi.Logger.Debug(
					"[Apprentice] Installed completion hook: " +
					patchDescription + "."
				);
			}
			catch (Exception exception)
			{
				serverApi.Logger.Warning(
					"[Apprentice] Could not install completion " +
					$"hook: {patchDescription}."
				);

				serverApi.Logger.Error(exception);
			}
		}

		private static HarmonyMethod? CreateHarmonyMethod(
			string? methodName)
		{
			if (string.IsNullOrWhiteSpace(methodName))
			{
				return null;
			}

			MethodInfo? patchMethod =
				AccessTools.Method(
					typeof(CompletionInteractionPatches),
					methodName
				) ??
				AccessTools.Method(
					typeof(RemainingInteractionPatches),
					methodName
				) ??
				AccessTools.Method(
					typeof(SkillMasteryPatches),
					methodName
				);

			return patchMethod == null
				? null
				: new HarmonyMethod(patchMethod);
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			disposed = true;

			try
			{
				harmony.UnpatchAll(HarmonyId);
			}
			finally
			{
				CompletionInteractionPatches.Clear();
				RemainingInteractionPatches.Clear();
				ReflectionMemberAccessor.Clear();
			}
		}
	}
}
