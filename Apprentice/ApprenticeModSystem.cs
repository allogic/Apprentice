using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
    public sealed class ApprenticeModSystem : ModSystem
    {
        private ClassConfig? classConfig;
        private SkillTreeConfig? skillTreeConfig;

        private IServerNetworkChannel? serverNetworkChannel;
        private IClientNetworkChannel? clientNetworkChannel;

        private ClassesManager? classesManager;
        private ExperienceManager? experienceManager;
        private SkillTreeManager? skillTreeManager;
        private InteractionEventBridge? interactionEventBridge;

        private InterfaceManager? interfaceManager;
        private OverlayManager? overlayManager;
        private ICoreClientAPI? apiForClientLogging;

        public override void Start(ICoreAPI api)
        {
            if (api.Side == EnumAppSide.Server)
            {
                ICoreServerAPI serverApi =
                    (ICoreServerAPI)api;

                serverNetworkChannel =
                    serverApi.Network
                        .RegisterChannel(
                            ApprenticeConstants.NetworkChannel
                        )
                        .RegisterMessageType<
                            ExperienceNotificationPacket
                        >()
                        .RegisterMessageType<
                            SkillPurchaseRequestPacket
                        >()
                        .RegisterMessageType<
                            SkillPurchaseResultPacket
                        >();
            }
            else
            {
                ICoreClientAPI clientApi =
                    (ICoreClientAPI)api;

                clientNetworkChannel =
                    clientApi.Network
                        .RegisterChannel(
                            ApprenticeConstants.NetworkChannel
                        )
                        .RegisterMessageType<
                            ExperienceNotificationPacket
                        >()
                        .RegisterMessageType<
                            SkillPurchaseRequestPacket
                        >()
                        .RegisterMessageType<
                            SkillPurchaseResultPacket
                        >();
            }
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            classConfig =
                ClassConfigLoader.Load(api);

            skillTreeConfig =
                SkillTreeConfigLoader.Load(api);
        }

        public override void StartServerSide(
            ICoreServerAPI api)
        {
            ClassConfig loadedClassConfig =
                classConfig
                ?? throw new InvalidOperationException(
                    "class.json was not loaded before " +
                    "server startup."
                );

            SkillTreeConfig loadedSkillTreeConfig =
                skillTreeConfig
                ?? throw new InvalidOperationException(
                    "skilltrees.json was not loaded before server startup."
                );

            IServerNetworkChannel networkChannel =
                serverNetworkChannel
                ?? throw new InvalidOperationException(
                    "The Apprentice server network channel " +
                    "was not registered."
                );

            BaseConfig baseConfig =
                BaseConfigLoader.Load(api);

            classesManager =
                new ClassesManager(
                    api,
                    loadedClassConfig
                );

            skillTreeManager =
                new SkillTreeManager(
                    api,
                    loadedClassConfig,
                    loadedSkillTreeConfig,
                    networkChannel
                );

            experienceManager =
                new ExperienceManager(
                    api,
                    networkChannel,
                    loadedClassConfig,
                    baseConfig,
                    skillTreeManager
                );

            interactionEventBridge =
                new InteractionEventBridge(
                    api,
                    experienceManager
                );
        }

        public override void StartClientSide(
            ICoreClientAPI api)
        {
            apiForClientLogging = api;
            api.Logger.Notification(
                "[Apprentice] Client startup: begin — " +
                "Interactive SkillTree Stable Shell, version 2.1.5."
            );

            try
            {
                ClassConfig loadedClassConfig =
                    classConfig
                    ?? throw new InvalidOperationException(
                        "class.json was not loaded before " +
                        "client startup."
                    );

                api.Logger.Notification(
                    "[Apprentice] Client startup: class config ready."
                );

                SkillTreeConfig loadedSkillTreeConfig =
                    skillTreeConfig
                    ?? throw new InvalidOperationException(
                        "skilltrees.json was not loaded before client startup."
                    );

                api.Logger.Notification(
                    "[Apprentice] Client startup: skill trees ready."
                );

                IClientNetworkChannel networkChannel =
                    clientNetworkChannel
                    ?? throw new InvalidOperationException(
                        "The Apprentice client network channel " +
                        "was not registered."
                    );

                api.Logger.Notification(
                    "[Apprentice] Client startup: network ready."
                );

                BaseConfig baseConfig =
                    BaseConfigLoader.Load(api);

                api.Logger.Notification(
                    "[Apprentice] Client startup: base config ready."
                );

                // These managers now register lightweight handlers only.
                // They do not compose a GUI during connection.
                interfaceManager =
                    new InterfaceManager(
                        api,
                        loadedClassConfig,
                        loadedSkillTreeConfig,
                        networkChannel
                    );

                api.Logger.Notification(
                    "[Apprentice] Client startup: hotkey ready."
                );

                overlayManager =
                    new OverlayManager(
                        api,
                        baseConfig,
                        loadedClassConfig
                    );

                api.Logger.Notification(
                    "[Apprentice] Client startup: lazy HUD ready."
                );

                networkChannel.SetMessageHandler<
                    ExperienceNotificationPacket
                >(OnExperienceNotification);

                networkChannel.SetMessageHandler<
                    SkillPurchaseResultPacket
                >(OnSkillPurchaseResult);

                api.Logger.Notification(
                    "[Apprentice] Client startup: complete."
                );
            }
            catch (Exception exception)
            {
                // A client-side display failure must not prevent joining.
                api.Logger.Error(
                    "[Apprentice] Client-side startup failed. " +
                    "Server progression can continue, but the UI " +
                    "may be unavailable."
                );
                api.Logger.Error(exception);
            }
        }

        private void OnExperienceNotification(
            ExperienceNotificationPacket packet)
        {
            ICoreClientAPI? api =
                apiForClientLogging;

            if (api == null)
            {
                return;
            }

            try
            {
                // Network packet callbacks are not a safe place to create
                // or update GUI elements. Always transfer the work to the
                // client main thread first.
                api.Event.EnqueueMainThreadTask(
                    () =>
                    {
                        try
                        {
                            overlayManager?.Enqueue(packet);
                            interfaceManager?.RefreshIfOpen();
                        }
                        catch (Exception exception)
                        {
                            api.Logger.Error(
                                "[Apprentice] Main-thread XP " +
                                "notification handling failed."
                            );

                            api.Logger.Error(exception);
                        }
                    },
                    "apprentice-xp-notification"
                );
            }
            catch (Exception exception)
            {
                api.Logger.Error(
                    "[Apprentice] Could not enqueue the XP " +
                    "notification on the client main thread."
                );

                api.Logger.Error(exception);
            }
        }

        private void OnSkillPurchaseResult(
            SkillPurchaseResultPacket packet)
        {
            ICoreClientAPI? api = apiForClientLogging;
            if (api == null) return;

            api.Event.EnqueueMainThreadTask(
                () =>
                {
                    interfaceManager?.ApplyPurchaseResult(packet);
                },
                "apprentice-skill-purchase-result"
            );
        }

        public override void Dispose()
        {
            interactionEventBridge?.Dispose();
            skillTreeManager?.Dispose();
            classesManager?.Dispose();

            interfaceManager?.Dispose();
            overlayManager?.Dispose();

            interactionEventBridge = null;
            experienceManager = null;
            skillTreeManager = null;
            classesManager = null;
            interfaceManager = null;
            overlayManager = null;
            apiForClientLogging = null;

            base.Dispose();
        }
    }
}
