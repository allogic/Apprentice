using System;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Apprentice
{
    /// <summary>
    /// Resolves the exact hot-spring blocks selected by Vintage Story's own
    /// worldgen/global.json. Level 7 deliberately owns only basin selection;
    /// all hot-spring fluid, gravel, bacteria textures and gameplay behavior
    /// remain base-game content.
    /// </summary>
    internal sealed class HighlandsNativeHotSpringBlocks
    {
        private HighlandsNativeHotSpringBlocks(
            Block boilingWater,
            Block sludgyGravel,
            Block[] bacteriaByTemperature)
        {
            BoilingWater = boilingWater;
            SludgyGravel = sludgyGravel;
            BacteriaByTemperature =
                bacteriaByTemperature;
        }

        internal Block BoilingWater { get; }

        internal Block SludgyGravel { get; }

        /// <summary>
        /// Base-game order: 87, 74, 65 and 55 degrees.
        /// </summary>
        internal Block[] BacteriaByTemperature { get; }

        internal static bool TryResolve(
            ICoreServerAPI api,
            out HighlandsNativeHotSpringBlocks? blocks,
            out string error)
        {
            blocks = null;
            try
            {
                IAsset asset = api.Assets.Get(
                    new AssetLocation(
                        "game",
                        "worldgen/global.json"
                    )
                );
                NativeWorldgenConfig config =
                    asset.ToObject<NativeWorldgenConfig>();
                Block boilingWater = ResolveGameBlock(
                    api,
                    config.BoilingWaterBlockCode,
                    "boilingWaterBlockCode"
                );
                Block sludgyGravel = ResolveGameBlock(
                    api,
                    config.SludgyGravelBlockCode,
                    "sludgyGravelBlockCode"
                );
                Block[] bacteria =
                {
                    ResolveGameBlock(
                        api,
                        config.HotSpringBacteria87DegCode,
                        "hotSpringBacteria87DegCode"
                    ),
                    ResolveGameBlock(
                        api,
                        config.HotSpringBacteriaSmooth74DegCode,
                        "hotSpringBacteriaSmooth74DegCode"
                    ),
                    ResolveGameBlock(
                        api,
                        config.HotSpringBacteriaSmooth65DegCode,
                        "hotSpringBacteriaSmooth65DegCode"
                    ),
                    ResolveGameBlock(
                        api,
                        config.HotSpringBacteriaSmooth55DegCode,
                        "hotSpringBacteriaSmooth55DegCode"
                    )
                };
                if (!string.Equals(
                        boilingWater.LiquidCode,
                        "boilingwater",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "the configured base-game hot-spring fluid is not boiling water"
                    );
                }

                blocks = new HighlandsNativeHotSpringBlocks(
                    boilingWater,
                    sludgyGravel,
                    bacteria
                );
                error = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                error =
                    exception.GetType().Name + ": " +
                    exception.Message;
                return false;
            }
        }

        private static Block ResolveGameBlock(
            ICoreServerAPI api,
            AssetLocation? code,
            string settingName)
        {
            if (code == null)
            {
                throw new InvalidOperationException(
                    $"base-game worldgen/global.json does not define {settingName}"
                );
            }
            if (!string.Equals(
                    code.Domain,
                    "game",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{settingName} resolved outside the base-game domain: {code}"
                );
            }

            Block? block = api.World.GetBlock(code);
            if (block == null || block.Id <= 0)
            {
                throw new InvalidOperationException(
                    $"base-game hot-spring block did not load: {code}"
                );
            }
            return block;
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class NativeWorldgenConfig
        {
            [JsonProperty("hotSpringBacteria87DegCode")]
            internal AssetLocation?
                HotSpringBacteria87DegCode { get; set; }

            [JsonProperty("hotSpringBacteriaSmooth74DegCode")]
            internal AssetLocation?
                HotSpringBacteriaSmooth74DegCode { get; set; }

            [JsonProperty("hotSpringBacteriaSmooth65DegCode")]
            internal AssetLocation?
                HotSpringBacteriaSmooth65DegCode { get; set; }

            [JsonProperty("hotSpringBacteriaSmooth55DegCode")]
            internal AssetLocation?
                HotSpringBacteriaSmooth55DegCode { get; set; }

            [JsonProperty("sludgyGravelBlockCode")]
            internal AssetLocation?
                SludgyGravelBlockCode { get; set; }

            [JsonProperty("boilingWaterBlockCode")]
            internal AssetLocation?
                BoilingWaterBlockCode { get; set; }
        }
    }
}
