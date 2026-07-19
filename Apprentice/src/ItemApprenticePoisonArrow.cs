using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace Apprentice
{
    /// <summary>
    /// Keeps the complete vanilla arrow/projectile implementation and only
    /// adds Apprentice's poison information.  Inheriting ItemArrow is
    /// important: bows discover, consume and launch these stacks exactly like
    /// every native arrow.
    /// </summary>
    public sealed class ItemApprenticePoisonArrow : ItemArrow
    {
        public override void GetHeldItemInfo(
            ItemSlot inSlot,
            StringBuilder description,
            IWorldAccessor world,
            bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, description, world, withDebugInfo);

            string poison = Attributes?["apprenticePoison"]
                .AsString(string.Empty) ?? string.Empty;
            (double dps, double duration) = poison.ToLowerInvariant() switch
            {
                "mild" => (0.2, 8),
                "standard" => (0.3, 12),
                "potent" => (0.4, 16),
                "grandmaster" => (0.5, 20),
                _ => (0, 0)
            };

            if (dps <= 0 || duration <= 0) return;

            description.AppendLine(Lang.Get(
                "apprentice:poison-arrow-effect",
                dps.ToString("0.0"),
                duration.ToString("0"),
                (dps * duration).ToString("0.0")
            ));
        }
    }
}
