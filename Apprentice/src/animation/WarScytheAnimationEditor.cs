using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using Apprentice.AnimationReference;
using Animation =
    Apprentice.AnimationReference.Animation;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;

using Apprentice.ClientTools;

namespace Apprentice
{
    internal sealed class WarScytheAnimationEditor : IDisposable
    {
        public static readonly string[] ControlledElements =
        {
            "DetachedAnchor",
            "UpperTorso",
            "LowerTorso",
            "Neck",
            "Head",
            "UpperFootR",
            "UpperFootL",
            "LowerFootR",
            "LowerFootL",
            "ItemAnchor",
            "UpperArmR",
            "LowerArmR",
            "ItemAnchorL",
            "UpperArmL",
            "LowerArmL"
        };

        private static readonly string[] RequiredWarScytheElements =
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
        private readonly Dictionary<string, Animation> sourceAnimations =
            new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> animationLabels =
            new(StringComparer.Ordinal);
        private readonly List<string> animationOrder = new();
        private readonly HashSet<string> modifiedAnimations =
            new(StringComparer.Ordinal);
        private readonly HashSet<string> reachedElements =
            new(StringComparer.OrdinalIgnoreCase);

        private ApprenticeAnimationDefinition workingDefinition;
        private Item? selectedItem;
        private WarScytheImGuiEditorWindow? window;
        private WarScytheGeometryTrace playbackTrace;
        private WarScytheGeometrySample latestGeometry;
        private string copiedFrame = string.Empty;
        private string latestPlaybackStatus = "not-run";
        private string statusMessage =
            "Equip the War Scythe, then open the editor.";
        private float previewTime;
        private float playbackSpeed = 1f;
        private int selectedAnimationIndex;
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
            playbackTrace = NewTrace();

            markerRenderer = new WarScytheCalibrationRenderer(
                api,
                this
            );

            api.Input.RegisterHotKey(
                HotKeyCode,
                "Apprentice item animation editor",
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
                    "Open the Apprentice item animation editor for the held item"
                )
                .HandleWith(_ =>
                {
                    ToggleDialog();
                    return TextCommandResult.Success(
                        "Item animation editor toggled."
                    );
                });

            api.Logger.Notification(
                "[Apprentice] Item animation editor ready: command=.scytheeditor; hotkey=Ctrl+Shift+K; slash command=/apprentice calibrate <itemname> edit."
            );
        }

        public bool PreviewActive => previewActive;
        public bool MarkersVisible =>
            previewActive && markersVisible && SupportsGeometry;
        public bool Playing => playing;
        public bool LoopPlayback => loopPlayback;
        public float PreviewTime => previewTime;
        public float PlaybackSpeed => playbackSpeed;
        public int SelectedAnimationIndex => selectedAnimationIndex;
        public int SelectedFrameIndex => selectedFrameIndex;
        public int SelectedElementIndex => selectedElementIndex;
        public string SelectedElement =>
            ControlledElements[selectedElementIndex];
        public string SelectedItemCode =>
            selectedItem?.Code?.ToString() ?? "none";
        public string SelectedAnimationCode =>
            animationOrder.Count == 0
                ? "none"
                : animationOrder[selectedAnimationIndex];
        public int FrameCount =>
            workingDefinition.Animation.PlayerKeyFrames.Count;
        public float DurationSeconds =>
            workingDefinition.DurationSeconds;
        public bool SupportsGeometry =>
            sourceDefinition.IsSupportedHeldItemCode(
                selectedItem?.Code?.ToString()) &&
            SelectedAnimationCode == sourceDefinition.Code;
        public ApprenticeAnimationDefinition WorkingDefinition =>
            workingDefinition;
        public string WorkingPath => Path.Combine(
            AuthoringDirectory,
            ItemFileStem + "-working.json"
        );
        public string ExportPath => Path.Combine(
            AuthoringDirectory,
            ItemFileStem + ".json"
        );

        private string AuthoringDirectory => Path.Combine(
            GamePaths.DataPath,
            "ModConfig",
            "ApprenticeAuthoring"
        );

