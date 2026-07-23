using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    internal sealed class WarScytheGeometryProbe
    {
        private readonly WarScytheAcceptanceDefinition acceptance;
        private readonly BladeMarker[] bladeMarkers;

        public WarScytheGeometryProbe(ICoreClientAPI api)
            : this(
                api.Assets.Get(new AssetLocation(
                    "apprentice",
                    "shapes/item/2.7/war-scythe.json"
                )).ToObject<Shape>(),
                WarScytheAcceptanceDefinition.Load(api)
            )
        {
        }

        internal WarScytheGeometryProbe(Shape shape)
            : this(shape, CreateDefaultAcceptance())
        {
        }

        internal WarScytheGeometryProbe(
            Shape shape,
            WarScytheAcceptanceDefinition acceptance)
        {
            this.acceptance = acceptance;
            bladeMarkers = shape.Elements
                .SelectMany(Walk)
                .Where(element => acceptance.BladeElementPrefixes.Any(
                    prefix => element.Name?.StartsWith(
                        prefix,
                        StringComparison.Ordinal
                    ) == true))
                .Select(BladeMarker.Create)
                .ToArray();

            if (bladeMarkers.Length == 0)
            {
                throw new InvalidOperationException(
                    "The War Scythe shape contains no complete-mesh blade elements."
                );
            }
        }

        internal int BladeMarkerCount => bladeMarkers.Length;
        internal WarScytheAcceptanceDefinition Acceptance => acceptance;

        public bool TrySample(
            EntityAgent entity,
            ItemStack stack,
            out WarScytheGeometrySample sample)
        {
            sample = default;
            IAnimator? animator = entity.AnimManager?.Animator;
            AttachmentPointAndPose? right = animator?
                .GetAttachmentPointPose("RightHand");
            AttachmentPointAndPose? left = animator?
                .GetAttachmentPointPose("LeftHand");
            ElementPose? head = animator?.GetPosebyName(
                "Head",
                StringComparison.OrdinalIgnoreCase
            );
            ElementPose? neck = animator?.GetPosebyName(
                "Neck",
                StringComparison.OrdinalIgnoreCase
            );
            ElementPose? torso = animator?.GetPosebyName(
                "UpperTorso",
                StringComparison.OrdinalIgnoreCase
            );
            ModelTransform? configuredTransform = stack.Item.TpHandTransform;
            if (right?.AnimModelMatrix == null || right.AttachPoint == null ||
                left?.AnimModelMatrix == null || left.AttachPoint == null ||
                head?.ForElement == null || head.AnimModelMatrix == null ||
                neck?.ForElement == null || neck.AnimModelMatrix == null ||
                torso?.ForElement == null || torso.AnimModelMatrix == null ||
                configuredTransform == null)
            {
                return false;
            }

            ModelTransform transform = configuredTransform
                .Clone()
                .EnsureDefaultValues();
            Matrixf itemMatrix = BuildItemMatrix(right, transform);
            Vec3f rightHand = GetAttachmentPosition(right);
            Vec3f leftHand = GetAttachmentPosition(left);
            Matrixf inverseItemMatrix = itemMatrix.Clone().Invert();
            Vec3f rightHandItemLocal = Transform(
                inverseItemMatrix,
                new Vec4f(rightHand.X, rightHand.Y, rightHand.Z, 1)
            );
            Vec3f leftHandItemLocal = Transform(
                inverseItemMatrix,
                new Vec4f(leftHand.X, leftHand.Y, leftHand.Z, 1)
            );
            float itemScale = Math.Max(
                transform.ScaleXYZ.X,
                Math.Max(transform.ScaleXYZ.Y, transform.ScaleXYZ.Z)
            );

            Bounds3 bladeBounds = Bounds3.Empty;
            Bounds3 headBounds = GetPoseBounds(head);
            Bounds3 neckBounds = GetPoseBounds(neck);
            Bounds3 torsoBounds = GetPoseBounds(torso);
            bool headOrNeckOverlap = false;
            bool torsoOverlap = false;
            foreach (BladeMarker marker in bladeMarkers)
            {
                Bounds3 markerBounds = marker.Transform(itemMatrix);
                bladeBounds.Include(markerBounds);
                headOrNeckOverlap |= markerBounds.Intersects(headBounds) ||
                    markerBounds.Intersects(neckBounds);
                torsoOverlap |= markerBounds.Intersects(torsoBounds);
            }

            if (!bladeBounds.IsValid || !headBounds.IsValid ||
                !neckBounds.IsValid ||
                !torsoBounds.IsValid)
            {
                return false;
            }

            sample = new WarScytheGeometrySample(
                DistanceToBox(
                    rightHandItemLocal,
                    acceptance.RightGripMinimum,
                    acceptance.RightGripMaximum
                ) * itemScale,
                DistanceToBox(
                    leftHandItemLocal,
                    acceptance.LeftGripMinimum,
                    acceptance.LeftGripMaximum
                ) * itemScale,
                Distance(rightHand, leftHand),
                bladeBounds.CenterX,
                bladeBounds.CenterY,
                bladeBounds.MinY,
                bladeBounds.MaxY,
                torsoBounds.MinY,
                torsoBounds.MaxY,
                Math.Min(headBounds.MinY, neckBounds.MinY),
                bladeBounds.MaxY > Math.Min(
                    headBounds.MinY,
                    neckBounds.MinY
                ),
                headOrNeckOverlap,
                torsoOverlap
            );
            return true;
        }

        public bool TryBuildDebugGeometry(
            EntityAgent entity,
            ItemStack stack,
            out WarScytheDebugGeometry geometry)
        {
            geometry = default;
            IAnimator? animator = entity.AnimManager?.Animator;
            AttachmentPointAndPose? right = animator?
                .GetAttachmentPointPose("RightHand");
            AttachmentPointAndPose? left = animator?
                .GetAttachmentPointPose("LeftHand");
            ElementPose? head = animator?.GetPosebyName(
                "Head",
                StringComparison.OrdinalIgnoreCase
            );
            ElementPose? neck = animator?.GetPosebyName(
                "Neck",
                StringComparison.OrdinalIgnoreCase
            );
            ElementPose? torso = animator?.GetPosebyName(
                "UpperTorso",
                StringComparison.OrdinalIgnoreCase
            );
            ModelTransform? configuredTransform = stack.Item.TpHandTransform;
            if (right?.AnimModelMatrix == null || right.AttachPoint == null ||
                left?.AnimModelMatrix == null || left.AttachPoint == null ||
                head?.ForElement == null || head.AnimModelMatrix == null ||
                neck?.ForElement == null || neck.AnimModelMatrix == null ||
                torso?.ForElement == null || torso.AnimModelMatrix == null ||
                configuredTransform == null)
            {
                return false;
            }

            Matrixf itemMatrix = BuildItemMatrix(
                right,
                configuredTransform.Clone().EnsureDefaultValues()
            );
            Bounds3 bladeBounds = Bounds3.Empty;
            foreach (BladeMarker marker in bladeMarkers)
            {
                bladeBounds.Include(marker.Transform(itemMatrix));
            }
            Bounds3 headNeckBounds = GetPoseBounds(head);
            headNeckBounds.Include(GetPoseBounds(neck));
            Bounds3 torsoBounds = GetPoseBounds(torso);
            if (!bladeBounds.IsValid || !headNeckBounds.IsValid ||
                !torsoBounds.IsValid)
            {
                return false;
            }

            Matrixf playerWorld = BuildPlayerWorldMatrix(entity);
            geometry = new WarScytheDebugGeometry(
                TransformBox(
                    playerWorld,
                    itemMatrix,
                    acceptance.RightGripMinimum,
                    acceptance.RightGripMaximum
                ),
                TransformBox(
                    playerWorld,
                    itemMatrix,
                    acceptance.LeftGripMinimum,
                    acceptance.LeftGripMaximum
                ),
                TransformBounds(playerWorld, bladeBounds),
                TransformBounds(playerWorld, headNeckBounds),
                TransformBounds(playerWorld, torsoBounds),
                TransformWorld(
                    playerWorld,
                    GetAttachmentPosition(right)
                ),
                TransformWorld(
                    playerWorld,
                    GetAttachmentPosition(left)
                )
            );
            return true;
        }

        private static Matrixf BuildPlayerWorldMatrix(EntityAgent entity)
        {
            float rotateX = entity.Properties.Client.Shape?.rotateX ?? 0;
            float rotateY = entity.Properties.Client.Shape?.rotateY ?? 0;
            float rotateZ = entity.Properties.Client.Shape?.rotateZ ?? 0;
            float size = entity.Properties.Client.Size;

            return new Matrixf()
                .Identity()
                .Translate(
                    entity.Pos.X,
                    entity.Pos.InternalY,
                    entity.Pos.Z
                )
                .Translate(0, entity.SelectionBox.Y2 / 2f, 0)
                .RotateX(entity.Pos.Roll + rotateX * GameMath.DEG2RAD)
                .RotateY(
                    entity.BodyYaw +
                    (90f + rotateY) * GameMath.DEG2RAD
                )
                .RotateZ(
                    ((entity as EntityPlayer)?.WalkPitch ?? 0) +
                    rotateZ * GameMath.DEG2RAD
                )
                .Translate(0, -entity.SelectionBox.Y2 / 2f, 0)
                .Scale(size, size, size)
                .Translate(-0.5f, 0, -0.5f);
        }

        private static Vec3d[] TransformBox(
            Matrixf playerWorld,
            Matrixf localMatrix,
            Vec3f minimum,
            Vec3f maximum) =>
            Corners(
                    minimum.X,
                    minimum.Y,
                    minimum.Z,
                    maximum.X,
                    maximum.Y,
                    maximum.Z
                )
                .Select(corner =>
                {
                    Vec3f local = Transform(localMatrix, corner);
                    return TransformWorld(playerWorld, local);
                })
                .ToArray();

        private static Vec3d[] TransformBounds(
            Matrixf playerWorld,
            Bounds3 bounds) =>
            Corners(
                    bounds.MinX,
                    bounds.MinY,
                    bounds.MinZ,
                    bounds.MaxX,
                    bounds.MaxY,
                    bounds.MaxZ
                )
                .Select(corner => TransformWorld(
                    playerWorld,
                    new Vec3f(corner.X, corner.Y, corner.Z)
                ))
                .ToArray();

        private static Vec3d TransformWorld(
            Matrixf playerWorld,
            Vec3f point)
        {
            Vec4f transformed = playerWorld.TransformVector(
                new Vec4f(point.X, point.Y, point.Z, 1)
            );
            return new Vec3d(
                transformed.X,
                transformed.Y,
                transformed.Z
            );
        }

        private static WarScytheAcceptanceDefinition
            CreateDefaultAcceptance() => new()
        {
            Version = 1,
            HeldItemCode = "apprentice:warscythe",
            ModelOrigin = new[] { 8f, 0f, 8f },
            ShaftAxis = new[] { 0f, 1f, 0f },
            RightGripMin = new[] { 6.65f, -2.5f, 6.65f },
            RightGripMax = new[] { 9.35f, 4.7f, 9.35f },
            LeftGripMin = new[] { 6.65f, 5.3f, 6.65f },
            LeftGripMax = new[] { 9.35f, 12.9f, 9.35f },
            BladeElementPrefixes = new[] { "blade-" },
            MaximumGripDistance = 0.03f,
            MinimumBladeCenterLateralSpan = 0.75f
        };

        private static IEnumerable<ShapeElement> Walk(ShapeElement element)
        {
            yield return element;
            if (element.Children == null) yield break;

            foreach (ShapeElement child in element.Children)
            {
                foreach (ShapeElement descendant in Walk(child))
                {
                    yield return descendant;
                }
            }
        }

        private static Matrixf BuildItemMatrix(
            AttachmentPointAndPose right,
            ModelTransform transform)
        {
            AttachmentPoint point = right.AttachPoint;
            return new Matrixf()
                .Set(right.AnimModelMatrix)
                .Translate(
                    transform.Origin.X,
                    transform.Origin.Y,
                    transform.Origin.Z
                )
                .Scale(
                    transform.ScaleXYZ.X,
                    transform.ScaleXYZ.Y,
                    transform.ScaleXYZ.Z
                )
                .Translate(
                    point.PosX / 16.0 + transform.Translation.X,
                    point.PosY / 16.0 + transform.Translation.Y,
                    point.PosZ / 16.0 + transform.Translation.Z
                )
                .RotateX((float)(point.RotationX + transform.Rotation.X) *
                    GameMath.DEG2RAD)
                .RotateY((float)(point.RotationY + transform.Rotation.Y) *
                    GameMath.DEG2RAD)
                .RotateZ((float)(point.RotationZ + transform.Rotation.Z) *
                    GameMath.DEG2RAD)
                .Translate(
                    -transform.Origin.X,
                    -transform.Origin.Y,
                    -transform.Origin.Z
                );
        }

        private static Vec3f GetAttachmentPosition(
            AttachmentPointAndPose attachment)
        {
            AttachmentPoint point = attachment.AttachPoint;
            Matrixf matrix = new Matrixf()
                .Set(attachment.AnimModelMatrix)
                .Translate(
                    point.PosX / 16.0,
                    point.PosY / 16.0,
                    point.PosZ / 16.0
                );
            return Transform(matrix, new Vec4f(0, 0, 0, 1));
        }

        private static Bounds3 GetPoseBounds(ElementPose pose)
        {
            ShapeElement element = pose.ForElement;
            if (element.From == null || element.To == null ||
                element.From.Length < 3 || element.To.Length < 3)
            {
                return Bounds3.Empty;
            }

            float sizeX = (float)Math.Abs(
                element.To[0] - element.From[0]) / 16f;
            float sizeY = (float)Math.Abs(
                element.To[1] - element.From[1]) / 16f;
            float sizeZ = (float)Math.Abs(
                element.To[2] - element.From[2]) / 16f;
            Matrixf matrix = new Matrixf().Set(pose.AnimModelMatrix);
            Bounds3 bounds = Bounds3.Empty;
            foreach (Vec4f corner in Corners(0, 0, 0, sizeX, sizeY, sizeZ))
            {
                bounds.Include(Transform(matrix, corner));
            }
            return bounds;
        }

        private static IEnumerable<Vec4f> Corners(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ)
        {
            foreach (float x in new[] { minX, maxX })
            foreach (float y in new[] { minY, maxY })
            foreach (float z in new[] { minZ, maxZ })
            {
                yield return new Vec4f(x, y, z, 1);
            }
        }

        private static Vec3f Transform(Matrixf matrix, Vec4f point)
        {
            Vec4f transformed = matrix.TransformVector(point);
            return new Vec3f(transformed.X, transformed.Y, transformed.Z);
        }

        private static float Distance(Vec3f first, Vec3f second)
        {
            float x = first.X - second.X;
            float y = first.Y - second.Y;
            float z = first.Z - second.Z;
            return MathF.Sqrt(x * x + y * y + z * z);
        }

        private static float DistanceToBox(
            Vec3f point,
            Vec3f minimum,
            Vec3f maximum)
        {
            float x = Math.Max(
                Math.Max(minimum.X - point.X, 0),
                point.X - maximum.X
            );
            float y = Math.Max(
                Math.Max(minimum.Y - point.Y, 0),
                point.Y - maximum.Y
            );
            float z = Math.Max(
                Math.Max(minimum.Z - point.Z, 0),
                point.Z - maximum.Z
            );
            return MathF.Sqrt(x * x + y * y + z * z);
        }

        private sealed class BladeMarker
        {
            private readonly Vec4f[] corners;

            private BladeMarker(Vec4f[] corners)
            {
                this.corners = corners;
            }

            public static BladeMarker Create(ShapeElement element)
            {
                if (element.From == null || element.To == null ||
                    element.From.Length < 3 || element.To.Length < 3)
                {
                    throw new InvalidOperationException(
                        $"Blade element '{element.Name}' has no complete box."
                    );
                }

                float minX = (float)element.From[0] / 16f;
                float minY = (float)element.From[1] / 16f;
                float minZ = (float)element.From[2] / 16f;
                float maxX = (float)element.To[0] / 16f;
                float maxY = (float)element.To[1] / 16f;
                float maxZ = (float)element.To[2] / 16f;
                double[] origin = element.RotationOrigin ??
                    new[] { 0.0, 0.0, 0.0 };
                Matrixf elementMatrix = new Matrixf()
                    .Identity()
                    .Translate(
                        origin[0] / 16.0,
                        origin[1] / 16.0,
                        origin[2] / 16.0
                    )
                    .RotateX((float)element.RotationX * GameMath.DEG2RAD)
                    .RotateY((float)element.RotationY * GameMath.DEG2RAD)
                    .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
                    .Translate(
                        -origin[0] / 16.0,
                        -origin[1] / 16.0,
                        -origin[2] / 16.0
                    );
                return new BladeMarker(Corners(
                        minX,
                        minY,
                        minZ,
                        maxX,
                        maxY,
                        maxZ)
                    .Select(point =>
                    {
                        Vec3f transformed = WarScytheGeometryProbe.Transform(
                            elementMatrix,
                            point
                        );
                        return new Vec4f(
                            transformed.X,
                            transformed.Y,
                            transformed.Z,
                            1
                        );
                    })
                    .ToArray());
            }

            public Bounds3 Transform(Matrixf itemMatrix)
            {
                Bounds3 bounds = Bounds3.Empty;
                foreach (Vec4f corner in corners)
                {
                    bounds.Include(WarScytheGeometryProbe.Transform(
                        itemMatrix,
                        corner
                    ));
                }
                return bounds;
            }
        }

        private struct Bounds3
        {
            public static Bounds3 Empty => new()
            {
                MinX = float.PositiveInfinity,
                MinY = float.PositiveInfinity,
                MinZ = float.PositiveInfinity,
                MaxX = float.NegativeInfinity,
                MaxY = float.NegativeInfinity,
                MaxZ = float.NegativeInfinity
            };

            public float MinX;
            public float MinY;
            public float MinZ;
            public float MaxX;
            public float MaxY;
            public float MaxZ;

            public bool IsValid => float.IsFinite(MinX) &&
                float.IsFinite(MinY) && float.IsFinite(MinZ) &&
                float.IsFinite(MaxX) && float.IsFinite(MaxY) &&
                float.IsFinite(MaxZ);
            public float CenterX => (MinX + MaxX) * 0.5f;
            public float CenterY => (MinY + MaxY) * 0.5f;

            public void Include(Vec3f point)
            {
                MinX = Math.Min(MinX, point.X);
                MinY = Math.Min(MinY, point.Y);
                MinZ = Math.Min(MinZ, point.Z);
                MaxX = Math.Max(MaxX, point.X);
                MaxY = Math.Max(MaxY, point.Y);
                MaxZ = Math.Max(MaxZ, point.Z);
            }

            public void Include(Bounds3 other)
            {
                if (!other.IsValid) return;
                Include(new Vec3f(other.MinX, other.MinY, other.MinZ));
                Include(new Vec3f(other.MaxX, other.MaxY, other.MaxZ));
            }

            public bool Intersects(Bounds3 other) => IsValid &&
                other.IsValid && MinX <= other.MaxX && MaxX >= other.MinX &&
                MinY <= other.MaxY && MaxY >= other.MinY &&
                MinZ <= other.MaxZ && MaxZ >= other.MinZ;
        }
    }

    internal readonly struct WarScytheDebugGeometry
    {
        public WarScytheDebugGeometry(
            Vec3d[] rightGripCorners,
            Vec3d[] leftGripCorners,
            Vec3d[] bladeBoundsCorners,
            Vec3d[] headNeckBoundsCorners,
            Vec3d[] torsoBoundsCorners,
            Vec3d rightHand,
            Vec3d leftHand)
        {
            RightGripCorners = rightGripCorners;
            LeftGripCorners = leftGripCorners;
            BladeBoundsCorners = bladeBoundsCorners;
            HeadNeckBoundsCorners = headNeckBoundsCorners;
            TorsoBoundsCorners = torsoBoundsCorners;
            RightHand = rightHand;
            LeftHand = leftHand;
        }

        public Vec3d[] RightGripCorners { get; }
        public Vec3d[] LeftGripCorners { get; }
        public Vec3d[] BladeBoundsCorners { get; }
        public Vec3d[] HeadNeckBoundsCorners { get; }
        public Vec3d[] TorsoBoundsCorners { get; }
        public Vec3d RightHand { get; }
        public Vec3d LeftHand { get; }
    }

    internal readonly struct WarScytheGeometrySample
    {
        public WarScytheGeometrySample(
            float rightGripDistance,
            float leftGripDistance,
            float handSeparation,
            float bladeCenterX,
            float bladeCenterY,
            float bladeMinY,
            float bladeMaxY,
            float torsoMinY,
            float torsoMaxY,
            float headNeckMinY,
            bool bladeAboveHeadOrNeck,
            bool headOrNeckOverlap,
            bool torsoOverlap)
        {
            RightGripDistance = rightGripDistance;
            LeftGripDistance = leftGripDistance;
            HandSeparation = handSeparation;
            BladeCenterX = bladeCenterX;
            BladeCenterY = bladeCenterY;
            BladeMinY = bladeMinY;
            BladeMaxY = bladeMaxY;
            TorsoMinY = torsoMinY;
            TorsoMaxY = torsoMaxY;
            HeadNeckMinY = headNeckMinY;
            BladeAboveHeadOrNeck = bladeAboveHeadOrNeck;
            HeadOrNeckOverlap = headOrNeckOverlap;
            TorsoOverlap = torsoOverlap;
        }

        public float RightGripDistance { get; }
        public float LeftGripDistance { get; }
        public float HandSeparation { get; }
        public float BladeCenterX { get; }
        public float BladeCenterY { get; }
        public float BladeMinY { get; }
        public float BladeMaxY { get; }
        public float TorsoMinY { get; }
        public float TorsoMaxY { get; }
        public float HeadNeckMinY { get; }
        public bool BladeAboveHeadOrNeck { get; }
        public bool HeadOrNeckOverlap { get; }
        public bool TorsoOverlap { get; }
    }

    internal sealed class WarScytheGeometryTrace
    {
        private readonly float maximumGripDistance;
        private readonly float minimumLateralSpan;
        private int samples;
        private int strikeSamples;
        private int headOrNeckOverlapSamples;
        private int torsoOverlapSamples;
        private int bladeAboveHeadSamples;
        private int leftGripMissSamples;
        private int bladeCenterOutsideTorsoSamples;
        private float maxRightGripDistance;
        private float maxLeftGripDistance;
        private float minHandSeparation = float.PositiveInfinity;
        private float minBladeCenterX = float.PositiveInfinity;
        private float maxBladeCenterX = float.NegativeInfinity;
        private float minBladeCenterY = float.PositiveInfinity;
        private float maxBladeCenterY = float.NegativeInfinity;
        private float minBladeY = float.PositiveInfinity;
        private float maxBladeY = float.NegativeInfinity;
        private float minTorsoY = float.PositiveInfinity;
        private float maxTorsoY = float.NegativeInfinity;
        private float minHeadNeckY = float.PositiveInfinity;

        public WarScytheGeometryTrace(
            WarScytheAcceptanceDefinition? acceptance = null)
        {
            maximumGripDistance =
                acceptance?.MaximumGripDistance ?? 0.03f;
            minimumLateralSpan =
                acceptance?.MinimumBladeCenterLateralSpan ?? 0.75f;
        }

        public void Record(
            WarScytheGeometrySample sample,
            float actionTime,
            float strikeStart,
            float strikeStop)
        {
            samples++;
            if (actionTime < strikeStart || actionTime > strikeStop) return;

            strikeSamples++;
            maxRightGripDistance = Math.Max(
                maxRightGripDistance,
                sample.RightGripDistance
            );
            maxLeftGripDistance = Math.Max(
                maxLeftGripDistance,
                sample.LeftGripDistance
            );
            minHandSeparation = Math.Min(
                minHandSeparation,
                sample.HandSeparation
            );
            minBladeCenterX = Math.Min(
                minBladeCenterX,
                sample.BladeCenterX
            );
            maxBladeCenterX = Math.Max(
                maxBladeCenterX,
                sample.BladeCenterX
            );
            minBladeCenterY = Math.Min(
                minBladeCenterY,
                sample.BladeCenterY
            );
            maxBladeCenterY = Math.Max(
                maxBladeCenterY,
                sample.BladeCenterY
            );
            minBladeY = Math.Min(minBladeY, sample.BladeMinY);
            maxBladeY = Math.Max(maxBladeY, sample.BladeMaxY);
            minTorsoY = Math.Min(minTorsoY, sample.TorsoMinY);
            maxTorsoY = Math.Max(maxTorsoY, sample.TorsoMaxY);
            minHeadNeckY = Math.Min(
                minHeadNeckY,
                sample.HeadNeckMinY
            );
            if (sample.LeftGripDistance > maximumGripDistance)
            {
                leftGripMissSamples++;
            }
            if (sample.BladeCenterY < sample.TorsoMinY ||
                sample.BladeCenterY > sample.TorsoMaxY)
            {
                bladeCenterOutsideTorsoSamples++;
            }
            if (sample.BladeAboveHeadOrNeck) bladeAboveHeadSamples++;
            if (sample.HeadOrNeckOverlap) headOrNeckOverlapSamples++;
            if (sample.TorsoOverlap) torsoOverlapSamples++;
        }

        public bool ContractPass =>
            strikeSamples > 0 &&
            maxRightGripDistance <= maximumGripDistance &&
            maxLeftGripDistance <= maximumGripDistance &&
            leftGripMissSamples == 0 &&
            maxBladeCenterX - minBladeCenterX >= minimumLateralSpan &&
            bladeCenterOutsideTorsoSamples == 0 &&
            bladeAboveHeadSamples == 0 &&
            headOrNeckOverlapSamples == 0 &&
            torsoOverlapSamples == 0;

        public string BuildStatus(int sequence)
        {
            if (strikeSamples == 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "seq={0},samples={1},strikeSamples=0,geometry=unavailable",
                    sequence,
                    samples
                );
            }

            float lateralSpan = maxBladeCenterX - minBladeCenterX;
            return string.Format(
                CultureInfo.InvariantCulture,
                "seq={0},samples={1},strikeSamples={2},rightGripSurfaceMax={3:0.###},leftGripSurfaceMax={4:0.###},leftGripMissSamples={5},handSeparationMin={6:0.###},bladeCenterXSpan={7:0.###},bladeCenterY={8:0.###}..{9:0.###},bladeCenterOutsideTorsoSamples={10},bladeY={11:0.###}..{12:0.###},torsoY={13:0.###}..{14:0.###},headNeckMinY={15:0.###},bladeAboveHeadSamples={16},headNeckOverlapSamples={17},torsoOverlapSamples={18},contractPass={19}",
                sequence,
                samples,
                strikeSamples,
                maxRightGripDistance,
                maxLeftGripDistance,
                leftGripMissSamples,
                minHandSeparation,
                lateralSpan,
                minBladeCenterY,
                maxBladeCenterY,
                bladeCenterOutsideTorsoSamples,
                minBladeY,
                maxBladeY,
                minTorsoY,
                maxTorsoY,
                minHeadNeckY,
                bladeAboveHeadSamples,
                headOrNeckOverlapSamples,
                torsoOverlapSamples,
                ContractPass
            );
        }
    }
}
