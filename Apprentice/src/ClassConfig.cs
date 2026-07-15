using System.Collections.Generic;
using Newtonsoft.Json;

namespace Apprentice
{
	/// <summary>
	/// Schema for assets/apprentice/config/class.json.
	/// Which classes exist is controlled only by that JSON file.
	/// </summary>
	internal sealed class ClassConfig
	{
		[JsonProperty("Version")]
		public int Version { get; set; }

		[JsonProperty("ClassTypes")]
		public Dictionary<string, ClassDefinition> ClassTypes { get; set; }
			= new();
	}

	internal sealed class ClassDefinition
	{
		[JsonProperty("Id")]
		public string Id { get; set; } = string.Empty;

		[JsonProperty("DisplayName")]
		public string DisplayName { get; set; } = string.Empty;

		[JsonProperty("Interactions")]
		public Dictionary<string, Dictionary<string, double>>
			Interactions
		{ get; set; } = new();
	}
}