        private string ItemFileStem
        {
            get
            {
                string path = selectedItem?.Code?.Path ?? "item";
                if (path.Equals(
                        "warscythe",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return "war-scythe";
                }

                char[] invalid = Path.GetInvalidFileNameChars();
                return new string(path.Select(character =>
                    invalid.Contains(character) ? '-' : character
                ).ToArray());
            }
        }

        public bool ToggleDialog()
        {
            if (disposed) return false;

            if (window?.IsOpen == true)
            {
                window.Close();
                return true;
            }
            Item? heldItem = api.World.Player?.Entity?
                .RightHandItemSlot?.Itemstack?.Item;
            if (heldItem == null)
            {
                statusMessage =
                    "Put an item in your right hand before opening the editor.";
                api.ShowChatMessage(
                    "[Apprentice] Put an item in your right hand first."
                );
                return false;
            }

            return OpenForItem(heldItem);
        }

        public bool OpenForItem(Item item)
        {
            if (disposed || item?.Code == null) return false;

            string itemCode = item.Code.ToString();
            string? heldCode = api.World.Player?.Entity?
                .RightHandItemSlot?.Itemstack?.Item?.Code?.ToString();
            if (!itemCode.Equals(
                    heldCode,
                    StringComparison.OrdinalIgnoreCase))
            {
                statusMessage =
                    $"Put {itemCode} in your right hand before editing it.";
                api.ShowChatMessage("[Apprentice] " + statusMessage);
                return false;
            }

            bool itemChanged = !itemCode.Equals(
                selectedItem?.Code?.ToString(),
                StringComparison.OrdinalIgnoreCase
            );
            if (window?.IsOpen == true && itemChanged)
            {
                window.Close();
            }
            if (itemChanged || editableAnimations.Count == 0)
            {
                ConfigureItem(item);
            }

            try
            {
                window ??= new WarScytheImGuiEditorWindow(
                    api,
                    this
                );
                return window.IsOpen || window.TryOpen();
            }
            catch (Exception exception)
            {
                ReportUiFailure(
                    "Open VSImGui editor",
                    exception
                );
                api.ShowChatMessage(
                    "[Apprentice] The item animation editor requires the vsimgui 1.2.7 mod."
                );
                return false;
            }
        }

        public void ActivatePreview()
        {
            if (disposed || !HasHeldSelectedItem()) return;

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
            if (!HasHeldSelectedItem())
            {
                statusMessage =
                    "Preview stopped because the selected item is no longer held.";
                window?.Close();
                return;
            }

            if (playing)
            {
                float duration = Math.Max(
                    0.001f,
                    workingDefinition.DurationSeconds
                );
                previewTime += Math.Max(0, deltaTime) *
                    playbackSpeed;
                if (previewTime >= duration)
                {
                    latestPlaybackStatus = SupportsGeometry
                        ? playbackTrace.BuildStatus(0)
                        : "complete";
                    fullPlaybackComplete = true;
                    latestPlaybackContractPass =
                        !SupportsGeometry ||
                        playbackTrace.ContractPass;
                    if (loopPlayback)
                    {
                        previewTime %= duration;
                        playbackTrace = NewTrace();
                    }
                    else
                    {
                        previewTime = duration;
                        playing = false;
                    }
                }
                UpdatePreviewFrame();
            }

            EntityAgent entity = api.World.Player.Entity;
            ItemStack? stack =
                entity.RightHandItemSlot?.Itemstack;
            geometryAvailable = SupportsGeometry &&
                stack != null &&
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
            if (!MarkersVisible || !HasHeldSelectedItem())
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

        public string[] AnimationLabels() =>
            animationOrder
                .Select(code => animationLabels.TryGetValue(
                    code,
                    out string? label
                ) ? label : code)
                .ToArray();

        public void SelectAnimation(int index)
        {
            EndValueEdit();
            if (animationOrder.Count == 0) return;

            selectedAnimationIndex = Math.Clamp(
                index,
                0,
                animationOrder.Count - 1
            );
            workingDefinition.ReplaceAnimation(
                editableAnimations[SelectedAnimationCode]
            );
            selectedFrameIndex = 0;
            selectedElementIndex = 0;
            previewTime = 0;
            playing = false;
            reachedElements.Clear();
            geometryAvailable = false;
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
            statusMessage =
                $"Selected animation {SelectedAnimationCode}.";
        }

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
            modifiedAnimations.Add(SelectedAnimationCode);
            editableAnimations[SelectedAnimationCode] =
                workingDefinition.Animation;
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
                SelectedAnimationCode,
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
                    SelectedAnimationCode,
                    workingDefinition.Animation
                );
                editableAnimations[SelectedAnimationCode] =
                    workingDefinition.Animation;
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
                ? SupportsGeometry
                    ? "Grip, blade, torso, and head markers visible."
                    : "Geometry markers are only available for the Apprentice attack track."
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
                    sourceAnimations[SelectedAnimationCode]
                        .PlayerKeyFrames[
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
                sourceAnimations[SelectedAnimationCode].Clone();
            PerformEdit(
                "Reset all frames",
                () => ReplaceWorkingAnimation(source)
            );
            selectedFrameIndex = 0;
            selectedElementIndex = 0;
            previewTime = 0;
            playing = false;
            modifiedAnimations.Remove(SelectedAnimationCode);
            statusMessage =
                "Reset the selected animation to its packaged source.";
        }

        public void Undo()
        {
            EndValueEdit();
            if (!history.Undo(
                    SelectedAnimationCode,
                    editableAnimations,
                    out string status))
            {
                statusMessage = status;
                return;
            }

            workingDefinition.ReplaceAnimation(
                editableAnimations[SelectedAnimationCode]
            );
            modifiedAnimations.Add(SelectedAnimationCode);
            ClampSelection();
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
            statusMessage = status;
        }

        public void Redo()
        {
            EndValueEdit();
            if (!history.Redo(
                    SelectedAnimationCode,
                    editableAnimations,
                    out string status))
            {
                statusMessage = status;
                return;
            }

            workingDefinition.ReplaceAnimation(
                editableAnimations[SelectedAnimationCode]
            );
            modifiedAnimations.Add(SelectedAnimationCode);
            ClampSelection();
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
            statusMessage = status;
        }

        public void SaveWorking()
        {
            WriteDefinition(WorkingPath);
            statusMessage =
                "All item animation tracks were saved to the working JSON and copied to the clipboard.";
        }

        public void Export()
        {
            if (!CanExport(out string reason))
            {
                statusMessage = "Export blocked: " + reason;
                return;
            }

            WriteDefinition(ExportPath);
            statusMessage =
                "All edited item animation tracks were exported to file and copied to the clipboard.";
            api.Logger.Notification(
                "[Apprentice] ITEM ANIMATION EDITOR EXPORT item={0}; animation={1}; path={2}; elements={3}; playback=[{4}]",
                SelectedItemCode,
                SelectedAnimationCode,
                ExportPath,
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
            if (!File.Exists(WorkingPath))
            {
                statusMessage =
                    "No working reference JSON exists yet.";
                return;
            }

            LoadAnimationFile(
                File.ReadAllText(WorkingPath),
                "working-file"
            );
            statusMessage =
                "Reloaded every animation track from the working JSON file.";
        }

        public void ReloadPackagedAsset()
        {
            Item item = selectedItem ??
                throw new InvalidOperationException(
                    "No item is selected."
                );
            ConfigureItem(item);
            statusMessage =
                "Reloaded the selected item's packaged animation tracks.";
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
                "[Apprentice] ITEM ANIMATION EDITOR action failed: {0}",
                action
            );
            api.Logger.Error(exception);
        }

        public string BuildStatusText()
        {
            string missing = string.Join(
                ", ",
                RequiredWarScytheElements.Where(element =>
                    !reachedElements.Contains(element))
            );
            string hooks = !SupportsGeometry
                ? "not required for the selected animation"
                : missing.Length == 0
                    ? "all six reference elements reached"
                    : "missing: " + missing;

            string geometry = !SupportsGeometry
                ? "War Scythe geometry gate is not required for this animation."
                : geometryAvailable
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
                "{0}\n\nItem {1}\nAnimation {2}\n" +
                "Time {3:0.000}/{4:0.000}s | speed {5:0.0}x | {6}\n" +
                "Frame {7}/{8} at {9:0.000}s | element {10}\n" +
                "Pipeline: reference Animation -> PlayerItemFrame -> OnFrameInvoke\n" +
                "Hook: {11}\n{12}\nPlayback gate: {13}",
                statusMessage,
                SelectedItemCode,
                SelectedAnimationCode,
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
            window?.Dispose();
            window = null;
            markerRenderer.Dispose();
            editableAnimations.Clear();
            sourceAnimations.Clear();
            animationLabels.Clear();
            animationOrder.Clear();
            modifiedAnimations.Clear();
            reachedElements.Clear();
        }

        private void ConfigureItem(Item item)
        {
            EndValueEdit();
            foreach (string code in animationOrder)
            {
                history.Clear(code);
            }

            selectedItem = item;
            editableAnimations.Clear();
            sourceAnimations.Clear();
            animationLabels.Clear();
            animationOrder.Clear();
            modifiedAnimations.Clear();
            reachedElements.Clear();
            copiedFrame = string.Empty;

            string itemCode = item.Code.ToString();
            if (sourceDefinition.IsSupportedHeldItemCode(itemCode))
            {
                AddAnimation(
                    sourceDefinition.Code,
                    "Apprentice attack: " +
                        sourceDefinition.Code,
                    sourceDefinition.Animation
                );
            }

            Shape? playerShape = api.World.Player?.Entity?
                .Properties?.Client?.LoadedShapeForEntity ??
                api.World.Player?.Entity?.Properties?.Client?
                    .LoadedShape;
            if (playerShape?.Animations != null)
            {
                HashSet<string> itemAnimationCodes =
                    CollectItemAnimationCodes(item);
                HashSet<string> candidates = new(
                    itemAnimationCodes,
                    StringComparer.OrdinalIgnoreCase
                );
                foreach (string requestedCode in itemAnimationCodes)
                {
                    if (!requestedCode.EndsWith(
                            "-fp",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(requestedCode + "-fp");
                    }
                    if (!requestedCode.EndsWith(
                            "-ifp",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        candidates.Add(requestedCode + "-ifp");
                    }
                }

                foreach (string requestedCode in candidates.OrderBy(
                    value => value,
                    StringComparer.OrdinalIgnoreCase))
                {
                    string shortCode = requestedCode.Contains(':')
                        ? requestedCode[
                            (requestedCode.LastIndexOf(':') + 1)..]
                        : requestedCode;
                    Vintagestory.API.Common.Animation? native =
                        playerShape.Animations.FirstOrDefault(
                            animation =>
                                string.Equals(
                                    animation.Code,
                                    shortCode,
                                    StringComparison.OrdinalIgnoreCase
                                ) ||
                                string.Equals(
                                    animation.Name,
                                    shortCode,
                                    StringComparison.OrdinalIgnoreCase
                                )
                        );
                    if (native == null)
                    {
                        if (itemAnimationCodes.Contains(requestedCode))
                        {
                            AddAnimation(
                                "referenced:" + requestedCode,
                                "Item reference: " + requestedCode +
                                    " (new track)",
                                CreateHeldPoseAnimation()
                            );
                        }
                        continue;
                    }

                    List<PLayerKeyFrame> frames =
                        PLayerKeyFrame.FromVanillaAnimation(
                            native,
                            out bool hasPlayerFrames
                        );
                    if (!hasPlayerFrames || frames.Count == 0)
                    {
                        continue;
                    }

                    string nativeCode =
                        native.Code ?? native.Name ?? shortCode;
                    AddAnimation(
                        "native:" + nativeCode,
                        "Native player: " + nativeCode,
                        new Animation(frames)
                    );
                }
            }

            if (animationOrder.Count == 0)
            {
                string poseCode =
                    item.Code.Domain + ":" + item.Code.Path +
                    "-held-pose";
                AddAnimation(
                    poseCode,
                    "New held pose",
                    CreateHeldPoseAnimation()
                );
            }

            selectedAnimationIndex = 0;
            selectedFrameIndex = 0;
            selectedElementIndex = 0;
            previewTime = 0;
            playing = false;
            geometryAvailable = false;
            fullPlaybackComplete = false;
            latestPlaybackContractPass = false;
            latestPlaybackStatus = "not-run";
            playbackTrace = NewTrace();
            workingDefinition = sourceDefinition.DeepClone();
            workingDefinition.ReplaceAnimation(
                editableAnimations[SelectedAnimationCode]
            );
            statusMessage = string.Format(
                CultureInfo.InvariantCulture,
                "Loaded {0} animation track(s) for {1}.",
                animationOrder.Count,
                itemCode
            );
            UpdatePreviewFrame();
        }

        private void AddAnimation(
            string code,
            string label,
            Animation animation)
        {
            if (editableAnimations.ContainsKey(code)) return;

            Animation source = animation.Clone();
            sourceAnimations[code] = source;
            editableAnimations[code] = source.Clone();
            animationLabels[code] = label;
            animationOrder.Add(code);
        }

        private static Animation CreateHeldPoseAnimation() =>
            new(
                new[]
                {
                    new PLayerKeyFrame(
                        PlayerFrame.Zero,
                        TimeSpan.Zero,
                        EasingFunctionType.Linear
                    ),
                    new PLayerKeyFrame(
                        PlayerFrame.Zero,
                        TimeSpan.FromSeconds(1),
                        EasingFunctionType.Linear
                    )
                }
            );

        private static HashSet<string> CollectItemAnimationCodes(
            Item item)
        {
            HashSet<string> result =
                new(StringComparer.OrdinalIgnoreCase);
            const BindingFlags Flags =
                BindingFlags.Instance |
                BindingFlags.Public;

            foreach (PropertyInfo property in item.GetType()
                .GetProperties(Flags)
                .Where(property =>
                    property.PropertyType == typeof(string) &&
                    property.Name.Contains(
                        "Animation",
                        StringComparison.OrdinalIgnoreCase) &&
                    property.GetIndexParameters().Length == 0))
            {
                try
                {
                    AddAnimationCode(
                        result,
                        property.GetValue(item) as string
                    );
                }
                catch
                {
                    // A collectible may expose a contextual property getter.
                    // Attribute discovery below still covers JSON-owned codes.
                }
            }

            foreach (FieldInfo field in item.GetType()
                .GetFields(Flags)
                .Where(field =>
                    field.FieldType == typeof(string) &&
                    field.Name.Contains(
                        "Animation",
                        StringComparison.OrdinalIgnoreCase)))
            {
                AddAnimationCode(
                    result,
                    field.GetValue(item) as string
                );
            }

            if (item.Attributes?.Token != null)
            {
                CollectAnimationTokens(
                    item.Attributes.Token,
                    result
                );
            }
            return result;
        }

        private static void CollectAnimationTokens(
            JToken token,
            ISet<string> destination)
        {
            if (token is JObject objectToken)
            {
                foreach (JProperty property in objectToken.Properties())
                {
                    if (property.Name.Contains(
                            "animation",
                            StringComparison.OrdinalIgnoreCase) &&
                        property.Value.Type == JTokenType.String)
                    {
                        AddAnimationCode(
                            destination,
                            property.Value.Value<string>()
                        );
                    }
                    CollectAnimationTokens(
                        property.Value,
                        destination
                    );
                }
            }
            else if (token is JArray arrayToken)
            {
                foreach (JToken child in arrayToken)
                {
                    CollectAnimationTokens(child, destination);
                }
            }
        }

        private static void AddAnimationCode(
            ISet<string> destination,
            string? code)
        {
            if (!string.IsNullOrWhiteSpace(code))
            {
                destination.Add(code.Trim());
            }
        }

        private void PerformEdit(
            string label,
            Action edit)
        {
            EndValueEdit();
            history.BeginEdit(
                SelectedAnimationCode,
                workingDefinition.Animation,
                label
            );
            try
            {
                edit();
                editableAnimations[SelectedAnimationCode] =
                    workingDefinition.Animation;
                history.CommitEdit(
                    SelectedAnimationCode,
                    workingDefinition.Animation
                );
                modifiedAnimations.Add(SelectedAnimationCode);
            }
            catch
            {
                history.CancelPendingEdit();
                throw;
            }
            InvalidatePlaybackAcceptance();
            UpdatePreviewFrame();
        }

        private void ReplaceWorkingAnimation(Animation animation)
        {
            workingDefinition.ReplaceAnimation(animation);
            editableAnimations[SelectedAnimationCode] = animation;
        }

        private bool CanExport(out string reason)
        {
            if (!modifiedAnimations.Contains(
                    sourceDefinition.Code))
            {
                reason = string.Empty;
                return true;
            }
            if (SelectedAnimationCode != sourceDefinition.Code)
            {
                reason =
                    "select the Apprentice attack animation and validate its complete playback first";
                return false;
            }

            string[] missing = RequiredWarScytheElements.Where(element =>
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

        private bool HasHeldSelectedItem() =>
            selectedItem?.Code != null &&
            string.Equals(
                api.World.Player?.Entity?.RightHandItemSlot?
                    .Itemstack?.Item?.Code?.ToString(),
                selectedItem.Code.ToString(),
                StringComparison.OrdinalIgnoreCase
            );

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
            editableAnimations[SelectedAnimationCode] =
                workingDefinition.Animation;
            UpdatePreviewFrame();
        }

        private float FrameTimeSeconds(int index) =>
            (float)workingDefinition.Animation.PlayerKeyFrames[
                index].Time.TotalSeconds;

        private WarScytheGeometryTrace NewTrace() =>
            new(geometryProbe.Acceptance);

        private void LoadAnimationFile(
            string json,
            string source)
        {
            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Could not parse {source}.",
                    exception
                );
            }

            string previousCode = SelectedAnimationCode;
            foreach (JProperty property in root.Properties())
            {
                AnimationJson dto =
                    property.Value.ToObject<AnimationJson>() ??
                    throw new InvalidOperationException(
                        $"{source} animation '{property.Name}' is invalid."
                    );
                Animation animation = dto.ToAnimation();
                if (!editableAnimations.ContainsKey(property.Name))
                {
                    sourceAnimations[property.Name] =
                        animation.Clone();
                    animationLabels[property.Name] =
                        "Loaded: " + property.Name;
                    animationOrder.Add(property.Name);
                }
                editableAnimations[property.Name] = animation;
                modifiedAnimations.Add(property.Name);
                history.Clear(property.Name);
            }

            int previousIndex = animationOrder.FindIndex(code =>
                code.Equals(previousCode, StringComparison.Ordinal)
            );
            selectedAnimationIndex =
                previousIndex >= 0 ? previousIndex : 0;
            workingDefinition.ReplaceAnimation(
                editableAnimations[SelectedAnimationCode]
            );
            ClampSelection();
            InvalidatePlaybackAcceptance();
        }

        private void WriteDefinition(string path)
        {
            JObject root = new();
            JsonSerializer serializer =
                JsonSerializer.CreateDefault();
            foreach (string code in animationOrder)
            {
                root[code] = JToken.FromObject(
                    AnimationJson.FromAnimation(
                        editableAnimations[code]
                    ),
                    serializer
                );
            }
            string json =
                root.ToString(Formatting.Indented) +
                Environment.NewLine;

            JObject validation = JObject.Parse(json);
            foreach (JProperty property in validation.Properties())
            {
                AnimationJson dto =
                    property.Value.ToObject<AnimationJson>() ??
                    throw new InvalidOperationException(
                        $"Animation '{property.Name}' failed export validation."
                    );
                _ = dto.ToAnimation();
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
