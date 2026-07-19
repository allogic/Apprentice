using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics;
using System.IO;

namespace CakeBuild
{
	public static class Program
	{
		public static int Main(string[] args)
		{
			// Every task below intentionally uses paths relative to CakeBuild.
			// dotnet run, IDEs and CI do not guarantee the same process working
			// directory, so normalize it once before Frosting constructs context.
			Directory.SetCurrentDirectory(FindBuildDirectory());
			return new CakeHost()
				.UseContext<BuildContext>()
				.Run(args);
		}

		private static string FindBuildDirectory()
		{
			foreach (string start in new[]
			{
				Directory.GetCurrentDirectory(),
				AppContext.BaseDirectory
			})
			{
				DirectoryInfo? cursor = new(start);
				while (cursor != null)
				{
					string directProject = Path.Combine(
						cursor.FullName,
						"CakeBuild.csproj"
					);
					string? directParent = cursor.Parent?.FullName;
					if (File.Exists(directProject) &&
						directParent != null &&
						File.Exists(Path.Combine(
							directParent,
							"Apprentice",
							"modinfo.json"
						)))
					{
						return cursor.FullName;
					}

					string nestedBuild = Path.Combine(
						cursor.FullName,
						"CakeBuild"
					);
					if (File.Exists(Path.Combine(
							nestedBuild,
							"CakeBuild.csproj"
						)) &&
						File.Exists(Path.Combine(
							cursor.FullName,
							"Apprentice",
							"modinfo.json"
						)))
					{
						return nestedBuild;
					}

					cursor = cursor.Parent;
				}
			}

			throw new DirectoryNotFoundException(
				"Could not locate the Apprentice repository and CakeBuild project."
			);
		}
	}

	public class BuildContext : FrostingContext
	{
		public const string ProjectName = "Apprentice";
		public string BuildConfiguration { get; }
		public string Version { get; }
		public string Name { get; }
		public bool SkipJsonValidation { get; }

		public BuildContext(ICakeContext context)
			: base(context)
		{
			BuildConfiguration = context.Argument("configuration", "Release");
			SkipJsonValidation = context.Argument("skipJsonValidation", false);

			string modInfoPath = $"../{ProjectName}/modinfo.json";
			JObject modInfo = JObject.Parse(File.ReadAllText(modInfoPath));
			Version = ReadRequiredString(modInfo, "version", modInfoPath);
			Name = ReadRequiredString(modInfo, "modid", modInfoPath);
		}

		private static string ReadRequiredString(
			JObject document,
			string propertyName,
			string sourcePath)
		{
			string value = document[propertyName]?.Value<string>()?.Trim()
				?? string.Empty;
			if (string.IsNullOrWhiteSpace(value))
			{
				throw new InvalidOperationException(
					$"{sourcePath} must define a non-empty '{propertyName}' value."
				);
			}
			return value;
		}
	}

	[TaskName("ValidateJson")]
	public sealed class ValidateJsonTask : FrostingTask<BuildContext>
	{
		public override void Run(BuildContext context)
		{
			if (context.SkipJsonValidation)
			{
				return;
			}
			var jsonFiles = context.GetFiles($"../{BuildContext.ProjectName}/assets/**/*.json");
			foreach (var file in jsonFiles)
			{
				try
				{
					var json = File.ReadAllText(file.FullPath);
					JToken.Parse(json);
				}
				catch (JsonException ex)
				{
					throw new Exception($"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
				}
			}
		}
	}

	[TaskName("Build")]
	[IsDependentOn(typeof(ValidateAssetsTask))]
	public sealed class BuildTask : FrostingTask<BuildContext>
	{
		public override void Run(BuildContext context)
		{
			context.DotNetClean($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
				new DotNetCleanSettings
				{
					Configuration = context.BuildConfiguration
				});


			context.DotNetPublish($"../{BuildContext.ProjectName}/{BuildContext.ProjectName}.csproj",
				new DotNetPublishSettings
				{
					Configuration = context.BuildConfiguration
				});
		}
	}

