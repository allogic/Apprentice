using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace Apprentice
{
	internal static class ClassConfigLoader
	{
		private static readonly AssetLocation ClassConfigAsset =
			new("apprentice", "config/class.json");

		public static ClassConfig Load(ICoreAPI api)
		{
			ArgumentNullException.ThrowIfNull(api);

			// Called from ModSystem.AssetsLoaded(), not Start().
			IAsset asset = api.Assets.Get(ClassConfigAsset);

			ClassConfig config =
				JsonConvert.DeserializeObject<ClassConfig>(
					asset.ToText()
				)
				?? throw new InvalidOperationException(
					$"Failed to parse class configuration: " +
					$"{ClassConfigAsset}"
				);

			ValidateAndNormalize(config);
			return config;
		}

		private static void ValidateAndNormalize(ClassConfig config)
		{
			if (config.Version <= 0)
			{
				throw new InvalidOperationException(
					"class.json must contain a positive Version."
				);
			}

			if (config.ClassTypes == null ||
				config.ClassTypes.Count == 0)
			{
				throw new InvalidOperationException(
					"class.json must define at least one class " +
					"inside ClassTypes."
				);
			}

			var normalizedClasses =
				new Dictionary<string, ClassDefinition>(
					StringComparer.OrdinalIgnoreCase
				);

			foreach (KeyValuePair<string, ClassDefinition> classEntry
					 in config.ClassTypes)
			{
				string jsonKey = classEntry.Key?.Trim()
					?? string.Empty;

				ClassDefinition classDefinition =
					classEntry.Value;

				if (string.IsNullOrWhiteSpace(jsonKey))
				{
					throw new InvalidOperationException(
						"class.json contains an empty class key."
					);
				}

				if (classDefinition == null)
				{
					throw new InvalidOperationException(
						$"Class '{jsonKey}' has no definition."
					);
				}

				// The ClassTypes dictionary key is canonical. The Id
				// property remains in JSON for readability and validation.
				if (string.IsNullOrWhiteSpace(classDefinition.Id))
				{
					classDefinition.Id = jsonKey;
				}
				else
				{
					classDefinition.Id =
						classDefinition.Id.Trim();

					if (!string.Equals(
						classDefinition.Id,
						jsonKey,
						StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException(
							$"Class key '{jsonKey}' does not match " +
							$"its Id '{classDefinition.Id}'."
						);
					}
				}

				if (classDefinition.Id.Contains('/'))
				{
					throw new InvalidOperationException(
						$"Class ID '{classDefinition.Id}' cannot " +
						"contain '/'."
					);
				}

				classDefinition.DisplayName =
					string.IsNullOrWhiteSpace(
						classDefinition.DisplayName)
					? classDefinition.Id
					: classDefinition.DisplayName.Trim();

				classDefinition.Interactions ??= new();

				var normalizedInteractions =
					new Dictionary<
						string,
						Dictionary<string, double>
					>(StringComparer.OrdinalIgnoreCase);

				foreach (
					KeyValuePair<
						string,
						Dictionary<string, double>
					> interactionEntry
					in classDefinition.Interactions)
				{
					string interactionName =
						interactionEntry.Key?.Trim()
						?? string.Empty;

					Dictionary<string, double> rewards =
						interactionEntry.Value;

					if (string.IsNullOrWhiteSpace(
						interactionName))
					{
						throw new InvalidOperationException(
							$"Class '{classDefinition.Id}' has an " +
							"empty interaction name."
						);
					}

					if (rewards == null)
					{
						throw new InvalidOperationException(
							$"Interaction '{interactionName}' in " +
							$"class '{classDefinition.Id}' has no " +
							"reward table."
						);
					}

					var normalizedRewards =
						new Dictionary<string, double>(
							StringComparer.OrdinalIgnoreCase
						);

					foreach (
						KeyValuePair<string, double> rewardEntry
						in rewards)
					{
						string pattern =
							rewardEntry.Key?.Trim()
							?? string.Empty;

						double experience =
							rewardEntry.Value;

						if (string.IsNullOrWhiteSpace(pattern))
						{
							throw new InvalidOperationException(
								$"Interaction '{interactionName}' " +
								$"in class '{classDefinition.Id}' " +
								"contains an empty pattern."
							);
						}

						if (!double.IsFinite(experience) ||
							experience < 0)
						{
							throw new InvalidOperationException(
								$"Interaction '{interactionName}' " +
								$"in class '{classDefinition.Id}' " +
								$"contains invalid XP for pattern " +
								$"'{pattern}'."
							);
						}

						if (!normalizedRewards.TryAdd(
							pattern,
							experience))
						{
							throw new InvalidOperationException(
								$"Interaction '{interactionName}' " +
								$"in class '{classDefinition.Id}' " +
								$"contains duplicate pattern " +
								$"'{pattern}'."
							);
						}
					}

					if (!normalizedInteractions.TryAdd(
						interactionName,
						normalizedRewards))
					{
						throw new InvalidOperationException(
							$"Class '{classDefinition.Id}' contains " +
							$"duplicate interaction " +
							$"'{interactionName}'."
						);
					}
				}

				classDefinition.Interactions =
					normalizedInteractions;

				if (!normalizedClasses.TryAdd(
					classDefinition.Id,
					classDefinition))
				{
					throw new InvalidOperationException(
						$"Duplicate class ID " +
						$"'{classDefinition.Id}'."
					);
				}
			}

			config.ClassTypes = normalizedClasses;
		}
	}
}
