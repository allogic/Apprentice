using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;

namespace Apprentice
{
	internal static class SkillTreeRuntime
	{
		public static SkillTreeManager? Manager { get; set; }

		public static double GetExperienceMultiplier(
			IServerPlayer player,
			string classId)
		{
			return Manager?.GetExperienceMultiplier(player, classId) ?? 1;
		}

		public static double GetWeaponDamageMultiplier(
			IServerPlayer player,
			AssetLocation weaponCode,
			Entity target,
			bool isPvP)
		{
			return Manager?.GetWeaponDamageMultiplier(
				player,
				weaponCode,
				target,
				isPvP
			) ?? 1;
		}

		public static double GetIncomingDamageMultiplier(
			IServerPlayer player,
			DamageSource source,
			float healthRatio,
			bool holdingShield)
		{
			return Manager?.GetIncomingDamageMultiplier(
				player,
				source,
				healthRatio,
				holdingShield
			) ?? 1;
		}

		public static double GetWoodMiningMultiplier(IPlayer player)
		{
			return Manager?.GetWoodMiningMultiplier(player) ?? 1;
		}

		public static double GetEffectValue(
			IServerPlayer player,
			string effectType,
			string? contextCode = null)
		{
			return Manager?.GetEffectValue(
				player,
				effectType,
				contextCode
			) ?? 0;
		}

		public static double GetEffectValue(
			IPlayer player,
			string effectType,
			string? contextCode = null)
		{
			return player is IServerPlayer serverPlayer
				? GetEffectValue(serverPlayer, effectType, contextCode)
				: 0;
		}

		public static bool RollEffect(
			IServerPlayer player,
			string effectType,
			string? contextCode = null)
		{
			return Manager?.RollEffect(
				player,
				effectType,
				contextCode
			) == true;
		}

		public static bool TryTriggerCheatDeath(
			IServerPlayer player)
		{
			return Manager?.TryTriggerCheatDeath(player) ?? false;
		}

		public static bool IsHiddenClassUnlocked(
			Entity entity,
			string hiddenClassId)
		{
			return HiddenClassData.IsUnlocked(entity, hiddenClassId);
		}

		public static void ReevaluateDiscoveries(IServerPlayer player)
		{
			Manager?.DiscoverHiddenClasses(player);
		}

		public static bool CanCraftLockedRecipe(
			IPlayer player,
			AssetLocation outputCode,
			out string requiredNode)
		{
			if (Manager == null)
			{
				requiredNode = string.Empty;
				return true;
			}

			return Manager.CanCraftLockedRecipe(
				player,
				outputCode,
				out requiredNode
			);
		}
	}

	internal sealed class SkillTreeManager : IDisposable
	{
		private const int MasteryCap = 50;
		private const string StatCodePrefix = "apprentice-";

		private readonly ICoreServerAPI serverApi;
		private readonly ClassConfig classConfig;
		private readonly SkillTreeConfig skillConfig;
		private readonly IServerNetworkChannel channel;
		private readonly Dictionary<string, (string ClassId, string NodeId)>
			recipeLocks = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, long>
			lastTrackingNoticeMs = new(StringComparer.OrdinalIgnoreCase);
		private long trackingListenerId;

		public SkillTreeManager(
			ICoreServerAPI serverApi,
			ClassConfig classConfig,
			SkillTreeConfig skillConfig,
			IServerNetworkChannel channel)
		{
			this.serverApi = serverApi;
			this.classConfig = classConfig;
			this.skillConfig = skillConfig;
			this.channel = channel;

			BuildRecipeLocks();
			channel.SetMessageHandler<SkillPurchaseRequestPacket>(OnPurchaseRequest);
			serverApi.Event.PlayerJoin += OnPlayerJoin;
			trackingListenerId = serverApi.Event.RegisterGameTickListener(
				OnTrackingTick,
				2000
			);
			SkillTreeRuntime.Manager = this;
		}

		public void OnClassLevelChanged(IServerPlayer player, string classId)
		{
			ApplyPlayerStats(player);
		}

