using System.Numerics;

using Vintagestory.API.Common;

namespace Apprentice.AnimationReference
{

public interface IAnimationSoundSink
{
    void Play(SoundFrame frame);
}

public interface IAnimationParticleSink
{
    void Spawn(
        EntityPlayer player,
        string code,
        Vector3 position,
        Vector3 velocity,
        float intensity);
}

}
