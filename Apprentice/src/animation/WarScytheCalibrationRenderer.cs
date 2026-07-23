using System;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    internal sealed class WarScytheCalibrationRenderer : IRenderer
    {
        private static readonly int RightGripColor =
            ColorUtil.ColorFromRgba(45, 235, 90, 255);
        private static readonly int LeftGripColor =
            ColorUtil.ColorFromRgba(45, 145, 255, 255);
        private static readonly int BladeColor =
            ColorUtil.ColorFromRgba(255, 166, 35, 255);
        private static readonly int HeadColor =
            ColorUtil.ColorFromRgba(255, 45, 45, 255);
        private static readonly int TorsoColor =
            ColorUtil.ColorFromRgba(255, 225, 45, 255);
        private static readonly int HandColor =
            ColorUtil.ColorFromRgba(255, 255, 255, 255);

        private readonly ICoreClientAPI api;
        private readonly WarScytheAnimationEditor editor;
        private bool disposed;

        public WarScytheCalibrationRenderer(
            ICoreClientAPI api,
            WarScytheAnimationEditor editor)
        {
            this.api = api;
            this.editor = editor;
            api.Event.RegisterRenderer(
                this,
                EnumRenderStage.Opaque,
                "apprentice-war-scythe-calibration"
            );
        }

        public double RenderOrder => 0.99;
        public int RenderRange => 9999;

        public void OnRenderFrame(
            float deltaTime,
            EnumRenderStage stage)
        {
            if (disposed || stage != EnumRenderStage.Opaque ||
                !editor.TryGetDebugGeometry(
                    out WarScytheDebugGeometry geometry))
            {
                return;
            }

            api.Render.GLDisableDepthTest();
            DrawWireBox(geometry.RightGripCorners, RightGripColor);
            DrawWireBox(geometry.LeftGripCorners, LeftGripColor);
            DrawWireBox(geometry.BladeBoundsCorners, BladeColor);
            DrawWireBox(geometry.HeadNeckBoundsCorners, HeadColor);
            DrawWireBox(geometry.TorsoBoundsCorners, TorsoColor);
            DrawPoint(geometry.RightHand, HandColor);
            DrawPoint(geometry.LeftHand, HandColor);
            api.Render.GLEnableDepthTest();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        }

        private void DrawWireBox(Vec3d[] corners, int color)
        {
            if (corners.Length < 8) return;

            DrawLine(corners[0], corners[1], color);
            DrawLine(corners[1], corners[3], color);
            DrawLine(corners[3], corners[2], color);
            DrawLine(corners[2], corners[0], color);
            DrawLine(corners[4], corners[5], color);
            DrawLine(corners[5], corners[7], color);
            DrawLine(corners[7], corners[6], color);
            DrawLine(corners[6], corners[4], color);
            DrawLine(corners[0], corners[4], color);
            DrawLine(corners[1], corners[5], color);
            DrawLine(corners[2], corners[6], color);
            DrawLine(corners[3], corners[7], color);
        }

        private void DrawPoint(Vec3d center, int color)
        {
            const double Radius = 0.035;
            Vec3d[] corners =
            {
                new(center.X - Radius, center.Y - Radius, center.Z - Radius),
                new(center.X - Radius, center.Y - Radius, center.Z + Radius),
                new(center.X - Radius, center.Y + Radius, center.Z - Radius),
                new(center.X - Radius, center.Y + Radius, center.Z + Radius),
                new(center.X + Radius, center.Y - Radius, center.Z - Radius),
                new(center.X + Radius, center.Y - Radius, center.Z + Radius),
                new(center.X + Radius, center.Y + Radius, center.Z - Radius),
                new(center.X + Radius, center.Y + Radius, center.Z + Radius)
            };
            DrawWireBox(corners, color);
        }

        private void DrawLine(Vec3d start, Vec3d end, int color)
        {
            BlockPos origin = new(
                (int)Math.Floor(start.X),
                (int)Math.Floor(start.Y),
                (int)Math.Floor(start.Z)
            );
            api.Render.RenderLine(
                origin,
                (float)(start.X - origin.X),
                (float)(start.Y - origin.Y),
                (float)(start.Z - origin.Z),
                (float)(end.X - origin.X),
                (float)(end.Y - origin.Y),
                (float)(end.Z - origin.Z),
                color
            );
        }
    }
}
