using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // A fight is data: phases at HP thresholds, each a list of actions (the "bricks" of
    // ROADMAP.md, section A). The fight controller knows how to run each kind of action; a boss
    // is only a table here. 0.1: Eikthyr.
    internal enum ActKind { Wave, Lieutenant, Nest, Totem, Marks, Charge }

    internal class Act
    {
        public ActKind Kind;
        public string[] Prefabs;        // creatures (Wave, Lieutenant, Totem spawns), the nest (Nest), the AoE (Marks)
        public float Count = 1f;        // Wave/Lieutenant: base count of each prefab; Nest/Totem: objects; Marks: players per cast
        public int Level = 1;           // creature level, 1 = no stars
        public float HpMul = 1f;        // effective HP multiplier (damage divisor on the owner)
        public string Prop;             // Totem: the destructible prop; Marks: the telegraph effect
        public float Interval = 12f;    // Totem: seconds between spawns
        public int MaxAlive = 3;        // Totem: adds alive per totem
        public float Damage;            // Marks: damage of the strike
        public HitData.DamageType DamageType = HitData.DamageType.Lightning;
    }

    internal class PhaseDef
    {
        public float Pct;               // fires once when HP% drops to this or below
        public string Say;              // announcement ("" = none)
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
        public string Title;            // name used in messages
        public string Section;          // config section
        public List<PhaseDef> Phases = new List<PhaseDef>();
        public RewardDef Reward = new RewardDef();

        public ConfigEntry<string> CfgMode;
        public ConfigEntry<string> CfgProfile;
        public Action<ExtendedBossesPlugin, BossDef> BindExtra;

        // optional per-boss knobs (null when the boss does not use them)
        public ConfigEntry<string> CfgTotemPrefab;
        public ConfigEntry<string> CfgMarkPrefab;
        public ConfigEntry<string> CfgMarkEffect;
        public ConfigEntry<float> CfgMarkDamage;
        public ConfigEntry<float> CfgChargeInterval;
        public ConfigEntry<float> CfgChargeSpeed;
        public ConfigEntry<float> CfgChargeDuration;
    }

    public partial class ExtendedBossesPlugin
    {
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
        }

        // ------------------------------------------------------------------
        // 1. Eikthyr - Meadows, the "tutorial raid": every mechanic once, gently
        // ------------------------------------------------------------------
        private static BossDef Eikthyr()
        {
            BossDef b = new BossDef();
            b.Prefab = "Eikthyr";
            b.Title = "Эйктюр";
            b.Section = "10 Eikthyr";

            PhaseDef p;

            p = new PhaseDef { Pct = 80f, Say = "Эйктюр: Стадо, ко мне!" };
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Boar", "Neck" }, Count = 2f });
            b.Phases.Add(p);

            p = new PhaseDef { Pct = 60f, Say = "Эйктюр пригибает рога — берегитесь рывка!" };
            p.Acts.Add(new Act { Kind = ActKind.Charge });
            b.Phases.Add(p);

            p = new PhaseDef { Pct = 50f, Say = "Из груды черепов поднимаются мертвецы — разбейте её!" };
            p.Acts.Add(new Act
            {
                Kind = ActKind.Totem, Prop = "skull_pile", Count = 1f,
                Prefabs = new[] { "Skeleton_Meadows_noarcher", "Skeleton_Meadows_noarcher", "Skeleton_Meadows" },
                Interval = 12f, MaxAlive = 3
            });
            b.Phases.Add(p);

            p = new PhaseDef { Pct = 30f, Say = "Небо темнеет над Эйктюром… Отмеченный молнией — отойди от остальных!" };
            p.Acts.Add(new Act
            {
                Kind = ActKind.Marks, Prefabs = new[] { "lightningAOE" }, Prop = "vfx_prespawn",
                Count = 1f, Damage = 30f, DamageType = HitData.DamageType.Lightning
            });
            b.Phases.Add(p);

            p = new PhaseDef { Pct = 15f, Say = "Вожак стада пришёл на зов Эйктюра!" };
            p.Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Boar" }, Count = 1f, Level = 3, HpMul = 3f });
            b.Phases.Add(p);

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
                d.CfgTotemPrefab = pl.Config.Bind(d.Section, "TotemPrefab", "skull_pile",
                    "Destructible vanilla object used as the skeleton totem at 50%. Must have ZNetView and Destructible/WearNTear; check with 'eb probe <prefab>'. Fallback: BonePileSpawner (a vanilla bone-pile spawner).");
                d.CfgMarkPrefab = pl.Config.Bind(d.Section, "MarkPrefab", "lightningAOE",
                    "Vanilla AoE prefab of the lightning strike at 30%.");
                d.CfgMarkEffect = pl.Config.Bind(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect shown on the marked player before the strike (players with the mod).");
                d.CfgMarkDamage = pl.Config.Bind(d.Section, "MarkDamage", 30f,
                    new ConfigDescription("Lightning damage of the strike (before 04 Marks DamageMultiplier).", new AcceptableValueRange<float>(0f, 500f)));
                d.CfgChargeInterval = pl.Config.Bind(d.Section, "ChargeInterval", 20f,
                    new ConfigDescription("Seconds between charges from 60%.", new AcceptableValueRange<float>(5f, 120f)));
                d.CfgChargeSpeed = pl.Config.Bind(d.Section, "ChargeSpeed", 2f,
                    new ConfigDescription("Speed multiplier during a charge.", new AcceptableValueRange<float>(1f, 4f)));
                d.CfgChargeDuration = pl.Config.Bind(d.Section, "ChargeDuration", 2.5f,
                    new ConfigDescription("Seconds a charge lasts.", new AcceptableValueRange<float>(0.5f, 8f)));
            };
            return b;
        }
    }
}
