using System;

namespace Apprentice
{
	/// <summary>
	/// Stable Apprentice experience curve.
	///
	/// XP cost for advancing from the current level:
	///
	/// Level 1  -> 2:  10 XP
	/// Level 2  -> 3:  20 XP
	/// ...
	/// Level 9  -> 10: 90 XP
	/// Level 10 -> 11: 95 XP
	/// Level 11 -> 12: 100 XP
	/// Level 12 -> 13: 105 XP
	/// ...
	///
	/// From level 10 onward, each next level costs 5 XP more.
	/// The curve is not capped at level 100.
	/// </summary>
	public static class ExpMath
	{
		private const int FirstReducedGrowthLevel = 10;
		private const double LevelTenCost = 95;
		private const double LateLevelIncrease = 5;

		/// <summary>
		/// Returns the player's current level from their accumulated,
		/// lifetime class XP.
		/// </summary>
		public static int GetLevel(double totalExp)
		{
			if (double.IsNaN(totalExp) ||
				totalExp <= 0)
			{
				return 1;
			}

			if (double.IsPositiveInfinity(totalExp))
			{
				return int.MaxValue;
			}

			double rawLevel;

			if (totalExp < GetTotalExpRequiredForLevel(10))
			{
				// For levels 1 through 10:
				//
				// total(level) = 5 * level * (level - 1)
				//
				// Solve the quadratic for level.
				rawLevel =
					(
						1 +
						Math.Sqrt(
							1 + 0.8 * totalExp
						)
					) / 2;
			}
			else
			{
				// For level 10 and above:
				//
				// total(level)
				// = 2.5 * level^2
				// + 42.5 * level
				// - 225
				//
				// Stable inverse:
				rawLevel =
					(
						-85 +
						Math.Sqrt(
							16_225 +
							40 * totalExp
						)
					) / 10;
			}

			if (rawLevel >= int.MaxValue)
			{
				return int.MaxValue;
			}

			int level = Math.Max(
				1,
				(int)Math.Floor(rawLevel)
			);

			// Correct any possible floating-point rounding at an exact
			// level boundary. These loops normally run zero times.
			while (level < int.MaxValue &&
				   totalExp >=
				   GetTotalExpRequiredForLevel(level + 1))
			{
				level++;
			}

			while (level > 1 &&
				   totalExp <
				   GetTotalExpRequiredForLevel(level))
			{
				level--;
			}

			return level;
		}

		/// <summary>
		/// XP needed to advance from currentLevel to currentLevel + 1.
		/// </summary>
		public static double GetXpRequiredForNextLevel(
			int currentLevel)
		{
			ValidateLevel(currentLevel);

			if (currentLevel <
				FirstReducedGrowthLevel)
			{
				return currentLevel * 10d;
			}

			return LevelTenCost +
				(
					currentLevel -
					FirstReducedGrowthLevel
				) * LateLevelIncrease;
		}

		/// <summary>
		/// Total accumulated XP required to begin the requested level.
		///
		/// Level 1 begins at 0 total XP.
		/// Level 2 begins at 10 total XP.
		/// Level 3 begins at 30 total XP.
		/// Level 10 begins at 450 total XP.
		/// Level 11 begins at 545 total XP.
		/// </summary>
		public static double GetTotalExpRequiredForLevel(
			int level)
		{
			ValidateLevel(level);

			if (level <= 1)
			{
				return 0;
			}

			if (level <=
				FirstReducedGrowthLevel)
			{
				return 5d *
					level *
					(level - 1);
			}

			return 2.5d * level * level +
				42.5d * level -
				225d;
		}

		/// <summary>
		/// Compatibility alias retained for older Apprentice code.
		/// It returns total XP required to begin the requested level.
		/// </summary>
		public static double GetRequiredExp(int level)
		{
			return GetTotalExpRequiredForLevel(level);
		}

		public static double GetExpUntilNextLevel(
			double totalExp)
		{
			double safeTotal =
				NormalizeTotalExp(totalExp);

			int level = GetLevel(safeTotal);

			if (level == int.MaxValue)
			{
				return 0;
			}

			return Math.Max(
				0,
				GetLevelEndExp(level) -
				safeTotal
			);
		}

		/// <summary>
		/// Returns level-bar progress from 0.0 to 1.0.
		/// </summary>
		public static double GetProgress(
			double totalExp)
		{
			double safeTotal =
				NormalizeTotalExp(totalExp);

			int level = GetLevel(safeTotal);

			if (level == int.MaxValue)
			{
				return 1;
			}

			double levelStart =
				GetLevelStartExp(level);

			double required =
				GetExperienceSpanForLevel(level);

			if (required <= 0)
			{
				return 1;
			}

			return Math.Clamp(
				(safeTotal - levelStart) /
				required,
				0,
				1
			);
		}

		public static double GetLevelStartExp(
			int level)
		{
			return GetTotalExpRequiredForLevel(level);
		}

		public static double GetLevelEndExp(
			int level)
		{
			ValidateLevel(level);

			if (level == int.MaxValue)
			{
				return double.PositiveInfinity;
			}

			return GetTotalExpRequiredForLevel(
				level + 1
			);
		}

		public static double GetExperienceIntoLevel(
			double totalExp)
		{
			double safeTotal =
				NormalizeTotalExp(totalExp);

			int level = GetLevel(safeTotal);

			return Math.Max(
				0,
				safeTotal -
				GetLevelStartExp(level)
			);
		}

		public static double GetExperienceSpanForLevel(
			int level)
		{
			return GetXpRequiredForNextLevel(level);
		}

		private static double NormalizeTotalExp(
			double totalExp)
		{
			if (double.IsNaN(totalExp) ||
				totalExp < 0)
			{
				return 0;
			}

			return totalExp;
		}

		private static void ValidateLevel(int level)
		{
			if (level < 1)
			{
				throw new ArgumentOutOfRangeException(
					nameof(level),
					level,
					"Level must be at least 1."
				);
			}
		}
	}
}