		public double GetExperienceMultiplier(
			IServerPlayer player,
			string classId)
		{
			if (!skillConfig.Trees.TryGetValue(classId, out SkillTreeDefinition? tree))
			{
				return 1;
			}

			int mastery = GetMasteryRank(player.Entity, classId);
			double bonus = GetPassiveValue(
				tree,
				mastery,
				"ExperienceGain"
			);

			foreach (SkillNodeDefinition node in tree.Nodes)
			{
				int rank = SkillTreeData.GetNodeRank(player.Entity, classId, node.Id);
				if (rank <= 0) continue;

				foreach (SkillEffectDefinition effect in node.Effects)
				{
					if (effect.Type.Equals("ExperienceGain", StringComparison.OrdinalIgnoreCase))
					{
						bonus += effect.ValuePerRank * rank;
					}
				}
			}
			return Math.Max(
				0.1,
				(1 + bonus) * RaceAppearanceSystem
					.GetProfessionExperienceMultiplier(player.Entity, classId)
			);
		}

		public double GetWoodMiningMultiplier(IPlayer player)
		{
			if (player is not IServerPlayer serverPlayer ||
				!skillConfig.Trees.TryGetValue("woodworker", out SkillTreeDefinition? tree))
			{
				return 1;
			}

			int mastery = GetMasteryRank(serverPlayer.Entity, "woodworker");
			double bonus = GetPassiveValue(tree, mastery, "WoodMiningSpeed");
			bonus += GetNodeEffectTotal(serverPlayer.Entity, tree, "ToolSpeed");
			return 1 + bonus;
		}

		public double GetWeaponDamageMultiplier(
			IServerPlayer player,
			AssetLocation weaponCode,
			Entity target,
			bool isPvP)
		{
			string code = weaponCode.ToShortString();
			double bonus = 0;

			foreach (SkillTreeDefinition tree in skillConfig.Trees.Values)
			{
				if (tree.WeaponPatterns.Count == 0 ||
					!tree.WeaponPatterns.Any(pattern => WildcardMatch(pattern, code)))
				{
					continue;
				}

				int mastery = GetMasteryRank(player.Entity, tree.ClassId);
				bonus += GetPassiveValue(tree, mastery, "WeaponDamage");
				bonus += GetNodeEffectTotal(player.Entity, tree, "WeaponDamage", code);
			}

			bool animalTarget =
				target is EntityAgent &&
				target is not EntityPlayer;

			if (animalTarget)
			{
				bonus += GetEffectValue(player, "AnimalDamage", target.Code?.ToShortString());
			}

			bool rangedWeapon =
				code.Contains("bow", StringComparison.OrdinalIgnoreCase) ||
				code.Contains("crossbow", StringComparison.OrdinalIgnoreCase) ||
				code.Contains("sling", StringComparison.OrdinalIgnoreCase) ||
				code.Contains("arrow", StringComparison.OrdinalIgnoreCase) ||
				code.Contains("bolt", StringComparison.OrdinalIgnoreCase);

			if (rangedWeapon)
			{
				bonus += GetEffectValue(player, "RangedDamage", code);

				double critChance =
					GetEffectValue(player, "RangedCritChance", code);

				if (critChance > 0 &&
					serverApi.World.Rand.NextDouble() < critChance)
				{
					bonus += Math.Max(
						0.25,
						GetEffectValue(player, "RangedCritDamage", code)
					);
				}
			}

			if (rangedWeapon &&
				HiddenClassData.IsUnlocked(player.Entity, "shadowarcher") &&
				target is EntityAgent &&
				target is not EntityPlayer &&
				IsTargetFacingAway(player.Entity, target))
			{
				// The hidden Shadow Archer class guarantees a critical arrow
				// against PvE targets that are not looking at the player.
				bonus += 1.0;
			}

			double lowHealthDamage =
				GetEffectValue(player, "LowHealthDamage", code);

			if (lowHealthDamage > 0 &&
				GetHealthRatio(player.Entity) <= 0.35f)
			{
				bonus += lowHealthDamage;
			}

			if (isPvP)
			{
				bonus *= 0.5;
			}

			return Math.Max(0.1, 1 + bonus);
		}

