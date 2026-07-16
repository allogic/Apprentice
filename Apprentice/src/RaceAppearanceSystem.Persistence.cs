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
            ApplyRacePacket(fromPlayer, packet, true);
        }

        private static void ApplyRacePacket(
            IServerPlayer serverPlayer,
            RaceBodyPacket packet,
            bool persist)
        {
            EntityPlayer player = serverPlayer.Entity;
            string currentClass = GetClassCode(player);
            string classCode = RaceByClass.ContainsKey(packet.RaceClass)
                ? packet.RaceClass
                : currentClass;
            RaceProfile profile = GetProfile(classCode);
            SaveBodyChoices(
                player,
                profile,
                packet.Height,
                packet.Thickness,
                packet.Horns,
                packet.Teeth,
                packet.Subclass,
                packet.Profession,
                packet.HornColor
            );

            if (currentClass != classCode)
            {
                SetCharacterClassForRestore(player, classCode);
            }

            ApplyRaceAppearance(player, classCode);
            RestoreAppearanceParts(player, profile, packet.AppearanceParts);
            RefreshHiddenRaceParts(player, profile);
            ApplyBodyStats(player, profile);

            if (persist)
            {
                serverPlayer.SetModdata(
                    PersistentRaceDataKey,
                    SerializerUtil.Serialize(BuildRaceBodyPacket(player))
                );
            }
        }

        private static void OnPlayerJoin(IServerPlayer player)
        {
            serverApi?.Event.RegisterCallback(
                _ =>
                {
                    if (player.GetModdata(PersistentRaceDataKey) != null) return;
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
            if (data == null || data.Length == 0)
            {
                RaceProfile currentProfile = GetProfile(player.Entity);
                bool completeSubclass = !HasSubclasses(currentProfile) ||
                    GetSelectedSubclass(player.Entity, currentProfile) != null;
                if (completeSubclass && GetProfessionChoice(player.Entity) != "select")
                {
                    ApplyRaceAppearance(
                        player.Entity,
                        GetClassCode(player.Entity)
                    );
                    ApplyBodyStats(player.Entity, currentProfile);
                    player.SetModdata(
                        PersistentRaceDataKey,
                        SerializerUtil.Serialize(BuildRaceBodyPacket(player.Entity))
                    );
                }
                return;
            }

            try
            {
                RaceBodyPacket packet = SerializerUtil.Deserialize<RaceBodyPacket>(data);
                ApplyRacePacket(player, packet, false);
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
                ModSystem? characterSystem = serverApi?.ModLoader.GetModSystem(
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

        private static void SendBodyPacket(EntityPlayer player)
        {
            clientChannel?.SendPacket(BuildRaceBodyPacket(player));
        }

        private static RaceBodyPacket BuildRaceBodyPacket(EntityPlayer player)
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
                RaceClass = GetClassCode(player)
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
            player.WatchedAttributes.MarkAllDirty();
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

        internal static string[] GetHornColorNames() => HornColorNames;

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
