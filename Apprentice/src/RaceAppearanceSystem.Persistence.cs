using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace Apprentice
{
    public sealed partial class RaceAppearanceSystem
    {
        private static void AfterSetCharacterClass(EntityPlayer eplayer, string classCode)
        {
            if (RestoreInProgress.Contains(eplayer)) return;

            ApplyRaceAppearance(eplayer, classCode);
            ApplyBodyStats(eplayer, GetProfile(classCode));

            if (eplayer.Api.Side == EnumAppSide.Client)
            {
                if (activeCharacterDialog != null)
                {
                    ConfirmedRaceDialogs.Remove(activeCharacterDialog);
                }
                eplayer.Api.Event.RegisterCallback(
                    _ =>
                    {
                        string currentClass = GetClassCode(eplayer);
                        if (currentClass == classCode)
                        {
                            ApplyRaceAppearance(eplayer, classCode);
                        }
                    },
                    100
                );
            }
        }

        private static void OnRaceBodyPacket(IServerPlayer fromPlayer, RaceBodyPacket packet)
        {
            RaceSaveResultPacket result = new()
            {
                RequestId = packet.RequestId
            };

            try
            {
                if (packet.RequestId <= 0)
                {
                    result.Error = "The customization request ID is invalid.";
                }
                else if (!TryNormalizeSnapshot(
                    fromPlayer.Entity,
                    packet,
                    true,
                    out RaceBodyPacket snapshot,
                    out string error,
                    out List<string> corrections))
                {
                    result.Error = error;
                }
                else
                {
                    LogSnapshotCorrections(fromPlayer, corrections);
                    fromPlayer.SetModdata(
                        PersistentRaceDataKey,
                        SerializerUtil.Serialize(snapshot)
                    );
                    ApplyCustomizationSnapshot(fromPlayer.Entity, snapshot);
                    result.Success = true;
                }
            }
            catch (Exception exception)
            {
                result.Error = "The server could not save this character customization.";
                serverApi?.Logger.Error(
                    "[Apprentice] Could not save race choices for {0}: {1}",
                    fromPlayer.PlayerName,
                    exception
                );
            }

            serverChannel?.SendPacket(result, fromPlayer);
        }

        private static void OnRaceSnapshotReceived(RaceBodyPacket packet)
        {
            clientApi?.Event.EnqueueMainThreadTask(
                () => ApplyRaceSnapshotOnClient(packet),
                "apprentice-restore-race-snapshot"
            );
        }

        private static void ApplyRaceSnapshotOnClient(RaceBodyPacket packet)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null) return;

            if (!TryNormalizeSnapshot(
                player,
                packet,
                false,
                out RaceBodyPacket snapshot,
                out string error,
                out _))
            {
                clientApi?.Logger.Warning(
                    "[Apprentice] Ignored an incompatible race snapshot: {0}",
                    error
                );
                return;
            }

            ApplyCustomizationSnapshot(player, snapshot);
            if (activeCharacterDialog != null)
            {
                UpdateBodyTrait(activeCharacterDialog);
                RefreshOptionsDialog(activeCharacterDialog);
            }
        }

        private static void ApplyCustomizationSnapshot(
            EntityPlayer player,
            RaceBodyPacket snapshot)
        {
            RaceProfile profile = GetProfile(snapshot.RaceClass);
            RestoreInProgress.Add(player);
            try
            {
                SaveBodyChoices(
                    player,
                    profile,
                    snapshot.Height,
                    snapshot.Thickness,
                    snapshot.Horns,
                    snapshot.Teeth,
                    snapshot.Subclass,
                    snapshot.Profession,
                    snapshot.HornColor
                );

                if (GetClassCode(player) != snapshot.RaceClass)
                {
                    SetCharacterClassForRestore(player, snapshot.RaceClass);
                }

                ApplyRaceAppearance(player, snapshot.RaceClass);
                RestoreAppearanceParts(player, profile, snapshot.AppearanceParts);
                RefreshHiddenRaceParts(player, profile);
                ApplyBodyStats(player, profile);
                player.WatchedAttributes.MarkAllDirty();
            }
            finally
            {
                RestoreInProgress.Remove(player);
            }

			// Heritage discoveries are server-owned and are evaluated only
			// after the complete, normalized race snapshot has been applied.
			// Unlocks are permanent, so later cosmetic edits cannot revoke one.
			if (player.Player is IServerPlayer serverPlayer)
			{
				SkillTreeRuntime.ReevaluateDiscoveries(serverPlayer);
			}
        }

        private static void OnPlayerJoin(IServerPlayer player)
        {
            serverApi?.Event.RegisterCallback(
                _ =>
                {
                    if (player.GetModdata(PersistentRaceDataKey) != null ||
                        player.GetModdata(LegacyPersistentRaceDataKey) != null)
                    {
                        return;
                    }
                    RaceProfile profile = GetProfile(player.Entity);
                    ApplyRaceAppearance(player.Entity, GetClassCode(player.Entity));
                    ApplyBodyStats(player.Entity, profile);
                },
                250
            );
        }

        private static void OnPlayerReady(IServerPlayer player)
        {
            RestorePersistentRaceState(player);
            serverApi?.Event.RegisterCallback(
                _ => RestorePersistentRaceState(player),
                250
            );
            serverApi?.Event.RegisterCallback(
                _ => RestorePersistentRaceState(player),
                1000
            );
        }

        private static void RestorePersistentRaceState(IServerPlayer player)
        {
            byte[]? data = player.GetModdata(PersistentRaceDataKey);
            bool migratedLegacyKey = false;
            if (data == null || data.Length == 0)
            {
                data = player.GetModdata(LegacyPersistentRaceDataKey);
                migratedLegacyKey = data != null && data.Length > 0;
            }

            if (data == null || data.Length == 0)
            {
                RaceProfile currentProfile = GetProfile(player.Entity);
                bool completeSubclass = !HasSubclasses(currentProfile) ||
                    GetSelectedSubclass(player.Entity, currentProfile) != null;
                if (completeSubclass && GetProfessionChoice(player.Entity) != "select")
                {
                    RaceBodyPacket current = BuildRaceBodyPacket(player.Entity, 0);
                    if (TryNormalizeSnapshot(
                        player.Entity,
                        current,
                        false,
                        out RaceBodyPacket snapshot,
                        out _,
                        out List<string> corrections))
                    {
                        LogSnapshotCorrections(player, corrections);
                        player.SetModdata(
                            PersistentRaceDataKey,
                            SerializerUtil.Serialize(snapshot)
                        );
                        ApplyCustomizationSnapshot(player.Entity, snapshot);
                        serverChannel?.SendPacket(snapshot, player);
                    }
                }
                return;
            }

            try
            {
                RaceBodyPacket packet = SerializerUtil.Deserialize<RaceBodyPacket>(data);
                if (!TryNormalizeSnapshot(
                    player.Entity,
                    packet,
                    false,
                    out RaceBodyPacket snapshot,
                    out string error,
                    out List<string> corrections))
                {
                    serverApi?.Logger.Error(
                        "[Apprentice] Could not restore saved race choices for {0}: {1}",
                        player.PlayerName,
                        error
                    );
                    return;
                }

                if (migratedLegacyKey)
                {
                    corrections.Add("storage key: v1 -> v2");
                }
                LogSnapshotCorrections(player, corrections);
                player.SetModdata(
                    PersistentRaceDataKey,
                    SerializerUtil.Serialize(snapshot)
                );
                ApplyCustomizationSnapshot(player.Entity, snapshot);
                serverChannel?.SendPacket(snapshot, player);
            }
            catch (Exception exception)
            {
                serverApi?.Logger.Error(
                    "[Apprentice] Could not restore saved race choices for {0}: {1}",
                    player.PlayerName,
                    exception.Message
                );
            }
        }

        private static void SetCharacterClassForRestore(
            EntityPlayer player,
            string classCode)
        {
            try
            {
                ModSystem? characterSystem = player.Api.ModLoader.GetModSystem(
                    "Vintagestory.GameContent.CharacterSystem"
                );
                MethodInfo? setClass = characterSystem == null
                    ? null
                    : AccessTools.Method(
                        characterSystem.GetType(),
                        "setCharacterClass",
                        new[] { typeof(EntityPlayer), typeof(string), typeof(bool) }
                    );
                if (setClass != null)
                {
                    setClass.Invoke(characterSystem, new object[] { player, classCode, false });
                    return;
                }
            }
            catch (Exception exception)
            {
                serverApi?.Logger.Warning(
                    "[Apprentice] Character-class restore fallback used: {0}",
                    exception.GetBaseException().Message
                );
            }

            player.WatchedAttributes.SetString("characterClass", classCode);
            player.WatchedAttributes.MarkAllDirty();
        }

        private static long SendBodyPacket(EntityPlayer player)
        {
            if (clientChannel == null) return 0;

            long requestId = System.Threading.Interlocked.Increment(
                ref nextRaceSaveRequestId
            );
            clientChannel.SendPacket(BuildRaceBodyPacket(player, requestId));
            return requestId;
        }

        private static RaceBodyPacket BuildRaceBodyPacket(
            EntityPlayer player,
            long requestId)
        {
            RaceProfile profile = GetProfile(player);
            RaceBodyPacket packet = new()
            {
                Height = GetHeightChoice(player),
                Thickness = GetThicknessChoice(player),
                Horns = GetHornChoice(player, profile),
                Teeth = GetTeethChoice(player, profile),
                Subclass = GetSubclassChoice(player, profile),
                Profession = GetProfessionChoice(player),
                HornColor = GetHornColorChoice(player),
                RaceClass = GetClassCode(player),
                SchemaVersion = RaceSnapshotSchemaVersion,
                RequestId = requestId
            };

            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");
            if (appliedParts != null)
            {
                foreach (string partCode in PersistentAppearancePartCodes)
                {
                    string? value = appliedParts.GetString(partCode, null);
                    if (!string.IsNullOrEmpty(value))
                    {
                        packet.AppearanceParts[partCode] = value;
                    }
                }
            }

            return packet;
        }

        private static bool TryNormalizeSnapshot(
            EntityPlayer player,
            RaceBodyPacket source,
            bool strict,
            out RaceBodyPacket snapshot,
            out string error,
            out List<string> corrections)
        {
            corrections = new List<string>();
            error = "";
            snapshot = new RaceBodyPacket
            {
                SchemaVersion = RaceSnapshotSchemaVersion,
                RequestId = source.RequestId
            };

            if (source.SchemaVersion > RaceSnapshotSchemaVersion)
            {
                error = $"Race customization schema {source.SchemaVersion} is newer than supported schema {RaceSnapshotSchemaVersion}.";
                return false;
            }
            if (source.SchemaVersion != RaceSnapshotSchemaVersion)
            {
                corrections.Add(
                    $"schema: '{source.SchemaVersion}' -> '{RaceSnapshotSchemaVersion}'"
                );
            }

            string fallbackRace = RaceByClass.ContainsKey(GetClassCode(player))
                ? GetClassCode(player)
                : "apprentice-race-human";
            if (!TryChooseSnapshotCode(
                "race",
                source.RaceClass,
                RaceByClass.Keys,
                fallbackRace,
                strict,
                corrections,
                out string classCode,
                out error))
            {
                return false;
            }
            snapshot.RaceClass = classCode;

            RaceProfile profile = GetProfile(classCode);
            string[] subclassCodes = GetSubclasses(profile)
                .Select(subclass => subclass.Code)
                .ToArray();
            if (subclassCodes.Length == 0)
            {
                subclassCodes = new[] { "select" };
            }
            string subclassFallback = subclassCodes[0];
            if (!TryChooseSnapshotCode(
                "subrace",
                source.Subclass,
                subclassCodes,
                subclassFallback,
                strict,
                corrections,
                out string subclass,
                out error))
            {
                return false;
            }
            snapshot.Subclass = subclass;

            if (!TryChooseSnapshotCode(
                "profession",
                source.Profession,
                ProfessionCodes.Where(code => code != "select"),
                ProfessionCodes[1],
                strict,
                corrections,
                out string profession,
                out error))
            {
                return false;
            }
            snapshot.Profession = profession;

            snapshot.Height = Math.Clamp(source.Height, 0f, 1f);
            snapshot.Thickness = Math.Clamp(source.Thickness, 0f, 1f);
            if (snapshot.Height != source.Height)
            {
                corrections.Add($"height: '{source.Height}' -> '{snapshot.Height}'");
            }
            if (snapshot.Thickness != source.Thickness)
            {
                corrections.Add(
                    $"thickness: '{source.Thickness}' -> '{snapshot.Thickness}'"
                );
            }

            if (!TryChooseSnapshotCode(
                "horns",
                source.Horns,
                profile.HornCodes,
                profile.DefaultHorns,
                strict,
                corrections,
                out string horns,
                out error) ||
                !TryChooseSnapshotCode(
                    "teeth",
                    source.Teeth,
                    profile.TeethCodes,
                    profile.DefaultTeeth,
                    strict,
                    corrections,
                    out string teeth,
                    out error))
            {
                return false;
            }
            snapshot.Horns = horns;
            snapshot.Teeth = teeth;

            bool supportsHorns = profile.HornCodes.Any(code => code != "none");
            if (supportsHorns)
            {
                if (!TryChooseSnapshotCode(
                    "horn color",
                    source.HornColor,
                    HornColorCodes,
                    "yellowish-white",
                    strict,
                    corrections,
                    out string hornColor,
                    out error))
                {
                    return false;
                }
                snapshot.HornColor = hornColor;
            }
            else
            {
                snapshot.Horns = "none";
                snapshot.HornColor = "yellowish-white";
                if (source.Horns != "none")
                {
                    corrections.Add($"horns: '{source.Horns}' -> 'none'");
                }
            }

            if (player.GetBehavior("skinnableplayer") is not EntityBehaviorExtraSkinnable skin)
            {
                error = "The player skin configuration is not ready.";
                return false;
            }

            SubclassProfile? subclassProfile = GetSubclasses(profile)
                .FirstOrDefault(candidate => candidate.Code == subclass);
            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");
            Dictionary<string, string> requestedParts = source.AppearanceParts ??
                new Dictionary<string, string>();

            foreach (string partCode in PersistentAppearancePartCodes)
            {
                string[] allowed = GetAllowedAppearanceCodes(
                    skin,
                    profile,
                    subclassProfile,
                    partCode
                );
                if (allowed.Length == 0) continue;

                string current = appliedParts?.GetString(partCode, allowed[0]) ?? allowed[0];
                string fallback = allowed.Contains(current, StringComparer.Ordinal)
                    ? current
                    : allowed[0];
                requestedParts.TryGetValue(partCode, out string? requested);
                if (!TryChooseSnapshotCode(
                    "appearance " + partCode,
                    requested,
                    allowed,
                    fallback,
                    strict,
                    corrections,
                    out string selected,
                    out error))
                {
                    return false;
                }
                snapshot.AppearanceParts[partCode] = selected;
            }

            return true;
        }

        private static string[] GetAllowedAppearanceCodes(
            EntityBehaviorExtraSkinnable skin,
            RaceProfile profile,
            SubclassProfile? subclass,
            string partCode)
        {
            if (!skin.AvailableSkinPartsByCode.TryGetValue(
                partCode,
                out SkinnablePart? part))
            {
                return Array.Empty<string>();
            }

            IEnumerable<string> available = part.Variants
                .Select(variant => variant.Code)
                .Distinct(StringComparer.Ordinal);
            string[] raceAllowed = partCode switch
            {
                "baseskin" => subclass?.SkinCodes ?? profile.SkinCodes,
                "eyecolor" => subclass?.EyeColors ?? profile.EyeCodes,
                "haircolor" => subclass?.HairColors ?? profile.HairCodes,
                _ => Array.Empty<string>()
            };
            if (raceAllowed.Length > 0)
            {
                HashSet<string> allowList = new(raceAllowed, StringComparer.Ordinal);
                available = available.Where(allowList.Contains);
            }

            return available.ToArray();
        }

        private static bool TryChooseSnapshotCode(
            string field,
            string? requested,
            IEnumerable<string> allowedValues,
            string fallback,
            bool strict,
            List<string> corrections,
            out string selected,
            out string error)
        {
            string[] allowed = allowedValues
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (requested != null && allowed.Contains(requested, StringComparer.Ordinal))
            {
                selected = requested;
                error = "";
                return true;
            }

            selected = allowed.Contains(fallback, StringComparer.Ordinal)
                ? fallback
                : allowed.FirstOrDefault() ?? "";
            if (strict || selected.Length == 0)
            {
                error = $"The selected {field} value '{requested ?? "<missing>"}' is not valid for this race.";
                return false;
            }

            corrections.Add(
                $"{field}: '{requested ?? "<missing>"}' -> '{selected}'"
            );
            error = "";
            return true;
        }

        private static void LogSnapshotCorrections(
            IServerPlayer player,
            List<string> corrections)
        {
            if (corrections.Count == 0) return;
            serverApi?.Logger.Warning(
                "[Apprentice] Migrated race choices for {0}: {1}",
                player.PlayerName,
                string.Join("; ", corrections)
            );
        }

        private static void SaveBodyChoices(
            EntityPlayer player,
            RaceProfile profile,
            float height,
            float thickness,
            string? horns,
            string? teeth,
            string? subclass,
            string? profession,
            string? hornColor)
        {
            player.WatchedAttributes.SetFloat(HeightAttribute, Math.Clamp(height, 0f, 1f));
            player.WatchedAttributes.SetFloat(ThicknessAttribute, Math.Clamp(thickness, 0f, 1f));
            player.WatchedAttributes.SetString(
                HornAttribute,
                ValidateOption(horns, profile.HornCodes, profile.DefaultHorns)
            );
            player.WatchedAttributes.SetString(
                TeethAttribute,
                ValidateOption(teeth, profile.TeethCodes, profile.DefaultTeeth)
            );
            player.WatchedAttributes.SetString(
                HornColorAttribute,
                ValidateOption(hornColor, HornColorCodes, "yellowish-white")
            );
            string[] subclassCodes = GetSubclasses(profile)
                .Select(value => value.Code)
                .ToArray();
            player.WatchedAttributes.SetString(
                SubclassAttribute,
                ValidateOption(subclass, subclassCodes, "select")
            );
            player.WatchedAttributes.SetString(
                ProfessionAttribute,
                ValidateOption(profession, ProfessionCodes, "select")
            );
        }

        private static string ValidateOption(
            string? option,
            string[] allowed,
            string fallback)
        {
            return option != null && allowed.Contains(option, StringComparer.Ordinal)
                ? option
                : fallback;
        }

        internal static float GetHeightChoice(Entity entity) =>
            Math.Clamp(entity.WatchedAttributes.GetFloat(HeightAttribute, 0.5f), 0f, 1f);

        internal static float GetThicknessChoice(Entity entity) =>
            Math.Clamp(entity.WatchedAttributes.GetFloat(ThicknessAttribute, 0.5f), 0f, 1f);

        internal static string GetHornChoice(Entity entity, RaceProfile profile) =>
            ValidateOption(
                entity.WatchedAttributes.GetString(HornAttribute, profile.DefaultHorns),
                profile.HornCodes,
                profile.DefaultHorns
            );

        internal static string GetHornColorChoice(Entity entity) =>
            ValidateOption(
                entity.WatchedAttributes.GetString(
                    HornColorAttribute,
                    "yellowish-white"
                ),
                HornColorCodes,
                "yellowish-white"
            );

        internal static string[] GetHornColorCodes() => HornColorCodes;

        internal static string[] GetHornColorNames() => new[]
        {
            Lang.Get("apprentice:horn-color-dark-gray"),
            Lang.Get("apprentice:horn-color-light-gray"),
            Lang.Get("apprentice:horn-color-white"),
            Lang.Get("apprentice:horn-color-light-pink"),
            Lang.Get("apprentice:horn-color-yellowish-white")
        };

        internal static string GetTeethChoice(Entity entity, RaceProfile profile) =>
            ValidateOption(
                entity.WatchedAttributes.GetString(TeethAttribute, profile.DefaultTeeth),
                profile.TeethCodes,
                profile.DefaultTeeth
            );

        internal static SubclassProfile[] GetSubclasses(RaceProfile profile) =>
            SubclassesByRace.TryGetValue(profile.Code, out SubclassProfile[]? subclasses)
                ? subclasses
                : Array.Empty<SubclassProfile>();

        internal static bool HasSubclasses(RaceProfile profile) =>
            GetSubclasses(profile).Length > 0;

        internal static string[] GetSubclassCodes(RaceProfile profile) =>
            HasSubclasses(profile)
                ? new[] { "select" }.Concat(
                    GetSubclasses(profile).Select(subclass => subclass.Code)
                ).ToArray()
                : Array.Empty<string>();

        internal static string[] GetSubclassNames(RaceProfile profile) =>
            HasSubclasses(profile)
                ? new[] { Lang.Get("apprentice:select-subclass") }.Concat(
                    GetSubclasses(profile).Select(subclass => subclass.Name)
                ).ToArray()
                : Array.Empty<string>();

        internal static string GetSubclassChoice(Entity entity, RaceProfile profile)
        {
            string selected = entity.WatchedAttributes.GetString(
                SubclassAttribute,
                "select"
            );
            return GetSubclasses(profile).Any(
                subclass => subclass.Code == selected
            ) ? selected : "select";
        }

        internal static string[] GetProfessionCodes() => ProfessionCodes;

        internal static string[] GetProfessionNames() => new[]
        {
            Lang.Get("apprentice:select-profession"),
            Lang.Get("apprentice:profession-miner"),
            Lang.Get("apprentice:profession-woodworker"),
            Lang.Get("apprentice:profession-farmer"),
            Lang.Get("apprentice:profession-builder"),
            Lang.Get("apprentice:profession-blacksmith"),
            Lang.Get("apprentice:profession-cook"),
            Lang.Get("apprentice:profession-potter"),
            Lang.Get("apprentice:profession-leatherworker"),
            Lang.Get("apprentice:profession-tailor"),
            Lang.Get("apprentice:profession-hunter"),
            Lang.Get("apprentice:profession-warrior"),
            Lang.Get("apprentice:profession-ranger"),
            Lang.Get("apprentice:profession-fisher"),
            Lang.Get("apprentice:profession-animalhusbandry"),
            Lang.Get("apprentice:profession-beekeeper"),
            Lang.Get("apprentice:profession-spearman"),
            Lang.Get("apprentice:profession-shield"),
            Lang.Get("apprentice:profession-tank")
        };

        internal static string GetProfessionChoice(Entity entity) =>
            ValidateOption(
                entity.WatchedAttributes.GetString(ProfessionAttribute, "select"),
                ProfessionCodes,
                "select"
            );

        internal static double GetProfessionExperienceMultiplier(
            Entity entity,
            string classId) =>
            GetProfessionChoice(entity).Equals(classId, StringComparison.Ordinal)
                ? 1.10
                : 1.00;

        private static SubclassProfile? GetSelectedSubclass(
            Entity entity,
            RaceProfile profile)
        {
            string selected = GetSubclassChoice(entity, profile);
            return GetSubclasses(profile).FirstOrDefault(
                subclass => subclass.Code == selected
            );
        }

    }
}