		public double GetIncomingDamageMultiplier(
			IServerPlayer player,
			DamageSource source,
			float healthRatio,
			bool holdingShield)
		{
			double reduction =
				GetEffectValue(player, "DamageReduction");

			if (holdingShield)
			{
				reduction +=
					GetEffectValue(player, "ShieldedDamageReduction");
			}

			if (healthRatio <= 0.35f)
			{
				reduction +=
					GetEffectValue(player, "LowHealthDamageReduction");
			}

			string sourceName =
				source.Source.ToString() + " " +
				source.Type.ToString();

			if (sourceName.Contains("Fall", StringComparison.OrdinalIgnoreCase))
			{
				reduction +=
					GetEffectValue(player, "FallDamageReduction");
			}

			if (sourceName.Contains("Fire", StringComparison.OrdinalIgnoreCase) ||
				sourceName.Contains("Heat", StringComparison.OrdinalIgnoreCase) ||
				sourceName.Contains("Burn", StringComparison.OrdinalIgnoreCase))
			{
				reduction +=
					GetEffectValue(player, "FireDamageReduction");
			}

			if (sourceName.Contains("Projectile", StringComparison.OrdinalIgnoreCase) ||
				sourceName.Contains("Arrow", StringComparison.OrdinalIgnoreCase))
			{
				reduction +=
					GetEffectValue(player, "ProjectileDamageReduction");
			}

			Entity? sourceEntity =
				source.GetCauseEntity() ??
				source.SourceEntity;

			if (sourceEntity is EntityAgent &&
				sourceEntity is not EntityPlayer)
			{
				reduction +=
					GetEffectValue(player, "AnimalDamageReduction");

				string sourceCode =
					sourceEntity.Code?.ToShortString() ??
					string.Empty;

				if (sourceCode.Contains("bee", StringComparison.OrdinalIgnoreCase) ||
					sourceCode.Contains("wasp", StringComparison.OrdinalIgnoreCase) ||
					sourceCode.Contains("hornet", StringComparison.OrdinalIgnoreCase))
				{
					reduction +=
						GetEffectValue(player, "BeeDamageReduction");
				}
			}

			if (HiddenClassData.IsUnlocked(player.Entity, "deepwarden") &&
				player.Entity.Pos.Y < serverApi.World.SeaLevel - 4)
			{
				reduction += GetHiddenEffectValue(
					player.Entity,
					"UndergroundDamageReduction"
				);
			}

			reduction = Math.Clamp(reduction, 0, 0.75);
			return 1 - reduction;
		}

		public double GetEffectValue(
			IServerPlayer player,
			string effectType,
			string? contextCode = null)
		{
			double total = 0;

			foreach (SkillTreeDefinition tree in skillConfig.Trees.Values)
			{
				int mastery =
					GetMasteryRank(
						player.Entity,
						tree.ClassId
					);

				total += GetPassiveValue(
					tree,
					mastery,
					effectType
				);

				total += GetNodeEffectTotal(
					player.Entity,
					tree,
					effectType,
					contextCode
				);
			}

			total += GetHiddenEffectValue(
				player.Entity,
				effectType,
				contextCode
			);

			return total;
		}

		public bool RollEffect(
			IServerPlayer player,
			string effectType,
			string? contextCode = null)
		{
			double chance =
				Math.Clamp(
					GetEffectValue(player, effectType, contextCode),
					0,
					1
				);

			return chance > 0 &&
				serverApi.World.Rand.NextDouble() < chance;
		}

		public bool CanCraftLockedRecipe(
			IPlayer player,
			AssetLocation outputCode,
			out string requiredNode)
		{
			requiredNode = string.Empty;
			if (!recipeLocks.TryGetValue(outputCode.ToShortString(), out var requirement))
			{
				return true;
			}

			requiredNode = requirement.NodeId;
			return SkillTreeData.GetNodeRank(
				player.Entity,
				requirement.ClassId,
				requirement.NodeId
			) > 0;
		}

