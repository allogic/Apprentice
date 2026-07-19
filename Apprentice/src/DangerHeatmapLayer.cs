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
    /// One isolated world-map layer. The radial bitmap is rebuilt only when
    /// the server-owned anchor/ring/palette cache key changes; zooming merely
    /// rescales the existing texture.
    /// </summary>
    public sealed class DangerHeatmapLayer : MarkerMapLayer
    {
        private const int TextureSize = 512;
        // Vanilla terrain chunks render at z=50 and waypoint icons at z=60.
        // Keep the heatmap between them so it tints the terrain without
        // hiding waypoints or falling behind the map canvas.
        private const float HeatmapRingDepth = 55f;
        private readonly ICoreAPI coreApi;
        private readonly IWorldMapManager worldMapManager;

        private ICoreClientAPI? capi;
        private DangerWorldState? state;
        private LoadedTexture? overlayTexture;
        private double cachedViewX1 = double.NaN;
        private double cachedViewZ1 = double.NaN;
        private double cachedViewX2 = double.NaN;
        private double cachedViewZ2 = double.NaN;
        private float stateRequestCooldown;
        private bool loggedFirstRender;

        public override bool RequireChunkLoaded => false;
        public override string Title => "Apprentice danger regions (tiers 0-10)";
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
                // The layer has its own side-tab, but the map API does not
                // reliably deliver the first tab activation to mod layers.
                // Start enabled so a received server snapshot is visible.
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
            SendState(fromPlayer);
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
            SendState(fromPlayer);
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
                    received.Palette.Length != received.MaximumTier + 1)
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
                received.Palette.Length != received.MaximumTier + 1)
            {
                capi?.Logger.Warning(
                    "[Apprentice] Ignored an incomplete danger heatmap snapshot."
                );
                return;
            }

            state = received;
            Active = received.Enabled;
            InvalidateViewTexture();
            capi?.Logger.Notification(
                "[Apprentice] Danger heatmap ready at X={0:0}, Z={1:0} with tiers 0-{2}.",
                received.AnchorX,
                received.AnchorZ,
                received.MaximumTier
            );
        }

        public override void OnMapOpenedClient()
        {
            Active = true;
            DangerHeatmapStatePacket? latest =
                DangerHeatmapClientRuntime.LatestState;
            if (state == null && latest != null)
            {
                ApplyState(latest);
            }
            RequestStateIfMissing(force: true);
            EnsureTerrainVisible();
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

            stateRequestCooldown = 2f;
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
            cachedViewX1 = double.NaN;
            cachedViewZ1 = double.NaN;
            cachedViewX2 = double.NaN;
            cachedViewZ2 = double.NaN;
        }

        private void EnsureViewportTexture(GuiElementMap map)
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

            double viewWidth = Math.Max(1, view.X2 - view.X1);
            double viewHeight = Math.Max(1, view.Z2 - view.Z1);
            double scaleX = TextureSize / viewWidth;
            double scaleY = TextureSize / viewHeight;
            double centerX = (state.AnchorX - view.X1) * scaleX;
            double centerY = (state.AnchorZ - view.Z1) * scaleY;

            using ImageSurface surface = new((Format)0, TextureSize, TextureSize);
            using Context context = new(surface);
            context.Operator = Operator.Source;

            (double backgroundRed, double backgroundGreen, double backgroundBlue) =
                ParseColor(state.Palette[state.MaximumTier]);
            context.SetSourceRGBA(
                backgroundRed,
                backgroundGreen,
                backgroundBlue,
                0.58
            );
            context.Paint();

            // Build a viewport-sized overlay instead of scaling one world-size
            // quad to tens of thousands of GUI pixels.  Cairo clips these
            // ellipses into the 512px surface, then the GPU draws one ordinary
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
                context.SetSourceRGBA(red, green, blue, 0.58);
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
            cachedViewX1 = view.X1;
            cachedViewZ1 = view.Z1;
            cachedViewX2 = view.X2;
            cachedViewZ2 = view.Z2;
        }

        public override void Render(GuiElementMap map, float dt)
        {
            EnsureTerrainVisible();
            DangerHeatmapStatePacket? latest =
                DangerHeatmapClientRuntime.LatestState;
            if (state == null && latest != null)
            {
                ApplyState(latest);
            }

            if (capi == null || state?.Enabled != true ||
                state.Palette == null || state.Palette.Length == 0)
            {
                return;
            }

            EnsureViewportTexture(map);
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
            render.Render2DTexturePremultipliedAlpha(
                overlayTexture.TextureId,
                map.Bounds.renderX,
                map.Bounds.renderY,
                map.Bounds.OuterWidth,
                map.Bounds.OuterHeight,
                HeatmapRingDepth
            );
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

        public override void Dispose()
        {
            if (ReferenceEquals(DangerHeatmapClientRuntime.Layer, this))
            {
                DangerHeatmapClientRuntime.Layer = null;
            }
            overlayTexture?.Dispose();
            overlayTexture = null;
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
