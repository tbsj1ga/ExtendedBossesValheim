using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // Every value is read at the moment it is used (through Sv/Sb/Ss/Si, which also honour the
    // server's copy), nothing is cached, so a change in the file or in ConfigurationManager (F11)
    // takes effect on the next tick - no relog.
    public partial class ExtendedBossesPlugin
    {
        internal const string ProfileLight = "Light";
        internal const string ProfileRaid = "Raid";
        internal const string ProfileHard = "Hard";
        internal const string ProfileDefault = "Default";
        internal const string ModeMod = "Mod";
        internal const string ModeVanilla = "Vanilla";

        // 01 General
        private ConfigEntry<bool> _cfgEnabled;
        private ConfigEntry<bool> _cfgDebug;
        private ConfigEntry<string> _cfgProfile;
        private ConfigEntry<string> _cfgAnnounce;

        // 02 Scaling
        private ConfigEntry<float> _cfgScalingRange;
        private ConfigEntry<float> _cfgHealthPerPlayer;
        private ConfigEntry<int> _cfgMaxScalingPlayers;
        private ConfigEntry<float> _cfgBaseHealth;
        private ConfigEntry<float> _cfgHardHealth;
        private ConfigEntry<float> _cfgSoloAdds;
        private ConfigEntry<float> _cfgAddsPerPlayer;
        private ConfigEntry<float> _cfgHardAdds;
        private ConfigEntry<int> _cfgLieutenantStarPlayers;
        private ConfigEntry<int> _cfgAddStarPlayers;

        // 03 Mechanics
        private ConfigEntry<bool> _cfgWaves;
        private ConfigEntry<bool> _cfgNests;
        private ConfigEntry<bool> _cfgLieutenants;
        private ConfigEntry<bool> _cfgMarks;
        private ConfigEntry<bool> _cfgSpecials;
        private ConfigEntry<bool> _cfgCleanupOnDeath;
        private ConfigEntry<bool> _cfgCleanupOnDisable;
        private ConfigEntry<float> _cfgSpawnRadiusMin;
        private ConfigEntry<float> _cfgSpawnRadiusMax;

        // 04 Marks
        private ConfigEntry<float> _cfgMarkDelay;
        private ConfigEntry<float> _cfgMarkInterval;
        private ConfigEntry<float> _cfgMarkDamage;

        // 05 Rewards
        private ConfigEntry<bool> _cfgRewards;
        private ConfigEntry<int> _cfgQualityMin;
        private ConfigEntry<int> _cfgQualityMax;
        private ConfigEntry<float> _cfgPlayersPerItem;
        private ConfigEntry<float> _cfgNextBiomeChance;
        private ConfigEntry<float> _cfgValuables;

        // 06 Reset
        private ConfigEntry<bool> _cfgReset;
        private ConfigEntry<float> _cfgResetRadius;
        private ConfigEntry<float> _cfgResetSeconds;

        // 07 Client (never synced: how this client draws things)
        private ConfigEntry<bool> _cfgShowCircles;

        private static readonly string[] Profiles = { ProfileLight, ProfileRaid, ProfileHard };
        private static readonly string[] BossProfiles = { ProfileDefault, ProfileLight, ProfileRaid, ProfileHard };
        private static readonly string[] Modes = { ModeMod, ModeVanilla };

        private void BindConfig()
        {
            _cfgEnabled = Config.Bind("01 General", "Enabled", true, "Master switch. Off: every boss is vanilla.");
            _cfgDebug = Config.Bind("01 General", "Debug", false, "Log phases, spawns, marks and scaling.");
            _cfgProfile = Config.Bind("01 General", "Profile", ProfileRaid,
                new ConfigDescription("Difficulty profile for every boss whose own Profile is Default. Light: adds, nests and lieutenants only. Raid: everything. Hard: Raid with more adds and stars, faster marks, more HP.",
                    new AcceptableValueList<string>(Profiles)));
            _cfgAnnounce = Config.Bind("01 General", "Announce", "Center",
                new ConfigDescription("Where phase messages go. Center: middle of the screen for every player (vanilla ShowMessage, players without the mod see it). Chat: a chat line from the owner of the boss. Off: nothing.",
                    new AcceptableValueList<string>("Center", "Chat", "Off")));

            _cfgScalingRange = Config.Bind("02 Scaling", "Range", 100f,
                new ConfigDescription("Players within this distance of the boss count as the group (vanilla uses 100 m).", new AcceptableValueRange<float>(20f, 300f)));
            _cfgHealthPerPlayer = Config.Bind("02 Scaling", "HealthPerPlayer", 0.5f,
                new ConfigDescription("Effective boss HP grows by this per player beyond the first. Vanilla already gives 0.3 up to 5 players; the mod replaces that with this value up to MaxPlayers (0.3 and 5 = vanilla).", new AcceptableValueRange<float>(0f, 2f)));
            _cfgMaxScalingPlayers = Config.Bind("02 Scaling", "MaxPlayers", 8,
                new ConfigDescription("Players counted for scaling at most.", new AcceptableValueRange<int>(1, 20)));
            _cfgBaseHealth = Config.Bind("02 Scaling", "BaseHealthMultiplier", 1f,
                new ConfigDescription("Overall multiplier of effective boss HP (for tuning against WeaponArts).", new AcceptableValueRange<float>(0.25f, 5f)));
            _cfgHardHealth = Config.Bind("02 Scaling", "HardHealthMultiplier", 1.25f,
                new ConfigDescription("Extra effective HP in the Hard profile.", new AcceptableValueRange<float>(1f, 5f)));
            _cfgSoloAdds = Config.Bind("02 Scaling", "SoloAddsFactor", 0.5f,
                new ConfigDescription("Adds multiplier for a single player (rounded, at least 1).", new AcceptableValueRange<float>(0.1f, 2f)));
            _cfgAddsPerPlayer = Config.Bind("02 Scaling", "AddsPerPlayer", 0.5f,
                new ConfigDescription("Adds multiplier per player in a group: 4 players x 0.5 = x2.", new AcceptableValueRange<float>(0.1f, 2f)));
            _cfgHardAdds = Config.Bind("02 Scaling", "HardAddsMultiplier", 1.5f,
                new ConfigDescription("Extra adds in the Hard profile.", new AcceptableValueRange<float>(1f, 4f)));
            _cfgLieutenantStarPlayers = Config.Bind("02 Scaling", "LieutenantStarPlayers", 4,
                new ConfigDescription("From this many players lieutenants get +1 star.", new AcceptableValueRange<int>(1, 20)));
            _cfgAddStarPlayers = Config.Bind("02 Scaling", "AddStarPlayers", 7,
                new ConfigDescription("From this many players ordinary adds get +1 star.", new AcceptableValueRange<int>(1, 20)));

            _cfgWaves = Config.Bind("03 Mechanics", "Waves", true, "Waves of adds at HP thresholds.");
            _cfgNests = Config.Bind("03 Mechanics", "Nests", true, "Destructible spawners (vanilla nests and totems).");
            _cfgLieutenants = Config.Bind("03 Mechanics", "Lieutenants", true, "Big biome creatures as mini-bosses.");
            _cfgMarks = Config.Bind("03 Mechanics", "Marks", true, "Marks on players followed by an AoE (spread out). Off in the Light profile.");
            _cfgSpecials = Config.Bind("03 Mechanics", "Specials", true, "Boss-specific abilities (Eikthyr's charge). Off in the Light profile.");
            _cfgCleanupOnDeath = Config.Bind("03 Mechanics", "CleanupOnDeath", true, "Remove the boss's adds and nests when it dies.");
            _cfgCleanupOnDisable = Config.Bind("03 Mechanics", "CleanupOnDisable", true, "Remove the adds and nests of a fight when its boss is switched to Vanilla.");
            _cfgSpawnRadiusMin = Config.Bind("03 Mechanics", "SpawnRadiusMin", 6f,
                new ConfigDescription("Adds and nests appear in a ring around the boss from this distance...", new AcceptableValueRange<float>(2f, 40f)));
            _cfgSpawnRadiusMax = Config.Bind("03 Mechanics", "SpawnRadiusMax", 14f,
                new ConfigDescription("...to this distance.", new AcceptableValueRange<float>(3f, 60f)));

            _cfgMarkDelay = Config.Bind("04 Marks", "Delay", 3f,
                new ConfigDescription("Seconds between the mark and the strike.", new AcceptableValueRange<float>(1f, 10f)));
            _cfgMarkInterval = Config.Bind("04 Marks", "Interval", 20f,
                new ConfigDescription("Seconds between marks (x0.75 in the Hard profile).", new AcceptableValueRange<float>(5f, 120f)));
            _cfgMarkDamage = Config.Bind("04 Marks", "DamageMultiplier", 1f,
                new ConfigDescription("Multiplier of every boss's mark damage.", new AcceptableValueRange<float>(0f, 5f)));

            _cfgRewards = Config.Bind("05 Rewards", "Enabled", true, "Extra loot on top of the vanilla drop: valuables of the biome and upgraded gear, more with a bigger group.");
            _cfgQualityMin = Config.Bind("05 Rewards", "QualityMin", 2,
                new ConfigDescription("Gear drops already upgraded to at least this level...", new AcceptableValueRange<int>(1, 4)));
            _cfgQualityMax = Config.Bind("05 Rewards", "QualityMax", 3,
                new ConfigDescription("...and at most this level (capped by the item's own max).", new AcceptableValueRange<int>(1, 4)));
            _cfgPlayersPerItem = Config.Bind("05 Rewards", "PlayersPerItem", 2.5f,
                new ConfigDescription("One piece of gear per this many players (3 players = 1, 5 = 2, 8 = 3).", new AcceptableValueRange<float>(1f, 10f)));
            _cfgNextBiomeChance = Config.Bind("05 Rewards", "NextBiomeChance", 0.15f,
                new ConfigDescription("Chance per piece of gear to be from the next biome; also the chance of each next-biome resource.", new AcceptableValueRange<float>(0f, 1f)));
            _cfgValuables = Config.Bind("05 Rewards", "ValuablesMultiplier", 1f,
                new ConfigDescription("Multiplier of coins, gems and ore.", new AcceptableValueRange<float>(0f, 10f)));

            _cfgReset = Config.Bind("06 Reset", "Enabled", false, "Reset the fight (full HP, phases, adds and nests removed) when no living player is near the boss for a while.");
            _cfgResetRadius = Config.Bind("06 Reset", "Radius", 50f,
                new ConfigDescription("Players within this distance keep the fight going.", new AcceptableValueRange<float>(10f, 200f)));
            _cfgResetSeconds = Config.Bind("06 Reset", "Seconds", 60f,
                new ConfigDescription("Seconds without players before the reset.", new AcceptableValueRange<float>(10f, 600f)));

            _cfgShowCircles = Config.Bind("07 Client", "ShowMarkCircles", true, "Draw the exact AoE radius of a mark on the ground (only players with the mod see it; not synced).");
        }

        // ------------------------------------------------------------------
        // per boss: section "1x <Boss>"
        // ------------------------------------------------------------------
        private void BindBossConfig()
        {
            for (int i = 0; i < _bosses.Count; i++)
            {
                BossDef b = _bosses[i];
                b.CfgMode = Config.Bind(b.Section, "Mode", ModeMod,
                    new ConfigDescription("Mod: extended fight. Vanilla: the mod leaves this boss alone. Takes effect immediately, mid-fight too.",
                        new AcceptableValueList<string>(Modes)));
                b.CfgProfile = Config.Bind(b.Section, "Profile", ProfileDefault,
                    new ConfigDescription("Default: the global profile from 01 General.", new AcceptableValueList<string>(BossProfiles)));
                if (b.BindExtra != null) b.BindExtra(this, b);
            }
        }

        internal bool IsModMode(BossDef b)
        {
            return b != null && Ss(b.CfgMode) == ModeMod;
        }

        internal string ProfileOf(BossDef b)
        {
            string p = b != null ? Ss(b.CfgProfile) : ProfileDefault;
            if (p == ProfileDefault || Array.IndexOf(Profiles, p) < 0) p = Ss(_cfgProfile);
            return Array.IndexOf(Profiles, p) < 0 ? ProfileRaid : p;
        }

        // Mechanic toggles combined with the profile: Light keeps only adds, nests and lieutenants.
        internal bool MechanicOn(BossDef b, ActKind kind)
        {
            string prof = ProfileOf(b);
            switch (kind)
            {
                case ActKind.Wave: return Sb(_cfgWaves);
                case ActKind.Lieutenant: return Sb(_cfgLieutenants);
                case ActKind.Nest:
                case ActKind.Totem: return Sb(_cfgNests);
                case ActKind.Marks: return Sb(_cfgMarks) && prof != ProfileLight;
                case ActKind.Charge: return Sb(_cfgSpecials) && prof != ProfileLight;
            }
            return false;
        }
    }
}