		public void ApplyPlayerStats(IServerPlayer player)
		{
			EntityPlayer entity = player.Entity;

			float miningBonus = 0;

			if (skillConfig.Trees.TryGetValue("miner", out SkillTreeDefinition? miner))
			{
				int mastery = GetMasteryRank(entity, "miner");
				miningBonus += (float)GetPassiveValue(miner, mastery, "MiningSpeed");
				miningBonus += (float)GetNodeEffectTotal(entity, miner, "MiningSpeed");
				miningBonus += (float)GetNodeEffectTotal(entity, miner, "ToolSpeed");
			}

			float maxHealthBonus =
				(float)GetEffectValue(player, "MaxHealth");

			float walkSpeedBonus =
				(float)Math.Clamp(
					GetEffectValue(player, "WalkSpeed"),
					0,
					0.15
				);

			float hungerReduction =
				(float)GetEffectValue(player, "HungerReduction");

			float healingBonus =
				(float)GetEffectValue(player, "HealingEffectiveness");

			entity.Stats.Set(
				"miningSpeedMul",
				StatCodePrefix + "miner",
				miningBonus,
				false
			);

			entity.Stats.Set(
				"maxhealthExtraPoints",
				StatCodePrefix + "health",
				maxHealthBonus,
				false
			);

			entity.Stats.Set(
				"walkspeed",
				StatCodePrefix + "walkspeed",
				walkSpeedBonus,
				false
			);

			entity.Stats.Set(
				"hungerrate",
				StatCodePrefix + "hunger",
				-hungerReduction,
				false
			);

			entity.Stats.Set(
				"healingeffectivness",
				StatCodePrefix + "healing",
				healingBonus,
				false
			);

			RefreshHealth(entity);
		}

		private void OnPlayerJoin(IServerPlayer player)
		{
			int migrated = HiddenClassData.MigrateUnlockedSchemas(player);
			if (migrated > 0)
			{
				serverApi.Logger.Notification(
					"[Apprentice] Migrated {0} discovery schema marker(s) for {1}.",
					migrated,
					player.PlayerName
				);
			}
			DiscoverHiddenClasses(player);
			ApplyPlayerStats(player);
		}

		private void OnPurchaseRequest(
			IServerPlayer player,
			SkillPurchaseRequestPacket packet)
		{
			string classId = (packet.ClassId ?? string.Empty).Trim();
			string nodeId = (packet.NodeId ?? string.Empty).Trim();

			serverApi.Logger.Notification(
				$"[Apprentice] Purchase request received from {player.PlayerName}: " +
				$"class='{classId}', node='{nodeId}'."
			);

			SkillPurchaseResultPacket result = Purchase(player, classId, nodeId);
			channel.SendPacket(result, player);
		}

