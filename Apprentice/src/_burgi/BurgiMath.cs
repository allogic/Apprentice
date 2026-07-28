using System;

using Vintagestory.API.MathTools;

namespace Apprentice.src._burgi
{
	internal class BurgiMath
	{
		public static readonly Vec3d WorldRight = new(1, 0, 0);
		public static readonly Vec3d WorldUp = new(0, 1, 0);
		public static readonly Vec3d WorldForward = new(0, 0, 1);
		public static readonly Vec3d WorldLeft = new(-1, 0, 0);
		public static readonly Vec3d WorldDown = new(0, -1, 0);
		public static readonly Vec3d WorldBack = new(0, 0, -1);

		public static readonly float RAD_TO_DEG = 57.29577951308232286465F;
		public static readonly float DEG_TO_RAD = 0.017453292519943295470F;

		public static Vec3d RotateAroundAxis(Vec3d v, Vec3d axis, double angle)
		{
			axis = axis.Clone().Normalize();

			double cos = Math.Cos(angle);
			double sin = Math.Sin(angle);

			Vec3d term1 = v * cos;
			Vec3d term2 = axis.Cross(v) * sin;
			Vec3d term3 = axis * (axis.Dot(v) * (1.0 - cos));

			return term1 + term2 + term3;
		}
		public static double AngleDifference(double target, double current)
		{
			double diff = target - current;

			while (diff > Math.PI)
				diff -= Math.Tau;

			while (diff < -Math.PI)
				diff += Math.Tau;

			return diff;
		}

		// Pirate's Life https://easings.net/
		public static float EaseInCirc(float x)
		{
			return 1.0F - (float)Math.Sqrt(1.0F - (float)Math.Pow(x, 2.0F));
		}
		public static float EaseInOutElastic(float x)
		{
			float c5 = (2.0F * (float)Math.PI) / 4.5F;
			return x == 0.0F
				? 0.0F
				: x == 1.0F
				? 1.0F
				: x < 0.5F
				? -((float)Math.Pow(2.0F, 20.0F * x - 10.0F) * (float)Math.Sin((20.0F * x - 11.125F) * c5)) / 2.0F
				: ((float)Math.Pow(2.0F, -20.0F * x + 10.0F) * (float)Math.Sin((20.0F * x - 11.125F) * c5)) / 2.0F + 1.0F;
		}
		public static float EaseOutCirc(float x)
		{
			return (float)Math.Sqrt(1.0F - (float)Math.Pow(x - 1.0F, 2.0F));
		}
		public static float EaseOutElastic(float x)
		{
			float c4 = (2.0F * (float)Math.PI) / 3.0F;
			return x == 0.0F
				? 0.0F
				: x == 1.0F
				? 1.0F
				: (float)Math.Pow(2.0F, -10.0F * x) * (float)Math.Sin((x * 10.0F - 0.75F) * c4) + 1.0F;
		}
	}
}
