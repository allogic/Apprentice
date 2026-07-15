using System;
using Vintagestory.API.Common;

namespace Apprentice
{
    /// <summary>
    /// Pure classification helpers shared by native event adapters.
    /// </summary>
    internal static class NativeInteractionClassifier
    {
        public static bool IsPlantingPlacement(
            ItemStack? sourceStack,
            Block? placedBlock)
        {
            AssetLocation? sourceCode =
                sourceStack?.Collectible?.Code;

            if (sourceCode != null &&
                IsPlantingItemPath(sourceCode.Path))
            {
                return true;
            }

            // This also catches crop placement performed by unusual tools
            // or other mods whose source item code does not contain
            // "seed" or "sapling".
            return placedBlock?.CropProps != null;
        }

        public static AssetLocation? ResolvePlantTarget(
            ItemStack? sourceStack,
            Block? placedBlock)
        {
            // Plant rewards are normally configured against the seed or
            // sapling item, so prefer the consumed source item.
            return sourceStack?.Collectible?.Code
                ?? placedBlock?.Code;
        }

        public static bool IsMatureCrop(Block? block)
        {
            if (block?.Code == null ||
                block.CropProps == null ||
                block.CropProps.GrowthStages <= 0)
            {
                return false;
            }

            if (!TryReadGrowthStage(
                block.Code.Path,
                out int growthStage))
            {
                return false;
            }

            return growthStage >=
                block.CropProps.GrowthStages;
        }

        public static bool IsSpecialHarvestBlock(
            Block? block)
        {
            string path =
                block?.Code?.Path ??
                string.Empty;

            string typeName =
                block?.GetType().Name ??
                string.Empty;

            string combined =
                path +
                " " +
                typeName;

            string[] tokens =
            [
                "beehive",
                "skep",
                "honey",
                "fruit",
                "berry"
            ];

            foreach (string token in tokens)
            {
                if (combined.Contains(
                    token,
                    StringComparison.OrdinalIgnoreCase
                ))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPlantingItemPath(
            string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return path.Contains(
                       "seed",
                       StringComparison.OrdinalIgnoreCase
                   ) ||
                   path.Contains(
                       "sapling",
                       StringComparison.OrdinalIgnoreCase
                   );
        }

        private static bool TryReadGrowthStage(
            string path,
            out int growthStage)
        {
            growthStage = 0;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string[] parts = path.Split(
                '-',
                StringSplitOptions.RemoveEmptyEntries
            );

            // Vanilla crop block codes carry their stage as a numeric
            // variant. Scan backwards so additional leading variants do
            // not matter.
            for (int index = parts.Length - 1;
                 index >= 0;
                 index--)
            {
                if (int.TryParse(
                    parts[index],
                    out growthStage))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
