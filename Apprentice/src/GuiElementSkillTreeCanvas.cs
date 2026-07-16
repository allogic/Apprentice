using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Apprentice
{
	internal enum SkillNodeVisualState
	{
		Locked,
		Available,
		Purchased,
		Maximum,
		ExclusiveConflict
	}

	internal enum SkillTreeHitKind
	{
		None,
		Tab,
		Category,
		Class,
		Node,
		Purchase,
		ResetView,
		TreeArea
	}

	internal enum ApprenticeWindowTab
	{
		SkillTree,
		Stats,
		HiddenClasses
	}

	internal sealed record SkillTreeCategory(
		string Id,
		string DisplayName,
		IReadOnlyList<string> ClassIds
	);

	internal sealed record SkillTreeHitRegion(
		SkillTreeHitKind Kind,
		string Id,
		double X,
		double Y,
		double Width,
		double Height
	)
	{
		public bool Contains(
			double x,
			double y)
		{
			return x >= X &&
				   x <= X + Width &&
				   y >= Y &&
				   y <= Y + Height;
		}
	}

	internal sealed record SkillNodeLayout(
		SkillNodeDefinition Node,
		double X,
		double Y,
		double Radius
	);

	/// <summary>
	/// One texture-backed interactive element for the entire skill-tree UI.
	///
	/// The element is intentionally self-contained:
	/// - one GPU texture;
	/// - one input target;
	/// - no child buttons;
	/// - no composer rebuilding while open.
	/// </summary>
	internal sealed partial class GuiElementSkillTreeCanvas :
		GuiElement
	{
		private const int MasteryCap = 50;
		private const double DesignCanvasWidth = 1120;
		private const double DesignCanvasHeight = 682;

		private readonly SkillTreeConfig skillConfig;
		private readonly IClientNetworkChannel channel;
		private readonly List<SkillTreeCategory> categories;
		private readonly Dictionary<
			string,
			SkillTreeDefinition
		> treesById;

		private LoadedTexture canvasTexture;
		private readonly TextDrawUtil textUtil =
			new();

		private readonly CairoFont titleFont;
		private readonly CairoFont sectionFont;
		private readonly CairoFont bodyFont;
		private readonly CairoFont smallFont;
		private readonly CairoFont tinyFont;

		private readonly List<SkillTreeHitRegion>
			hitRegions =
			new();

		private readonly Dictionary<
			string,
			SkillNodeLayout
		> nodeLayouts =
			new(
				StringComparer.OrdinalIgnoreCase
			);

		private string selectedCategoryId;
		private string selectedClassId;
		private string selectedNodeId;
		private string? hoveredRegionKey;
		private string statusMessage =
			"Choose a node. Gold nodes can be learned.";

		private ApprenticeWindowTab activeTab =
			ApprenticeWindowTab.SkillTree;

		private readonly Dictionary<string, int>
			purchaseRankOverrides =
			new(StringComparer.OrdinalIgnoreCase);

		private readonly Dictionary<string, int>
			purchaseAvailablePointOverrides =
			new(StringComparer.OrdinalIgnoreCase);

		private bool purchasePending;
		private string pendingClassId = string.Empty;
		private string pendingNodeId = string.Empty;

		private double zoom = 1;
		private double textScale = 1;
		private double canvasScaleX = 1;
		private double canvasScaleY = 1;
		private double panX;
		private double panY;

		private bool draggingTree;
		private bool textureReady;
		private bool disposed;

		private double treePanelX;
		private double treePanelY;
		private double treePanelWidth;
		private double treePanelHeight;

		public override bool Focusable =>
			true;

		public override string MouseOverCursor
		{
			get;
			protected set;
		} = null!;

		public GuiElementSkillTreeCanvas(
			ICoreClientAPI api,
			ElementBounds bounds,
			SkillTreeConfig skillConfig,
			IClientNetworkChannel channel)
			: base(api, bounds)
		{
			this.skillConfig =
				skillConfig ??
				throw new ArgumentNullException(
					nameof(skillConfig)
				);

			this.channel =
				channel ??
				throw new ArgumentNullException(
					nameof(channel)
				);

			treesById =
				skillConfig.Trees.Values
					.ToDictionary(
						tree => tree.ClassId,
						StringComparer.OrdinalIgnoreCase
					);

			categories =
				BuildCategories(
					treesById
				);

			if (categories.Count == 0 ||
				treesById.Count == 0)
			{
				throw new InvalidOperationException(
					"No skill trees are available for the GUI."
				);
			}

			selectedCategoryId =
				categories[0].Id;

			selectedClassId =
				categories[0].ClassIds[0];

			selectedNodeId =
				CurrentTree.Nodes
					.FirstOrDefault()
					?.Id ??
				string.Empty;

			canvasTexture =
				new LoadedTexture(api);

			titleFont =
				CairoFont.WhiteMediumText()
					.WithColor(
						new[]
						{
							0.98,
							0.91,
							0.72,
							1.0
						}
					);

			sectionFont =
				CairoFont.WhiteSmallishText()
					.WithColor(
						new[]
						{
							0.93,
							0.84,
							0.62,
							1.0
						}
					);

			bodyFont =
				CairoFont.WhiteDetailText()
					.WithColor(
						new[]
						{
							0.91,
							0.92,
							0.95,
							1.0
						}
					);

			smallFont =
				CairoFont.WhiteSmallText()
					.WithColor(
						new[]
						{
							0.78,
							0.81,
							0.86,
							1.0
						}
					);

			tinyFont =
				CairoFont.WhiteSmallText()
					.WithColor(
						new[]
						{
							0.66,
							0.70,
							0.76,
							1.0
						}
					);
		}

		public override void ComposeElements(
			Context ctxStatic,
			ImageSurface surface)
		{
			api.Logger.Notification(
				"[Apprentice] Interactive canvas: " +
				"ComposeElements begin."
			);

			RebuildTexture(
				logStages: true
			);

			api.Logger.Notification(
				"[Apprentice] Interactive canvas: " +
				"ComposeElements complete."
			);
		}

		public void Refresh(
			string? message = null)
		{
			if (!string.IsNullOrWhiteSpace(
					message))
			{
				statusMessage =
					message;
			}

			RebuildTexture();
		}

		public void ApplyPurchaseResult(
			SkillPurchaseResultPacket packet)
		{
			string classId =
				(packet.ClassId ?? string.Empty).Trim();

			string nodeId =
				(packet.NodeId ?? string.Empty).Trim();

			bool matchedPendingRequest =
				purchasePending &&
				classId.Equals(
					pendingClassId,
					StringComparison.OrdinalIgnoreCase
				) &&
				nodeId.Equals(
					pendingNodeId,
					StringComparison.OrdinalIgnoreCase
				);

			purchasePending = false;
			pendingClassId = string.Empty;
			pendingNodeId = string.Empty;

			if (packet.Success &&
				!string.IsNullOrWhiteSpace(classId) &&
				!string.IsNullOrWhiteSpace(nodeId))
			{
				purchaseRankOverrides[
					BuildNodeCacheKey(classId, nodeId)
				] = Math.Max(0, packet.NewRank);

				purchaseAvailablePointOverrides[classId] =
					Math.Max(0, packet.AvailablePoints);
			}

			statusMessage =
				string.IsNullOrWhiteSpace(packet.Message)
					? packet.Success
						? "Skill purchase accepted by the server."
						: "Skill purchase rejected by the server."
					: packet.Message;

			api.Logger.Notification(
				"[Apprentice] Purchase result received: " +
				$"success={packet.Success}, class={classId}, node={nodeId}, " +
				$"rank={packet.NewRank}, available={packet.AvailablePoints}, " +
				$"matchedPending={matchedPendingRequest}."
			);

			RebuildTexture();
		}

		public override void RenderInteractiveElements(
			float deltaTime)
		{
			if (!textureReady ||
				canvasTexture.TextureId == 0)
			{
				return;
			}

			Render2DTexture(
				canvasTexture.TextureId,
				Bounds
			);
		}

		public override void OnMouseMove(
			ICoreClientAPI api,
			MouseEvent args)
		{
			double localX =
				args.X -
				Bounds.absX;

			double localY =
				args.Y -
				Bounds.absY;

			if (draggingTree)
			{
				panX +=
					args.DeltaX /
					Math.Max(0.01, canvasScaleX);

				panY +=
					args.DeltaY /
					Math.Max(0.01, canvasScaleY);

				ClampPan();
				RebuildTexture();

				args.Handled = true;
				return;
			}

			SkillTreeHitRegion? region =
				FindRegion(
					localX,
					localY
				);

			string? newKey =
				region == null
					? null
					: $"{region.Kind}:{region.Id}";

			MouseOverCursor =
				region != null &&
				region.Kind !=
					SkillTreeHitKind.TreeArea
					? "hand"
					: null!;

			if (!string.Equals(
					hoveredRegionKey,
					newKey,
					StringComparison.Ordinal))
			{
				hoveredRegionKey =
					newKey;

				RebuildTexture();
			}
		}

		public override void OnMouseDownOnElement(
			ICoreClientAPI api,
			MouseEvent args)
		{
			base.OnMouseDownOnElement(
				api,
				args
			);

			double localX =
				args.X -
				Bounds.absX;

			double localY =
				args.Y -
				Bounds.absY;

			SkillTreeHitRegion? region =
				FindRegion(
					localX,
					localY
				);

			if (args.Button ==
					EnumMouseButton.Middle &&
				IsInsideTreePanel(
					localX,
					localY
				))
			{
				draggingTree = true;
				args.Handled = true;
				return;
			}

			if (args.Button !=
				EnumMouseButton.Left ||
				region == null)
			{
				return;
			}

			switch (region.Kind)
			{
				case SkillTreeHitKind.Tab:
					activeTab = region.Id switch
					{
						"stats" => ApprenticeWindowTab.Stats,
						"hidden" => ApprenticeWindowTab.HiddenClasses,
						_ => ApprenticeWindowTab.SkillTree
					};
					draggingTree = false;
					statusMessage = activeTab switch
					{
						ApprenticeWindowTab.Stats => "Showing all active Apprentice bonuses.",
						ApprenticeWindowTab.HiddenClasses => "Hidden classes are discovered through capstone combinations.",
						_ => "Skill tree selected."
					};
					break;

				case SkillTreeHitKind.Category:
					SelectCategory(
						region.Id
					);
					break;

				case SkillTreeHitKind.Class:
					SelectClass(
						region.Id
					);
					break;

				case SkillTreeHitKind.Node:
					selectedNodeId =
						region.Id;

					statusMessage =
						"Node selected.";
					break;

				case SkillTreeHitKind.Purchase:
					TryPurchaseSelectedNode();
					break;

				case SkillTreeHitKind.ResetView:
					zoom = 1;
					panX = 0;
					panY = 0;
					statusMessage =
						"Tree view reset.";
					break;
			}

			RebuildTexture();
			args.Handled = true;
		}

		public override void OnMouseUp(
			ICoreClientAPI api,
			MouseEvent args)
		{
			draggingTree = false;

			base.OnMouseUp(
				api,
				args
			);
		}

		public override void OnMouseWheel(
			ICoreClientAPI api,
			MouseWheelEventArgs args)
		{
			if (activeTab != ApprenticeWindowTab.SkillTree ||
				!IsMouseOverTreePanel())
			{
				return;
			}

			double change =
				args.deltaPrecise != 0
					? args.deltaPrecise
					: args.delta;

			zoom =
				Math.Clamp(
					zoom +
					Math.Sign(change) *
					0.08,
					0.78,
					1.30
				);

			ClampPan();

			statusMessage =
				$"Tree zoom: {zoom * 100:0}%";

			RebuildTexture();
			args.SetHandled();
		}

		private bool IsMouseOverTreePanel()
		{
			double localX =
				(api.Input.MouseX - Bounds.absX) /
				Math.Max(0.01, canvasScaleX);

			double localY =
				(api.Input.MouseY - Bounds.absY) /
				Math.Max(0.01, canvasScaleY);

			return localX >= treePanelX &&
				   localX <= treePanelX + treePanelWidth &&
				   localY >= treePanelY + 44 &&
				   localY <= treePanelY + 44 + treePanelHeight;
		}

		private bool IsInsideTreePanel(
			double x,
			double y)
		{
			double designX =
				x / Math.Max(0.01, canvasScaleX);
			double contentY =
				y / Math.Max(0.01, canvasScaleY) - 44;

			return designX >= treePanelX &&
				   designX <= treePanelX +
					   treePanelWidth &&
				   contentY >= treePanelY &&
				   contentY <= treePanelY +
					   treePanelHeight;
		}

		private void SelectCategory(
			string categoryId)
		{
			SkillTreeCategory? category =
				categories.FirstOrDefault(
					candidate =>
						candidate.Id.Equals(
							categoryId,
							StringComparison.OrdinalIgnoreCase
						)
				);

			if (category == null)
			{
				return;
			}

			selectedCategoryId =
				category.Id;

			if (!category.ClassIds.Contains(
					selectedClassId,
					StringComparer.OrdinalIgnoreCase))
			{
				selectedClassId =
					category.ClassIds[0];

				selectedNodeId =
					CurrentTree.Nodes
						.FirstOrDefault()
						?.Id ??
					string.Empty;
			}

			statusMessage =
				$"{category.DisplayName} category selected.";
		}

		private void SelectClass(
			string classId)
		{
			if (!treesById.ContainsKey(
					classId))
			{
				return;
			}

			selectedClassId =
				classId;

			selectedNodeId =
				CurrentTree.Nodes
					.FirstOrDefault()
					?.Id ??
				string.Empty;

			zoom = 1;
			panX = 0;
			panY = 0;

			statusMessage =
				$"{CurrentTree.DisplayName} selected.";
		}

		private void TryPurchaseSelectedNode()
		{
			if (purchasePending)
			{
				statusMessage =
					"A purchase request is already waiting for the server.";
				return;
			}

			SkillNodeDefinition? node =
				CurrentNode;

			if (node == null)
			{
				statusMessage =
					"No node is selected.";
				return;
			}

			NodeEvaluation evaluation =
				EvaluateNode(
					CurrentTree,
					node
				);

			if (!evaluation.CanPurchase)
			{
				statusMessage =
					evaluation.Reason;
				return;
			}

			pendingClassId = CurrentTree.ClassId;
			pendingNodeId = node.Id;
			purchasePending = true;

			api.Logger.Notification(
				"[Apprentice] Sending purchase request: " +
				$"class={pendingClassId}, node={pendingNodeId}."
			);

			channel.SendPacket(
				new SkillPurchaseRequestPacket
				{
					ClassId =
						pendingClassId,
					NodeId =
						pendingNodeId
				}
			);

			statusMessage =
				$"Waiting for server: {node.DisplayName}...";
		}

		private void RebuildTexture(
			bool logStages = false)
		{
			try
			{
				Bounds.CalcWorldBounds();

				if (logStages)
				{
					api.Logger.Notification(
						"[Apprentice] Interactive canvas: " +
						"bounds calculated."
					);
				}

				int width =
					Math.Max(
						1,
						(int)Math.Ceiling(
							Bounds.InnerWidth
						)
					);

				int height =
					Math.Max(
						1,
						(int)Math.Ceiling(
							Bounds.InnerHeight
						)
					);

				UpdateCanvasScale(
					width,
					height
				);

				if (logStages)
				{
					api.Logger.Notification(
						"[Apprentice] Interactive canvas: " +
						$"surface size {width}x{height}."
					);
				}

				using var surface =
					new ImageSurface(
						Format.ARGB32,
						width,
						height
					);

				using Context ctx =
					genContext(
						surface
					);

				if (logStages)
				{
					api.Logger.Notification(
						"[Apprentice] Interactive canvas: " +
						"Cairo context ready."
					);
				}

				DrawCanvas(
					ctx,
					width,
					height
				);

				if (logStages)
				{
					api.Logger.Notification(
						"[Apprentice] Interactive canvas: " +
						"Cairo drawing complete."
					);
				}

				generateTexture(
					surface,
					ref canvasTexture
				);

				if (logStages)
				{
					api.Logger.Notification(
						"[Apprentice] Interactive canvas: " +
						"GPU texture upload complete."
					);
				}

				textureReady = true;
			}
			catch (Exception exception)
			{
				textureReady = false;

				api.Logger.Error(
					"[Apprentice] Interactive skill-tree " +
					"canvas failed to rebuild."
				);

				api.Logger.Error(
					exception
				);
			}
		}

		internal sealed record NodeEvaluation(
			SkillNodeVisualState State,
			bool CanPurchase,
			string Reason
		);

		internal sealed record RequirementView(
			bool Met,
			string Text
		);

		internal sealed record ThemePalette(
			double AccentR,
			double AccentG,
			double AccentB,
			double PanelR,
			double PanelG,
			double PanelB,
			double SuccessR,
			double SuccessG,
			double SuccessB
		);
	}
}

