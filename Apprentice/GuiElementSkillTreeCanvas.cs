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
    internal sealed class GuiElementSkillTreeCanvas :
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

        private void DrawWindowTabs(
            Context ctx,
            double width)
        {
            const double tabY = 5;
            const double tabHeight = 34;
            const double tabWidth = 150;
            const double gap = 8;

            DrawWindowTabButton(ctx, 14, tabY, tabWidth, tabHeight,
                "SKILLTREE", "skilltree", activeTab == ApprenticeWindowTab.SkillTree);
            DrawWindowTabButton(ctx, 14 + tabWidth + gap, tabY, tabWidth, tabHeight,
                "STATS", "stats", activeTab == ApprenticeWindowTab.Stats);
            DrawWindowTabButton(ctx, 14 + (tabWidth + gap) * 2, tabY, tabWidth, tabHeight,
                "HIDDEN", "hidden", activeTab == ApprenticeWindowTab.HiddenClasses);
        }

        private void DrawWindowTabButton(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            string label,
            string id,
            bool selected)
        {
            RoundRectangle(ctx, x, y, width, height, 8);
            SetColor(ctx,
                selected ? 0.34 : 0.10,
                selected ? 0.24 : 0.11,
                selected ? 0.08 : 0.14,
                0.96);
            ctx.FillPreserve();
            SetColor(ctx,
                selected ? 0.95 : 0.30,
                selected ? 0.72 : 0.32,
                selected ? 0.25 : 0.37,
                1);
            ctx.LineWidth = selected ? 2.5 : 1.2;
            ctx.Stroke();
            DrawCenteredText(ctx, sectionFont, label, x + width / 2, y + 9);

            hitRegions.Add(new SkillTreeHitRegion(
                SkillTreeHitKind.Tab,
                id,
                x,
                y,
                width,
                height));
        }

        private void DrawStatsTab(
            Context ctx,
            double width,
            double height)
        {
            const double margin = 14;
            ThemePalette theme = GetTheme(CurrentTree);
            DrawPanel(ctx, margin, margin, width - margin * 2, height - margin * 2,
                0.05, 0.06, 0.085, 0.985,
                theme.AccentR, theme.AccentG, theme.AccentB, 0.85);

            DrawText(ctx, titleFont, "ACTIVE SKILLTREE BONUSES", margin + 20, margin + 16);
            DrawText(ctx, smallFont,
                "Every line below is currently active on your character.",
                margin + 20, margin + 50);

            Entity? entity = api.World?.Player?.Entity;
            if (entity == null)
            {
                DrawText(ctx, bodyFont, "Player data is not available.", margin + 20, margin + 86);
                return;
            }

            var entries = new List<(string Group, string Text)>();

            foreach (SkillTreeDefinition tree in skillConfig.Trees.Values
                .OrderBy(tree => tree.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                foreach (SkillNodeDefinition node in tree.Nodes)
                {
                    int rank = SkillTreeData.GetNodeRank(entity, tree.ClassId, node.Id);
                    if (rank <= 0)
                    {
                        continue;
                    }

                    foreach (SkillEffectDefinition effect in node.Effects)
                    {
                        if (effect.Type.Equals("UnlockRecipe", StringComparison.OrdinalIgnoreCase))
                        {
                            entries.Add((tree.DisplayName,
                                $"{node.DisplayName}: unlocks {effect.Code ?? "a special recipe"}"));
                        }
                        else
                        {
                            double value = effect.ValuePerRank * rank;
                            entries.Add((tree.DisplayName,
                                $"{node.DisplayName}: {FormatStatEffect(effect, value)}"));
                        }
                    }
                }
            }

            foreach (HiddenClassDefinition hidden in HiddenClassCatalog.All)
            {
                if (!HiddenClassData.IsUnlocked(entity, hidden.Id))
                {
                    continue;
                }

                foreach (SkillEffectDefinition effect in hidden.Effects)
                {
                    entries.Add((hidden.DisplayName,
                        FormatStatEffect(effect, effect.ValuePerRank)));
                }
            }

            double leftX = margin + 22;
            double rightX = width / 2 + 8;
            double columnWidth = width / 2 - margin - 34;
            double leftY = margin + 86;
            double rightY = margin + 86;
            string? lastLeftGroup = null;
            string? lastRightGroup = null;

            for (int index = 0; index < entries.Count; index++)
            {
                bool right = index % 2 == 1;
                double x = right ? rightX : leftX;
                double y = right ? rightY : leftY;
                string? lastGroup = right ? lastRightGroup : lastLeftGroup;

                if (!string.Equals(lastGroup, entries[index].Group, StringComparison.Ordinal))
                {
                    DrawText(ctx, sectionFont, entries[index].Group.ToUpperInvariant(), x, y);
                    y += 24;
                    if (right) lastRightGroup = entries[index].Group;
                    else lastLeftGroup = entries[index].Group;
                }

                double used = DrawWrappedText(ctx, smallFont,
                    "• " + entries[index].Text,
                    x, y, columnWidth);
                y += Math.Max(24, used + 8);

                if (right) rightY = y;
                else leftY = y;
            }

            if (entries.Count == 0)
            {
                DrawText(ctx, bodyFont, "No skill bonuses have been learned yet.", leftX, leftY);
            }
        }

        private void DrawHiddenClassesTab(
            Context ctx,
            double width,
            double height)
        {
            const double margin = 14;
            ThemePalette theme = GetTheme(CurrentTree);
            DrawPanel(ctx, margin, margin, width - margin * 2, height - margin * 2,
                0.045, 0.055, 0.08, 0.99,
                0.38, 0.28, 0.55, 0.95);

            DrawText(ctx, titleFont, "HIDDEN CLASS DISCOVERIES", margin + 20, margin + 16);
            DrawText(ctx, smallFont,
                "Capstone combinations reveal powerful hybrid classes. Undiscovered classes remain secret.",
                margin + 20, margin + 50);

            Entity? entity = api.World?.Player?.Entity;
            // Four columns keep all eight discoveries visible in the restored
            // 1120 x 682 compact window.
            int columns = width >= 1000 ? 4 : 3;
            double gap = 14;
            double cardWidth = (width - margin * 2 - 40 - gap * (columns - 1)) / columns;
            double cardHeight = 210;
            double startX = margin + 20;
            double startY = margin + 84;

            for (int index = 0; index < HiddenClassCatalog.All.Count; index++)
            {
                HiddenClassDefinition definition = HiddenClassCatalog.All[index];
                int column = index % columns;
                int row = index / columns;
                double x = startX + column * (cardWidth + gap);
                double y = startY + row * (cardHeight + gap);
                bool unlocked = entity != null && HiddenClassData.IsUnlocked(entity, definition.Id);

                RoundRectangle(ctx, x, y, cardWidth, cardHeight, 12);
                SetColor(ctx,
                    unlocked ? 0.12 : 0.055,
                    unlocked ? 0.09 : 0.06,
                    unlocked ? 0.18 : 0.08,
                    0.98);
                ctx.FillPreserve();
                SetColor(ctx,
                    unlocked ? 0.72 : 0.22,
                    unlocked ? 0.50 : 0.24,
                    unlocked ? 0.95 : 0.30,
                    unlocked ? 0.95 : 0.75);
                ctx.LineWidth = unlocked ? 2.5 : 1.5;
                ctx.Stroke();

                if (!unlocked)
                {
                    DrawCenteredText(ctx, titleFont, "?", x + cardWidth / 2, y + 40,
                        0.62, 0.58, 0.72, 1);
                    DrawCenteredText(ctx, sectionFont, "UNDISCOVERED", x + cardWidth / 2, y + 96);
                    DrawCenteredText(ctx, smallFont, "Requirements hidden", x + cardWidth / 2, y + 130);
                    continue;
                }

                DrawHiddenClassIcon(
                    ctx,
                    definition.Id,
                    x + cardWidth / 2,
                    y + 34,
                    34,
                    0.94,
                    0.78,
                    1.0,
                    1
                );

                DrawCenteredText(ctx, titleFont, definition.DisplayName,
                    x + cardWidth / 2, y + 66);
                double cursorY = y + 98;
                cursorY += DrawWrappedText(ctx, smallFont, definition.Description,
                    x + 14, cursorY, cardWidth - 28) + 8;

                string requirements = string.Join(" + ", definition.RequiredClasses.Select(classId =>
                    skillConfig.Trees.TryGetValue(classId, out SkillTreeDefinition? tree)
                        ? tree.DisplayName
                        : classId));
                cursorY += DrawWrappedText(ctx, tinyFont,
                    "Discovered from: " + requirements,
                    x + 14, cursorY, cardWidth - 28) + 8;

                foreach (SkillEffectDefinition effect in definition.Effects)
                {
                    cursorY += DrawWrappedText(ctx, smallFont,
                        "• " + FormatStatEffect(effect, effect.ValuePerRank),
                        x + 14, cursorY, cardWidth - 28) + 5;
                }
            }
        }

        private static void DrawHiddenClassIcon(
            Context ctx,
            string hiddenClassId,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();
            try
            {
                ctx.Translate(centerX, centerY);
                ctx.Scale(size, size);
                ctx.LineWidth = 0.075;
                SetColor(ctx, r, g, b, a);

                switch (hiddenClassId)
                {
                    case "lumberjack":
                        DrawLogGlyph(ctx);
                        DrawAxeGlyph(ctx);
                        break;
                    case "weaponmaster":
                        DrawCrossedSwordsGlyph(ctx);
                        DrawAnvilGlyph(ctx);
                        break;
                    case "shadowarcher":
                        DrawEyeGlyph(ctx);
                        StrokeLine(ctx, -0.34, 0.24, 0.34, -0.24);
                        break;
                    case "berserk":
                        DrawHelmetGlyph(ctx);
                        DrawFlameGlyph(ctx);
                        break;
                    case "deepwarden":
                        DrawShieldGlyph(ctx);
                        DrawPickaxeGlyph(ctx);
                        break;
                    case "wildheart":
                        DrawHeartGlyph(ctx);
                        DrawPawGlyph(ctx);
                        break;
                    case "grandartisan":
                        DrawGearGlyph(ctx);
                        DrawHammerGlyph(ctx);
                        break;
                    case "stormforged":
                        DrawRangerGlyph(ctx);
                        DrawBeeGlyph(ctx);
                        break;
                    default:
                        DrawStarGlyph(ctx, false);
                        break;
                }
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static string FormatStatEffect(
            SkillEffectDefinition effect,
            double value)
        {
            string name = string.IsNullOrWhiteSpace(effect.Stat)
                ? effect.Type
                : effect.Stat;

            if (effect.Type.Equals("MaxHealth", StringComparison.OrdinalIgnoreCase) ||
                effect.Type.Equals("TrackingRange", StringComparison.OrdinalIgnoreCase))
            {
                return $"{name}: +{value:0.##}";
            }

            if (effect.Type.Contains("Reduction", StringComparison.OrdinalIgnoreCase))
            {
                return $"{name}: -{value * 100:0.##}%";
            }

            if (effect.Type.Equals("CheatDeath", StringComparison.OrdinalIgnoreCase))
            {
                return $"{name}: restores {value * 100:0.##}% health, 48-hour cooldown";
            }

            if (effect.Type.Equals("ShadowArrowCrit", StringComparison.OrdinalIgnoreCase))
            {
                return $"{name}: {value * 100:0}%";
            }

            return $"{name}: +{value * 100:0.##}%";
        }

        private void DrawBackground(
            Context ctx,
            double width,
            double height)
        {
            ctx.SetSourceRGBA(
                0.035,
                0.045,
                0.065,
                0.98
            );

            ctx.Rectangle(
                0,
                0,
                width,
                height
            );

            ctx.Fill();

            // Subtle layered background.
            ctx.SetSourceRGBA(
                0.11,
                0.12,
                0.15,
                0.24
            );

            for (int index = 0;
                 index < 7;
                 index++)
            {
                double inset =
                    index * 18;

                RoundRectangle(
                    ctx,
                    inset,
                    inset,
                    width -
                        inset * 2,
                    height -
                        inset * 2,
                    18
                );

                ctx.Stroke();
            }
        }

        private void DrawHeader(
            Context ctx,
            double x,
            double y,
            double width,
            double height)
        {
            ThemePalette theme =
                GetTheme(
                    CurrentTree
                );

            DrawPanel(
                ctx,
                x,
                y,
                width,
                height,
                theme.PanelR,
                theme.PanelG,
                theme.PanelB,
                0.95,
                theme.AccentR,
                theme.AccentG,
                theme.AccentB,
                0.78
            );

            double iconSize =
                height -
                20;

            double iconX =
                x +
                12;

            double iconY =
                y +
                10;

            DrawClassIcon(
                ctx,
                CurrentTree,
                iconX,
                iconY,
                iconSize,
                theme
            );

            double titleX =
                iconX +
                iconSize +
                14;

            DrawFittedText(
                ctx,
                titleFont,
                CurrentTree.DisplayName,
                titleX,
                y + 10,
                Math.Max(
                    120,
                    x + width - 350 - titleX
                )
            );

            Entity? entity =
                api.World?
                    .Player?
                    .Entity;

            int level =
                entity == null
                    ? 1
                    : ExpMath.GetLevel(
                        ProgressionData
                            .GetExperience(
                                entity,
                                CurrentTree.ClassId
                            )
                    );

            int mastery =
                Math.Clamp(
                    level - 1,
                    0,
                    MasteryCap
                );

            int available =
                entity == null
                    ? 0
                    : GetAvailablePoints(
                        CurrentTree
                    );

            int spent =
                entity == null
                    ? 0
                    : GetSpentPoints(
                        CurrentTree
                    );

            DrawBadge(
                ctx,
                x + width - 330,
                y + 13,
                92,
                27,
                $"LEVEL {level}",
                theme
            );

            DrawBadge(
                ctx,
                x + width - 228,
                y + 13,
                102,
                27,
                $"POINTS {available}",
                theme
            );

            DrawBadge(
                ctx,
                x + width - 116,
                y + 13,
                102,
                27,
                $"SPENT {spent}",
                theme
            );

            double totalExperience =
                entity == null
                    ? 0
                    : ProgressionData
                        .GetExperience(
                            entity,
                            CurrentTree.ClassId
                        );

            double levelStart =
                ExpMath.GetLevelStartExp(
                    level
                );

            double levelEnd =
                ExpMath.GetLevelEndExp(
                    level
                );

            double intoLevel =
                Math.Max(
                    0,
                    totalExperience -
                    levelStart
                );

            double required =
                Math.Max(
                    1,
                    levelEnd -
                    levelStart
                );

            double progress =
                Math.Clamp(
                    intoLevel /
                    required,
                    0,
                    1
                );

            double barX =
                titleX;

            double barY =
                y +
                height -
                22;

            const double xpCounterWidth =
                210;

            double barWidth =
                width -
                (
                    barX -
                    x
                ) -
                16 -
                xpCounterWidth;

            DrawProgressBar(
                ctx,
                barX,
                barY,
                barWidth,
                10,
                progress,
                theme
            );

            DrawFittedText(
                ctx,
                tinyFont,
                $"XP {intoLevel:0.##} / {required:0.##}",
                barX +
                    barWidth +
                    12,
                barY - 2,
                xpCounterWidth -
                    12
            );
        }

        private void DrawSidebar(
            Context ctx,
            double x,
            double y,
            double width,
            double height)
        {
            ThemePalette theme =
                GetTheme(
                    CurrentTree
                );

            DrawPanel(
                ctx,
                x,
                y,
                width,
                height,
                0.07,
                0.08,
                0.11,
                0.97,
                0.20,
                0.22,
                0.27,
                0.9
            );

            DrawText(
                ctx,
                sectionFont,
                "PROFESSIONS",
                x + 15,
                y + 13
            );

            double categoryY =
                y +
                45;

            foreach (
                SkillTreeCategory category
                in categories)
            {
                bool selected =
                    category.Id.Equals(
                        selectedCategoryId,
                        StringComparison.OrdinalIgnoreCase
                    );

                bool hovered =
                    IsHovered(
                        SkillTreeHitKind.Category,
                        category.Id
                    );

                DrawSidebarEntry(
                    ctx,
                    x + 11,
                    categoryY,
                    width - 22,
                    34,
                    category.Id,
                    category.DisplayName,
                    selected,
                    hovered,
                    theme
                );

                hitRegions.Add(
                    new SkillTreeHitRegion(
                        SkillTreeHitKind.Category,
                        category.Id,
                        x + 11,
                        categoryY,
                        width - 22,
                        34
                    )
                );

                categoryY +=
                    39;
            }

            DrawDivider(
                ctx,
                x + 12,
                categoryY + 4,
                width - 24
            );

            SkillTreeCategory selectedCategory =
                categories.First(
                    category =>
                        category.Id.Equals(
                            selectedCategoryId,
                            StringComparison.OrdinalIgnoreCase
                        )
                );

            double classY =
                categoryY +
                18;

            foreach (
                string classId
                in selectedCategory.ClassIds)
            {
                SkillTreeDefinition tree =
                    treesById[classId];

                bool selected =
                    classId.Equals(
                        selectedClassId,
                        StringComparison.OrdinalIgnoreCase
                    );

                bool hovered =
                    IsHovered(
                        SkillTreeHitKind.Class,
                        classId
                    );

                Entity? entity =
                    api.World?
                        .Player?
                        .Entity;

                int level =
                    entity == null
                        ? 1
                        : ExpMath.GetLevel(
                            ProgressionData.GetExperience(
                                entity,
                                classId
                            )
                        );

                DrawClassEntry(
                    ctx,
                    x + 11,
                    classY,
                    width - 22,
                    39,
                    tree,
                    level,
                    selected,
                    hovered,
                    theme
                );

                hitRegions.Add(
                    new SkillTreeHitRegion(
                        SkillTreeHitKind.Class,
                        classId,
                        x + 11,
                        classY,
                        width - 22,
                        39
                    )
                );

                classY +=
                    44;
            }
        }

        private void DrawTreePanel(
            Context ctx,
            double x,
            double y,
            double width,
            double height)
        {
            ThemePalette theme =
                GetTheme(
                    CurrentTree
                );

            DrawPanel(
                ctx,
                x,
                y,
                width,
                height,
                0.045,
                0.055,
                0.075,
                0.985,
                0.15,
                0.17,
                0.22,
                0.95
            );

            hitRegions.Add(
                new SkillTreeHitRegion(
                    SkillTreeHitKind.TreeArea,
                    "tree",
                    x,
                    y,
                    width,
                    height
                )
            );

            DrawText(
                ctx,
                sectionFont,
                "SKILL TREE",
                x + 16,
                y + 13
            );

            DrawText(
                ctx,
                tinyFont,
                $"Zoom {zoom * 100:0}%",
                x + width - 104,
                y + 16
            );

            const double resetWidth = 104;

            bool resetHovered =
                IsHovered(
                    SkillTreeHitKind.ResetView,
                    "reset"
                );

            DrawMiniButton(
                ctx,
                x + width -
                    resetWidth -
                    12,
                y + 39,
                resetWidth,
                24,
                "RESET VIEW",
                resetHovered,
                true,
                theme
            );

            hitRegions.Add(
                new SkillTreeHitRegion(
                    SkillTreeHitKind.ResetView,
                    "reset",
                    x + width -
                        resetWidth -
                        12,
                    y + 39,
                    resetWidth,
                    24
                )
            );

            BuildNodeLayouts(
                x,
                y,
                width,
                height
            );

            DrawConnections(
                ctx,
                theme
            );

            foreach (
                SkillNodeDefinition node
                in CurrentTree.Nodes)
            {
                if (!nodeLayouts.TryGetValue(
                    node.Id,
                    out SkillNodeLayout? layout))
                {
                    continue;
                }

                NodeEvaluation evaluation =
                    EvaluateNode(
                        CurrentTree,
                        node
                    );

                DrawNode(
                    ctx,
                    layout,
                    evaluation,
                    theme
                );

                hitRegions.Add(
                    new SkillTreeHitRegion(
                        SkillTreeHitKind.Node,
                        node.Id,
                        layout.X -
                            layout.Radius -
                            8,
                        layout.Y -
                            layout.Radius -
                            8,
                        (
                            layout.Radius +
                            8
                        ) * 2,
                        (
                            layout.Radius +
                            8
                        ) * 2
                    )
                );
            }
        }

        private void BuildNodeLayouts(
            double x,
            double y,
            double width,
            double height)
        {
            double contentX =
                x +
                28;

            double contentY =
                y +
                70;

            double contentWidth =
                width -
                56;

            double contentHeight =
                height -
                95;

            double centerX =
                contentX +
                contentWidth / 2;

            double centerY =
                contentY +
                contentHeight / 2;

            double[] columnPositions =
            {
                0.17,
                0.50,
                0.83
            };

            foreach (
                SkillNodeDefinition node
                in CurrentTree.Nodes)
            {
                int column =
                    Math.Clamp(
                        node.Column,
                        0,
                        2
                    );

                int row =
                    Math.Clamp(
                        node.Row,
                        0,
                        4
                    );

                double baseX =
                    contentX +
                    contentWidth *
                    columnPositions[column];

                double baseY =
                    contentY +
                    36 +
                    (
                        contentHeight -
                        72
                    ) *
                    row /
                    4.0;

                double transformedX =
                    centerX +
                    (
                        baseX -
                        centerX
                    ) *
                    zoom +
                    panX;

                double transformedY =
                    centerY +
                    (
                        baseY -
                        centerY
                    ) *
                    zoom +
                    panY;

                double radius =
                    (
                        node.Capstone
                            ? 42
                            : node.Row >= 2
                                ? 34
                                : 31
                    ) *
                    zoom;

                nodeLayouts[node.Id] =
                    new SkillNodeLayout(
                        node,
                        transformedX,
                        transformedY,
                        radius
                    );
            }
        }

        private void DrawConnections(
            Context ctx,
            ThemePalette theme)
        {
            foreach (
                SkillNodeDefinition child
                in CurrentTree.Nodes)
            {
                if (!nodeLayouts.TryGetValue(
                        child.Id,
                        out SkillNodeLayout? childLayout))
                {
                    continue;
                }

                List<string> parents =
                    child.Requires
                        .Concat(
                            child.RequiresAny
                        )
                        .Distinct(
                            StringComparer.OrdinalIgnoreCase
                        )
                        .ToList();

                foreach (
                    string parentId
                    in parents)
                {
                    if (!nodeLayouts.TryGetValue(
                            parentId,
                            out SkillNodeLayout? parentLayout))
                    {
                        continue;
                    }

                    int parentRank =
                        GetNodeRank(
                            parentId
                        );

                    int childRank =
                        GetNodeRank(
                            child.Id
                        );

                    if (childRank > 0)
                    {
                        SetColor(
                            ctx,
                            theme.SuccessR,
                            theme.SuccessG,
                            theme.SuccessB,
                            0.95
                        );
                    }
                    else if (parentRank > 0)
                    {
                        SetColor(
                            ctx,
                            theme.AccentR,
                            theme.AccentG,
                            theme.AccentB,
                            0.40
                        );
                    }
                    else
                    {
                        SetColor(
                            ctx,
                            0.23,
                            0.25,
                            0.30,
                            0.78
                        );
                    }

                    ctx.LineWidth = 2.2;

                    double midY =
                        (
                            parentLayout.Y +
                            childLayout.Y
                        ) /
                        2;

                    ctx.NewPath();

                    ctx.MoveTo(
                        parentLayout.X,
                        parentLayout.Y +
                            parentLayout.Radius
                    );

                    ctx.CurveTo(
                        parentLayout.X,
                        midY,
                        childLayout.X,
                        midY,
                        childLayout.X,
                        childLayout.Y -
                            childLayout.Radius
                    );

                    ctx.Stroke();
                }
            }
        }

        private void DrawNode(
            Context ctx,
            SkillNodeLayout layout,
            NodeEvaluation evaluation,
            ThemePalette theme)
        {
            ctx.NewPath();

            SkillNodeDefinition node =
                layout.Node;

            bool selected =
                node.Id.Equals(
                    selectedNodeId,
                    StringComparison.OrdinalIgnoreCase
                );

            bool hovered =
                IsHovered(
                    SkillTreeHitKind.Node,
                    node.Id
                );

            (double fillR,
             double fillG,
             double fillB,
             double borderR,
             double borderG,
             double borderB) =
                GetNodeColors(
                    evaluation.State,
                    theme
                );

            double radius =
                layout.Radius;

            if (hovered)
            {
                radius += 4;
            }

            if (selected)
            {
                DrawGlow(
                    ctx,
                    layout.X,
                    layout.Y,
                    radius + 9,
                    theme.AccentR,
                    theme.AccentG,
                    theme.AccentB,
                    0.30
                );
            }
            else if (evaluation.State ==
                     SkillNodeVisualState.Available)
            {
                DrawGlow(
                    ctx,
                    layout.X,
                    layout.Y,
                    radius + 6,
                    theme.AccentR,
                    theme.AccentG,
                    theme.AccentB,
                    0.18
                );
            }

            DrawNodeShape(
                ctx,
                node,
                layout.X,
                layout.Y,
                radius
            );

            SetColor(
                ctx,
                fillR,
                fillG,
                fillB,
                0.96
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                selected
                    ? 0.98
                    : borderR,
                selected
                    ? 0.93
                    : borderG,
                selected
                    ? 0.72
                    : borderB,
                1
            );

            ctx.LineWidth =
                selected
                    ? 4
                    : hovered
                        ? 3
                        : 2;

            ctx.Stroke();

            // Cairo paths are not part of Save/Restore state. Clear any
            // unfinished path before and after icon rendering so one node's
            // vector path cannot connect to a later node as a stray line.
            ctx.NewPath();

            DrawIsolatedSkillNodeIcon(
                ctx,
                CurrentTree.ClassId,
                node,
                layout.X,
                layout.Y - 5,
                Math.Min(
                    36,
                    radius * 1.18
                ),
                0.98,
                0.95,
                0.88,
                1
            );

            ctx.NewPath();

            int rank =
                GetNodeRank(
                    node.Id
                );

            DrawCenteredText(
                ctx,
                tinyFont,
                $"{rank}/{node.MaxRank}",
                layout.X,
                layout.Y + 10
            );

            string label =
                Shorten(
                    node.DisplayName,
                    20
                );

            double labelY =
                layout.Y +
                radius +
                8;

            // The two specialization labels sit directly on the vertical
            // connector leading to their expert nodes. Raise them slightly;
            // also lift capstone labels so long names stay inside the panel.
            if (node.Id.Equals(
                    "path-a",
                    StringComparison.OrdinalIgnoreCase) ||
                node.Id.Equals(
                    "path-b",
                    StringComparison.OrdinalIgnoreCase))
            {
                labelY -= 8;
            }
            else if (node.Capstone)
            {
                labelY -= 8;
            }

            // Connections are rendered first, but their strokes can still be
            // visible through glyph gaps. A compact panel-colored backing
            // keeps every node label legible without altering the tree lines.
            double labelWidth =
                MeasureTextWidth(
                    tinyFont,
                    label
                );

            RoundRectangle(
                ctx,
                layout.X -
                    labelWidth / 2 -
                    4,
                labelY - 1,
                labelWidth + 8,
                17,
                3
            );

            SetColor(
                ctx,
                0.045,
                0.055,
                0.075,
                0.94
            );

            ctx.Fill();

            DrawCenteredText(
                ctx,
                tinyFont,
                label,
                layout.X,
                labelY,
                evaluation.State ==
                    SkillNodeVisualState.Locked
                    ? 0.50
                    : 0.84,
                evaluation.State ==
                    SkillNodeVisualState.Locked
                    ? 0.52
                    : 0.87,
                evaluation.State ==
                    SkillNodeVisualState.Locked
                    ? 0.57
                    : 0.91,
                1
            );

            if (node.Capstone)
            {
                DrawCenteredText(
                    ctx,
                    tinyFont,
                    "CAPSTONE",
                    layout.X,
                    layout.Y -
                        radius -
                        19,
                    theme.AccentR,
                    theme.AccentG,
                    theme.AccentB,
                    1
                );
            }

            ctx.NewPath();
        }

        private void DrawDetailsPanel(
            Context ctx,
            double x,
            double y,
            double width,
            double height)
        {
            ThemePalette theme =
                GetTheme(
                    CurrentTree
                );

            DrawPanel(
                ctx,
                x,
                y,
                width,
                height,
                0.065,
                0.075,
                0.105,
                0.985,
                0.17,
                0.19,
                0.24,
                0.96
            );

            SkillNodeDefinition? displayNode =
                HoveredNode ??
                CurrentNode;

            DrawText(
                ctx,
                sectionFont,
                displayNode?.Capstone == true
                    ? "CAPSTONE DETAILS"
                    : "NODE DETAILS",
                x + 17,
                y + 15
            );

            if (displayNode == null)
            {
                DrawText(
                    ctx,
                    bodyFont,
                    "No node selected.",
                    x + 17,
                    y + 53
                );

                return;
            }

            NodeEvaluation evaluation =
                EvaluateNode(
                    CurrentTree,
                    displayNode
                );

            double cursorY =
                y +
                54;

            DrawFittedText(
                ctx,
                titleFont,
                displayNode.DisplayName,
                x + 17,
                cursorY,
                width - 34
            );

            cursorY +=
                39;

            int rank =
                GetNodeRank(
                    displayNode.Id
                );

            DrawStatePill(
                ctx,
                x + 17,
                cursorY,
                width - 34,
                28,
                evaluation,
                rank,
                displayNode,
                theme
            );

            cursorY +=
                43;

            cursorY +=
                DrawWrappedText(
                    ctx,
                    bodyFont,
                    displayNode.Description,
                    x + 17,
                    cursorY,
                    width - 34
                );

            cursorY +=
                18;

            DrawText(
                ctx,
                sectionFont,
                "EFFECTS",
                x + 17,
                cursorY
            );

            cursorY +=
                27;

            if (displayNode.Effects.Count == 0)
            {
                DrawText(
                    ctx,
                    smallFont,
                    "No configured effect.",
                    x + 17,
                    cursorY
                );

                cursorY +=
                    22;
            }
            else
            {
                foreach (
                    SkillEffectDefinition effect
                    in displayNode.Effects)
                {
                    string effectText =
                        DescribeEffect(
                            effect,
                            rank,
                            displayNode.MaxRank
                        );

                    cursorY +=
                        DrawRequirementLine(
                            ctx,
                            x + 17,
                            cursorY,
                            width - 34,
                            true,
                            effectText,
                            theme
                        );
                }
            }

            cursorY +=
                8;

            DrawText(
                ctx,
                sectionFont,
                "REQUIREMENTS",
                x + 17,
                cursorY
            );

            cursorY +=
                27;

            foreach (
                RequirementView requirement
                in BuildRequirements(
                    CurrentTree,
                    displayNode
                ))
            {
                cursorY +=
                    DrawRequirementLine(
                        ctx,
                        x + 17,
                        cursorY,
                        width - 34,
                        requirement.Met,
                        requirement.Text,
                        theme
                    );
            }

            double buttonHeight =
                44;

            double buttonX =
                x +
                17;

            double buttonY =
                y +
                height -
                94;

            double buttonWidth =
                width -
                34;

            bool purchaseHovered =
                IsHovered(
                    SkillTreeHitKind.Purchase,
                    "purchase"
                );

            DrawPurchaseButton(
                ctx,
                buttonX,
                buttonY,
                buttonWidth,
                buttonHeight,
                displayNode,
                evaluation,
                purchaseHovered,
                theme
            );

            hitRegions.Add(
                new SkillTreeHitRegion(
                    SkillTreeHitKind.Purchase,
                    "purchase",
                    buttonX,
                    buttonY,
                    buttonWidth,
                    buttonHeight
                )
            );

            DrawStatusBar(
                ctx,
                x + 17,
                y + height - 39,
                width - 34,
                24,
                statusMessage,
                theme
            );
        }

        private void DrawPurchaseButton(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            SkillNodeDefinition node,
            NodeEvaluation evaluation,
            bool hovered,
            ThemePalette theme)
        {
            int rank =
                GetNodeRank(
                    node.Id
                );

            string label;

            if (purchasePending)
            {
                label =
                    "WAITING FOR SERVER...";
            }
            else if (rank >=
                node.MaxRank)
            {
                label =
                    "MAXIMUM RANK";
            }
            else if (evaluation.CanPurchase)
            {
                label =
                    node.Capstone
                        ? $"UNLOCK CAPSTONE - {node.Cost} PTS"
                        : $"LEARN RANK {rank + 1} - {node.Cost} PTS";
            }
            else
            {
                label =
                    "LOCKED";
            }

            bool enabled =
                evaluation.CanPurchase &&
                !purchasePending;

            double fillR =
                enabled
                    ? theme.AccentR
                    : 0.16;

            double fillG =
                enabled
                    ? theme.AccentG
                    : 0.17;

            double fillB =
                enabled
                    ? theme.AccentB
                    : 0.20;

            if (hovered &&
                enabled)
            {
                fillR =
                    Math.Min(
                        1,
                        fillR +
                        0.12
                    );

                fillG =
                    Math.Min(
                        1,
                        fillG +
                        0.12
                    );

                fillB =
                    Math.Min(
                        1,
                        fillB +
                        0.12
                    );
            }

            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                9
            );

            SetColor(
                ctx,
                fillR,
                fillG,
                fillB,
                enabled
                    ? 0.88
                    : 0.72
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                enabled
                    ? 0.98
                    : 0.35,
                enabled
                    ? 0.93
                    : 0.37,
                enabled
                    ? 0.74
                    : 0.41,
                1
            );

            ctx.LineWidth =
                hovered
                    ? 3
                    : 2;

            ctx.Stroke();

            DrawCenteredText(
                ctx,
                sectionFont,
                label,
                x +
                    width / 2,
                y +
                    12,
                enabled
                    ? 0.06
                    : 0.64,
                enabled
                    ? 0.07
                    : 0.66,
                enabled
                    ? 0.09
                    : 0.70,
                1
            );
        }

        private NodeEvaluation EvaluateNode(
            SkillTreeDefinition tree,
            SkillNodeDefinition node)
        {
            Entity? entity =
                api.World?
                    .Player?
                    .Entity;

            if (entity == null)
            {
                return new NodeEvaluation(
                    SkillNodeVisualState.Locked,
                    false,
                    "Player data is not available."
                );
            }

            int level =
                ExpMath.GetLevel(
                    ProgressionData
                        .GetExperience(
                            entity,
                            tree.ClassId
                        )
                );

            int rank =
                GetNodeRank(
                    tree,
                    node.Id
                );

            int spent =
                GetSpentPoints(
                    tree
                );

            int available =
                GetAvailablePoints(
                    tree
                );

            if (rank >=
                node.MaxRank)
            {
                return new NodeEvaluation(
                    SkillNodeVisualState.Maximum,
                    false,
                    "This node is already at maximum rank."
                );
            }

            if (!string.IsNullOrWhiteSpace(
                    node.ExclusiveGroup))
            {
                bool conflict =
                    tree.Nodes.Any(
                        other =>
                            !other.Id.Equals(
                                node.Id,
                                StringComparison.OrdinalIgnoreCase
                            ) &&
                            string.Equals(
                                other.ExclusiveGroup,
                                node.ExclusiveGroup,
                                StringComparison.OrdinalIgnoreCase
                            ) &&
                            GetNodeRank(
                                tree,
                                other.Id
                            ) > 0
                    );

                if (conflict)
                {
                    return new NodeEvaluation(
                        SkillNodeVisualState.ExclusiveConflict,
                        false,
                        "This specialization conflicts with your chosen path."
                    );
                }
            }

            if (level <
                node.RequiredClassLevel)
            {
                return new NodeEvaluation(
                    rank > 0
                        ? SkillNodeVisualState.Purchased
                        : SkillNodeVisualState.Locked,
                    false,
                    $"Requires class level {node.RequiredClassLevel}."
                );
            }

            if (spent <
                node.RequiredPointsSpent)
            {
                return new NodeEvaluation(
                    rank > 0
                        ? SkillNodeVisualState.Purchased
                        : SkillNodeVisualState.Locked,
                    false,
                    $"Requires {node.RequiredPointsSpent} points spent."
                );
            }

            if (node.Requires.Any(
                    required =>
                        GetNodeRank(
                            tree,
                            required
                        ) <= 0))
            {
                return new NodeEvaluation(
                    rank > 0
                        ? SkillNodeVisualState.Purchased
                        : SkillNodeVisualState.Locked,
                    false,
                    "A prerequisite node is missing."
                );
            }

            if (node.RequiresAny.Count >
                    0 &&
                !node.RequiresAny.Any(
                    required =>
                        GetNodeRank(
                            tree,
                            required
                        ) > 0))
            {
                return new NodeEvaluation(
                    rank > 0
                        ? SkillNodeVisualState.Purchased
                        : SkillNodeVisualState.Locked,
                    false,
                    "Complete one required specialization path."
                );
            }

            if (available <
                node.Cost)
            {
                return new NodeEvaluation(
                    rank > 0
                        ? SkillNodeVisualState.Purchased
                        : SkillNodeVisualState.Locked,
                    false,
                    $"Requires {node.Cost} available skill points."
                );
            }

            return new NodeEvaluation(
                rank > 0
                    ? SkillNodeVisualState.Purchased
                    : SkillNodeVisualState.Available,
                true,
                "Ready to learn."
            );
        }

        private IReadOnlyList<RequirementView>
            BuildRequirements(
                SkillTreeDefinition tree,
                SkillNodeDefinition node)
        {
            Entity? entity =
                api.World?
                    .Player?
                    .Entity;

            if (entity == null)
            {
                return new[]
                {
                    new RequirementView(
                        false,
                        "Player data unavailable"
                    )
                };
            }

            int level =
                ExpMath.GetLevel(
                    ProgressionData.GetExperience(
                        entity,
                        tree.ClassId
                    )
                );

            int spent =
                GetSpentPoints(
                    tree
                );

            int available =
                GetAvailablePoints(
                    tree
                );

            var requirements =
                new List<RequirementView>
                {
                    new(
                        level >=
                            node.RequiredClassLevel,
                        $"Class level {node.RequiredClassLevel} " +
                        $"(current {level})"
                    ),

                    new(
                        spent >=
                            node.RequiredPointsSpent,
                        $"{node.RequiredPointsSpent} points spent " +
                        $"(current {spent})"
                    ),

                    new(
                        available >=
                            node.Cost,
                        $"{node.Cost} available points " +
                        $"(current {available})"
                    )
                };

            foreach (
                string required
                in node.Requires)
            {
                SkillNodeDefinition? requiredNode =
                    tree.Nodes.FirstOrDefault(
                        candidate =>
                            candidate.Id.Equals(
                                required,
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

                requirements.Add(
                    new RequirementView(
                        GetNodeRank(required) >
                            0,
                        requiredNode?.DisplayName ??
                            required
                    )
                );
            }

            if (node.RequiresAny.Count >
                0)
            {
                bool anyMet =
                    node.RequiresAny.Any(
                        required =>
                            GetNodeRank(
                                required
                            ) > 0
                    );

                string names =
                    string.Join(
                        " or ",
                        node.RequiresAny.Select(
                            required =>
                                tree.Nodes.FirstOrDefault(
                                    candidate =>
                                        candidate.Id.Equals(
                                            required,
                                            StringComparison.OrdinalIgnoreCase
                                        )
                                )?.DisplayName ??
                                required
                        )
                    );

                requirements.Add(
                    new RequirementView(
                        anyMet,
                        names
                    )
                );
            }

            if (!string.IsNullOrWhiteSpace(
                    node.ExclusiveGroup))
            {
                bool conflict =
                    tree.Nodes.Any(
                        other =>
                            !other.Id.Equals(
                                node.Id,
                                StringComparison.OrdinalIgnoreCase
                            ) &&
                            string.Equals(
                                other.ExclusiveGroup,
                                node.ExclusiveGroup,
                                StringComparison.OrdinalIgnoreCase
                            ) &&
                            GetNodeRank(
                                other.Id
                            ) > 0
                    );

                requirements.Add(
                    new RequirementView(
                        !conflict,
                        "No conflicting specialization"
                    )
                );
            }

            return requirements;
        }

        private static string BuildNodeCacheKey(
            string classId,
            string nodeId)
        {
            return $"{classId}/{nodeId}";
        }

        private int GetNodeRank(
            string nodeId)
        {
            return GetNodeRank(
                CurrentTree,
                nodeId
            );
        }

        private int GetNodeRank(
            SkillTreeDefinition tree,
            string nodeId)
        {
            Entity? entity =
                api.World?
                    .Player?
                    .Entity;

            int watchedRank =
                entity == null
                    ? 0
                    : SkillTreeData.GetNodeRank(
                        entity,
                        tree.ClassId,
                        nodeId
                    );

            string cacheKey =
                BuildNodeCacheKey(
                    tree.ClassId,
                    nodeId
                );

            if (!purchaseRankOverrides.TryGetValue(
                    cacheKey,
                    out int authoritativeRank))
            {
                return watchedRank;
            }

            if (watchedRank >= authoritativeRank)
            {
                purchaseRankOverrides.Remove(cacheKey);
                return watchedRank;
            }

            return authoritativeRank;
        }

        private int GetSpentPoints(
            SkillTreeDefinition tree)
        {
            int spent = 0;

            foreach (SkillNodeDefinition node in tree.Nodes)
            {
                spent +=
                    GetNodeRank(tree, node.Id) *
                    node.Cost;
            }

            return spent;
        }

        private int GetAvailablePoints(
            SkillTreeDefinition tree)
        {
            Entity? entity =
                api.World?
                    .Player?
                    .Entity;

            if (entity == null)
            {
                return 0;
            }

            int watchedAvailable =
                SkillTreeData.GetAvailablePoints(
                    entity,
                    tree
                );

            if (!purchaseAvailablePointOverrides.TryGetValue(
                    tree.ClassId,
                    out int authoritativeAvailable))
            {
                return Math.Max(
                    0,
                    SkillTreeData.GetEarnedPoints(
                        entity,
                        tree.ClassId
                    ) -
                    GetSpentPoints(tree)
                );
            }

            if (watchedAvailable == authoritativeAvailable)
            {
                purchaseAvailablePointOverrides.Remove(
                    tree.ClassId
                );

                return watchedAvailable;
            }

            return authoritativeAvailable;
        }

        private bool IsNodeOnSelectedPath(
            string parentId,
            string childId)
        {
            SkillNodeDefinition? selected =
                CurrentNode;

            if (selected == null)
            {
                return false;
            }

            if (selected.Id.Equals(
                    childId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return selected.Requires.Contains(
                       childId,
                       StringComparer.OrdinalIgnoreCase
                   ) ||
                   selected.RequiresAny.Contains(
                       childId,
                       StringComparer.OrdinalIgnoreCase
                   ) ||
                   selected.Requires.Contains(
                       parentId,
                       StringComparer.OrdinalIgnoreCase
                   ) ||
                   selected.RequiresAny.Contains(
                       parentId,
                       StringComparer.OrdinalIgnoreCase
                   );
        }

        private SkillTreeDefinition CurrentTree
        {
            get
            {
                if (treesById.TryGetValue(
                    selectedClassId,
                    out SkillTreeDefinition? tree))
                {
                    return tree;
                }

                return treesById.Values.First();
            }
        }

        private SkillNodeDefinition? CurrentNode
        {
            get
            {
                SkillNodeDefinition? node =
                    CurrentTree.Nodes.FirstOrDefault(
                        candidate =>
                            candidate.Id.Equals(
                                selectedNodeId,
                                StringComparison.OrdinalIgnoreCase
                            )
                    );

                return node ??
                    CurrentTree.Nodes
                        .FirstOrDefault();
            }
        }

        private SkillNodeDefinition? HoveredNode
        {
            get
            {
                if (hoveredRegionKey == null ||
                    !hoveredRegionKey.StartsWith(
                        $"{SkillTreeHitKind.Node}:",
                        StringComparison.Ordinal))
                {
                    return null;
                }

                string nodeId =
                    hoveredRegionKey.Substring(
                        $"{SkillTreeHitKind.Node}:".Length
                    );

                return CurrentTree.Nodes.FirstOrDefault(
                    node =>
                        node.Id.Equals(
                            nodeId,
                            StringComparison.OrdinalIgnoreCase
                        )
                );
            }
        }

        private bool IsHovered(
            SkillTreeHitKind kind,
            string id)
        {
            return string.Equals(
                hoveredRegionKey,
                $"{kind}:{id}",
                StringComparison.Ordinal
            );
        }

        private SkillTreeHitRegion? FindRegion(
            double x,
            double y)
        {
            double designX =
                x / Math.Max(0.01, canvasScaleX);
            double designY =
                y / Math.Max(0.01, canvasScaleY);

            for (int index = hitRegions.Count - 1; index >= 0; index--)
            {
                SkillTreeHitRegion region = hitRegions[index];
                double testY = region.Kind == SkillTreeHitKind.Tab
                    ? designY
                    : designY - 44;

                if (region.Contains(designX, testY))
                {
                    return region;
                }
            }

            return null;
        }

        private void ClampPan()
        {
            double limitX =
                Math.Max(
                    20,
                    treePanelWidth *
                    0.22
                );

            double limitY =
                Math.Max(
                    20,
                    treePanelHeight *
                    0.18
                );

            panX =
                Math.Clamp(
                    panX,
                    -limitX,
                    limitX
                );

            panY =
                Math.Clamp(
                    panY,
                    -limitY,
                    limitY
                );
        }

        private static List<SkillTreeCategory>
            BuildCategories(
                IReadOnlyDictionary<
                    string,
                    SkillTreeDefinition
                > trees)
        {
            var definitions =
                new[]
                {
                    new SkillTreeCategory(
                        "combat",
                        "Combat",
                        new[]
                        {
                            "warrior",
                            "ranger",
                            "spearman",
                            "shield",
                            "tank",
                            "hunter"
                        }
                    ),

                    new SkillTreeCategory(
                        "gathering",
                        "Gathering",
                        new[]
                        {
                            "miner",
                            "woodworker",
                            "farmer",
                            "fisher"
                        }
                    ),

                    new SkillTreeCategory(
                        "crafting",
                        "Crafting",
                        new[]
                        {
                            "builder",
                            "blacksmith",
                            "potter",
                            "leatherworker",
                            "tailor"
                        }
                    ),

                    new SkillTreeCategory(
                        "production",
                        "Production",
                        new[]
                        {
                            "cook",
                            "animalhusbandry",
                            "beekeeper"
                        }
                    )
                };

            var result =
                new List<SkillTreeCategory>();

            foreach (
                SkillTreeCategory category
                in definitions)
            {
                List<string> existing =
                    category.ClassIds
                        .Where(
                            trees.ContainsKey
                        )
                        .ToList();

                if (existing.Count ==
                    0)
                {
                    continue;
                }

                result.Add(
                    category with
                    {
                        ClassIds =
                            existing
                    }
                );
            }

            HashSet<string> included =
                result
                    .SelectMany(
                        category =>
                            category.ClassIds
                    )
                    .ToHashSet(
                        StringComparer.OrdinalIgnoreCase
                    );

            List<string> remaining =
                trees.Keys
                    .Where(
                        classId =>
                            !included.Contains(
                                classId
                            )
                    )
                    .OrderBy(
                        classId =>
                            trees[classId]
                                .DisplayName,
                        StringComparer.OrdinalIgnoreCase
                    )
                    .ToList();

            if (remaining.Count >
                0)
            {
                result.Add(
                    new SkillTreeCategory(
                        "other",
                        "Other",
                        remaining
                    )
                );
            }

            return result;
        }

        private static ThemePalette GetTheme(
            SkillTreeDefinition tree)
        {
            return tree.ClassId switch
            {
                "warrior" or
                "ranger" or
                "spearman" or
                "shield" or
                "tank" or
                "hunter" =>
                    new ThemePalette(
                        0.82,
                        0.48,
                        0.20,
                        0.18,
                        0.075,
                        0.055,
                        0.18,
                        0.76,
                        0.64
                    ),

                "miner" or
                "woodworker" or
                "farmer" or
                "fisher" =>
                    new ThemePalette(
                        0.34,
                        0.72,
                        0.44,
                        0.055,
                        0.14,
                        0.09,
                        0.22,
                        0.77,
                        0.58
                    ),

                "builder" or
                "blacksmith" or
                "potter" or
                "leatherworker" or
                "tailor" =>
                    new ThemePalette(
                        0.82,
                        0.64,
                        0.30,
                        0.15,
                        0.11,
                        0.055,
                        0.28,
                        0.74,
                        0.78
                    ),

                _ =>
                    new ThemePalette(
                        0.30,
                        0.67,
                        0.75,
                        0.055,
                        0.12,
                        0.15,
                        0.25,
                        0.78,
                        0.68
                    )
            };
        }

        private static (
            double FillR,
            double FillG,
            double FillB,
            double BorderR,
            double BorderG,
            double BorderB)
            GetNodeColors(
                SkillNodeVisualState state,
                ThemePalette theme)
        {
            return state switch
            {
                SkillNodeVisualState.Available =>
                    (
                        theme.AccentR *
                            0.45,
                        theme.AccentG *
                            0.45,
                        theme.AccentB *
                            0.45,
                        theme.AccentR,
                        theme.AccentG,
                        theme.AccentB
                    ),

                SkillNodeVisualState.Purchased =>
                    (
                        theme.SuccessR *
                            0.55,
                        theme.SuccessG *
                            0.55,
                        theme.SuccessB *
                            0.55,
                        theme.SuccessR,
                        theme.SuccessG,
                        theme.SuccessB
                    ),

                SkillNodeVisualState.Maximum =>
                    (
                        0.28,
                        0.62,
                        0.56,
                        0.55,
                        0.98,
                        0.86
                    ),

                SkillNodeVisualState.ExclusiveConflict =>
                    (
                        0.30,
                        0.075,
                        0.085,
                        0.88,
                        0.24,
                        0.27
                    ),

                _ =>
                    (
                        0.105,
                        0.115,
                        0.14,
                        0.32,
                        0.34,
                        0.39
                    )
            };
        }

        private static void DrawIsolatedSkillNodeIcon(
            Context target,
            string classId,
            SkillNodeDefinition node,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            int surfaceSize =
                Math.Max(
                    72,
                    (int)Math.Ceiling(
                        size * 3.2
                    )
                );

            using var surface =
                new ImageSurface(
                    Format.ARGB32,
                    surfaceSize,
                    surfaceSize
                );

            using var iconContext =
                new Context(surface);

            iconContext.NewPath();

            DrawSkillNodeIcon(
                iconContext,
                classId,
                node,
                surfaceSize / 2.0,
                surfaceSize / 2.0,
                size,
                r,
                g,
                b,
                a
            );

            iconContext.NewPath();
            surface.Flush();

            target.Save();

            try
            {
                target.NewPath();
                target.SetSourceSurface(
                    surface,
                    (int)Math.Round(
                        centerX - surfaceSize / 2.0
                    ),
                    (int)Math.Round(
                        centerY - surfaceSize / 2.0
                    )
                );
                target.Paint();
                target.NewPath();
            }
            finally
            {
                target.Restore();
                target.NewPath();
            }
        }

        private static void DrawSkillNodeIcon(
            Context ctx,
            string classId,
            SkillNodeDefinition node,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            if (node.Capstone ||
                node.Id.Equals(
                    "capstone",
                    StringComparison.Ordinal))
            {
                DrawProfessionIcon(
                    ctx,
                    classId,
                    centerX,
                    centerY + size * 0.05,
                    size * 0.80,
                    r,
                    g,
                    b,
                    a
                );

                DrawCrownIcon(
                    ctx,
                    centerX,
                    centerY - size * 0.34,
                    size * 0.34,
                    r,
                    g,
                    b,
                    a
                );
                return;
            }

            DrawArchetypeNodeIcon(
                ctx,
                classId,
                node.Id,
                centerX,
                centerY,
                size,
                r,
                g,
                b,
                a
            );
        }

        private static void DrawArchetypeNodeIcon(
            Context ctx,
            string classId,
            string nodeId,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size * 0.84,
                    size * 0.84
                );

                ctx.LineWidth = 0.085;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                switch (classId.ToLowerInvariant())
                {
                    case "miner":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawPickaxeGlyph(ctx);
                                break;
                            case "discipline":
                                DrawPickaxeGlyph(ctx);
                                FillCircle(ctx, 0.28, 0.05, 0.05);
                                FillCircle(ctx, 0.18, 0.18, 0.04);
                                break;
                            case "path-a":
                                DrawSurveyorGlyph(ctx);
                                break;
                            case "path-b":
                                DrawLanternGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawMasterSurveyorGlyph(ctx);
                                break;
                            case "expert-b":
                                DrawLanternGlyph(ctx);
                                StrokeLine(ctx, -0.22, 0.24, -0.10, 0.08);
                                StrokeLine(ctx, -0.03, 0.30, 0.09, 0.12);
                                break;
                            default:
                                DrawPickaxeGlyph(ctx);
                                break;
                        }
                        break;

                    case "woodworker":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawAxeGlyph(ctx);
                                break;
                            case "discipline":
                                DrawAxeGlyph(ctx);
                                DrawPlankGlyph(ctx);
                                break;
                            case "path-a":
                                DrawPlankGlyph(ctx);
                                DrawHammerGlyph(ctx);
                                break;
                            case "path-b":
                                DrawLogGlyph(ctx);
                                DrawAxeGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawPlankGlyph(ctx);
                                DrawHammerGlyph(ctx);
                                StrokeLine(ctx, -0.12, -0.26, 0.12, -0.26);
                                break;
                            case "expert-b":
                                DrawLogGlyph(ctx);
                                DrawAxeGlyph(ctx);
                                StrokeLine(ctx, 0.18, -0.20, 0.31, -0.29);
                                break;
                            default:
                                DrawAxeGlyph(ctx);
                                break;
                        }
                        break;

                    case "farmer":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawSeedlingGlyph(ctx);
                                break;
                            case "discipline":
                                DrawWheatGlyph(ctx);
                                DrawSickleGlyph(ctx);
                                break;
                            case "path-a":
                                DrawSeedlingGlyph(ctx);
                                break;
                            case "path-b":
                                DrawWheatGlyph(ctx);
                                DrawSickleGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawSeedlingGlyph(ctx);
                                FillCircle(ctx, -0.24, -0.12, 0.04);
                                FillCircle(ctx, -0.17, -0.22, 0.03);
                                break;
                            case "expert-b":
                                DrawWheatGlyph(ctx);
                                DrawSickleGlyph(ctx);
                                StrokeLine(ctx, 0.12, 0.26, 0.28, 0.20);
                                break;
                            default:
                                DrawWheatGlyph(ctx);
                                break;
                        }
                        break;

                    case "builder":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawTrowelGlyph(ctx);
                                break;
                            case "discipline":
                                DrawBrickGlyph(ctx);
                                DrawTrowelGlyph(ctx);
                                break;
                            case "path-a":
                                DrawDraftingGlyph(ctx);
                                break;
                            case "path-b":
                                DrawBrickGlyph(ctx);
                                DrawTrowelGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawDraftingGlyph(ctx);
                                StrokeLine(ctx, -0.22, 0.24, 0.22, 0.24);
                                break;
                            case "expert-b":
                                DrawBrickGlyph(ctx);
                                DrawWallGlyph(ctx);
                                break;
                            default:
                                DrawBrickGlyph(ctx);
                                break;
                        }
                        break;

                    case "blacksmith":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawAnvilGlyph(ctx);
                                break;
                            case "discipline":
                                DrawAnvilGlyph(ctx);
                                DrawHammerGlyph(ctx);
                                break;
                            case "path-a":
                                DrawSwordGlyph(ctx);
                                break;
                            case "path-b":
                                DrawBreastplateGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawSwordGlyph(ctx);
                                StrokeLine(ctx, -0.18, -0.16, 0.15, -0.30);
                                break;
                            case "expert-b":
                                DrawBreastplateGlyph(ctx);
                                StrokeLine(ctx, -0.24, -0.18, 0.24, -0.18);
                                break;
                            default:
                                DrawAnvilGlyph(ctx);
                                break;
                        }
                        break;

                    case "cook":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawPotGlyph(ctx);
                                break;
                            case "discipline":
                                DrawClocheGlyph(ctx);
                                break;
                            case "path-a":
                                DrawClocheGlyph(ctx);
                                break;
                            case "path-b":
                                DrawPlatterGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawClocheGlyph(ctx);
                                StrokeLine(ctx, -0.16, -0.24, 0.16, -0.24);
                                break;
                            case "expert-b":
                                DrawPlatterGlyph(ctx);
                                StrokeLine(ctx, -0.20, -0.16, -0.08, -0.28);
                                StrokeLine(ctx, 0.20, -0.16, 0.08, -0.28);
                                break;
                            default:
                                DrawPotGlyph(ctx);
                                break;
                        }
                        break;

                    case "potter":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawVaseGlyph(ctx);
                                break;
                            case "discipline":
                                DrawVaseGlyph(ctx);
                                StrokeLine(ctx, -0.22, 0.20, 0.22, 0.20);
                                break;
                            case "path-a":
                                DrawVaseGlyph(ctx);
                                break;
                            case "path-b":
                                DrawVaseGlyph(ctx);
                                DrawFlameGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawVaseGlyph(ctx);
                                StrokeLine(ctx, -0.16, -0.24, 0.16, -0.24);
                                break;
                            case "expert-b":
                                DrawVaseGlyph(ctx);
                                DrawFlameGlyph(ctx);
                                StrokeLine(ctx, 0.20, -0.22, 0.33, -0.32);
                                break;
                            default:
                                DrawVaseGlyph(ctx);
                                break;
                        }
                        break;

                    case "leatherworker":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawHideGlyph(ctx);
                                break;
                            case "discipline":
                                DrawHideGlyph(ctx);
                                StrokeLine(ctx, -0.16, -0.08, 0.16, 0.08);
                                break;
                            case "path-a":
                                DrawSaddleGlyph(ctx);
                                break;
                            case "path-b":
                                DrawHideGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawSaddleGlyph(ctx);
                                StrokeLine(ctx, -0.18, 0.24, 0.18, 0.24);
                                break;
                            case "expert-b":
                                DrawHideGlyph(ctx);
                                StrokeLine(ctx, -0.24, -0.08, 0.24, 0.08);
                                StrokeLine(ctx, 0.00, -0.20, 0.00, 0.20);
                                break;
                            default:
                                DrawHideGlyph(ctx);
                                break;
                        }
                        break;

                    case "tailor":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawNeedleGlyph(ctx);
                                break;
                            case "discipline":
                                DrawNeedleGlyph(ctx);
                                StrokeLine(ctx, -0.12, 0.20, 0.18, 0.30);
                                break;
                            case "path-a":
                                DrawNeedleGlyph(ctx);
                                break;
                            case "path-b":
                                DrawLoomGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawNeedleGlyph(ctx);
                                StrokeLine(ctx, -0.20, -0.22, 0.12, -0.26);
                                break;
                            case "expert-b":
                                DrawLoomGlyph(ctx);
                                StrokeLine(ctx, -0.18, 0.28, 0.18, 0.28);
                                break;
                            default:
                                DrawNeedleGlyph(ctx);
                                break;
                        }
                        break;

                    case "hunter":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawHunterGlyph(ctx);
                                break;
                            case "discipline":
                                DrawHunterGlyph(ctx);
                                DrawTargetGlyph(ctx);
                                break;
                            case "path-a":
                                DrawTrackerGlyph(ctx);
                                break;
                            case "path-b":
                                DrawCleaverGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawMasterTrackerGlyph(ctx);
                                break;
                            case "expert-b":
                                DrawHunterGlyph(ctx);
                                StrokeLine(ctx, -0.22, 0.22, 0.22, -0.22);
                                break;
                            default:
                                DrawHunterGlyph(ctx);
                                break;
                        }
                        break;

                    case "warrior":
                        switch (nodeId)
                        {
                            case "foundation":
                            case "discipline":
                            case "path-b":
                            case "expert-b":
                                DrawCrossedSwordsGlyph(ctx);
                                break;
                            case "path-a":
                                DrawSwordGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawSwordGlyph(ctx);
                                StrokeLine(ctx, -0.14, -0.26, 0.14, -0.26);
                                break;
                            default:
                                DrawCrossedSwordsGlyph(ctx);
                                break;
                        }
                        break;

                    case "ranger":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawEyeGlyph(ctx);
                                break;
                            case "discipline":
                                DrawStringCareGlyph(ctx);
                                break;
                            case "path-a":
                                DrawSharpshooterGlyph(ctx);
                                break;
                            case "path-b":
                                DrawSkirmisherGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawMasterSharpshooterGlyph(ctx);
                                break;
                            case "expert-b":
                                DrawArrowstormGlyph(ctx);
                                break;
                            default:
                                DrawRangerGlyph(ctx);
                                break;
                        }
                        break;

                    case "fisher":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawFishGlyph(ctx);
                                break;
                            case "discipline":
                                DrawHookGlyph(ctx);
                                break;
                            case "path-a":
                                DrawHookGlyph(ctx);
                                break;
                            case "path-b":
                                DrawNetGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawHookGlyph(ctx);
                                StrokeLine(ctx, -0.14, -0.24, 0.14, -0.24);
                                break;
                            case "expert-b":
                                DrawNetGlyph(ctx);
                                StrokeLine(ctx, -0.20, -0.24, 0.20, -0.24);
                                break;
                            default:
                                DrawFishGlyph(ctx);
                                break;
                        }
                        break;

                    case "animalhusbandry":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawPawGlyph(ctx);
                                break;
                            case "discipline":
                                DrawCrookGlyph(ctx);
                                break;
                            case "path-a":
                                DrawHeartGlyph(ctx);
                                FillCircle(ctx, 0.18, 0.18, 0.08);
                                break;
                            case "path-b":
                                DrawCrookGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawHeartGlyph(ctx);
                                FillCircle(ctx, 0.18, 0.18, 0.08);
                                StrokeLine(ctx, -0.18, -0.24, 0.18, -0.24);
                                break;
                            case "expert-b":
                                DrawCrookGlyph(ctx);
                                StrokeLine(ctx, -0.14, 0.28, 0.14, 0.28);
                                break;
                            default:
                                DrawPawGlyph(ctx);
                                break;
                        }
                        break;

                    case "beekeeper":
                        switch (nodeId)
                        {
                            case "foundation":
                                DrawBeeGlyph(ctx);
                                break;
                            case "discipline":
                                DrawHoneycombGlyph(ctx);
                                break;
                            case "path-a":
                                DrawHoneycombGlyph(ctx);
                                break;
                            case "path-b":
                                DrawHoneyJarGlyph(ctx);
                                break;
                            case "expert-a":
                                DrawHoneycombGlyph(ctx);
                                StrokeLine(ctx, -0.18, -0.24, 0.18, -0.24);
                                break;
                            case "expert-b":
                                DrawHoneyJarGlyph(ctx);
                                StrokeLine(ctx, -0.16, -0.24, 0.16, -0.24);
                                break;
                            default:
                                DrawBeeGlyph(ctx);
                                break;
                        }
                        break;

                    case "spearman":
                        switch (nodeId)
                        {
                            case "foundation":
                            case "discipline":
                                DrawSpearGlyph(ctx);
                                break;
                            case "path-a":
                                DrawSpearGlyph(ctx);
                                FillPolygon(ctx, -0.06, -0.02, -0.25, 0.00, -0.06, 0.14);
                                break;
                            case "path-b":
                                DrawSpearGlyph(ctx);
                                FillCircle(ctx, 0.21, -0.20, 0.06);
                                break;
                            case "expert-a":
                                DrawSpearGlyph(ctx);
                                FillPolygon(ctx, -0.06, -0.02, -0.25, 0.00, -0.06, 0.14);
                                StrokeLine(ctx, -0.20, -0.22, 0.20, -0.22);
                                break;
                            case "expert-b":
                                DrawSpearGlyph(ctx);
                                FillCircle(ctx, 0.21, -0.20, 0.06);
                                StrokeLine(ctx, -0.20, -0.22, 0.20, -0.22);
                                break;
                            default:
                                DrawSpearGlyph(ctx);
                                break;
                        }
                        break;

                    case "shield":
                        switch (nodeId)
                        {
                            case "foundation":
                            case "discipline":
                            case "path-a":
                            case "expert-a":
                                DrawShieldGlyph(ctx);
                                break;
                            case "path-b":
                            case "expert-b":
                                DrawWallGlyph(ctx);
                                break;
                            default:
                                DrawShieldGlyph(ctx);
                                break;
                        }
                        break;

                    case "tank":
                        switch (nodeId)
                        {
                            case "foundation":
                            case "discipline":
                                DrawHelmetGlyph(ctx);
                                break;
                            case "path-a":
                            case "expert-a":
                                DrawBreastplateGlyph(ctx);
                                break;
                            case "path-b":
                            case "expert-b":
                                DrawHelmetGlyph(ctx);
                                StrokeLine(ctx, 0.20, -0.10, 0.38, -0.10);
                                StrokeLine(ctx, 0.18, 0.03, 0.35, 0.03);
                                break;
                            default:
                                DrawHelmetGlyph(ctx);
                                break;
                        }
                        break;

                    default:
                        DrawProfessionIcon(
                            ctx,
                            classId,
                            0,
                            0,
                            0.92,
                            r,
                            g,
                            b,
                            a
                        );
                        break;
                }
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawEyeGlyph(
            Context ctx)
        {
            ctx.MoveTo(-0.42, 0);
            ctx.CurveTo(-0.22, -0.28, 0.22, -0.28, 0.42, 0);
            ctx.CurveTo(0.22, 0.28, -0.22, 0.28, -0.42, 0);
            ctx.ClosePath();
            ctx.Stroke();
            StrokeCircle(ctx, 0, 0, 0.14);
            FillCircle(ctx, 0, 0, 0.055);
        }

        private static void DrawRangerGlyph(
            Context ctx)
        {
            DrawBowGlyph(ctx);
        }

        private static void DrawStringCareGlyph(
            Context ctx)
        {
            DrawBowGlyph(ctx);
            StrokeLine(ctx, -0.20, -0.31, 0.16, 0.00);
            StrokeLine(ctx, 0.16, 0.00, -0.20, 0.31);
        }

        private static void DrawSharpshooterGlyph(
            Context ctx)
        {
            DrawTargetGlyph(ctx);
        }

        private static void DrawMasterSharpshooterGlyph(
            Context ctx)
        {
            DrawTargetGlyph(ctx);
            StrokeCircle(ctx, 0, 0, 0.38);
            FillCircle(ctx, 0, 0, 0.03);
        }

        private static void DrawSkirmisherGlyph(
            Context ctx)
        {
            DrawBowGlyph(ctx);
            StrokeLine(ctx, -0.43, -0.22, -0.27, -0.22);
            StrokeLine(ctx, -0.46, -0.05, -0.28, -0.05);
            StrokeLine(ctx, -0.43, 0.12, -0.27, 0.12);
        }

        private static void DrawArrowstormGlyph(
            Context ctx)
        {
            DrawQuiverGlyph(ctx);
            DrawSingleArrowGlyph(ctx, -0.22, -0.40, -0.10, 0.16);
            DrawSingleArrowGlyph(ctx, 0.00, -0.44, 0.04, 0.14);
            DrawSingleArrowGlyph(ctx, 0.22, -0.39, 0.17, 0.16);
        }

        private static void DrawQuiverGlyph(
            Context ctx)
        {
            ctx.MoveTo(-0.22, -0.12);
            ctx.LineTo(0.18, -0.12);
            ctx.LineTo(0.11, 0.36);
            ctx.LineTo(-0.12, 0.36);
            ctx.ClosePath();
            ctx.Stroke();
            StrokeLine(ctx, -0.17, 0.08, 0.15, 0.08);
            StrokeLine(ctx, -0.13, 0.22, 0.12, 0.22);
        }

        private static void DrawSingleArrowGlyph(
            Context ctx,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            StrokeLine(ctx, x1, y1, x2, y2);

            double dx = x2 - x1;
            double dy = y2 - y1;
            double length = Math.Max(0.0001, Math.Sqrt(dx * dx + dy * dy));
            double ux = dx / length;
            double uy = dy / length;
            double px = -uy;
            double py = ux;

            FillPolygon(
                ctx,
                x1,
                y1,
                x1 + ux * 0.11 + px * 0.055,
                y1 + uy * 0.11 + py * 0.055,
                x1 + ux * 0.11 - px * 0.055,
                y1 + uy * 0.11 - py * 0.055
            );

            StrokeLine(
                ctx,
                x2,
                y2,
                x2 - ux * 0.10 + px * 0.06,
                y2 - uy * 0.10 + py * 0.06
            );
            StrokeLine(
                ctx,
                x2,
                y2,
                x2 - ux * 0.10 - px * 0.06,
                y2 - uy * 0.10 - py * 0.06
            );
        }

        private static void DrawTrackerGlyph(
            Context ctx)
        {
            DrawFootprintGlyph(ctx, -0.13, 0.10, 0.95);
            DrawFootprintGlyph(ctx, 0.14, -0.12, 0.95);
            StrokeLine(ctx, -0.26, 0.26, 0.02, 0.02);
        }

        private static void DrawMasterTrackerGlyph(
            Context ctx)
        {
            DrawTrackerGlyph(ctx);
            ctx.Arc(0.24, -0.22, 0.11, 0, Math.PI * 2);
            ctx.Stroke();
            StrokeLine(ctx, 0.24, -0.33, 0.24, -0.11);
            StrokeLine(ctx, 0.13, -0.22, 0.35, -0.22);
        }

        private static void DrawFootprintGlyph(
            Context ctx,
            double offsetX,
            double offsetY,
            double scale)
        {
            FillCircle(ctx, offsetX, offsetY + 0.08 * scale, 0.08 * scale);
            FillCircle(ctx, offsetX - 0.07 * scale, offsetY - 0.05 * scale, 0.03 * scale);
            FillCircle(ctx, offsetX - 0.01 * scale, offsetY - 0.10 * scale, 0.03 * scale);
            FillCircle(ctx, offsetX + 0.05 * scale, offsetY - 0.11 * scale, 0.028 * scale);
            FillCircle(ctx, offsetX + 0.10 * scale, offsetY - 0.07 * scale, 0.025 * scale);
        }

        private static void DrawSurveyorGlyph(
            Context ctx)
        {
            StrokeLine(ctx, -0.22, 0.28, 0.00, -0.02);
            StrokeLine(ctx, 0.00, -0.02, 0.22, 0.28);
            StrokeLine(ctx, 0.00, -0.02, 0.00, 0.28);
            ctx.Rectangle(-0.16, -0.18, 0.32, 0.12);
            ctx.Stroke();
            StrokeLine(ctx, -0.20, -0.02, 0.20, -0.02);
        }

        private static void DrawMasterSurveyorGlyph(
            Context ctx)
        {
            DrawSurveyorGlyph(ctx);
            ctx.MoveTo(-0.28, 0.10);
            ctx.CurveTo(-0.12, 0.02, 0.02, 0.02, 0.18, 0.10);
            ctx.CurveTo(0.28, 0.15, 0.30, 0.22, 0.18, 0.28);
            ctx.Stroke();
            ctx.MoveTo(-0.24, 0.18);
            ctx.CurveTo(-0.08, 0.12, 0.04, 0.12, 0.20, 0.18);
            ctx.Stroke();
        }

        private static void DrawOpenBookIcon(
            Context ctx,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size,
                    size
                );

                ctx.LineWidth = 0.08;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                DrawOpenBookGlyph(ctx);
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawDisciplineMedalIcon(
            Context ctx,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size,
                    size
                );

                ctx.LineWidth = 0.08;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                DrawRosetteGlyph(ctx);
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawMiniProfessionBadge(
            Context ctx,
            string classId,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Arc(
                    centerX,
                    centerY,
                    size * 0.58,
                    0,
                    Math.PI * 2
                );

                SetColor(
                    ctx,
                    r * 0.28,
                    g * 0.28,
                    b * 0.28,
                    0.96 * a
                );
                ctx.FillPreserve();

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );
                ctx.LineWidth = Math.Max(1.0, size * 0.10);
                ctx.Stroke();

                DrawProfessionIcon(
                    ctx,
                    classId,
                    centerX,
                    centerY,
                    size * 0.48,
                    r,
                    g,
                    b,
                    a
                );
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawUpgradeSparkIcon(
            Context ctx,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size,
                    size
                );

                ctx.LineWidth = 0.10;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                DrawSparkBurstGlyph(ctx);
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawSpecializationNodeIcon(
            Context ctx,
            string classId,
            bool pathA,
            bool expert,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size * 0.72,
                    size * 0.72
                );

                ctx.LineWidth = 0.08;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                switch (
                    classId
                        .ToLowerInvariant()
                )
                {
                    case "miner":
                        if (pathA)
                        {
                            DrawCompassGlyph(ctx);
                        }
                        else
                        {
                            DrawLanternGlyph(ctx);
                        }
                        break;

                    case "woodworker":
                        if (pathA)
                        {
                            DrawPlankGlyph(ctx);
                            DrawHammerGlyph(ctx);
                        }
                        else
                        {
                            DrawLogGlyph(ctx);
                            DrawAxeGlyph(ctx);
                        }
                        break;

                    case "farmer":
                        if (pathA)
                        {
                            DrawSeedlingGlyph(ctx);
                        }
                        else
                        {
                            DrawWheatGlyph(ctx);
                            DrawSickleGlyph(ctx);
                        }
                        break;

                    case "builder":
                        if (pathA)
                        {
                            DrawDraftingGlyph(ctx);
                        }
                        else
                        {
                            DrawBrickGlyph(ctx);
                            DrawTrowelGlyph(ctx);
                        }
                        break;

                    case "blacksmith":
                        if (pathA)
                        {
                            DrawSwordGlyph(ctx);
                        }
                        else
                        {
                            DrawBreastplateGlyph(ctx);
                        }
                        break;

                    case "cook":
                        if (pathA)
                        {
                            DrawClocheGlyph(ctx);
                        }
                        else
                        {
                            DrawPlatterGlyph(ctx);
                        }
                        break;

                    case "potter":
                        if (pathA)
                        {
                            DrawVaseGlyph(ctx);
                        }
                        else
                        {
                            DrawVaseGlyph(ctx);
                            DrawFlameGlyph(ctx);
                        }
                        break;

                    case "leatherworker":
                        if (pathA)
                        {
                            DrawSaddleGlyph(ctx);
                        }
                        else
                        {
                            DrawHideGlyph(ctx);
                        }
                        break;

                    case "tailor":
                        if (pathA)
                        {
                            DrawNeedleGlyph(ctx);
                        }
                        else
                        {
                            DrawLoomGlyph(ctx);
                        }
                        break;

                    case "hunter":
                        if (pathA)
                        {
                            DrawTargetGlyph(ctx);
                            DrawPawGlyph(ctx);
                        }
                        else
                        {
                            DrawCleaverGlyph(ctx);
                        }
                        break;

                    case "warrior":
                        if (pathA)
                        {
                            DrawSwordGlyph(ctx);
                        }
                        else
                        {
                            DrawCrossedSwordsGlyph(ctx);
                        }
                        break;

                    case "ranger":
                        if (pathA)
                        {
                            DrawTargetGlyph(ctx);
                        }
                        else
                        {
                            DrawBowGlyph(ctx);
                            StrokeLine(
                                ctx,
                                -0.32,
                                -0.18,
                                0.35,
                                -0.34
                            );
                        }
                        break;

                    case "fisher":
                        if (pathA)
                        {
                            DrawHookGlyph(ctx);
                        }
                        else
                        {
                            DrawNetGlyph(ctx);
                        }
                        break;

                    case "animalhusbandry":
                        if (pathA)
                        {
                            DrawHeartGlyph(ctx);
                            FillCircle(
                                ctx,
                                0.18,
                                0.18,
                                0.08
                            );
                        }
                        else
                        {
                            DrawCrookGlyph(ctx);
                        }
                        break;

                    case "beekeeper":
                        if (pathA)
                        {
                            DrawHoneycombGlyph(ctx);
                        }
                        else
                        {
                            DrawHoneyJarGlyph(ctx);
                        }
                        break;

                    case "spearman":
                        if (pathA)
                        {
                            DrawSpearGlyph(ctx);
                            FillPolygon(
                                ctx,
                                -0.06,
                                -0.02,
                                -0.25,
                                0.00,
                                -0.06,
                                0.14
                            );
                        }
                        else
                        {
                            DrawSpearGlyph(ctx);
                            FillCircle(
                                ctx,
                                0.21,
                                -0.20,
                                0.06
                            );
                        }
                        break;

                    case "shield":
                        if (pathA)
                        {
                            DrawShieldGlyph(ctx);
                        }
                        else
                        {
                            DrawWallGlyph(ctx);
                        }
                        break;

                    case "tank":
                        if (pathA)
                        {
                            DrawBreastplateGlyph(ctx);
                        }
                        else
                        {
                            DrawHelmetGlyph(ctx);
                            StrokeLine(
                                ctx,
                                0.20,
                                -0.10,
                                0.38,
                                -0.10
                            );
                            StrokeLine(
                                ctx,
                                0.18,
                                0.03,
                                0.35,
                                0.03
                            );
                        }
                        break;

                    default:
                        DrawProfessionIcon(
                            ctx,
                            classId,
                            0,
                            0,
                            0.85,
                            r,
                            g,
                            b,
                            a
                        );
                        break;
                }
            }
            finally
            {
                ctx.Restore();
            }

            DrawMiniProfessionBadge(
                ctx,
                classId,
                centerX - size * 0.28,
                centerY - size * 0.26,
                size * 0.21,
                r,
                g,
                b,
                a
            );

            if (expert)
            {
                DrawUpgradeSparkIcon(
                    ctx,
                    centerX + size * 0.28,
                    centerY - size * 0.26,
                    size * 0.15,
                    r,
                    g,
                    b,
                    a
                );

                DrawUpgradeSparkIcon(
                    ctx,
                    centerX + size * 0.08,
                    centerY + size * 0.26,
                    size * 0.10,
                    r,
                    g,
                    b,
                    a * 0.92
                );
            }
        }


        private static void DrawOpenBookGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.40,
                -0.25
            );
            ctx.CurveTo(
                -0.26,
                -0.35,
                -0.09,
                -0.34,
                0,
                -0.21
            );
            ctx.CurveTo(
                0.09,
                -0.34,
                0.26,
                -0.35,
                0.40,
                -0.25
            );
            ctx.LineTo(
                0.40,
                0.33
            );
            ctx.CurveTo(
                0.26,
                0.24,
                0.09,
                0.24,
                0,
                0.34
            );
            ctx.CurveTo(
                -0.09,
                0.24,
                -0.26,
                0.24,
                -0.40,
                0.33
            );
            ctx.ClosePath();
            ctx.Stroke();

            StrokeLine(
                ctx,
                0,
                -0.22,
                0,
                0.34
            );
        }

        private static void DrawRosetteGlyph(
            Context ctx)
        {
            StrokeCircle(
                ctx,
                0,
                -0.06,
                0.23
            );
            FillCircle(
                ctx,
                0,
                -0.06,
                0.08
            );
            FillPolygon(
                ctx,
                -0.12,
                0.16,
                -0.03,
                0.42,
                -0.21,
                0.31
            );
            FillPolygon(
                ctx,
                0.12,
                0.16,
                0.21,
                0.31,
                0.03,
                0.42
            );
        }

        private static void DrawSparkBurstGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                0,
                -0.36,
                0,
                0.36
            );
            StrokeLine(
                ctx,
                -0.36,
                0,
                0.36,
                0
            );
            StrokeLine(
                ctx,
                -0.25,
                -0.25,
                0.25,
                0.25
            );
            StrokeLine(
                ctx,
                -0.25,
                0.25,
                0.25,
                -0.25
            );
        }

        private static void DrawLanternGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.16,
                -0.25,
                0.16,
                -0.25
            );
            ctx.MoveTo(
                -0.24,
                -0.12
            );
            ctx.LineTo(
                -0.20,
                0.22
            );
            ctx.LineTo(
                0.20,
                0.22
            );
            ctx.LineTo(
                0.24,
                -0.12
            );
            ctx.ClosePath();
            ctx.Stroke();
            StrokeLine(
                ctx,
                0,
                -0.37,
                0,
                -0.25
            );
            DrawFlameGlyph(ctx);
        }

        private static void DrawPlankGlyph(
            Context ctx)
        {
            StrokeRectangle(
                ctx,
                -0.38,
                -0.12,
                0.76,
                0.24
            );
            StrokeLine(
                ctx,
                -0.18,
                -0.12,
                -0.18,
                0.12
            );
            StrokeLine(
                ctx,
                0.12,
                -0.12,
                0.12,
                0.12
            );
        }

        private static void DrawLogGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.35,
                -0.14
            );
            ctx.LineTo(
                0.28,
                -0.14
            );
            ctx.CurveTo(
                0.39,
                -0.14,
                0.39,
                0.14,
                0.28,
                0.14
            );
            ctx.LineTo(
                -0.35,
                0.14
            );
            ctx.ClosePath();
            ctx.Stroke();
            StrokeCircle(
                ctx,
                -0.35,
                0,
                0.14
            );
            StrokeCircle(
                ctx,
                -0.35,
                0,
                0.06
            );
        }

        private static void DrawSeedlingGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                0,
                0.38,
                0,
                -0.10
            );
            ctx.MoveTo(
                0,
                -0.06
            );
            ctx.CurveTo(
                -0.05,
                -0.28,
                -0.28,
                -0.24,
                -0.31,
                -0.03
            );
            ctx.Stroke();
            ctx.MoveTo(
                0,
                -0.01
            );
            ctx.CurveTo(
                0.05,
                -0.24,
                0.28,
                -0.28,
                0.31,
                -0.05
            );
            ctx.Stroke();
        }

        private static void DrawSickleGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                0.08,
                -0.20
            );
            ctx.CurveTo(
                -0.18,
                -0.08,
                -0.14,
                0.25,
                0.12,
                0.18
            );
            ctx.Stroke();
            StrokeLine(
                ctx,
                0.18,
                -0.28,
                0.06,
                -0.08
            );
        }

        private static void DrawDraftingGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.18,
                0.35,
                0.00,
                -0.14
            );
            StrokeLine(
                ctx,
                0.18,
                0.35,
                0.00,
                -0.14
            );
            StrokeLine(
                ctx,
                -0.28,
                0.35,
                0.28,
                0.35
            );
            StrokeLine(
                ctx,
                0.00,
                -0.14,
                0.14,
                -0.28
            );
        }

        private static void DrawTrowelGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.26,
                0.31,
                0.03,
                0.04
            );
            FillPolygon(
                ctx,
                0.03,
                0.04,
                0.30,
                -0.04,
                0.12,
                -0.24
            );
        }

        private static void DrawClocheGlyph(
            Context ctx)
        {
            ctx.Arc(
                0,
                0.08,
                0.28,
                Math.PI,
                Math.PI * 2
            );
            ctx.Stroke();
            StrokeLine(
                ctx,
                -0.34,
                0.08,
                0.34,
                0.08
            );
            FillCircle(
                ctx,
                0,
                -0.22,
                0.05
            );
        }

        private static void DrawPlatterGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.35,
                0.10,
                0.35,
                0.10
            );
            ctx.MoveTo(
                -0.22,
                0.10
            );
            ctx.CurveTo(
                -0.16,
                -0.16,
                0.16,
                -0.16,
                0.22,
                0.10
            );
            ctx.Stroke();
            DrawSteam(ctx, -0.11);
            DrawSteam(ctx, 0.11);
        }

        private static void DrawFlameGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                0,
                0.22
            );
            ctx.CurveTo(
                -0.14,
                0.08,
                -0.12,
                -0.10,
                0,
                -0.24
            );
            ctx.CurveTo(
                0.12,
                -0.10,
                0.14,
                0.08,
                0,
                0.22
            );
            ctx.ClosePath();
            ctx.Stroke();
        }

        private static void DrawSaddleGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.30,
                0.12
            );
            ctx.CurveTo(
                -0.26,
                -0.18,
                0.05,
                -0.28,
                0.26,
                -0.08
            );
            ctx.LineTo(
                0.18,
                0.16
            );
            ctx.LineTo(
                -0.08,
                0.16
            );
            ctx.LineTo(
                -0.10,
                0.34
            );
            ctx.LineTo(
                -0.20,
                0.34
            );
            ctx.LineTo(
                -0.22,
                0.16
            );
            ctx.LineTo(
                -0.30,
                0.12
            );
            ctx.Stroke();
        }

        private static void DrawLoomGlyph(
            Context ctx)
        {
            StrokeRectangle(
                ctx,
                -0.28,
                -0.30,
                0.56,
                0.60
            );
            StrokeLine(
                ctx,
                -0.16,
                -0.22,
                -0.16,
                0.22
            );
            StrokeLine(
                ctx,
                0,
                -0.22,
                0,
                0.22
            );
            StrokeLine(
                ctx,
                0.16,
                -0.22,
                0.16,
                0.22
            );
            StrokeLine(
                ctx,
                -0.28,
                0,
                0.28,
                0
            );
        }

        private static void DrawCleaverGlyph(
            Context ctx)
        {
            FillPolygon(
                ctx,
                -0.08,
                -0.28,
                0.26,
                -0.18,
                0.26,
                0.08,
                -0.04,
                0.08,
                -0.12,
                -0.05
            );
            StrokeLine(
                ctx,
                -0.08,
                0.08,
                -0.08,
                0.34
            );
        }

        private static void DrawTargetGlyph(
            Context ctx)
        {
            StrokeCircle(
                ctx,
                0,
                0,
                0.30
            );
            StrokeCircle(
                ctx,
                0,
                0,
                0.16
            );
            StrokeLine(
                ctx,
                -0.36,
                0,
                0.36,
                0
            );
            StrokeLine(
                ctx,
                0,
                -0.36,
                0,
                0.36
            );
        }

        private static void DrawHookGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                0.16,
                -0.36,
                0.02,
                -0.02
            );
            ctx.MoveTo(
                0.02,
                -0.02
            );
            ctx.CurveTo(
                -0.12,
                0.08,
                -0.12,
                0.30,
                0.06,
                0.26
            );
            ctx.Stroke();
            FillPolygon(
                ctx,
                0.08,
                0.18,
                0.19,
                0.20,
                0.12,
                0.29
            );
        }

        private static void DrawNetGlyph(
            Context ctx)
        {
            StrokeRectangle(
                ctx,
                -0.26,
                -0.28,
                0.52,
                0.56
            );
            StrokeLine(
                ctx,
                -0.09,
                -0.28,
                -0.09,
                0.28
            );
            StrokeLine(
                ctx,
                0.09,
                -0.28,
                0.09,
                0.28
            );
            StrokeLine(
                ctx,
                -0.26,
                -0.09,
                0.26,
                -0.09
            );
            StrokeLine(
                ctx,
                -0.26,
                0.09,
                0.26,
                0.09
            );
        }

        private static void DrawHeartGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                0,
                0.30
            );
            ctx.CurveTo(
                -0.36,
                0.06,
                -0.35,
                -0.22,
                -0.14,
                -0.24
            );
            ctx.CurveTo(
                -0.03,
                -0.24,
                0.00,
                -0.13,
                0.00,
                -0.13
            );
            ctx.CurveTo(
                0.00,
                -0.13,
                0.03,
                -0.24,
                0.14,
                -0.24
            );
            ctx.CurveTo(
                0.35,
                -0.22,
                0.36,
                0.06,
                0,
                0.30
            );
            ctx.ClosePath();
            ctx.Stroke();
        }

        private static void DrawCrookGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.02,
                0.36,
                -0.02,
                -0.03
            );
            ctx.MoveTo(
                -0.02,
                -0.03
            );
            ctx.CurveTo(
                -0.02,
                -0.24,
                0.24,
                -0.29,
                0.30,
                -0.08
            );
            ctx.Stroke();
        }

        private static void DrawHoneycombGlyph(
            Context ctx)
        {
            FillPolygon(
                ctx,
                0,
                -0.30,
                0.24,
                -0.16,
                0.24,
                0.16,
                0,
                0.30,
                -0.24,
                0.16,
                -0.24,
                -0.16
            );
            FillPolygon(
                ctx,
                -0.28,
                -0.02,
                -0.12,
                -0.16,
                0.04,
                -0.02,
                -0.12,
                0.12
            );
        }

        private static void DrawHoneyJarGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.12,
                -0.28,
                0.12,
                -0.28
            );
            StrokeRectangle(
                ctx,
                -0.22,
                -0.20,
                0.44,
                0.42
            );
            StrokeLine(
                ctx,
                -0.18,
                -0.04,
                0.18,
                -0.04
            );
            StrokeLine(
                ctx,
                -0.18,
                0.08,
                0.18,
                0.08
            );
        }

        private static void DrawWallGlyph(
            Context ctx)
        {
            StrokeRectangle(
                ctx,
                -0.34,
                -0.26,
                0.68,
                0.52
            );
            StrokeLine(
                ctx,
                -0.34,
                0,
                0.34,
                0
            );
            StrokeLine(
                ctx,
                -0.12,
                -0.26,
                -0.12,
                0
            );
            StrokeLine(
                ctx,
                0.12,
                0,
                0.12,
                0.26
            );
        }

        private static void DrawBreastplateGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.26,
                -0.28
            );
            ctx.LineTo(
                -0.10,
                -0.36
            );
            ctx.LineTo(
                0.10,
                -0.36
            );
            ctx.LineTo(
                0.26,
                -0.28
            );
            ctx.LineTo(
                0.22,
                0.24
            );
            ctx.LineTo(
                0.06,
                0.34
            );
            ctx.LineTo(
                -0.06,
                0.34
            );
            ctx.LineTo(
                -0.22,
                0.24
            );
            ctx.ClosePath();
            ctx.Stroke();
            StrokeLine(
                ctx,
                0,
                -0.36,
                0,
                0.28
            );
        }

        private static void DrawCategoryIcon(
            Context ctx,
            string categoryId,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size,
                    size
                );

                ctx.LineWidth = 0.082;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                switch (
                    categoryId
                        .ToLowerInvariant()
                )
                {
                    case "combat":
                        ctx.Save();
                        ctx.Scale(0.82, 0.82);
                        DrawShieldGlyph(ctx);
                        ctx.Restore();
                        DrawCrossedWeaponsGlyph(
                            ctx
                        );
                        break;

                    case "gathering":
                        DrawLeafGlyph(
                            ctx
                        );
                        break;

                    case "crafting":
                        DrawHammerGlyph(
                            ctx
                        );
                        break;

                    case "production":
                        DrawGearGlyph(
                            ctx
                        );
                        break;

                    default:
                        DrawStarGlyph(
                            ctx,
                            true
                        );
                        break;
                }
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawProfessionIcon(
            Context ctx,
            string classId,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size,
                    size
                );

                ctx.LineWidth = 0.075;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                switch (
                    classId
                        .ToLowerInvariant()
                )
                {
                    case "miner":
                        DrawPickaxeGlyph(
                            ctx
                        );
                        break;

                    case "woodworker":
                        DrawAxeGlyph(
                            ctx
                        );
                        break;

                    case "farmer":
                        DrawWheatGlyph(
                            ctx
                        );
                        break;

                    case "builder":
                        DrawBrickGlyph(
                            ctx
                        );
                        break;

                    case "blacksmith":
                        DrawAnvilGlyph(
                            ctx
                        );
                        break;

                    case "cook":
                        DrawPotGlyph(
                            ctx
                        );
                        break;

                    case "potter":
                        DrawVaseGlyph(
                            ctx
                        );
                        break;

                    case "leatherworker":
                        DrawHideGlyph(
                            ctx
                        );
                        break;

                    case "tailor":
                        DrawNeedleGlyph(
                            ctx
                        );
                        break;

                    case "hunter":
                        DrawHunterGlyph(
                            ctx
                        );
                        break;

                    case "warrior":
                        DrawCrossedSwordsGlyph(
                            ctx
                        );
                        break;

                    case "ranger":
                        DrawRangerGlyph(
                            ctx
                        );
                        break;

                    case "fisher":
                        DrawFishGlyph(
                            ctx
                        );
                        break;

                    case "animalhusbandry":
                        DrawPawGlyph(
                            ctx
                        );
                        break;

                    case "beekeeper":
                        DrawBeeGlyph(
                            ctx
                        );
                        break;

                    case "spearman":
                        DrawSpearGlyph(
                            ctx
                        );
                        break;

                    case "shield":
                        DrawShieldGlyph(
                            ctx
                        );
                        break;

                    case "tank":
                        DrawHelmetGlyph(
                            ctx
                        );
                        break;

                    default:
                        DrawStarGlyph(
                            ctx,
                            false
                        );
                        break;
                }
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawPickaxeGlyph(
            Context ctx)
        {
            // Handle.
            ctx.LineWidth = 0.105;
            StrokeLine(ctx, -0.31, 0.41, 0.04, -0.13);

            // Balanced, double-sided pick head. Both wings have the same reach.
            ctx.LineWidth = 0.095;
            ctx.MoveTo(-0.40, -0.14);
            ctx.CurveTo(-0.27, -0.31, -0.10, -0.35, 0.04, -0.23);
            ctx.CurveTo(0.18, -0.35, 0.35, -0.31, 0.48, -0.14);
            ctx.Stroke();

            // Lower edge gives the head thickness and keeps it from reading as a scythe.
            ctx.MoveTo(-0.40, -0.14);
            ctx.CurveTo(-0.25, -0.22, -0.08, -0.23, 0.04, -0.16);
            ctx.CurveTo(0.16, -0.23, 0.33, -0.22, 0.48, -0.14);
            ctx.Stroke();

            // Pick eye / socket.
            ctx.Rectangle(-0.035, -0.245, 0.15, 0.15);
            ctx.Stroke();
        }

        private static void DrawAxeGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.23,
                0.38,
                0.13,
                -0.31
            );

            FillPolygon(
                ctx,
                -0.01,
                -0.34,
                0.34,
                -0.23,
                0.25,
                0.05,
                0.02,
                -0.04
            );
        }

        private static void DrawWheatGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                0,
                0.42,
                0,
                -0.39
            );

            double[] levels =
            {
                -0.28,
                -0.14,
                0,
                0.14
            };

            foreach (
                double level
                in levels
            )
            {
                StrokeLine(
                    ctx,
                    0,
                    level,
                    -0.22,
                    level - 0.12
                );

                StrokeLine(
                    ctx,
                    0,
                    level + 0.07,
                    0.22,
                    level - 0.05
                );
            }

            FillCircle(
                ctx,
                0,
                -0.39,
                0.07
            );
        }

        private static void DrawBrickGlyph(
            Context ctx)
        {
            StrokeRectangle(
                ctx,
                -0.39,
                -0.28,
                0.78,
                0.56
            );

            StrokeLine(
                ctx,
                -0.39,
                0,
                0.39,
                0
            );

            StrokeLine(
                ctx,
                -0.12,
                -0.28,
                -0.12,
                0
            );

            StrokeLine(
                ctx,
                0.14,
                0,
                0.14,
                0.28
            );
        }

        private static void DrawAnvilGlyph(
            Context ctx)
        {
            FillPolygon(
                ctx,
                -0.40,
                -0.22,
                0.40,
                -0.22,
                0.23,
                -0.02,
                0.12,
                -0.02,
                0.09,
                0.20,
                0.27,
                0.34,
                -0.27,
                0.34,
                -0.09,
                0.20,
                -0.12,
                -0.02,
                -0.28,
                -0.02
            );
        }

        private static void DrawPotGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.34,
                -0.16,
                0.34,
                -0.16
            );

            ctx.MoveTo(
                -0.29,
                -0.12
            );
            ctx.CurveTo(
                -0.27,
                0.28,
                -0.15,
                0.37,
                0,
                0.37
            );
            ctx.CurveTo(
                0.15,
                0.37,
                0.27,
                0.28,
                0.29,
                -0.12
            );
            ctx.Stroke();

            StrokeLine(
                ctx,
                -0.42,
                -0.05,
                -0.29,
                0.02
            );

            StrokeLine(
                ctx,
                0.42,
                -0.05,
                0.29,
                0.02
            );

            DrawSteam(
                ctx,
                -0.13
            );
            DrawSteam(
                ctx,
                0.13
            );
        }

        private static void DrawVaseGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.16,
                -0.39
            );
            ctx.LineTo(
                0.16,
                -0.39
            );
            ctx.LineTo(
                0.12,
                -0.22
            );
            ctx.CurveTo(
                0.34,
                -0.03,
                0.31,
                0.31,
                0.12,
                0.39
            );
            ctx.LineTo(
                -0.12,
                0.39
            );
            ctx.CurveTo(
                -0.31,
                0.31,
                -0.34,
                -0.03,
                -0.12,
                -0.22
            );
            ctx.ClosePath();
            ctx.Stroke();
        }

        private static void DrawHideGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                0,
                -0.40
            );
            ctx.CurveTo(
                0.12,
                -0.31,
                0.25,
                -0.36,
                0.35,
                -0.22
            );
            ctx.CurveTo(
                0.27,
                -0.06,
                0.39,
                0.09,
                0.25,
                0.21
            );
            ctx.CurveTo(
                0.11,
                0.23,
                0.10,
                0.39,
                0,
                0.42
            );
            ctx.CurveTo(
                -0.10,
                0.39,
                -0.11,
                0.23,
                -0.25,
                0.21
            );
            ctx.CurveTo(
                -0.39,
                0.09,
                -0.27,
                -0.06,
                -0.35,
                -0.22
            );
            ctx.CurveTo(
                -0.25,
                -0.36,
                -0.12,
                -0.31,
                0,
                -0.40
            );
            ctx.ClosePath();
            ctx.Stroke();

            StrokeLine(
                ctx,
                -0.15,
                -0.20,
                0.15,
                0.22
            );
        }

        private static void DrawNeedleGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.31,
                0.34,
                0.28,
                -0.34
            );

            StrokeCircle(
                ctx,
                0.28,
                -0.34,
                0.07
            );

            ctx.MoveTo(
                -0.31,
                0.34
            );
            ctx.CurveTo(
                -0.02,
                0.17,
                0.05,
                0.43,
                0.34,
                0.25
            );
            ctx.Stroke();
        }

        private static void DrawBowGlyph(
            Context ctx)
        {
            // Bow limbs.
            ctx.MoveTo(-0.22, -0.38);
            ctx.CurveTo(-0.43, -0.24, -0.43, 0.24, -0.22, 0.38);
            ctx.Stroke();

            // String pulled back to the nocking hand.
            StrokeLine(ctx, -0.22, -0.38, 0.14, 0.00);
            StrokeLine(ctx, 0.14, 0.00, -0.22, 0.38);

            // Nocked arrow, ready to release.
            StrokeLine(ctx, -0.34, 0.00, 0.40, 0.00);
            FillPolygon(
                ctx,
                0.46,
                0.00,
                0.34,
                -0.07,
                0.34,
                0.07
            );
            StrokeLine(ctx, -0.34, 0.00, -0.25, -0.065);
            StrokeLine(ctx, -0.34, 0.00, -0.25, 0.065);
        }

        private static void DrawHunterGlyph(
            Context ctx)
        {
            FillCircle(
                ctx,
                -0.05,
                0.12,
                0.14
            );

            FillCircle(
                ctx,
                -0.20,
                -0.12,
                0.075
            );

            FillCircle(
                ctx,
                -0.07,
                -0.25,
                0.075
            );

            FillCircle(
                ctx,
                0.08,
                -0.24,
                0.075
            );

            StrokeLine(
                ctx,
                -0.02,
                0.26,
                0.30,
                -0.10
            );

            FillPolygon(
                ctx,
                0.38,
                -0.19,
                0.24,
                -0.11,
                0.31,
                0.00
            );
        }

        private static void DrawSwordGlyph(
            Context ctx)
        {
            FillPolygon(
                ctx,
                0,
                -0.43,
                0.10,
                -0.27,
                0.05,
                0.20,
                -0.05,
                0.20,
                -0.10,
                -0.27
            );

            StrokeLine(
                ctx,
                -0.20,
                0.20,
                0.20,
                0.20
            );

            StrokeLine(
                ctx,
                0,
                0.20,
                0,
                0.40
            );
        }

        private static void DrawCompassGlyph(
            Context ctx)
        {
            StrokeCircle(
                ctx,
                0,
                0,
                0.39
            );

            FillPolygon(
                ctx,
                0.08,
                -0.31,
                -0.05,
                0.03,
                -0.08,
                0.31,
                0.05,
                -0.03
            );

            FillCircle(
                ctx,
                0,
                0,
                0.055
            );
        }

        private static void DrawFishGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.28,
                0
            );
            ctx.CurveTo(
                -0.08,
                -0.28,
                0.22,
                -0.28,
                0.35,
                0
            );
            ctx.CurveTo(
                0.22,
                0.28,
                -0.08,
                0.28,
                -0.28,
                0
            );
            ctx.ClosePath();
            ctx.Stroke();

            FillPolygon(
                ctx,
                -0.28,
                0,
                -0.43,
                -0.22,
                -0.43,
                0.22
            );

            FillCircle(
                ctx,
                0.20,
                -0.06,
                0.04
            );
        }

        private static void DrawPawGlyph(
            Context ctx)
        {
            FillCircle(
                ctx,
                0,
                0.16,
                0.20
            );

            FillCircle(
                ctx,
                -0.25,
                -0.13,
                0.105
            );

            FillCircle(
                ctx,
                -0.08,
                -0.28,
                0.105
            );

            FillCircle(
                ctx,
                0.12,
                -0.28,
                0.105
            );

            FillCircle(
                ctx,
                0.28,
                -0.10,
                0.105
            );
        }

        private static void DrawBeeGlyph(
            Context ctx)
        {
            StrokeCircle(
                ctx,
                0,
                0.05,
                0.22
            );

            StrokeLine(
                ctx,
                -0.18,
                -0.03,
                0.18,
                -0.03
            );

            StrokeLine(
                ctx,
                -0.18,
                0.09,
                0.18,
                0.09
            );

            ctx.Arc(
                -0.20,
                -0.18,
                0.17,
                0,
                Math.PI * 2
            );
            ctx.Stroke();

            ctx.Arc(
                0.20,
                -0.18,
                0.17,
                0,
                Math.PI * 2
            );
            ctx.Stroke();

            StrokeLine(
                ctx,
                -0.09,
                -0.17,
                -0.18,
                -0.36
            );

            StrokeLine(
                ctx,
                0.09,
                -0.17,
                0.18,
                -0.36
            );
        }

        private static void DrawSpearGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.28,
                0.37,
                0.19,
                -0.23
            );

            FillPolygon(
                ctx,
                0.38,
                -0.43,
                0.20,
                -0.16,
                0.09,
                -0.27
            );
        }

        private static void DrawShieldGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                0,
                -0.42
            );
            ctx.LineTo(
                0.34,
                -0.28
            );
            ctx.LineTo(
                0.27,
                0.17
            );
            ctx.CurveTo(
                0.20,
                0.32,
                0.08,
                0.40,
                0,
                0.44
            );
            ctx.CurveTo(
                -0.08,
                0.40,
                -0.20,
                0.32,
                -0.27,
                0.17
            );
            ctx.LineTo(
                -0.34,
                -0.28
            );
            ctx.ClosePath();
            ctx.Stroke();

            StrokeLine(
                ctx,
                0,
                -0.33,
                0,
                0.31
            );
        }

        private static void DrawHelmetGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.35,
                0.28
            );
            ctx.LineTo(
                -0.30,
                -0.06
            );
            ctx.CurveTo(
                -0.25,
                -0.32,
                -0.10,
                -0.42,
                0.09,
                -0.42
            );
            ctx.CurveTo(
                0.29,
                -0.42,
                0.38,
                -0.18,
                0.34,
                0.08
            );
            ctx.LineTo(
                0.17,
                0.08
            );
            ctx.LineTo(
                0.12,
                0.35
            );
            ctx.LineTo(
                -0.06,
                0.35
            );
            ctx.LineTo(
                -0.09,
                0.06
            );
            ctx.LineTo(
                -0.35,
                0.06
            );
            ctx.ClosePath();
            ctx.Stroke();

            StrokeLine(
                ctx,
                -0.04,
                -0.39,
                -0.04,
                0.02
            );
        }

        private static void DrawCrossedWeaponsGlyph(
            Context ctx)
        {
            DrawCrossedSwordsGlyph(ctx);
        }

        private static void DrawCrossedSwordsGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.27,
                0.34,
                0.22,
                -0.20
            );
            StrokeLine(
                ctx,
                0.27,
                0.34,
                -0.22,
                -0.20
            );

            FillPolygon(
                ctx,
                0.28,
                -0.30,
                0.20,
                -0.09,
                0.10,
                -0.18
            );
            FillPolygon(
                ctx,
                -0.28,
                -0.30,
                -0.20,
                -0.09,
                -0.10,
                -0.18
            );

            StrokeLine(
                ctx,
                -0.14,
                0.14,
                0.02,
                0.29
            );
            StrokeLine(
                ctx,
                0.14,
                0.14,
                -0.02,
                0.29
            );
            StrokeLine(
                ctx,
                -0.22,
                0.20,
                -0.08,
                0.20
            );
            StrokeLine(
                ctx,
                0.22,
                0.20,
                0.08,
                0.20
            );
        }

        private static void DrawLeafGlyph(
            Context ctx)
        {
            ctx.MoveTo(
                -0.36,
                0.31
            );
            ctx.CurveTo(
                -0.31,
                -0.30,
                0.19,
                -0.43,
                0.39,
                -0.28
            );
            ctx.CurveTo(
                0.32,
                0.23,
                -0.02,
                0.42,
                -0.36,
                0.31
            );
            ctx.ClosePath();
            ctx.Stroke();

            StrokeLine(
                ctx,
                -0.31,
                0.27,
                0.27,
                -0.24
            );
        }

        private static void DrawHammerGlyph(
            Context ctx)
        {
            StrokeLine(
                ctx,
                -0.25,
                0.38,
                0.12,
                -0.16
            );

            FillPolygon(
                ctx,
                -0.10,
                -0.38,
                0.35,
                -0.08,
                0.21,
                0.12,
                -0.24,
                -0.18
            );
        }

        private static void DrawGearGlyph(
            Context ctx)
        {
            StrokeCircle(
                ctx,
                0,
                0,
                0.24
            );

            FillCircle(
                ctx,
                0,
                0,
                0.07
            );

            for (
                int index = 0;
                index < 8;
                index++
            )
            {
                double angle =
                    index *
                    Math.PI /
                    4;

                StrokeLine(
                    ctx,
                    Math.Cos(
                        angle
                    ) * 0.27,
                    Math.Sin(
                        angle
                    ) * 0.27,
                    Math.Cos(
                        angle
                    ) * 0.43,
                    Math.Sin(
                        angle
                    ) * 0.43
                );
            }
        }

        private static void DrawCrownIcon(
            Context ctx,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size,
                    size
                );

                ctx.LineWidth = 0.13;

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                FillPolygon(
                    ctx,
                    -0.48,
                    0.26,
                    -0.41,
                    -0.31,
                    -0.14,
                    -0.02,
                    0,
                    -0.42,
                    0.14,
                    -0.02,
                    0.41,
                    -0.31,
                    0.48,
                    0.26
                );

                StrokeLine(
                    ctx,
                    -0.44,
                    0.35,
                    0.44,
                    0.35
                );
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawRingBadge(
            Context ctx,
            double centerX,
            double centerY,
            double radius,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                ctx.LineWidth =
                    Math.Max(
                        1.3,
                        radius * 0.34
                    );

                ctx.Arc(
                    centerX,
                    centerY,
                    radius,
                    0,
                    Math.PI * 2
                );

                ctx.Stroke();
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawTriangleBadge(
            Context ctx,
            double centerX,
            double centerY,
            double size,
            bool pointLeft,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                if (pointLeft)
                {
                    FillPolygon(
                        ctx,
                        centerX - size,
                        centerY,
                        centerX + size,
                        centerY - size,
                        centerX + size,
                        centerY + size
                    );
                }
                else
                {
                    FillPolygon(
                        ctx,
                        centerX + size,
                        centerY,
                        centerX - size,
                        centerY - size,
                        centerX - size,
                        centerY + size
                    );
                }
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawDiamondBadge(
            Context ctx,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.Save();

            try
            {
                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                FillPolygon(
                    ctx,
                    centerX,
                    centerY - size,
                    centerX + size,
                    centerY,
                    centerX,
                    centerY + size,
                    centerX - size,
                    centerY
                );
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawStarIcon(
            Context ctx,
            double centerX,
            double centerY,
            double size,
            double r,
            double g,
            double b,
            double a,
            bool filled)
        {
            ctx.Save();

            try
            {
                ctx.Translate(
                    centerX,
                    centerY
                );

                ctx.Scale(
                    size,
                    size
                );

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    a
                );

                ctx.LineWidth = 0.16;

                DrawStarGlyph(
                    ctx,
                    filled
                );
            }
            finally
            {
                ctx.Restore();
            }
        }

        private static void DrawStarGlyph(
            Context ctx,
            bool filled)
        {
            const int points = 10;

            for (
                int index = 0;
                index < points;
                index++
            )
            {
                double angle =
                    -Math.PI /
                    2 +
                    index *
                    Math.PI /
                    5;

                double radius =
                    index %
                    2 ==
                    0
                        ? 0.46
                        : 0.20;

                double x =
                    Math.Cos(
                        angle
                    ) *
                    radius;

                double y =
                    Math.Sin(
                        angle
                    ) *
                    radius;

                if (index == 0)
                {
                    ctx.MoveTo(
                        x,
                        y
                    );
                }
                else
                {
                    ctx.LineTo(
                        x,
                        y
                    );
                }
            }

            ctx.ClosePath();

            if (filled)
            {
                ctx.Fill();
            }
            else
            {
                ctx.Stroke();
            }
        }

        private static void DrawSteam(
            Context ctx,
            double x)
        {
            ctx.MoveTo(
                x,
                -0.23
            );
            ctx.CurveTo(
                x - 0.08,
                -0.32,
                x + 0.08,
                -0.39,
                x,
                -0.48
            );
            ctx.Stroke();
        }

        private static void StrokeLine(
            Context ctx,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            ctx.NewPath();
            ctx.MoveTo(
                x1,
                y1
            );

            ctx.LineTo(
                x2,
                y2
            );

            ctx.Stroke();
        }

        private static void StrokeCircle(
            Context ctx,
            double centerX,
            double centerY,
            double radius)
        {
            ctx.NewPath();
            ctx.Arc(
                centerX,
                centerY,
                radius,
                0,
                Math.PI * 2
            );

            ctx.Stroke();
        }

        private static void FillCircle(
            Context ctx,
            double centerX,
            double centerY,
            double radius)
        {
            ctx.NewPath();
            ctx.Arc(
                centerX,
                centerY,
                radius,
                0,
                Math.PI * 2
            );

            ctx.Fill();
        }

        private static void StrokeRectangle(
            Context ctx,
            double x,
            double y,
            double width,
            double height)
        {
            ctx.NewPath();
            ctx.Rectangle(
                x,
                y,
                width,
                height
            );

            ctx.Stroke();
        }

        private static void FillPolygon(
            Context ctx,
            params double[] points)
        {
            ctx.NewPath();

            if (points.Length < 6)
            {
                return;
            }

            ctx.MoveTo(
                points[0],
                points[1]
            );

            for (
                int index = 2;
                index < points.Length - 1;
                index += 2
            )
            {
                ctx.LineTo(
                    points[index],
                    points[index + 1]
                );
            }

            ctx.ClosePath();
            ctx.Fill();
        }

        private static string DescribeEffect(
            SkillEffectDefinition effect,
            int currentRank,
            int maxRank)
        {
            if (effect.Type.Equals(
                    "UnlockRecipe",
                    StringComparison.OrdinalIgnoreCase))
            {
                return
                    "Unlocks recipe: " +
                    (
                        effect.Code ??
                        "unknown"
                    );
            }

            if (effect.Type.Equals(
                    "TrackingDetail",
                    StringComparison.OrdinalIgnoreCase))
            {
                return currentRank > 0
                    ? "Tracking readout: animal name and approximate distance"
                    : "Next rank: tracking shows the animal name and approximate distance";
            }

            double current =
                effect.ValuePerRank *
                currentRank;

            double next =
                effect.ValuePerRank *
                Math.Min(
                    maxRank,
                    currentRank + 1
                );

            string effectName =
                !string.IsNullOrWhiteSpace(effect.Stat)
                    ? effect.Stat
                    : effect.Type;

            bool flatPoints =
                effect.Type.Equals(
                    "MaxHealth",
                    StringComparison.OrdinalIgnoreCase
                );

            bool meters =
                effect.Type.Equals(
                    "TrackingRange",
                    StringComparison.OrdinalIgnoreCase
                );

            bool reduction =
                effect.Type.EndsWith(
                    "Reduction",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                effect.Type.Equals(
                    "HungerReduction",
                    StringComparison.OrdinalIgnoreCase
                ) ||
                effect.Stat?.Contains(
                    "damage taken",
                    StringComparison.OrdinalIgnoreCase
                ) == true ||
                effect.Stat?.Contains(
                    "hunger drain",
                    StringComparison.OrdinalIgnoreCase
                ) == true;

            string FormatValue(double value)
            {
                if (meters)
                {
                    return $"+{value:0.##}m";
                }

                if (flatPoints)
                {
                    return $"+{value:0.##}";
                }

                return reduction
                    ? $"-{value * 100:0.##}%"
                    : $"+{value * 100:0.##}%";
            }

            string currentText =
                FormatValue(current);

            string nextText =
                FormatValue(next);

            if (currentRank <= 0)
            {
                return
                    $"{effectName}: {nextText} at next rank";
            }

            if (currentRank >= maxRank)
            {
                return
                    $"{effectName}: {currentText}";
            }

            return
                $"{effectName}: {currentText} now -> {nextText} next rank";
        }

        private void DrawClassIcon(
            Context ctx,
            SkillTreeDefinition tree,
            double x,
            double y,
            double size,
            ThemePalette theme)
        {
            DrawGlow(
                ctx,
                x +
                    size / 2,
                y +
                    size / 2,
                size /
                    2 +
                    4,
                theme.AccentR,
                theme.AccentG,
                theme.AccentB,
                0.22
            );

            ctx.Arc(
                x +
                    size / 2,
                y +
                    size / 2,
                size / 2,
                0,
                Math.PI * 2
            );

            SetColor(
                ctx,
                theme.AccentR *
                    0.34,
                theme.AccentG *
                    0.34,
                theme.AccentB *
                    0.34,
                0.98
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                theme.AccentR,
                theme.AccentG,
                theme.AccentB,
                0.95
            );

            ctx.LineWidth = 2.5;
            ctx.Stroke();

            DrawProfessionIcon(
                ctx,
                tree.ClassId,
                x +
                    size / 2,
                y +
                    size / 2,
                size * 0.50,
                0.98,
                0.95,
                0.88,
                1
            );
        }

        private void DrawSidebarEntry(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            string categoryId,
            string label,
            bool selected,
            bool hovered,
            ThemePalette theme)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                7
            );

            SetColor(
                ctx,
                selected
                    ? theme.AccentR *
                        0.32
                    : hovered
                        ? 0.15
                        : 0.095,
                selected
                    ? theme.AccentG *
                        0.32
                    : hovered
                        ? 0.16
                        : 0.105,
                selected
                    ? theme.AccentB *
                        0.32
                    : hovered
                        ? 0.19
                        : 0.13,
                0.95
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                selected
                    ? theme.AccentR
                    : 0.22,
                selected
                    ? theme.AccentG
                    : 0.24,
                selected
                    ? theme.AccentB
                    : 0.29,
                selected
                    ? 0.95
                    : 0.80
            );

            ctx.LineWidth =
                selected
                    ? 2.2
                    : 1;

            ctx.Stroke();

            DrawCategoryIcon(
                ctx,
                categoryId,
                x + 23,
                y + height / 2,
                20,
                selected
                    ? theme.AccentR
                    : 0.63,
                selected
                    ? theme.AccentG
                    : 0.66,
                selected
                    ? theme.AccentB
                    : 0.70,
                1
            );

            DrawFittedText(
                ctx,
                smallFont,
                label,
                x + 46,
                y + 8,
                width - 56
            );
        }

        private void DrawClassEntry(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            SkillTreeDefinition tree,
            int level,
            bool selected,
            bool hovered,
            ThemePalette theme)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                8
            );

            SetColor(
                ctx,
                selected
                    ? theme.PanelR *
                        1.9
                    : hovered
                        ? 0.13
                        : 0.075,
                selected
                    ? theme.PanelG *
                        1.9
                    : hovered
                        ? 0.14
                        : 0.085,
                selected
                    ? theme.PanelB *
                        1.9
                    : hovered
                        ? 0.17
                        : 0.11,
                0.98
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                selected
                    ? theme.AccentR
                    : 0.18,
                selected
                    ? theme.AccentG
                    : 0.20,
                selected
                    ? theme.AccentB
                    : 0.24,
                selected
                    ? 0.94
                    : 0.82
            );

            ctx.LineWidth =
                selected
                    ? 2
                    : 1;

            ctx.Stroke();

            ctx.Arc(
                x + 19,
                y + height / 2,
                12,
                0,
                Math.PI * 2
            );

            SetColor(
                ctx,
                theme.AccentR *
                    0.45,
                theme.AccentG *
                    0.45,
                theme.AccentB *
                    0.45,
                selected
                    ? 1
                    : 0.76
            );

            ctx.Fill();

            DrawProfessionIcon(
                ctx,
                tree.ClassId,
                x + 19,
                y + height / 2,
                18.5,
                0.98,
                0.95,
                0.88,
                selected
                    ? 1
                    : 0.86
            );

            DrawCenteredFittedText(
                ctx,
                smallFont,
                tree.DisplayName,
                x + width / 2,
                y + 10,
                width - 104,
                1.10
            );

            // Center the level label inside the rightmost 58-unit column.
            // Its smaller font needs a slightly lower baseline than the
            // enlarged profession name to share the same visual center.
            DrawCenteredText(
                ctx,
                tinyFont,
                $"Lv {level}",
                x + width - 29,
                y + 12,
                theme.AccentR,
                theme.AccentG,
                theme.AccentB,
                1
            );
        }

        private void DrawStatePill(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            NodeEvaluation evaluation,
            int rank,
            SkillNodeDefinition node,
            ThemePalette theme)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                7
            );

            (
                double fillR,
                double fillG,
                double fillB,
                double borderR,
                double borderG,
                double borderB
            ) =
                GetNodeColors(
                    evaluation.State,
                    theme
                );

            SetColor(
                ctx,
                fillR,
                fillG,
                fillB,
                0.76
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                borderR,
                borderG,
                borderB,
                0.92
            );

            ctx.LineWidth = 1.5;
            ctx.Stroke();

            DrawText(
                ctx,
                tinyFont,
                evaluation.State
                    .ToString()
                    .ToUpperInvariant(),
                x + 10,
                y + 7
            );

            string rankText =
                $"RANK {rank}/{node.MaxRank}";

            double rankWidth =
                MeasureTextWidth(
                    tinyFont,
                    rankText
                );

            DrawText(
                ctx,
                tinyFont,
                rankText,
                x +
                    width -
                    rankWidth -
                    10,
                y + 7
            );
        }

        private double DrawRequirementLine(
            Context ctx,
            double x,
            double y,
            double width,
            bool met,
            string text,
            ThemePalette theme)
        {
            double markerSize = 12;

            ctx.Arc(
                x +
                    markerSize / 2,
                y +
                    markerSize / 2 +
                    2,
                markerSize / 2,
                0,
                Math.PI * 2
            );

            SetColor(
                ctx,
                met
                    ? theme.SuccessR
                    : 0.74,
                met
                    ? theme.SuccessG
                    : 0.23,
                met
                    ? theme.SuccessB
                    : 0.26,
                0.95
            );

            ctx.Fill();

            DrawCenteredText(
                ctx,
                tinyFont,
                met
                    ? "+"
                    : "-",
                x +
                    markerSize / 2,
                y +
                    1,
                0.04,
                0.05,
                0.07,
                1
            );

            double height =
                DrawWrappedText(
                    ctx,
                    smallFont,
                    text,
                    x + 20,
                    y - 2,
                    width - 20
                );

            return Math.Max(
                21,
                height + 4
            );
        }

        private void DrawStatusBar(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            string text,
            ThemePalette theme)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                6
            );

            SetColor(
                ctx,
                theme.PanelR *
                    1.5,
                theme.PanelG *
                    1.5,
                theme.PanelB *
                    1.5,
                0.94
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                theme.AccentR,
                theme.AccentG,
                theme.AccentB,
                0.54
            );

            ctx.LineWidth = 1;
            ctx.Stroke();

            DrawFittedText(
                ctx,
                tinyFont,
                Shorten(
                    text,
                    49
                ),
                x + 9,
                y + 6,
                width - 18
            );
        }

        private static void DrawPanel(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            double fillR,
            double fillG,
            double fillB,
            double fillA,
            double borderR,
            double borderG,
            double borderB,
            double borderA)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                11
            );

            SetColor(
                ctx,
                fillR,
                fillG,
                fillB,
                fillA
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                borderR,
                borderG,
                borderB,
                borderA
            );

            ctx.LineWidth = 1.4;
            ctx.Stroke();
        }

        private static void DrawProgressBar(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            double progress,
            ThemePalette theme)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                height / 2
            );

            SetColor(
                ctx,
                0.035,
                0.04,
                0.055,
                0.88
            );

            ctx.Fill();

            if (progress >
                0)
            {
                RoundRectangle(
                    ctx,
                    x,
                    y,
                    Math.Max(
                        height,
                        width *
                        progress
                    ),
                    height,
                    height / 2
                );

                SetColor(
                    ctx,
                    theme.AccentR,
                    theme.AccentG,
                    theme.AccentB,
                    0.92
                );

                ctx.Fill();
            }

            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                height / 2
            );

            SetColor(
                ctx,
                0.68,
                0.70,
                0.75,
                0.36
            );

            ctx.LineWidth = 1;
            ctx.Stroke();
        }

        private void DrawBadge(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            string text,
            ThemePalette theme)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                7
            );

            SetColor(
                ctx,
                theme.AccentR *
                    0.22,
                theme.AccentG *
                    0.22,
                theme.AccentB *
                    0.22,
                0.90
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                theme.AccentR,
                theme.AccentG,
                theme.AccentB,
                0.60
            );

            ctx.LineWidth = 1;
            ctx.Stroke();

            DrawCenteredText(
                ctx,
                tinyFont,
                text,
                x +
                    width / 2,
                y + 6
            );
        }

        private void DrawMiniButton(
            Context ctx,
            double x,
            double y,
            double width,
            double height,
            string text,
            bool hovered,
            bool enabled,
            ThemePalette theme)
        {
            RoundRectangle(
                ctx,
                x,
                y,
                width,
                height,
                6
            );

            SetColor(
                ctx,
                hovered
                    ? theme.AccentR *
                        0.36
                    : 0.095,
                hovered
                    ? theme.AccentG *
                        0.36
                    : 0.105,
                hovered
                    ? theme.AccentB *
                        0.36
                    : 0.13,
                enabled
                    ? 0.96
                    : 0.48
            );

            ctx.FillPreserve();

            SetColor(
                ctx,
                hovered
                    ? theme.AccentR
                    : 0.26,
                hovered
                    ? theme.AccentG
                    : 0.28,
                hovered
                    ? theme.AccentB
                    : 0.32,
                0.92
            );

            ctx.LineWidth = 1;
            ctx.Stroke();

            DrawCenteredText(
                ctx,
                tinyFont,
                text,
                x + width / 2,
                y + 5
            );
        }

        private static void DrawDivider(
            Context ctx,
            double x,
            double y,
            double width)
        {
            SetColor(
                ctx,
                0.38,
                0.40,
                0.45,
                0.34
            );

            ctx.LineWidth = 1;
            ctx.MoveTo(
                x,
                y
            );

            ctx.LineTo(
                x + width,
                y
            );

            ctx.Stroke();
        }

        private static void DrawNodeShape(
            Context ctx,
            SkillNodeDefinition node,
            double x,
            double y,
            double radius)
        {
            if (node.Capstone)
            {
                DrawPolygon(
                    ctx,
                    x,
                    y,
                    radius,
                    8,
                    Math.PI / 8
                );

                return;
            }

            if (node.Row ==
                2)
            {
                DrawPolygon(
                    ctx,
                    x,
                    y,
                    radius,
                    4,
                    Math.PI / 4
                );

                return;
            }

            ctx.Arc(
                x,
                y,
                radius,
                0,
                Math.PI * 2
            );
        }

        private static void DrawPolygon(
            Context ctx,
            double centerX,
            double centerY,
            double radius,
            int points,
            double rotation)
        {
            for (int index = 0;
                 index < points;
                 index++)
            {
                double angle =
                    rotation +
                    Math.PI *
                    2 *
                    index /
                    points;

                double x =
                    centerX +
                    Math.Cos(
                        angle
                    ) *
                    radius;

                double y =
                    centerY +
                    Math.Sin(
                        angle
                    ) *
                    radius;

                if (index ==
                    0)
                {
                    ctx.MoveTo(
                        x,
                        y
                    );
                }
                else
                {
                    ctx.LineTo(
                        x,
                        y
                    );
                }
            }

            ctx.ClosePath();
        }

        private static void DrawGlow(
            Context ctx,
            double x,
            double y,
            double radius,
            double r,
            double g,
            double b,
            double alpha)
        {
            for (int ring = 3;
                 ring >= 1;
                 ring--)
            {
                ctx.NewPath();
                ctx.Arc(
                    x,
                    y,
                    radius +
                        ring * 4,
                    0,
                    Math.PI * 2
                );

                SetColor(
                    ctx,
                    r,
                    g,
                    b,
                    alpha /
                    (
                        ring *
                        1.4
                    )
                );

                ctx.LineWidth =
                    ring * 3;

                ctx.Stroke();
            }
        }

        private void UpdateCanvasScale(
            int canvasWidth,
            int canvasHeight)
        {
            canvasScaleX = Math.Max(
                0.01,
                canvasWidth / DesignCanvasWidth
            );

            canvasScaleY = Math.Max(
                0.01,
                canvasHeight / DesignCanvasHeight
            );

            double dialogScale = Math.Min(
                canvasScaleX,
                canvasScaleY
            );

            // CairoFont applies the game's GUI scale itself. Divide that out
            // before the shared canvas transform scales the complete dialog.
            // This keeps text, icons, panels and hit regions on one scale that
            // comes only from the Apprentice dialog's own inner bounds.
            textScale =
                Math.Clamp(
                    1 / dialogScale,
                    0.40,
                    2.00
                );
        }

        private CairoFont CreateScaledFont(
            CairoFont font,
            double additionalScale = 1)
        {
            CairoFont drawFont =
                font.Clone();

            drawFont.UnscaledFontsize =
                Math.Max(
                    6,
                    font.UnscaledFontsize *
                    textScale *
                    Math.Clamp(
                        additionalScale,
                        0.55,
                        1.25
                    )
                );

            return drawFont;
        }

        private void DrawText(
            Context ctx,
            CairoFont font,
            string text,
            double x,
            double y)
        {
            DrawTextScaled(
                ctx,
                font,
                text,
                x,
                y,
                1
            );
        }

        private void DrawTextScaled(
            Context ctx,
            CairoFont font,
            string text,
            double x,
            double y,
            double additionalScale)
        {
            CairoFont drawFont =
                CreateScaledFont(
                    font,
                    additionalScale
                );

            ctx.Save();

            try
            {
                drawFont.SetupContext(
                    ctx
                );

                textUtil.DrawTextLine(
                    ctx,
                    drawFont,
                    SafeText(text),
                    x,
                    y
                );
            }
            finally
            {
                ctx.Restore();
                drawFont.Dispose();
            }
        }

        private void DrawText(
            Context ctx,
            CairoFont font,
            string text,
            double x,
            double y,
            double r,
            double g,
            double b,
            double a)
        {
            CairoFont drawFont =
                CreateScaledFont(
                    font
                )
                    .WithColor(
                        new[]
                        {
                            r,
                            g,
                            b,
                            a
                        }
                    );

            ctx.Save();

            try
            {
                drawFont.SetupContext(
                    ctx
                );

                textUtil.DrawTextLine(
                    ctx,
                    drawFont,
                    SafeText(text),
                    x,
                    y
                );
            }
            finally
            {
                ctx.Restore();
                drawFont.Dispose();
            }
        }

        private void DrawFittedText(
            Context ctx,
            CairoFont font,
            string text,
            double x,
            double y,
            double maximumWidth)
        {
            string safe =
                SafeText(
                    text
                );

            double measuredWidth =
                MeasureTextWidth(
                    font,
                    safe
                );

            double fitScale =
                measuredWidth <= 0 ||
                measuredWidth <= maximumWidth
                    ? 1
                    : Math.Max(
                        0.62,
                        maximumWidth / measuredWidth
                    );

            DrawTextScaled(
                ctx,
                font,
                safe,
                x,
                y,
                fitScale
            );
        }

        private void DrawCenteredFittedText(
            Context ctx,
            CairoFont font,
            string text,
            double centerX,
            double y,
            double maximumWidth,
            double preferredScale = 1)
        {
            string safe =
                SafeText(
                    text
                );

            double measuredWidth =
                MeasureTextWidth(
                    font,
                    safe
                );

            double fitScale =
                measuredWidth <= 0
                    ? preferredScale
                    : Math.Min(
                        preferredScale,
                        maximumWidth / measuredWidth
                    );

            fitScale =
                Math.Clamp(
                    fitScale,
                    0.62,
                    1.25
                );

            DrawTextScaled(
                ctx,
                font,
                safe,
                centerX -
                    measuredWidth *
                    fitScale / 2,
                y,
                fitScale
            );
        }

        private void DrawCenteredText(
            Context ctx,
            CairoFont font,
            string text,
            double centerX,
            double y)
        {
            string safe =
                SafeText(
                    text
                );

            double width =
                MeasureTextWidth(
                    font,
                    safe
                );

            DrawText(
                ctx,
                font,
                safe,
                centerX -
                    width / 2,
                y
            );
        }

        private void DrawCenteredText(
            Context ctx,
            CairoFont font,
            string text,
            double centerX,
            double y,
            double r,
            double g,
            double b,
            double a)
        {
            string safe =
                SafeText(
                    text
                );

            double width =
                MeasureTextWidth(
                    font,
                    safe
                );

            DrawText(
                ctx,
                font,
                safe,
                centerX -
                    width / 2,
                y,
                r,
                g,
                b,
                a
            );
        }

        private double DrawWrappedText(
            Context ctx,
            CairoFont font,
            string text,
            double x,
            double y,
            double width)
        {
            CairoFont drawFont =
                CreateScaledFont(
                    font
                );

            try
            {
                return textUtil
                    .AutobreakAndDrawMultilineTextAt(
                        ctx,
                        drawFont,
                        SafeText(text),
                        x,
                        y,
                        width
                    );
            }
            finally
            {
                drawFont.Dispose();
            }
        }

        private double MeasureTextWidth(
            CairoFont font,
            string text)
        {
            CairoFont measureFont =
                CreateScaledFont(
                    font
                );

            try
            {
                return measureFont
                    .GetTextExtents(
                        SafeText(text)
                    )
                    .Width;
            }
            finally
            {
                measureFont.Dispose();
            }
        }

        private static void SetColor(
            Context ctx,
            double r,
            double g,
            double b,
            double a)
        {
            ctx.SetSourceRGBA(
                Math.Clamp(
                    r,
                    0,
                    1
                ),
                Math.Clamp(
                    g,
                    0,
                    1
                ),
                Math.Clamp(
                    b,
                    0,
                    1
                ),
                Math.Clamp(
                    a,
                    0,
                    1
                )
            );
        }

        private static string SafeText(
            string? value)
        {
            if (string.IsNullOrEmpty(
                    value))
            {
                return string.Empty;
            }

            var characters =
                new char[value.Length];

            for (int index = 0;
                 index < value.Length;
                 index++)
            {
                char character =
                    value[index];

                characters[index] =
                    character >= 32 &&
                    character <= 126
                        ? character
                        : '?';
            }

            return new string(
                characters
            );
        }

        private static string Shorten(
            string value,
            int maximumLength)
        {
            string safe =
                SafeText(
                    value
                );

            if (safe.Length <=
                maximumLength)
            {
                return safe;
            }

            return safe.Substring(
                       0,
                       Math.Max(
                           1,
                           maximumLength -
                           3
                       )
                   ) +
                   "...";
        }

        public override void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            // The five field fonts are immutable templates. All Cairo work is
            // performed by short-lived initialized clones, so the templates do
            // not own native FontOptions and must not be disposed.
            canvasTexture.Dispose();

            base.Dispose();
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
