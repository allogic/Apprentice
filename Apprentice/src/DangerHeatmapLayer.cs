using System;
using System.Collections.Generic;
using System.Text;
using Cairo;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace Apprentice
{
    /// <summary>
    /// One isolated world-map layer. It reprojects its cached radial bitmap
    /// while the viewport moves and rebuilds only after the view settles or
    /// the server-owned visual state changes.
    /// </summary>
    public sealed class DangerHeatmapLayer : MarkerMapLayer
    {
        // The overlay contains only eleven flat concentric color regions.
        // A filtered 256px surface preserves their appearance while reducing
        // Cairo work and GPU upload traffic to one quarter of the old 512px
        // texture.
        private const int TextureSize = 256;
        private const int LegendWidth = 238;
        private const int LegendRowHeight = 19;
        private const int LegendPadding = 8;
        private const float ViewportSettleSeconds = 0.12f;
        private const long SharedStateRequestCooldownMs = 10000;
        private const double OverlayAlpha = 0.24;
        private const double FullMapMinimumWidth = 500;
        private const double FullMapMinimumHeight = 400;
        // Vanilla terrain chunks render at z=50 and waypoint icons at z=60.
        // Keep the heatmap between them so it tints the terrain without
        // hiding waypoints or falling behind the map canvas.
        private const float HeatmapRingDepth = 55f;
        private readonly ICoreAPI coreApi;
        private readonly IWorldMapManager worldMapManager;

        private ICoreClientAPI? capi;
        private DangerWorldState? state;
        private LoadedTexture? overlayTexture;
        private LoadedTexture? legendTexture;
        private double cachedViewX1 = double.NaN;
        private double cachedViewZ1 = double.NaN;
        private double cachedViewX2 = double.NaN;
        private double cachedViewZ2 = double.NaN;
        private double pendingViewX1 = double.NaN;
        private double pendingViewZ1 = double.NaN;
        private double pendingViewX2 = double.NaN;
        private double pendingViewZ2 = double.NaN;
        private float pendingViewStableSeconds;
        private float stateRequestCooldown;
        private bool loggedFirstRender;
        private bool loggedFirstTextureBuild;

        public override bool RequireChunkLoaded => false;
        public override string Title =>
            "Apprentice concentric realms (levels 0-10)";
        public override EnumMapAppSide DataSide => EnumMapAppSide.Client;
        public override string LayerGroupCode => "apprentice-danger-heatmap";

        public DangerHeatmapLayer(ICoreAPI api, IWorldMapManager mapSink)
            : base(api, mapSink)
        {
            coreApi = api;
            worldMapManager = mapSink;
            if (api.Side == EnumAppSide.Client)
            {
                capi = (ICoreClientAPI)api;
                DangerHeatmapClientRuntime.Layer = this;
                // Start visible on first use.  After that the map tab owns
                // this flag; never force it back on when the map is reopened.
                Active = true;

                // Network state can arrive before WorldMapManager creates its
                // layer instances. Keep the last authoritative snapshot and
                // hydrate late-created layers immediately.
                DangerHeatmapStatePacket? latest =
                    DangerHeatmapClientRuntime.LatestState;
                if (latest != null)
                {
                    ApplyState(latest);
                }
            }
        }

        public override void OnViewChangedServer(
            IServerPlayer fromPlayer,
            int x1,
            int z1,
            int x2,
            int z2)
        {
            // The realm snapshot is independent of the visible map bounds.
            // Sending it for every pan/zoom event creates duplicate client
            // work. PlayerReady, an explicit request and OnMapOpenedServer
            // already cover the complete layer lifecycle.
        }

        public override void OnMapOpenedServer(IServerPlayer fromPlayer)
        {
            // The gameplay-channel snapshot can arrive before the client has
            // attached its map-layer handler.  Always send the authoritative
            // state again through the world-map protocol when the player
            // opens the map; this is the lifecycle point guaranteed by a
            // server-side map layer.
            SendState(fromPlayer);
        }

        [Obsolete]
        public override void OnViewChangedServer(
            IServerPlayer fromPlayer,
            List<FastVec2i> nowVisible,
            List<FastVec2i> nowHidden)
        {
            // See the current bounds overload above.
        }

        internal bool SendState(IServerPlayer player)
        {
            DangerWorldState? current = DangerTierRuntime.WorldState;
            if (current == null) return false;
            worldMapManager.SendMapDataToClient(
                this,
                player,
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(current))
            );
            coreApi.Logger.Notification(
                "[Apprentice] Sent danger heatmap state to {0} through the world-map channel.",
                player.PlayerName
            );
            return true;
        }

        public override void OnDataFromServer(byte[] data)
        {
            try
            {
                if (data == null || data.Length == 0)
                {
                    return;
                }

                capi?.Logger.Notification(
                    "[Apprentice] Received {0} bytes of danger heatmap data through the world-map channel.",
                    data.Length
                );
                DangerWorldState? received = JsonConvert.DeserializeObject<DangerWorldState>(
                    Encoding.UTF8.GetString(data)
                );
                if (received == null || received.RingWidth <= 0 ||
                    received.Palette == null ||
                    received.Palette.Length != received.MaximumTier + 1 ||
                    received.RealmNames == null ||
                    received.RealmNames.Length !=
                        received.MaximumTier + 1)
                {
                    return;
                }

                ApplyState(received);
            }
            catch (Exception exception)
            {
                capi?.Logger.Warning(
                    "[Apprentice] Ignored invalid danger heatmap data: {0}",
                    exception.Message
                );
            }
        }

        internal void ApplyState(DangerHeatmapStatePacket packet)
        {
            ApplyState(packet.ToState());
        }

        private void ApplyState(DangerWorldState received)
        {
            if (received.RingWidth <= 0 || received.MaximumTier < 0 ||
                received.Palette == null ||
                received.Palette.Length != received.MaximumTier + 1 ||
                received.RealmNames == null ||
                received.RealmNames.Length !=
                    received.MaximumTier + 1)
            {
                capi?.Logger.Warning(
                    "[Apprentice] Ignored an incomplete danger heatmap snapshot."
                );
                return;
            }

            if (HasSameVisualState(state, received))
            {
                return;
            }

            state = received;
            // Enabled is the server feature switch.  Active is the user's
            // map-tab choice and must remain independently toggleable.
            if (!received.Enabled)
            {
                Active = false;
            }
            InvalidateViewTexture();
            RebuildLegendTexture();
            capi?.Logger.Notification(
                "[Apprentice] Realm heatmap ready at X={0:0}, Z={1:0} with levels 0-{2}.",
                received.AnchorX,
                received.AnchorZ,
                received.MaximumTier
            );
        }

        public override void OnMapOpenedClient()
        {
            DangerHeatmapStatePacket? latest =
                DangerHeatmapClientRuntime.LatestState;
            if (state == null && latest != null)
            {
                ApplyState(latest);
            }
            RequestStateIfMissing(force: true);
            if (Active)
            {
                EnsureTerrainVisible();
            }
        }

        public override void OnTick(float dt)
        {
            stateRequestCooldown = Math.Max(0, stateRequestCooldown - dt);
            RequestStateIfMissing(force: false);
            EnsureTerrainVisible();
        }

        private void RequestStateIfMissing(bool force)
        {
            if (state != null || capi == null ||
                worldMapManager is not WorldMapManager manager ||
                !manager.IsOpened || (!force && stateRequestCooldown > 0))
            {
                return;
            }

            long now = capi.ElapsedMilliseconds;
            long lastRequest =
                DangerHeatmapClientRuntime.LastStateRequestMs;
            if (lastRequest != long.MinValue &&
                now - lastRequest < SharedStateRequestCooldownMs)
            {
                return;
            }

            stateRequestCooldown = 10f;
            DangerHeatmapClientRuntime.LastStateRequestMs = now;
            Action? request = DangerHeatmapClientRuntime.RequestState;
            if (request == null)
            {
                capi.Logger.Error(
                    "[Apprentice] Danger heatmap cannot request state because the client packet sender is unavailable."
                );
                return;
            }

            capi.Logger.Notification(
                "[Apprentice] Requesting danger heatmap state from the server."
            );
            request.Invoke();
        }

        private void EnsureTerrainVisible()
        {
            if (!Active || capi == null ||
                worldMapManager is not WorldMapManager manager)
            {
                return;
            }

            // World-map side tabs are independent toggles.  A heatmap without
            // the terrain layer is unreadable, so selecting this overlay also
            // guarantees that the vanilla terrain base remains enabled.
            MapLayer? terrain = manager.MapLayers.Find(layer =>
                layer is ChunkMapLayer);
            if (terrain != null)
            {
                terrain.Active = true;
            }
        }

        private void InvalidateViewTexture()
        {
            overlayTexture?.Dispose();
            overlayTexture = null;
            cachedViewX1 = double.NaN;
            cachedViewZ1 = double.NaN;
            cachedViewX2 = double.NaN;
            cachedViewZ2 = double.NaN;
            pendingViewX1 = double.NaN;
            pendingViewZ1 = double.NaN;
            pendingViewX2 = double.NaN;
            pendingViewZ2 = double.NaN;
            pendingViewStableSeconds = 0;
        }

        private void EnsureViewportTexture(GuiElementMap map, float dt)
        {
            if (capi == null || state == null || state.Palette == null ||
                state.Palette.Length != state.MaximumTier + 1)
            {
                return;
            }

            Cuboidd view = map.CurrentBlockViewBounds;
            if (overlayTexture != null && !overlayTexture.Disposed &&
                Math.Abs(view.X1 - cachedViewX1) < 2 &&
                Math.Abs(view.Z1 - cachedViewZ1) < 2 &&
                Math.Abs(view.X2 - cachedViewX2) < 2 &&
                Math.Abs(view.Z2 - cachedViewZ2) < 2)
            {
                return;
            }

            bool samePendingView =
                Math.Abs(view.X1 - pendingViewX1) < 2 &&
                Math.Abs(view.Z1 - pendingViewZ1) < 2 &&
                Math.Abs(view.X2 - pendingViewX2) < 2 &&
                Math.Abs(view.Z2 - pendingViewZ2) < 2;
            if (!samePendingView)
            {
                pendingViewX1 = view.X1;
                pendingViewZ1 = view.Z1;
                pendingViewX2 = view.X2;
                pendingViewZ2 = view.Z2;
                pendingViewStableSeconds = 0;
                return;
            }

            pendingViewStableSeconds += Math.Clamp(
                dt,
                0,
                0.05f
            );
            if (pendingViewStableSeconds < ViewportSettleSeconds)
            {
                return;
            }

            double viewWidth = Math.Max(
                1,
                pendingViewX2 - pendingViewX1
            );
            double viewHeight = Math.Max(
                1,
                pendingViewZ2 - pendingViewZ1
            );
            double scaleX = TextureSize / viewWidth;
            double scaleY = TextureSize / viewHeight;
            double centerX =
                (state.AnchorX - pendingViewX1) * scaleX;
            double centerY =
                (state.AnchorZ - pendingViewZ1) * scaleY;

            using ImageSurface surface = new((Format)0, TextureSize, TextureSize);
            using Context context = new(surface);
            context.Operator = Operator.Source;

            (double backgroundRed, double backgroundGreen, double backgroundBlue) =
                ParseColor(state.Palette[state.MaximumTier]);
            context.SetSourceRGBA(
                backgroundRed,
                backgroundGreen,
                backgroundBlue,
                OverlayAlpha
            );
            context.Paint();

            // Build a viewport-sized overlay instead of scaling one world-size
            // quad to tens of thousands of GUI pixels.  Cairo clips these
            // ellipses into the 256px surface, then the GPU draws one ordinary
            // map-sized texture. Operator.Source keeps every tier a distinct
            // tint instead of alpha-blending all inner disks together.
            for (int tier = state.MaximumTier - 1; tier >= 0; tier--)
            {
                double boundary = state.BaseRadius + state.RingWidth * tier;
                double radiusX = Math.Max(0.001, boundary * scaleX);
                double radiusY = Math.Max(0.001, boundary * scaleY);
                (double red, double green, double blue) = ParseColor(
                    state.Palette[tier]
                );
                context.SetSourceRGBA(
                    red,
                    green,
                    blue,
                    OverlayAlpha
                );
                context.Save();
                context.Translate(centerX, centerY);
                context.Scale(radiusX, radiusY);
                context.Arc(0, 0, 1, 0, Math.PI * 2);
                context.Restore();
                context.Fill();
            }

            LoadedTexture texture = overlayTexture ?? new LoadedTexture(
                capi,
                0,
                TextureSize,
                TextureSize
            );
            capi.Gui.LoadOrUpdateCairoTexture(
                surface,
                linearMag: true,
                ref texture
            );
            overlayTexture = texture;
            cachedViewX1 = pendingViewX1;
            cachedViewZ1 = pendingViewZ1;
            cachedViewX2 = pendingViewX2;
            cachedViewZ2 = pendingViewZ2;
            if (!loggedFirstTextureBuild)
            {
                capi.Logger.Notification(
                    "[Apprentice] Deferred danger heatmap texture ready ({0}x{0}).",
                    TextureSize
                );
                loggedFirstTextureBuild = true;
            }
        }

        private void RebuildLegendTexture()
        {
            if (capi == null || state?.Palette == null ||
                state.RealmNames == null ||
                state.Palette.Length != state.RealmNames.Length)
            {
                return;
            }

            int statusRows = 1;
            int height = LegendPadding * 2 +
                (state.RealmNames.Length + statusRows) *
                    LegendRowHeight;
            using ImageSurface surface = new(
                (Format)0,
                LegendWidth,
                height
            );
            using Context context = new(surface);
            context.Operator = Operator.Source;
            context.SetSourceRGBA(0.035, 0.035, 0.04, 0.86);
            context.Paint();
            context.Operator = Operator.Over;
            context.SelectFontFace(
                "Sans",
                FontSlant.Normal,
                FontWeight.Normal
            );
            context.SetFontSize(12);

            bool worldgenActive =
                state.RealmWorldgenEnabled &&
                state.WorldgenProfile ==
                    WorldZoneLayout.ConcentricRealmsProfile;
            context.SetSourceRGB(
                worldgenActive ? 0.55 : 1.0,
                worldgenActive ? 0.92 : 0.45,
                worldgenActive ? 0.62 : 0.35
            );
            context.MoveTo(8, LegendPadding + 14);
            context.ShowText(
                worldgenActive
                    ? "Biome worldgen: active"
                    : "Biome worldgen: disabled"
            );

            for (int level = 0;
                level < state.RealmNames.Length;
                level++)
            {
                double y = LegendPadding +
                    (level + statusRows) * LegendRowHeight;
                (double red, double green, double blue) = ParseColor(
                    state.Palette[level]
                );
                context.SetSourceRGB(red, green, blue);
                context.Rectangle(8, y + 3, 12, 12);
                context.Fill();

                context.SetSourceRGB(0.96, 0.96, 0.96);
                context.MoveTo(27, y + 14);
                context.ShowText(
                    $"{level}. {state.RealmNames[level]}"
                );
            }

            LoadedTexture texture = legendTexture ?? new LoadedTexture(
                capi,
                0,
                LegendWidth,
                height
            );
            capi.Gui.LoadOrUpdateCairoTexture(
                surface,
                linearMag: true,
                ref texture
            );
            legendTexture = texture;
        }

        public override void Render(GuiElementMap map, float dt)
        {
            // GuiDialogWorldMap reuses every map layer for both its full map
            // and its 250x250 HUD minimap. Realm tinting and its legend are
            // useful only on the full map; on the HUD they obscure terrain
            // and consume most of the available space.
            if (map.Bounds.OuterWidth < FullMapMinimumWidth ||
                map.Bounds.OuterHeight < FullMapMinimumHeight)
            {
                return;
            }

            EnsureTerrainVisible();
            DangerHeatmapStatePacket? latest =
                DangerHeatmapClientRuntime.LatestState;
            if (state == null && latest != null)
            {
                ApplyState(latest);
            }

            if (!Active || capi == null || state?.Enabled != true ||
                state.Palette == null || state.Palette.Length == 0)
            {
                return;
            }

            EnsureViewportTexture(map, dt);
            if (overlayTexture == null || overlayTexture.Disposed) return;

            if (!loggedFirstRender)
            {
                capi.Logger.Notification(
                    "[Apprentice] Rendering danger heatmap over map bounds {0}x{1}.",
                    map.Bounds.OuterWidth,
                    map.Bounds.OuterHeight
                );
                loggedFirstRender = true;
            }

            // Cairo textures have premultiplied alpha.  The dedicated API
            // preserves that alpha and the map below it; driving the GUI
            // shader manually made the outer tier render as an opaque brown
            // rectangle on some GPUs.  Depth 55 intentionally sits above
            // vanilla terrain (50) and below waypoint icons (60).
            IRenderAPI render = capi.Render;
            Cuboidd currentView = map.CurrentBlockViewBounds;
            double currentWidth = Math.Max(
                1,
                currentView.X2 - currentView.X1
            );
            double currentHeight = Math.Max(
                1,
                currentView.Z2 - currentView.Z1
            );
            double renderX = map.Bounds.renderX +
                (cachedViewX1 - currentView.X1) /
                    currentWidth * map.Bounds.OuterWidth;
            double renderY = map.Bounds.renderY +
                (cachedViewZ1 - currentView.Z1) /
                    currentHeight * map.Bounds.OuterHeight;
            double renderWidth =
                (cachedViewX2 - cachedViewX1) /
                    currentWidth * map.Bounds.OuterWidth;
            double renderHeight =
                (cachedViewZ2 - cachedViewZ1) /
                    currentHeight * map.Bounds.OuterHeight;
            render.Render2DTexturePremultipliedAlpha(
                overlayTexture.TextureId,
                renderX,
                renderY,
                renderWidth,
                renderHeight,
                HeatmapRingDepth
            );
            if (legendTexture != null && !legendTexture.Disposed)
            {
                render.Render2DTexturePremultipliedAlpha(
                    legendTexture.TextureId,
                    map.Bounds.renderX + 12,
                    map.Bounds.renderY + 12,
                    LegendWidth,
                    legendTexture.Height,
                    HeatmapRingDepth + 1
                );
            }
        }

        private static (double Red, double Green, double Blue) ParseColor(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 7)
            {
                return (1, 1, 1);
            }

            return (
                Convert.ToInt32(value.Substring(1, 2), 16) / 255d,
                Convert.ToInt32(value.Substring(3, 2), 16) / 255d,
                Convert.ToInt32(value.Substring(5, 2), 16) / 255d
            );
        }

        private static bool HasSameVisualState(
            DangerWorldState? current,
            DangerWorldState received)
        {
            if (current == null ||
                current.SchemaVersion != received.SchemaVersion ||
                current.Enabled != received.Enabled ||
                current.AnchorX != received.AnchorX ||
                current.AnchorZ != received.AnchorZ ||
                current.BaseRadius != received.BaseRadius ||
                current.RingWidth != received.RingWidth ||
                current.MaximumTier != received.MaximumTier ||
                current.WorldgenProfile != received.WorldgenProfile ||
                current.RealmWorldgenEnabled !=
                    received.RealmWorldgenEnabled)
            {
                return false;
            }

            return SameStrings(current.Palette, received.Palette) &&
                SameStrings(current.RealmNames, received.RealmNames);
        }

        private static bool SameStrings(
            string[]? first,
            string[]? second)
        {
            if (ReferenceEquals(first, second))
            {
                return true;
            }
            if (first == null || second == null ||
                first.Length != second.Length)
            {
                return false;
            }

            for (int index = 0; index < first.Length; index++)
            {
                if (!string.Equals(
                    first[index],
                    second[index],
                    StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        public override void Dispose()
        {
            if (ReferenceEquals(DangerHeatmapClientRuntime.Layer, this))
            {
                DangerHeatmapClientRuntime.Layer = null;
            }
            overlayTexture?.Dispose();
            overlayTexture = null;
            legendTexture?.Dispose();
            legendTexture = null;
            base.Dispose();
        }
    }

    /// <summary>
    /// Vintage Story 1.22 can create and feed a third-party map layer without
    /// ever adding it to the GUI render loop.  The vanilla terrain layer is
    /// always rendered, so its postfix is the stable integration point for a
    /// transparent overlay.  The heatmap remains a normal map layer for its
    /// tab, networking and lifecycle; only its draw call is bridged here.
    /// </summary>
	internal static class DangerHeatmapTerrainRenderPatch
	{
		internal static void Postfix(GuiElementMap __0, float __1)
		{
			// Harmony's positional arguments remain stable even when Vintage
			// Story renames Render's mapElem/dt parameters between releases.
			DangerHeatmapClientRuntime.Layer?.Render(__0, __1);
		}
	}
}
