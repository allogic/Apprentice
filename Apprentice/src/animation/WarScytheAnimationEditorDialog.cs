using System;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Apprentice
{
    internal sealed class WarScytheAnimationEditorDialog : GuiDialog
    {
        private const string StatusKey = "scythe-editor-status";
        private const string FrameKey = "scythe-editor-frame";
        private const string ElementKey = "scythe-editor-element";
        private const string TimelineKey = "scythe-editor-timeline";

        private static readonly string[] ComponentLabels =
        {
            "Offset X",
            "Offset Y",
            "Offset Z",
            "Rotation X",
            "Rotation Y",
            "Rotation Z"
        };

        private readonly WarScytheAnimationEditor editor;
        private readonly string[] sliderKeys = new string[6];
        private readonly string[] valueKeys = new string[6];
        private readonly Matrixf viewportLightMatrix = new();
        private readonly Vec4f viewportLightPosition =
            new(1f, -1f, 0f, 0f);

        private ElementBounds? viewportBounds;
        private bool composing;
        private double lastFrameWidth;
        private double lastFrameHeight;

        public WarScytheAnimationEditorDialog(
            ICoreClientAPI api,
            WarScytheAnimationEditor editor)
            : base(api)
        {
            this.editor = editor;
            for (int index = 0; index < sliderKeys.Length; index++)
            {
                sliderKeys[index] = $"scythe-editor-value-{index}";
                valueKeys[index] = $"scythe-editor-label-{index}";
            }
        }

        public override string? ToggleKeyCombinationCode => null;
        public override bool PrefersUngrabbedMouse => true;

        public override void OnGuiOpened()
        {
            base.OnGuiOpened();
            editor.ActivatePreview();
            ComposeDialog();
        }

        public override void OnGuiClosed()
        {
            editor.DeactivatePreview();
            base.OnGuiClosed();
        }

        public override void OnRenderGUI(float deltaTime)
        {
            if (Math.Abs(capi.Render.FrameWidth - lastFrameWidth) > 0.5 ||
                Math.Abs(capi.Render.FrameHeight - lastFrameHeight) > 0.5)
            {
                ComposeDialog();
            }

            RefreshDynamicState();
            base.OnRenderGUI(deltaTime);
            RenderPlayerViewport(deltaTime);
        }

        private void ComposeDialog()
        {
            composing = true;
            ClearComposers();

            double availableHeight =
                capi.Render.FrameHeight / RuntimeEnv.GUIScale;
            const double Width = 650;
            double height = Math.Min(780, Math.Max(700, availableHeight - 24));
            lastFrameWidth = capi.Render.FrameWidth;
            lastFrameHeight = capi.Render.FrameHeight;

            ElementBounds dialogBounds = ElementBounds.Fixed(
                EnumDialogArea.RightMiddle,
                -12,
                0,
                Width,
                height
            );
            GuiComposer composer = capi.Gui
                .CreateCompo(
                    "apprentice-war-scythe-editor",
                    dialogBounds
                )
                .AddShadedDialogBG(
                    ElementBounds.Fixed(0, 0, Width, height),
                    true
                )
                .AddDialogTitleBar(
                    "War Scythe live calibration",
                    () => TryClose()
                );

            CairoFont body = CairoFont.WhiteSmallText();
            CairoFont detail = CairoFont.WhiteDetailText();
            CairoFont header = CairoFont.WhiteSmallText();

            composer.AddDynamicText(
                editor.BuildStatusText(),
                detail,
                ElementBounds.Fixed(18, 42, 390, 150),
                StatusKey
            );
            viewportBounds = ElementBounds.Fixed(418, 42, 214, 150);
            composer.AddInset(viewportBounds, 1);

            composer
                .AddStaticText(
                    "Keyframe",
                    header,
                    ElementBounds.Fixed(18, 208, 76, 25)
                )
                .AddDropDown(
                    FrameCodes(),
                    editor.FrameCodes(),
                    editor.SelectedFrameIndex,
                    OnFrameChanged,
                    ElementBounds.Fixed(92, 202, 214, 30),
                    FrameKey
                )
                .AddStaticText(
                    "Element",
                    header,
                    ElementBounds.Fixed(326, 208, 68, 25)
                )
                .AddDropDown(
                    ElementCodes(),
                    WarScytheAnimationEditor.ControlledElements,
                    editor.SelectedElementIndex,
                    OnElementChanged,
                    ElementBounds.Fixed(392, 202, 240, 30),
                    ElementKey
                );

            double sliderY = 240;
            for (int index = 0; index < 6; index++)
            {
                int component = index;
                ElementBounds sliderBounds =
                    ElementBounds.Fixed(130, sliderY, 390, 25);
                composer
                    .AddStaticText(
                        ComponentLabels[index],
                        body,
                        ElementBounds.Fixed(18, sliderY + 4, 112, 25)
                    )
                    .AddInteractiveElement(
                        new TransactionalSlider(
                            capi,
                            value => OnValueChanged(component, value),
                            sliderBounds,
                            editor.BeginValueEdit,
                            editor.EndValueEdit
                        ),
                        sliderKeys[index]
                    )
                    .AddDynamicText(
                        "0.0",
                        body,
                        ElementBounds.Fixed(532, sliderY + 4, 92, 25),
                        valueKeys[index]
                    );
                sliderY += 34;
            }

            double row = sliderY + 8;
            AddButtonRow(
                composer,
                row,
                ("Undo", editor.Undo),
                ("Redo", editor.Redo),
                ("Copy frame", editor.CopySelectedFrame),
                ("Paste frame", editor.PasteSelectedFrame)
            );
            row += 34;
            AddButtonRow(
                composer,
                row,
                ("Reset frame", editor.ResetSelectedFrame),
                ("Reset all", editor.ResetAll),
                ("Reload asset", editor.ReloadPackagedAsset),
                ("Reload working", editor.ReloadExport)
            );
            row += 38;

            composer
                .AddStaticText(
                    "Timeline",
                    header,
                    ElementBounds.Fixed(18, row + 4, 72, 25)
                )
                .AddSlider(
                    OnTimelineChanged,
                    ElementBounds.Fixed(92, row, 530, 25),
                    TimelineKey
                );
            row += 34;

            AddButtonRow(
                composer,
                row,
                (editor.Playing ? "Pause" : "Play", editor.TogglePlay),
                ("Stop", editor.StopPlayback),
                ("Frame <", () => editor.StepRenderedFrame(-1)),
                ("Frame >", () => editor.StepRenderedFrame(1))
            );
            row += 34;
            AddButtonRow(
                composer,
                row,
                ("Key <", () => editor.StepKeyFrame(-1)),
                ("Key >", () => editor.StepKeyFrame(1)),
                ("Speed -", () => editor.AdjustPlaybackSpeed(-0.1f)),
                ("Speed +", () => editor.AdjustPlaybackSpeed(0.1f))
            );
            row += 34;
            AddButtonRow(
                composer,
                row,
                ("100%", () => editor.SetPreviewFraction(1)),
                (
                    editor.LoopPlayback ? "Loop: on" : "Loop: off",
                    editor.ToggleLoop
                ),
                (
                    editor.MarkersVisible ? "Markers: on" : "Markers: off",
                    editor.ToggleMarkers
                ),
                ("Save working", editor.SaveWorking)
            );
            row += 38;

            AddButtonRow(
                composer,
                row,
                ("Export accepted JSON", editor.Export)
            );
            row += 34;

            SingleComposer = composer.Compose();
            ConfigureSliders();
            composing = false;
            RefreshDynamicState();
        }

        private void ConfigureSliders()
        {
            float[] values = editor.GetSelectedValues();
            for (int index = 0; index < sliderKeys.Length; index++)
            {
                int minimum = index < 3 ? -320 : -1800;
                int maximum = index < 3 ? 320 : 1800;
                GuiElementSlider slider =
                    SingleComposer.GetSlider(sliderKeys[index]);
                slider.SetValues(
                    (int)Math.Round(values[index] * 10),
                    minimum,
                    maximum,
                    1,
                    ""
                );
            }

            SingleComposer.GetSlider(TimelineKey).SetValues(
                (int)Math.Round(editor.PreviewTime * 1000),
                0,
                (int)Math.Round(
                    editor.WorkingDefinition.DurationSeconds * 1000
                ),
                1,
                "ms"
            );
        }

        private void RefreshDynamicState()
        {
            if (SingleComposer == null) return;

            SingleComposer.GetDynamicText(StatusKey)?.SetNewText(
                editor.BuildStatusText()
            );
            float[] values = editor.GetSelectedValues();
            for (int index = 0; index < valueKeys.Length; index++)
            {
                SingleComposer.GetDynamicText(valueKeys[index])?.SetNewText(
                    values[index].ToString(
                        "0.0",
                        CultureInfo.InvariantCulture
                    )
                );
            }

            composing = true;
            SingleComposer.GetSlider(TimelineKey)?.SetValue(
                (int)Math.Round(editor.PreviewTime * 1000)
            );
            composing = false;
        }

        private void RefreshSelection()
        {
            composing = true;
            float[] values = editor.GetSelectedValues();
            for (int index = 0; index < sliderKeys.Length; index++)
            {
                SingleComposer.GetSlider(sliderKeys[index])?.SetValue(
                    (int)Math.Round(values[index] * 10)
                );
            }
            SingleComposer.GetDropDown(FrameKey)?.SetSelectedIndex(
                editor.SelectedFrameIndex
            );
            SingleComposer.GetDropDown(ElementKey)?.SetSelectedIndex(
                editor.SelectedElementIndex
            );
            composing = false;
            RefreshDynamicState();
        }

        private void OnFrameChanged(string code, bool selected)
        {
            if (!selected || composing ||
                !int.TryParse(
                    code,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int index))
            {
                return;
            }

            editor.SelectFrame(index);
            RefreshSelection();
        }

        private void OnElementChanged(string code, bool selected)
        {
            if (!selected || composing ||
                !int.TryParse(
                    code,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int index))
            {
                return;
            }

            editor.SelectElement(index);
            RefreshSelection();
        }

        private bool OnValueChanged(int component, int value)
        {
            if (composing) return true;
            try
            {
                editor.SetSelectedValue(component, value / 10f);
                RefreshDynamicState();
            }
            catch (Exception exception)
            {
                editor.ReportUiFailure(
                    ComponentLabels[component] + " slider",
                    exception
                );
            }
            return true;
        }

        private bool OnTimelineChanged(int value)
        {
            if (composing) return true;
            editor.SetPreviewMilliseconds(value);
            RefreshDynamicState();
            return true;
        }

        private void AddButtonRow(
            GuiComposer composer,
            double y,
            params (string Label, Action Handler)[] buttons)
        {
            const double Gap = 6;
            double width = (614 - Gap * (buttons.Length - 1)) /
                buttons.Length;
            double x = 18;
            for (int index = 0; index < buttons.Length; index++)
            {
                (string label, Action handler) = buttons[index];
                composer.AddButton(
                    label,
                    () =>
                    {
                        try
                        {
                            handler();
                            RefreshSelection();
                        }
                        catch (Exception exception)
                        {
                            editor.ReportUiFailure(label, exception);
                            try
                            {
                                RefreshDynamicState();
                            }
                            catch (Exception refreshException)
                            {
                                editor.ReportUiFailure(
                                    label + " UI refresh",
                                    refreshException
                                );
                            }
                        }
                        return true;
                    },
                    ElementBounds.Fixed(x, y, width, 26),
                    CairoFont.WhiteSmallText(),
                    EnumButtonStyle.Normal,
                    $"scythe-editor-button-{y:0}-{index}"
                );
                x += width + Gap;
            }
        }

        private void RenderPlayerViewport(float deltaTime)
        {
            if (viewportBounds == null ||
                capi.World?.Player?.Entity == null)
            {
                return;
            }

            float size = (float)Math.Min(
                viewportBounds.InnerHeight * 0.86,
                viewportBounds.InnerWidth * 0.66
            );
            double posX = viewportBounds.renderX +
                viewportBounds.InnerWidth / 2 - size * 0.28;
            double posY = viewportBounds.renderY +
                viewportBounds.InnerHeight / 2 - size * 0.52;
            double posZ = GuiElement.scaled(250);

            capi.Render.GlPushMatrix();
            capi.Render.GlTranslate(0f, 0f, 150f);
            capi.Render.GlRotate(-12f, 1f, 0f, 0f);
            viewportLightMatrix.Identity();
            viewportLightMatrix.RotateXDeg(-12f);
            Vec4f light = viewportLightMatrix.TransformVector(
                viewportLightPosition
            );
            capi.Render.CurrentActiveShader?.Uniform(
                "lightPosition",
                light.X,
                light.Y,
                light.Z
            );

            capi.Render.PushScissor(viewportBounds, false);
            capi.Render.RenderEntityToGui(
                deltaTime,
                capi.World.Player.Entity,
                posX,
                posY,
                posZ,
                0f,
                size,
                ColorUtil.WhiteArgb
            );
            capi.Render.PopScissor();
            capi.Render.CurrentActiveShader?.Uniform(
                "lightPosition",
                0.7071068f,
                -0.7071068f,
                0f
            );
            capi.Render.GlPopMatrix();
        }

        private sealed class TransactionalSlider : GuiElementSlider
        {
            private readonly Action beginEdit;
            private readonly Action endEdit;
            private bool mouseEditActive;

            public TransactionalSlider(
                ICoreClientAPI api,
                ActionConsumable<int> onValueChanged,
                ElementBounds bounds,
                Action beginEdit,
                Action endEdit)
                : base(api, onValueChanged, bounds)
            {
                this.beginEdit = beginEdit;
                this.endEdit = endEdit;
            }

            public override void OnMouseDownOnElement(
                ICoreClientAPI api,
                MouseEvent args)
            {
                beginEdit();
                mouseEditActive = true;
                try
                {
                    base.OnMouseDownOnElement(api, args);
                }
                catch
                {
                    mouseEditActive = false;
                    endEdit();
                    throw;
                }
            }

            public override void OnMouseUp(
                ICoreClientAPI api,
                MouseEvent args)
            {
                try
                {
                    base.OnMouseUp(api, args);
                }
                finally
                {
                    if (mouseEditActive)
                    {
                        mouseEditActive = false;
                        endEdit();
                    }
                }
            }

            public override void OnMouseWheel(
                ICoreClientAPI api,
                MouseWheelEventArgs args)
            {
                beginEdit();
                try
                {
                    base.OnMouseWheel(api, args);
                }
                finally
                {
                    endEdit();
                }
            }

            public override void OnKeyDown(
                ICoreClientAPI api,
                KeyEvent args)
            {
                beginEdit();
                try
                {
                    base.OnKeyDown(api, args);
                }
                finally
                {
                    endEdit();
                }
            }
        }

        private string[] FrameCodes()
        {
            string[] codes = new string[
                editor.WorkingDefinition.Animation
                    .PlayerKeyFrames.Count
            ];
            for (int index = 0; index < codes.Length; index++)
            {
                codes[index] = index.ToString(
                    CultureInfo.InvariantCulture
                );
            }
            return codes;
        }

        private static string[] ElementCodes()
        {
            string[] codes = new string[
                WarScytheAnimationEditor.ControlledElements.Length
            ];
            for (int index = 0; index < codes.Length; index++)
            {
                codes[index] = index.ToString(
                    CultureInfo.InvariantCulture
                );
            }
            return codes;
        }
    }
}
