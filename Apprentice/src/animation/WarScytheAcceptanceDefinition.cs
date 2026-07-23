using System;
using System.Linq;

using Newtonsoft.Json;

using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    internal sealed class WarScytheAcceptanceDefinition
    {
        public int Version { get; set; }
        public string HeldItemCode { get; set; } = string.Empty;
        public float[] ModelOrigin { get; set; } = Array.Empty<float>();
        public float[] ShaftAxis { get; set; } = Array.Empty<float>();
        public float[] RightGripMin { get; set; } = Array.Empty<float>();
        public float[] RightGripMax { get; set; } = Array.Empty<float>();
        public float[] LeftGripMin { get; set; } = Array.Empty<float>();
        public float[] LeftGripMax { get; set; } = Array.Empty<float>();
        public string[] BladeElementPrefixes { get; set; } =
            Array.Empty<string>();
        public float MaximumGripDistance { get; set; }
        public float MinimumBladeCenterLateralSpan { get; set; }

        public static WarScytheAcceptanceDefinition Load(ICoreAPI api)
        {
            AssetLocation location = new(
                "apprentice",
                "config/animations/war-scythe-acceptance.json"
            );
            WarScytheAcceptanceDefinition definition =
                JsonConvert.DeserializeObject<WarScytheAcceptanceDefinition>(
                    api.Assets.Get(location).ToText()
                ) ?? throw new InvalidOperationException(
                    $"Failed to parse {location}."
                );
            definition.Validate(location);
            return definition;
        }

        public Vec3f RightGripMinimum => ToModelVector(RightGripMin);
        public Vec3f RightGripMaximum => ToModelVector(RightGripMax);
        public Vec3f LeftGripMinimum => ToModelVector(LeftGripMin);
        public Vec3f LeftGripMaximum => ToModelVector(LeftGripMax);

        private void Validate(AssetLocation location)
        {
            HeldItemCode = HeldItemCode.Trim();
            BladeElementPrefixes = BladeElementPrefixes
                .Select(prefix => prefix.Trim())
                .Where(prefix => prefix.Length != 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (Version != 1 ||
                HeldItemCode != "apprentice:warscythe" ||
                !IsFiniteVector(ModelOrigin) ||
                !IsFiniteVector(ShaftAxis) ||
                !IsFiniteVector(RightGripMin) ||
                !IsFiniteVector(RightGripMax) ||
                !IsFiniteVector(LeftGripMin) ||
                !IsFiniteVector(LeftGripMax) ||
                BladeElementPrefixes.Length == 0 ||
                !float.IsFinite(MaximumGripDistance) ||
                MaximumGripDistance <= 0 ||
                !float.IsFinite(MinimumBladeCenterLateralSpan) ||
                MinimumBladeCenterLateralSpan <= 0)
            {
                throw new InvalidOperationException(
                    $"{location} contains an invalid War Scythe model or acceptance contract."
                );
            }

            for (int axis = 0; axis < 3; axis++)
            {
                if (RightGripMin[axis] >= RightGripMax[axis] ||
                    LeftGripMin[axis] >= LeftGripMax[axis])
                {
                    throw new InvalidOperationException(
                        $"{location} grip volumes must have positive size."
                    );
                }
            }

            float shaftLength = MathF.Sqrt(
                ShaftAxis[0] * ShaftAxis[0] +
                ShaftAxis[1] * ShaftAxis[1] +
                ShaftAxis[2] * ShaftAxis[2]
            );
            if (Math.Abs(shaftLength - 1f) > 0.001f)
            {
                throw new InvalidOperationException(
                    $"{location} shaftAxis must be normalized."
                );
            }
        }

        private static bool IsFiniteVector(float[] values) =>
            values.Length == 3 && values.All(float.IsFinite);

        private static Vec3f ToModelVector(float[] values) =>
            new(values[0] / 16f, values[1] / 16f, values[2] / 16f);
    }
}
