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
    /// <summary>
    /// Synchronizes Apprentice race appearance, body dimensions and body-derived
    /// gameplay modifiers. Race shapes extend the stock Seraph skeleton so all
    /// normal equipment and animation paths remain intact.
    /// </summary>
    public sealed partial class RaceAppearanceSystem : ModSystem
    {
        private const string HarmonyId = "apprentice.raceappearance";
        private const string ChannelName = "apprentice-racebody";
        private const string PersistentRaceDataKey = "apprenticeRaceSelection-v1";
        private const string SkinPartCode = "apprenticerace";
        private const string HornPartCode = "apprenticehorns";
        private const string HornColorPartCode = "apprenticehorncolor";
        private const string TeethPartCode = "apprenticeteeth";
        private const string HeightAttribute = "apprenticeRaceHeight";
        private const string ThicknessAttribute = "apprenticeRaceThickness";
        private const string HornAttribute = "apprenticeRaceHorns";
        private const string HornColorAttribute = "apprenticeRaceHornColor";
        private const string TeethAttribute = "apprenticeRaceTeeth";
        private const string SubclassAttribute = "apprenticeRaceSubclass";
        private const string ProfessionAttribute = "apprenticeProfession";
        private const string AppearanceDefaultsAttribute =
            "apprenticeRaceAppearanceDefaults";
        private const string BodyStatSource = "apprentice-body";
        private const string SubclassStatSource = "apprentice-subclass";
        private const string BodyTraitFallback =
            "<font color=\"#e6c65c\">• Body Stats</font>";
        private const float GlobalMinimumHeight = 0.5504f;
        private const float GlobalMaximumHeight = 1.3664f;

        internal sealed class RaceProfile
        {
            public RaceProfile(
                string code,
                float width,
                float height,
                float minHeightScale,
                float maxHeightScale,
                float minPreviewZoom,
                float maxPreviewZoom,
                float previewXOffset,
                float previewYOffset,
                float previewFacePan,
                string? skinCode,
                string face,
                string eyes,
                string? hair,
                string? beard,
                string[] eyeCodes,
                string[] hairCodes,
                string[] hornCodes,
                string[] hornNames,
                string defaultHorns,
                string[] teethCodes,
                string[] teethNames,
                string defaultTeeth)
            {
                Code = code;
                Width = width;
                Height = height;
                MinHeightScale = minHeightScale;
                MaxHeightScale = maxHeightScale;
                MinPreviewZoom = minPreviewZoom;
                MaxPreviewZoom = maxPreviewZoom;
                PreviewXOffset = previewXOffset;
                PreviewYOffset = previewYOffset;
                PreviewFacePan = previewFacePan;
                SkinCode = skinCode;
                Face = face;
                Eyes = eyes;
                Hair = hair;
                Beard = beard;
                EyeCodes = eyeCodes;
                HairCodes = hairCodes;
                HornCodes = hornCodes;
                HornNames = hornNames;
                DefaultHorns = defaultHorns;
                TeethCodes = teethCodes;
                TeethNames = teethNames;
                DefaultTeeth = defaultTeeth;
            }

            public string Code { get; }
            public float Width { get; }
            public float Height { get; }
            public float MinHeightScale { get; }
            public float MaxHeightScale { get; }
            public float MinPreviewZoom { get; }
            public float MaxPreviewZoom { get; }
            public float PreviewXOffset { get; }
            public float PreviewYOffset { get; }
            public float PreviewFacePan { get; }
            public string? SkinCode { get; }
            public string Face { get; }
            public string Eyes { get; }
            public string? Hair { get; }
            public string? Beard { get; }
            public string[] EyeCodes { get; }
            public string[] HairCodes { get; }
            public string[] HornCodes { get; }
            public string[] HornNames { get; }
            public string DefaultHorns { get; }
            public string[] TeethCodes { get; }
            public string[] TeethNames { get; }
            public string DefaultTeeth { get; }

            public string[] SkinCodes => SkinCode == null
                ? HumanSkinCodes
                : new[] { SkinCode };
        }

        private readonly struct BodyEffects
        {
            public BodyEffects(
                float health,
                float melee,
                float movement,
                float hunger,
                float toolSpeed,
                float yield)
            {
                Health = health;
                Melee = melee;
                Movement = movement;
                Hunger = hunger;
                ToolSpeed = toolSpeed;
                Yield = yield;
            }

            public float Health { get; }
            public float Melee { get; }
            public float Movement { get; }
            public float Hunger { get; }
            public float ToolSpeed { get; }
            public float Yield { get; }
        }

        internal sealed class SubclassProfile
        {
            public SubclassProfile(
                string code,
                string name,
                string traitSummary,
                string[] skinCodes,
                string[] eyeColors,
                string[] hairColors,
                string hairStyle,
                string hairExtra,
                params (string Stat, float Value)[] stats)
            {
                Code = code;
                Name = name;
                TraitSummary = traitSummary;
                SkinCodes = skinCodes;
                EyeColors = eyeColors;
                HairColors = hairColors;
                HairStyle = hairStyle;
                HairExtra = hairExtra;
                Stats = stats;
            }

            public string Code { get; }
            public string Name { get; }
            public string TraitSummary { get; }
            public string[] SkinCodes { get; }
            public string[] EyeColors { get; }
            public string[] HairColors { get; }
            public string HairStyle { get; }
            public string HairExtra { get; }
            public (string Stat, float Value)[] Stats { get; }
        }

        private sealed class PaletteSnapshot
        {
            public PaletteSnapshot(
                SkinnablePart part,
                SkinnablePartVariant[] variants,
                Dictionary<string, SkinnablePartVariant> variantsByCode)
            {
                Part = part;
                Variants = variants;
                VariantsByCode = variantsByCode;
            }

            public SkinnablePart Part { get; }
            public SkinnablePartVariant[] Variants { get; }
            public Dictionary<string, SkinnablePartVariant> VariantsByCode { get; }
        }

        // Vintage Story's numbered skin palette includes fantasy blues, greens and
        // violets.  Keep humans inside documented D&D human coloration instead of
        // exposing every numbered game color.
        private static readonly string[] HumanSkinCodes =
        {
            "apprentice-dwarf", "apprentice-elf", "apprentice-gnome",
            "apprentice-halfling", "skin10", "skin11", "skin12"
        };

        private static readonly string[] NaturalHumanHair =
        {
            "lightgray", "silverchalice", "slategray", "eerieblack",
            "wheat", "ecru", "sanddune", "harvestgold", "cordovan",
            "rosewood", "russet", "liver", "blackolive", "darklava"
        };

        private static readonly string[] NaturalHumanEyes =
        {
            "aquamarine", "azure", "dark-green", "forest-green", "goldenrod",
            "jade", "phthalo-blue", "sand", "sapphire", "citrine", "smokyquartz"
        };

        private static readonly string[] NoOptions = { "none" };
        private static readonly string[] NoOptionNames = { "None" };

        private static readonly string[] HornColorCodes =
        {
            "dark-gray", "light-gray", "white", "light-pink", "yellowish-white"
        };

        private static readonly string[] HornColorNames =
        {
            "Dark gray", "Light gray", "White", "Light pink", "Yellowish white"
        };

        private static readonly string[] ProfessionCodes =
        {
            "select", "miner", "woodworker", "farmer", "builder",
            "blacksmith", "cook", "potter", "leatherworker", "tailor",
            "hunter", "warrior", "ranger", "fisher", "animalhusbandry",
            "beekeeper", "spearman", "shield", "tank"
        };

        private static readonly string[] PersistentAppearancePartCodes =
        {
            "baseskin", "eyecolor", "underwear", "voicetype", "voicepitch",
            "hairbase", "hairextra", "facialexpression", "mustache", "beard",
            "haircolor"
        };

        private static readonly string[] FirstPersonHiddenPoseNames =
        {
            "dragonborn-face-root", "dragonborn-visible-eyes",
            "dwarf-native-0-origin", "dwarf-native-1-origin", "dwarf-face-root",
            "elf-native-0-origin", "elf-face-root",
            "gnome-native-0-origin", "gnome-native-1-origin", "gnome-face-root",
            "halfling-native-0-origin", "halfling-native-1-origin", "halfling-face-root",
            "option-dragonborn-face-root", "option-back-swept-dragonborn-root",
            "option-quad-crown-dragonborn-root", "option-short-spikes-dragonborn-root",
            "option-curved-tiefling-root", "option-gazelle-tiefling-root",
            "option-ram-tiefling-root", "option-straight-tiefling-root",
            "option-asym-left-tiefling-root", "option-asym-right-tiefling-root",
            "option-crushing-dragon-teeth-root", "option-mixed-dragon-teeth-root",
            "option-sabers-dragon-teeth-root", "option-orc-face-root",
            "option-double-orc-teeth-root", "option-outward-orc-teeth-root",
            "option-wide-orc-teeth-root"
        };

        private static readonly Dictionary<string, RaceProfile> RaceByClass =
            new(StringComparer.Ordinal)
            {
                ["apprentice-race-dragonborn"] = new RaceProfile(
                    "dragonborn", 1.10f, 1.12f, 0.92f, 1.08f,
                    0.85f, 1.70f, 0.09f, 0.85f, 0.00f,
                    "apprentice-dragonborn", "determined", "acid-green", "bald", "none",
                    new[] { "ruby", "acid-green", "sapphire", "opal", "blackopal", "goldenrod" },
                    new[] { "eerieblack", "slategray", "silverchalice", "oldburgundy", "rosewood" },
                    new[] { "dragon-crown", "dragon-back-swept", "dragon-short-spikes", "dragon-quad-crown", "dragon-asym-left", "dragon-asym-right" },
                    new[] { "Symmetric crown", "Back-swept", "Short spikes", "Four-horn crown", "Asymmetric left", "Asymmetric right" },
                    "dragon-crown",
                    new[] { "dragon-fangs", "dragon-saw", "dragon-sabers", "dragon-crushing", "dragon-mixed" },
                    new[] { "Paired fangs", "Saw teeth", "Saber fangs", "Crushing teeth", "Mixed bite" },
                    "dragon-fangs"
                ),
                ["apprentice-race-dwarf"] = new RaceProfile(
                    "dwarf", 1.13f, 0.78f, 0.90f, 1.10f,
                    1.00f, 2.10f, 0.12f, 0.29f, -2.20f,
                    "apprentice-dwarf", "determined", "goldenrod", null, "brd-braid10-double",
                    new[] { "smokyquartz", "sand", "goldenrod", "forest-green", "azure" },
                    NaturalHumanHair, NoOptions, NoOptionNames, "none",
                    NoOptions, NoOptionNames, "none"
                ),
                ["apprentice-race-elf"] = new RaceProfile(
                    "elf", 0.94f, 1.08f, 0.94f, 1.06f,
                    0.95f, 1.75f, -0.09f, 0.85f, 0.00f,
                    "apprentice-elf", "thoughtful", "forest-green", "longflowing", "none",
                    new[] { "forest-green", "jade", "azure", "aquamarine", "lavander", "amethyst" },
                    new[] { "lightgray", "silverchalice", "wheat", "ecru", "sanddune", "harvestgold", "cordovan", "rosewood", "liver", "eerieblack", "mossgreen", "seagreen" },
                    NoOptions, NoOptionNames, "none", NoOptions, NoOptionNames, "none"
                ),
                ["apprentice-race-gnome"] = new RaceProfile(
                    "gnome", 0.82f, 0.64f, 0.86f, 1.08f,
                    1.25f, 2.60f, -0.25f, -0.58f, -3.10f,
                    "apprentice-gnome", "smirk", "aquamarine", "shortspiky", "brd-chin10-chinpuff",
                    new[] { "aquamarine", "azure", "jade", "goldenrod", "citrine" },
                    new[] { "wheat", "ecru", "sanddune", "harvestgold", "lightgray", "silverchalice" },
                    NoOptions, NoOptionNames, "none",
                    NoOptions, NoOptionNames, "none"
                ),
                ["apprentice-race-goliath"] = new RaceProfile(
                    "goliath", 1.20f, 1.22f, 0.88f, 1.12f,
                    0.86f, 1.45f, 0.15f, 1.04f, 0.83f,
                    "apprentice-goliath", "neutral", "quartzrose", "short-trimmed", "none",
                    new[] { "quartzrose", "smokyquartz", "opal", "blackopal" },
                    new[] { "lightgray", "silverchalice", "slategray", "eerieblack", "gunmetal" },
                    NoOptions, NoOptionNames, "none", NoOptions, NoOptionNames, "none"
                ),
                ["apprentice-race-halfling"] = new RaceProfile(
                    "halfling", 0.90f, 0.72f, 0.86f, 1.08f,
                    1.05f, 2.20f, -0.17f, 0.07f, -3.00f,
                    "apprentice-halfling", "kind", "goldenrod", "mediumcurls", "none",
                    new[] { "smokyquartz", "sand", "goldenrod", "citrine" },
                    new[] { "liver", "cordovan", "russet", "wheat", "ecru", "sanddune" },
                    NoOptions, NoOptionNames, "none",
                    NoOptions, NoOptionNames, "none"
                ),
                ["apprentice-race-human"] = new RaceProfile(
                    "human", 1.00f, 1.00f, 0.90f, 1.10f,
                    1.00f, 1.85f, 0.00f, 0.77f, 0.00f,
                    null, "neutral", "azure", null, null,
                    NaturalHumanEyes, NaturalHumanHair, NoOptions, NoOptionNames, "none",
                    NoOptions, NoOptionNames, "none"
                ),
                ["apprentice-race-orc"] = new RaceProfile(
                    "orc", 1.12f, 1.06f, 0.92f, 1.08f,
                    0.95f, 1.75f, 0.12f, 0.88f, 0.00f,
                    "apprentice-orc", "fierce", "goldenrod", "iroquois", "none",
                    new[] { "ruby", "cadmium-orange", "goldenrod", "smokyquartz", "midnight" },
                    new[] { "eerieblack", "slategray", "gunmetal", "liver", "darklava" },
                    NoOptions, NoOptionNames, "none",
                    new[] { "orc-short", "orc-long", "orc-wide", "orc-outward", "orc-double", "orc-broken-left", "orc-broken-right" },
                    new[] { "Short tusks", "Long tusks", "Broad tusks", "Outward tusks", "Double tusks", "Broken left", "Broken right" },
                    "orc-short"
                ),
                ["apprentice-race-tiefling"] = new RaceProfile(
                    "tiefling", 1.00f, 1.04f, 0.92f, 1.08f,
                    0.88f, 1.90f, 0.00f, 0.90f, 0.00f,
                    "apprentice-dragonborn", "smirk", "blackopal", "longwithstrands", "none",
                    new[] { "blackopal", "ruby", "opal", "smokyquartz", "goldenrod" },
                    new[] { "eerieblack", "liver", "oldburgundy", "rosewood", "steelblue", "darkpurple" },
                    new[] { "tiefling-curved", "tiefling-ram", "tiefling-straight", "tiefling-gazelle", "tiefling-asym-left", "tiefling-asym-right" },
                    new[] { "Symmetric swept", "Ram curls", "Straight spikes", "Gazelle sweep", "Asymmetric left", "Asymmetric right" },
                    "tiefling-curved", NoOptions, NoOptionNames, "none"
                )
            };

        private static readonly RaceProfile HumanProfile =
            RaceByClass["apprentice-race-human"];

        private static readonly Dictionary<string, SubclassProfile[]> SubclassesByRace =
            new(StringComparer.Ordinal)
            {
                ["dragonborn"] = new[]
                {
                    new SubclassProfile(
                        "chromatic", "Chromatic Lineage",
                        "+8% ranged, +4% melee",
                        new[] { "apprentice-dragonborn", "apprentice-orc", "skin3", "skin13", "skin16" },
                        new[] { "ruby", "acid-green", "sapphire", "opal", "blackopal" },
                        new[] { "eerieblack", "slategray", "silverchalice" },
                        "bald", "none",
                        ("rangedWeaponsDamage", 0.08f), ("meleeWeaponsDamage", 0.04f)
                    ),
                    new SubclassProfile(
                        "metallic", "Metallic Lineage",
                        "+1 HP, -8% armor slow",
                        new[] { "skin11", "skin12", "skin14", "apprentice-dwarf", "apprentice-goliath" },
                        new[] { "goldenrod", "citrine", "smokyquartz", "opal", "blackopal" },
                        new[] { "harvestgold", "ecru", "silverchalice", "slategray", "eerieblack" },
                        "bald", "none",
                        ("maxhealthExtraPoints", 1f), ("armorWalkSpeedAffectedness", -0.08f)
                    ),
                    new SubclassProfile(
                        "gem", "Gem Lineage",
                        "+6% ranged, -10% repair cost",
                        new[] { "apprentice-tiefling", "skin13", "skin18", "skin3", "skin11" },
                        new[] { "amethyst", "opal", "jade", "sapphire", "citrine" },
                        new[] { "silverchalice", "dimlavender", "palatinatepurple", "steelblue", "ecru" },
                        "bald", "none",
                        ("rangedWeaponsDamage", 0.06f), ("temporalGearTLRepairCost", -0.10f)
                    )
                },
                ["dwarf"] = new[]
                {
                    new SubclassProfile(
                        "hill", "Hill Dwarf",
                        "+1 HP, +8% forage",
                        new[] { "apprentice-dwarf", "apprentice-halfling", "skin10", "skin11", "skin12" },
                        new[] { "smokyquartz", "sand", "goldenrod", "forest-green", "azure" },
                        new[] { "eerieblack", "slategray", "silverchalice", "liver", "cordovan", "russet" },
                        "longcurls", "sidebraids",
                        ("maxhealthExtraPoints", 1f), ("forageDropRate", 0.08f)
                    ),
                    new SubclassProfile(
                        "mountain", "Mountain Dwarf",
                        "+10% mining, +8% ore yield",
                        new[] { "apprentice-dwarf", "apprentice-halfling", "skin10", "skin11", "skin12" },
                        new[] { "smokyquartz", "sand", "goldenrod", "forest-green", "azure" },
                        new[] { "eerieblack", "slategray", "silverchalice", "liver", "cordovan", "russet" },
                        "longflowing", "vikingtopbraid",
                        ("miningSpeedMul", 0.10f), ("oreDropRate", 0.08f)
                    ),
                    new SubclassProfile(
                        "duergar", "Duergar",
                        "+6% mining, -12% repair cost",
                        new[] { "apprentice-goliath", "skin15", "skin16", "skin8" },
                        new[] { "blackopal", "smokyquartz", "opal" },
                        new[] { "lightgray", "silverchalice", "slategray" },
                        "bald", "none",
                        ("miningSpeedMul", 0.06f), ("temporalGearTLRepairCost", -0.12f)
                    )
                },
                ["elf"] = new[]
                {
                    new SubclassProfile(
                        "high", "High Elf",
                        "+8% ranged acc., -8% repair cost",
                        new[] { "apprentice-elf", "skin10", "skin12", "skin3" },
                        new[] { "goldenrod", "opal", "blackopal", "azure", "forest-green" },
                        new[] { "silverchalice", "lightgray", "steelblue", "wheat", "harvestgold", "eerieblack", "liver", "cordovan", "rosewood", "russet" },
                        "longflowing", "largestickbun",
                        ("rangedWeaponsAcc", 0.08f), ("temporalGearTLRepairCost", -0.08f)
                    ),
                    new SubclassProfile(
                        "wood", "Wood Elf",
                        "+6% move, +10% forage",
                        new[] { "skin12", "apprentice-dwarf", "skin17", "skin18" },
                        new[] { "forest-green", "dark-green", "jade", "smokyquartz", "sand", "goldenrod" },
                        new[] { "liver", "eerieblack", "russet", "cordovan", "harvestgold" },
                        "longcurls", "neatbraid",
                        ("walkspeed", 0.06f), ("forageDropRate", 0.10f)
                    ),
                    new SubclassProfile(
                        "drow", "Drow",
                        "+8% ranged, animal detection +5%",
                        new[] { "apprentice-drow-black", "skin16", "apprentice-goliath", "skin15", "skin8" },
                        new[] { "ruby", "lavander", "opal", "azure", "smokyquartz" },
                        new[] { "lightgray", "silverchalice", "opal", "ecru" },
                        "longwithstrands", "sidebraids",
                        ("rangedWeaponsDamage", 0.08f), ("animalSeekingRange", 0.05f)
                    )
                },
                ["gnome"] = new[]
                {
                    new SubclassProfile(
                        "rock", "Rock Gnome",
                        "+10% mech dmg, +8% gear yield",
                        new[] { "apprentice-gnome", "apprentice-dwarf", "apprentice-halfling", "skin11", "skin12" },
                        new[] { "aquamarine", "azure", "jade", "goldenrod", "citrine" },
                        new[] { "wheat", "ecru", "sanddune", "harvestgold", "lightgray", "silverchalice" },
                        "shortspiky", "none",
                        ("mechanicalsDamage", 0.10f), ("rustyGearDropRate", 0.08f)
                    ),
                    new SubclassProfile(
                        "forest", "Forest Gnome",
                        "+10% forage, animal detection -10%",
                        new[] { "apprentice-gnome", "apprentice-dwarf", "apprentice-halfling", "skin11", "skin12" },
                        new[] { "aquamarine", "azure", "jade", "goldenrod" },
                        new[] { "wheat", "ecru", "sanddune", "harvestgold", "lightgray", "silverchalice" },
                        "mediumcurls", "backbunpuffy",
                        ("forageDropRate", 0.10f), ("animalSeekingRange", -0.10f)
                    ),
                    new SubclassProfile(
                        "deep", "Deep Gnome",
                        "+8% mining, -10% repair cost",
                        new[] { "apprentice-goliath", "skin15", "skin16", "skin8" },
                        new[] { "smokyquartz", "blackopal", "opal", "amethyst" },
                        new[] { "lightgray", "silverchalice", "slategray", "eerieblack" },
                        "short-trimmed", "none",
                        ("miningSpeedMul", 0.08f), ("temporalGearTLRepairCost", -0.10f)
                    )
                },
                ["goliath"] = new[]
                {
                    new SubclassProfile(
                        "stone", "Stone Giantkin",
                        "+1 HP, -10% armor slow",
                        new[] { "apprentice-goliath", "skin15", "skin16" },
                        new[] { "smokyquartz", "sand", "goldenrod", "blackopal" },
                        new[] { "eerieblack", "slategray", "silverchalice" },
                        "bald", "none",
                        ("maxhealthExtraPoints", 1f), ("armorWalkSpeedAffectedness", -0.10f)
                    ),
                    new SubclassProfile(
                        "frost", "Frost Giantkin",
                        "+1 HP, -5% hunger",
                        new[] { "skin1", "skin3", "skin9", "skin13" },
                        new[] { "azure", "aquamarine", "sapphire", "opal" },
                        new[] { "lightgray", "silverchalice", "opal", "slategray", "steelblue" },
                        "longflowing", "sidebraids",
                        ("maxhealthExtraPoints", 1f), ("hungerrate", -0.05f)
                    ),
                    new SubclassProfile(
                        "storm", "Storm Giantkin",
                        "+6% move, +6% ranged",
                        new[] { "skin5", "skin6", "skin7", "skin8", "skin3" },
                        new[] { "sapphire", "azure", "midnight", "amethyst", "opal" },
                        new[] { "slategray", "steelblue", "gunmetal", "eerieblack", "silverchalice" },
                        "longflowing", "none",
                        ("walkspeed", 0.06f), ("rangedWeaponsDamage", 0.06f)
                    )
                },
                ["halfling"] = new[]
                {
                    new SubclassProfile(
                        "lightfoot", "Lightfoot",
                        "+6% move, animal detection -10%",
                        new[] { "apprentice-halfling", "apprentice-dwarf", "apprentice-gnome", "skin10", "skin11", "skin12" },
                        new[] { "smokyquartz", "goldenrod", "sand", "citrine" },
                        new[] { "liver", "cordovan", "russet", "wheat", "ecru", "sanddune" },
                        "mediumcurls", "none",
                        ("walkspeed", 0.06f), ("animalSeekingRange", -0.10f)
                    ),
                    new SubclassProfile(
                        "stout", "Stout",
                        "+1 HP, -5% hunger",
                        new[] { "apprentice-halfling", "apprentice-dwarf", "apprentice-gnome", "skin10", "skin11", "skin12" },
                        new[] { "smokyquartz", "goldenrod", "sand", "citrine" },
                        new[] { "liver", "cordovan", "russet", "wheat", "ecru", "sanddune" },
                        "shortcurls", "none",
                        ("maxhealthExtraPoints", 1f), ("hungerrate", -0.05f)
                    ),
                    new SubclassProfile(
                        "ghostwise", "Ghostwise",
                        "+8% forage, -8% repair cost",
                        new[] { "apprentice-halfling", "apprentice-dwarf", "apprentice-gnome", "skin10", "skin11", "skin12" },
                        new[] { "smokyquartz", "goldenrod", "sand", "citrine" },
                        new[] { "liver", "cordovan", "russet", "wheat", "ecru", "sanddune" },
                        "longcurls", "sidebraids",
                        ("forageDropRate", 0.08f), ("temporalGearTLRepairCost", -0.08f)
                    )
                },
                ["orc"] = new[]
                {
                    new SubclassProfile(
                        "gray", "Gray Orc",
                        "+6% move, +6% ranged acc.",
                        new[] { "apprentice-goliath", "skin15", "skin16", "skin8" },
                        new[] { "ruby", "cadmium-orange", "goldenrod", "smokyquartz", "midnight" },
                        new[] { "eerieblack", "slategray", "gunmetal", "liver", "darklava" },
                        "iroquois", "none",
                        ("walkspeed", 0.06f), ("rangedWeaponsAcc", 0.06f)
                    ),
                    new SubclassProfile(
                        "mountain", "Mountain Orc",
                        "+1 HP, +6% melee",
                        new[] { "apprentice-orc", "skin17", "skin18", "skin19", "skin20" },
                        new[] { "ruby", "cadmium-orange", "goldenrod", "smokyquartz", "midnight" },
                        new[] { "eerieblack", "slategray", "gunmetal", "liver", "darklava" },
                        "dreadlocks", "tieddreads",
                        ("maxhealthExtraPoints", 1f), ("meleeWeaponsDamage", 0.06f)
                    ),
                    new SubclassProfile(
                        "orog", "Orog",
                        "+10% mining, -8% armor slow",
                        new[] { "apprentice-goliath", "skin16", "skin20", "apprentice-orc" },
                        new[] { "ruby", "cadmium-orange", "goldenrod", "smokyquartz", "midnight" },
                        new[] { "eerieblack", "slategray", "gunmetal", "liver", "darklava" },
                        "short-trimmed", "none",
                        ("miningSpeedMul", 0.10f), ("armorWalkSpeedAffectedness", -0.08f)
                    )
                },
                ["tiefling"] = new[]
                {
                    new SubclassProfile(
                        "infernal", "Infernal Bloodline",
                        "+8% ranged, -6% repair cost",
                        HumanSkinCodes.Concat(new[] { "apprentice-dragonborn" }).Distinct(StringComparer.Ordinal).ToArray(),
                        new[] { "ruby", "goldenrod", "blackopal", "opal", "smokyquartz" },
                        new[] { "eerieblack", "liver", "oldburgundy", "rosewood", "steelblue", "darkpurple" },
                        "longwithstrands", "ponytail",
                        ("rangedWeaponsDamage", 0.08f), ("temporalGearTLRepairCost", -0.06f)
                    ),
                    new SubclassProfile(
                        "abyssal", "Abyssal Bloodline",
                        "+6% melee, +5% move",
                        HumanSkinCodes.Concat(new[] { "apprentice-dragonborn" }).Distinct(StringComparer.Ordinal).ToArray(),
                        new[] { "blackopal", "ruby", "opal", "smokyquartz", "goldenrod" },
                        new[] { "eerieblack", "liver", "oldburgundy", "rosewood", "steelblue", "darkpurple" },
                        "dreadlocks", "tieddreads",
                        ("meleeWeaponsDamage", 0.06f), ("walkspeed", 0.05f)
                    ),
                    new SubclassProfile(
                        "chthonic", "Chthonic Bloodline",
                        "-12% repair cost, animal detection -5%",
                        HumanSkinCodes.Concat(new[] { "apprentice-dragonborn" }).Distinct(StringComparer.Ordinal).ToArray(),
                        new[] { "blackopal", "ruby", "opal", "smokyquartz", "goldenrod" },
                        new[] { "eerieblack", "liver", "oldburgundy", "rosewood", "steelblue", "darkpurple" },
                        "longflowing", "sidebraids",
                        ("temporalGearTLRepairCost", -0.12f), ("animalSeekingRange", -0.05f)
                    )
                }
            };

        private static readonly string[] SubclassStatNames = SubclassesByRace.Values
            .SelectMany(subclasses => subclasses)
            .SelectMany(subclass => subclass.Stats)
            .Select(stat => stat.Stat)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        private static readonly HashSet<object> ConfirmedRaceDialogs = new();
        private static readonly Dictionary<EntityBehaviorExtraSkinnable, List<PaletteSnapshot>>
            PaletteSnapshots = new();
        private static readonly ConditionalWeakTable<MeshData, HashSet<int>>
            FirstPersonHiddenJoints = new();

        private Harmony? harmony;
        private static MethodInfo? selectSkinPartMethod;
        private static bool refreshingRaceShapeTexture;
        private static ICoreClientAPI? clientApi;
        private static ICoreServerAPI? serverApi;
        private static IClientNetworkChannel? clientChannel;
        private static IServerNetworkChannel? serverChannel;
        private static object? activeCharacterDialog;
        private static object? pendingSkinConfirmDialog;
        private static RaceOptionsDialog? optionsDialog;
        private static HornColorDialog? hornColorDialog;

        public override double ExecuteOrder() => 0.2;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            if (api.Side == EnumAppSide.Server)
            {
                serverChannel = ((ICoreServerAPI)api).Network
                    .RegisterChannel(ChannelName)
                    .RegisterMessageType<RaceBodyPacket>();
            }
            else
            {
                clientChannel = ((ICoreClientAPI)api).Network
                    .RegisterChannel(ChannelName)
                    .RegisterMessageType<RaceBodyPacket>();
            }

            harmony = new Harmony(HarmonyId);
            PatchCharacterClass();
            PatchPlayerRenderers(api);

            if (api is ICoreClientAPI capi)
            {
                clientApi = capi;
                PatchCharacterDialog();
                PatchFinalGuiEntityRender(capi);
            }

            api.Logger.Notification(
                "[Apprentice] Installed race customization."
            );
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            serverApi = api;
            serverChannel?.SetMessageHandler<RaceBodyPacket>(OnRaceBodyPacket);
            api.Event.PlayerJoin += OnPlayerJoin;
            api.Event.PlayerReady += OnPlayerReady;
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            clientApi = api;
        }

        private void PatchCharacterClass()
        {
            if (harmony == null) return;

            Type? characterSystemType = AccessTools.TypeByName(
                "Vintagestory.GameContent.CharacterSystem"
            );
            MethodInfo? target = characterSystemType == null
                ? null
                : AccessTools.Method(
                    characterSystemType,
                    "setCharacterClass",
                    new[] { typeof(EntityPlayer), typeof(string), typeof(bool) }
                );
            MethodInfo? postfix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(AfterSetCharacterClass)
            );

            if (target != null && postfix != null)
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            }

            MethodInfo? traitTextTarget = characterSystemType == null
                ? null
                : AccessTools.Method(characterSystemType, "getClassTraitText");
            MethodInfo? traitTextPostfix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(AfterGetClassTraitText)
            );
            if (traitTextTarget != null && traitTextPostfix != null)
            {
                harmony.Patch(
                    traitTextTarget,
                    postfix: new HarmonyMethod(traitTextPostfix)
                );
            }
        }

        private void PatchPlayerRenderers(ICoreAPI api)
        {
            Type? rendererType = AccessTools.TypeByName(
                "Vintagestory.GameContent.EntityPlayerShapeRenderer"
            );
            if (rendererType == null) return;

            PatchRendererMethod(
                rendererType,
                "loadModelMatrixForPlayer",
                nameof(AfterPlayerModelMatrix)
            );
            PatchRendererMethod(
                rendererType.BaseType,
                "loadModelMatrixForGui",
                nameof(AfterGuiModelMatrix)
            );

            if (harmony == null || api.Side != EnumAppSide.Client) return;

            MethodInfo? meshTarget = rendererType.GetMethod(
                "<Tesselate>b__21_0",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(MeshData) },
                null
            );
            MethodInfo? meshPrefix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(BeforePlayerMeshBuilt)
            );
            if (meshTarget != null && meshPrefix != null)
            {
                harmony.Patch(meshTarget, prefix: new HarmonyMethod(meshPrefix));
            }

            MethodInfo? immersiveFilter = rendererType
                .GetNestedTypes(BindingFlags.NonPublic)
                .Select(type => type.GetMethod(
                    "<Tesselate>b__1",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(int) },
                    null
                ))
                .FirstOrDefault(method => method?.ReturnType == typeof(bool));
            MethodInfo? filterPostfix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(AfterImmersiveFirstPersonVertexFilter)
            );
            if (immersiveFilter != null && filterPostfix != null)
            {
                harmony.Patch(
                    immersiveFilter,
                    postfix: new HarmonyMethod(filterPostfix)
                );
            }
        }

        private static void BeforePlayerMeshBuilt(object __instance, MeshData __0)
        {
            FirstPersonHiddenJoints.Remove(__0);

            Entity? entity = AccessTools.Field(__instance.GetType(), "entity")
                ?.GetValue(__instance) as Entity;
            IAnimator? animator = entity?.AnimManager?.Animator;
            if (animator == null) return;

            HashSet<int> jointIds = new();
            foreach (string poseName in FirstPersonHiddenPoseNames)
            {
                ElementPose? pose = animator.GetPosebyName(
                    poseName,
                    StringComparison.OrdinalIgnoreCase
                );
                if (pose != null)
                {
                    CollectPoseJointIds(pose, jointIds);
                }
            }

            if (jointIds.Count > 0)
            {
                FirstPersonHiddenJoints.Add(__0, jointIds);
            }
        }

        private static void CollectPoseJointIds(
            ElementPose pose,
            HashSet<int> jointIds)
        {
            int jointId = pose.ForElement?.JointId ?? 0;
            if (jointId > 0)
            {
                jointIds.Add(jointId);
            }

            if (pose.ChildElementPoses == null) return;
            foreach (ElementPose child in pose.ChildElementPoses)
            {
                CollectPoseJointIds(child, jointIds);
            }
        }

        private static void AfterImmersiveFirstPersonVertexFilter(
            object __instance,
            int __0,
            ref bool __result)
        {
            if (!__result) return;

            object? meshClosure = AccessTools.Field(
                __instance.GetType(),
                "CS$<>8__locals1"
            )?.GetValue(__instance);
            if (meshClosure == null) return;

            MeshData? mesh = AccessTools.Field(meshClosure.GetType(), "meshData")
                ?.GetValue(meshClosure) as MeshData;
            if (mesh?.CustomInts?.Values == null ||
                !FirstPersonHiddenJoints.TryGetValue(mesh, out HashSet<int>? jointIds))
            {
                return;
            }

            int valueIndex = __0 * 4;
            if (valueIndex >= 0 && valueIndex < mesh.CustomInts.Values.Length &&
                jointIds.Contains(mesh.CustomInts.Values[valueIndex]))
            {
                __result = false;
            }
        }

        private void PatchCharacterDialog()
        {
            Type? dialogType = AccessTools.TypeByName(
                "Vintagestory.GameContent.GuiDialogCreateCharacter"
            );
            if (dialogType == null || harmony == null) return;

            PatchDialogMethod(dialogType, "OnGuiOpened", null, nameof(AfterDialogOpened));
            PatchDialogMethod(dialogType, "OnGuiClosed", null, nameof(AfterDialogClosed));
            PatchDialogMethod(dialogType, "changeClass", null, nameof(AfterRaceChanged));
            PatchDialogMethod(dialogType, "onTabClicked", nameof(BeforeTabClicked), nameof(AfterTabClicked));
            PatchDialogMethod(dialogType, "OnNext", nameof(BeforeSkinConfirmNext), null);
            PatchDialogMethod(dialogType, "OnConfirm", nameof(BeforeDialogConfirm), null);
            PatchDialogMethod(dialogType, "ComposeGuis", nameof(BeforeComposeGuis), nameof(AfterComposeGuis));

            MethodInfo? composerWheelTarget = AccessTools.Method(
                typeof(GuiComposer),
                "OnMouseWheel",
                new[] { typeof(MouseWheelEventArgs) }
            );
            MethodInfo? composerWheelPrefix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(BeforeGuiComposerMouseWheel)
            );
            if (composerWheelTarget != null && composerWheelPrefix != null)
            {
                harmony.Patch(
                    composerWheelTarget,
                    prefix: new HarmonyMethod(composerWheelPrefix)
                );
            }

            ConstructorInfo? tabsConstructor = AccessTools.Constructor(
                typeof(GuiElementHorizontalTabs),
                new[]
                {
                    typeof(ICoreClientAPI),
                    typeof(GuiTab[]),
                    typeof(CairoFont),
                    typeof(CairoFont),
                    typeof(ElementBounds),
                    typeof(Action<int>)
                }
            );
            MethodInfo? tabsPrefix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(BeforeHorizontalTabsConstructed)
            );
            if (tabsConstructor != null && tabsPrefix != null)
            {
                harmony.Patch(tabsConstructor, prefix: new HarmonyMethod(tabsPrefix));
            }

            MethodInfo? addButtonTarget = AccessTools.Method(
                typeof(Vintagestory.API.Client.GuiComposerHelpers),
                nameof(Vintagestory.API.Client.GuiComposerHelpers.AddButton),
                new[]
                {
                    typeof(GuiComposer),
                    typeof(string),
                    typeof(ActionConsumable),
                    typeof(ElementBounds),
                    typeof(CairoFont),
                    typeof(EnumButtonStyle),
                    typeof(string)
                }
            );
            MethodInfo? addButtonPrefix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(BeforeCharacterSkinButtonAdded)
            );
            if (addButtonTarget != null && addButtonPrefix != null)
            {
                harmony.Patch(
                    addButtonTarget,
                    prefix: new HarmonyMethod(addButtonPrefix)
                );
            }

            MethodInfo? skinPartTarget = AccessTools.Method(
                typeof(EntityBehaviorExtraSkinnable),
                "selectSkinPart",
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }
            );
            MethodInfo? skinPartPostfix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(AfterSkinPartSelected)
            );
            if (skinPartTarget != null && skinPartPostfix != null)
            {
                harmony.Patch(
                    skinPartTarget,
                    postfix: new HarmonyMethod(skinPartPostfix)
                );
            }
        }

        private static void AfterSkinPartSelected(
            EntityBehaviorExtraSkinnable __instance,
            string __0)
        {
            if (refreshingRaceShapeTexture ||
                (__0 != "baseskin" && __0 != "eyecolor" && __0 != "haircolor"))
            {
                return;
            }

            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null ||
                !ReferenceEquals(player.GetBehavior("skinnableplayer"), __instance))
            {
                return;
            }

            // Extra race geometry is baked with the composed Seraph atlas. Reapply
            // the hidden shape after a palette change so Dragonborn snout/scales,
            // visible eyes and Dwarf hair-colored brows use the newly selected
            // texture rather than the atlas that existed when the shape was added.
            refreshingRaceShapeTexture = true;
            try
            {
                SelectHiddenPart(
                    __instance,
                    SkinPartCode,
                    GetProfile(player).Code
                );
            }
            finally
            {
                refreshingRaceShapeTexture = false;
            }
        }

        private void PatchDialogMethod(
            Type dialogType,
            string methodName,
            string? prefixName,
            string? postfixName)
        {
            if (harmony == null) return;
            MethodInfo? target = AccessTools.Method(dialogType, methodName);
            MethodInfo? prefix = prefixName == null
                ? null
                : AccessTools.Method(typeof(RaceAppearanceSystem), prefixName);
            MethodInfo? postfix = postfixName == null
                ? null
                : AccessTools.Method(typeof(RaceAppearanceSystem), postfixName);
            if (target != null)
            {
                harmony.Patch(
                    target,
                    prefix: prefix == null ? null : new HarmonyMethod(prefix),
                    postfix: postfix == null ? null : new HarmonyMethod(postfix)
                );
            }
        }

        private void PatchFinalGuiEntityRender(ICoreClientAPI capi)
        {
            if (harmony == null) return;

            MethodInfo? target = capi.Render.GetType().GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
            ).FirstOrDefault(method =>
            {
                if (!method.Name.EndsWith("RenderEntityToGui", StringComparison.Ordinal))
                    return false;
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 8 &&
                    parameters[1].ParameterType == typeof(Entity) &&
                    parameters[2].ParameterType == typeof(double) &&
                    parameters[3].ParameterType == typeof(double) &&
                    parameters[6].ParameterType == typeof(float);
            });

            MethodInfo? prefix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                nameof(BeforeRenderEntityToGui)
            );
            if (target != null && prefix != null)
            {
                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            }
        }

        private void PatchRendererMethod(
            Type? rendererType,
            string targetName,
            string postfixName)
        {
            if (rendererType == null || harmony == null) return;
            MethodInfo? target = AccessTools.Method(rendererType, targetName);
            MethodInfo? postfix = AccessTools.Method(
                typeof(RaceAppearanceSystem),
                postfixName
            );
            if (target != null && postfix != null)
            {
                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            }
        }

    }
}
