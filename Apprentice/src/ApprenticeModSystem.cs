using Apprentice.src.weapons;
using System;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace Apprentice
{
	public sealed class ApprenticeModSystem : ModSystem
	{
		private ICoreServerAPI sapi = null;
		private ICoreClientAPI capi = null;

		private IServerNetworkChannel? serverNetworkChannel = null;
		private IClientNetworkChannel? clientNetworkChannel = null;

		private ClassConfig? classConfig = null;
		private SkillTreeConfig? skillTreeConfig = null;

		private ClassesManager? classesManager = null;
		private ExperienceManager? experienceManager = null;
		private SkillTreeManager? skillTreeManager = null;
		private InteractionEventBridge? interactionEventBridge = null;

		private InterfaceManager? interfaceManager = null;
		private OverlayManager? overlayManager = null;
		private HealthBarRenderer? healthBarRenderer = null;

		#region ModSystem Impl
		public override void Start(ICoreAPI api)
		{
			if (api.Side == EnumAppSide.Server)
			{
				sapi = (ICoreServerAPI)api;

				serverNetworkChannel = sapi.Network.RegisterChannel(ApprenticeConstants.NetworkChannel)
					.RegisterMessageType<ExperienceNotificationPacket>()
					.RegisterMessageType<SkillPurchaseRequestPacket>()
					.RegisterMessageType<SkillPurchaseResultPacket>();
			}
			else
			{
				capi = (ICoreClientAPI)api;

				clientNetworkChannel = capi.Network.RegisterChannel(ApprenticeConstants.NetworkChannel)
					.RegisterMessageType<ExperienceNotificationPacket>()
					.RegisterMessageType<SkillPurchaseRequestPacket>()
					.RegisterMessageType<SkillPurchaseResultPacket>();
			}
		}
		public override void AssetsLoaded(ICoreAPI api)
		{
			classConfig = ClassConfigLoader.Load(api);
			skillTreeConfig = SkillTreeConfigLoader.Load(api);
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
		}
		public override void StartClientSide(ICoreClientAPI api)
		{
			ClassConfig loadedClassConfig = classConfig ?? throw new InvalidOperationException("class.json was not loaded before client startup.");
			SkillTreeConfig loadedSkillTreeConfig = skillTreeConfig ?? throw new InvalidOperationException("skilltrees.json was not loaded before client startup.");

			IClientNetworkChannel networkChannel = clientNetworkChannel ?? throw new InvalidOperationException("The Apprentice client network channel was not registered.");

			BaseConfig baseConfig = BaseConfigLoader.Load(api);

			interfaceManager = new InterfaceManager(api, loadedClassConfig, loadedSkillTreeConfig, networkChannel);
			overlayManager = new OverlayManager(api, baseConfig, loadedClassConfig);
			healthBarRenderer = new HealthBarRenderer(api);

			networkChannel.SetMessageHandler<ExperienceNotificationPacket>(OnExperienceNotification);
			networkChannel.SetMessageHandler<SkillPurchaseResultPacket>(OnSkillPurchaseResult);

			if (capi != null)
			{
				Entity playerEntity = capi.World.Player.Entity;
				playerEntity.AddBehavior(new UchigatanaDashBehaviour(capi, playerEntity));
			}
		}
		public override void Dispose()
		{
			interactionEventBridge?.Dispose();
			skillTreeManager?.Dispose();
			classesManager?.Dispose();

			overlayManager?.Dispose();
			interfaceManager?.Dispose();
			healthBarRenderer?.Dispose();

			interactionEventBridge = null;
			experienceManager = null;
			skillTreeManager = null;
			classesManager = null;
			interfaceManager = null;
			overlayManager = null;
			healthBarRenderer = null;

			base.Dispose();
		}
		#endregion

		#region Event Handler
		private void OnExperienceNotification(ExperienceNotificationPacket packet)
		{
			// Network packet callbacks are not a safe place to create
			// or update GUI elements. Always transfer the work to the
			// client main thread first.
			capi?.Event.EnqueueMainThreadTask(() =>
			{
				overlayManager?.Enqueue(packet);
				interfaceManager?.RefreshIfOpen();
			},
				"apprentice-xp-notification"
			);
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
