using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Apprentice.AnimationReference;
using Animation =
    Apprentice.AnimationReference.Animation;

using Newtonsoft.Json;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

namespace Apprentice
{
    internal sealed class WarScytheAnimationEditor : IDisposable
    {
        public static readonly string[] ControlledElements =
        {
            "ItemAnchor",
            "ItemAnchorL",
            "UpperArmR",
            "LowerArmR",
            "UpperArmL",
            "LowerArmL"
        };

        private const string HotKeyCode =
            "apprentice-war-scythe-editor";

        private readonly ICoreClientAPI api;
        private readonly ApprenticeAnimationSystem animationSystem;
        private readonly ApprenticeAnimationDefinition sourceDefinition;
        private readonly WarScytheGeometryProbe geometryProbe;
        private readonly WarScytheCalibrationRenderer markerRenderer;
        private readonly AnimationEditorHistory history = new();
        private readonly Dictionary<string, Animation> editableAnimations =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> reachedElements =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly string workingPath;
        private readonly string exportPath;

        private ApprenticeAnimationDefinition workingDefinition;
        private WarScytheAnimationEditorDialog? dialog;
        private WarScytheGeometryTrace playbackTrace;
        private WarScytheGeometrySample latestGeometry;
        private string copiedFrame = string.Empty;
        private string latestPlaybackStatus = "not-run";
        private string statusMessage =
            "Equip the War Scythe, then open the editor.";
        private float previewTime;
        private float playbackSpeed = 1f;
        private int selectedFrameIndex;
        private int selectedElementIndex;
        private bool previewActive;
        private bool playing;
        private bool loopPlayback = true;
        private bool markersVisible = true;
        private bool geometryAvailable;
        private bool fullPlaybackComplete;
        private bool latestPlaybackContractPass;
        private bool valueEditActive;
        private bool valueEditChanged;
        private bool disposed;

        public WarScytheAnimationEditor(
            ICoreClientAPI api,
            ApprenticeAnimationSystem animationSystem,
            ApprenticeAnimationDefinition sourceDefinition,
            WarScytheGeometryProbe geometryProbe)
        {
            this.api = api;
            this.animationSystem = animationSystem;
            this.sourceDefinition = sourceDefinition;
            this.geometryProbe = geometryProbe;
            workingDefinition = sourceDefinition.DeepClone();
            editableAnimations[workingDefinition.Code] =
                workingDefinition.Animation;
            playbackTrace = NewTrace();

            string authoringDirectory = Path.Combine(
                GamePaths.DataPath,
                "ModConfig",
                "ApprenticeAuthoring"
            );
            workingPath = Path.Combine(
                authoringDirectory,
                "war-scythe-working.json"
            );
            exportPath = Path.Combine(
                authoringDirectory,
                "war-scythe.json"
            );

            markerRenderer = new WarScytheCalibrationRenderer(
                api,
                this
            );

            api.Input.RegisterHotKey(
                HotKeyCode,
                "Apprentice War Scythe reference animation editor",
                GlKeys.K,
                HotkeyType.GUIOrOtherControls,
                ctrlPressed: true,
                shiftPressed: true
            );
            api.Input.SetHotKeyHandler(
                HotKeyCode,
                _ => ToggleDialog()
            );
            api.ChatCommands.Create("scytheeditor")
                .WithDescription(
                    "Open the Apprentice War Scythe reference-pipeline editor"
                )
                .HandleWith(_ =>
                {
                    ToggleDialog();
                    return TextCommandResult.Success(
                        "War Scythe editor toggled."
                    );
                });

            api.Logger.Notification(
                "[Apprentice] War Scythe reference editor ready: command=.scytheeditor; hotkey=Ctrl+Shift+K; working={0}; accepted={1}.",
                workingPath,
                exportPath
            );
        }

        public bool PreviewActive => previewActive;
        public bool MarkersVisible =>
            previewActive && markersVisible;
        public bool Playing => playing;
        public bool LoopPlayback => loopPlayback;
        public float PreviewTime => previewTime;
        public float PlaybackSpeed => playbackSpeed;
        public int SelectedFrameIndex => selectedFrameIndex;
        public int SelectedElementIndex => selectedElementIndex;
        public string SelectedElement =>
            ControlledElements[selectedElementIndex];
        public ApprenticeAnimationDefinition WorkingDefinition =>
            workingDefinition;
        public string ExportPath => exportPath;

