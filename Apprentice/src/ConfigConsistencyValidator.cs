using System;
using System.Collections.Generic;
using System.Linq;

namespace Apprentice
{
	/// <summary>
	/// Validates references that span class.json and skilltrees.json after
	/// both individual loaders have normalized their own documents.
	/// </summary>
	internal static class ConfigConsistencyValidator
	{
		public static void Validate(
			ClassConfig classConfig,
			SkillTreeConfig skillTreeConfig)
		{
			ArgumentNullException.ThrowIfNull(classConfig);
			ArgumentNullException.ThrowIfNull(skillTreeConfig);

			var classIds = new HashSet<string>(
				classConfig.ClassTypes.Keys,
				StringComparer.OrdinalIgnoreCase
			);
			var treeIds = new HashSet<string>(
				skillTreeConfig.Trees.Keys,
				StringComparer.OrdinalIgnoreCase
			);

			string[] missingTrees = classIds
				.Except(treeIds, StringComparer.OrdinalIgnoreCase)
				.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			string[] unknownTrees = treeIds
				.Except(classIds, StringComparer.OrdinalIgnoreCase)
				.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
				.ToArray();

			if (missingTrees.Length > 0 || unknownTrees.Length > 0)
			{
				throw new InvalidOperationException(
					"Class/skill-tree IDs do not match. " +
					$"Missing trees: {Format(missingTrees)}. " +
					$"Unknown trees: {Format(unknownTrees)}."
				);
			}

			foreach (KeyValuePair<string, SkillTreeDefinition> entry
					 in skillTreeConfig.Trees)
			{
				ValidateTree(entry.Key, entry.Value);
			}
		}

		private static void ValidateTree(
			string classId,
			SkillTreeDefinition tree)
		{
			var nodeIds = new HashSet<string>(
				tree.Nodes.Select(node => node.Id),
				StringComparer.OrdinalIgnoreCase
			);
			var occupiedPositions = new HashSet<(int Column, int Row)>();

			foreach (SkillNodeDefinition node in tree.Nodes)
			{
				if (!occupiedPositions.Add((node.Column, node.Row)))
				{
					throw new InvalidOperationException(
						$"Tree '{classId}' contains more than one " +
						$"node at column {node.Column}, row {node.Row}."
					);
				}

				ValidateRequirements(
					classId,
					node,
					"Requires",
					node.Requires,
					nodeIds
				);
				ValidateRequirements(
					classId,
					node,
					"RequiresAny",
					node.RequiresAny,
					nodeIds
				);
			}
		}

		private static void ValidateRequirements(
			string classId,
			SkillNodeDefinition node,
			string propertyName,
			IEnumerable<string> requiredIds,
			HashSet<string> nodeIds)
		{
			foreach (string requiredId in requiredIds)
			{
				if (nodeIds.Contains(requiredId))
				{
					continue;
				}

				throw new InvalidOperationException(
					$"Tree '{classId}' node '{node.Id}' has an " +
					$"unknown {propertyName} reference " +
					$"'{requiredId}'."
				);
			}
		}

		private static string Format(IEnumerable<string> values)
		{
			string text = string.Join(", ", values);
			return string.IsNullOrEmpty(text) ? "none" : text;
		}
	}
}
