namespace Apprentice
{
    internal static class ApprenticeConstants
    {
        public const string NetworkChannel =
            "apprentice-progression";

        public const string BaseConfigFile =
            "ApprenticeConfig.json";

        public const string ProgressionRootKey =
            "apprentice";

        public const string ClassesTreeKey =
            "classes";

        public const string ExperienceKey =
            "experience";

        public const string ExperienceDialogHotKey =
            "experiencedialog";

        public const string SkillTreeConfigAsset =
            "config/skilltrees.json";

    }

    /// <summary>
    /// These names connect generic Vintage Story engine events to
    /// interaction names used by class.json.
    ///
    /// Connected through version 1.4.0: all configured base-game
    /// interaction names, including panning, fishing, breeding, shearing,
    /// right-click/carcass harvesting and expanded smelting/processing.
    /// </summary>
    internal static class InteractionNames
    {
        public const string DestroyBlock = "DestroyBlock";
        public const string PlaceBlock = "PlaceBlock";
        public const string Craft = "Craft";
        public const string Prospect = "Prospect";
        public const string Pan = "Pan";
        public const string Plant = "Plant";
        public const string Harvest = "Harvest";
        public const string Smith = "Smith";
        public const string Smelt = "Smelt";
        public const string Repair = "Repair";
        public const string Cook = "Cook";
        public const string Process = "Process";
        public const string KillPvE = "KillPvE";
        public const string KillPvP = "KillPvP";

        /// <summary>
        /// Kill rewards whose target code is the weapon used rather than
        /// the defeated entity. Victim-based KillPvE/KillPvP rewards stay
        /// available for classes such as Hunter.
        /// </summary>
        public const string WeaponKillPvE =
            "WeaponKillPvE";

        public const string WeaponKillPvP =
            "WeaponKillPvP";

        /// <summary>
        /// One completed shield absorption event.
        /// </summary>
        public const string ShieldBlock =
            "ShieldBlock";

        /// <summary>
        /// Actual health points lost after all mitigation.
        /// </summary>
        public const string DamageTaken =
            "DamageTaken";
        public const string Fish = "Fish";
        public const string Breed = "Breed";
        public const string Milk = "Milk";
        public const string Shear = "Shear";
    }
}
