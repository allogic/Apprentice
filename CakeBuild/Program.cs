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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

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
		public bool StrictAssetValidation { get; }

		public BuildContext(ICakeContext context)
			: base(context)
		{
			BuildConfiguration = context.Argument("configuration", "Release");
			SkipJsonValidation = context.Argument("skipJsonValidation", false);
			StrictAssetValidation = context.Argument("strictAssetValidation", false);

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
		private sealed record PythonInvocation(
			string FileName,
			IReadOnlyList<string> PrefixArguments
		);

		public override void Run(BuildContext context)
		{
			if (context.SkipJsonValidation)
			{
				return;
			}

			PythonInvocation? python = FindPython();
			if (python == null)
			{
				const string message =
					"Python 3 was not found. The extended Apprentice asset " +
					"validator did not run; the built-in JSON validation did run. " +
					"Install Python 3, set PYTHON to its executable path, or use " +
					"--strictAssetValidation=true to require it for a release check.";
				if (context.StrictAssetValidation)
				{
					throw new InvalidOperationException(message);
				}

				Console.ForegroundColor = ConsoleColor.Yellow;
				Console.WriteLine($"Warning: {message}");
				Console.ResetColor();
				return;
			}

			string validator = Path.GetFullPath("../tools/validate_assets.py");
			ProcessStartInfo startInfo = new()
			{
				FileName = python.FileName,
				UseShellExecute = false,
				WorkingDirectory = Path.GetFullPath("..")
			};
			foreach (string argument in python.PrefixArguments)
			{
				startInfo.ArgumentList.Add(argument);
			}
			startInfo.ArgumentList.Add(validator);
			using Process process = Process.Start(startInfo)
				?? throw new InvalidOperationException(
				$"Could not start '{python.FileName}' for the Apprentice asset validator."
			);
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				throw new InvalidOperationException(
					$"The Apprentice asset validator failed with exit code {process.ExitCode}."
				);
			}
		}

		private static PythonInvocation? FindPython()
		{
			List<PythonInvocation> candidates = new();
			string configured = Environment.GetEnvironmentVariable("PYTHON")?
				.Trim()
				.Trim('"') ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(configured))
			{
				candidates.Add(new PythonInvocation(configured, Array.Empty<string>()));
			}

			if (OperatingSystem.IsWindows())
			{
				candidates.Add(new PythonInvocation("py", new[] { "-3" }));
				candidates.Add(new PythonInvocation("python3", Array.Empty<string>()));
				candidates.Add(new PythonInvocation("python", Array.Empty<string>()));
			}
			else
			{
				candidates.Add(new PythonInvocation("python3", Array.Empty<string>()));
				candidates.Add(new PythonInvocation("python", Array.Empty<string>()));
			}

			HashSet<string> attempted = new(StringComparer.OrdinalIgnoreCase);
			foreach (PythonInvocation candidate in candidates)
			{
				string signature = string.Join("\0", new[] { candidate.FileName }
					.Concat(candidate.PrefixArguments));
				if (!attempted.Add(signature))
				{
					continue;
				}

				try
				{
					ProcessStartInfo probeInfo = new()
					{
						FileName = candidate.FileName,
						UseShellExecute = false,
						CreateNoWindow = true,
						RedirectStandardOutput = true,
						RedirectStandardError = true
					};
					foreach (string argument in candidate.PrefixArguments)
					{
						probeInfo.ArgumentList.Add(argument);
					}
					probeInfo.ArgumentList.Add("--version");

					using Process? probe = Process.Start(probeInfo);
					if (probe == null)
					{
						continue;
					}

					probe.WaitForExit();
					if (probe.ExitCode == 0)
					{
						return candidate;
					}
				}
				catch (System.ComponentModel.Win32Exception)
				{
					// Candidate is not installed or cannot be executed.
				}
			}

			return null;
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
			int sourceAssetCount = context
				.GetFiles($"../{BuildContext.ProjectName}/assets/**/*")
				.Count();
			int stagedAssetCount = context
				.GetFiles($"{stagingDirectory}/assets/**/*")
				.Count();
			if (sourceAssetCount == 0 || stagedAssetCount != sourceAssetCount)
			{
				throw new InvalidOperationException(
					$"The release staging directory contains {stagedAssetCount} of " +
					$"{sourceAssetCount} source assets; refusing to package a partial mod."
				);
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
				$"{stagingDirectory}/assets/apprentice/config/class.json",
				$"{stagingDirectory}/assets/apprentice/config/content-2.7.json",
				$"{stagingDirectory}/assets/apprentice/config/remaps.json",
				$"{stagingDirectory}/assets/apprentice/itemtypes/2.7/ingot.json",
				$"{stagingDirectory}/assets/apprentice/itemtypes/2.7/workitem.json",
				$"{stagingDirectory}/assets/apprentice/itemtypes/2.7/compositebow.json",
				$"{stagingDirectory}/assets/apprentice/itemtypes/2.7/towershield.json",
				$"{stagingDirectory}/assets/apprentice/itemtypes/2.7/advancedtrapkit.json",
				$"{stagingDirectory}/assets/apprentice/blocktypes/2.7/advancedtrap.json",
				$"{stagingDirectory}/assets/apprentice/shapes/block/2.7/advancedtrap-opening3.json",
				$"{stagingDirectory}/assets/apprentice/shapes/block/2.7/advancedtrap-opening4.json",
				$"{stagingDirectory}/assets/apprentice/entities/2.7/atlatl.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/grandmaster-spear.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/tower-shield.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/master-fishing-rod.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/kit-trap.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/kit-armor-upgrade.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/kit-weapon-upgrade.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/kit-tool-upgrade.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/kit-first-aid.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/composite-bow.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/composite-bow-charge1.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/composite-bow-charge2.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/composite-bow-charge3.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/composite-bow-charge4.json",
				$"{stagingDirectory}/assets/apprentice/shapes/item/2.7/composite-bow-charge5.json",
				$"{stagingDirectory}/assets/apprentice/textures/item/2.7/compositebow-material.png",
				$"{stagingDirectory}/assets/apprentice/textures/item/2.7/compositebow-grip-wrap.png",
				// Apprentice owns the collectible IDs; the game-domain assets below are
				// texture inputs for the vanilla ingot/work-item renderers only.
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
