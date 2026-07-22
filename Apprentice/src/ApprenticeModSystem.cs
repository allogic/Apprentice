using Apprentice.Weapon;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Apprentice
{
	public sealed class ApprenticeModSystem : ModSystem
	{
		private const string PlaytestVersion = "2.7.0-dev.20260722.40";
		private const string BowAssetFingerprint = "BOW-DARKWOOD-OXBLOOD-C-AXIS2-EDIT1-UV1-DRAW5";
		private const string ReviewedAssetFingerprint = "ITEMS-RUNEBOUND5-GILDED2-SUNLANCE2-KITS-HANDANCHOR-TRAP-CHAIN-SHIELD-SIDEWAYS-FISHING-NATIVE-METALS-APPRENTICE2";
		private static readonly string[] ExpectedBowShapeCodes =
		{
			"apprentice:item/2.7/composite-bow",
			"apprentice:item/2.7/composite-bow-charge1",
			"apprentice:item/2.7/composite-bow-charge2",
			"apprentice:item/2.7/composite-bow-charge3",
			"apprentice:item/2.7/composite-bow-charge4",
			"apprentice:item/2.7/composite-bow-charge5"
		};
		private static readonly string[] ExpectedBowAssetPaths =
		{
			"shapes/item/2.7/composite-bow.json",
			"shapes/item/2.7/composite-bow-charge1.json",
			"shapes/item/2.7/composite-bow-charge2.json",
			"shapes/item/2.7/composite-bow-charge3.json",
			"shapes/item/2.7/composite-bow-charge4.json",
			"shapes/item/2.7/composite-bow-charge5.json",
			"textures/item/2.7/compositebow-material.png",
			"textures/item/2.7/compositebow-grip-wrap.png"
		};
		private static readonly string[] ExpectedReviewedAssetPaths =
		{
			"shapes/item/2.7/tower-shield.json",
			"shapes/item/2.7/master-fishing-rod.json",
			"shapes/item/2.7/grandmaster-spear.json",
			"shapes/item/2.7/kit-trap.json",
			"shapes/item/2.7/kit-armor-upgrade.json",
			"shapes/item/2.7/kit-weapon-upgrade.json",
			"shapes/item/2.7/kit-tool-upgrade.json",
			"shapes/item/2.7/kit-first-aid.json",
			"shapes/block/2.7/advancedtrap-triggered.json",
			"shapes/block/2.7/advancedtrap-opening1.json",
			"shapes/block/2.7/advancedtrap-opening2.json",
			"shapes/block/2.7/advancedtrap-opening3.json",
			"shapes/block/2.7/advancedtrap-opening4.json",
			"shapes/block/2.7/advancedtrap-armed.json"
		};

		private ICoreServerAPI? sapi = null;
		private ICoreClientAPI? capi = null;

		private IServerNetworkChannel? serverNetworkChannel = null;
		private IClientNetworkChannel? clientNetworkChannel = null;

		private ClassConfig? classConfig = null;
		private SkillTreeConfig? skillTreeConfig = null;
		private ApprenticeContentRegistry contentRegistry =
			ApprenticeContentRegistry.Empty;

		private ClassesManager? classesManager = null;
		private ExperienceManager? experienceManager = null;
		private SkillTreeManager? skillTreeManager = null;
		private InteractionEventBridge? interactionEventBridge = null;
		private DangerTierSystem? dangerTierSystem = null;
		private PoisonEffectSystem? poisonEffectSystem = null;
		private EcologyWorldgenSystem? ecologyWorldgenSystem = null;

		private InterfaceManager? interfaceManager = null;
		private OverlayManager? overlayManager = null;
		private HealthBarRenderer? healthBarRenderer = null;
		private Harmony? poisonInfoHarmony = null;
		private Harmony? durabilityUpgradeHarmony = null;

		#region ModSystem Impl
		public override void Start(ICoreAPI api)
		{
			api.Logger.Notification(
				"[Apprentice] Loading playtest build {0} ({1}).",
				PlaytestVersion,
				api.Side
			);

			// Collectible mappings are required during asset finalization. Keep
			// them ahead of every optional client integration so a changed GUI or
			// Harmony target can never make valid Apprentice assets unresolvable.
			api.RegisterItemClass(
				"ApprenticeCementationBlister",
				typeof(ItemCementationBlister)
			);
			api.RegisterItemClass(
				"ApprenticeIngot",
				typeof(ItemApprenticeIngot)
			);
			api.RegisterItemClass(
				"ApprenticeTrapKit",
				typeof(ItemAdvancedTrapKit)
			);
			api.RegisterItemClass(
				"ApprenticePoisonPortion",
				typeof(ItemApprenticePoisonPortion)
			);
			api.RegisterItemClass(
				"ApprenticePoisonArrow",
				typeof(ItemApprenticePoisonArrow)
			);
			api.RegisterItemClass(
				"ApprenticeGrandmasterSpear",
				typeof(ItemGrandmasterSpear)
			);
			api.RegisterItemClass(
				"ApprenticeCompositeBow",
				typeof(ItemCompositeBow)
			);
			api.RegisterItemClass(
				"ApprenticeUpgradeKit",
				typeof(ItemUpgradeKit)
			);
			api.RegisterItemClass(
				"ApprenticeFirstAidKit",
				typeof(ItemFirstAidKit)
			);
			api.RegisterItemClass(
				"ApprenticeMasterFishingRod",
				typeof(ItemMasterFishingRod)
			);
			api.RegisterBlockClass(
				"ApprenticeAdvancedTrap",
				typeof(BlockAdvancedTrap)
			);
			api.RegisterBlockClass(
				"ApprenticeLegacyAdvancedTrap",
				typeof(BlockLegacyAdvancedTrap)
			);
			api.RegisterBlockClass(
				"ApprenticeVenomberryBush",
				typeof(BlockVenomberryBush)
			);
			api.RegisterBlockEntityClass(
				"ApprenticeAdvancedTrap",
				typeof(BlockEntityAdvancedTrap)
			);
			api.RegisterBlockClass(
				"ApprenticeCementationFurnace",
				typeof(BlockCementationFurnace)
			);
			api.RegisterBlockEntityClass(
				"ApprenticeCementationFurnace",
				typeof(BlockEntityCementationFurnace)
			);

			durabilityUpgradeHarmony = new Harmony(
				"apprentice.item-upgrades"
			);
			try
			{
				MethodInfo getMaxDurability = typeof(CollectibleObject)
					.GetMethod(
						nameof(CollectibleObject.GetMaxDurability),
						BindingFlags.Instance | BindingFlags.Public,
						binder: null,
						types: new[] { typeof(ItemStack) },
						modifiers: null
					) ?? throw new MissingMethodException(
						"CollectibleObject.GetMaxDurability(ItemStack)"
					);
				durabilityUpgradeHarmony.Patch(
					getMaxDurability,
					postfix: new HarmonyMethod(
						typeof(DurabilityUpgradeRuntime),
						nameof(
							DurabilityUpgradeRuntime
								.GetMaxDurabilityPostfix
						)
					)
				);
			}
			catch (Exception exception)
			{
				api.Logger.Error(
					"[Apprentice] Durability upgrade integration failed: {0}",
					exception.Message
				);
			}

			try
			{
				api.ModLoader.GetModSystem<WorldMapManager>(true)
					.RegisterMapLayer<DangerHeatmapLayer>(
						"apprentice-danger-heatmap",
						0.45
					);
			}
			catch (Exception exception)
			{
				api.Logger.Warning(
					"[Apprentice] Danger heatmap map-layer registration was disabled: {0}",
					exception.Message
				);
			}

			if (api.Side == EnumAppSide.Client)
			{
				poisonInfoHarmony = new Harmony("apprentice.client.runtime");

				try
				{
					poisonInfoHarmony.Patch(
						typeof(Vintagestory.API.Common.Entities.Entity).GetMethod(
							nameof(Vintagestory.API.Common.Entities.Entity.GetInfoText),
							BindingFlags.Instance | BindingFlags.Public,
							binder: null, types: Type.EmptyTypes, modifiers: null
						) ?? throw new MissingMethodException("Entity.GetInfoText()"),
						postfix: new HarmonyMethod(
							typeof(PoisonInfoPatch),
							nameof(PoisonInfoPatch.Postfix)
						)
					);
				}
				catch (Exception exception)
				{
					api.Logger.Warning(
						"[Apprentice] Poison inspection text integration was disabled: {0}",
						exception.Message
					);
				}

				try
				{
					MethodInfo terrainRender = typeof(ChunkMapLayer).GetMethod(
						nameof(ChunkMapLayer.Render),
						BindingFlags.Instance | BindingFlags.Public,
						binder: null,
						types: new[] { typeof(GuiElementMap), typeof(float) },
						modifiers: null
					) ?? throw new MissingMethodException(
						"ChunkMapLayer.Render(GuiElementMap, float)"
					);
					poisonInfoHarmony.Patch(
						terrainRender,
						postfix: new HarmonyMethod(
							typeof(DangerHeatmapTerrainRenderPatch),
							nameof(DangerHeatmapTerrainRenderPatch.Postfix)
						)
					);
				}
				catch (Exception exception)
				{
					api.Logger.Warning(
						"[Apprentice] Danger heatmap terrain-render bridge was disabled: {0}",
						exception.Message
					);
				}
			}

			if (api.Side == EnumAppSide.Server)
			{
				sapi = (ICoreServerAPI)api;

				serverNetworkChannel = sapi.Network.RegisterChannel(ApprenticeConstants.NetworkChannel)
					.RegisterMessageType<ExperienceNotificationPacket>()
					.RegisterMessageType<DangerHeatmapStatePacket>()
					.RegisterMessageType<DangerHeatmapRequestPacket>()
					.RegisterMessageType<SkillPurchaseRequestPacket>()
					.RegisterMessageType<SkillPurchaseResultPacket>();
			}
			else
			{
				capi = (ICoreClientAPI)api;

				clientNetworkChannel = capi.Network.RegisterChannel(ApprenticeConstants.NetworkChannel)
					.RegisterMessageType<ExperienceNotificationPacket>()
					.RegisterMessageType<DangerHeatmapStatePacket>()
					.RegisterMessageType<DangerHeatmapRequestPacket>()
					.RegisterMessageType<SkillPurchaseRequestPacket>()
					.RegisterMessageType<SkillPurchaseResultPacket>();
			}
		}
		public override void AssetsLoaded(ICoreAPI api)
		{
			classConfig = ClassConfigLoader.Load(api);
			skillTreeConfig = SkillTreeConfigLoader.Load(api);
			ConfigConsistencyValidator.Validate(classConfig, skillTreeConfig);
			contentRegistry = ApprenticeContentRegistry.Load(api);
			HiddenClassCatalog.Configure(contentRegistry.Discoveries);
		}
		public override void AssetsFinalize(ICoreAPI api)
		{
			ApplyVanillaMetalPresentation(api);
			RejectGameDomainAdvancedMetals(api);
			int creativeCollectibles =
				ApprenticeContentRegistry.EnsureCreativeInventoryPresence(api);
			api.Logger.Notification(
				"[Apprentice] {0} exposed {1} collectibles in the creative inventory.",
				PlaytestVersion,
				creativeCollectibles
			);
			if (creativeCollectibles == 0)
			{
				api.Logger.Error(
					"[Apprentice] No Apprentice collectibles were loaded. The installed mod is incomplete: replace the whole mod folder/archive, including assets and Apprentice.dll."
				);
			}
			contentRegistry.ResolveCollectibles(api);
			CementationRuntime.Registry = contentRegistry;
			if (api.Side == EnumAppSide.Client)
			{
				LogBowAssetFingerprint(api);
				LogReviewedAssetFingerprint(api);
			}
		}

		private static void ApplyVanillaMetalPresentation(ICoreAPI api)
		{
			CloneVanillaPresentation(
				api,
				"game:ingot-iron",
				new[]
				{
					"apprentice:ingot-starsteel",
					"apprentice:ingot-aethersteel"
				},
				copyTongAttributes: true
			);
			CloneVanillaPresentation(
				api,
				"game:workitem-iron",
				new[]
				{
					"apprentice:workitem-starsteel",
					"apprentice:workitem-aethersteel"
				},
				copyTongAttributes: false
			);
		}

		private static void CloneVanillaPresentation(
			ICoreAPI api,
			string referenceCode,
			IEnumerable<string> targetCodes,
			bool copyTongAttributes)
		{
			Item reference = api.World.GetItem(new AssetLocation(referenceCode))
				?? throw new InvalidOperationException(
					$"Required vanilla reference '{referenceCode}' is missing."
				);
			foreach (string targetCode in targetCodes)
			{
				Item target = api.World.GetItem(new AssetLocation(targetCode))
					?? throw new InvalidOperationException(
						$"Required Apprentice item '{targetCode}' is missing."
					);
				if (reference.GuiTransform != null)
					target.GuiTransform = reference.GuiTransform.Clone();
				if (reference.FpHandTransform != null)
					target.FpHandTransform = reference.FpHandTransform.Clone();
				if (reference.GroundTransform != null)
					target.GroundTransform = reference.GroundTransform.Clone();
				if (reference.TpHandTransform != null)
					target.TpHandTransform = reference.TpHandTransform.Clone();
				if (reference.TpOffHandTransform != null)
					target.TpOffHandTransform = reference.TpOffHandTransform.Clone();

				if (!copyTongAttributes ||
					reference.Attributes?.Token is not JObject referenceAttributes)
				{
					continue;
				}

				JObject targetAttributes =
					target.Attributes?.Token?.DeepClone() as JObject
					?? new JObject();
				foreach (string key in new[]
				{
					"requiresTongs",
					"tongOpening",
					"onTongTransform",
					"onMetalTongTransform"
				})
				{
					if (targetAttributes[key] == null &&
						referenceAttributes[key] != null)
					{
						targetAttributes[key] =
							referenceAttributes[key]!.DeepClone();
					}
				}
				target.Attributes = new JsonObject(targetAttributes);
			}
		}

		private static void RejectGameDomainAdvancedMetals(ICoreAPI api)
		{
			string[] missingLegacyCodes = api.World.Items
				.Where(item =>
					item != null &&
					item.IsMissing &&
					IsGameDomainAdvancedMetal(item.Code)
				)
				.Select(item => item.Code.ToString())
				.Concat(
					api.World.Blocks
						.Where(block =>
							block != null &&
							block.IsMissing &&
							IsGameDomainAdvancedMetal(block.Code)
						)
						.Select(block => block.Code.ToString())
				)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(code => code, StringComparer.Ordinal)
				.ToArray();
			if (missingLegacyCodes.Length != 0)
			{
				api.Logger.Notification(
					"[Apprentice] Found {0} historical game-domain advanced-metal save mappings represented by Vintage Story as missing collectibles; they are not active registrations: {1}",
					missingLegacyCodes.Length,
					string.Join(", ", missingLegacyCodes)
				);
			}

			string[] invalidCodes = api.World.Items
				.Where(item =>
					item != null &&
					!item.IsMissing &&
					IsGameDomainAdvancedMetal(item.Code)
				)
				.Select(item => item.Code.ToString())
				.Concat(
					api.World.Blocks
						.Where(block =>
							block != null &&
							!block.IsMissing &&
							IsGameDomainAdvancedMetal(block.Code)
						)
						.Select(block => block.Code.ToString())
				)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(code => code, StringComparer.Ordinal)
				.ToArray();
			if (invalidCodes.Length == 0)
			{
				api.Logger.Notification(
					"[Apprentice] Advanced-metal registry ownership verified: apprentice domain only."
				);
				return;
			}

			throw new InvalidOperationException(
				"Starsteel and Aethersteel must be owned only by the " +
				$"apprentice domain. Invalid game-domain registrations: {string.Join(", ", invalidCodes)}"
			);
		}

		private static bool IsGameDomainAdvancedMetal(AssetLocation? code) =>
			code?.Domain == "game" &&
			(code.Path.Contains("starsteel", StringComparison.Ordinal) ||
			 code.Path.Contains("aethersteel", StringComparison.Ordinal));

		private static void LogReviewedAssetFingerprint(ICoreAPI api)
		{
			string[] missingAssets = ExpectedReviewedAssetPaths
				.Where(
					path => api.Assets.TryGet(
						new AssetLocation("apprentice", path)
					) == null
				)
				.ToArray();

			api.Logger.Notification(
				"[Apprentice] Reviewed item asset fingerprint {0}: missing-files=[{1}].",
				ReviewedAssetFingerprint,
				string.Join(", ", missingAssets)
			);
			if (missingAssets.Length != 0)
			{
				api.Logger.Error(
					"[Apprentice] Reviewed item assets do not match build {0}. Remove every older Apprentice ZIP/folder and install one clean archive.",
					PlaytestVersion
				);
			}
		}

		private static void LogBowAssetFingerprint(ICoreAPI api)
		{
			Item? bow = api.World.GetItem(
				new AssetLocation("apprentice", "compositebow")
			);
			CompositeShape? configuredShape = bow?.Shape;
			string[] configuredShapeCodes = configuredShape == null
				? Array.Empty<string>()
				: new[]
					{
						configuredShape.Base?.ToString() ?? "<missing-base>"
					}
					.Concat(
						configuredShape.Alternates?.Select(
							alternate => alternate.Base?.ToString()
								?? "<missing-alternate>"
						) ?? Enumerable.Empty<string>()
					)
					.ToArray();

			string[] missingAssets = ExpectedBowAssetPaths
				.Where(
					path => api.Assets.TryGet(
						new AssetLocation("apprentice", path)
					) == null
				)
				.ToArray();
			bool correctConfiguration =
				configuredShapeCodes.SequenceEqual(ExpectedBowShapeCodes);

			api.Logger.Notification(
				"[Apprentice] Bow asset fingerprint {0}: configured=[{1}]; missing-files=[{2}].",
				BowAssetFingerprint,
				string.Join(", ", configuredShapeCodes),
				string.Join(", ", missingAssets)
			);
			if (!correctConfiguration || missingAssets.Length != 0)
			{
				api.Logger.Error(
					"[Apprentice] Composite Bow runtime assets do not match build {0}. Remove every older Apprentice ZIP/folder and rebuild from a clean source extraction.",
					PlaytestVersion
				);
			}
		}
		public override void StartServerSide(ICoreServerAPI api)
		{
			ClassConfig loadedClassConfig = classConfig ?? throw new InvalidOperationException("class.json was not loaded before server startup.");
			SkillTreeConfig loadedSkillTreeConfig = skillTreeConfig ?? throw new InvalidOperationException("skilltrees.json was not loaded before server startup.");

			IServerNetworkChannel networkChannel = serverNetworkChannel ?? throw new InvalidOperationException("The Apprentice server network channel was not registered.");

			BaseConfig baseConfig = BaseConfigLoader.Load(api);

			classesManager = new ClassesManager(api, loadedClassConfig);
			skillTreeManager = new SkillTreeManager(api, loadedClassConfig, loadedSkillTreeConfig, networkChannel);
			experienceManager = new ExperienceManager(api, networkChannel, loadedClassConfig, baseConfig, skillTreeManager);
			interactionEventBridge = new InteractionEventBridge(api, experienceManager);
			dangerTierSystem = new DangerTierSystem(
				api,
				contentRegistry,
				networkChannel
			);
			networkChannel.SetMessageHandler<DangerHeatmapRequestPacket>(
				OnDangerHeatmapRequest
			);
			poisonEffectSystem = new PoisonEffectSystem(api, contentRegistry);
			ecologyWorldgenSystem = new EcologyWorldgenSystem(api, contentRegistry);
		}
		public override void StartClientSide(ICoreClientAPI api)
		{
			// Heatmap state is gameplay-owned and must not depend on optional
			// client HUD construction succeeding later in this method.
			if (clientNetworkChannel != null)
			{
				clientNetworkChannel.SetMessageHandler<DangerHeatmapStatePacket>(
					OnDangerHeatmapState
				);
				DangerHeatmapClientRuntime.RequestState = () =>
					clientNetworkChannel.SendPacket(
						new DangerHeatmapRequestPacket()
					);
			}
			else
			{
				api.Logger.Error(
					"[Apprentice] The client network channel is missing; danger heatmap state cannot be received."
				);
			}

			api.Event.RegisterCallback(
				_ =>
				{
					bool heatmapRegistered = api.ModLoader
						.GetModSystem<WorldMapManager>(true)
						.MapLayers.Any(layer => layer is DangerHeatmapLayer);
					if (heatmapRegistered)
					{
						api.Logger.Notification(
							"[Apprentice] Danger heatmap map tab registered."
						);
					}
					else
					{
						api.Logger.Error(
							"[Apprentice] Danger heatmap map tab is missing. Remove stale Apprentice copies and install the complete 2.7.0 package."
						);
					}
				},
				1000
			);
			try
			{
				ClassConfig loadedClassConfig = classConfig ?? throw new InvalidOperationException("class.json was not loaded before client startup.");
				SkillTreeConfig loadedSkillTreeConfig = skillTreeConfig ?? throw new InvalidOperationException("skilltrees.json was not loaded before client startup.");

				IClientNetworkChannel networkChannel = clientNetworkChannel ?? throw new InvalidOperationException("The Apprentice client network channel was not registered.");

				BaseConfig baseConfig = BaseConfigLoader.Load(api);

				interfaceManager = new InterfaceManager(api, loadedClassConfig, loadedSkillTreeConfig, networkChannel);
				overlayManager = new OverlayManager(api, baseConfig, loadedClassConfig);
				try
				{
					healthBarRenderer = new HealthBarRenderer(api);
				}
				catch (Exception exception)
				{
					// The enemy health bars are optional presentation. A GPU or
					// shader problem must not abort the skill-tree UI or packet
					// handlers that are initialized immediately below.
					api.Logger.Warning(
						"[Apprentice] Enemy health bars were disabled because their shader could not be initialized: {0}",
						exception.Message
					);
					healthBarRenderer = null;
				}

				networkChannel.SetMessageHandler<ExperienceNotificationPacket>(OnExperienceNotification);
				networkChannel.SetMessageHandler<SkillPurchaseResultPacket>(OnSkillPurchaseResult);
			}
			catch (Exception exception)
			{
				api.Logger.Error(
					"[Apprentice] Client-side startup failed. Server progression can continue, but the UI may be unavailable."
				);
				api.Logger.Error(exception);
			}

			capi?.Event.PlayerJoin += OnPlayerJoin;
		}

		public override void Dispose()
		{
			durabilityUpgradeHarmony?.UnpatchAll(
				"apprentice.item-upgrades"
			);
			durabilityUpgradeHarmony = null;
			poisonInfoHarmony?.UnpatchAll("apprentice.client.runtime");
			poisonInfoHarmony = null;
			interactionEventBridge?.Dispose();
			dangerTierSystem?.Dispose();
			poisonEffectSystem?.Dispose();
			ecologyWorldgenSystem?.Dispose();
			skillTreeManager?.Dispose();
			classesManager?.Dispose();

			overlayManager?.Dispose();
			interfaceManager?.Dispose();
			healthBarRenderer?.Dispose();

			interactionEventBridge = null;
			dangerTierSystem = null;
			poisonEffectSystem = null;
			ecologyWorldgenSystem = null;
			experienceManager = null;
			skillTreeManager = null;
			classesManager = null;
			interfaceManager = null;
			overlayManager = null;
			healthBarRenderer = null;
			DangerHeatmapClientRuntime.RequestState = null;
			DangerHeatmapClientRuntime.LatestState = null;

			base.Dispose();
		}
		#endregion

		#region Event Handler
		private void OnPlayerJoin(IClientPlayer byPlayer)
		{
			if (capi != null)
			{
				byPlayer.Entity.AddBehavior(new UchigatanaDashBehaviour(capi, byPlayer.Entity));
				byPlayer.Entity.AddBehavior(new TrueThirdPersonBehaviour(capi, byPlayer.Entity));
			}
		}
		private void OnExperienceNotification(ExperienceNotificationPacket packet)
		{
			// Network packet callbacks are not a safe place to create
			// or update GUI elements. Always transfer the work to the
			// client main thread first.
			ICoreClientAPI? api = capi;
			if (api == null) return;

			try
			{
				api.Event.EnqueueMainThreadTask(() =>
				{
					try
					{
						overlayManager?.Enqueue(packet);
						interfaceManager?.RefreshIfOpen();
					}
					catch (Exception exception)
					{
						api.Logger.Error("[Apprentice] Main-thread XP notification handling failed.");
						api.Logger.Error(exception);
					}
				}, "apprentice-xp-notification");
			}
			catch (Exception exception)
			{
				api.Logger.Error("[Apprentice] Could not enqueue the XP notification on the client main thread.");
				api.Logger.Error(exception);
			}
		}
		private void OnDangerHeatmapState(DangerHeatmapStatePacket packet)
		{
			ICoreClientAPI? api = capi;
			if (api == null) return;

			api.Logger.Notification(
				"[Apprentice] Received danger heatmap state through the gameplay channel."
			);

			// Retain the packet before crossing to the main thread. Map layers
			// are recreated during level finalization and dialog lifecycle, so a
			// one-shot delivery to the current instance is not sufficient.
			DangerHeatmapClientRuntime.LatestState = packet;

			api.Event.EnqueueMainThreadTask(
				() =>
				{
					DangerHeatmapLayer[] layers = api.ModLoader
						.GetModSystem<WorldMapManager>(true)
						.MapLayers
						.OfType<DangerHeatmapLayer>()
						.ToArray();
					foreach (DangerHeatmapLayer layer in layers)
					{
						layer.ApplyState(packet);
					}

					if (layers.Length == 0)
					{
						api.Logger.Error(
							"[Apprentice] Received danger heatmap state, but its map layer is missing."
						);
					}
				},
				"apprentice-danger-heatmap-state"
			);
		}
		private void OnDangerHeatmapRequest(
			IServerPlayer player,
			DangerHeatmapRequestPacket packet)
		{
			if (packet.Request)
			{
				sapi?.Logger.Notification(
					"[Apprentice] Received danger heatmap request from {0}.",
					player.PlayerName
				);
				dangerTierSystem?.SendHeatmapState(player);

				// The gameplay channel is retained for clients which have not opened
				// the map yet. Also answer through Vintage Story's native world-map
				// transport: it is tied to the registered map layer and avoids a
				// client packet-handler race during first-world initialization.
				try
				{
					WorldMapManager mapManager = sapi!.ModLoader
						.GetModSystem<WorldMapManager>(true);
					DangerHeatmapLayer? layer = mapManager.MapLayers
						.OfType<DangerHeatmapLayer>()
						.FirstOrDefault();
					if (layer == null)
					{
						sapi.Logger.Error(
							"[Apprentice] Cannot send danger heatmap map data because the server map layer is missing."
						);
					}
					else if (!layer.SendState(player))
					{
						sapi.Logger.Error(
							"[Apprentice] Cannot send danger heatmap map data because its world state is unavailable."
						);
					}
				}
				catch (Exception exception)
				{
					sapi?.Logger.Error(
						"[Apprentice] Failed to send danger heatmap data through the world-map channel."
					);
					sapi?.Logger.Error(exception);
				}
			}
		}
		private void OnSkillPurchaseResult(SkillPurchaseResultPacket packet)
		{
			// I've got no idea what this could be...
			capi?.Event.EnqueueMainThreadTask(() =>
			{
				interfaceManager?.ApplyPurchaseResult(packet);
			},
				"apprentice-skill-purchase-result"
			);
		}
		#endregion
	}
}