        public bool ToggleDialog()
        {
            if (disposed) return false;
            dialog ??= new WarScytheAnimationEditorDialog(
                api,
                this
            );

            if (dialog.IsOpened())
            {
                dialog.TryClose();
                return true;
            }
            if (!HasHeldWarScythe())
            {
                statusMessage =
                    "Equip apprentice:warscythe before opening the editor.";
                api.ShowChatMessage(
                    "[Apprentice] Equip the War Scythe first."
                );
                return false;
            }

            return dialog.TryOpen();
        }

        public void ActivatePreview()
        {
            if (disposed || !HasHeldWarScythe()) return;

            animationSystem.EnterEditorMode();
            previewActive = true;
            playing = false;
            previewTime = FrameTimeSeconds(selectedFrameIndex);
            reachedElements.Clear();
            geometryAvailable = false;
            latestPlaybackStatus = "not-run";
            fullPlaybackComplete = false;
            latestPlaybackContractPass = false;
            playbackTrace = NewTrace();
            statusMessage =
                "Reference preview active. The editor and gameplay use the same PlayerItemFrame path.";
            UpdatePreviewFrame();
        }

        public void DeactivatePreview()
        {
            EndValueEdit();
            previewActive = false;
            playing = false;
            geometryAvailable = false;
            reachedElements.Clear();
            animationSystem.SetEditorFrameOverride(null);
            statusMessage =
                "Preview closed; the packaged asset was not modified.";
        }

        public void Tick(float deltaTime)
        {
            if (!previewActive || disposed) return;
            if (!HasHeldWarScythe())
            {
                statusMessage =
                    "Preview stopped because the War Scythe is no longer held.";
                dialog?.TryClose();
                return;
            }

            if (playing)
            {
                previewTime += Math.Max(0, deltaTime) *
                    playbackSpeed;
                if (previewTime >=
                    workingDefinition.DurationSeconds)
                {
                    latestPlaybackStatus =
                        playbackTrace.BuildStatus(0);
                    fullPlaybackComplete = true;
                    latestPlaybackContractPass =
                        playbackTrace.ContractPass;
                    if (loopPlayback)
                    {
                        previewTime %=
                            workingDefinition.DurationSeconds;
                        playbackTrace = NewTrace();
                    }
                    else
                    {
                        previewTime =
                            workingDefinition.DurationSeconds;
                        playing = false;
                    }
                }
                UpdatePreviewFrame();
            }

            EntityAgent entity = api.World.Player.Entity;
            ItemStack? stack =
                entity.RightHandItemSlot?.Itemstack;
            geometryAvailable = stack != null &&
                geometryProbe.TrySample(
                    entity,
                    stack,
                    out latestGeometry
                );
            if (geometryAvailable && playing)
            {
                playbackTrace.Record(
                    latestGeometry,
                    previewTime,
                    workingDefinition.Callbacks[0].TimeSeconds,
                    workingDefinition.Callbacks[2].TimeSeconds
                );
            }
        }

        public void NoteHookElement(string elementName)
        {
            if (!previewActive ||
                !ControlledElements.Contains(
                    elementName,
                    StringComparer.OrdinalIgnoreCase))
            {
                return;
            }
            reachedElements.Add(elementName);
        }

        public bool TryGetDebugGeometry(
            out WarScytheDebugGeometry geometry)
        {
            geometry = default;
            if (!MarkersVisible || !HasHeldWarScythe())
            {
                return false;
            }

            EntityAgent entity = api.World.Player.Entity;
            ItemStack? stack =
                entity.RightHandItemSlot?.Itemstack;
            return stack != null &&
                geometryProbe.TryBuildDebugGeometry(
                    entity,
                    stack,
                    out geometry
                );
        }

        public float[] GetSelectedValues() =>
            ReferenceAnimationEditing.GetValues(
                workingDefinition.Animation,
                selectedFrameIndex,
                SelectedElement
            );

