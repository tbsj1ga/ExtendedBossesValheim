using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // A fight is data: phases at HP thresholds, each a list of actions (the "bricks" of
    // ROADMAP.md, section A). The fight controller knows how to run each kind of action; a boss
    // is only a table here.
    internal enum ActKind { Wave, Lieutenant, Nest, Totem, Marks, Charge, Shield, Resist, Bark }

    internal class Act
    {
        public ActKind Kind;
        public string[] Prefabs;        // creatures (Wave, Lieutenant, Totem spawns), the nest (Nest), the AoE (Marks)
        public float Count = 1f;        // Wave/Lieutenant: base count of each prefab; Nest/Totem: objects; Marks: players per cast
        public int ExtraFrom;           // Lieutenant: one more from this many players (0 = never)
        public int Level = 1;           // creature level, 1 = no stars
        public float HpMul = 1f;        // effective HP multiplier (damage divisor on the owner)
        public int Role;                // RoleHealer: heals the boss while alive
        public float Lifetime;          // creatures: removed after this many seconds (0 = stay)
        public string Prop;             // Totem: the destructible prop; Marks: the telegraph effect
        public float Interval = 12f;    // Totem: seconds between spawns
        public int MaxAlive = 3;        // Totem: adds alive per totem
        public float Damage;            // Marks: damage of the strike
        public HitData.DamageType DamageType = HitData.DamageType.Lightning;
        public string MarkKey;          // Marks: text key announced per mark ({0} boss, {1} player)
        public string Creature;         // Marks: creature summoned around the mark instead of an AoE (roots)
        public int CreatureCount = 4;   // Marks: how many, in a ring
        public float RingRadius = 3f;   // Marks: ring radius around the marked player
        public float Duration;          // Resist: seconds (0 = while the shield holds); Bark: seconds it lasts
        public Dictionary<HitData.DamageType, float> Mods; // Resist, Bark (Sap): damage multipliers by type; missing types use Other
        public float Other = 1f;        // Bark (Sap): multiplier of every type not in Mods; Bark (Back): multiplier from the front
        public float CooldownMin;       // Bark: seconds between barks, random in [min, max]
        public float CooldownMax;
        public float Delay;             // Bark: first bark this long after the phase if no burn window came first
    }

    internal class PhaseDef
    {
        public float Pct;               // fires once when HP% drops to this or below
        public string Say;              // text key announced ({0} = boss name), "" = none
        public List<Act> Acts = new List<Act>();
    }

    internal class Loot
    {
        public string Prefab;
        public int Min;
        public int Max;
        public bool PerPlayer;          // amount scales with the group
        public Loot(string prefab, int min, int max, bool perPlayer) { Prefab = prefab; Min = min; Max = max; PerPlayer = perPlayer; }
    }

    internal class RewardDef
    {
        public List<Loot> Valuables = new List<Loot>();     // every kill
        public List<Loot> NextBiome = new List<Loot>();     // each with NextBiomeChance
        public string[] Gear = new string[0];               // this biome
        public string[] NextGear = new string[0];           // next biome, with NextBiomeChance per piece
    }

    internal class BossDef
    {
        public string Prefab;
        public int Hash;
        public string Section;          // config section
        public List<PhaseDef> Phases = new List<PhaseDef>();
        public RewardDef Reward = new RewardDef();

        public ConfigEntry<string> CfgMode;
        public ConfigEntry<string> CfgProfile;
        public Action<ExtendedBossesPlugin, BossDef> BindExtra;

        // optional per-boss knobs (null when the boss does not use them)
        public ConfigEntry<string> CfgTotemPrefab;
        public ConfigEntry<string> CfgNestPrefab;
        public ConfigEntry<string> CfgMarkPrefab;
        public ConfigEntry<string> CfgMarkEffect;
        public ConfigEntry<string> CfgMarkCreature;
        public ConfigEntry<float> CfgMarkDamage;
        public ConfigEntry<float> CfgChargeInterval;
        public ConfigEntry<float> CfgChargeSpeed;
        public ConfigEntry<float> CfgChargeDuration;
        public ConfigEntry<string> CfgBarkVariant;
        public ConfigEntry<float> CfgBarkDuration;
        public ConfigEntry<float> CfgBarkCdMin;
        public ConfigEntry<float> CfgBarkCdMax;
        public ConfigEntry<float> CfgBarkDelay;
        public ConfigEntry<float> CfgBackArc;
    }

    public partial class ExtendedBossesPlugin
    {
        internal const int RoleHealer = 1;
        internal const string BarkSap = "Sap";
        internal const string BarkBack = "Back";
        internal const string BarkAuto = "Auto";
        internal const string BarkRandom = "Random";

        private readonly List<BossDef> _bosses = new List<BossDef>();
        private readonly Dictionary<int, BossDef> _bossByHash = new Dictionary<int, BossDef>();

        internal BossDef BossOf(ZDO zdo)
        {
            BossDef b;
            return zdo != null && _bossByHash.TryGetValue(zdo.GetPrefab(), out b) ? b : null;
        }

        private void AddBoss(BossDef b)
        {
            b.Hash = b.Prefab.GetStableHashCode();
            _bosses.Add(b);
            _bossByHash[b.Hash] = b;
        }

        private void BuildBosses()
        {
            AddBoss(Eikthyr());
            AddBoss(Elder());
        }

        private static PhaseDef Phase(BossDef b, float pct, string say)
        {
            PhaseDef p = new PhaseDef { Pct = pct, Say = say };
            b.Phases.Add(p);
            return p;
        }

        // ------------------------------------------------------------------
        // 1. Eikthyr - Meadows, the "tutorial raid": every mechanic once, gently
        // ------------------------------------------------------------------
        private static BossDef Eikthyr()
        {
            BossDef b = new BossDef { Prefab = "Eikthyr", Section = "10 Eikthyr" };

            Phase(b, 80f, "eikthyr.80").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Boar", "Neck" }, Count = 2f });
            Phase(b, 60f, "eikthyr.60").Acts.Add(new Act { Kind = ActKind.Charge });
            Phase(b, 50f, "eikthyr.50").Acts.Add(new Act
            {
                Kind = ActKind.Totem, Prop = "skull_pile", Count = 1f,
                Prefabs = new[] { "Skeleton_Meadows_noarcher", "Skeleton_Meadows_noarcher", "Skeleton_Meadows" },
                Interval = 12f, MaxAlive = 3
            });
            Phase(b, 30f, "eikthyr.30").Acts.Add(new Act
            {
                Kind = ActKind.Marks, Prefabs = new[] { "lightningAOE" }, Prop = "vfx_prespawn", MarkKey = "eikthyr.mark",
                Count = 1f, Damage = 30f, DamageType = HitData.DamageType.Lightning
            });
            Phase(b, 15f, "eikthyr.15").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Boar" }, Level = 3, HpMul = 3f });

            b.Reward.Valuables.Add(new Loot("Coins", 20, 30, true));
            b.Reward.Valuables.Add(new Loot("Amber", 1, 1, true));
            b.Reward.NextBiome.Add(new Loot("CopperOre", 2, 4, false));
            b.Reward.NextBiome.Add(new Loot("TinOre", 2, 4, false));
            b.Reward.Gear = new[] { "AxeFlint", "SpearFlint", "KnifeFlint", "Club", "Bow", "ShieldWood",
                                    "ArmorLeatherChest", "ArmorLeatherLegs", "HelmetLeather", "CapeDeerHide" };
            b.Reward.NextGear = new[] { "AxeBronze", "MaceBronze", "SwordBronze", "SpearBronze", "AtgeirBronze",
                                        "ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze", "ShieldBronzeBuckler" };

            b.BindExtra = delegate(ExtendedBossesPlugin pl, BossDef d)
            {
                d.CfgTotemPrefab = pl.S(d.Section, "TotemPrefab", "skull_pile",
                    "Destructible vanilla object used as the skeleton totem at 50%. Must be networked and destructible (see the self-check in the log). Fallback: BonePileSpawner.",
                    "Разрушаемый ванильный объект — тотем со скелетами на 50 %. Должен быть сетевым и разрушаемым (см. самопроверку в логе). Запасной — BonePileSpawner.");
                d.CfgMarkPrefab = pl.S(d.Section, "MarkPrefab", "lightningAOE",
                    "Vanilla AoE prefab of the lightning strike at 30%.", "Ванильный AoE-префаб удара молнии на 30 %.");
                d.CfgMarkEffect = pl.S(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect on the marked player before the strike (players with the mod).", "Ванильный эффект на отмеченном игроке до удара (у игроков с модом).");
                d.CfgMarkDamage = pl.F(d.Section, "MarkDamage", 30f, 0f, 500f,
                    "Lightning damage of the strike (before 04 Marks DamageMultiplier).", "Урон молнией (до множителя из 04 Marks).");
                d.CfgChargeInterval = pl.F(d.Section, "ChargeInterval", 20f, 5f, 120f, "Seconds between charges from 60%.", "Секунд между рывками с 60 %.");
                d.CfgChargeSpeed = pl.F(d.Section, "ChargeSpeed", 2f, 1f, 4f, "Speed multiplier during a charge.", "Множитель скорости во время рывка.");
                d.CfgChargeDuration = pl.F(d.Section, "ChargeDuration", 2.5f, 0.5f, 8f, "Seconds a charge lasts.", "Длительность рывка, секунд.");
            };
            return b;
        }

        // ------------------------------------------------------------------
        // 2. The Elder - Black Forest: nests first, then the heavy creatures of the forest
        // ------------------------------------------------------------------
        private static BossDef Elder()
        {
            BossDef b = new BossDef { Prefab = "gd_king", Section = "11 Elder" };
            const string nest = "Spawner_GreydwarfNest";

            Phase(b, 85f, "elder.85").Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { nest }, Count = 1f });

            PhaseDef p = Phase(b, 70f, "elder.70");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { nest }, Count = 2f });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Marks, Creature = "TentaRoot", CreatureCount = 4, RingRadius = 3f, Lifetime = 15f,
                Prop = "vfx_prespawn", MarkKey = "elder.mark", Count = 1f
            });

            p = Phase(b, 55f, "elder.55");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { nest }, Count = 3f });
            p.Acts.Add(new Act { Kind = ActKind.Shield });
            // Living bark: after the burn window ends (or 60 s after this phase if the window never
            // came), 30 s of bark, then 50-70 s cooldown, repeat. Sap: fire x0.25 (its usual
            // weakness is gone), slash x1.25 (chop the tree), everything else x0.25. Back: from
            // the front x0.25, from behind full damage.
            p.Acts.Add(new Act
            {
                Kind = ActKind.Bark, Duration = 30f, CooldownMin = 50f, CooldownMax = 70f, Delay = 60f, Other = 0.25f,
                Mods = new Dictionary<HitData.DamageType, float> { { HitData.DamageType.Slash, 1.25f }, { HitData.DamageType.Fire, 0.25f } }
            });

            Phase(b, 45f, "elder.45").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Troll" }, ExtraFrom = 3 });
            Phase(b, 30f, "elder.30").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Bjorn" } });

            p = Phase(b, 20f, "elder.20");
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Greydwarf_Elite" }, Count = 2f });
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Greydwarf_Shaman" }, Count = 1f, Role = RoleHealer });

            Phase(b, 10f, "elder.10").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Skeleton", "Ghost" }, Count = 1f });

            b.Reward.Valuables.Add(new Loot("Coins", 40, 60, true));
            b.Reward.Valuables.Add(new Loot("Amber", 1, 2, true));
            b.Reward.Valuables.Add(new Loot("Ruby", 1, 1, false));
            b.Reward.Valuables.Add(new Loot("CopperOre", 4, 8, false));
            b.Reward.Valuables.Add(new Loot("TinOre", 4, 8, false));
            b.Reward.NextBiome.Add(new Loot("IronScrap", 2, 4, false));
            b.Reward.Gear = new[] { "AxeBronze", "MaceBronze", "SwordBronze", "SpearBronze", "AtgeirBronze", "KnifeCopper", "BowFineWood",
                                    "ArmorBronzeChest", "ArmorBronzeLegs", "HelmetBronze", "ShieldBronzeBuckler",
                                    "ArmorTrollLeatherChest", "ArmorTrollLeatherLegs", "HelmetTrollLeather", "CapeTrollHide" };
            b.Reward.NextGear = new[] { "SwordIron", "MaceIron", "AxeIron", "SpearElderbark", "AtgeirIron",
                                        "ArmorIronChest", "ArmorIronLegs", "HelmetIron", "ShieldBanded" };

            b.BindExtra = delegate(ExtendedBossesPlugin pl, BossDef d)
            {
                d.CfgNestPrefab = pl.S(d.Section, "NestPrefab", nest,
                    "Vanilla destructible spawner placed at 85/70/55%.", "Ванильный разрушаемый спавнер на 85/70/55 %.");
                d.CfgMarkCreature = pl.S(d.Section, "RootsCreature", "TentaRoot",
                    "Creature summoned in a ring around the marked player from 70%.", "Существо, которое появляется кольцом вокруг отмеченного игрока с 70 %.");
                d.CfgMarkEffect = pl.S(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect on the marked player before the roots (players with the mod).", "Ванильный эффект на отмеченном игроке до корней (у игроков с модом).");
                d.CfgBarkVariant = pl.S(d.Section, "BarkVariant", BarkRandom,
                    "Living bark from 55%. Random: Sap or Back, chosen anew for every bark. Sap: fire x0.25, slash x1.25, the rest x0.25 - chop with axes. Back: from the front x0.25, full damage only from behind - the tank holds the Elder facing them. Auto: Back for a group of 3+, Sap otherwise.",
                    "Живая кора с 55 %. Random — Sap или Back, заново для каждой коры. Sap — огонь ×0.25, рубящий ×1.25, остальное ×0.25: рубите топорами. Back — спереди ×0.25, полный урон только в спину: танк держит Древнего лицом к себе. Auto — Back для группы от 3 игроков, иначе Sap.",
                    BarkRandom, BarkSap, BarkBack, BarkAuto);
                d.CfgBarkDuration = pl.F(d.Section, "BarkDuration", 30f, 5f, 120f, "Seconds the bark lasts.", "Длительность коры, секунд.");
                d.CfgBarkCdMin = pl.F(d.Section, "BarkCooldownMin", 50f, 5f, 300f, "Cooldown between barks: from...", "КД между корами: от…");
                d.CfgBarkCdMax = pl.F(d.Section, "BarkCooldownMax", 70f, 5f, 300f, "...to (random each time).", "…до (случайно каждый раз).");
                d.CfgBarkDelay = pl.F(d.Section, "BarkStartDelay", 60f, 0f, 300f,
                    "The first bark comes when the stagger after the fallen shield ends; if the nests still stand this long after 55%, it starts anyway.",
                    "Первая кора — когда кончается оглушение после падения щита; если гнёзда стоят дольше этого после 55 %, кора начинается всё равно.");
                d.CfgBackArc = pl.F(d.Section, "BackArc", 120f, 30f, 270f,
                    "Back variant: width of the arc behind the Elder that counts as 'the back', degrees.", "Вариант Back: ширина дуги позади Древнего, которая считается спиной, градусов.");
            };
            return b;
        }
    }
}
