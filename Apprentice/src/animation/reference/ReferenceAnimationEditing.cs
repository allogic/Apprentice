using System;

namespace Apprentice.AnimationReference
{

internal static class ReferenceAnimationEditing
{
    public static AnimationElement GetElement(
        Animation animation,
        int frameIndex,
        string elementName)
    {
        if (!Enum.TryParse(
                elementName,
                ignoreCase: false,
                out EnumAnimatedElement element))
        {
            throw new InvalidOperationException(
                $"Unknown reference rig element '{elementName}'."
            );
        }

        if (frameIndex < 0 ||
            frameIndex >= animation.PlayerKeyFrames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        AnimationElement result = GetElement(
            animation.PlayerKeyFrames[frameIndex].Frame,
            element,
            out bool exists
        );
        return exists
            ? result
            : throw new InvalidOperationException(
                $"Frame {frameIndex} does not own '{elementName}'."
            );
    }

    public static float[] GetValues(
        Animation animation,
        int frameIndex,
        string elementName)
    {
        float?[] values = GetElement(
            animation,
            frameIndex,
            elementName
        ).ToArray();
        return Array.ConvertAll(
            values,
            value => value ?? 0f
        );
    }

    public static void SetComponent(
        Animation animation,
        int frameIndex,
        string elementName,
        int component,
        float value)
    {
        if (component < 0 || component >= 6)
        {
            throw new ArgumentOutOfRangeException(nameof(component));
        }

        float?[] values = GetElement(
            animation,
            frameIndex,
            elementName
        ).ToArray();
        values[component] = value;
        SetElement(
            animation,
            frameIndex,
            elementName,
            new AnimationElement(values)
        );
    }

    public static void SetElement(
        Animation animation,
        int frameIndex,
        string elementName,
        AnimationElement value)
    {
        if (!Enum.TryParse(
                elementName,
                ignoreCase: false,
                out EnumAnimatedElement element))
        {
            throw new InvalidOperationException(
                $"Unknown reference rig element '{elementName}'."
            );
        }
        if (frameIndex < 0 ||
            frameIndex >= animation.PlayerKeyFrames.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        PLayerKeyFrame keyFrame =
            animation.PlayerKeyFrames[frameIndex];
        PlayerFrame frame = SetElement(
            keyFrame.Frame,
            element,
            value
        );
        animation.PlayerKeyFrames[frameIndex] = new PLayerKeyFrame(
            frame,
            keyFrame.Time,
            keyFrame.EasingFunction,
            keyFrame.EasingType,
            keyFrame.FrameProgressRange
        );
    }

    public static PLayerKeyFrame CloneFrame(
        PLayerKeyFrame frame) =>
        PLayerKeyFrameJson.FromKeyFrame(frame).ToKeyFrame();

    public static PLayerKeyFrame WithTime(
        PLayerKeyFrame frame,
        TimeSpan time) =>
        new(
            frame.Frame,
            time,
            frame.EasingFunction,
            frame.EasingType,
            frame.FrameProgressRange
        );

    public static bool NearlyEquals(
        AnimationElement first,
        AnimationElement second,
        float epsilon)
    {
        float?[] left = first.ToArray();
        float?[] right = second.ToArray();
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index].HasValue != right[index].HasValue)
            {
                return false;
            }
            if (left[index].HasValue &&
                Math.Abs(left[index]!.Value -
                    right[index]!.Value) > epsilon)
            {
                return false;
            }
        }
        return true;
    }

    private static PlayerFrame SetElement(
        PlayerFrame frame,
        EnumAnimatedElement selectedPart,
        AnimationElement element)
    {
        RightHandFrame? right = frame.RightHand;
        LeftHandFrame? left = frame.LeftHand;

        switch (selectedPart)
        {
            case EnumAnimatedElement.ItemAnchor:
            case EnumAnimatedElement.LowerArmR:
            case EnumAnimatedElement.UpperArmR:
                RightHandFrame r = right ?? RightHandFrame.Zero;
                right = selectedPart switch
                {
                    EnumAnimatedElement.ItemAnchor =>
                        new(element, r.LowerArmR, r.UpperArmR),
                    EnumAnimatedElement.LowerArmR =>
                        new(r.ItemAnchor, element, r.UpperArmR),
                    _ => new(r.ItemAnchor, r.LowerArmR, element)
                };
                break;

            case EnumAnimatedElement.ItemAnchorL:
            case EnumAnimatedElement.LowerArmL:
            case EnumAnimatedElement.UpperArmL:
                LeftHandFrame l = left ?? LeftHandFrame.Zero;
                left = selectedPart switch
                {
                    EnumAnimatedElement.ItemAnchorL =>
                        new(element, l.LowerArmL, l.UpperArmL),
                    EnumAnimatedElement.LowerArmL =>
                        new(l.ItemAnchorL, element, l.UpperArmL),
                    _ => new(l.ItemAnchorL, l.LowerArmL, element)
                };
                break;

            default:
                throw new InvalidOperationException(
                    $"'{selectedPart}' is outside the War Scythe six-part reference frame."
                );
        }

        return new PlayerFrame(
            right,
            left,
            frame.OtherParts,
            frame.UpperTorso,
            frame.DetachedAnchorFrame,
            frame.DetachedAnchor,
            frame.SwitchArms,
            frame.PitchFollow,
            frame.FovMultiplier,
            frame.BobbingAmplitude,
            frame.DetachedAnchorFollow,
            frame.LowerTorso
        );
    }

    private static AnimationElement GetElement(
        PlayerFrame frame,
        EnumAnimatedElement selectedPart,
        out bool exists)
    {
        exists = true;
        switch (selectedPart)
        {
            case EnumAnimatedElement.ItemAnchor
                when frame.RightHand != null:
                return frame.RightHand.Value.ItemAnchor;
            case EnumAnimatedElement.LowerArmR
                when frame.RightHand != null:
                return frame.RightHand.Value.LowerArmR;
            case EnumAnimatedElement.UpperArmR
                when frame.RightHand != null:
                return frame.RightHand.Value.UpperArmR;
            case EnumAnimatedElement.ItemAnchorL
                when frame.LeftHand != null:
                return frame.LeftHand.Value.ItemAnchorL;
            case EnumAnimatedElement.LowerArmL
                when frame.LeftHand != null:
                return frame.LeftHand.Value.LowerArmL;
            case EnumAnimatedElement.UpperArmL
                when frame.LeftHand != null:
                return frame.LeftHand.Value.UpperArmL;
            default:
                exists = false;
                return AnimationElement.Zero;
        }
    }
}

}
