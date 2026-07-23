using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Vintagestory.API.Common;

namespace Apprentice
{
    internal sealed class ApprenticeAnimationDefinition
    {
        private static readonly string[] RequiredWarScytheElements =
        {
            "ItemAnchor",
            "ItemAnchorL",
            "UpperArmR",
            "LowerArmR",
            "UpperArmL",
            "LowerArmL"
        };

        private static readonly string[] RequiredWarScytheCallbacks =
        {
            "attack-start",
            "attack-sample",
            "attack-stop",
            "ready"
        };

        public int Version { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string HeldItemCode { get; set; } = string.Empty;
        public float EaseInSeconds { get; set; }
        public float EaseOutSeconds { get; set; }
        public List<ApprenticeAnimationKeyFrame> KeyFrames { get; set; } = new();
        public List<ApprenticeAnimationCallback> Callbacks { get; set; } = new();

        [JsonIgnore]
        public float DurationSeconds => KeyFrames.Count == 0
            ? 0
            : KeyFrames[^1].TimeSeconds;

        [JsonIgnore]
        public float TotalActionSeconds =>
            EaseInSeconds + DurationSeconds + EaseOutSeconds;

        public ApprenticeAnimationDefinition DeepClone()
        {
            string json = ToJson();
            return ParseWarScythe(
                json,
                new AssetLocation(
                    "apprentice",
                    "config/animations/war-scythe-editor-copy.json"
                )
            );
        }

        public string ToJson() =>
            JsonConvert.SerializeObject(this, Formatting.Indented) +
            Environment.NewLine;

        public static ApprenticeAnimationDefinition LoadWarScythe(
            ICoreAPI api)
        {
            ArgumentNullException.ThrowIfNull(api);

            AssetLocation location = new(
                "apprentice",
                "config/animations/war-scythe.json"
            );
            IAsset asset = api.Assets.Get(location);
            return ParseWarScythe(asset.ToText(), location);
        }

        internal static ApprenticeAnimationDefinition ParseWarScythe(
            string json,
            AssetLocation location)
        {
            ApprenticeAnimationDefinition definition =
                JsonConvert.DeserializeObject<ApprenticeAnimationDefinition>(
                    json
                ) ?? throw new InvalidOperationException(
                    $"Failed to parse {location}."
                );

            definition.ValidateWarScythe(location);
            return definition;
        }

        internal void CollectCallbacks(
            ref int nextCallbackIndex,
            float actionTimeSeconds,
            ICollection<ApprenticeAnimationCallback> destination)
        {
            while (nextCallbackIndex < Callbacks.Count &&
                Callbacks[nextCallbackIndex].TimeSeconds <= actionTimeSeconds)
            {
                destination.Add(Callbacks[nextCallbackIndex++]);
            }
        }

        public bool TrySample(
            string elementName,
            float timeSeconds,
            out ApprenticeElementTransform transform)
        {
            transform = default;
            if (KeyFrames.Count == 0) return false;

            float clamped = Math.Clamp(timeSeconds, 0, DurationSeconds);
            int nextIndex = KeyFrames.FindIndex(
                frame => frame.TimeSeconds >= clamped
            );
            if (nextIndex < 0) nextIndex = KeyFrames.Count - 1;

            ApprenticeAnimationKeyFrame next = KeyFrames[nextIndex];
            if (!next.TryGet(elementName, out ApprenticeElementTransform to))
            {
                return false;
            }

            if (nextIndex == 0 || next.TimeSeconds <= clamped)
            {
                transform = to;
                return true;
            }

            ApprenticeAnimationKeyFrame previous = KeyFrames[nextIndex - 1];
            if (!previous.TryGet(
                elementName,
                out ApprenticeElementTransform from))
            {
                return false;
            }

            float span = next.TimeSeconds - previous.TimeSeconds;
            float progress = span <= 0
                ? 1
                : (clamped - previous.TimeSeconds) / span;
            transform = ApprenticeElementTransform.Interpolate(
                from,
                to,
                progress
            );
            return true;
        }

        private void ValidateWarScythe(AssetLocation location)
        {
            if (Version != 1)
            {
                throw new InvalidOperationException(
                    $"{location} must use animation definition version 1."
                );
            }

            Code = Code.Trim();
            Category = Category.Trim();
            HeldItemCode = HeldItemCode.Trim();
            if (Code != "war-scythe-attack" ||
                Category != "apprentice-mainhand" ||
                HeldItemCode != "apprentice:warscythe")
            {
                throw new InvalidOperationException(
                    $"{location} contains an unexpected code, category, or item owner."
                );
            }

            if (!float.IsFinite(EaseInSeconds) || EaseInSeconds <= 0 ||
                !float.IsFinite(EaseOutSeconds) || EaseOutSeconds <= 0 ||
                KeyFrames.Count < 2)
            {
                throw new InvalidOperationException(
                    $"{location} must define positive transitions and at least two keyframes."
                );
            }

            KeyFrames = KeyFrames
                .OrderBy(frame => frame.TimeSeconds)
                .ToList();
            if (Math.Abs(KeyFrames[0].TimeSeconds) > 0.0001f ||
                DurationSeconds <= 0)
            {
                throw new InvalidOperationException(
                    $"{location} must start at 0 seconds and end after 0 seconds."
                );
            }

            float previousTime = -1;
            foreach (ApprenticeAnimationKeyFrame frame in KeyFrames)
            {
                if (!float.IsFinite(frame.TimeSeconds) ||
                    frame.TimeSeconds <= previousTime)
                {
                    throw new InvalidOperationException(
                        $"{location} keyframe times must be finite and strictly increasing."
                    );
                }
                previousTime = frame.TimeSeconds;
                frame.Validate(location, RequiredWarScytheElements);
            }

            foreach (string element in RequiredWarScytheElements)
            {
                if (!KeyFrames[0].TryGet(
                    element,
                    out ApprenticeElementTransform first) ||
                    !KeyFrames[^1].TryGet(
                        element,
                        out ApprenticeElementTransform last) ||
                    !first.NearlyEquals(last, 0.0001f))
                {
                    throw new InvalidOperationException(
                        $"{location} must finish in the exact '{element}' ready pose."
                    );
                }
            }

            Callbacks = Callbacks
                .OrderBy(callback => callback.TimeSeconds)
                .ToList();
            if (!Callbacks.Select(callback => callback.Code).SequenceEqual(
                RequiredWarScytheCallbacks,
                StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{location} callbacks must be attack-start, attack-sample, attack-stop, ready in that order."
                );
            }

            previousTime = -1;
            foreach (ApprenticeAnimationCallback callback in Callbacks)
            {
                callback.Code = callback.Code.Trim();
                if (!float.IsFinite(callback.TimeSeconds) ||
                    callback.TimeSeconds <= previousTime ||
                    callback.TimeSeconds > DurationSeconds)
                {
                    throw new InvalidOperationException(
                        $"{location} callback times must be ordered and inside the animation duration."
                    );
                }
                previousTime = callback.TimeSeconds;
            }
        }
    }