		private SkillPurchaseResultPacket Purchase(
			IServerPlayer player,
			string classId,
			string nodeId)
		{
			var result = new SkillPurchaseResultPacket
			{
				ClassId = classId,
				NodeId = nodeId
			};

			if (string.IsNullOrWhiteSpace(classId) ||
				string.IsNullOrWhiteSpace(nodeId))
			{
				return RejectPurchase(
					result,
					player,
					"The purchase request did not contain a class and node ID."
				);
			}

			if (!skillConfig.Trees.TryGetValue(classId, out SkillTreeDefinition? tree))
			{
				return RejectPurchase(result, player, "Unknown class skill tree.");
			}

			SkillNodeDefinition? node = tree.Nodes.FirstOrDefault(
				candidate => candidate.Id.Equals(nodeId, StringComparison.OrdinalIgnoreCase)
			);
			if (node == null)
			{
				return RejectPurchase(result, player, "Unknown skill node.");
			}

			Entity entity = player.Entity;
			int level = ExpMath.GetLevel(ProgressionData.GetExperience(entity, classId));
			int rank = SkillTreeData.GetNodeRank(entity, classId, node.Id);
			int spent = SkillTreeData.GetSpentPoints(entity, tree);
			int available = SkillTreeData.GetAvailablePoints(entity, tree);
			result.NewRank = rank;
			result.AvailablePoints = available;

			serverApi.Logger.Notification(
				$"[Apprentice] Purchase validation for {player.PlayerName}: " +
				$"class={classId}, node={node.Id}, level={level}, rank={rank}/{node.MaxRank}, " +
				$"spent={spent}, available={available}, cost={node.Cost}."
			);

			if (rank >= node.MaxRank)
			{
				return RejectPurchase(result, player, "This skill is already at maximum rank.", level, rank, spent, available);
			}
			if (level < node.RequiredClassLevel)
			{
				return RejectPurchase(result, player, $"Requires class level {node.RequiredClassLevel}.", level, rank, spent, available);
			}
			if (spent < node.RequiredPointsSpent)
			{
				return RejectPurchase(result, player, $"Requires {node.RequiredPointsSpent} points spent in this tree.", level, rank, spent, available);
			}
			if (available < node.Cost)
			{
				return RejectPurchase(result, player, $"Requires {node.Cost} available skill points.", level, rank, spent, available);
			}

			string? missingRequired = node.Requires.FirstOrDefault(required =>
				SkillTreeData.GetNodeRank(entity, classId, required) <= 0);
			if (missingRequired != null)
			{
				return RejectPurchase(result, player, $"Missing prerequisite: {missingRequired}.", level, rank, spent, available);
			}

			if (node.RequiresAny.Count > 0 && !node.RequiresAny.Any(required =>
				SkillTreeData.GetNodeRank(entity, classId, required) > 0))
			{
				return RejectPurchase(result, player, "Choose one specialization path first.", level, rank, spent, available);
			}
			if (!string.IsNullOrWhiteSpace(node.ExclusiveGroup))
			{
				SkillNodeDefinition? conflict = tree.Nodes.FirstOrDefault(other =>
					!other.Id.Equals(node.Id, StringComparison.OrdinalIgnoreCase) &&
					string.Equals(other.ExclusiveGroup, node.ExclusiveGroup,
						StringComparison.OrdinalIgnoreCase) &&
					SkillTreeData.GetNodeRank(entity, classId, other.Id) > 0
				);
				if (conflict != null)
				{
					return RejectPurchase(
						result,
						player,
						$"This specialization conflicts with {conflict.DisplayName}.",
						level, rank, spent, available
					);
				}
			}

			int newRank = rank + 1;
			SkillTreeData.SetNodeRank(player, classId, node.Id, newRank);
			DiscoverHiddenClasses(player);
			ApplyPlayerStats(player);

			result.Success = true;
			result.NewRank = newRank;
			result.AvailablePoints = SkillTreeData.GetAvailablePoints(entity, tree);
			result.Message = node.Capstone
				? $"Capstone unlocked: {node.DisplayName}!"
				: $"Learned {node.DisplayName} rank {newRank}.";

			serverApi.Logger.Notification(
				$"[Apprentice] Purchase accepted for {player.PlayerName}: " +
				$"class={classId}, node={node.Id}, newRank={newRank}, " +
				$"remainingPoints={result.AvailablePoints}."
			);
			return result;
		}

		private SkillPurchaseResultPacket RejectPurchase(
			SkillPurchaseResultPacket result,
			IServerPlayer player,
			string message,
			int? level = null,
			int? rank = null,
			int? spent = null,
			int? available = null)
		{
			result.Success = false;
			result.Message = message;

			serverApi.Logger.Warning(
				$"[Apprentice] Purchase rejected for {player.PlayerName}: " +
				$"class='{result.ClassId}', node='{result.NodeId}', reason='{message}', " +
				$"level={level?.ToString() ?? "n/a"}, rank={rank?.ToString() ?? "n/a"}, " +
				$"spent={spent?.ToString() ?? "n/a"}, available={available?.ToString() ?? "n/a"}."
			);
			return result;
		}

		private void BuildRecipeLocks()
		{
			foreach (SkillTreeDefinition tree in skillConfig.Trees.Values)
			{
				foreach (SkillNodeDefinition node in tree.Nodes)
				{
					foreach (SkillEffectDefinition effect in node.Effects)
					{
						if (!effect.Type.Equals("UnlockRecipe", StringComparison.OrdinalIgnoreCase) ||
							string.IsNullOrWhiteSpace(effect.Code))
						{
							continue;
						}

						recipeLocks[effect.Code] = (tree.ClassId, node.Id);
					}
				}
			}
		}

