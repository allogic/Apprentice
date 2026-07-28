using System;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Apprentice
{
    /// <summary>
    /// Client-side Level 3 rule. The full world map and minimap are closed as
    /// soon as the local player enters the Endless Forest, and the normal map
    /// preference is restored after leaving it.
    /// </summary>
    public sealed class EndlessForestMapRestrictionSystem : ModSystem
    {
        private const int EndlessForestLevel = 3;
        private const string HarmonyId =
            "apprentice.endless-forest-map-restriction";
        private const string MinimapSetting = "showMinimapHud";
        private const int TickIntervalMilliseconds = 100;
        private const long RejectionMessageCooldownMilliseconds = 1200;

        private static EndlessForestMapRestrictionSystem? activeInstance;

        private ICoreClientAPI? api;
        private Harmony? harmony;
        private long tickListenerId;
        private DangerHeatmapStatePacket? observedPacket;
        private DangerWorldState? observedState;
        private bool mapRestricted;
        private bool savedMinimapPreference;
        private bool hasSavedMinimapPreference;
        private long lastRejectionMessageAt =
            -RejectionMessageCooldownMilliseconds;

        public override bool ShouldLoad(EnumAppSide side) =>
            side == EnumAppSide.Client;

        public override double ExecuteOrder() => 1.01;

        public override void StartClientSide(ICoreClientAPI capi)
        {
            api = capi ?? throw new ArgumentNullException(nameof(capi));
            activeInstance = this;

            MethodInfo toggleMap = typeof(WorldMapManager).GetMethod(
                nameof(WorldMapManager.ToggleMap),
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(EnumDialogType) },
                modifiers: null
            ) ?? throw new MissingMethodException(
                "WorldMapManager.ToggleMap(EnumDialogType)"
            );

            harmony = new Harmony(HarmonyId);
            harmony.Patch(
                toggleMap,
                prefix: new HarmonyMethod(
                    typeof(EndlessForestMapRestrictionSystem),
                    nameof(ToggleMapPrefix)
                )
            );

            tickListenerId = capi.Event.RegisterGameTickListener(
                OnClientTick,
                TickIntervalMilliseconds
            );
            capi.Event.LeaveWorld += OnLeaveWorld;
            capi.Logger.Notification(
                "[Apprentice] Endless Forest map restriction ready."
            );
        }

        private void OnClientTick(float deltaTime)
        {
            ICoreClientAPI? capi = api;
            if (capi?.World?.Player?.Entity == null)
            {
                SetMapRestricted(false);
                return;
            }

            DangerHeatmapStatePacket? packet =
                DangerHeatmapClientRuntime.LatestState;
            if (!ReferenceEquals(packet, observedPacket))
            {
                observedPacket = packet;
                observedState = packet?.ToState();
            }

            bool shouldRestrict =
                observedState?.Enabled == true &&
                observedState.RealmWorldgenEnabled &&
                string.Equals(
                    observedState.WorldgenProfile,
                    WorldZoneLayout.ConcentricRealmsProfile,
                    StringComparison.Ordinal
                ) &&
                WorldZoneLayout.IsLevelAt(
                    observedState,
                    EndlessForestLevel,
                    capi.World.Player.Entity.Pos.X,
                    capi.World.Player.Entity.Pos.Z
                );

            SetMapRestricted(shouldRestrict);
            if (shouldRestrict)
            {
                ForceMapClosed();
            }
        }

        private void SetMapRestricted(bool restricted)
        {
            ICoreClientAPI? capi = api;
            if (capi == null || mapRestricted == restricted)
            {
                return;
            }

            if (restricted)
            {
                savedMinimapPreference =
                    capi.Settings.Bool[MinimapSetting];
                hasSavedMinimapPreference = true;
                mapRestricted = true;
                ForceMapClosed();
                capi.TriggerIngameDiscovery(
                    this,
                    "apprentice-endless-forest-map-disabled",
                    Lang.Get("endless-forest-map-disabled")
                );
                return;
            }

            mapRestricted = false;
            if (!hasSavedMinimapPreference)
            {
                return;
            }

            bool restoreMinimap = savedMinimapPreference;
            hasSavedMinimapPreference = false;
            capi.Settings.Bool.Set(
                MinimapSetting,
                restoreMinimap,
                shouldTriggerWatchers: false
            );
            if (!restoreMinimap ||
                capi.World?.Player?.Entity == null)
            {
                return;
            }

            WorldMapManager manager = capi.ModLoader
                .GetModSystem<WorldMapManager>(true);
            if (!manager.IsOpened)
            {
                manager.ToggleMap(EnumDialogType.HUD);
            }
        }

        private void ForceMapClosed()
        {
            ICoreClientAPI? capi = api;
            if (capi == null)
            {
                return;
            }

            if (capi.Settings.Bool[MinimapSetting])
            {
                capi.Settings.Bool.Set(
                    MinimapSetting,
                    value: false,
                    shouldTriggerWatchers: false
                );
            }

            WorldMapManager manager = capi.ModLoader
                .GetModSystem<WorldMapManager>(true);
            if (manager.worldMapDlg?.IsOpened() == true)
            {
                manager.worldMapDlg.TryClose();
            }
        }

        private static bool ToggleMapPrefix()
        {
            EndlessForestMapRestrictionSystem? instance =
                activeInstance;
            if (instance?.mapRestricted != true)
            {
                return true;
            }

            instance.ShowMapRejected();
            return false;
        }

        private void ShowMapRejected()
        {
            ICoreClientAPI? capi = api;
            if (capi == null)
            {
                return;
            }

            long now = capi.ElapsedMilliseconds;
            if (now - lastRejectionMessageAt <
                RejectionMessageCooldownMilliseconds)
            {
                return;
            }

            lastRejectionMessageAt = now;
            capi.TriggerIngameError(
                this,
                "apprentice-endless-forest-map-disabled",
                Lang.Get("endless-forest-map-disabled")
            );
        }

        private void OnLeaveWorld()
        {
            ICoreClientAPI? capi = api;
            mapRestricted = false;
            observedPacket = null;
            observedState = null;
            if (capi != null && hasSavedMinimapPreference)
            {
                capi.Settings.Bool.Set(
                    MinimapSetting,
                    savedMinimapPreference,
                    shouldTriggerWatchers: false
                );
            }
            hasSavedMinimapPreference = false;
        }

        public override void Dispose()
        {
            ICoreClientAPI? capi = api;
            if (capi != null)
            {
                OnLeaveWorld();
                capi.Event.LeaveWorld -= OnLeaveWorld;
                if (tickListenerId != 0)
                {
                    capi.Event.UnregisterGameTickListener(tickListenerId);
                    tickListenerId = 0;
                }
            }

            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            if (ReferenceEquals(activeInstance, this))
            {
                activeInstance = null;
            }
            api = null;
            base.Dispose();
        }
    }
}
