using System;
using System.Globalization;
using System.Numerics;

using ImGuiNET;

using Vintagestory.API.Client;

using VSImGui.API;

namespace Apprentice.ClientTools
{
    internal sealed class WarScytheImGuiEditorWindow :
        ImGuiDialogBase
    {
        private const string WindowTitle =
            "Apprentice item animation editor##apprentice-item-editor";

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

        private bool disposed;

        public WarScytheImGuiEditorWindow(
            ICoreClientAPI api,
            WarScytheAnimationEditor editor)
            : base(api)
        {
            this.editor = editor;
        }

        public bool IsOpen => Opened;

        public bool TryOpen()
        {
            if (disposed) return false;
            Open();
            return Opened;
        }

        protected override bool OnOpen()
        {
            if (disposed) return false;
            editor.ActivatePreview();
            return true;
        }

        protected override bool OnClose()
        {
            if (!Opened) return true;
            editor.DeactivatePreview();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing && Opened)
                {
                    Close();
                }
                disposed = true;
            }
            base.Dispose(disposing);
        }

        protected override bool OnDraw()
        {
            bool keepOpen = true;
            ImGui.SetNextWindowSize(
                new Vector2(760, 690),
                ImGuiCond.FirstUseEver
            );
            ImGui.SetNextWindowSizeConstraints(
                new Vector2(610, 520),
                new Vector2(float.MaxValue, float.MaxValue)
            );

            if (ImGui.Begin(
                    WindowTitle,
                    ref keepOpen,
                    ImGuiWindowFlags.NoCollapse))
            {
                DrawHeader();

                if (ImGui.BeginTabBar(
                        "war-scythe-editor-tabs",
                        ImGuiTabBarFlags.None))
                {
                    if (ImGui.BeginTabItem("Pose"))
                    {
                        DrawPoseTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem(
                            "Playback and files"))
                    {
                        DrawPlaybackAndFilesTab();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Diagnostics"))
                    {
                        DrawDiagnosticsTab();
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.End();

            return keepOpen && !disposed;
        }

        protected override CallbackGUIStatus Draw(
            float deltaSeconds)
        {
            if (!Opened)
            {
                return CallbackGUIStatus.Closed;
            }

            if (!OnDraw())
            {
                Close();
            }

            return Opened
                ? CallbackGUIStatus.GrabMouse
                : CallbackGUIStatus.Closed;
        }

        private void DrawHeader()
        {
            ImGui.TextUnformatted(
                editor.SelectedItemCode
            );
            ImGui.SameLine();
            ImGui.TextDisabled(
                "Animation -> PlayerItemFrame -> ElementPose"
            );
            ImGui.Separator();
        }

        private void DrawPoseTab()
        {
            DrawSelection();
            ImGui.SeparatorText("Selected transform");

            float[] values = editor.GetSelectedValues();
            for (int component = 0;
                component < ComponentLabels.Length;
                component++)
            {
                float value = values[component];
                float minimum = component < 3 ? -32f : -180f;
                float maximum = component < 3 ? 32f : 180f;

                ImGui.SetNextItemWidth(-1);
                bool changed = ImGui.SliderFloat(
                    ComponentLabels[component] +
                        "##war-scythe-component-" + component,
                    ref value,
                    minimum,
                    maximum,
                    "%.1f"
                );
                bool activated = ImGui.IsItemActivated();
                bool deactivated =
                    ImGui.IsItemDeactivatedAfterEdit();

                if (activated)
                {
                    SafeAction(
                        ComponentLabels[component] + " begin",
                        editor.BeginValueEdit
                    );
                }
                if (changed)
                {
                    int selectedComponent = component;
                    float selectedValue = value;
                    SafeAction(
                        ComponentLabels[component],
                        () => editor.SetSelectedValue(
                            selectedComponent,
                            selectedValue
                        )
                    );
                }
                if (deactivated)
                {
                    SafeAction(
                        ComponentLabels[component] + " end",
                        editor.EndValueEdit
                    );
                }
            }

            ImGui.Spacing();
            DrawPoseActions();
        }

        private void DrawSelection()
        {
            string[] animations = editor.AnimationLabels();
            int animation = editor.SelectedAnimationIndex;
            ImGui.SetNextItemWidth(420);
            if (ImGui.Combo(
                    "Animation##item-animation",
                    ref animation,
                    animations,
                    animations.Length))
            {
                int selected = animation;
                SafeAction(
                    "Select animation",
                    () => editor.SelectAnimation(selected)
                );
            }

            string[] frames = editor.FrameCodes();
            int frame = editor.SelectedFrameIndex;
            ImGui.SetNextItemWidth(280);
            if (ImGui.Combo(
                    "Keyframe##war-scythe-frame",
                    ref frame,
                    frames,
                    frames.Length))
            {
                int selected = frame;
                SafeAction(
                    "Select keyframe",
                    () => editor.SelectFrame(selected)
                );
            }

            int element = editor.SelectedElementIndex;
            ImGui.SetNextItemWidth(280);
            if (ImGui.Combo(
                    "Element##war-scythe-element",
                    ref element,
                    WarScytheAnimationEditor.ControlledElements,
                    WarScytheAnimationEditor.ControlledElements.Length))
            {
                int selected = element;
                SafeAction(
                    "Select element",
                    () => editor.SelectElement(selected)
                );
            }
        }

        private void DrawPoseActions()
        {
            if (ImGui.Button("Undo"))
            {
                SafeAction("Undo", editor.Undo);
            }
            ImGui.SameLine();
            if (ImGui.Button("Redo"))
            {
                SafeAction("Redo", editor.Redo);
            }
            ImGui.SameLine();
            if (ImGui.Button("Copy frame"))
            {
                SafeAction(
                    "Copy frame",
                    editor.CopySelectedFrame
                );
            }
            ImGui.SameLine();
            if (ImGui.Button("Paste frame"))
            {
                SafeAction(
                    "Paste frame",
                    editor.PasteSelectedFrame
                );
            }

            if (ImGui.Button("Reset selected frame"))
            {
                SafeAction(
                    "Reset selected frame",
                    editor.ResetSelectedFrame
                );
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset all frames"))
            {
                SafeAction("Reset all frames", editor.ResetAll);
            }
        }

        private void DrawPlaybackAndFilesTab()
        {
            ImGui.SeparatorText("Timeline");

            float previewTime = editor.PreviewTime;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.SliderFloat(
                    "Time##war-scythe-timeline",
                    ref previewTime,
                    0,
                    editor.DurationSeconds,
                    "%.3f s"))
            {
                float selectedTime = previewTime;
                SafeAction(
                    "Timeline",
                    () => editor.SetPreviewMilliseconds(
                        (int)Math.Round(
                            selectedTime * 1000f
                        )
                    )
                );
            }

            if (ImGui.Button(editor.Playing ? "Pause" : "Play"))
            {
                SafeAction("Play or pause", editor.TogglePlay);
            }
            ImGui.SameLine();
            if (ImGui.Button("Stop"))
            {
                SafeAction("Stop", editor.StopPlayback);
            }
            ImGui.SameLine();
            if (ImGui.Button("Rendered frame <"))
            {
                SafeAction(
                    "Previous rendered frame",
                    () => editor.StepRenderedFrame(-1)
                );
            }
            ImGui.SameLine();
            if (ImGui.Button("Rendered frame >"))
            {
                SafeAction(
                    "Next rendered frame",
                    () => editor.StepRenderedFrame(1)
                );
            }

            if (ImGui.Button("Keyframe <"))
            {
                SafeAction(
                    "Previous keyframe",
                    () => editor.StepKeyFrame(-1)
                );
            }
            ImGui.SameLine();
            if (ImGui.Button("Keyframe >"))
            {
                SafeAction(
                    "Next keyframe",
                    () => editor.StepKeyFrame(1)
                );
            }
            ImGui.SameLine();
            if (ImGui.Button("Speed -"))
            {
                SafeAction(
                    "Decrease speed",
                    () => editor.AdjustPlaybackSpeed(-0.1f)
                );
            }
            ImGui.SameLine();
            if (ImGui.Button("Speed +"))
            {
                SafeAction(
                    "Increase speed",
                    () => editor.AdjustPlaybackSpeed(0.1f)
                );
            }

            bool loop = editor.LoopPlayback;
            if (ImGui.Checkbox(
                    "Loop playback##war-scythe-loop",
                    ref loop))
            {
                SafeAction("Toggle loop", editor.ToggleLoop);
            }
            ImGui.SameLine();
            bool markers = editor.MarkersVisible;
            if (ImGui.Checkbox(
                    "Geometry markers##war-scythe-markers",
                    ref markers))
            {
                SafeAction(
                    "Toggle geometry markers",
                    editor.ToggleMarkers
                );
            }

            ImGui.SeparatorText("Working and accepted JSON");

            if (ImGui.Button("Save working"))
            {
                SafeAction("Save working", editor.SaveWorking);
            }
            ImGui.SameLine();
            if (ImGui.Button("Export accepted JSON"))
            {
                SafeAction(
                    "Export accepted JSON",
                    editor.Export
                );
            }

            if (ImGui.Button("Reload working"))
            {
                SafeAction(
                    "Reload working",
                    editor.ReloadExport
                );
            }
            ImGui.SameLine();
            if (ImGui.Button("Reload packaged asset"))
            {
                SafeAction(
                    "Reload packaged asset",
                    editor.ReloadPackagedAsset
                );
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Working file");
            ImGui.TextWrapped(editor.WorkingPath);
            ImGui.TextDisabled("Accepted export");
            ImGui.TextWrapped(editor.ExportPath);
        }

        private void DrawDiagnosticsTab()
        {
            ImGui.TextWrapped(editor.BuildStatusText());
            ImGui.Spacing();

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Frame {0}/{1} | Element {2} | Time {3:0.000}/{4:0.000}s | Speed {5:0.0}x",
                editor.SelectedFrameIndex + 1,
                editor.FrameCount,
                editor.SelectedElement,
                editor.PreviewTime,
                editor.DurationSeconds,
                editor.PlaybackSpeed
            );
            ImGui.Separator();
            ImGui.TextUnformatted(summary);
        }

        private void SafeAction(
            string action,
            Action callback)
        {
            try
            {
                callback();
            }
            catch (Exception exception)
            {
                editor.ReportUiFailure(action, exception);
            }
        }
    }
}
