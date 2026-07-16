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
    public sealed class RaceAppearanceSystem : ModSystem
    {
        private const string HarmonyId = "apprentice.raceappearance.2.2.3-n";
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
                "[Apprentice] Installed 2.2.3-n race customization."
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

        private static void BeforeHorizontalTabsConstructed(ref GuiTab[] tabs)
        {
            if (tabs.Length != 2 ||
                !tabs.Any(tab => tab.DataInt == 0) ||
                !tabs.Any(tab => tab.DataInt == 1))
            {
                return;
            }

            GuiTab? skin = tabs.FirstOrDefault(tab => tab.DataInt == 0);
            GuiTab? race = tabs.FirstOrDefault(tab => tab.DataInt == 1);
            if (skin == null || race == null) return;

            race.Name = Lang.Get("apprentice:tab-race");
            skin.Name = Lang.Get("apprentice:tab-skinvoice");
            tabs = new[] { race, skin };
        }

        private static bool BeforeCharacterSkinButtonAdded(
            GuiComposer composer,
            string text,
            ref GuiComposer __result)
        {
            if (activeCharacterDialog == null ||
                !ReferenceEquals(GetCharacterComposer(activeCharacterDialog), composer))
            {
                return true;
            }

            bool unnecessarySkinAction =
                text == Lang.Get("Randomize") ||
                text == Lang.Get("Last selection");
            if (!unnecessarySkinAction) return true;

            // Keep the fluent composer chain intact while omitting both buttons.
            __result = composer;
            return false;
        }

        private static void AfterDialogOpened(object __instance)
        {
            activeCharacterDialog = __instance;
            ConfirmedRaceDialogs.Remove(__instance);
            SetDialogTab(__instance, 1);
            ResetDialogZoom(__instance);
            AccessTools.Method(__instance.GetType(), "ComposeGuis")?.Invoke(__instance, null);
            FixActiveTab(__instance);
        }

        private static void AfterDialogClosed(object __instance)
        {
            RestoreNaturalPalette();
            ConfirmedRaceDialogs.Remove(__instance);
            if (ReferenceEquals(pendingSkinConfirmDialog, __instance))
            {
                pendingSkinConfirmDialog = null;
            }
            if (ReferenceEquals(activeCharacterDialog, __instance))
            {
                activeCharacterDialog = null;
            }
            DestroyOptionsDialog();
            DestroyHornColorDialog();
        }

        private static void AfterRaceChanged(object __instance)
        {
            activeCharacterDialog = __instance;
            ConfirmedRaceDialogs.Remove(__instance);
            ResetDialogZoom(__instance);
            UpdateBodyTrait(__instance);
            RefreshOptionsDialog(__instance);
        }

        private static bool BeforeTabClicked(object __instance, int tabid)
        {
            if (tabid == 0 && !ConfirmedRaceDialogs.Contains(__instance))
            {
                clientApi?.TriggerIngameError(
                    typeof(RaceAppearanceSystem),
                    "race-first",
                    Lang.Get("apprentice:confirm-race-first")
                );
                return false;
            }
            return true;
        }

        private static void AfterTabClicked(object __instance)
        {
            activeCharacterDialog = __instance;
            FixActiveTab(__instance);
            RefreshOptionsDialog(__instance);
        }

        private static bool BeforeSkinConfirmNext(
            object __instance,
            ref bool __result)
        {
            if (GetDialogTab(__instance) != 0 ||
                !ConfirmedRaceDialogs.Contains(__instance))
            {
                return true;
            }

            // The stock Skin button calls OnNext() from inside the dialog's
            // OnMouseDown handler. Closing synchronously here disposes its GUI
            // composer while that handler is still using it. Finish on the next
            // client frame, after the mouse event has returned.
            if (!ReferenceEquals(pendingSkinConfirmDialog, __instance))
            {
                EntityPlayer? player = clientApi?.World.Player?.Entity;
                if (player != null)
                {
                    SendBodyPacket(player);
                }

                object dialog = __instance;
                pendingSkinConfirmDialog = dialog;
                clientApi?.Event.EnqueueMainThreadTask(
                    () =>
                    {
                        if (ReferenceEquals(pendingSkinConfirmDialog, dialog))
                        {
                            pendingSkinConfirmDialog = null;
                        }
                        if (!ReferenceEquals(activeCharacterDialog, dialog) ||
                            GetDialogTab(dialog) != 0 ||
                            !ConfirmedRaceDialogs.Contains(dialog))
                        {
                            return;
                        }
                        AccessTools.Method(dialog.GetType(), "OnConfirm")
                            ?.Invoke(dialog, null);
                    },
                    "apprentice-confirm-skin"
                );
            }
            __result = true;
            return false;
        }

        private static bool BeforeDialogConfirm(object __instance, ref bool __result)
        {
            if (GetDialogTab(__instance) != 1) return true;

            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player != null)
            {
                RaceProfile profile = GetProfile(player);
                if (HasSubclasses(profile) && GetSelectedSubclass(player, profile) == null)
                {
                    clientApi?.TriggerIngameError(
                        typeof(RaceAppearanceSystem),
                        "subclass-required",
                        Lang.Get("apprentice:select-subclass-first")
                    );
                    __result = false;
                    return false;
                }
                if (GetProfessionChoice(player) == "select")
                {
                    clientApi?.TriggerIngameError(
                        typeof(RaceAppearanceSystem),
                        "profession-required",
                        Lang.Get("apprentice:select-profession-first")
                    );
                    __result = false;
                    return false;
                }
            }

            ConfirmedRaceDialogs.Add(__instance);
            if (player != null)
            {
                SendBodyPacket(player);
            }
            SetDialogTab(__instance, 0);
            AccessTools.Method(__instance.GetType(), "ComposeGuis")?.Invoke(__instance, null);
            HideOptionsDialog();
            __result = true;
            return false;
        }

        private static void BeforeComposeGuis(object __instance)
        {
            activeCharacterDialog = __instance;
            if (GetDialogTab(__instance) == 0)
            {
                ApplyNaturalPalette();
            }
            else
            {
                RestoreNaturalPalette();
            }
        }

        private static void AfterComposeGuis(object __instance)
        {
            FixActiveTab(__instance);
            UpdateBodyTrait(__instance);
            RefreshOptionsDialog(__instance);
        }

        private static GuiComposer? GetCharacterComposer(object dialog)
        {
            if (dialog is GuiDialog guiDialog)
            {
                if (guiDialog.Composers.ContainsKey("createcharacter"))
                {
                    return guiDialog.Composers["createcharacter"];
                }

                GuiComposer? matchingComposer = guiDialog.Composers.Values.FirstOrDefault(
                    composer => composer.GetHorizontalTabs("tabs") != null ||
                        composer.GetRichtext("characterDesc") != null
                );
                if (matchingComposer != null)
                {
                    return matchingComposer;
                }
            }

            return AccessTools.Property(dialog.GetType(), "SingleComposer")
                ?.GetValue(dialog) as GuiComposer;
        }

        private static void FixActiveTab(object dialog)
        {
            if (GetCharacterComposer(dialog) is not GuiComposer composer ||
                composer.GetHorizontalTabs("tabs") is not GuiElementHorizontalTabs tabsElement)
            {
                return;
            }

            int tabId = GetDialogTab(dialog);
            int index = Array.FindIndex(tabsElement.tabs, tab => tab.DataInt == tabId);
            if (index >= 0)
            {
                // activeElement is an array index, while curTab stores GuiTab.DataInt.
                // Reordering Race before Skin & Voice means those values are no
                // longer interchangeable.
                tabsElement.SetValue(index, false);
            }
        }

        private static void RefreshOptionsDialog(object dialog)
        {
            RefreshHornColorDialog(dialog);

            ICoreClientAPI? capi = clientApi;
            EntityPlayer? player = capi?.World.Player?.Entity;
            if (capi == null || player == null || GetDialogTab(dialog) != 1)
            {
                HideOptionsDialog();
                return;
            }

            RaceProfile profile = GetProfile(player);
            FieldInfo? heightField = AccessTools.Field(dialog.GetType(), "dlgHeight");
            int dialogHeight = heightField?.GetValue(dialog) is int value ? value : 500;

            if (optionsDialog == null)
            {
                optionsDialog = new RaceOptionsDialog(
                    capi,
                    dialogHeight,
                    profile,
                    OnBodyOptionsChanged
                );
            }
            else
            {
                optionsDialog.Refresh(profile);
            }

            if (!optionsDialog.IsOpened())
            {
                optionsDialog.TryOpen();
            }
        }

        private static void HideOptionsDialog()
        {
            if (optionsDialog?.IsOpened() == true)
            {
                optionsDialog.TryClose();
            }
        }

        private static void DestroyOptionsDialog()
        {
            HideOptionsDialog();
            optionsDialog?.Dispose();
            optionsDialog = null;
        }

        private static void RefreshHornColorDialog(object dialog)
        {
            ICoreClientAPI? capi = clientApi;
            EntityPlayer? player = capi?.World.Player?.Entity;
            RaceProfile? profile = player == null ? null : GetProfile(player);
            bool hasHorns = profile?.HornCodes.Any(code => code != "none") == true;
            if (capi == null ||
                player == null ||
                GetDialogTab(dialog) != 0 ||
                !ConfirmedRaceDialogs.Contains(dialog) ||
                !hasHorns)
            {
                HideHornColorDialog();
                return;
            }

            FieldInfo? heightField = AccessTools.Field(dialog.GetType(), "dlgHeight");
            int dialogHeight = heightField?.GetValue(dialog) is int value ? value : 500;

            if (hornColorDialog == null)
            {
                hornColorDialog = new HornColorDialog(
                    capi,
                    dialogHeight,
                    OnSkinHornColorChanged
                );
            }
            else
            {
                hornColorDialog.Refresh();
            }

            if (!hornColorDialog.IsOpened())
            {
                hornColorDialog.TryOpen();
            }
        }

        private static void HideHornColorDialog()
        {
            if (hornColorDialog?.IsOpened() == true)
            {
                hornColorDialog.TryClose();
            }
        }

        private static void DestroyHornColorDialog()
        {
            HideHornColorDialog();
            hornColorDialog?.Dispose();
            hornColorDialog = null;
        }

        private static void OnSkinHornColorChanged(string hornColor)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null) return;

            RaceProfile profile = GetProfile(player);
            SaveBodyChoices(
                player,
                profile,
                GetHeightChoice(player),
                GetThicknessChoice(player),
                GetHornChoice(player, profile),
                GetTeethChoice(player, profile),
                GetSubclassChoice(player, profile),
                GetProfessionChoice(player),
                hornColor
            );
            ApplyRaceAppearance(player, GetClassCode(player));
            SendBodyPacket(player);
        }

        private static void OnBodyOptionsChanged(
            float height,
            float thickness,
            string horns,
            string teeth,
            string subclass,
            string profession)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null) return;

            RaceProfile profile = GetProfile(player);
            SaveBodyChoices(
                player,
                profile,
                height,
                thickness,
                horns,
                teeth,
                subclass,
                profession,
                GetHornColorChoice(player)
            );
            ApplyRaceAppearance(player, GetClassCode(player));
            ApplyBodyStats(player, profile);
            if (activeCharacterDialog != null)
            {
                ConfirmedRaceDialogs.Remove(activeCharacterDialog);
                ResetDialogZoom(activeCharacterDialog);
                UpdateBodyTrait(activeCharacterDialog);
            }
            SendBodyPacket(player);
        }

        private static bool BeforeGuiComposerMouseWheel(MouseWheelEventArgs __0)
        {
            object? dialog = activeCharacterDialog;
            ICoreClientAPI? capi = clientApi;
            if (dialog == null || capi == null) return true;

            FieldInfo? boundsField = AccessTools.Field(
                dialog.GetType(),
                "insetSlotBounds"
            );
            if (boundsField?.GetValue(dialog) is not ElementBounds bounds ||
                !bounds.PointInside(capi.Input.MouseX, capi.Input.MouseY))
            {
                return true;
            }

            FieldInfo? zoomField = AccessTools.Field(dialog.GetType(), "charZoom");
            EntityPlayer? player = capi.World.Player?.Entity;
            if (zoomField?.FieldType != typeof(float) || player == null) return true;

            RaceProfile profile = GetProfile(player);
            float minZoom = GetMinimumPreviewZoom(profile, player);
            float maxZoom = GetMaximumPreviewZoom(profile, player);
            float current = (float)zoomField.GetValue(dialog)!;
            zoomField.SetValue(
                dialog,
                Math.Clamp(current + __0.deltaPrecise / 8f, minZoom, maxZoom)
            );
            __0.SetHandled(true);
            return false;
        }

        private static void BeforeRenderEntityToGui(
            Entity __1,
            ref double __2,
            ref double __3,
            ref float __6)
        {
            object? dialog = activeCharacterDialog;
            if (dialog == null || clientApi?.World.Player?.Entity != __1) return;

            RaceProfile profile = GetProfile(__1);
            FieldInfo? tabField = AccessTools.Field(dialog.GetType(), "curTab");
            FieldInfo? zoomField = AccessTools.Field(dialog.GetType(), "charZoom");
            if (tabField?.FieldType != typeof(int) || zoomField?.FieldType != typeof(float))
                return;

            int tab = (int)tabField.GetValue(dialog)!;
            float zoom = (float)zoomField.GetValue(dialog)!;
            float originalSize = __6;
            float cameraBasis = tab == 1
                ? originalSize
                : originalSize / Math.Max(zoom, 0.01f) * 0.62f;

            if (tab == 1)
            {
                __6 *= zoom;
                __2 += originalSize - __6;
            }

            float minZoom = GetMinimumPreviewZoom(profile, __1);
            float maxZoom = GetMaximumPreviewZoom(profile, __1);
            float progress = Math.Clamp(
                (zoom - minZoom) / Math.Max(maxZoom - minZoom, 0.01f),
                0f,
                1f
            );
            __2 += cameraBasis * profile.PreviewXOffset;
            __3 += cameraBasis * (
                profile.PreviewYOffset + progress * profile.PreviewFacePan
            );
        }

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

        internal static string DescribeBodyEffects(
            RaceProfile profile,
            float height,
            float thickness)
        {
            BodyEffects effects = CalculateBodyEffects(profile, height, thickness);
            return string.Format(
                Lang.Get("apprentice:race-body-effects"),
                Signed(effects.Health, 1),
                SignedPercent(effects.Melee),
                SignedPercent(effects.Movement),
                SignedPercent(effects.Hunger),
                SignedPercent(effects.ToolSpeed),
                SignedPercent(effects.Yield)
            );
        }

        private static string DescribeBodyTrait(
            RaceProfile profile,
            float height,
            float thickness)
        {
            BodyEffects effects = CalculateBodyEffects(profile, height, thickness);
            List<string> parts = new();
            if (Math.Abs(effects.Health) >= 0.05f)
                parts.Add($"{Signed(effects.Health, 1)} HP");
            if (Math.Abs(effects.Melee) >= 0.0005f)
                parts.Add($"{SignedPercentCompact(effects.Melee)} melee");
            if (Math.Abs(effects.Movement) >= 0.0005f)
                parts.Add($"{SignedPercentCompact(effects.Movement)} move");
            if (Math.Abs(effects.Hunger) >= 0.0005f)
                parts.Add($"{SignedPercentCompact(effects.Hunger)} hunger");

            if (Math.Abs(effects.ToolSpeed - effects.Yield) < 0.0005f &&
                Math.Abs(effects.ToolSpeed) >= 0.0005f)
            {
                parts.Add($"{SignedPercentCompact(effects.ToolSpeed)} tools/yield");
            }
            else
            {
                if (Math.Abs(effects.ToolSpeed) >= 0.0005f)
                    parts.Add($"{SignedPercentCompact(effects.ToolSpeed)} tools");
                if (Math.Abs(effects.Yield) >= 0.0005f)
                    parts.Add($"{SignedPercentCompact(effects.Yield)} yield");
            }

            if (parts.Count == 0) parts.Add("no change");
            return $"{BodyTraitFallback} ({string.Join(", ", parts)})";
        }

        private static string CompactTraitText(string text)
        {
            string compact = Regex.Replace(
                text,
                @"Animals detect you from ([+\-]?\d+(?:[\.,]\d+)?)% farther away",
                "Animal detection +$1%",
                RegexOptions.IgnoreCase
            );
            compact = Regex.Replace(
                compact,
                @"Animals detect you from ([+\-]?\d+(?:[\.,]\d+)?)% less distance",
                "Animal detection -$1%",
                RegexOptions.IgnoreCase
            );
            compact = Regex.Replace(
                compact,
                @"([+\-]?\d+(?:[\.,]\d+)?)% less armor slowdown",
                "-$1% armor slow",
                RegexOptions.IgnoreCase
            );

            return compact
                .Replace("health points", "HP", StringComparison.OrdinalIgnoreCase)
                .Replace("health point", "HP", StringComparison.OrdinalIgnoreCase)
                .Replace("ranged damage", "ranged dmg", StringComparison.OrdinalIgnoreCase)
                .Replace("melee damage", "melee dmg", StringComparison.OrdinalIgnoreCase)
                .Replace("mechanical damage", "mech dmg", StringComparison.OrdinalIgnoreCase)
                .Replace("movement speed", "move", StringComparison.OrdinalIgnoreCase)
                .Replace("mining speed", "mining", StringComparison.OrdinalIgnoreCase)
                .Replace("ranged accuracy", "ranged acc.", StringComparison.OrdinalIgnoreCase)
                .Replace("temporal repair cost", "repair cost", StringComparison.OrdinalIgnoreCase)
                .Replace("hunger rate", "hunger", StringComparison.OrdinalIgnoreCase)
                .Replace("rusty-gear yield", "gear yield", StringComparison.OrdinalIgnoreCase)
                .Replace("forage yield", "forage", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceBodyTrait(string text, Entity entity)
        {
            int start = text.IndexOf(BodyTraitFallback, StringComparison.Ordinal);
            if (start < 0) return text;

            int lineEnd = text.IndexOf('\n', start);
            RaceProfile profile = GetProfile(entity);
            string bodyTrait = DescribeBodyTrait(
                profile,
                GetHeightChoice(entity),
                GetThicknessChoice(entity)
            );
            return lineEnd < 0
                ? text[..start] + bodyTrait
                : text[..start] + bodyTrait + text[lineEnd..];
        }

        private static string InsertSubclassTrait(string text, Entity entity)
        {
            RaceProfile profile = GetProfile(entity);
            if (!HasSubclasses(profile)) return text;

            const string color = "#84c7ff";
            SubclassProfile? subclass = GetSelectedSubclass(entity, profile);
            string line = subclass == null
                ? $"<font color=\"{color}\">• {Lang.Get("apprentice:select-subclass")}</font>"
                : $"<font color=\"{color}\">• {subclass.Name}</font> ({subclass.TraitSummary})";

            int bodyStart = text.IndexOf(BodyTraitFallback, StringComparison.Ordinal);
            if (bodyStart < 0) return text;
            int lineStart = text.LastIndexOf('\n', bodyStart);
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            return text[..lineStart] + line + "\n" + text[lineStart..];
        }

        private static void AfterGetClassTraitText(ref string __result)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player != null)
            {
                __result = CompactTraitText(ReplaceBodyTrait(__result, player));
            }
        }

        private static void UpdateBodyTrait(object dialog)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player == null ||
                GetCharacterComposer(dialog) is not GuiComposer composer ||
                composer.GetRichtext("characterDesc") is not GuiElementRichtext richtext)
            {
                return;
            }

            if (AccessTools.Field(dialog.GetType(), "modSys")?.GetValue(dialog)
                    is not CharacterSystem characterSystem ||
                AccessTools.Method(characterSystem.GetType(), "getClassTraitText")
                    ?.Invoke(characterSystem, null) is not string traitText)
            {
                return;
            }

            string updated =
                Lang.Get("characterdesc-" + GetClassCode(player)) +
                "\n\n" + Lang.Get("traits-title") + "\n" +
                CompactTraitText(
                    InsertSubclassTrait(ReplaceBodyTrait(traitText, player), player)
                );
            // Use the compact Human-sized trait text for every race and
            // subclass. This keeps long subclass/body lines above the options.
            CairoFont font = CairoFont.WhiteDetailText();
            richtext.SetNewText(updated, font, null);
        }

        private static string Signed(float value, int decimals) =>
            value.ToString(value >= 0 ? "+0." + new string('0', decimals) : "0." + new string('0', decimals));

        private static string SignedPercent(float value) =>
            $"{value * 100:+0.0;-0.0;0.0}%";

        private static string SignedPercentCompact(float value)
        {
            float percent = value * 100;
            return Math.Abs(percent - MathF.Round(percent)) < 0.05f
                ? $"{percent:+0;-0;0}%"
                : $"{percent:+0.0;-0.0;0.0}%";
        }

        private static BodyEffects CalculateBodyEffects(
            RaceProfile profile,
            float height,
            float thickness)
        {
            float effectiveHeight = GetEffectiveHeight(profile, height);
            float sizeScore = effectiveHeight >= 1f
                ? (effectiveHeight - 1f) / (GlobalMaximumHeight - 1f)
                : (effectiveHeight - 1f) / (1f - GlobalMinimumHeight);
            sizeScore = Math.Clamp(sizeScore, -1f, 1f);

            float health = 4f * sizeScore;
            float melee = 0.10f * sizeScore;
            float movement = 0.04f * sizeScore;
            float hunger = sizeScore > 0 ? 0.20f * sizeScore : 0f;
            float toolSpeed = sizeScore < 0 ? 0.10f * -sizeScore : 0f;
            float yield = toolSpeed;

            float buildScore = (Math.Clamp(thickness, 0f, 1f) - 0.5f) * 2f;
            if (buildScore > 0)
            {
                hunger -= 0.10f * buildScore;
                movement -= 0.06f * buildScore;
            }
            else
            {
                hunger += 0.05f * -buildScore;
                movement += 0.03f * -buildScore;
            }

            return new BodyEffects(
                health,
                melee,
                movement,
                hunger,
                toolSpeed,
                yield
            );
        }

        private static void ApplyBodyStats(EntityPlayer player, RaceProfile profile)
        {
            BodyEffects effects = CalculateBodyEffects(
                profile,
                GetHeightChoice(player),
                GetThicknessChoice(player)
            );

            player.Stats.Set("maxhealthExtraPoints", BodyStatSource, effects.Health, false);
            player.Stats.Set("meleeWeaponsDamage", BodyStatSource, effects.Melee, false);
            player.Stats.Set("walkspeed", BodyStatSource, effects.Movement, false);
            player.Stats.Set("hungerrate", BodyStatSource, effects.Hunger, false);
            player.Stats.Set("miningSpeedMul", BodyStatSource, effects.ToolSpeed, false);
            player.Stats.Set("animalHarvestingTime", BodyStatSource, -effects.ToolSpeed, false);

            foreach (string stat in new[]
            {
                "forageDropRate", "oreDropRate", "wildCropDropRate",
                "vesselContentsDropRate", "rustyGearDropRate", "animalLootDropRate"
            })
            {
                player.Stats.Set(stat, BodyStatSource, effects.Yield, false);
            }

            ApplySubclassStats(player, profile);

            if (player.Api.Side == EnumAppSide.Server)
            {
                RefreshHealth(player);
            }
        }

        private static void ApplySubclassStats(EntityPlayer player, RaceProfile profile)
        {
            foreach (string stat in SubclassStatNames)
            {
                player.Stats.Set(stat, SubclassStatSource, 0f, false);
            }

            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            if (subclass == null) return;
            foreach ((string stat, float value) in subclass.Stats)
            {
                player.Stats.Set(stat, SubclassStatSource, value, false);
            }
        }

        private static void RefreshHealth(EntityPlayer player)
        {
            IEnumerable<EntityBehavior> behaviors = player.ServerBehaviorsMainThread
                .Concat(player.ServerBehaviorsThreadsafe);
            foreach (EntityBehavior behavior in behaviors)
            {
                if (!behavior.GetType().Name.Contains("Health", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    AccessTools.Method(behavior.GetType(), "MarkDirty")?.Invoke(behavior, null);
                }
                catch
                {
                    // A later join, race confirmation or skill refresh retries this.
                }
                return;
            }
        }

        internal static void ApplyRaceAppearance(EntityPlayer player, string classCode)
        {
            RaceProfile profile = GetProfile(classCode);
            ApplyPhysicalProportions(player, profile);

            EntityBehavior? skin = player.GetBehavior("skinnableplayer");
            if (skin == null) return;

            selectSkinPartMethod ??= AccessTools.Method(
                skin.GetType(),
                "selectSkinPart",
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }
            );

            ApplyRaceSkin(player, skin, profile);
            ApplyFacialIdentity(player, skin, profile);
            RefreshHiddenRaceParts(player, profile);
        }

        private static void RefreshHiddenRaceParts(
            EntityPlayer player,
            RaceProfile profile)
        {
            EntityBehavior? skin = player.GetBehavior("skinnableplayer");
            if (skin == null) return;

            selectSkinPartMethod ??= AccessTools.Method(
                skin.GetType(),
                "selectSkinPart",
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }
            );
            string hornColor = GetHornColorChoice(player);
            SelectHiddenPart(skin, HornColorPartCode, hornColor);
            SelectHiddenPart(skin, HornPartCode, GetHornChoice(player, profile));
            SelectHiddenPart(skin, TeethPartCode, GetTeethChoice(player, profile));
            SelectHiddenPart(skin, SkinPartCode, profile.Code);
        }

        private static void RestoreAppearanceParts(
            EntityPlayer player,
            RaceProfile profile,
            Dictionary<string, string>? appearanceParts)
        {
            if (appearanceParts == null || appearanceParts.Count == 0 ||
                player.GetBehavior("skinnableplayer") is not EntityBehaviorExtraSkinnable skin)
            {
                return;
            }

            selectSkinPartMethod ??= AccessTools.Method(
                skin.GetType(),
                "selectSkinPart",
                new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) }
            );
            if (selectSkinPartMethod == null) return;

            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            string[] skinCodes = subclass?.SkinCodes ?? profile.SkinCodes;
            string[] eyeCodes = subclass?.EyeColors ?? profile.EyeCodes;
            string[] hairCodes = subclass?.HairColors ?? profile.HairCodes;

            foreach (string partCode in PersistentAppearancePartCodes)
            {
                if (!appearanceParts.TryGetValue(partCode, out string? value) ||
                    string.IsNullOrEmpty(value) ||
                    !skin.AvailableSkinPartsByCode.TryGetValue(
                        partCode,
                        out SkinnablePart? part
                    ) ||
                    !part.VariantsByCode.ContainsKey(value))
                {
                    continue;
                }

                if ((partCode == "baseskin" && !skinCodes.Contains(value, StringComparer.Ordinal)) ||
                    (partCode == "eyecolor" && !eyeCodes.Contains(value, StringComparer.Ordinal)) ||
                    (partCode == "haircolor" && !hairCodes.Contains(value, StringComparer.Ordinal)))
                {
                    continue;
                }

                selectSkinPartMethod.Invoke(
                    skin,
                    new object[] { partCode, value, true, false }
                );
            }
        }

        private static void SelectHiddenPart(
            EntityBehavior skin,
            string partCode,
            string variantCode)
        {
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { partCode, variantCode, true, false }
            );
        }

        private static void ApplyFacialIdentity(
            EntityPlayer player,
            EntityBehavior skin,
            RaceProfile profile)
        {
            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            SelectRacePart(player, skin, "facialexpression", profile.Face);
            if (subclass != null)
            {
                SelectAllowedRacePart(
                    player,
                    skin,
                    "eyecolor",
                    subclass.EyeColors
                );
                SelectAllowedRacePart(
                    player,
                    skin,
                    "haircolor",
                    subclass.HairColors
                );
            }
            else
            {
                SelectAllowedRacePart(player, skin, "eyecolor", profile.EyeCodes);
                SelectAllowedRacePart(player, skin, "haircolor", profile.HairCodes);
            }

            string defaultsKey = profile.Code + ":" + (subclass?.Code ?? "base");
            string appliedDefaults = player.WatchedAttributes.GetString(
                AppearanceDefaultsAttribute,
                ""
            );
            if (appliedDefaults != defaultsKey)
            {
                string? hairStyle = subclass?.HairStyle ?? profile.Hair;
                if (hairStyle != null)
                {
                    SelectRacePart(player, skin, "hairbase", hairStyle);
                }
                if (subclass != null)
                {
                    SelectRacePart(player, skin, "hairextra", subclass.HairExtra);
                }
                if (profile.Beard != null)
                {
                    SelectRacePart(player, skin, "beard", profile.Beard);
                }
                player.WatchedAttributes.SetString(
                    AppearanceDefaultsAttribute,
                    defaultsKey
                );
            }
        }

        private static void SelectAllowedRacePart(
            EntityPlayer player,
            EntityBehavior skin,
            string partCode,
            string[] allowed)
        {
            if (allowed.Length == 0) return;
            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");
            string? current = appliedParts?.GetString(partCode, null);
            string selected = current != null && allowed.Contains(
                current,
                StringComparer.Ordinal
            ) ? current : allowed[0];
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { partCode, selected, true, false }
            );
        }

        private static void SelectRacePart(
            EntityPlayer player,
            EntityBehavior skin,
            string partCode,
            string raceValue)
        {
            string savedKey = "apprenticeOriginal-" + partCode;
            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");

            if (!player.WatchedAttributes.HasAttribute(savedKey))
            {
                string? original = appliedParts?.GetString(partCode, null);
                if (original != null)
                {
                    player.WatchedAttributes.SetString(savedKey, original);
                }
            }

            string selected = profileIsHuman(player)
                ? player.WatchedAttributes.GetString(savedKey, raceValue)
                : raceValue;
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { partCode, selected, true, false }
            );
        }

        private static bool profileIsHuman(EntityPlayer player) =>
            GetClassCode(player) == "apprentice-race-human";

        private static void ApplyRaceSkin(
            EntityPlayer player,
            EntityBehavior skin,
            RaceProfile profile)
        {
            const string savedSkinKey = "apprenticeOriginalBaseSkin";
            ITreeAttribute? appliedParts = player.WatchedAttributes
                .GetTreeAttribute("skinConfig")
                ?.GetTreeAttribute("appliedParts");
            string currentSkin = appliedParts?.GetString("baseskin", "skin4") ?? "skin4";

            if (!player.WatchedAttributes.HasAttribute(savedSkinKey))
            {
                player.WatchedAttributes.SetString(savedSkinKey, currentSkin);
            }

            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            string skinCode;
            if (subclass != null)
            {
                skinCode = subclass.SkinCodes.Contains(
                    currentSkin,
                    StringComparer.Ordinal
                ) ? currentSkin : subclass.SkinCodes[0];
            }
            else
            {
                string savedSkin = player.WatchedAttributes.GetString(
                    savedSkinKey,
                    profile.SkinCodes[0]
                );
                skinCode = profile.SkinCode ??
                    (profile.SkinCodes.Contains(savedSkin, StringComparer.Ordinal)
                        ? savedSkin
                        : profile.SkinCodes[0]);
            }
            selectSkinPartMethod?.Invoke(
                skin,
                new object[] { "baseskin", skinCode, true, false }
            );
        }

        private static void ApplyNaturalPalette()
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player?.GetBehavior("skinnableplayer") is not EntityBehaviorExtraSkinnable skin)
                return;

            RestoreNaturalPalette(skin);
            RaceProfile profile = GetProfile(player);
            SubclassProfile? subclass = GetSelectedSubclass(player, profile);
            List<PaletteSnapshot> snapshots = new();
            FilterPalettePart(
                skin,
                "baseskin",
                subclass == null ? profile.SkinCodes : subclass.SkinCodes,
                snapshots
            );
            FilterPalettePart(
                skin,
                "eyecolor",
                subclass == null ? profile.EyeCodes : subclass.EyeColors,
                snapshots
            );
            FilterPalettePart(
                skin,
                "haircolor",
                subclass == null ? profile.HairCodes : subclass.HairColors,
                snapshots
            );
            PaletteSnapshots[skin] = snapshots;
        }

        private static void FilterPalettePart(
            EntityBehaviorExtraSkinnable skin,
            string partCode,
            string[] allowed,
            List<PaletteSnapshot> snapshots)
        {
            if (!skin.AvailableSkinPartsByCode.TryGetValue(partCode, out SkinnablePart? part))
                return;

            snapshots.Add(new PaletteSnapshot(part, part.Variants, part.VariantsByCode));
            HashSet<string> accepted = new(allowed, StringComparer.Ordinal);
            part.Variants = part.Variants
                .Where(variant => accepted.Contains(variant.Code))
                .GroupBy(variant => variant.Code, StringComparer.Ordinal)
                .Select(group => group.Last())
                .ToArray();
            part.VariantsByCode = part.Variants.ToDictionary(
                variant => variant.Code,
                StringComparer.Ordinal
            );
        }

        private static void RestoreNaturalPalette()
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            if (player?.GetBehavior("skinnableplayer") is EntityBehaviorExtraSkinnable skin)
            {
                RestoreNaturalPalette(skin);
            }
        }

        private static void RestoreNaturalPalette(EntityBehaviorExtraSkinnable skin)
        {
            if (!PaletteSnapshots.Remove(skin, out List<PaletteSnapshot>? snapshots))
                return;
            foreach (PaletteSnapshot snapshot in snapshots)
            {
                snapshot.Part.Variants = snapshot.Variants;
                snapshot.Part.VariantsByCode = snapshot.VariantsByCode;
            }
        }

        private static void ApplyPhysicalProportions(EntityPlayer player, RaceProfile profile)
        {
            float width = GetEffectiveWidth(profile, GetThicknessChoice(player));
            float height = GetEffectiveHeight(profile, GetHeightChoice(player));
            player.SetCollisionBox(0.6f * width, 1.85f * height);
            player.SetSelectionBox(0.6f * width, 1.85f * height);
            player.LocalEyePos.Y = 1.7 * height;
        }

        private static float GetEffectiveHeight(RaceProfile profile, float choice) =>
            profile.Height * Lerp(
                profile.MinHeightScale,
                profile.MaxHeightScale,
                Math.Clamp(choice, 0f, 1f)
            );

        private static float GetEffectiveWidth(RaceProfile profile, float choice) =>
            profile.Width * Lerp(0.86f, 1.14f, Math.Clamp(choice, 0f, 1f));

        private static float Lerp(float min, float max, float value) =>
            min + (max - min) * value;

        private static float GetChosenHeightScale(RaceProfile profile, Entity entity) =>
            Lerp(profile.MinHeightScale, profile.MaxHeightScale, GetHeightChoice(entity));

        private static float GetMinimumPreviewZoom(RaceProfile profile, Entity entity) =>
            profile.MinPreviewZoom / GetChosenHeightScale(profile, entity);

        private static float GetMaximumPreviewZoom(RaceProfile profile, Entity entity) =>
            profile.MaxPreviewZoom / GetChosenHeightScale(profile, entity);

        private static void ResetDialogZoom(object dialog)
        {
            EntityPlayer? player = clientApi?.World.Player?.Entity;
            FieldInfo? zoomField = AccessTools.Field(dialog.GetType(), "charZoom");
            if (player == null || zoomField?.FieldType != typeof(float)) return;
            RaceProfile profile = GetProfile(player);
            zoomField.SetValue(dialog, GetMinimumPreviewZoom(profile, player));
        }

        private static int GetDialogTab(object dialog)
        {
            FieldInfo? tabField = AccessTools.Field(dialog.GetType(), "curTab");
            return tabField?.GetValue(dialog) is int tab ? tab : 1;
        }

        private static void SetDialogTab(object dialog, int tab)
        {
            AccessTools.Field(dialog.GetType(), "curTab")?.SetValue(dialog, tab);
        }

        private static void AfterPlayerModelMatrix(object __instance, Entity entity) =>
            ApplyRenderScale(__instance, entity);

        private static void AfterGuiModelMatrix(object __instance, Entity entity) =>
            ApplyRenderScale(__instance, entity);

        private static void ApplyRenderScale(object renderer, Entity entity)
        {
            RaceProfile profile = GetProfile(entity);
            FieldInfo? modelMatField = AccessTools.Field(renderer.GetType(), "ModelMat");
            if (modelMatField?.GetValue(renderer) is not float[] modelMat) return;

            float width = GetEffectiveWidth(profile, GetThicknessChoice(entity));
            float height = GetEffectiveHeight(profile, GetHeightChoice(entity));
            Mat4f.Scale(modelMat, modelMat, new[] { width, height, width });
        }

        private static string GetClassCode(Entity entity) =>
            entity.WatchedAttributes.GetString(
                "characterClass",
                "apprentice-race-human"
            );

        private static RaceProfile GetProfile(Entity entity) =>
            GetProfile(GetClassCode(entity));

        private static RaceProfile GetProfile(string classCode) =>
            RaceByClass.TryGetValue(classCode, out RaceProfile? profile)
                ? profile
                : HumanProfile;

        public override void Dispose()
        {
            if (serverApi != null)
            {
                serverApi.Event.PlayerJoin -= OnPlayerJoin;
                serverApi.Event.PlayerReady -= OnPlayerReady;
            }
            RestoreNaturalPalette();
            DestroyOptionsDialog();
            DestroyHornColorDialog();
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            clientApi = null;
            serverApi = null;
            clientChannel = null;
            serverChannel = null;
            activeCharacterDialog = null;
            ConfirmedRaceDialogs.Clear();
            PaletteSnapshots.Clear();
            base.Dispose();
        }
    }
}
