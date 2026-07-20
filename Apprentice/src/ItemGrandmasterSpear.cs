using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Apprentice
{
    /// <summary>
    /// Retains the native spear lifecycle while preventing a missing or
    /// mistyped projectile asset from taking down the entire client. The
    /// matching Apprentice entity still supplies the normal thrown spear.
    /// </summary>
    public sealed class ItemGrandmasterSpear : ItemSpear
    {
        public override void OnHeldInteractStop(
            float secondsUsed,
            ItemSlot slot,
            EntityAgent byEntity,
            BlockSelection blockSel,
            EntitySelection entitySel)
        {
            string entityCode = Attributes?["spearEntityCode"]
                .AsString(string.Empty) ?? string.Empty;
            EntityProperties? entityType = entityCode.Length == 0
                ? null
                : byEntity.World.GetEntityType(new AssetLocation(entityCode));

            if (entityType == null)
            {
                byEntity.Attributes.SetInt("aiming", 0);
                byEntity.Attributes.SetInt("aimingCancel", 1);
                byEntity.StopAnimation("aim");
                api.Logger.Error(
                    "[Apprentice] Grandmaster Spear throw cancelled: " +
                    "projectile entity '{0}' is not registered.",
                    entityCode
                );
                return;
            }

            base.OnHeldInteractStop(
                secondsUsed,
                slot,
                byEntity,
                blockSel,
                entitySel
            );
        }
    }
}
