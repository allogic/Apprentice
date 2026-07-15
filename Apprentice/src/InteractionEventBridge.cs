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
	/// Lifetime owner for independent server interaction adapters.
	/// </summary>
	internal sealed class InteractionEventBridge : IDisposable
	{
		private readonly List<IInteractionEventAdapter> adapters =
			new();

		private bool disposed;

		public InteractionEventBridge(
			ICoreServerAPI serverApi,
			ExperienceManager experienceManager)
		{
			ArgumentNullException.ThrowIfNull(serverApi);
			ArgumentNullException.ThrowIfNull(
				experienceManager
			);

			try
			{
				adapters.Add(
					new BlockInteractionAdapter(
						serverApi,
						experienceManager
					)
				);

				adapters.Add(
					new EntityDeathInteractionAdapter(
						serverApi,
						experienceManager
					)
				);

				adapters.Add(
					new CompletionInteractionAdapter(
						serverApi,
						experienceManager
					)
				);
			}
			catch
			{
				Dispose();
				throw;
			}

			serverApi.Logger.Notification(
				"[Apprentice] Interaction adapters ready: " +
				"DestroyBlock, PlaceBlock, Plant, Harvest, " +
				"KillPvE, KillPvP, weapon kills, ShieldBlock, " +
				"DamageTaken, Craft, ClayForm, Repair, Smith, " +
				"Prospect, Pan, Fish, Breed, Milk, Shear, " +
				"Harvest, Smelt, Cook and Process."
			);
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			disposed = true;

			for (int index = adapters.Count - 1;
				 index >= 0;
				 index--)
			{
				adapters[index].Dispose();
			}

			adapters.Clear();
		}
	}

	/// <summary>
	/// Native entity-death adapter for player-attributed kills.
	/// </summary>
	internal sealed class EntityDeathInteractionAdapter
		: IInteractionEventAdapter
	{
		private readonly ICoreServerAPI serverApi;
		private readonly ExperienceManager experienceManager;

		private bool disposed;

		public EntityDeathInteractionAdapter(
			ICoreServerAPI serverApi,
			ExperienceManager experienceManager)
		{
			this.serverApi = serverApi
				?? throw new ArgumentNullException(
					nameof(serverApi)
				);

			this.experienceManager = experienceManager
				?? throw new ArgumentNullException(
					nameof(experienceManager)
				);

			serverApi.Event.OnEntityDeath +=
				OnEntityDeath;
		}

		private void OnEntityDeath(
			Entity deadEntity,
			DamageSource damageSource)
		{
			try
			{
				if (deadEntity?.Code == null ||
					damageSource == null)
				{
					return;
				}

				Entity? causeEntity =
					damageSource.GetCauseEntity();

				if (causeEntity is not
					EntityPlayer killerEntity)
				{
					return;
				}

				if (killerEntity.EntityId ==
					deadEntity.EntityId)
				{
					return;
				}

				if (killerEntity.Player is not
					IServerPlayer killerPlayer)
				{
					return;
				}

				string interaction =
					deadEntity is EntityPlayer
						? InteractionNames.KillPvP
						: InteractionNames.KillPvE;

				// Victim-based reward remains available for skills
				// such as Hunter.
				experienceManager.HandleInteraction(
					new InteractionContext(
						killerPlayer,
						interaction,
						deadEntity.Code
					)
				);

				AssetLocation? weaponCode =
					CompletionInteractionPatches
						.ConsumeWeaponForDeath(
							deadEntity,
							damageSource,
							killerEntity
						);

				if (weaponCode != null)
				{
					string weaponInteraction =
						deadEntity is EntityPlayer
							? InteractionNames
								.WeaponKillPvP
							: InteractionNames
								.WeaponKillPvE;

					experienceManager.HandleInteraction(
						new InteractionContext(
							killerPlayer,
							weaponInteraction,
							weaponCode
						)
					);
				}
			}
			catch (Exception exception)
			{
				serverApi.Logger.Error(
					"[Apprentice] Failed to process an " +
					"entity-death interaction."
				);

				serverApi.Logger.Error(exception);
			}
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			disposed = true;

			serverApi.Event.OnEntityDeath -=
				OnEntityDeath;
		}
	}

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
			}
		}
	}

	/// <summary>
	/// One-shot suppression used when a specialized interaction itself
	/// breaks a block, such as prospecting. This prevents an additional
	/// generic DestroyBlock reward for the same physical action.
	/// </summary>
	internal static class InteractionEventSuppression
	{
		private static readonly Dictionary<
			string,
			Dictionary<string, long>
		> suppressedBreaks =
			new(StringComparer.Ordinal);

		public static void SuppressNextBlockBreak(
			string playerUid,
			BlockPos position,
			long nowMs)
		{
			SuppressBlockBreaks(
				playerUid,
				new[]
				{
					position
				},
				nowMs
			);
		}

		public static void SuppressBlockBreaks(
			string playerUid,
			IEnumerable<BlockPos> positions,
			long nowMs)
		{
			if (string.IsNullOrWhiteSpace(playerUid) ||
				positions == null)
			{
				return;
			}

			lock (suppressedBreaks)
			{
				if (!suppressedBreaks.TryGetValue(
					playerUid,
					out Dictionary<string, long>? playerBreaks))
				{
					playerBreaks =
						new Dictionary<string, long>(
							StringComparer.Ordinal
						);

					suppressedBreaks[playerUid] =
						playerBreaks;
				}

				long expiresAt =
					nowMs + 5000;

				foreach (BlockPos position in positions)
				{
					if (position == null)
					{
						continue;
					}

					playerBreaks[
						GetPositionKey(position)
					] = expiresAt;
				}
			}
		}

		public static bool ConsumeSuppressedBlockBreak(
			string playerUid,
			BlockPos? position)
		{
			if (string.IsNullOrWhiteSpace(playerUid) ||
				position == null)
			{
				return false;
			}

			lock (suppressedBreaks)
			{
				if (!suppressedBreaks.TryGetValue(
					playerUid,
					out Dictionary<string, long>? playerBreaks))
				{
					return false;
				}

				string positionKey =
					GetPositionKey(position);

				if (!playerBreaks.TryGetValue(
					positionKey,
					out long expiresAt))
				{
					RemoveExpiredEntries(
						playerUid,
						playerBreaks
					);

					return false;
				}

				playerBreaks.Remove(positionKey);

				if (playerBreaks.Count == 0)
				{
					suppressedBreaks.Remove(
						playerUid
					);
				}

				return expiresAt >=
					Environment.TickCount64;
			}
		}

		private static void RemoveExpiredEntries(
			string playerUid,
			Dictionary<string, long> playerBreaks)
		{
			long now =
				Environment.TickCount64;

			List<string> expiredKeys =
				new();

			foreach (
				KeyValuePair<string, long> entry
				in playerBreaks)
			{
				if (entry.Value < now)
				{
					expiredKeys.Add(
						entry.Key
					);
				}
			}

			foreach (string key in expiredKeys)
			{
				playerBreaks.Remove(key);
			}

			if (playerBreaks.Count == 0)
			{
				suppressedBreaks.Remove(
					playerUid
				);
			}
		}

		private static string GetPositionKey(
			BlockPos position)
		{
			return
				$"{position.X}:{position.Y}:{position.Z}";
		}
	}

	internal static class CompletionInteractionPatches
	{
		private const string PendingInteractionKey =
			"apprentice:pendingInteraction";

		private const string PendingTargetKey =
			"apprentice:pendingTarget";

		private const string PendingOperationsKey =
			"apprentice:pendingOperations";

		private const long CombatAttributionLifetimeMs =
			120000;

		private static readonly object combatSync =
			new();

		private static readonly Dictionary<
			long,
			CombatHitRecord
		> lastCombatHits =
			new();

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
			lock (combatSync)
			{
				lastCombatHits.Clear();
			}

			serverApi = null;
			experienceManager = null;
		}

		// -----------------------------------------------------------------
		// Combat attribution, Shield and Tank
		// -----------------------------------------------------------------

		public static void HealthDamagePrefix(
			object __instance,
			DamageSource damageSource,
			ref float damage,
			out HealthDamageState? __state)
		{
			__state = null;

			Entity? damagedEntity =
				GetMemberValue(
					__instance,
					"entity",
					"Entity"
				) as Entity;

			if (damagedEntity == null ||
				damagedEntity.World.Side !=
					EnumAppSide.Server)
			{
				return;
			}

			float healthBefore =
				ReadFloatMember(
					__instance,
					"Health"
				);

			if (!float.IsFinite(healthBefore))
			{
				return;
			}

			Entity? damagingEntity =
				damageSource?.GetCauseEntity() ??
				damageSource?.SourceEntity;

			bool wasEntityDamage =
				damagingEntity != null;

			EntityPlayer? attacker =
				damagingEntity as EntityPlayer;

			AssetLocation? weaponCode =
				attacker == null
					? null
					: ResolveWeaponCode(
						damageSource,
						attacker
					);

			if (attacker?.Player is IServerPlayer attackerPlayer &&
				weaponCode != null &&
				damage > 0)
			{
				damage *= (float)SkillTreeRuntime
					.GetWeaponDamageMultiplier(
						attackerPlayer,
						weaponCode,
						damagedEntity,
						damagedEntity is EntityPlayer
					);
			}

			EntityPlayer? damagedPlayer =
				damagedEntity as EntityPlayer;

			if (damagedPlayer?.Player is IServerPlayer damagedServerPlayer &&
				damage > 0)
			{
				float maxHealth =
					ReadFloatMember(
						__instance,
						"MaxHealth"
					);

				float healthRatio =
					maxHealth > 0
						? Math.Clamp(
							healthBefore / maxHealth,
							0,
							1
						)
						: 1;

				bool holdingShield =
					IsShieldStack(
						damagedPlayer.LeftHandItemSlot?.Itemstack
					) ||
					IsShieldStack(
						damagedPlayer.RightHandItemSlot?.Itemstack
					);

				damage *= (float)SkillTreeRuntime
					.GetIncomingDamageMultiplier(
						damagedServerPlayer,
						damageSource,
						healthRatio,
						holdingShield
					);

				if (damage >= healthBefore &&
					SkillTreeRuntime.TryTriggerCheatDeath(damagedServerPlayer))
				{
					float restoredHealth = Math.Max(1, maxHealth * 0.5f);
					WriteFloatMember(__instance, "Health", restoredHealth);
					damage = 0;
				}
			}

			__state =
				new HealthDamageState(
					damagedEntity,
					damagedPlayer,
					healthBefore,
					damage,
					damageSource,
					wasEntityDamage,
					attacker?.PlayerUID,
					weaponCode?.ToShortString(),
					CaptureShield(
						damagedPlayer?
							.LeftHandItemSlot
					),
					CaptureShield(
						damagedPlayer?
							.RightHandItemSlot
					)
				);
		}

		public static void HealthDamagePostfix(
			object __instance,
			ref float damage,
			HealthDamageState? __state)
		{
			if (__state == null)
			{
				return;
			}

			float healthAfter =
				ReadFloatMember(
					__instance,
					"Health"
				);

			if (!float.IsFinite(healthAfter))
			{
				return;
			}

			double actualHealthLost =
				Math.Max(
					0,
					__state.HealthBefore -
					healthAfter
				);

			if (actualHealthLost > 0)
			{
				RememberCombatHit(
					__state.DamagedEntity.EntityId,
					__state.AttackerPlayerUid,
					__state.WeaponCode
				);

				if (__state.WasEntityDamage &&
					__state.DamagedPlayer?
						.Player is
					IServerPlayer damagedPlayer)
				{
					Award(
						new InteractionContext(
							damagedPlayer,
							InteractionNames.DamageTaken,
							BuildDamageTarget(
								__state.DamageSource
							),
							quantity:
								actualHealthLost
						)
					);
				}
			}

			ShieldSnapshot? blockedWith =
				FindShieldDurabilityLoss(
					__state.LeftShield,
					__state.RightShield
				);

			if (blockedWith != null &&
				__state.DamagedPlayer?
						.Player is
					IServerPlayer shieldPlayer)
			{
				Award(
					new InteractionContext(
						shieldPlayer,
						InteractionNames.ShieldBlock,
						blockedWith.ShieldCode
					)
				);
			}
		}

		private static bool IsShieldStack(
			ItemStack? stack)
		{
			string path =
				stack?.Collectible?.Code?.Path ??
				string.Empty;

			return path.Contains(
				"shield",
				StringComparison.OrdinalIgnoreCase
			);
		}

		public static AssetLocation?
			ConsumeWeaponForDeath(
				Entity deadEntity,
				DamageSource damageSource,
				EntityPlayer killerEntity)
		{
			AssetLocation? directWeapon =
				ResolveWeaponCode(
					damageSource,
					killerEntity
				);

			CombatHitRecord? remembered = null;

			lock (combatSync)
			{
				if (lastCombatHits.TryGetValue(
					deadEntity.EntityId,
					out CombatHitRecord? found))
				{
					lastCombatHits.Remove(
						deadEntity.EntityId
					);

					if (found.ExpiresAtMs >=
							Environment.TickCount64 &&
						string.Equals(
							found.AttackerPlayerUid,
							killerEntity.PlayerUID,
							StringComparison.Ordinal
						))
					{
						remembered = found;
					}
				}
			}

			if (directWeapon != null)
			{
				return directWeapon;
			}

			return TryParseAssetLocation(
				remembered?.WeaponCode
			);
		}

		private static void RememberCombatHit(
			long damagedEntityId,
			string? attackerPlayerUid,
			string? weaponCode)
		{
			if (string.IsNullOrWhiteSpace(
					attackerPlayerUid) ||
				string.IsNullOrWhiteSpace(
					weaponCode))
			{
				return;
			}

			long now =
				Environment.TickCount64;

			lock (combatSync)
			{
				lastCombatHits[damagedEntityId] =
					new CombatHitRecord(
						attackerPlayerUid,
						weaponCode,
						now +
						CombatAttributionLifetimeMs
					);

				RemoveExpiredCombatHits(now);
			}
		}

		private static void RemoveExpiredCombatHits(
			long now)
		{
			List<long> expired =
				new();

			foreach (
				KeyValuePair<long, CombatHitRecord>
					entry
				in lastCombatHits)
			{
				if (entry.Value.ExpiresAtMs < now)
				{
					expired.Add(entry.Key);
				}
			}

			foreach (long entityId in expired)
			{
				lastCombatHits.Remove(entityId);
			}
		}

		private static AssetLocation?
			ResolveWeaponCode(
				DamageSource? damageSource,
				EntityPlayer attacker)
		{
			if (damageSource?.SourceEntity is
					Entity sourceEntity &&
				sourceEntity.EntityId !=
					attacker.EntityId)
			{
				ItemStack? firingWeapon =
					ResolveItemStack(
						GetMemberValue(
							sourceEntity,
							"WeaponStack",
							"weaponStack"
						)
					);

				AssetLocation? firingWeaponCode =
					firingWeapon?.Collectible?.Code;

				if (firingWeaponCode != null)
				{
					return firingWeaponCode;
				}

				ItemStack? projectileStack =
					ResolveItemStack(
						GetMemberValue(
							sourceEntity,
							"ProjectileStack",
							"projectileStack"
						)
					);

				AssetLocation? projectileCode =
					projectileStack?
						.Collectible?.Code;

				if (projectileCode != null)
				{
					return projectileCode;
				}

				if (sourceEntity.Code != null)
				{
					return sourceEntity.Code;
				}
			}

			ItemStack? rightHand =
				attacker.RightHandItemSlot?
					.Itemstack;

			AssetLocation? rightHandCode =
				rightHand?.Collectible?.Code;

			if (rightHandCode != null)
			{
				return rightHandCode;
			}

			ItemStack? activeStack =
				attacker.Player?
					.InventoryManager?
					.ActiveHotbarSlot?
					.Itemstack;

			return activeStack?
				.Collectible?.Code;
		}

		private static ShieldSnapshot?
			CaptureShield(
				ItemSlot? slot)
		{
			ItemStack? stack =
				slot?.Itemstack;

			if (slot == null ||
				stack?.Collectible?.Code == null ||
				!IsShield(stack))
			{
				return null;
			}

			int durability =
				stack.Collectible
					.GetRemainingDurability(stack);

			return new ShieldSnapshot(
				slot,
				stack.Collectible.Code,
				durability
			);
		}

		private static bool IsShield(
			ItemStack stack)
		{
			if (stack.ItemAttributes?["shield"]
					.Exists == true)
			{
				return true;
			}

			return stack.Collectible
				.GetType()
				.Name
				.Contains(
					"Shield",
					StringComparison.OrdinalIgnoreCase
				);
		}

		private static ShieldSnapshot?
			FindShieldDurabilityLoss(
				ShieldSnapshot? leftShield,
				ShieldSnapshot? rightShield)
		{
			ShieldSnapshot? best = null;
			int largestLoss = 0;

			foreach (
				ShieldSnapshot? snapshot
				in new[]
				{
					leftShield,
					rightShield
				})
			{
				if (snapshot == null)
				{
					continue;
				}

				ItemStack? afterStack =
					snapshot.Slot.Itemstack;

				int afterDurability = 0;

				if (afterStack?.Collectible?.Code != null &&
					string.Equals(
						afterStack.Collectible.Code
							.ToShortString(),
						snapshot.ShieldCode
							.ToShortString(),
						StringComparison.OrdinalIgnoreCase
					))
				{
					afterDurability =
						afterStack.Collectible
							.GetRemainingDurability(
								afterStack
							);
				}

				int durabilityLoss =
					snapshot.DurabilityBefore -
					afterDurability;

				if (durabilityLoss > largestLoss)
				{
					largestLoss = durabilityLoss;
					best = snapshot;
				}
			}

			return best;
		}

		private static AssetLocation
			BuildDamageTarget(
				DamageSource? damageSource)
		{
			string source =
				damageSource?.Source
					.ToString()
					.ToLowerInvariant() ??
				"unknown";

			string type =
				damageSource?.Type
					.ToString()
					.ToLowerInvariant() ??
				"unknown";

			return new AssetLocation(
				"apprentice",
				$"damage-{source}-{type}"
			);
		}

		private static float ReadFloatMember(
			object instance,
			string memberName)
		{
			object? value =
				GetMemberValue(
					instance,
					memberName
				);

			try
			{
				return value == null
					? float.NaN
					: Convert.ToSingle(value);
			}
			catch
			{
				return float.NaN;
			}
		}

		private static void WriteFloatMember(
			object instance,
			string memberName,
			float value)
		{
			const BindingFlags flags =
				BindingFlags.Instance |
				BindingFlags.Public |
				BindingFlags.NonPublic |
				BindingFlags.DeclaredOnly;

			Type? currentType = instance.GetType();

			while (currentType != null)
			{
				PropertyInfo? property = currentType.GetProperty(memberName, flags);
				if (property?.CanWrite == true)
				{
					try
					{
						property.SetValue(instance, Convert.ChangeType(value, property.PropertyType));
						return;
					}
					catch
					{
					}
				}

				FieldInfo? field = currentType.GetField(memberName, flags);
				if (field != null)
				{
					try
					{
						field.SetValue(instance, Convert.ChangeType(value, field.FieldType));
						return;
					}
					catch
					{
					}
				}

				currentType = currentType.BaseType;
			}
		}

		// -----------------------------------------------------------------
		// Tree felling
		// -----------------------------------------------------------------

		public static void TreeFellingPrefix(
			object __instance,
			object[] __args,
			out TreeFellingState? __state)
		{
			__state = null;

			if (__args.Length < 4 ||
				__args[0] is not
					IWorldAccessor world ||
				__args[1] is not
					EntityPlayer playerEntity ||
				__args[3] is not
					BlockSelection blockSelection ||
				blockSelection.Position == null ||
				world.Side != EnumAppSide.Server ||
				playerEntity.Player is not
					IServerPlayer player)
			{
				return;
			}

			MethodInfo? findTreeMethod =
				AccessTools.Method(
					__instance.GetType(),
					"FindTree"
				);

			if (findTreeMethod == null)
			{
				return;
			}

			object[] findTreeArguments =
			[
				world,
				blockSelection.Position,
				0,
				0
			];

			object? foundTree;

			try
			{
				foundTree =
					findTreeMethod.Invoke(
						__instance,
						findTreeArguments
					);
			}
			catch
			{
				return;
			}

			if (foundTree is not
				System.Collections.IEnumerable positions)
			{
				return;
			}

			List<TreeBlockSnapshot> treeBlocks =
				new();

			foreach (object? entry in positions)
			{
				if (entry is not BlockPos position)
				{
					continue;
				}

				Block block =
					world.BlockAccessor.GetBlock(
						position
					);

				if (block?.Code == null)
				{
					continue;
				}

				treeBlocks.Add(
					new TreeBlockSnapshot(
						position.Copy(),
						block.Id,
						block.BlockMaterial ==
							EnumBlockMaterial.Wood
					)
				);
			}

			// A single ordinary log should continue through the normal
			// DidBreakBlock interaction. This hook is only for actual
			// multi-block tree felling.
			if (treeBlocks.Count <= 1)
			{
				return;
			}

			Block startBlock =
				world.BlockAccessor.GetBlock(
					blockSelection.Position
				);

			if (startBlock?.Code == null)
			{
				return;
			}

			InteractionEventSuppression
				.SuppressBlockBreaks(
					player.PlayerUID,
					GetTreePositions(treeBlocks),
					Environment.TickCount64
				);

			__state =
				new TreeFellingState(
					player,
					world,
					startBlock.Code,
					treeBlocks
				);
		}

		public static void TreeFellingPostfix(
			bool __result,
			TreeFellingState? __state)
		{
			if (!__result ||
				__state == null)
			{
				return;
			}

			int brokenWoodBlocks = 0;

			foreach (
				TreeBlockSnapshot snapshot
				in __state.TreeBlocks)
			{
				if (!snapshot.WasWood)
				{
					continue;
				}

				Block blockAfter =
					__state.World.BlockAccessor
						.GetBlock(
							snapshot.Position
						);

				if (blockAfter.Id !=
					snapshot.OriginalBlockId)
				{
					brokenWoodBlocks++;
				}
			}

			if (brokenWoodBlocks <= 0)
			{
				return;
			}

			int rewardedWoodBlocks =
				Math.Min(
					20,
					brokenWoodBlocks
				);

			Award(
				new InteractionContext(
					__state.Player,
					InteractionNames.DestroyBlock,
					__state.TargetCode,
					quantity:
						rewardedWoodBlocks
				)
			);

			serverApi?.Logger.Debug(
				"[Apprentice] Tree felling awarded " +
				$"{rewardedWoodBlocks} wood-block units " +
				$"from {brokenWoodBlocks} actually broken " +
				$"wood blocks to {__state.Player.PlayerName}."
			);
		}

		private static IEnumerable<BlockPos>
			GetTreePositions(
				IReadOnlyList<TreeBlockSnapshot> treeBlocks)
		{
			foreach (
				TreeBlockSnapshot snapshot
				in treeBlocks)
			{
				yield return snapshot.Position;
			}
		}

		// -----------------------------------------------------------------
		// Craft / Repair
		// -----------------------------------------------------------------

		public static void GridRecipeConsumeInputPostfix(
			GridRecipe __instance,
			IPlayer byPlayer,
			bool __result)
		{
			if (!__result ||
				byPlayer is not IServerPlayer serverPlayer)
			{
				return;
			}

			ItemStack? outputStack =
				ResolveItemStack(__instance.Output);

			AssetLocation? outputCode =
				outputStack?.Collectible?.Code;

			if (outputCode == null)
			{
				return;
			}

			bool isRepair =
				__instance.Name?.Path?.Contains(
					"repair",
					StringComparison.OrdinalIgnoreCase
				) == true;

			Award(
				new InteractionContext(
					serverPlayer,
					isRepair
						? InteractionNames.Repair
						: InteractionNames.Craft,
					outputCode
				)
			);

			if (!isRepair)
			{
				RemainingInteractionPatches.GrantBonusOutput(
					serverPlayer,
					outputCode,
					Math.Max(
						1,
						outputStack?.StackSize ?? 1
					),
					"CraftYield"
				);
				RemainingInteractionPatches.GrantBonusOutput(
					serverPlayer,
					outputCode,
					Math.Max(
						1,
						outputStack?.StackSize ?? 1
					),
					"JoineryYield"
				);
				RemainingInteractionPatches.GrantBonusOutput(
					serverPlayer,
					outputCode,
					Math.Max(
						1,
						outputStack?.StackSize ?? 1
					),
					"FurnitureYield"
				);
			}
		}

		// -----------------------------------------------------------------
		// Smithing
		// -----------------------------------------------------------------

		public static void SmithingPrefix(
			object __instance,
			object[] __args,
			out SmithingState? __state)
		{
			__state = null;

			if (__args.Length < 1 ||
				__args[0] is not IServerPlayer player)
			{
				return;
			}

			object? selectedRecipe =
				GetMemberValue(
					__instance,
					"SelectedRecipe"
				);

			if (selectedRecipe == null ||
				!InvokeBoolean(
					__instance,
					"MatchesRecipe"
				))
			{
				return;
			}

			ItemStack? outputStack =
				ResolveItemStack(
					GetMemberValue(
						selectedRecipe,
						"Output"
					)
				);

			AssetLocation? outputCode =
				outputStack?.Collectible?.Code;

			if (outputCode == null)
			{
				return;
			}

			__state =
				new SmithingState(
					player,
					outputCode
				);
		}

		public static void SmithingPostfix(
			SmithingState? __state)
		{
			if (__state == null)
			{
				return;
			}

			Award(
				new InteractionContext(
					__state.Player,
					InteractionNames.Smith,
					__state.TargetCode
				)
			);

			RemainingInteractionPatches.GrantBonusOutput(
				__state.Player,
				__state.TargetCode,
				1,
				"SmithingYield"
			);
		}

		// -----------------------------------------------------------------
		// Clay forming
		// -----------------------------------------------------------------

		public static void ClayFormingPrefix(
			object __instance,
			object[] __args,
			out ClayFormingState? __state)
		{
			__state = null;

			if (__args.Length < 2 ||
				__args[0] is not
					IServerPlayer player)
			{
				return;
			}

			object? selectedRecipe =
				GetMemberValue(
					__instance,
					"SelectedRecipe",
					"selectedRecipe"
				);

			ItemStack? outputStack =
				ResolveItemStack(
					GetMemberValue(
						selectedRecipe,
						"Output"
					)
				);

			AssetLocation? outputCode =
				outputStack?.Collectible?.Code;

			if (selectedRecipe == null ||
				outputCode == null)
			{
				return;
			}

			__state =
				new ClayFormingState(
					player,
					outputCode,
					selectedRecipe
				);
		}

		public static void ClayFormingPostfix(
			object __instance,
			ClayFormingState? __state)
		{
			if (__state == null)
			{
				return;
			}

			object? recipeAfter =
				GetMemberValue(
					__instance,
					"SelectedRecipe",
					"selectedRecipe"
				);

			// Vanilla clears SelectedRecipe only when the pattern really
			// completed on the server and the output was created.
			if (recipeAfter != null)
			{
				return;
			}

			Award(
				new InteractionContext(
					__state.Player,
					InteractionNames.Craft,
					__state.TargetCode
				)
			);

			RemainingInteractionPatches.GrantBonusOutput(
				__state.Player,
				__state.TargetCode,
				1,
				"ClayYield"
			);
			RemainingInteractionPatches.GrantBonusOutput(
				__state.Player,
				__state.TargetCode,
				1,
				"RawClayYield"
			);
			RemainingInteractionPatches.GrantBonusOutput(
				__state.Player,
				__state.TargetCode,
				1,
				"DecorativeClayYield"
			);
			RemainingInteractionPatches.GrantBonusOutput(
				__state.Player,
				__state.TargetCode,
				1,
				"StorageClayYield"
			);
		}

		// -----------------------------------------------------------------
		// Prospecting
		// -----------------------------------------------------------------

		public static void ProspectNodePrefix(
			object[] __args,
			out InteractionContext? __state)
		{
			__state = null;

			if (__args.Length < 4 ||
				__args[0] is not IWorldAccessor world ||
				__args[1] is not EntityPlayer playerEntity ||
				__args[3] is not BlockSelection selection ||
				selection.Position == null ||
				playerEntity.Player is not
					IServerPlayer player)
			{
				return;
			}

			Block block =
				world.BlockAccessor.GetBlock(
					selection.Position
				);

			if (block?.Code == null ||
				block.Attributes?["propickable"]
					.AsBool(false) != true)
			{
				return;
			}

			InteractionEventSuppression
				.SuppressNextBlockBreak(
					player.PlayerUID,
					selection.Position,
					Environment.TickCount64
				);

			__state =
				new InteractionContext(
					player,
					InteractionNames.Prospect,
					block.Code,
					position:
						selection.Position.Copy()
				);
		}

		public static void ProspectNodePostfix(
			InteractionContext? __state)
		{
			if (__state != null)
			{
				Award(__state);
			}
		}

		public static void ProspectDensityPostfix(
			object[] __args)
		{
			if (__args.Length < 3 ||
				__args[1] is not IServerPlayer player ||
				__args[2] is not ItemSlot itemSlot)
			{
				return;
			}

			AssetLocation? toolCode =
				itemSlot.Itemstack?
					.Collectible?.Code;

			if (toolCode == null)
			{
				return;
			}

			Award(
				new InteractionContext(
					player,
					InteractionNames.Prospect,
					toolCode
				)
			);
		}

		// -----------------------------------------------------------------
		// Milking
		// -----------------------------------------------------------------

		public static void MilkingPrefix(
			object __instance,
			object[] __args,
			out MilkingState? __state)
		{
			__state = null;

			if (__args.Length < 2 ||
				__args[1] is not
					EntityPlayer playerEntity ||
				playerEntity.Player is not
					IServerPlayer player)
			{
				return;
			}

			Entity? milkedEntity =
				GetMemberValue(
					__instance,
					"entity"
				) as Entity;

			if (milkedEntity?.Code == null ||
				milkedEntity.World.Side !=
					EnumAppSide.Server)
			{
				return;
			}

			double previousHours =
				milkedEntity.WatchedAttributes
					.GetFloat(
						"lastMilkedTotalHours"
					);

			__state =
				new MilkingState(
					player,
					milkedEntity,
					previousHours
				);
		}

		public static void MilkingPostfix(
			MilkingState? __state)
		{
			if (__state == null)
			{
				return;
			}

			double newHours =
				__state.Entity.WatchedAttributes
					.GetFloat(
						"lastMilkedTotalHours"
					);

			if (newHours <=
				__state.PreviousMilkedHours)
			{
				return;
			}

			Award(
				new InteractionContext(
					__state.Player,
					InteractionNames.Milk,
					__state.Entity.Code
				)
			);
		}

		// -----------------------------------------------------------------
		// Firepit Smelt / Cook
		// -----------------------------------------------------------------

		public static void FirepitSmeltPrefix(
			object __instance,
			out OutputSnapshot? __state)
		{
			__state =
				CaptureOutputSnapshot(
					ResolveOutputSlot(
						__instance,
						preferredSlotNames:
						[
							"outputSlot",
							"OutputSlot"
						],
						inventorySlotIndex: 2
					)
				);
		}

		public static void FirepitSmeltPostfix(
			object __instance,
			OutputSnapshot? __state)
		{
			ItemSlot? outputSlot =
				ResolveOutputSlot(
					__instance,
					preferredSlotNames:
					[
						"outputSlot",
						"OutputSlot"
					],
					inventorySlotIndex: 2
				);

			ItemStack? outputStack =
				outputSlot?.Itemstack;

			if (!DidOutputIncrease(
				__state,
				outputStack))
			{
				return;
			}

			(
				string interaction,
				AssetLocation target
			) = ClassifyFirepitOutput(outputStack);

			AddPendingOperation(
				outputStack,
				interaction,
				target
			);
		}

		// -----------------------------------------------------------------
		// Quern Process
		// -----------------------------------------------------------------

		public static void QuernProcessPrefix(
			object __instance,
			out OutputSnapshot? __state)
		{
			__state =
				CaptureOutputSnapshot(
					ResolveOutputSlot(
						__instance,
						preferredSlotNames:
						[
							"OutputSlot",
							"outputSlot"
						],
						inventorySlotIndex: 1
					)
				);
		}

		public static void QuernProcessPostfix(
			object __instance,
			OutputSnapshot? __state)
		{
			ItemSlot? outputSlot =
				ResolveOutputSlot(
					__instance,
					preferredSlotNames:
					[
						"OutputSlot",
						"outputSlot"
					],
					inventorySlotIndex: 1
				);

			ItemStack? outputStack =
				outputSlot?.Itemstack;

			if (!DidOutputIncrease(
				__state,
				outputStack) ||
				outputStack?.Collectible?.Code == null)
			{
				return;
			}

			AddPendingOperation(
				outputStack,
				InteractionNames.Process,
				outputStack.Collectible.Code
			);
		}

		// -----------------------------------------------------------------
		// Player pickup attribution for asynchronous machine outputs.
		// -----------------------------------------------------------------

		public static void InventoryActivateSlotPrefix(
			InventoryBase __instance,
			int slotId,
			ItemSlot sourceSlot,
			ref ItemStackMoveOperation op,
			out PendingPickupState? __state)
		{
			__state = null;

			if (op.ActingPlayer is not
					IServerPlayer player ||
				slotId < 0)
			{
				return;
			}

			ItemSlot clickedSlot;

			try
			{
				clickedSlot = __instance[slotId];
			}
			catch
			{
				return;
			}

			ItemStack? stack =
				clickedSlot.Itemstack;

			PendingMarker? marker =
				ReadPendingMarker(stack);

			if (stack == null ||
				marker == null ||
				stack.StackSize <= 0)
			{
				return;
			}

			__state =
				new PendingPickupState(
					player,
					clickedSlot,
					stack,
					stack.StackSize,
					op.MovedQuantity,
					marker
				);

			ClearPendingMarker(stack);
		}

		public static void InventoryActivateSlotPostfix(
			ref ItemStackMoveOperation op,
			PendingPickupState? __state)
		{
			if (__state == null)
			{
				return;
			}

			int movedQuantity =
				Math.Max(
					0,
					op.MovedQuantity -
					__state.MovedBefore
				);

			ItemStack? remainingStack =
				__state.ClickedSlot.Itemstack;

			if (movedQuantity <= 0)
			{
				RestorePendingMarker(
					remainingStack ??
					__state.OriginalStack,
					__state.Marker
				);

				return;
			}

			int awardedOperations;
			int remainingOperations;

			if (movedQuantity >=
					__state.OriginalStackSize ||
				remainingStack == null)
			{
				awardedOperations =
					__state.Marker.Operations;

				remainingOperations = 0;
			}
			else
			{
				awardedOperations =
					(
						__state.Marker.Operations *
						movedQuantity
					) /
					__state.OriginalStackSize;

				remainingOperations =
					__state.Marker.Operations -
					awardedOperations;
			}

			if (remainingOperations > 0 &&
				remainingStack != null)
			{
				RestorePendingMarker(
					remainingStack,
					__state.Marker with
					{
						Operations =
							remainingOperations
					}
				);
			}

			if (awardedOperations <= 0)
			{
				return;
			}

			AssetLocation? targetCode =
				TryParseAssetLocation(
					__state.Marker.TargetCode
				);

			if (targetCode == null)
			{
				return;
			}

			Award(
				new InteractionContext(
					__state.Player,
					__state.Marker.Interaction,
					targetCode,
					quantity:
						awardedOperations
				)
			);

			string? yieldEffect =
				__state.Marker.Interaction switch
				{
					InteractionNames.Cook => "CookYield",
					InteractionNames.Smelt => "SmeltYield",
					InteractionNames.Process => "ProcessYield",
					_ => null
				};

			if (yieldEffect != null)
			{
				RemainingInteractionPatches.GrantBonusOutput(
					__state.Player,
					targetCode,
					movedQuantity,
					yieldEffect
				);
			}
		}

		// -----------------------------------------------------------------
		// Shared helpers
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
				manager.HandleInteraction(context);
			}
			catch (Exception exception)
			{
				api.Logger.Error(
					"[Apprentice] Completion interaction " +
					$"'{context.Interaction}' failed."
				);

				api.Logger.Error(exception);
			}
		}

		private static (
			string Interaction,
			AssetLocation Target
		) ClassifyFirepitOutput(
			ItemStack outputStack)
		{
			string? recipeCode =
				outputStack.Attributes.GetString(
					"recipeCode",
					null
				);

			if (!string.IsNullOrWhiteSpace(
				recipeCode))
			{
				AssetLocation recipeLocation =
					new(recipeCode);

				return (
					InteractionNames.Cook,
					new AssetLocation(
						recipeLocation.Domain,
						"meal-" +
						recipeLocation.Path
					)
				);
			}

			AssetLocation outputCode =
				outputStack.Collectible.Code;

			string path =
				outputCode.Path;

			bool isCookingOutput =
				path.Contains(
					"meal",
					StringComparison.OrdinalIgnoreCase
				) ||
				path.Contains(
					"bread",
					StringComparison.OrdinalIgnoreCase
				) ||
				path.Contains(
					"pie",
					StringComparison.OrdinalIgnoreCase
				);

			return isCookingOutput
				? (
					InteractionNames.Cook,
					outputCode
				)
				: (
					InteractionNames.Smelt,
					outputCode
				);
		}

		private static ItemSlot? ResolveOutputSlot(
			object instance,
			string[] preferredSlotNames,
			int inventorySlotIndex)
		{
			foreach (string name in
					 preferredSlotNames)
			{
				if (GetMemberValue(
					instance,
					name
				) is ItemSlot slot)
				{
					return slot;
				}
			}

			object? inventoryObject =
				GetMemberValue(
					instance,
					"Inventory",
					"inventory"
				);

			if (inventoryObject is
				InventoryBase inventory)
			{
				try
				{
					return inventory[
						inventorySlotIndex
					];
				}
				catch
				{
					return null;
				}
			}

			return null;
		}

		private static OutputSnapshot?
			CaptureOutputSnapshot(
				ItemSlot? slot)
		{
			ItemStack? stack =
				slot?.Itemstack;

			if (stack == null)
			{
				return new OutputSnapshot(
					null,
					0
				);
			}

			return new OutputSnapshot(
				stack.Collectible?.Code
					?.ToShortString(),
				stack.StackSize
			);
		}

		private static bool DidOutputIncrease(
			OutputSnapshot? before,
			ItemStack? after)
		{
			if (after?.Collectible?.Code == null)
			{
				return false;
			}

			if (before == null ||
				string.IsNullOrWhiteSpace(
					before.Code))
			{
				return after.StackSize > 0;
			}

			string afterCode =
				after.Collectible.Code
					.ToShortString();

			if (!string.Equals(
				before.Code,
				afterCode,
				StringComparison.OrdinalIgnoreCase
			))
			{
				return true;
			}

			return after.StackSize >
				before.StackSize;
		}

		private static void AddPendingOperation(
			ItemStack stack,
			string interaction,
			AssetLocation targetCode)
		{
			PendingMarker? existing =
				ReadPendingMarker(stack);

			int operations = 1;

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
				operations +=
					existing.Operations;
			}

			RestorePendingMarker(
				stack,
				new PendingMarker(
					interaction,
					targetCode.ToShortString(),
					operations
				)
			);
		}

		private static PendingMarker? ReadPendingMarker(
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
				string.IsNullOrWhiteSpace(target) ||
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

		private static void RestorePendingMarker(
			ItemStack? stack,
			PendingMarker marker)
		{
			if (stack == null)
			{
				return;
			}

			ITreeAttribute attributes =
				stack.TempAttributes;

			attributes.SetString(
				PendingInteractionKey,
				marker.Interaction
			);

			attributes.SetString(
				PendingTargetKey,
				marker.TargetCode
			);

			attributes.SetInt(
				PendingOperationsKey,
				marker.Operations
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

		private static ItemStack? ResolveItemStack(
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

			foreach (string memberName in
					 new[]
					 {
						 "ResolvedItemStack",
						 "ResolvedItemstack",
						 "Itemstack",
						 "ItemStack",
						 "Output"
					 })
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
			object instance,
			params string[] memberNames)
		{
			const BindingFlags flags =
				BindingFlags.Instance |
				BindingFlags.Public |
				BindingFlags.NonPublic |
				BindingFlags.DeclaredOnly;

			Type? currentType =
				instance.GetType();

			while (currentType != null)
			{
				foreach (string memberName in
						 memberNames)
				{
					PropertyInfo? property =
						currentType.GetProperty(
							memberName,
							flags
						);

					if (property != null &&
						property.GetIndexParameters()
							.Length == 0)
					{
						try
						{
							return property.GetValue(
								instance
							);
						}
						catch
						{
							// Continue with fields/base types.
						}
					}

					FieldInfo? field =
						currentType.GetField(
							memberName,
							flags
						);

					if (field != null)
					{
						try
						{
							return field.GetValue(
								instance
							);
						}
						catch
						{
							// Continue with base types.
						}
					}
				}

				currentType =
					currentType.BaseType;
			}

			return null;
		}

		private static bool InvokeBoolean(
			object instance,
			string methodName)
		{
			MethodInfo? method =
				AccessTools.Method(
					instance.GetType(),
					methodName
				);

			if (method == null)
			{
				return false;
			}

			try
			{
				return method.Invoke(
					instance,
					parameters: null
				) is true;
			}
			catch
			{
				return false;
			}
		}

		private static AssetLocation?
			TryParseAssetLocation(
				string? code)
		{
			if (string.IsNullOrWhiteSpace(
				code))
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

		internal sealed record CombatHitRecord(
			string AttackerPlayerUid,
			string WeaponCode,
			long ExpiresAtMs
		);

		internal sealed record ShieldSnapshot(
			ItemSlot Slot,
			AssetLocation ShieldCode,
			int DurabilityBefore
		);

		internal sealed record HealthDamageState(
			Entity DamagedEntity,
			EntityPlayer? DamagedPlayer,
			float HealthBefore,
			float IncomingDamage,
			DamageSource DamageSource,
			bool WasEntityDamage,
			string? AttackerPlayerUid,
			string? WeaponCode,
			ShieldSnapshot? LeftShield,
			ShieldSnapshot? RightShield
		);

		internal sealed record TreeBlockSnapshot(
			BlockPos Position,
			int OriginalBlockId,
			bool WasWood
		);

		internal sealed record TreeFellingState(
			IServerPlayer Player,
			IWorldAccessor World,
			AssetLocation TargetCode,
			IReadOnlyList<TreeBlockSnapshot> TreeBlocks
		);

		internal sealed record SmithingState(
			IServerPlayer Player,
			AssetLocation TargetCode
		);

		internal sealed record ClayFormingState(
			IServerPlayer Player,
			AssetLocation TargetCode,
			object SelectedRecipeBefore
		);

		internal sealed record MilkingState(
			IServerPlayer Player,
			Entity Entity,
			double PreviousMilkedHours
		);

		internal sealed record OutputSnapshot(
			string? Code,
			int StackSize
		);

		internal sealed record PendingMarker(
			string Interaction,
			string TargetCode,
			int Operations
		);

		internal sealed record PendingPickupState(
			IServerPlayer Player,
			ItemSlot ClickedSlot,
			ItemStack OriginalStack,
			int OriginalStackSize,
			int MovedBefore,
			PendingMarker Marker
		);
	}

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
			if (instance == null)
			{
				return null;
			}

			const BindingFlags flags =
				BindingFlags.Instance |
				BindingFlags.Public |
				BindingFlags.NonPublic |
				BindingFlags.DeclaredOnly;

			Type? currentType =
				instance.GetType();

			while (currentType != null)
			{
				foreach (string memberName in
						 memberNames)
				{
					PropertyInfo? property =
						currentType.GetProperty(
							memberName,
							flags
						);

					if (property != null &&
						property.GetIndexParameters()
							.Length == 0)
					{
						try
						{
							return property.GetValue(
								instance
							);
						}
						catch
						{
							// Try fields and base types.
						}
					}

					FieldInfo? field =
						currentType.GetField(
							memberName,
							flags
						);

					if (field != null)
					{
						try
						{
							return field.GetValue(
								instance
							);
						}
						catch
						{
							// Try base types.
						}
					}
				}

				currentType =
					currentType.BaseType;
			}

			return null;
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