    internal sealed class ApprenticeAnimationKeyFrame
    {
        public float TimeSeconds { get; set; }
        public Dictionary<string, float[]> Elements { get; set; } =
            new(StringComparer.Ordinal);

        public bool TryGet(
            string elementName,
            out ApprenticeElementTransform transform)
        {
            transform = default;
            if (!Elements.TryGetValue(elementName, out float[]? values) ||
                values.Length != 6)
            {
                return false;
            }

            transform = new ApprenticeElementTransform(values);
            return true;
        }

        public void Validate(
            AssetLocation location,
            IReadOnlyCollection<string> requiredElements)
        {
            if (Elements.Count != requiredElements.Count ||
                !requiredElements.All(Elements.ContainsKey))
            {
                throw new InvalidOperationException(
                    $"{location} frame {TimeSeconds:0.###} must own exactly ItemAnchor, ItemAnchorL, and both arm chains."
                );
            }

            foreach ((string element, float[] values) in Elements)
            {
                if (values == null || values.Length != 6 ||
                    values.Any(value => !float.IsFinite(value)))
                {
                    throw new InvalidOperationException(
                        $"{location} frame {TimeSeconds:0.###} element '{element}' must contain six finite values."
                    );
                }
            }
        }
    }

    internal sealed class ApprenticeAnimationCallback
    {
        public string Code { get; set; } = string.Empty;
        public float TimeSeconds { get; set; }
    }

    internal readonly struct ApprenticeElementTransform
    {
        public ApprenticeElementTransform(float[] values)
        {
            OffsetX = values[0];
            OffsetY = values[1];
            OffsetZ = values[2];
            RotationX = values[3];
            RotationY = values[4];
            RotationZ = values[5];
        }

        private ApprenticeElementTransform(
            float offsetX,
            float offsetY,
            float offsetZ,
            float rotationX,
            float rotationY,
            float rotationZ)
        {
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
        }

        public float OffsetX { get; }
        public float OffsetY { get; }
        public float OffsetZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }

        public static ApprenticeElementTransform Interpolate(
            ApprenticeElementTransform from,
            ApprenticeElementTransform to,
            float progress)
        {
            float t = Math.Clamp(progress, 0, 1);
            return new ApprenticeElementTransform(
                Lerp(from.OffsetX, to.OffsetX, t),
                Lerp(from.OffsetY, to.OffsetY, t),
                Lerp(from.OffsetZ, to.OffsetZ, t),
                LerpAngle(from.RotationX, to.RotationX, t),
                LerpAngle(from.RotationY, to.RotationY, t),
                LerpAngle(from.RotationZ, to.RotationZ, t)
            );
        }

        public bool NearlyEquals(
            ApprenticeElementTransform other,
            float epsilon) =>
            Math.Abs(OffsetX - other.OffsetX) <= epsilon &&
            Math.Abs(OffsetY - other.OffsetY) <= epsilon &&
            Math.Abs(OffsetZ - other.OffsetZ) <= epsilon &&
            Math.Abs(NormalizeDegrees(RotationX - other.RotationX)) <= epsilon &&
            Math.Abs(NormalizeDegrees(RotationY - other.RotationY)) <= epsilon &&
            Math.Abs(NormalizeDegrees(RotationZ - other.RotationZ)) <= epsilon;

        public ApprenticeElementTransform WithRotations(
            float rotationX,
            float rotationY,
            float rotationZ) =>
            new(
                OffsetX,
                OffsetY,
                OffsetZ,
                rotationX,
                rotationY,
                rotationZ
            );

        private static float Lerp(float from, float to, float progress) =>
            from + (to - from) * progress;

        private static float LerpAngle(
            float from,
            float to,
            float progress) =>
            from + NormalizeDegrees(to - from) * progress;

        internal static float NormalizeDegrees(float degrees)
        {
            float normalized = (degrees + 180f) % 360f;
            if (normalized < 0) normalized += 360f;
            return normalized - 180f;
        }
    }
}
