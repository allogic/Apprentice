using System;
using System.Collections.Generic;
using System.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    internal sealed class WarScytheOffhandIkSolver
    {
        private const float GripCenterX = 8f / 16f;
        private const float GripCenterY = 9.1f / 16f;
        private const float GripCenterZ = 8f / 16f;
        private const float DifferenceStepDegrees = 0.75f;
        private const float Damping = 0.00008f;
        private const float MaximumIterationStepDegrees = 18f;
        private const float MaximumAuthoredCorrectionDegrees = 120f;
        private const int MaximumIterations = 10;

        public bool TrySolve(
            ClientAnimator animator,
            ElementPose triggerPose,
            ItemStack stack,
            ApprenticeAnimationDefinition definition,
            float actionTime,
            out WarScytheOffhandIkSolution solution,
            out string outcome)
        {
            solution = default;
            outcome = "definition-sample-missing";
            if (!definition.TrySample(
                    "UpperArmL",
                    actionTime,
                    out ApprenticeElementTransform upperAuthored) ||
                !definition.TrySample(
                    "LowerArmL",
                    actionTime,
                    out ApprenticeElementTransform lowerAuthored))
            {
                return false;
            }

            AttachmentPointAndPose? right = animator
                .GetAttachmentPointPose("RightHand");
            AttachmentPointAndPose? left = animator
                .GetAttachmentPointPose("LeftHand");
            ModelTransform? configuredTransform = stack.Item.TpHandTransform;
            if (right == null || right.AttachPoint == null)
            {
                outcome = "right-attachment-unavailable";
                return false;
            }

            if (left == null || left.AttachPoint == null)
            {
                outcome = "left-attachment-unavailable";
                return false;
            }

            if (configuredTransform == null)
            {
                outcome = "tp-hand-transform-unavailable";
                return false;
            }

            if (animator.RootPoses == null)
            {
                outcome = "entity-root-poses-unavailable";
                return false;
            }

            // The attachment's cached pose is shared across all entities using
            // the shape and is explicitly internal-only in the VS API.
            // Resolve the attachment owner inside this animator's RootPoses so
            // every pose in the IK chain belongs to the rendered entity.
            if (!TryFindAttachmentOwnerPath(
                    animator.RootPoses,
                    right.AttachPoint,
                    out List<ElementPose> rightRootPath))
            {
                outcome = "right-attachment-owner-path-missing";
                return false;
            }

            if (!TryFindAttachmentOwnerPath(
                    animator.RootPoses,
                    left.AttachPoint,
                    out List<ElementPose> rootPath))
            {
                outcome = "left-attachment-owner-path-missing";
                return false;
            }

            int upperIndex = rootPath.FindIndex(
                pose => string.Equals(
                    pose.ForElement?.Name,
                    "UpperArmL",
                    StringComparison.OrdinalIgnoreCase
                )
            );
            int lowerIndex = rootPath.FindIndex(
                pose => string.Equals(
                    pose.ForElement?.Name,
                    "LowerArmL",
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (upperIndex < 0)
            {
                outcome = "upper-arm-not-on-attachment-path";
                return false;
            }
            if (lowerIndex <= upperIndex)
            {
                outcome = "lower-arm-not-after-upper-arm";
                return false;
            }

            ElementPose upper = rootPath[upperIndex];
            ElementPose lower = rootPath[lowerIndex];
            if (!ReferenceEquals(triggerPose, upper))
            {
                outcome = "render-trigger-not-upper-arm";
                return false;
            }
            if (upper.ForElement == null || lower.ForElement == null)
            {
                outcome = "arm-shape-element-unavailable";
                return false;
            }

            float[] parentMatrix = upperIndex == 0
                ? Mat4f.Create()
                : rootPath[upperIndex - 1].AnimModelMatrix;
            if (parentMatrix == null || parentMatrix.Length < 16)
            {
                outcome = "upper-arm-parent-matrix-unavailable";
                return false;
            }
            float[] rightOwnerMatrix =
                rightRootPath[rightRootPath.Count - 1].AnimModelMatrix;
            if (rightOwnerMatrix == null || rightOwnerMatrix.Length < 16)
            {
                outcome = "right-attachment-owner-matrix-unavailable";
                return false;
            }

            List<ElementPose> armPath = rootPath
                .Skip(upperIndex)
                .ToList();
            ModelTransform transform = configuredTransform
                .Clone()
                .EnsureDefaultValues();
            Vec3f target = GetGripTarget(
                right.AttachPoint,
                rightOwnerMatrix,
                transform
            );

            float[] authored =
            {
                upperAuthored.RotationX,
                upperAuthored.RotationY,
                upperAuthored.RotationZ,
                lowerAuthored.RotationX,
                lowerAuthored.RotationY,
                lowerAuthored.RotationZ
            };
            float[] angles = (float[])authored.Clone();
            Vec3f endpoint = EvaluateEndpoint(
                armPath,
                parentMatrix,
                left.AttachPoint,
                upper,
                lower,
                upperAuthored,
                lowerAuthored,
                angles
            );
            float initialDistance = Distance(endpoint, target);
            if (!float.IsFinite(initialDistance))
            {
                outcome = "non-finite-initial-distance";
                return false;
            }
            float previousDistance = initialDistance;
            string noImprovementReason = "iteration-limit";

            for (int iteration = 0;
                iteration < MaximumIterations && previousDistance > 0.006f;
                iteration++)
            {
                float[,] jacobian = new float[3, 6];
                for (int column = 0; column < 6; column++)
                {
                    float original = angles[column];
                    angles[column] = NormalizeDegrees(
                        original + DifferenceStepDegrees
                    );
                    Vec3f changed = EvaluateEndpoint(
                        armPath,
                        parentMatrix,
                        left.AttachPoint,
                        upper,
                        lower,
                        upperAuthored,
                        lowerAuthored,
                        angles
                    );
                    angles[column] = original;
                    jacobian[0, column] =
                        (changed.X - endpoint.X) / DifferenceStepDegrees;
                    jacobian[1, column] =
                        (changed.Y - endpoint.Y) / DifferenceStepDegrees;
                    jacobian[2, column] =
                        (changed.Z - endpoint.Z) / DifferenceStepDegrees;
                }

                Vec3f error = new(
                    target.X - endpoint.X,
                    target.Y - endpoint.Y,
                    target.Z - endpoint.Z
                );
                if (!TrySolveDampedLeastSquares(
                        jacobian,
                        error,
                        out float[] delta))
                {
                    noImprovementReason = "linear-solve-rejected";
                    break;
                }

                bool accepted = false;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    float stepScale = 1f / (1 << attempt);
                    float[] candidate = (float[])angles.Clone();
                    for (int index = 0; index < candidate.Length; index++)
                    {
                        float step = Math.Clamp(
                            delta[index] * stepScale,
                            -MaximumIterationStepDegrees,
                            MaximumIterationStepDegrees
                        );
                        float proposed = NormalizeDegrees(
                            candidate[index] + step
                        );
                        float authoredDelta = Math.Clamp(
                            NormalizeDegrees(proposed - authored[index]),
                            -MaximumAuthoredCorrectionDegrees,
                            MaximumAuthoredCorrectionDegrees
                        );
                        candidate[index] = NormalizeDegrees(
                            authored[index] + authoredDelta
                        );
                    }

                    Vec3f candidateEndpoint = EvaluateEndpoint(
                        armPath,
                        parentMatrix,
                        left.AttachPoint,
                        upper,
                        lower,
                        upperAuthored,
                        lowerAuthored,
                        candidate
                    );
                    float candidateDistance = Distance(
                        candidateEndpoint,
                        target
                    );
                    if (candidateDistance >= previousDistance - 0.00001f)
                    {
                        continue;
                    }

                    angles = candidate;
                    endpoint = candidateEndpoint;
                    previousDistance = candidateDistance;
                    accepted = true;
                    break;
                }

                if (!accepted)
                {
                    noImprovementReason = "line-search-rejected";
                    break;
                }

                noImprovementReason = "iteration-limit";
            }

            float maximumCorrection = 0;
            for (int index = 0; index < angles.Length; index++)
            {
                maximumCorrection = Math.Max(
                    maximumCorrection,
                    Math.Abs(NormalizeDegrees(
                        angles[index] - authored[index]
                    ))
                );
            }

            bool improved = previousDistance < initialDistance - 0.0001f;
            solution = new WarScytheOffhandIkSolution(
                upperAuthored.WithRotations(
                    angles[0],
                    angles[1],
                    angles[2]
                ),
                lowerAuthored.WithRotations(
                    angles[3],
                    angles[4],
                    angles[5]
                ),
                initialDistance,
                previousDistance,
                maximumCorrection,
                previousDistance <= 0.03f
            );
            bool applied = improved || previousDistance <= 0.03f;
            outcome = applied
                ? (previousDistance <= 0.03f
                    ? "applied-within-tolerance"
                    : "applied-improved")
                : "no-improvement-" + noImprovementReason;
            return applied;
        }

        private static Vec3f EvaluateEndpoint(
            IReadOnlyList<ElementPose> armPath,
            float[] parentMatrix,
            AttachmentPoint attachPoint,
            ElementPose upper,
            ElementPose lower,
            ApprenticeElementTransform upperAuthored,
            ApprenticeElementTransform lowerAuthored,
            IReadOnlyList<float> angles)
        {
            Matrixf matrix = new Matrixf().Set(parentMatrix);
            float[] local = Mat4f.Create();
            foreach (ElementPose pose in armPath)
            {
                ElementPose evaluated = pose;
                if (ReferenceEquals(pose, upper))
                {
                    evaluated = CreatePose(
                        pose,
                        upperAuthored.WithRotations(
                            angles[0],
                            angles[1],
                            angles[2]
                        )
                    );
                }
                else if (ReferenceEquals(pose, lower))
                {
                    evaluated = CreatePose(
                        pose,
                        lowerAuthored.WithRotations(
                            angles[3],
                            angles[4],
                            angles[5]
                        )
                    );
                }

                Mat4f.Identity(local);
                pose.ForElement.GetLocalTransformMatrix(
                    0,
                    local,
                    evaluated
                );
                matrix.Mul(local);
            }

            matrix.Translate(
                attachPoint.PosX / 16.0,
                attachPoint.PosY / 16.0,
                attachPoint.PosZ / 16.0
            );
            return Transform(matrix, new Vec4f(0, 0, 0, 1));
        }

        private static ElementPose CreatePose(
            ElementPose source,
            ApprenticeElementTransform transform) =>
            new()
            {
                ForElement = source.ForElement,
                AnimModelMatrix = source.AnimModelMatrix,
                ChildElementPoses = source.ChildElementPoses,
                degOffX = source.degOffX,
                degOffY = source.degOffY,
                degOffZ = source.degOffZ,
                degX = transform.RotationX,
                degY = transform.RotationY,
                degZ = transform.RotationZ,
                scaleX = source.scaleX,
                scaleY = source.scaleY,
                scaleZ = source.scaleZ,
                translateX = transform.OffsetX / 16f,
                translateY = transform.OffsetY / 16f,
                translateZ = transform.OffsetZ / 16f,
                RotShortestDistanceX = source.RotShortestDistanceX,
                RotShortestDistanceY = source.RotShortestDistanceY,
                RotShortestDistanceZ = source.RotShortestDistanceZ
            };

        private static Vec3f GetGripTarget(
            AttachmentPoint point,
            float[] ownerMatrix,
            ModelTransform transform)
        {
            Matrixf matrix = new Matrixf()
                .Set(ownerMatrix)
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
                .RotateX((float)(point.RotationX +
                    transform.Rotation.X) * GameMath.DEG2RAD)
                .RotateY((float)(point.RotationY +
                    transform.Rotation.Y) * GameMath.DEG2RAD)
                .RotateZ((float)(point.RotationZ +
                    transform.Rotation.Z) * GameMath.DEG2RAD)
                .Translate(
                    -transform.Origin.X,
                    -transform.Origin.Y,
                    -transform.Origin.Z
                );
            return Transform(
                matrix,
                new Vec4f(
                    GripCenterX,
                    GripCenterY,
                    GripCenterZ,
                    1
                )
            );
        }

        private static bool TryFindAttachmentOwnerPath(
            IEnumerable<ElementPose> roots,
            AttachmentPoint target,
            out List<ElementPose> path)
        {
            path = new List<ElementPose>();
            foreach (ElementPose root in roots)
            {
                if (TryFindAttachmentOwnerPath(root, target, path))
                {
                    return true;
                }
            }

            path.Clear();
            return false;
        }

        private static bool TryFindAttachmentOwnerPath(
            ElementPose current,
            AttachmentPoint target,
            ICollection<ElementPose> path)
        {
            path.Add(current);
            if (OwnsAttachment(current, target)) return true;

            if (current.ChildElementPoses == null)
            {
                path.Remove(current);
                return false;
            }

            foreach (ElementPose child in current.ChildElementPoses)
            {
                if (TryFindAttachmentOwnerPath(child, target, path))
                {
                    return true;
                }
            }

            path.Remove(current);
            return false;
        }

        private static bool OwnsAttachment(
            ElementPose pose,
            AttachmentPoint target)
        {
            ShapeElement? element = pose.ForElement;
            if (element == null) return false;

            if (target.ParentElement != null &&
                ReferenceEquals(element, target.ParentElement))
            {
                return true;
            }

            AttachmentPoint[]? points = element.AttachmentPoints;
            if (points == null || string.IsNullOrWhiteSpace(target.Code))
            {
                return false;
            }

            return points.Any(point =>
                ReferenceEquals(point, target) ||
                string.Equals(
                    point.Code,
                    target.Code,
                    StringComparison.OrdinalIgnoreCase
                ));
        }

        private static bool TrySolveDampedLeastSquares(
            float[,] jacobian,
            Vec3f error,
            out float[] delta)
        {
            float[,] matrix = new float[3, 3];
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                float value = row == column ? Damping : 0;
                for (int joint = 0; joint < 6; joint++)
                {
                    value += jacobian[row, joint] *
                        jacobian[column, joint];
                }
                matrix[row, column] = value;
            }

            if (!TrySolveThreeByThree(
                    matrix,
                    new[] { error.X, error.Y, error.Z },
                    out float[] projected))
            {
                delta = Array.Empty<float>();
                return false;
            }

            delta = new float[6];
            for (int joint = 0; joint < delta.Length; joint++)
            {
                delta[joint] = jacobian[0, joint] * projected[0] +
                    jacobian[1, joint] * projected[1] +
                    jacobian[2, joint] * projected[2];
            }
            return delta.All(float.IsFinite);
        }

        private static bool TrySolveThreeByThree(
            float[,] matrix,
            IReadOnlyList<float> vector,
            out float[] result)
        {
            const double RelativePivotTolerance = 1e-10;
            double[,] augmented = new double[3, 4];
            double maximumCoefficient = 0;
            for (int row = 0; row < 3; row++)
            {
                if (!float.IsFinite(vector[row]))
                {
                    result = Array.Empty<float>();
                    return false;
                }

                for (int column = 0; column < 3; column++)
                {
                    float value = matrix[row, column];
                    if (!float.IsFinite(value))
                    {
                        result = Array.Empty<float>();
                        return false;
                    }

                    augmented[row, column] = value;
                    maximumCoefficient = Math.Max(
                        maximumCoefficient,
                        Math.Abs(value)
                    );
                }

                augmented[row, 3] = vector[row];
            }

            if (maximumCoefficient <= 0)
            {
                result = Array.Empty<float>();
                return false;
            }

            double pivotTolerance = Math.Max(
                1e-15,
                maximumCoefficient * RelativePivotTolerance
            );
            for (int column = 0; column < 3; column++)
            {
                int pivotRow = column;
                double pivotMagnitude = Math.Abs(
                    augmented[pivotRow, column]
                );
                for (int row = column + 1; row < 3; row++)
                {
                    double candidate = Math.Abs(
                        augmented[row, column]
                    );
                    if (candidate > pivotMagnitude)
                    {
                        pivotRow = row;
                        pivotMagnitude = candidate;
                    }
                }

                if (!double.IsFinite(pivotMagnitude) ||
                    pivotMagnitude <= pivotTolerance)
                {
                    result = Array.Empty<float>();
                    return false;
                }

                if (pivotRow != column)
                {
                    for (int index = column; index < 4; index++)
                    {
                        (augmented[column, index],
                            augmented[pivotRow, index]) =
                            (augmented[pivotRow, index],
                                augmented[column, index]);
                    }
                }

                double pivot = augmented[column, column];
                for (int row = column + 1; row < 3; row++)
                {
                    double factor = augmented[row, column] / pivot;
                    augmented[row, column] = 0;
                    for (int index = column + 1; index < 4; index++)
                    {
                        augmented[row, index] -=
                            factor * augmented[column, index];
                    }
                }
            }

            double[] solved = new double[3];
            for (int row = 2; row >= 0; row--)
            {
                double value = augmented[row, 3];
                for (int column = row + 1; column < 3; column++)
                {
                    value -= augmented[row, column] * solved[column];
                }

                double pivot = augmented[row, row];
                if (!double.IsFinite(pivot) ||
                    Math.Abs(pivot) <= pivotTolerance)
                {
                    result = Array.Empty<float>();
                    return false;
                }

                solved[row] = value / pivot;
            }

            result = solved.Select(value => (float)value).ToArray();
            return result.All(float.IsFinite);
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

        private static float NormalizeDegrees(float degrees) =>
            ApprenticeElementTransform.NormalizeDegrees(degrees);
    }

    internal readonly struct WarScytheOffhandIkSolution
    {
        public WarScytheOffhandIkSolution(
            ApprenticeElementTransform upper,
            ApprenticeElementTransform lower,
            float initialDistance,
            float finalDistance,
            float maximumCorrectionDegrees,
            bool withinTolerance)
        {
            Upper = upper;
            Lower = lower;
            InitialDistance = initialDistance;
            FinalDistance = finalDistance;
            MaximumCorrectionDegrees = maximumCorrectionDegrees;
            WithinTolerance = withinTolerance;
        }

        public ApprenticeElementTransform Upper { get; }
        public ApprenticeElementTransform Lower { get; }
        public float InitialDistance { get; }
        public float FinalDistance { get; }
        public float MaximumCorrectionDegrees { get; }
        public bool WithinTolerance { get; }

        public bool TryGet(
            string elementName,
            out ApprenticeElementTransform transform)
        {
            switch (elementName)
            {
                case "UpperArmL":
                    transform = Upper;
                    return true;
                case "LowerArmL":
                    transform = Lower;
                    return true;
                default:
                    transform = default;
                    return false;
            }
        }
    }
}
