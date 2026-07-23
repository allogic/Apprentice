using System;
using System.Collections.Generic;
using System.Linq;

using Apprentice.AnimationReference;
using Animation =
    Apprentice.AnimationReference.Animation;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Vintagestory.API.Common;

namespace Apprentice
{
    internal sealed class ApprenticeAnimationDefinition
    {
        public const string AnimationCode =
            "apprentice:war-scythe-attack";
        public const string CategoryCode =
            "apprentice-mainhand";
        public const string ItemCode =
            "apprentice:warscythe";
        public const float EaseInSecondsValue = 0.12f;
        public const float EaseOutSecondsValue = 0.18f;

        private static readonly string[] RequiredElements =
        {
            "ItemAnchor",
            "ItemAnchorL",
            "UpperArmR",
            "LowerArmR",
            "UpperArmL",
            "LowerArmL"
        };

        private static readonly string[] RequiredCallbacks =
        {
            "attack-start",
            "attack-sample",
            "attack-stop",
            "ready"
        };

        private ApprenticeAnimationDefinition(
            Animation animation,
            IReadOnlyList<ApprenticeAnimationCallback> callbacks)
        {
            Animation = animation;
            Callbacks = callbacks;
        }

        public Animation Animation { get; private set; }
        public string Code => AnimationCode;
        public string Category => CategoryCode;
        public string HeldItemCode => ItemCode;
        public float EaseInSeconds => EaseInSecondsValue;
        public float EaseOutSeconds => EaseOutSecondsValue;
        public float DurationSeconds =>
            (float)Animation.TotalDuration.TotalSeconds;
        public float TotalActionSeconds =>
            EaseInSeconds + DurationSeconds + EaseOutSeconds;
        public IReadOnlyList<ApprenticeAnimationCallback> Callbacks
        {
            get;
        }

        public ApprenticeAnimationDefinition DeepClone() =>
            new(
                Animation.Clone(),
                Callbacks.Select(callback => callback.Clone()).ToArray()
            );

        public string ToJson()
        {
            JObject root = new()
            {
                [AnimationCode] = JToken.FromObject(
                    AnimationJson.FromAnimation(Animation),
                    JsonSerializer.CreateDefault()
                )
            };
            return root.ToString(Formatting.Indented) + Environment.NewLine;
        }

        public static ApprenticeAnimationDefinition LoadWarScythe(
            ICoreAPI api)
        {
            if (api == null)
            {
                throw new ArgumentNullException(nameof(api));
            }
            AssetLocation location = new(
                "apprentice",
                "config/animations/war-scythe.json"
            );
            IAsset asset = api.Assets.Get(location);
            return ParseWarScythe(asset.ToText(), location);
        }

        internal static ApprenticeAnimationDefinition ParseWarScythe(
            string json,
            AssetLocation location) =>
            Parse(json, location, requireReadyPoseLoop: true);

        internal static ApprenticeAnimationDefinition ParseWarScytheDraft(
            string json,
            AssetLocation location) =>
            Parse(json, location, requireReadyPoseLoop: false);

        internal void ReplaceAnimation(Animation animation)
        {
            if (animation == null)
            {
                throw new ArgumentNullException(nameof(animation));
            }
            Animation = animation;
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

        internal bool HasExactReadyPoseLoop(
            out string differingElement)
        {
            int last = Animation.PlayerKeyFrames.Count - 1;
            foreach (string elementName in RequiredElements)
            {
                AnimationElement first =
                    ReferenceAnimationEditing.GetElement(
                        Animation,
                        0,
                        elementName
                    );
                AnimationElement final =
                    ReferenceAnimationEditing.GetElement(
                        Animation,
                        last,
                        elementName
                    );
                if (!ReferenceAnimationEditing.NearlyEquals(
                    first,
                    final,
                    0.0001f))
                {
                    differingElement = elementName;
                    return false;
                }
            }

            differingElement = string.Empty;
            return true;
        }

        private static ApprenticeAnimationDefinition Parse(
            string json,
            AssetLocation location,
            bool requireReadyPoseLoop)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to parse {location}.",
                    exception
                );
            }

            if (root.Properties().Count() != 1 ||
                root.Property(AnimationCode, StringComparison.Ordinal) is not
                    JProperty animationProperty)
            {
                throw new InvalidOperationException(
                    $"{location} must contain exactly '{AnimationCode}' in the proven OverhaulLib animation format."
                );
            }

