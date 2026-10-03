using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // Every value is read at the moment it is used (through Sv/Sb/Ss/Si, which also honour the
    // server's copy), nothing is cached, so a change in the file or in ConfigurationManager (F1)
    // takes effect on the next tick - no relog. Descriptions are in English and Russian.
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
        private ConfigEntry<float> _cfgSoloHealth;
        private ConfigEntry<float> _cfgSoloLieutenant;

        // 03 Mechanics
        private ConfigEntry<bool> _cfgWaves;
        private ConfigEntry<bool> _cfgNests;
        private ConfigEntry<bool> _cfgLieutenants;
        private ConfigEntry<bool> _cfgMarks;
        private ConfigEntry<bool> _cfgSpecials;
        private ConfigEntry<bool> _cfgShield;
        private ConfigEntry<bool> _cfgHealers;
        private ConfigEntry<bool> _cfgResist;
        private ConfigEntry<bool> _cfgThreat;
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
        private ConfigEntry<int> _cfgGearMinPlayers;
        private ConfigEntry<float> _cfgValuables;

        // 06 Reset
        private ConfigEntry<bool> _cfgReset;
        private ConfigEntry<float> _cfgResetRadius;
        private ConfigEntry<float> _cfgResetSeconds;

        // 07 Client (never synced: how this client draws things)
        private ConfigEntry<bool> _cfgShowCircles;
        private ConfigEntry<float> _cfgMessageDuration;

        // 09 Raid
        private ConfigEntry<float> _cfgShieldFactor;
        private ConfigEntry<float> _cfgHardShieldFactor;
        private ConfigEntry<float> _cfgWindowSeconds;
        private ConfigEntry<float> _cfgWindowMultiplier;
        private ConfigEntry<float> _cfgHealPercent;
        private ConfigEntry<float> _cfgHealRange;
        private ConfigEntry<float> _cfgLeash;
        private ConfigEntry<float> _cfgSwitchMargin;
        private ConfigEntry<float> _cfgHoldSeconds;
        private ConfigEntry<float> _cfgThreatDecay;
        private ConfigEntry<float> _cfgOutOfLeashHalfLife;
        private ConfigEntry<float> _cfgShieldMax;
        private ConfigEntry<float> _cfgMinDamage;
        private ConfigEntry<int> _cfgConfigVersion;

        private static readonly string[] Profiles = { ProfileLight, ProfileRaid, ProfileHard };
        private static readonly string[] BossProfiles = { ProfileDefault, ProfileLight, ProfileRaid, ProfileHard };
        private static readonly string[] Modes = { ModeMod, ModeVanilla };

        internal static string D(string en, string ru) { return en + "\n" + ru; }

        private ConfigEntry<float> F(string section, string key, float def, float min, float max, string en, string ru)
        {
            return Config.Bind(section, key, def, new ConfigDescription(D(en, ru), new AcceptableValueRange<float>(min, max)));
        }

        private ConfigEntry<int> I(string section, string key, int def, int min, int max, string en, string ru)
        {
            return Config.Bind(section, key, def, new ConfigDescription(D(en, ru), new AcceptableValueRange<int>(min, max)));
        }

        private ConfigEntry<bool> B(string section, string key, bool def, string en, string ru)
        {
            return Config.Bind(section, key, def, D(en, ru));
        }

        private ConfigEntry<string> S(string section, string key, string def, string en, string ru, params string[] allowed)
        {
            return allowed != null && allowed.Length > 0
                ? Config.Bind(section, key, def, new ConfigDescription(D(en, ru), new AcceptableValueList<string>(allowed)))
                : Config.Bind(section, key, def, D(en, ru));
        }

        private void BindConfig()
        {
            const string G = "01 General";
            _cfgEnabled = B(G, "Enabled", true, "Master switch. Off: every boss is vanilla.", "Главный выключатель. Выкл — все боссы ванильные.");
            _cfgDebug = B(G, "Debug", false, "Log phases, spawns, marks, scaling and the self-check details.", "Писать в лог фазы, спавны, метки, масштаб и подробности самопроверки.");
            _cfgProfile = S(G, "Profile", ProfileRaid,
                "Difficulty for every boss whose own Profile is Default. Light: adds, nests and lieutenants only. Raid: everything. Hard: Raid with more adds and stars, faster marks, more HP, stronger shield.",
                "Сложность для всех боссов с Profile = Default. Light — только адды, гнёзда и лейтенанты. Raid — всё. Hard — Raid с большим числом аддов и звёзд, частыми метками, большим HP и сильным щитом.",
                Profiles);
            _cfgAnnounce = S(G, "Announce", "Center",
                "Where phase messages go. Center: middle of the screen for every player, each in their language (players without the mod: see GuestLanguage). Chat: a chat line from the boss owner. Off: nothing.",
                "Куда писать сообщения фаз. Center — в центр экрана всем, каждому на его языке (игрокам без мода — см. GuestLanguage). Chat — строкой в чат от владельца босса. Off — никуда.",
                "Center", "Chat", "Off");
            _cfgConfigVersion = I(G, "ConfigVersion", 0, 0, 1000,
                "Internal: lets a new version of the mod update defaults that changed. Do not edit.",
                "Служебное: позволяет новой версии мода обновить изменившиеся значения по умолчанию. Не менять.");
            _cfgGuestLanguage = S(G, "GuestLanguage", "Russian",
                "Language of messages for players WITHOUT the mod (their game language is unknown to the server). Boss and creature names are still shown in their own language.",
                "Язык сообщений для игроков БЕЗ мода (сервер не знает язык их игры). Имена боссов и мобов у них всё равно на их языке.",
                "Russian", "English", "Both");

            const string Sc = "02 Scaling";
            _cfgScalingRange = F(Sc, "Range", 100f, 20f, 300f,
                "Players within this distance of the boss count as the group (vanilla uses 100 m).",
                "Игроки ближе этого расстояния к боссу считаются группой (в ванили 100 м).");
            _cfgHealthPerPlayer = F(Sc, "HealthPerPlayer", 0.5f, 0f, 2f,
                "Effective boss HP grows by this per player beyond the first. Vanilla gives 0.3 up to 5 players; the mod replaces that with this value up to MaxPlayers (0.3 and 5 = vanilla).",
                "Эффективное HP босса растёт на столько за каждого игрока сверх первого. В ванили 0.3 до 5 игроков; мод заменяет это своим значением до MaxPlayers (0.3 и 5 — как в ванили).");
            _cfgMaxScalingPlayers = I(Sc, "MaxPlayers", 8, 1, 20, "Players counted for scaling at most.", "Сколько игроков максимум учитывается в масштабе.");
            _cfgBaseHealth = F(Sc, "BaseHealthMultiplier", 1f, 0.25f, 5f,
                "Overall multiplier of effective boss HP (for tuning against WeaponArts).", "Общий множитель эффективного HP босса (подстройка под WeaponArts).");
            _cfgHardHealth = F(Sc, "HardHealthMultiplier", 1.25f, 1f, 5f, "Extra effective HP in the Hard profile.", "Доп. множитель HP в профиле Hard.");
            _cfgSoloAdds = F(Sc, "SoloAddsFactor", 0.5f, 0.1f, 2f,
                "Adds multiplier for a single player (rounded, at least 1).", "Множитель аддов для одного игрока (с округлением, не меньше 1).");
            _cfgAddsPerPlayer = F(Sc, "AddsPerPlayer", 0.5f, 0.1f, 2f,
                "Adds multiplier per player in a group: 4 players x 0.5 = x2.", "Множитель аддов на игрока в группе: 4 игрока × 0.5 = ×2.");
            _cfgHardAdds = F(Sc, "HardAddsMultiplier", 1.5f, 1f, 4f, "Extra adds in the Hard profile.", "Доп. множитель аддов в профиле Hard.");
            _cfgLieutenantStarPlayers = I(Sc, "LieutenantStarPlayers", 4, 1, 20,
                "From this many players lieutenants get +1 star.", "С этого числа игроков лейтенанты получают +1 звезду.");
            _cfgAddStarPlayers = I(Sc, "AddStarPlayers", 7, 1, 20,
                "From this many players ordinary adds get +1 star.", "С этого числа игроков обычные адды получают +1 звезду.");
            _cfgSoloHealth = F(Sc, "SoloHealthMultiplier", 0.8f, 0.25f, 2f,
                "Effective boss HP for a single player (x): all the mechanics fall on one person.",
                "Эффективное HP босса для одного игрока (×): все механики ложатся на одного человека.");
            _cfgSoloLieutenant = F(Sc, "SoloLieutenantHealth", 0.6f, 0.1f, 2f,
                "Effective HP of lieutenants (troll, bear, golem, Morgen...) for a single player (x).",
                "Эффективное HP лейтенантов (тролль, медведь, голем, морген…) для одного игрока (×).");

            const string M = "03 Mechanics";
            _cfgWaves = B(M, "Waves", true, "Waves of adds at HP thresholds.", "Волны аддов на порогах HP.");
            _cfgNests = B(M, "Nests", true, "Destructible spawners (vanilla nests and totems).", "Разрушаемые спавнеры (ванильные гнёзда и тотемы).");
            _cfgLieutenants = B(M, "Lieutenants", true, "Big biome creatures as mini-bosses.", "Крупные мобы биома как мини-боссы.");
            _cfgMarks = B(M, "Marks", true, "Marks on players followed by an AoE or roots (spread out). Off in Light.", "Метки на игроках, затем удар или корни (разбегитесь). Выкл в Light.");
            _cfgSpecials = B(M, "Specials", true, "Boss-specific abilities (Eikthyr's charge). Off in Light.", "Особые способности босса (рывок Эйктюра). Выкл в Light.");
            _cfgShield = B(M, "Shield", true, "The boss takes little damage while its nests stand; destroying them opens a burn window. Off in Light.", "Босс почти не получает урона, пока стоят его гнёзда; их разрушение открывает окно уязвимости. Выкл в Light.");
            _cfgHealers = B(M, "Healers", true, "Healer adds (shamans) heal the boss while alive and near; slime that reaches Bonemass heals it. Off in Light.", "Адды-лекари (шаманы) лечат босса, пока живы и рядом; дошедшая до Массивного слизь лечит его. Выкл в Light.");
            _cfgResist = B(M, "Resistances", true, "Phases that change what hurts the boss (Elder's living bark, Bonemass's hardening). Off in Light.", "Фазы смены сопротивлений (живая кора Древнего, затвердевание Массивного). Выкл в Light.");
            _cfgThreat = B(M, "Threat", true, "Threat table: the boss attacks whoever angered it most within the leash radius. Off in Light.", "Таблица угрозы: босс бьёт того, кто разозлил его больше всех в радиусе привязи. Выкл в Light.");
            _cfgCleanupOnDeath = B(M, "CleanupOnDeath", true, "Remove the boss's adds and nests when it dies.", "Убирать аддов и гнёзда босса после его смерти.");
            _cfgCleanupOnDisable = B(M, "CleanupOnDisable", true, "Remove the adds and nests of a fight when its boss is switched to Vanilla.", "Убирать аддов и гнёзда боя, когда босс переключён в Vanilla.");
            _cfgSpawnRadiusMin = F(M, "SpawnRadiusMin", 6f, 2f, 40f, "Adds and nests appear in a ring around the boss from this distance...", "Адды и гнёзда появляются кольцом вокруг босса от этого расстояния…");
            _cfgSpawnRadiusMax = F(M, "SpawnRadiusMax", 14f, 3f, 60f, "...to this distance.", "…до этого.");

            const string Mk = "04 Marks";
            _cfgMarkDelay = F(Mk, "Delay", 3f, 1f, 10f, "Seconds between the mark and the strike.", "Секунд между меткой и ударом.");
            _cfgMarkInterval = F(Mk, "Interval", 20f, 5f, 120f, "Seconds between marks (x0.75 in Hard).", "Секунд между метками (×0.75 в Hard).");
            _cfgMarkDamage = F(Mk, "DamageMultiplier", 1f, 0f, 5f, "Multiplier of every boss's mark damage.", "Множитель урона меток всех боссов.");

            const string R = "05 Rewards";
            _cfgRewards = B(R, "Enabled", true,
                "Extra loot on top of the vanilla drop: coins and gems (more with a bigger group), about a stack of each material of the biome, about half a stack of each material of the next biome, upgraded gear of the next biome for a group.",
                "Доп. добыча поверх ванильного дропа: монеты и камни (больше для большей группы), около стака каждого материала биома, около половины стака каждого материала следующего биома, улучшенная экипировка следующего биома для группы.");
            _cfgQualityMin = I(R, "QualityMin", 2, 1, 4, "Gear drops already upgraded to at least this level...", "Экипировка выпадает уже улучшенной минимум до этого уровня…");
            _cfgQualityMax = I(R, "QualityMax", 3, 1, 4, "...and at most this level (capped by the item's own max).", "…и максимум до этого (не выше предела самого предмета).");
            _cfgGearMinPlayers = I(R, "GearMinPlayers", 2, 1, 20,
                "Gear (of the next biome) drops only for a group of at least this many players.",
                "Экипировка (следующего биома) выпадает только группе не меньше стольких игроков.");
            _cfgPlayersPerItem = F(R, "PlayersPerItem", 2.5f, 1f, 10f,
                "One piece of gear per this many players, at least one (2-4 players = 1, 5-7 = 2, 8 = 3).", "Одна вещь на столько игроков, не меньше одной (2–4 игрока — 1, 5–7 — 2, 8 — 3).");
            _cfgValuables = F(R, "ValuablesMultiplier", 1f, 0f, 10f, "Multiplier of every reward amount (coins, gems, materials).", "Множитель количества всей награды (монеты, камни, материалы).");

            const string Rs = "06 Reset";
            _cfgReset = B(Rs, "Enabled", false,
                "Reset the fight (full HP, phases, adds and nests removed) when no living player is near the boss for a while.",
                "Сбрасывать бой (полное HP, фазы, адды и гнёзда убираются), если рядом с боссом долго нет живых игроков.");
            _cfgResetRadius = F(Rs, "Radius", 50f, 10f, 200f, "Players within this distance keep the fight going.", "Игроки ближе этого расстояния удерживают бой.");
            _cfgResetSeconds = F(Rs, "Seconds", 60f, 10f, 600f, "Seconds without players before the reset.", "Секунд без игроков до сброса.");

            _cfgMessageDuration = F("07 Client", "MessageDurationMultiplier", 1.5f, 0.5f, 4f,
                "How long the mod's centre messages stay on screen, x vanilla (4 s). Only on this client; players without the mod see the vanilla length.",
                "Сколько сообщения мода держатся в центре экрана, × от ванильных 4 с. Только на этом клиенте; у игроков без мода — ванильная длительность.");
            _cfgShowCircles = B("07 Client", "ShowMarkCircles", true,
                "Draw the exact AoE radius of a mark on the ground (only players with the mod see it; not synced).",
                "Рисовать на земле точный радиус удара метки (видят только игроки с модом; не синкается).");

            const string Rd = "09 Raid";
            _cfgShieldFactor = F(Rd, "ShieldDamageFactor", 0.2f, 0f, 1f, "Damage the boss takes while shielded (x).", "Урон по боссу под щитом (×).");
            _cfgHardShieldFactor = F(Rd, "HardShieldDamageFactor", 0.1f, 0f, 1f, "Same in the Hard profile.", "То же в профиле Hard.");
            _cfgWindowSeconds = F(Rd, "WindowSeconds", 10f, 0f, 60f, "Burn window after the shield falls, seconds (the boss is staggered).", "Окно уязвимости после падения щита, секунд (босс оглушён).");
            _cfgWindowMultiplier = F(Rd, "WindowDamageMultiplier", 1.5f, 1f, 5f, "Damage the boss takes during the window (x).", "Урон по боссу в окне уязвимости (×).");
            _cfgHealPercent = F(Rd, "HealPercentPerSecond", 0.3f, 0f, 10f, "Boss max HP healed per second by each living healer (%, at most 2 healers count).", "Сколько % макс. HP босса в секунду лечит каждый живой лекарь (учитываются не больше 2).");
            _cfgHealRange = F(Rd, "HealRange", 40f, 5f, 150f, "Healers farther than this from the boss do not heal it.", "Лекари дальше этого от босса его не лечат.");
            _cfgLeash = F(Rd, "ThreatLeash", 25f, 5f, 100f,
                "Threat: only players within this distance of the boss can hold it; one who runs out loses the boss to the nearest.",
                "Угроза: удерживать босса могут только игроки ближе этого; убежавший теряет босса, тот переключается на ближайшего.");
            _cfgSwitchMargin = F(Rd, "ThreatSwitchMargin", 0.1f, 0f, 1f, "The boss switches only to a threat this much higher (0.1 = 110%).", "Босс переключается, только если угроза выше на столько (0.1 = 110 %).");
            _cfgHoldSeconds = F(Rd, "ThreatHoldSeconds", 3.5f, 0f, 15f, "Minimum seconds on a new target.", "Минимум секунд на новой цели.");
            _cfgThreatDecay = F(Rd, "ThreatDecayPerSecond", 0.05f, 0f, 1f, "Share of threat forgotten per second.", "Доля угрозы, которая забывается за секунду.");
            _cfgOutOfLeashHalfLife = F(Rd, "ThreatOutOfLeashHalfLife", 3f, 0.5f, 30f, "Outside the leash threat halves every this many seconds.", "Вне радиуса привязи угроза вдвое падает за столько секунд.");
            _cfgShieldMax = F(Rd, "ShieldMaxSeconds", 90f, 0f, 600f,
                "A shield falls by itself after this many seconds even if nests still stand (no burn window then). 0 = never.",
                "Щит спадает сам через столько секунд, даже если гнёзда стоят (окна уязвимости тогда нет). 0 — никогда.");
            _cfgMinDamage = F(Rd, "MinDamageFactor", 0.1f, 0f, 1f,
                "All the mechanics together (shield, cocoon, resist and cycle phases) never cut damage to the boss below this (x). Cycle phases also never start under a shield or cocoon.",
                "Все механики вместе (щит, кокон, сопротивления и фазы-циклы) никогда не режут урон по боссу ниже этого (×). Фазы-циклы к тому же не начинаются под щитом или коконом.");
        }

        // ------------------------------------------------------------------
        // per boss: section "1x <Boss>"
        // ------------------------------------------------------------------
        private void BindBossConfig()
        {
            for (int i = 0; i < _bosses.Count; i++)
            {
                BossDef b = _bosses[i];
                b.CfgMode = S(b.Section, "Mode", ModeMod,
                    "Mod: extended fight. Vanilla: the mod leaves this boss alone. Takes effect immediately, mid-fight too.",
                    "Mod — расширенный бой. Vanilla — мод не трогает этого босса. Действует сразу, в том числе посреди боя.",
                    Modes);
                b.CfgProfile = S(b.Section, "Profile", ProfileDefault,
                    "Default: the global profile from 01 General.", "Default — общий профиль из 01 General.", BossProfiles);
                if (b.BindExtra != null) b.BindExtra(this, b);
            }
        }

        // ------------------------------------------------------------------
        // migration: defaults that changed are reset once in an existing file (other values kept)
        // ------------------------------------------------------------------
        private const int CurrentConfigVersion = 3;

        private void MigrateConfig()
        {
            int v = _cfgConfigVersion.Value;
            if (v >= CurrentConfigVersion) return;
            List<string> reset = new List<string>();
            if (v < 2)
            {
                // 0.8.3: after the first test - softer heals, a weaker Eikthyr lightning
                ResetToDefault(_cfgHealPercent, reset);
                BossDef eik = BossByPrefab("Eikthyr");
                if (eik != null) ResetToDefault(eik.CfgMarkDamage, reset);
                BossDef yag = BossByPrefab("GoblinKing");
                if (yag != null) ResetToDefault(yag.CfgRegen, reset);
            }
            if (v < 3)
            {
                // 0.8.8: Bonemass's slime comes less often
                BossDef bm = BossByPrefab("Bonemass");
                if (bm != null) ResetToDefault(bm.CfgFusionInterval, reset);
            }
            _cfgConfigVersion.Value = CurrentConfigVersion;
            if (reset.Count > 0) Logger.LogInfo("Config updated to version " + CurrentConfigVersion + ", new defaults: " + string.Join(", ", reset.ToArray()));
        }

        private static void ResetToDefault(ConfigEntryBase e, List<string> log)
        {
            if (e == null || Equals(e.BoxedValue, e.DefaultValue)) return;
            e.BoxedValue = e.DefaultValue;
            log.Add(e.Definition.Section + "/" + e.Definition.Key);
        }

        private BossDef BossByPrefab(string prefab)
        {
            for (int i = 0; i < _bosses.Count; i++) if (_bosses[i].Prefab == prefab) return _bosses[i];
            return null;
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
            bool full = ProfileOf(b) != ProfileLight;
            switch (kind)
            {
                case ActKind.Wave: return Sb(_cfgWaves);
                case ActKind.Lieutenant: return Sb(_cfgLieutenants);
                case ActKind.Nest:
                case ActKind.Totem: return Sb(_cfgNests);
                case ActKind.Marks: return Sb(_cfgMarks) && full;
                case ActKind.Charge: return Sb(_cfgSpecials) && full;
                case ActKind.Shield: return Sb(_cfgShield) && Sb(_cfgNests) && full;
                case ActKind.Resist:
                case ActKind.Cycle: return Sb(_cfgResist) && full;
                case ActKind.Fusion: return Sb(_cfgHealers) && full;
                case ActKind.Seeds: return Sb(_cfgNests);
                case ActKind.HitEffect: return Sb(_cfgSpecials) && full;
                case ActKind.Fixate:
                case ActKind.Hazard: return Sb(_cfgMarks) && full;
                case ActKind.Cocoon: return Sb(_cfgShield) && full;
            }
            return false;
        }

        internal bool HealersOn(BossDef b) { return Sb(_cfgHealers) && ProfileOf(b) != ProfileLight; }
        internal bool ThreatOn(BossDef b) { return Sb(_cfgThreat) && ProfileOf(b) != ProfileLight; }
    }
}
