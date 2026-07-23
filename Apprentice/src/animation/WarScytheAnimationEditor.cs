using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

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
        private const int MaximumHistoryEntries = 64;

        private readonly ICoreClientAPI api;
        private readonly ApprenticeAnimationSystem animationSystem;
        private readonly ApprenticeAnimationDefinition sourceDefinition;
        private readonly WarScytheGeometryProbe geometryProbe;
        private readonly WarScytheCalibrationRenderer markerRenderer;
        private readonly Stack<string> undo = new();
        private readonly Stack<string> redo = new();
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
                "Apprentice War Scythe live editor",
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
                    "Open the Apprentice War Scythe live animation editor"
                )
                .HandleWith(_ =>
                {
                    ToggleDialog();
                    return TextCommandResult.Success(
                        "War Scythe editor toggled."
                    );
                });

            api.Logger.Notification(
                "[Apprentice] War Scythe authoring editor ready: command=.scytheeditor; hotkey=Ctrl+Shift+K; working={0}; accepted={1}.",
                workingPath,
                exportPath
            );
        }

        public bool PreviewActive => previewActive;
        public bool MarkersVisible => previewActive && markersVisible;
        public bool Playing => playing;
        public bool LoopPlayback => loopPlayback;
        public float PreviewTime => previewTime;
        public float PlaybackSpeed => playbackSpeed;
        public int SelectedFrameIndex => selectedFrameIndex;
        public int SelectedElementIndex => selectedElementIndex;
        public string SelectedElement => ControlledElements[
            selectedElementIndex
        ];
        public ApprenticeAnimationDefinition WorkingDefinition =>
            workingDefinition;
        public string ExportPath => exportPath;

        public bool ToggleDialog()
        {
            if (disposed) return false;
            dialog ??= new WarScytheAnimationEditorDialog(api, this);

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
            previewTime = workingDefinition.KeyFrames[
                selectedFrameIndex
            ].TimeSeconds;
            reachedElements.Clear();
            geometryAvailable = false;
            latestPlaybackStatus = "not-run";
            fullPlaybackComplete = false;
            latestPlaybackContractPass = false;
            playbackTrace = NewTrace();
            statusMessage =
                "Live preview active. Editing writes only the temporary composer frame.";
        }

        public void DeactivatePreview()
        {
            previewActive = false;
            playing = false;
            geometryAvailable = false;
            reachedElements.Clear();
            statusMessage =
                "Preview closed; the canonical asset was not modified.";
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
                previewTime += Math.Max(0, deltaTime) * playbackSpeed;
                if (previewTime >= workingDefinition.DurationSeconds)
                {
                    latestPlaybackStatus = playbackTrace.BuildStatus(0);
                    fullPlaybackComplete = true;
                    latestPlaybackContractPass =
                        playbackTrace.ContractPass;
                    if (loopPlayback)
                    {
                        previewTime %= workingDefinition.DurationSeconds;
                        playbackTrace = NewTrace();
                    }
                    else
                    {
                        previewTime = workingDefinition.DurationSeconds;
                        playing = false;
                    }
                }
            }

            EntityAgent entity = api.World.Player.Entity;
            ItemStack? stack = entity.RightHandItemSlot?.Itemstack;
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

        public bool TrySample(
            EntityAgent entity,
            string elementName,
            out ApprenticeElementTransform transform)
        {
            transform = default;
            if (!previewActive ||
                api.World.Player?.Entity?.EntityId != entity.EntityId ||
                !HasHeldWarScythe())
            {
                return false;
            }

            return workingDefinition.TrySample(
                elementName,
                previewTime,
                out transform
            );
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
            if (!MarkersVisible || !HasHeldWarScythe()) return false;

            EntityAgent entity = api.World.Player.Entity;
            ItemStack? stack = entity.RightHandItemSlot?.Itemstack;
            return stack != null && geometryProbe.TryBuildDebugGeometry(
                entity,
                stack,
                out geometry
            );
        }

        public float[] GetSelectedValues()
        {
            ApprenticeAnimationKeyFrame frame =
                workingDefinition.KeyFrames[selectedFrameIndex];
            return frame.Elements[SelectedElement];
        }

        public void SelectFrame(int index)
        {
            selectedFrameIndex = Math.Clamp(
                index,
                0,
                workingDefinition.KeyFrames.Count - 1
            );
            previewTime = workingDefinition.KeyFrames[
                selectedFrameIndex
            ].TimeSeconds;
            playing = false;
            statusMessage =
                $"Selected keyframe {selectedFrameIndex + 1}.";
        }

        public void SelectElement(int index)
        {
            selectedElementIndex = Math.Clamp(
                index,
                0,
                ControlledElements.Length - 1
            );
            statusMessage = $"Selected {SelectedElement}.";
        }

        public void SetSelectedValue(int component, float value)
        {
            if (component < 0 || component >= 6 ||
                !float.IsFinite(value))
            {
                return;
            }

            PushUndo();
            float[] values = GetSelectedValues();
            values[component] = value;
            statusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} = {2:0.0}",
                SelectedElement,
                ComponentName(component),
                value
            );
        }

        public void SetPreviewMilliseconds(int milliseconds)
        {
            previewTime = Math.Clamp(
                milliseconds / 1000f,
                0,
                workingDefinition.DurationSeconds
            );
            playing = false;
        }

        public void SetPreviewFraction(float fraction)
        {
            previewTime = workingDefinition.DurationSeconds *
                Math.Clamp(fraction, 0, 1);
            playing = false;
        }

        public void StepRenderedFrame(int direction)
        {
            SetPreviewMilliseconds((int)Math.Round(
                (previewTime + Math.Sign(direction) / 30f) * 1000f
            ));
        }

        public void StepKeyFrame(int direction)
        {
            int next = selectedFrameIndex + Math.Sign(direction);
            if (next < 0) next = workingDefinition.KeyFrames.Count - 1;
            if (next >= workingDefinition.KeyFrames.Count) next = 0;
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

            if (previewTime >= workingDefinition.DurationSeconds)
            {
                previewTime = 0;
            }
            playbackTrace = NewTrace();
            latestPlaybackStatus = "running";
            fullPlaybackComplete = false;
            latestPlaybackContractPass = false;
            playing = true;
            statusMessage =
                "Playback uses the same final-pose composer as the attack.";
        }

        public void StopPlayback()
        {
            playing = false;
            SelectFrame(selectedFrameIndex);
            statusMessage = "Playback stopped at the selected keyframe.";
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
            copiedFrame = SerializeFrame(
                workingDefinition.KeyFrames[selectedFrameIndex]
            );
            statusMessage =
                $"Copied keyframe {selectedFrameIndex + 1}.";
        }

        public void PasteSelectedFrame()
        {
            if (string.IsNullOrWhiteSpace(copiedFrame))
            {
                statusMessage = "No copied keyframe is available.";
                return;
            }

            PushUndo();
            ApprenticeAnimationKeyFrame parsed =
                Newtonsoft.Json.JsonConvert
                    .DeserializeObject<ApprenticeAnimationKeyFrame>(
                        copiedFrame
                    ) ?? throw new InvalidOperationException(
                        "The copied keyframe is invalid."
                    );
            float time = workingDefinition.KeyFrames[
                selectedFrameIndex
            ].TimeSeconds;
            parsed.TimeSeconds = time;
            workingDefinition.KeyFrames[selectedFrameIndex] = parsed;
            previewTime = time;
            statusMessage =
                $"Pasted pose into keyframe {selectedFrameIndex + 1}.";
        }

        public void ResetSelectedFrame()
        {
            PushUndo();
            ApprenticeAnimationKeyFrame source =
                sourceDefinition.DeepClone().KeyFrames[selectedFrameIndex];
            workingDefinition.KeyFrames[selectedFrameIndex] = source;
            previewTime = source.TimeSeconds;
            statusMessage =
                $"Reset keyframe {selectedFrameIndex + 1} to the packaged source.";
        }

        public void ResetAll()
        {
            PushUndo();
            workingDefinition = sourceDefinition.DeepClone();
            selectedFrameIndex = 0;
            selectedElementIndex = 0;
            previewTime = 0;
            playing = false;
            statusMessage =
                "Reset every working keyframe to the packaged source.";
        }

        public void Undo()
        {
            if (undo.Count == 0)
            {
                statusMessage = "Nothing to undo.";
                return;
            }

            redo.Push(workingDefinition.ToJson());
            workingDefinition = Parse(undo.Pop(), "undo");
            ClampSelection();
            statusMessage = "Undid the last editor change.";
        }

        public void Redo()
        {
            if (redo.Count == 0)
            {
                statusMessage = "Nothing to redo.";
                return;
            }

            undo.Push(workingDefinition.ToJson());
            workingDefinition = Parse(redo.Pop(), "redo");
            ClampSelection();
            statusMessage = "Redid the editor change.";
        }

        public void SaveWorking()
        {
            WriteDefinition(workingPath);
            statusMessage =
                "Working JSON saved and copied to the clipboard.";
        }

        public void Export()
        {
            if (!CanExport(out string reason))
            {
                statusMessage = "Export blocked: " + reason;
                return;
            }

            WriteDefinition(exportPath);
            statusMessage =
                "Accepted JSON exported to file and clipboard.";
            api.Logger.Notification(
                "[Apprentice] WARSCYTHE EDITOR EXPORT path={0}; hooks={1}; playback=[{2}]",
                exportPath,
                string.Join(",", reachedElements.OrderBy(
                    value => value,
                    StringComparer.Ordinal
                )),
                latestPlaybackStatus
            );
        }

        public void ReloadExport()
        {
            if (!File.Exists(workingPath))
            {
                statusMessage = "No working JSON exists yet.";
                return;
            }

            PushUndo();
            workingDefinition = Parse(
                File.ReadAllText(workingPath),
                "working-file"
            );
            ClampSelection();
            statusMessage = "Reloaded the working JSON file.";
        }

        public void ReloadPackagedAsset()
        {
            PushUndo();
            workingDefinition =
                ApprenticeAnimationDefinition.LoadWarScythe(api);
            ClampSelection();
            statusMessage = "Reloaded the packaged animation asset.";
        }

        public string BuildStatusText()
        {
            string missing = string.Join(
                ", ",
                ControlledElements.Where(element =>
                    !reachedElements.Contains(element))
            );
            string hooks = missing.Length == 0
                ? "all six controlled elements reached"
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
                "Hook: {9}\n{10}\nPlayback gate: {11}",
                statusMessage,
                previewTime,
                workingDefinition.DurationSeconds,
                playbackSpeed,
                playing ? "playing" : "paused",
                selectedFrameIndex + 1,
                workingDefinition.KeyFrames.Count,
                workingDefinition.KeyFrames[
                    selectedFrameIndex
                ].TimeSeconds,
                SelectedElement,
                hooks,
                geometry,
                latestPlaybackStatus
            );
        }

        public string[] FrameCodes() =>
            workingDefinition.KeyFrames.Select((frame, index) =>
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1:0.000}s",
                    index + 1,
                    frame.TimeSeconds
                ))
                .ToArray();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            previewActive = false;
            playing = false;
            dialog?.TryClose();
            dialog?.Dispose();
            dialog = null;
            markerRenderer.Dispose();
            undo.Clear();
            redo.Clear();
            reachedElements.Clear();
        }

        private bool CanExport(out string reason)
        {
            string[] missing = ControlledElements.Where(element =>
                !reachedElements.Contains(element))
                .ToArray();
            if (missing.Length != 0)
            {
                reason = "render hook has not reached " +
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
                reason = "play the complete timeline after the last edit";
                return false;
            }
            if (!latestPlaybackContractPass)
            {
                reason = "the complete playback acceptance contract failed";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        private bool HasHeldWarScythe() =>
            api.World.Player?.Entity?.RightHandItemSlot?.Itemstack?.Item?
                .Code?.ToString() == workingDefinition.HeldItemCode;

        private void PushUndo()
        {
            fullPlaybackComplete = false;
            latestPlaybackContractPass = false;
            latestPlaybackStatus = "not-run-after-edit";
            undo.Push(workingDefinition.ToJson());
            while (undo.Count > MaximumHistoryEntries)
            {
                string[] retained = undo
                    .Take(MaximumHistoryEntries)
                    .Reverse()
                    .ToArray();
                undo.Clear();
                foreach (string entry in retained) undo.Push(entry);
            }
            redo.Clear();
        }

        private void ClampSelection()
        {
            selectedFrameIndex = Math.Clamp(
                selectedFrameIndex,
                0,
                workingDefinition.KeyFrames.Count - 1
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
        }

        private ApprenticeAnimationDefinition Parse(
            string json,
            string source) =>
            ApprenticeAnimationDefinition.ParseWarScythe(
                json,
                new AssetLocation(
                    "apprentice",
                    "editor/" + source + ".json"
                )
            );

        private WarScytheGeometryTrace NewTrace() =>
            new(geometryProbe.Acceptance);

        private void WriteDefinition(string path)
        {
            string json = workingDefinition.ToJson();
            _ = Parse(json, "write-validation");
            string? directory = Path.GetDirectoryName(path);
            if (directory == null)
            {
                throw new InvalidOperationException(
                    "The authoring directory is unavailable."
                );
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, path, overwrite: true);
            api.Forms.SetClipboardText(json);
        }

        private static string SerializeFrame(
            ApprenticeAnimationKeyFrame frame) =>
            Newtonsoft.Json.JsonConvert.SerializeObject(frame);

        private static string ComponentName(int component) =>
            component switch
            {
                0 => "offset X",
                1 => "offset Y",
                2 => "offset Z",
                3 => "rotation X",
                4 => "rotation Y",
                5 => "rotation Z",
                _ => "unknown"
            };
    }
}