		private static int GetMasteryRank(Entity entity, string classId)
		{
			int level = ExpMath.GetLevel(ProgressionData.GetExperience(entity, classId));
			return Math.Clamp(level - 1, 0, MasteryCap);
		}

		private static double GetPassiveValue(
			SkillTreeDefinition tree,
			int mastery,
			string effectType)
		{
			SkillEffectDefinition? effect = tree.PassiveEffect;
			if (effect == null || !effect.Type.Equals(effectType, StringComparison.OrdinalIgnoreCase))
			{
				return 0;
			}
			return effect.ValuePerRank * mastery;
		}

		private static double GetNodeEffectTotal(
			Entity entity,
			SkillTreeDefinition tree,
			string effectType,
			string? contextCode = null)
		{
			double total = 0;

			foreach (SkillNodeDefinition node in tree.Nodes)
			{
				int rank = SkillTreeData.GetNodeRank(
					entity,
					tree.ClassId,
					node.Id
				);

				if (rank <= 0)
				{
					continue;
				}

				foreach (SkillEffectDefinition effect in node.Effects)
				{
					if (!effect.Type.Equals(
							effectType,
							StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					if (!string.IsNullOrWhiteSpace(effect.Code) &&
						(
							string.IsNullOrWhiteSpace(contextCode) ||
							!effect.Code
								.Split('|', StringSplitOptions.RemoveEmptyEntries)
								.Any(pattern =>
									WildcardMatch(
										pattern.Trim(),
										contextCode
									)
								)
						))
					{
						continue;
					}

					total += effect.ValuePerRank * rank;
				}
			}

			return total;
		}

		public bool TryTriggerCheatDeath(
			IServerPlayer player)
		{
			if (!HiddenClassData.IsUnlocked(player.Entity, "berserk"))
			{
				return false;
			}

			double currentHour = serverApi.World.Calendar.TotalHours;
			double previousHour = HiddenClassData.GetLastBerserkHour(player.Entity);

			if (currentHour - previousHour < 48)
			{
				return false;
			}

			HiddenClassData.SetLastBerserkHour(player, currentHour);
			player.SendMessage(
				Vintagestory.API.Config.GlobalConstants.GeneralChatGroup,
				"Berserk discovery power triggered: death was refused and half health was restored.",
				EnumChatType.Notification
			);
			return true;
		}

		internal void DiscoverHiddenClasses(IServerPlayer player)
		{
			foreach (HiddenClassDefinition definition in HiddenClassCatalog.All)
			{
				if (HiddenClassData.IsUnlocked(player.Entity, definition.Id) ||
					!MeetsDiscoveryRequirements(player.Entity, definition))
				{
					continue;
				}

				HiddenClassData.Unlock(player, definition.Id);

				player.SendMessage(
					Vintagestory.API.Config.GlobalConstants.GeneralChatGroup,
					$"Discovery: hidden class unlocked — {definition.DisplayName}!",
					EnumChatType.Notification
				);

				serverApi.Logger.Notification(
					$"[Apprentice] {player.PlayerName} discovered hidden class " +
					$"'{definition.DisplayName}' ({definition.Id})."
				);
			}
		}

		private bool MeetsDiscoveryRequirements(
			Entity entity,
			HiddenClassDefinition definition)
		{
			if (!definition.RequiredClasses.All(classId =>
				IsCapstoneUnlocked(entity, classId)))
			{
				return false;
			}

			string profession = entity.WatchedAttributes
				.GetString("apprenticeProfession", "select")
				.Trim()
				.ToLowerInvariant();
			if (definition.RequiredProfession != null &&
				!profession.Equals(
					definition.RequiredProfession,
					StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			string race = entity.WatchedAttributes
				.GetString("characterClass", string.Empty)
				.Trim()
				.ToLowerInvariant();
			const string racePrefix = "apprentice-race-";
			if (race.StartsWith(racePrefix, StringComparison.Ordinal))
			{
				race = race[racePrefix.Length..];
			}

			if (definition.AllowedRaces.Count > 0 &&
				!definition.AllowedRaces.Contains(
					race,
					StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}

			string subrace = entity.WatchedAttributes
				.GetString("apprenticeRaceSubclass", "select")
				.Trim()
				.ToLowerInvariant();
			if (definition.AllowedSubraces.Count > 0 &&
				!definition.AllowedSubraces.Contains(
					subrace,
					StringComparer.OrdinalIgnoreCase))
			{
				return false;
			}

			if (definition.MinimumCapstones > 0)
			{
				// "Any profession plus N other Grandmasters" still requires
				// the selected profession to be one of the mastered trees.
				if (profession == "select" ||
					!IsCapstoneUnlocked(entity, profession))
				{
					return false;
				}

				int capstones = skillConfig.Trees.Keys.Count(classId =>
					IsCapstoneUnlocked(entity, classId));
				if (capstones < definition.MinimumCapstones)
				{
					return false;
				}
			}

			return true;
		}

		private bool IsCapstoneUnlocked(Entity entity, string classId)
		{
			if (!skillConfig.Trees.TryGetValue(classId, out SkillTreeDefinition? tree))
			{
				return false;
			}

			SkillNodeDefinition? capstone = tree.Nodes.FirstOrDefault(node => node.Capstone);
			return capstone != null &&
				SkillTreeData.GetNodeRank(entity, classId, capstone.Id) > 0;
		}

		private static double GetHiddenEffectValue(
			Entity entity,
			string effectType,
			string? contextCode = null)
		{
			double total = 0;

			foreach (HiddenClassDefinition definition in HiddenClassCatalog.All)
			{
				if (!HiddenClassData.IsUnlocked(entity, definition.Id))
				{
					continue;
				}

				foreach (SkillEffectDefinition effect in definition.Effects)
				{
					if (definition.Id.Equals("deepwarden", StringComparison.OrdinalIgnoreCase) &&
						effect.Type.Equals("OreYield", StringComparison.OrdinalIgnoreCase) &&
						entity.Pos.Y >= entity.World.SeaLevel - 4)
					{
						continue;
					}

					if (!effect.Type.Equals(effectType, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}

					if (!string.IsNullOrWhiteSpace(effect.Code) &&
						(string.IsNullOrWhiteSpace(contextCode) ||
						 !effect.Code.Split('|', StringSplitOptions.RemoveEmptyEntries)
							.Any(pattern => WildcardMatch(pattern.Trim(), contextCode))))
					{
						continue;
					}

					total += effect.ValuePerRank;
				}
			}

			return total;
		}

		private static bool IsTargetFacingAway(Entity attacker, Entity target)
		{
			double toAttackerX = attacker.Pos.X - target.Pos.X;
			double toAttackerZ = attacker.Pos.Z - target.Pos.Z;
			double length = Math.Sqrt(toAttackerX * toAttackerX + toAttackerZ * toAttackerZ);

			if (length < 0.001)
			{
				return false;
			}

			toAttackerX /= length;
			toAttackerZ /= length;

			double forwardX = Math.Sin(target.Pos.Yaw);
			double forwardZ = Math.Cos(target.Pos.Yaw);
			double dot = forwardX * toAttackerX + forwardZ * toAttackerZ;

			return dot < -0.15;
		}

		private void OnTrackingTick(float deltaTime)
		{
			long now = Environment.TickCount64;

			foreach (IServerPlayer player in serverApi.World.AllOnlinePlayers)
			{
				double range =
					GetEffectValue(player, "TrackingRange");

				if (range <= 0 ||
					player.Entity?.Controls?.Sneak != true)
				{
					continue;
				}

				lastTrackingNoticeMs.TryGetValue(
					player.PlayerUID,
					out long previous
				);

				if (now - previous < 8000)
				{
					continue;
				}

				Entity? nearest = null;
				double nearestDistance = double.MaxValue;

				Entity[] nearby =
					serverApi.World.GetEntitiesAround(
						player.Entity.Pos.XYZ,
						(float)range,
						(float)range,
						entity =>
							entity.Alive &&
							entity is EntityAgent &&
							entity is not EntityPlayer &&
							entity.Code != null
					);

				foreach (Entity entity in nearby)
				{
					double distance =
						entity.Pos.XYZ.DistanceTo(
							player.Entity.Pos.XYZ
						);

					if (distance < nearestDistance)
					{
						nearest = entity;
						nearestDistance = distance;
					}
				}

				if (nearest == null)
				{
					continue;
				}

				double dx =
					nearest.Pos.X - player.Entity.Pos.X;
				double dz =
					nearest.Pos.Z - player.Entity.Pos.Z;

				string direction =
					GetCardinalDirection(dx, dz);

				bool detailed =
					GetEffectValue(player, "TrackingDetail") > 0;

				string animalName =
					nearest.Code?.Path
						.Replace('-', ' ') ??
					"animal";

				string message =
					detailed
						? $"Tracks: {animalName}, {nearestDistance:0}m {direction}."
						: $"Fresh animal tracks lead {direction}.";

				player.SendMessage(
					0,
					message,
					EnumChatType.Notification
				);

				lastTrackingNoticeMs[player.PlayerUID] = now;
			}
		}

		private static string GetCardinalDirection(
			double dx,
			double dz)
		{
			double angle =
				Math.Atan2(dx, dz) *
				180 / Math.PI;

			if (angle < 0)
			{
				angle += 360;
			}

			string[] directions =
			[
				"north",
				"north-east",
				"east",
				"south-east",
				"south",
				"south-west",
				"west",
				"north-west"
			];

			int index =
				(int)Math.Round(angle / 45.0) % 8;

			return directions[index];
		}

		private static float GetHealthRatio(EntityPlayer entity)
		{
			foreach (EntityBehavior behavior in
					 entity.ServerBehaviorsMainThread
						 .Concat(entity.ServerBehaviorsThreadsafe))
			{
				if (!behavior.GetType().Name.Contains(
						"Health",
						StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				PropertyInfo? healthProperty =
					behavior.GetType().GetProperty(
						"Health",
						BindingFlags.Instance |
						BindingFlags.Public |
						BindingFlags.NonPublic
					);

				PropertyInfo? maxProperty =
					behavior.GetType().GetProperty(
						"MaxHealth",
						BindingFlags.Instance |
						BindingFlags.Public |
						BindingFlags.NonPublic
					);

				try
				{
					float health =
						Convert.ToSingle(healthProperty?.GetValue(behavior));
					float maxHealth =
						Convert.ToSingle(maxProperty?.GetValue(behavior));

					if (maxHealth > 0)
					{
						return Math.Clamp(health / maxHealth, 0, 1);
					}
				}
				catch
				{
					return 1;
				}
			}

			return 1;
		}

		private static bool WildcardMatch(string pattern, string value)
		{
			return Regex.IsMatch(
				value,
				"^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$",
				RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
			);
		}

		private static void RefreshHealth(EntityPlayer entity)
		{
			IEnumerable<EntityBehavior> behaviors =
				entity.ServerBehaviorsMainThread
					.Concat(entity.ServerBehaviorsThreadsafe);

			foreach (EntityBehavior behavior in behaviors)
			{
				if (!behavior.GetType().Name.Contains(
					"Health",
					StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}

				MethodInfo? markDirty = behavior.GetType().GetMethod(
					"MarkDirty",
					BindingFlags.Instance |
					BindingFlags.Public |
					BindingFlags.NonPublic
				);

				try
				{
					markDirty?.Invoke(behavior, null);
				}
				catch
				{
					// Health will be refreshed again on the next level,
					// purchase or player join.
				}

				return;
			}
		}

		public void Dispose()
		{
			serverApi.Event.PlayerJoin -= OnPlayerJoin;

			if (trackingListenerId != 0)
			{
				serverApi.Event.UnregisterGameTickListener(
					trackingListenerId
				);
				trackingListenerId = 0;
			}
			if (ReferenceEquals(SkillTreeRuntime.Manager, this))
			{
				SkillTreeRuntime.Manager = null;
			}
		}
	}
}
