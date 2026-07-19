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
					EnumAppSide.Server ||
				damageSource == null)
			{
				return;
			}

			float healthBefore =
				ReadFloatMember(
					__instance,
					"Health"
				);

			// Several creature health behaviors do not expose a public Health
			// member.  Combat attribution still has to be captured for them.
			if (!float.IsFinite(healthBefore)) healthBefore = 0;

			Entity? damagingEntity =
				damageSource.GetCauseEntity() ??
				damageSource.SourceEntity;

			bool wasEntityDamage =
				damagingEntity != null;

			if (wasEntityDamage && damage > 0)
			{
				damage *= (float)DangerTierRuntime
					.GetDamageMultiplier(damagingEntity);
			}

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
					),
					PoisonRuntime.CaptureProjectileHit(
						damageSource
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

			// Projectile identification is captured in the prefix while the
			// projectile entity and its stack still exist.  Apply poison here
			// independently of the reflected health value; some creature health
			// behaviors expose no readable Health member even though the hit lands.
			PoisonRuntime.ApplyConfirmedProjectileHit(
				__state.DamagedEntity,
				__state.PoisonHit
			);

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
			ReflectionMemberAccessor.WriteConverted(
				instance,
				memberName,
				value
			);
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
			if (selectedRecipe == null)
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

			if (outputStack == null || !DidOutputIncrease(
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
			return ReflectionMemberAccessor.GetValue(
				instance,
				memberNames
			);
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
			ShieldSnapshot? RightShield,
			PoisonProjectileHit? PoisonHit
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
}