        public void SelectFrame(int index)
        {
            EndValueEdit();
            selectedFrameIndex = Math.Clamp(
                index,
                0,
                workingDefinition.Animation.PlayerKeyFrames.Count -
                    1
            );
            previewTime = FrameTimeSeconds(selectedFrameIndex);
            playing = false;
            UpdatePreviewFrame();
            statusMessage =
                $"Selected keyframe {selectedFrameIndex + 1}.";
        }

        public void SelectElement(int index)
        {
            EndValueEdit();
            selectedElementIndex = Math.Clamp(
                index,
                0,
                ControlledElements.Length - 1
            );
            statusMessage = $"Selected {SelectedElement}.";
        }

        public void SetSelectedValue(
            int component,
            float value)
        {
            if (component < 0 || component >= 6 ||
                !float.IsFinite(value))
            {
                return;
            }

            float[] values = GetSelectedValues();
            if (Math.Abs(values[component] - value) < 0.0001f)
            {
                return;
            }

            bool implicitTransaction = !valueEditActive;
            if (implicitTransaction) BeginValueEdit();
            ReferenceAnimationEditing.SetComponent(
                workingDefinition.Animation,
                selectedFrameIndex,
                SelectedElement,
                component,
                value
            );
            valueEditChanged = true;
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
            statusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} = {2:0.0}; reference frame updated live",
                SelectedElement,
                ComponentName(component),
                value
            );
            if (implicitTransaction) EndValueEdit();
        }

        public void BeginValueEdit()
        {
            if (valueEditActive) return;
            valueEditActive = true;
            valueEditChanged = false;
            history.BeginEdit(
                workingDefinition.Code,
                workingDefinition.Animation,
                $"{SelectedElement} slider drag"
            );
        }

        public void EndValueEdit()
        {
            if (!valueEditActive) return;

            if (valueEditChanged)
            {
                history.CommitEdit(
                    workingDefinition.Code,
                    workingDefinition.Animation
                );
            }
            else
            {
                history.CancelPendingEdit();
            }
            valueEditActive = false;
            valueEditChanged = false;
        }

        public void SetPreviewMilliseconds(int milliseconds)
        {
            previewTime = Math.Clamp(
                milliseconds / 1000f,
                0,
                workingDefinition.DurationSeconds
            );
            playing = false;
            UpdatePreviewFrame();
        }

        public void SetPreviewFraction(float fraction)
        {
            previewTime = workingDefinition.DurationSeconds *
                Math.Clamp(fraction, 0, 1);
            playing = false;
            UpdatePreviewFrame();
        }

        public void StepRenderedFrame(int direction)
        {
            SetPreviewMilliseconds((int)Math.Round(
                (previewTime + Math.Sign(direction) / 30f) *
                    1000f
            ));
        }

        public void StepKeyFrame(int direction)
        {
            int next =
                selectedFrameIndex + Math.Sign(direction);
            int count =
                workingDefinition.Animation.PlayerKeyFrames.Count;
            if (next < 0) next = count - 1;
            if (next >= count) next = 0;
            SelectFrame(next);
        }

        public void TogglePlay()
        {
            if (playing)
            {
                playing = false;
                statusMessage = "Playback paused.";
                return;
            }

            if (previewTime >=
                workingDefinition.DurationSeconds)
            {
                previewTime = 0;
            }
            playbackTrace = NewTrace();
            latestPlaybackStatus = "running";
            fullPlaybackComplete = false;
            latestPlaybackContractPass = false;
            playing = true;
            UpdatePreviewFrame();
            statusMessage =
                "Playback samples the same reference Animation used by gameplay.";
        }

        public void StopPlayback()
        {
            playing = false;
            previewTime = FrameTimeSeconds(selectedFrameIndex);
            UpdatePreviewFrame();
            statusMessage =
                "Playback stopped at the selected keyframe.";
        }

        public void AdjustPlaybackSpeed(float delta)
        {
            playbackSpeed = Math.Clamp(
                playbackSpeed + delta,
                0.1f,
                2f
            );
            statusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "Playback speed {0:0.0}x.",
                playbackSpeed
            );
        }

        public void ToggleLoop()
        {
            loopPlayback = !loopPlayback;
            statusMessage = loopPlayback
                ? "Playback loop enabled."
                : "Playback loop disabled.";
        }

        public void ToggleMarkers()
        {
            markersVisible = !markersVisible;
            statusMessage = markersVisible
                ? "Grip, blade, torso, and head markers visible."
                : "Geometry markers hidden.";
        }

        public void CopySelectedFrame()
        {
            copiedFrame = JsonConvert.SerializeObject(
                PLayerKeyFrameJson.FromKeyFrame(
                    workingDefinition.Animation.PlayerKeyFrames[
                        selectedFrameIndex]
                ),
                Formatting.Indented
            );
            statusMessage =
                $"Copied keyframe {selectedFrameIndex + 1}.";
        }

        public void PasteSelectedFrame()
        {
            if (string.IsNullOrWhiteSpace(copiedFrame))
            {
                statusMessage =
                    "No copied keyframe is available.";
                return;
            }

            PLayerKeyFrameJson parsed =
                JsonConvert.DeserializeObject<PLayerKeyFrameJson>(
                    copiedFrame
                ) ?? throw new InvalidOperationException(
                    "The copied reference keyframe is invalid."
                );
            PLayerKeyFrame replacement =
                ReferenceAnimationEditing.WithTime(
                    parsed.ToKeyFrame(),
                    workingDefinition.Animation.PlayerKeyFrames[
                        selectedFrameIndex].Time
                );
            PerformEdit("Paste frame", () =>
            {
                workingDefinition.Animation.PlayerKeyFrames[
                    selectedFrameIndex] = replacement;
            });
            previewTime = FrameTimeSeconds(selectedFrameIndex);
            statusMessage =
                $"Pasted pose into keyframe {selectedFrameIndex + 1}.";
        }

        public void ResetSelectedFrame()
        {
            PLayerKeyFrame source =
                ReferenceAnimationEditing.CloneFrame(
                    sourceDefinition.Animation.PlayerKeyFrames[
                        selectedFrameIndex]
                );
            PerformEdit("Reset frame", () =>
            {
                workingDefinition.Animation.PlayerKeyFrames[
                    selectedFrameIndex] = source;
            });
            previewTime = FrameTimeSeconds(selectedFrameIndex);
            statusMessage =
                $"Reset keyframe {selectedFrameIndex + 1} to the packaged source.";
        }

        public void ResetAll()
        {
            Animation source =
                sourceDefinition.Animation.Clone();
            PerformEdit(
                "Reset all frames",
                () => ReplaceWorkingAnimation(source)
            );
            selectedFrameIndex = 0;
            selectedElementIndex = 0;
            previewTime = 0;
            playing = false;
            statusMessage =
                "Reset every working keyframe to the packaged source.";
        }

        public void Undo()
        {
            EndValueEdit();
            if (!history.Undo(
                    workingDefinition.Code,
                    editableAnimations,
                    out string status))
            {
                statusMessage = status;
                return;
            }

            workingDefinition.ReplaceAnimation(
                editableAnimations[workingDefinition.Code]
            );
            ClampSelection();
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
            statusMessage = status;
        }

        public void Redo()
        {
            EndValueEdit();
            if (!history.Redo(
                    workingDefinition.Code,
                    editableAnimations,
                    out string status))
            {
                statusMessage = status;
                return;
            }

            workingDefinition.ReplaceAnimation(
                editableAnimations[workingDefinition.Code]
            );
            ClampSelection();
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
            statusMessage = status;
        }

        public void SaveWorking()
        {
            WriteDefinition(
                workingPath,
                requireReadyPoseLoop: false
            );
            statusMessage =
                "Working reference JSON saved and copied to the clipboard.";
        }

        public void Export()
        {
            if (!CanExport(out string reason))
            {
                statusMessage = "Export blocked: " + reason;
                return;
            }

            WriteDefinition(
                exportPath,
                requireReadyPoseLoop: true
            );
            statusMessage =
                "Accepted reference JSON exported to file and clipboard.";
            api.Logger.Notification(
                "[Apprentice] WARSCYTHE REFERENCE EDITOR EXPORT path={0}; elements={1}; playback=[{2}]",
                exportPath,
                string.Join(
                    ",",
                    reachedElements.OrderBy(
                        value => value,
                        StringComparer.Ordinal
                    )
                ),
                latestPlaybackStatus
            );
        }

        public void ReloadExport()
        {
            if (!File.Exists(workingPath))
            {
                statusMessage =
                    "No working reference JSON exists yet.";
                return;
            }

            ApprenticeAnimationDefinition reloaded = ParseDraft(
                File.ReadAllText(workingPath),
                "working-file"
            );
            ReplaceFromDefinition(
                reloaded,
                "Reload working JSON"
            );
            statusMessage =
                "Reloaded the working reference JSON file.";
        }

        public void ReloadPackagedAsset()
        {
            ApprenticeAnimationDefinition reloaded =
                ApprenticeAnimationDefinition.LoadWarScythe(api);
            ReplaceFromDefinition(
                reloaded,
                "Reload packaged asset"
            );
            statusMessage =
                "Reloaded the packaged reference animation asset.";
        }

        public void ReportUiFailure(
            string action,
            Exception exception)
        {
            history.CancelPendingEdit();
            valueEditActive = false;
            valueEditChanged = false;
            statusMessage =
                $"{action} failed: {exception.Message}";
            api.Logger.Error(
                "[Apprentice] WARSCYTHE REFERENCE EDITOR action failed: {0}",
                action
            );
            api.Logger.Error(exception);
        }

        public string BuildStatusText()
        {
            string missing = string.Join(
                ", ",
                ControlledElements.Where(element =>
                    !reachedElements.Contains(element))
            );
            string hooks = missing.Length == 0
                ? "all six reference elements reached"
                : "missing: " + missing;

            string geometry = geometryAvailable
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    "grips R={0:0.###} L={1:0.###} | blade Y={2:0.###}..{3:0.###}\n" +
                    "torso Y={4:0.###}..{5:0.###} | head/neck starts {6:0.###}\n" +
                    "above head={7} overlap={8}",
                    latestGeometry.RightGripDistance,
                    latestGeometry.LeftGripDistance,
                    latestGeometry.BladeMinY,
                    latestGeometry.BladeMaxY,
                    latestGeometry.TorsoMinY,
                    latestGeometry.TorsoMaxY,
                    latestGeometry.HeadNeckMinY,
                    latestGeometry.BladeAboveHeadOrNeck,
                    latestGeometry.HeadOrNeckOverlap
                )
                : "geometry unavailable until the held model renders";

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}\n\nTime {1:0.000}/{2:0.000}s | speed {3:0.0}x | {4}\n" +
                "Frame {5}/{6} at {7:0.000}s | element {8}\n" +
                "Pipeline: reference Animation -> PlayerItemFrame -> OnFrameInvoke\n" +
                "Hook: {9}\n{10}\nPlayback gate: {11}",
                statusMessage,
                previewTime,
                workingDefinition.DurationSeconds,
                playbackSpeed,
                playing ? "playing" : "paused",
                selectedFrameIndex + 1,
                workingDefinition.Animation.PlayerKeyFrames.Count,
                FrameTimeSeconds(selectedFrameIndex),
                SelectedElement,
                hooks,
                geometry,
                latestPlaybackStatus
            );
        }

        public string[] FrameCodes() =>
            workingDefinition.Animation.PlayerKeyFrames
                .Select((frame, index) =>
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: {1:0.000}s",
                        index + 1,
                        frame.Time.TotalSeconds
                    ))
                .ToArray();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            previewActive = false;
            playing = false;
            history.CancelPendingEdit();
            animationSystem.SetEditorFrameOverride(null);
            dialog?.TryClose();
            dialog?.Dispose();
            dialog = null;
            markerRenderer.Dispose();
            editableAnimations.Clear();
            reachedElements.Clear();
        }

        private void PerformEdit(
            string label,
            Action edit)
        {
            EndValueEdit();
            history.BeginEdit(
                workingDefinition.Code,
                workingDefinition.Animation,
                label
            );
            try
            {
                edit();
                editableAnimations[workingDefinition.Code] =
                    workingDefinition.Animation;
                history.CommitEdit(
                    workingDefinition.Code,
                    workingDefinition.Animation
                );
            }
            catch
            {
                history.CancelPendingEdit();
                throw;
            }
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
        }

        private void ReplaceFromDefinition(
            ApprenticeAnimationDefinition replacement,
            string label)
        {
            PerformEdit(
                label,
                () => ReplaceWorkingAnimation(
                    replacement.Animation.Clone()
                )
            );
            ClampSelection();
        }

        private void ReplaceWorkingAnimation(Animation animation)
        {
            workingDefinition.ReplaceAnimation(animation);
            editableAnimations[workingDefinition.Code] = animation;
        }

        private bool CanExport(out string reason)
        {
            if (!workingDefinition.HasExactReadyPoseLoop(
                    out string differingElement))
            {
                reason =
                    "keyframe 1 and the final keyframe differ for " +
                    differingElement;
                return false;
            }

            string[] missing = ControlledElements.Where(element =>
                !reachedElements.Contains(element))
                .ToArray();
            if (missing.Length != 0)
            {
                reason =
                    "the reference pose hook has not reached " +
                    string.Join(", ", missing);
                return false;
            }
            if (!geometryAvailable)
            {
                reason = "live geometry is unavailable";
                return false;
            }
            if (playing)
            {
                reason = "pause playback before exporting";
                return false;
            }
            if (!fullPlaybackComplete)
            {
                reason =
                    "play the complete timeline after the last edit";
                return false;
            }
            if (!latestPlaybackContractPass)
            {
                reason =
                    "the complete playback acceptance contract failed";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool HasHeldWarScythe() =>
            api.World.Player?.Entity?.RightHandItemSlot?.Itemstack?
                .Item?.Code?.ToString() ==
            workingDefinition.HeldItemCode;

        private void UpdatePreviewFrame()
        {
            if (!previewActive) return;

            float duration =
                workingDefinition.DurationSeconds;
            float progress = duration <= 0
                ? 0
                : Math.Clamp(previewTime / duration, 0, 1);
            PlayerItemFrame frame =
                workingDefinition.Animation.StillFrame(progress);
            animationSystem.SetEditorFrameOverride(frame);
        }

        private void InvalidatePlaybackAcceptance()
        {
            fullPlaybackComplete = false;
            latestPlaybackContractPass = false;
            latestPlaybackStatus = "not-run-after-edit";
        }

        private void ClampSelection()
        {
            selectedFrameIndex = Math.Clamp(
                selectedFrameIndex,
                0,
                workingDefinition.Animation.PlayerKeyFrames.Count -
                    1
            );
            selectedElementIndex = Math.Clamp(
                selectedElementIndex,
                0,
                ControlledElements.Length - 1
            );
            previewTime = Math.Clamp(
                previewTime,
                0,
                workingDefinition.DurationSeconds
            );
            playing = false;
            editableAnimations[workingDefinition.Code] =
                workingDefinition.Animation;
            UpdatePreviewFrame();
        }

        private float FrameTimeSeconds(int index) =>
            (float)workingDefinition.Animation.PlayerKeyFrames[
                index].Time.TotalSeconds;

        private ApprenticeAnimationDefinition ParseDraft(
            string json,
            string source) =>
            ApprenticeAnimationDefinition.ParseWarScytheDraft(
                json,
                new AssetLocation(
                    "apprentice",
                    "editor/" + source + ".json"
                )
            );

        private WarScytheGeometryTrace NewTrace() =>
            new(geometryProbe.Acceptance);

        private void WriteDefinition(
            string path,
            bool requireReadyPoseLoop)
        {
            string json = workingDefinition.ToJson();
            if (requireReadyPoseLoop)
            {
                _ = ApprenticeAnimationDefinition.ParseWarScythe(
                    json,
                    new AssetLocation(
                        "apprentice",
                        "editor/accepted-write-validation.json"
                    )
                );
            }
            else
            {
                _ = ApprenticeAnimationDefinition
                    .ParseWarScytheDraft(
                        json,
                        new AssetLocation(
                            "apprentice",
                            "editor/working-write-validation.json"
                        )
                    );
            }

            Directory.CreateDirectory(
                Path.GetDirectoryName(path) ??
                throw new InvalidOperationException(
                    "Authoring path has no directory."
                )
            );
            File.WriteAllText(path, json);
            api.Forms.SetClipboardText(json);
        }

        private static string ComponentName(int component) =>
            component switch
            {
                0 => "offset X",
                1 => "offset Y",
                2 => "offset Z",
                3 => "rotation X",
                4 => "rotation Y",
                5 => "rotation Z",
                _ => "component"
            };
    }
}
