using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace Apprentice
{
	internal static class SkillTreeConfigLoader
	{
		private static readonly AssetLocation ConfigAsset =
			new("apprentice", "config/skilltrees.json");

		public static SkillTreeConfig Load(ICoreAPI api)
		{
			ArgumentNullException.ThrowIfNull(api);

			IAsset asset = api.Assets.Get(ConfigAsset);
			SkillTreeConfig config =
				JsonConvert.DeserializeObject<SkillTreeConfig>(
					asset.ToText()
				) ?? throw new InvalidOperationException(
					$"Failed to parse {ConfigAsset}."
				);

			Validate(config);
			return config;
		}

		private static void Validate(SkillTreeConfig config)
		{
			if (config.Version < 1 || config.Trees.Count == 0)
			{
				throw new InvalidOperationException(
					"skilltrees.json must define a positive Version and Trees."
				);
			}

			var normalized = new Dictionary<string, SkillTreeDefinition>(
				StringComparer.OrdinalIgnoreCase
			);

			foreach (var entry in config.Trees)
			{
				string classId = entry.Key.Trim();
				SkillTreeDefinition tree = entry.Value;
				tree.ClassId = string.IsNullOrWhiteSpace(tree.ClassId)
					? classId
					: tree.ClassId.Trim();

				if (!string.Equals(classId, tree.ClassId,
					StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException(
						$"Skill tree key '{classId}' does not match ClassId '{tree.ClassId}'."
					);
				}

				var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (SkillNodeDefinition node in tree.Nodes)
				{
					node.Id = node.Id.Trim();
					if (string.IsNullOrWhiteSpace(node.Id) || !nodeIds.Add(node.Id))
					{
						throw new InvalidOperationException(
							$"Tree '{classId}' contains an empty or duplicate node ID."
						);
					}

					if (node.Cost < 1 || node.MaxRank < 1 ||
						node.RequiredClassLevel < 1 || node.Column < 0 || node.Row < 0)
					{
						throw new InvalidOperationException(
							$"Node '{classId}/{node.Id}' contains invalid numeric values."
						);
					}
				}

				normalized.Add(classId, tree);
			}

			config.Trees = normalized;
		}
	}
}
