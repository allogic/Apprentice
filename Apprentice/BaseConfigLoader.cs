using System;
using Vintagestory.API.Common;

namespace Apprentice
{
    internal static class BaseConfigLoader
    {
        public static BaseConfig Load(ICoreAPI api)
        {
            ArgumentNullException.ThrowIfNull(api);

            try
            {
                BaseConfig config =
                    api.LoadModConfig<BaseConfig>(
                        ApprenticeConstants.BaseConfigFile
                    )
                    ?? new BaseConfig();

                config.Normalize();

                // Store the normalized config so newly added generic
                // options appear in the user's ModConfig directory.
                api.StoreModConfig(
                    config,
                    ApprenticeConstants.BaseConfigFile
                );

                return config;
            }
            catch (Exception exception)
            {
                api.Logger.Error(
                    $"Could not load " +
                    $"{ApprenticeConstants.BaseConfigFile}. " +
                    "Using default values for this session."
                );

                api.Logger.Error(exception);

                BaseConfig fallback = new();
                fallback.Normalize();
                return fallback;
            }
        }
    }
}