            AnimationJson animationJson =
                animationProperty.Value.ToObject<AnimationJson>() ??
                throw new InvalidOperationException(
                    $"{location} does not contain a valid animation."
                );
            ValidateJson(animationJson, location);

            Animation animation;
            try
            {
                animation = animationJson.ToAnimation();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"{location} could not be converted by the reference animation parser.",
                    exception
                );
            }

            ApprenticeAnimationCallback[] callbacks =
                animation.CallbackFrames
                    .Select(frame => new ApprenticeAnimationCallback(
                        frame.Code,
                        frame.DurationFraction *
                            (float)animation.TotalDuration.TotalSeconds
                    ))
                    .ToArray();
            ApprenticeAnimationDefinition result =
                new(animation, callbacks);

            if (requireReadyPoseLoop &&
                !result.HasExactReadyPoseLoop(
                    out string differingElement))
            {
                throw new InvalidOperationException(
                    $"{location} must finish in the exact '{differingElement}' ready pose."
                );
            }

            return result;
        }

        private static void ValidateJson(
            AnimationJson animation,
            AssetLocation location)
        {
            if (animation.Hold ||
                animation.PlayerKeyFrames.Length < 2 ||
                animation.ItemKeyFrames.Length != 0 ||
                animation.SoundFrames.Length != 0 ||
                animation.ParticlesFrames.Length != 0)
            {
                throw new InvalidOperationException(
                    $"{location} must be a non-holding player animation with at least two frames and no unrelated item, sound, or particle tracks."
                );
            }

            float previousTime = -1;
            foreach (PLayerKeyFrameJson frame in
                animation.PlayerKeyFrames)
            {
                if (!float.IsFinite(frame.EasingTime) ||
                    frame.EasingTime <= previousTime ||
                    frame.Elements.Count != RequiredElements.Length ||
                    !RequiredElements.All(frame.Elements.ContainsKey))
                {
                    throw new InvalidOperationException(
                        $"{location} frames must have strictly increasing times and own exactly ItemAnchor, ItemAnchorL, and both arm chains."
                    );
                }
                if (frame.DetachedAnchor || frame.SwitchArms ||
                    frame.PitchFollow ||
                    !frame.PitchDontFollow ||
                    Math.Abs(frame.FOVMultiplier - 1f) > 0.0001f ||
                    Math.Abs(frame.BobbingAmplitude - 1f) > 0.0001f)
                {
                    throw new InvalidOperationException(
                        $"{location} War Scythe frames must keep normal hands, disable pitch following, and preserve FOV/bobbing."
                    );
                }

                foreach ((string element, float?[] values) in
                    frame.Elements)
                {
                    if (values == null || values.Length != 6 ||
                        values.Any(value =>
                            value.HasValue &&
                            !float.IsFinite(value.Value)))
                    {
                        throw new InvalidOperationException(
                            $"{location} element '{element}' must contain six finite or omitted transform components."
                        );
                    }
                }
                previousTime = frame.EasingTime;
            }

            if (Math.Abs(animation.PlayerKeyFrames[0].EasingTime) >
                    0.0001f ||
                animation.PlayerKeyFrames[^1].EasingTime <= 0)
            {
                throw new InvalidOperationException(
                    $"{location} must start at 0 ms and finish after 0 ms."
                );
            }

            string[] callbackCodes = animation.CallbackFrames
                .Select(frame => frame.Code)
                .ToArray();
            if (!callbackCodes.SequenceEqual(
                RequiredCallbacks,
                StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{location} callbacks must be attack-start, attack-sample, attack-stop, ready in that order."
                );
            }

            float previousFraction = -1;
            foreach (CallbackFrameJson callback in
                animation.CallbackFrames)
            {
                if (!float.IsFinite(callback.DurationFraction) ||
                    callback.DurationFraction <= previousFraction ||
                    callback.DurationFraction < 0 ||
                    callback.DurationFraction > 1)
                {
                    throw new InvalidOperationException(
                        $"{location} callback fractions must be finite, ordered, and inside the animation."
                    );
                }
                previousFraction = callback.DurationFraction;
            }
        }
    }

    internal sealed class ApprenticeAnimationCallback
    {
        public ApprenticeAnimationCallback(
            string code,
            float timeSeconds)
        {
            Code = code;
            TimeSeconds = timeSeconds;
        }

        public string Code { get; }
        public float TimeSeconds { get; }

        public ApprenticeAnimationCallback Clone() =>
            new(Code, TimeSeconds);
    }
}
