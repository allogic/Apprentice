using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;


namespace Apprentice
{
	/// <summary>
	/// One-shot suppression used when a specialized interaction itself
	/// breaks a block, such as prospecting. This prevents an additional
	/// generic DestroyBlock reward for the same physical action.
	/// </summary>
	internal static class InteractionEventSuppression
	{
		private static readonly Dictionary<
			string,
			Dictionary<string, long>
		> suppressedBreaks =
			new(StringComparer.Ordinal);

		public static void SuppressNextBlockBreak(
			string playerUid,
			BlockPos position,
			long nowMs)
		{
			SuppressBlockBreaks(
				playerUid,
				new[]
				{
					position
				},
				nowMs
			);
		}

		public static void SuppressBlockBreaks(
			string playerUid,
			IEnumerable<BlockPos> positions,
			long nowMs)
		{
			if (string.IsNullOrWhiteSpace(playerUid) ||
				positions == null)
			{
				return;
			}

			lock (suppressedBreaks)
			{
				if (!suppressedBreaks.TryGetValue(
					playerUid,
					out Dictionary<string, long>? playerBreaks))
				{
					playerBreaks =
						new Dictionary<string, long>(
							StringComparer.Ordinal
						);

					suppressedBreaks[playerUid] =
						playerBreaks;
				}

				long expiresAt =
					nowMs + 5000;

				foreach (BlockPos position in positions)
				{
					if (position == null)
					{
						continue;
					}

					playerBreaks[
						GetPositionKey(position)
					] = expiresAt;
				}
			}
		}

		public static bool ConsumeSuppressedBlockBreak(
			string playerUid,
			BlockPos? position)
		{
			if (string.IsNullOrWhiteSpace(playerUid) ||
				position == null)
			{
				return false;
			}

			lock (suppressedBreaks)
			{
				if (!suppressedBreaks.TryGetValue(
					playerUid,
					out Dictionary<string, long>? playerBreaks))
				{
					return false;
				}

				string positionKey =
					GetPositionKey(position);

				if (!playerBreaks.TryGetValue(
					positionKey,
					out long expiresAt))
				{
					RemoveExpiredEntries(
						playerUid,
						playerBreaks
					);

					return false;
				}

				playerBreaks.Remove(positionKey);

				if (playerBreaks.Count == 0)
				{
					suppressedBreaks.Remove(
						playerUid
					);
				}

				return expiresAt >=
					Environment.TickCount64;
			}
		}

		private static void RemoveExpiredEntries(
			string playerUid,
			Dictionary<string, long> playerBreaks)
		{
			long now =
				Environment.TickCount64;

			List<string> expiredKeys =
				new();

			foreach (
				KeyValuePair<string, long> entry
				in playerBreaks)
			{
				if (entry.Value < now)
				{
					expiredKeys.Add(
						entry.Key
					);
				}
			}

			foreach (string key in expiredKeys)
			{
				playerBreaks.Remove(key);
			}

			if (playerBreaks.Count == 0)
			{
				suppressedBreaks.Remove(
					playerUid
				);
			}
		}

		private static string GetPositionKey(
			BlockPos position)
		{
			return
				$"{position.X}:{position.Y}:{position.Z}";
		}
	}
}
