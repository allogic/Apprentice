using System;

using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Apprentice
{
	/// <summary>
	/// Keeps vanilla bow firing and cancellation behavior while allowing the
	/// Composite Bow to display two additional late-draw model states.
	/// </summary>
	public sealed class ItemCompositeBow : ItemBow
	{
		private const int MaximumRenderVariant = 5;
		private const float RenderVariantsPerSecond = 4f;

		public override bool OnHeldInteractStep(
			float secondsUsed,
			ItemSlot slot,
			EntityAgent byEntity,
			BlockSelection blockSel,
			EntitySelection entitySel
		)
		{
			ItemStack? itemStack = slot.Itemstack;
			if (itemStack == null)
			{
				return false;
			}

			int renderVariant = GameMath.Clamp(
				(int)Math.Ceiling(secondsUsed * RenderVariantsPerSecond),
				0,
				MaximumRenderVariant
			);
			int previousRenderVariant = itemStack.Attributes.GetInt(
				"renderVariant",
				0
			);

			itemStack.TempAttributes.SetInt("renderVariant", renderVariant);
			itemStack.Attributes.SetInt("renderVariant", renderVariant);

			if (previousRenderVariant != renderVariant)
			{
				(byEntity as EntityPlayer)?.Player?.InventoryManager
					.BroadcastHotbarSlot();
			}

			return true;
		}
	}
}
