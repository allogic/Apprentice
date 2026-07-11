#pragma warning disable CS8602

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Newtonsoft.Json;

using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
	internal class ExperienceManager
	{
		private class ClassConfig
		{
			// Fixed JSON fields
			public required int Version { get; set; }
			public required Dictionary<string, ClassType> ClassTypes { get; set; } = [];
		}
		private class ClassType
		{
			// Fixed JSON fields
			public required string Id { get; set; }
			public required string DisplayName { get; set; }
			public required Dictionary<string, Dictionary<string, double>> Interactions { get; set; } = [];

			// Runtime cache
			[JsonIgnore]
			public Dictionary<string, List<(Regex, double)>> CompiledInteractions { get; set; } = [];
		}

		private readonly ICoreServerAPI? api = null;
		private readonly AssetLocation classAsset = new("apprentice", "config/class.json");
		private readonly ClassConfig? classConfig = null;

		public ExperienceManager(ICoreServerAPI? api)
		{
			this.api = api;

			// Retrieve our config JSON
			IAsset asset = api.Assets.Get(classAsset) ?? throw new Exception("Missing class asset!");
			classConfig = JsonConvert.DeserializeObject<ClassConfig>(asset.ToText()) ?? throw new Exception("Failed parsing class config!");

			// Pre-Build blockCode regex's for performance reasons
			foreach (var (className, classType) in classConfig.ClassTypes)
			{
				foreach (var (interactionName, interactionEvent) in classType.Interactions)
				{
					List<(Regex, double)> compiled = [];

					foreach (var (blockCode, blockExp) in interactionEvent)
					{
						Regex regex = new(
							"^" + Regex.Escape(blockCode).Replace(@"\*", ".*") + "$",
							RegexOptions.Compiled | RegexOptions.CultureInvariant
						);

						compiled.Add((regex, blockExp));
					}

					classType.CompiledInteractions[interactionName] = compiled;
				}
			}
		}

		public void UpdatePlayerExperienceByBreakingBlocks(IServerPlayer player, Block block)
		{
			// Iterate over all classes
			foreach (var (className, classType) in classConfig.ClassTypes)
			{
				// Check if we have the "DestroyBlock" interaction
				if (!classType.CompiledInteractions.TryGetValue("DestroyBlock", out var blockCodePatterns))
				{
					continue;
				}

				// Use the compiled regex's to quickly compare all blockCode patterns
				foreach (var (regex, exp) in blockCodePatterns)
				{
					string blockCodeWithDomain = block.Code.Domain + ":" + block.Code.Path;

					if (regex.IsMatch(blockCodeWithDomain))
					{
						// Increment player's experience
						double playerExp = player.Entity.WatchedAttributes.GetDouble("exp");
						playerExp += exp;
						player.Entity.WatchedAttributes.SetDouble("exp", playerExp);

						// Some debugging
						api.Logger.Notification($"You gained {exp} xp by breaking {block.Code.Path}, you got {playerExp} total xp now");
					}
				}
			}
		}
	}
}
