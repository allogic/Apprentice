using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Apprentice
{
	/// <summary>
	/// Normalized, server-authoritative description of one completed
	/// gameplay interaction.
	///
	/// Future adapters can add richer metadata without changing the
	/// ExperienceManager entry point.
	/// </summary>
	internal sealed class InteractionContext
	{
		public InteractionContext(
			IServerPlayer player,
			string interaction,
			AssetLocation targetCode,
			double quantity = 1,
			BlockPos? position = null)
		{
			Player = player
				?? throw new ArgumentNullException(nameof(player));

			if (string.IsNullOrWhiteSpace(interaction))
			{
				throw new ArgumentException(
					"An interaction name is required.",
					nameof(interaction)
				);
			}

			Interaction = interaction;
			TargetCode = targetCode
				?? throw new ArgumentNullException(
					nameof(targetCode)
				);

			if (!double.IsFinite(quantity) ||
				quantity <= 0)
			{
				throw new ArgumentOutOfRangeException(
					nameof(quantity),
					quantity,
					"Interaction quantity must be a finite value above zero."
				);
			}

			Quantity = quantity;
			Position = position;
		}

		public IServerPlayer Player { get; }

		public string Interaction { get; }

		public AssetLocation TargetCode { get; }

		/// <summary>
		/// Number of completed units represented by this interaction.
		/// This is a double so systems such as Tank can award XP for
		/// fractional health damage. Normal interactions use one.
		/// </summary>
		public double Quantity { get; }

		public BlockPos? Position { get; }
	}
}