	[TaskName("ValidateAssets")]
	[IsDependentOn(typeof(ValidateJsonTask))]
	public sealed class ValidateAssetsTask : FrostingTask<BuildContext>
	{
		public override void Run(BuildContext context)
		{
			if (context.SkipJsonValidation)
			{
				return;
			}

			string python = Environment.GetEnvironmentVariable("PYTHON")?.Trim()
				?? (OperatingSystem.IsWindows() ? "python" : "python3");
			string validator = Path.GetFullPath("../tools/validate_assets.py");
			ProcessStartInfo startInfo = new()
			{
				FileName = python,
				UseShellExecute = false,
				WorkingDirectory = Path.GetFullPath("..")
			};
			startInfo.ArgumentList.Add(validator);
			using Process process = Process.Start(startInfo)
				?? throw new InvalidOperationException(
				$"Could not start '{python}' for the Apprentice asset validator."
			);
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				throw new InvalidOperationException(
					$"The Apprentice asset validator failed with exit code {process.ExitCode}."
				);
			}
		}
	}

	[TaskName("Package")]
	[IsDependentOn(typeof(BuildTask))]
	public sealed class PackageTask : FrostingTask<BuildContext>
	{
		public override void Run(BuildContext context)
		{
			context.EnsureDirectoryExists("../Releases");
			string stagingDirectory = $"../Releases/{context.Name}";
			string archivePath = $"../Releases/{context.Name}_{context.Version}.zip";
			string assemblyPath =
				$"../{BuildContext.ProjectName}/bin/{context.BuildConfiguration}/Mods/mod/publish/{BuildContext.ProjectName}.dll";
			context.EnsureDirectoryExists(stagingDirectory);
			context.CleanDirectory(stagingDirectory);
			if (!context.FileExists(assemblyPath))
			{
				throw new FileNotFoundException(
					"The published Apprentice assembly is missing; refusing to create an assets-only release archive.",
					assemblyPath
				);
			}
			context.CopyFile(
				assemblyPath,
				$"{stagingDirectory}/{BuildContext.ProjectName}.dll"
			);
			if (context.DirectoryExists($"../{BuildContext.ProjectName}/assets"))
			{
				context.CopyDirectory($"../{BuildContext.ProjectName}/assets", $"../Releases/{context.Name}/assets");
			}
			context.CopyFile($"../{BuildContext.ProjectName}/modinfo.json", $"../Releases/{context.Name}/modinfo.json");
			if (context.FileExists($"../{BuildContext.ProjectName}/modicon.png"))
			{
				context.CopyFile($"../{BuildContext.ProjectName}/modicon.png", $"../Releases/{context.Name}/modicon.png");
			}

			string[] requiredReleaseFiles =
			{
				$"{stagingDirectory}/{BuildContext.ProjectName}.dll",
				$"{stagingDirectory}/modinfo.json",
				$"{stagingDirectory}/assets/apprentice/config/content-2.7.json",
				// The two ingots are deliberately vanilla metal variants now. Their
				// definitions come from the game's generic ingot itemtype, while these
				// textures and metal patches provide the Apprentice-specific material.
				// Requiring the deleted duplicate itemtype files here encouraged people
				// to copy only assets after Package failed, producing a silently broken
				// install with no ModSystem, poison, heatmap, or trap block entity.
				$"{stagingDirectory}/assets/game/textures/block/metal/ingot/starsteel.png",
				$"{stagingDirectory}/assets/game/textures/block/metal/ingot/aethersteel.png",
				$"{stagingDirectory}/assets/apprentice/patches/2.7/metals.json",
				$"{stagingDirectory}/assets/apprentice/lang/en.json"
			};
			foreach (string requiredFile in requiredReleaseFiles)
			{
				if (!context.FileExists(requiredFile))
				{
					throw new FileNotFoundException(
						"The release staging directory is incomplete; refusing to package a stale or assets-only build.",
						requiredFile
					);
				}
			}
			if (context.FileExists(archivePath))
			{
				context.DeleteFile(archivePath);
			}
			context.Zip(stagingDirectory, archivePath);
		}
	}

	[TaskName("Default")]
	[IsDependentOn(typeof(PackageTask))]
	public class DefaultTask : FrostingTask
	{
	}
}
