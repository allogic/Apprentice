using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Apprentice
{
	internal sealed partial class GuiElementSkillTreeCanvas
	{
		private void DrawCanvas(
			Context ctx,
			double width,
			double height)
		{
			hitRegions.Clear();
			nodeLayouts.Clear();

			const double tabOffset = 44;

			ctx.Save();
			try
			{
				ctx.Scale(canvasScaleX, canvasScaleY);

				DrawBackground(
					ctx,
					DesignCanvasWidth,
					DesignCanvasHeight
				);

				DrawWindowTabs(
					ctx,
					DesignCanvasWidth
				);

				ctx.Translate(0, tabOffset);

				switch (activeTab)
				{
					case ApprenticeWindowTab.Stats:
						DrawStatsTab(
							ctx,
							DesignCanvasWidth,
							DesignCanvasHeight - tabOffset
						);
						break;

					case ApprenticeWindowTab.HiddenClasses:
						DrawHiddenClassesTab(
							ctx,
							DesignCanvasWidth,
							DesignCanvasHeight - tabOffset
						);
						break;

					default:
						DrawSkillTreeTabContent(
							ctx,
							DesignCanvasWidth,
							DesignCanvasHeight - tabOffset
						);
						break;
				}
			}
			finally
			{
				ctx.Restore();
			}
		}

		private void DrawSkillTreeTabContent(
			Context ctx,
			double width,
			double height)
		{

			const double margin = 14;
			const double headerHeight = 92;
			const double sidebarWidth = 205;
			const double detailWidth = 310;
			const double gap = 12;

			double contentTop =
				headerHeight +
				margin;

			double contentHeight =
				height -
				contentTop -
				margin;

			double sidebarX =
				margin;

			double sidebarY =
				contentTop;

			double treeX =
				sidebarX +
				sidebarWidth +
				gap;

			double treeWidth =
				width -
				margin * 2 -
				sidebarWidth -
				detailWidth -
				gap * 2;

			double detailX =
				treeX +
				treeWidth +
				gap;

			treePanelX = treeX;
			treePanelY = contentTop;
			treePanelWidth = treeWidth;
			treePanelHeight = contentHeight;

			DrawHeader(
				ctx,
				margin,
				margin,
				width -
					margin * 2,
				headerHeight -
					margin
			);

			DrawSidebar(
				ctx,
				sidebarX,
				sidebarY,
				sidebarWidth,
				contentHeight
			);

			DrawTreePanel(
				ctx,
				treeX,
				contentTop,
				treeWidth,
				contentHeight
			);

			DrawDetailsPanel(
				ctx,
				detailX,
				contentTop,
				detailWidth,
				contentHeight
			);
		}
	}
}

