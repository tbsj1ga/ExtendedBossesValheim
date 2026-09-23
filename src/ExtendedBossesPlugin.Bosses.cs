using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // A fight is data: phases at HP thresholds, each a list of actions (the "bricks" of
    // ROADMAP.md, section A). The fight controller knows how to run each kind of action; a boss
    // is only a table here.
    internal enum ActKind { Wave, Lieutenant, Nest, Totem, Marks, Charge, Shield, Resist, Cycle, Fusion, Seeds, HitEffect, Fixate, Cocoon, Hazard }

    // One variant of a resistance cycle (Elder's sap/back bark, Bonemass's hardening).
    internal class CycleVariant
    {
        public string Id;               // "Sap", "Back", "Harden" - also the config value that pins it
        public string Say;              // announced when it starts ({0} = boss)
        public Dictionary<HitData.DamageType, float> Mods;  // multipliers by type
        public float Other = 1f;        // multiplier of every type not in Mods (Back: from the front)
        public bool Back;               // positional: full damage only from behind; never picked solo
        public float MeleeFactor = 1f;  // multiplier of hits from attackers within MeleeRange (+ boss radius)
        public float MeleeRange = 5f;
        public float Retaliate;         // damage dealt back to a close attacker per hit (at most once a second)
        public HitData.DamageType RetaliateType = HitData.DamageType.Poison;
        public float RangedFactor = 1f; // multiplier of hits from attackers beyond MeleeRange (+ boss radius)
        public bool GroundedOnly;       // no effect while the boss flies (Moder)
        public float ThornsPercent;     // % of every hit's damage dealt back to the attacker (RetaliateType), at most once a second
        public float ThornsMin;         // at least this much when thorns trigger
        public float RegenPercent;      // boss heals this % of max HP per second while it lasts...
        public HitData.DamageType RegenStopType = HitData.DamageType.Frost; // ...unless hit by this type
        public float RegenStopSeconds = 3f;                                   // ...within this many seconds
        public bool Adapt;              // resists the damage type it took most lately (announced with {1} = type)
        public float AdaptFactor = 0.6f;  // that type x
        public float AdaptOthers = 1.1f;  // every other type x
        public float Lifesteal;         // boss heals this % of the raw damage of its hits on players
        public string EndKey;           // announced when it ends (falls back to the act's EndKey)
    }

    internal class Act
    {
        public ActKind Kind;
        public string[] Prefabs;        // creatures (Wave, Lieutenant, Totem, Fusion), the nest (Nest), the AoE (Marks)
        public string[] NightPrefabs;   // Wave: used instead of Prefabs at night (wolves -> Ulvs)
        public string Fallback = "BonePileSpawner"; // Totem: vanilla nest used if the prop is not destructible (null = skip)
        public float Count = 1f;        // Wave/Lieutenant/Fusion: base count of each prefab; Nest/Totem: objects; Marks: players per cast
        public int ExtraFrom;           // Lieutenant: one more from this many players (0 = never)
        public int Level = 1;           // creature level, 1 = no stars
        public float HpMul = 1f;        // effective HP multiplier (damage divisor on the owner)
        public int Role;                // RoleHealer, RoleFuse
        public float Lifetime;          // creatures: removed after this many seconds (0 = stay)
        public bool InWater;            // Wave: only at spots under water (leeches); none found = none spawned
        public string Prop;             // Totem: the destructible prop; Marks: the telegraph effect
        public float Interval = 12f;    // Totem, Fusion: seconds between spawns
        public int MaxAlive = 3;        // Totem: adds alive per totem
        public float Damage;            // Marks: damage of the strike
        public HitData.DamageType DamageType = HitData.DamageType.Lightning;
        public string MarkKey;          // Marks: text key announced per mark ({0} boss, {1} player); Fusion: per wave
        public string Creature;         // Marks: creature summoned around the mark instead of an AoE (roots)
        public int CreatureCount = 4;   // Marks: how many, in a ring
        public float RingRadius = 3f;   // Marks: ring radius around the marked player
        public float Duration;          // Resist: seconds (0 = while the shield holds); Cycle: seconds each lasts
        public Dictionary<HitData.DamageType, float> Mods; // Resist: damage multipliers by type
        public float CooldownMin;       // Cycle: seconds between, random in [min, max]
        public float CooldownMax;
        public float Delay;             // Cycle: first one this long after the phase if no burn window came first
        public List<CycleVariant> Variants; // Cycle
        public string EndKey;           // Cycle: announced when one ends
        public float Heal;              // Fusion: % of boss max HP healed per creature that reaches it
        public string Gate;             // runs only if the boss's bool setting with this key is on (experiments)
        public bool AnyProp;            // Nest: any networked object, need not be destructible (eggs that hatch on their own)
        public bool ScaleAsAdds;        // Nest/Totem: count scales like adds instead of the nest rule
        public float Chance;            // Seeds: chance per landed projectile
        public string Effect;           // HitEffect: vanilla status effect put on the boss's hits (Wet, Tared)
        public bool Land;               // Fixate: a flying boss lands first (Moder's breath)
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
        public ConfigEntry<string> CfgCycleVariant;
        public ConfigEntry<float> CfgCycleDuration;
        public ConfigEntry<float> CfgCycleCdMin;
        public ConfigEntry<float> CfgCycleCdMax;
        public ConfigEntry<float> CfgCycleDelay;
        public ConfigEntry<float> CfgBackArc;
        public ConfigEntry<float> CfgCycleMelee;       // MeleeFactor of the variant that has one
        public ConfigEntry<float> CfgCycleRetaliate;   // Retaliate of the variant that has one
        public ConfigEntry<float> CfgCycleRanged;      // RangedFactor of the variant that has one
        public ConfigEntry<float> CfgCycleThorns;      // ThornsPercent of the variant that has one
        public ConfigEntry<float> CfgRegen;            // RegenPercent of the variant that has one
        public ConfigEntry<float> CfgAdaptFactor;
        public ConfigEntry<float> CfgAdaptOthers;
        public ConfigEntry<float> CfgLifesteal;
        public readonly Dictionary<string, ConfigEntry<bool>> Gates = new Dictionary<string, ConfigEntry<bool>>();
        public ConfigEntry<float> CfgFusionInterval;
        public ConfigEntry<float> CfgFusionHeal;
    }

    public partial class ExtendedBossesPlugin
    {
        internal const int RoleHealer = 1;
        internal const int RoleFuse = 2;
        internal const int RoleGuard = 3;
        internal const string CycleRandom = "Random";
        internal const string CycleAuto = "Auto";

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
            AddBoss(Bonemass());
            AddBoss(Moder());
            AddBoss(Yagluth());
            AddBoss(Queen());
            AddBoss(Fader());
        }

        private static PhaseDef Phase(BossDef b, float pct, string say)
        {
            PhaseDef p = new PhaseDef { Pct = pct, Say = say };
            b.Phases.Add(p);
            return p;
        }

        private static Dictionary<HitData.DamageType, float> Mods(params object[] pairs)
        {
            Dictionary<HitData.DamageType, float> d = new Dictionary<HitData.DamageType, float>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[(HitData.DamageType)pairs[i]] = (float)pairs[i + 1];
            return d;
        }

        // Binds the usual knobs of a resistance cycle under the given key prefix ("Bark", "Harden").
        private void BindCycle(BossDef d, string prefix, string whatEn, string whatRu, params string[] variantIds)
        {
            if (variantIds != null && variantIds.Length > 1)
            {
                List<string> allowed = new List<string>();
                allowed.Add(CycleRandom);
                allowed.AddRange(variantIds);
                allowed.Add(CycleAuto);
                d.CfgCycleVariant = S(d.Section, prefix + "Variant", CycleRandom,
                    "Which variant (" + whatEn + ") comes: Random (chosen anew each time), a variant name to pin it, or Auto (positional variant for a group of 3+). A single player never gets a positional variant.",
                    "Какой вариант (" + whatRu + "): Random — заново каждый раз, имя варианта — всегда он, Auto — позиционный для группы от 3 игроков. Одному игроку позиционный вариант не выпадает никогда.",
                    allowed.ToArray());
            }
            d.CfgCycleDuration = F(d.Section, prefix + "Duration", 30f, 5f, 120f, "Seconds each " + whatEn + " lasts.", "Длительность (" + whatRu + "), секунд.");
            d.CfgCycleCdMin = F(d.Section, prefix + "CooldownMin", 50f, 5f, 300f, "Cooldown between: from...", "Перерыв между: от…");
            d.CfgCycleCdMax = F(d.Section, prefix + "CooldownMax", 70f, 5f, 300f, "...to (random each time).", "…до (случайно каждый раз).");
            d.CfgCycleDelay = F(d.Section, prefix + "StartDelay", 60f, 0f, 300f,
                "The first one comes when the stagger after the fallen shield ends; if the shield still stands this long after 55%, it starts anyway.",
                "Первый раз — когда кончается оглушение после падения щита; если щит стоит дольше этого после 55 %, начинается всё равно.");
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

            b.Reward.Valuables.Add(new Loot("Coins", 60, 90, true));
            b.Reward.Valuables.Add(new Loot("Amber", 3, 3, true));
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
            // seeds: where his projectile lands, a nest may grow - rarely (5 %, at most once in 20 s, at
            // most 2 seed nests standing); seed nests spawn but never hold the shield
            p.Acts.Add(new Act { Kind = ActKind.Seeds, Prefabs = new[] { nest }, Chance = 0.05f, Interval = 20f, MaxAlive = 2, MarkKey = "elder.seed" });

            p = Phase(b, 55f, "elder.55");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { nest }, Count = 3f });
            p.Acts.Add(new Act { Kind = ActKind.Shield });
            // Living bark: after the burn window ends (or 60 s after this phase if the window never
            // came), 30 s of bark, then 50-70 s cooldown, repeat; the variant is random each time.
            p.Acts.Add(new Act
            {
                Kind = ActKind.Cycle, Duration = 30f, CooldownMin = 50f, CooldownMax = 70f, Delay = 60f, EndKey = "elder.bark.end",
                Variants = new List<CycleVariant>
                {
                    // sap: its usual fire weakness is gone, chop the tree with axes
                    new CycleVariant { Id = "Sap", Say = "elder.bark.sap", Other = 0.25f,
                                       Mods = Mods(HitData.DamageType.Slash, 1.25f, HitData.DamageType.Fire, 0.25f) },
                    // back: x0.25 from the front, full damage from behind - the tank holds it
                    new CycleVariant { Id = "Back", Say = "elder.bark.back", Other = 0.25f, Back = true },
                }
            });

            Phase(b, 45f, "elder.45").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Troll" }, ExtraFrom = 3 });
            Phase(b, 30f, "elder.30").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Bjorn" } });

            p = Phase(b, 20f, "elder.20");
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Greydwarf_Elite" }, Count = 2f });
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Greydwarf_Shaman" }, Count = 1f, Role = RoleHealer });

            Phase(b, 10f, "elder.10").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Skeleton", "Ghost" }, Count = 1f });

            b.Reward.Valuables.Add(new Loot("Coins", 120, 180, true));
            b.Reward.Valuables.Add(new Loot("Amber", 3, 6, true));
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
                pl.BindCycle(d, "Bark", "living bark - Sap: fire x0.25, slash x1.25, the rest x0.25; Back: x0.25 from the front, full from behind",
                    "живая кора: Sap — огонь ×0.25, рубящий ×1.25, остальное ×0.25; Back — спереди ×0.25, в спину полный", "Sap", "Back");
                d.CfgBackArc = pl.F(d.Section, "BackArc", 120f, 30f, 270f,
                    "Back variant: width of the arc behind the Elder that counts as 'the back', degrees.", "Вариант Back: ширина дуги позади Древнего, которая считается спиной, градусов.");
            };
            return b;
        }

        // ------------------------------------------------------------------
        // 3. Bonemass - Swamp: bone piles, then the big things of the swamp; hardening, poison
        //    puddles, slime that heals it if it gets through
        // ------------------------------------------------------------------
        private static BossDef Bonemass()
        {
            BossDef b = new BossDef { Prefab = "Bonemass", Section = "12 Bonemass" };
            const string pile = "Spawner_DraugrPile";

            Phase(b, 85f, "bonemass.85").Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { pile }, Count = 1f });

            PhaseDef p = Phase(b, 70f, "bonemass.70");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { pile }, Count = 2f });
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Leech" }, Count = 2f, InWater = true });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Marks, Prefabs = new[] { "bonemass_aoe" }, Prop = "vfx_prespawn", MarkKey = "bonemass.mark",
                Count = 1f, Damage = 25f, DamageType = HitData.DamageType.Poison
            });
            p.Acts.Add(new Act { Kind = ActKind.HitEffect, Effect = "Wet" });      // swamp water: his hits leave you wet

            p = Phase(b, 55f, "bonemass.55");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { pile }, Count = 3f });
            p.Acts.Add(new Act { Kind = ActKind.Shield });
            // alternating, like the Elder's bark:
            //   hardening - its blunt weakness turns into resistance, fire burns the bones;
            //   rotten steam - close hits sink in (x0.5) and poison the attacker, ranged is full
            p.Acts.Add(new Act
            {
                Kind = ActKind.Cycle, Duration = 30f, CooldownMin = 50f, CooldownMax = 70f, Delay = 60f,
                Variants = new List<CycleVariant>
                {
                    new CycleVariant { Id = "Harden", Say = "bonemass.harden", EndKey = "bonemass.harden.end", Other = 1f,
                                       Mods = Mods(HitData.DamageType.Blunt, 0.25f, HitData.DamageType.Fire, 2f) },
                    new CycleVariant { Id = "Rot", Say = "bonemass.rot", EndKey = "bonemass.rot.end", Other = 1f,
                                       MeleeFactor = 0.5f, MeleeRange = 5f, Retaliate = 12f, RetaliateType = HitData.DamageType.Poison },
                }
            });

            p = Phase(b, 45f, "bonemass.45");
            p.Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Abomination" } });
            p.Acts.Add(new Act { Kind = ActKind.Fusion, Prefabs = new[] { "Blob", "Blob", "BlobElite" }, Count = 2f, Interval = 25f, Heal = 3f, MarkKey = "bonemass.fusion" });

            Phase(b, 40f, "bonemass.40").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Writhan" }, Count = 1f });
            p = Phase(b, 35f, "bonemass.35");
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Surtling" }, Count = 2f });
            p.Acts.Add(new Act { Kind = ActKind.HitEffect, Effect = "Tared" });   // later: tar - slow and flammable (surtlings!)
            Phase(b, 25f, "bonemass.25").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Draugr_Elite" }, Count = 1f });
            Phase(b, 15f, "bonemass.15").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Wraith", "Bat_Swamp", "Bat_Swamp" }, Count = 1f });

            b.Reward.Valuables.Add(new Loot("Coins", 180, 270, true));
            b.Reward.Valuables.Add(new Loot("Ruby", 1, 2, false));
            b.Reward.Valuables.Add(new Loot("AmberPearl", 1, 1, true));
            b.Reward.Valuables.Add(new Loot("IronScrap", 6, 10, false));
            b.Reward.NextBiome.Add(new Loot("SilverOre", 2, 4, false));
            b.Reward.Gear = new[] { "SwordIron", "MaceIron", "AxeIron", "AtgeirIron", "SpearElderbark", "BowHuntsman",
                                    "ArmorIronChest", "ArmorIronLegs", "HelmetIron", "ShieldBanded", "ShieldIronTower",
                                    "ArmorRootChest", "ArmorRootLegs", "HelmetRoot" };
            b.Reward.NextGear = new[] { "SwordSilver", "MaceSilver", "SpearWolfFang", "KnifeSilver", "BowDraugrFang",
                                        "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake", "CapeWolf", "ShieldSilver" };

            b.BindExtra = delegate(ExtendedBossesPlugin pl, BossDef d)
            {
                d.CfgNestPrefab = pl.S(d.Section, "NestPrefab", pile,
                    "Vanilla destructible spawner placed at 85/70/55% (bone pile). Alternative: BonePileSpawner_swamp.",
                    "Ванильный разрушаемый спавнер на 85/70/55 % (куча костей). Альтернатива — BonePileSpawner_swamp.");
                d.CfgMarkPrefab = pl.S(d.Section, "MarkPrefab", "bonemass_aoe",
                    "Vanilla AoE prefab of the poison puddle under a marked player from 70%.", "Ванильный AoE-префаб ядовитой лужи под отмеченным игроком с 70 %.");
                d.CfgMarkEffect = pl.S(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect on the marked player before the puddle (players with the mod).", "Ванильный эффект на отмеченном игроке до лужи (у игроков с модом).");
                d.CfgMarkDamage = pl.F(d.Section, "MarkDamage", 25f, 0f, 500f,
                    "Poison damage of the puddle (before 04 Marks DamageMultiplier).", "Урон ядом лужи (до множителя из 04 Marks).");
                pl.BindCycle(d, "Cycle", "Harden: blunt x0.25, fire x2; Rot: close hits x0.5 and poison the attacker, ranged full",
                    "Harden — затвердевание: дробящий ×0.25, огонь ×2; Rot — гнилостный пар: удары вблизи ×0.5 и травят атакующего, издалека полный", "Harden", "Rot");
                d.CfgCycleMelee = pl.F(d.Section, "RotMeleeFactor", 0.5f, 0f, 1f,
                    "Rotten steam: damage of hits from within 5 m (x).", "Гнилостный пар: урон ударов ближе 5 м (×).");
                d.CfgCycleRetaliate = pl.F(d.Section, "RotPoisonDamage", 12f, 0f, 200f,
                    "Rotten steam: poison dealt back to a close attacker per hit (at most once a second per player).",
                    "Гнилостный пар: яд, который получает атакующий вблизи за удар (не чаще раза в секунду на игрока).");
                d.CfgFusionInterval = pl.F(d.Section, "SlimeInterval", 25f, 5f, 180f,
                    "From 45%: seconds between slime waves crawling to Bonemass.", "С 45 %: секунд между волнами слизи, ползущей к Массивному.");
                d.CfgFusionHeal = pl.F(d.Section, "SlimeHealPercent", 3f, 0f, 25f,
                    "% of max HP Bonemass heals for every slime that reaches it.", "Сколько % макс. HP Массивный лечит за каждую дошедшую до него слизь.");
            };
            return b;
        }

        // ------------------------------------------------------------------
        // 4. Moder - Mountains: ice stalagmites hatch drakes and hold her shield; wolves, cultists,
        //    a golem; ice flashes under marked players; ice armor (arrows bounce while she is on
        //    the ground) alternating with ice thorns (every hit is paid back with frost)
        // ------------------------------------------------------------------
        private static BossDef Moder()
        {
            BossDef b = new BossDef { Prefab = "Dragon", Section = "13 Moder" };
            const string spike = "caverock_ice_stalagmite";
            string[] drakes = { "Hatchling" };

            PhaseDef p = Phase(b, 85f, "moder.85");
            p.Acts.Add(new Act { Kind = ActKind.Totem, Prop = spike, Count = 1f, Prefabs = drakes, Interval = 15f, MaxAlive = 2, Fallback = null });
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Wolf" }, NightPrefabs = new[] { "Ulv" }, Count = 2f });

            p = Phase(b, 70f, "moder.70");
            p.Acts.Add(new Act { Kind = ActKind.Totem, Prop = spike, Count = 2f, Prefabs = drakes, Interval = 15f, MaxAlive = 2, Fallback = null });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Marks, Prefabs = new[] { "FenringIceNova_aoe" }, Prop = "vfx_prespawn", MarkKey = "moder.mark",
                Count = 1f, Damage = 25f, DamageType = HitData.DamageType.Frost
            });

            p = Phase(b, 55f, "moder.55");
            p.Acts.Add(new Act { Kind = ActKind.Totem, Prop = spike, Count = 3f, Prefabs = drakes, Interval = 15f, MaxAlive = 2, Fallback = null });
            p.Acts.Add(new Act { Kind = ActKind.Shield });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Cycle, Duration = 30f, CooldownMin = 50f, CooldownMax = 70f, Delay = 60f,
                Variants = new List<CycleVariant>
                {
                    // ice armor: arrows bounce off while she is on the ground - melee window
                    new CycleVariant { Id = "IceArmor", Say = "moder.ice", EndKey = "moder.ice.end", Other = 1f,
                                       MeleeRange = 6f, RangedFactor = 0.25f, GroundedOnly = true },
                    // ice thorns: every hit is paid back with frost (and the frost slows)
                    new CycleVariant { Id = "Thorns", Say = "moder.thorns", EndKey = "moder.thorns.end", Other = 1f,
                                       ThornsPercent = 12f, ThornsMin = 5f, RetaliateType = HitData.DamageType.Frost },
                }
            });

            p = Phase(b, 45f, "moder.45");
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Fenring_Cultist" }, Count = 1f });
            // breath mark: she lands and takes the marked player as her target for a while
            p.Acts.Add(new Act { Kind = ActKind.Fixate, Land = true, Interval = 35f, Duration = 8f, MarkKey = "moder.breath" });
            Phase(b, 35f, "moder.35").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "StoneGolem" } });
            p = Phase(b, 20f, "moder.20");
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Fenring" }, Count = 1f });
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = drakes, Count = 2f });

            b.Reward.Valuables.Add(new Loot("Coins", 240, 360, true));
            b.Reward.Valuables.Add(new Loot("Ruby", 1, 2, false));
            b.Reward.Valuables.Add(new Loot("SilverNecklace", 1, 1, true));
            b.Reward.Valuables.Add(new Loot("SilverOre", 6, 10, false));
            b.Reward.Valuables.Add(new Loot("Obsidian", 4, 8, false));
            b.Reward.Valuables.Add(new Loot("Crystal", 2, 4, false));
            b.Reward.NextBiome.Add(new Loot("BlackMetalScrap", 2, 4, false));
            b.Reward.Gear = new[] { "SwordSilver", "MaceSilver", "SpearWolfFang", "KnifeSilver", "BowDraugrFang", "ShieldSilver",
                                    "ArmorWolfChest", "ArmorWolfLegs", "HelmetDrake", "CapeWolf",
                                    "ArmorFenringChest", "ArmorFenringLegs", "HelmetFenring" };
            b.Reward.NextGear = new[] { "SwordBlackmetal", "AxeBlackMetal", "AtgeirBlackmetal", "KnifeBlackMetal", "MaceNeedle", "ShieldBlackmetal",
                                        "ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded", "CapeLinen" };

            b.BindExtra = delegate(ExtendedBossesPlugin pl, BossDef d)
            {
                d.CfgTotemPrefab = pl.S(d.Section, "TotemPrefab", spike,
                    "Destructible vanilla object that hatches drakes at 85/70/55% and holds the shield. Must be networked and destructible (see the self-check).",
                    "Разрушаемый ванильный объект, из которого вылупляются дрейки на 85/70/55 % и который держит щит. Должен быть сетевым и разрушаемым (см. самопроверку).");
                d.CfgMarkPrefab = pl.S(d.Section, "MarkPrefab", "FenringIceNova_aoe",
                    "Vanilla AoE prefab of the ice flash under a marked player from 70%.", "Ванильный AoE-префаб ледяной вспышки под отмеченным игроком с 70 %.");
                d.CfgMarkEffect = pl.S(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect on the marked player before the flash (players with the mod).", "Ванильный эффект на отмеченном игроке до вспышки (у игроков с модом).");
                d.CfgMarkDamage = pl.F(d.Section, "MarkDamage", 25f, 0f, 500f,
                    "Frost damage of the flash (before 04 Marks DamageMultiplier); frost also slows.", "Урон морозом вспышки (до множителя из 04 Marks); мороз ещё и замедляет.");
                pl.BindCycle(d, "Cycle", "IceArmor: hits from afar x0.25 while she is on the ground; Thorns: every hit is paid back with frost",
                    "IceArmor — ледяная броня: удары издалека ×0.25, пока она на земле; Thorns — ледяные шипы: каждый удар возвращается морозом", "IceArmor", "Thorns");
                d.CfgCycleRanged = pl.F(d.Section, "IceArmorRangedFactor", 0.25f, 0f, 1f,
                    "Ice armor: damage of hits from farther than 6 m (x), only while Moder is on the ground.", "Ледяная броня: урон ударов дальше 6 м (×), только пока Модер на земле.");
                d.CfgCycleThorns = pl.F(d.Section, "ThornsPercent", 12f, 0f, 100f,
                    "Ice thorns: % of each hit's damage dealt back as frost to the attacker (at least 5, at most once a second per player).",
                    "Ледяные шипы: % урона удара, который возвращается атакующему морозом (не меньше 5, не чаще раза в секунду на игрока).");
            };
            return b;
        }

        // ------------------------------------------------------------------
        // 5. Yagluth - Plains: fuling totems with the shield, shamans heal him, meteors on marks,
        //    brutes, Unbjorn, the three skeletons of HardBosses, deathsquitos; regeneration (stopped
        //    by frost) alternating with a mild adaptation to the damage type used most
        // ------------------------------------------------------------------
        private static BossDef Yagluth()
        {
            BossDef b = new BossDef { Prefab = "GoblinKing", Section = "14 Yagluth" };
            const string totem = "goblin_totempole";
            string[] fulings = { "Goblin", "Goblin", "GoblinArcher" };

            Phase(b, 85f, "yagluth.85").Acts.Add(new Act { Kind = ActKind.Totem, Prop = totem, Count = 1f, Prefabs = fulings, Interval = 12f, MaxAlive = 3, Fallback = null });

            PhaseDef p = Phase(b, 70f, "yagluth.70");
            p.Acts.Add(new Act { Kind = ActKind.Totem, Prop = totem, Count = 2f, Prefabs = fulings, Interval = 12f, MaxAlive = 3, Fallback = null });
            // his own meteor shower, dropped around a marked player (its own vanilla damage)
            p.Acts.Add(new Act { Kind = ActKind.Marks, Prefabs = new[] { "spawn_meteors" }, Prop = "vfx_prespawn", MarkKey = "yagluth.mark", Count = 1f });

            p = Phase(b, 55f, "yagluth.55");
            p.Acts.Add(new Act { Kind = ActKind.Totem, Prop = totem, Count = 3f, Prefabs = fulings, Interval = 12f, MaxAlive = 3, Fallback = null });
            p.Acts.Add(new Act { Kind = ActKind.Shield });
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "GoblinShaman" }, Count = 1f, Role = RoleHealer });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Cycle, Duration = 30f, CooldownMin = 50f, CooldownMax = 70f, Delay = 60f,
                Variants = new List<CycleVariant>
                {
                    // the fire of the fulings feeds him; frost puts it out for a while
                    new CycleVariant { Id = "Regen", Say = "yagluth.regen", EndKey = "yagluth.regen.end",
                                       RegenPercent = 0.6f, RegenStopType = HitData.DamageType.Frost, RegenStopSeconds = 3f },
                    // mild: the most used damage type of the last seconds x0.6, the rest x1.1
                    new CycleVariant { Id = "Adapt", Say = "yagluth.adapt", EndKey = "yagluth.adapt.end",
                                       Adapt = true, AdaptFactor = 0.6f, AdaptOthers = 1.1f },
                }
            });

            p = Phase(b, 45f, "yagluth.45");
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "GoblinBrute" }, Count = 1f });
            // beam on a mark: he locks on a player (his beam follows his target) - break line of sight
            p.Acts.Add(new Act { Kind = ActKind.Fixate, Interval = 30f, Duration = 8f, MarkKey = "yagluth.beam" });
            p.Acts.Add(new Act { Kind = ActKind.HitEffect, Effect = "Tared" });   // tar on his hits - and he is fire
            Phase(b, 30f, "yagluth.30").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Unbjorn" } });
            Phase(b, 20f, "yagluth.20").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Skeleton_NoArcher", "Skeleton", "Skeleton_Poison" }, Count = 1f });
            Phase(b, 15f, "yagluth.15").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Deathsquito" }, Count = 1f });
            // experiment, off by default: the echo of the previous boss, half its HP
            Phase(b, 10f, "yagluth.10").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Aspect_Moder" }, HpMul = 0.5f, Gate = "EchoOfModer" });

            b.Reward.Valuables.Add(new Loot("Coins", 300, 450, true));
            b.Reward.Valuables.Add(new Loot("SilverNecklace", 1, 2, true));
            b.Reward.Valuables.Add(new Loot("Ruby", 2, 3, false));
            b.Reward.Valuables.Add(new Loot("BlackMetalScrap", 8, 14, false));
            b.Reward.NextBiome.Add(new Loot("Softtissue", 2, 4, false));
            b.Reward.NextBiome.Add(new Loot("BlackCore", 1, 1, false));
            b.Reward.Gear = new[] { "SwordBlackmetal", "AxeBlackMetal", "AtgeirBlackmetal", "KnifeBlackMetal", "MaceNeedle",
                                    "ShieldBlackmetal", "ShieldBlackmetalTower",
                                    "ArmorPaddedCuirass", "ArmorPaddedGreaves", "HelmetPadded", "CapeLinen", "CapeLox" };
            b.Reward.NextGear = new[] { "SwordMistwalker", "AxeJotunBane", "THSwordKrom", "KnifeSkollAndHati", "SpearCarapace", "AtgeirHimminAfl",
                                        "MaceEldner", "BowSpineSnap", "CrossbowArbalest", "ShieldCarapace",
                                        "ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace",
                                        "ArmorMageChest", "ArmorMageLegs", "HelmetMage", "StaffFireball", "StaffIceShards" };

            b.BindExtra = delegate(ExtendedBossesPlugin pl, BossDef d)
            {
                d.CfgTotemPrefab = pl.S(d.Section, "TotemPrefab", totem,
                    "Destructible vanilla object that spawns fulings at 85/70/55% and holds the shield. Must be networked and destructible (see the self-check).",
                    "Разрушаемый ванильный объект, из которого идут фулинги на 85/70/55 % и который держит щит. Должен быть сетевым и разрушаемым (см. самопроверку).");
                d.CfgMarkPrefab = pl.S(d.Section, "MarkPrefab", "spawn_meteors",
                    "Vanilla prefab dropped at a marked player from 70%: spawn_meteors (his meteor shower, its own damage) or an AoE such as aoe_nova.",
                    "Ванильный префаб под отмеченным игроком с 70 %: spawn_meteors (его метеоритный дождь, урон свой) или AoE, например aoe_nova.");
                d.CfgMarkEffect = pl.S(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect on the marked player before the meteors (players with the mod).", "Ванильный эффект на отмеченном игроке до метеоров (у игроков с модом).");
                pl.BindCycle(d, "Cycle", "Regen: heals unless hit by frost within 3 s; Adapt: the most used damage type x0.6, the rest x1.1",
                    "Regen — регенерация: лечится, если 3 с не получал мороза; Adapt — адаптация: самый частый тип урона ×0.6, остальные ×1.1", "Regen", "Adapt");
                d.CfgRegen = pl.F(d.Section, "RegenPercentPerSecond", 0.6f, 0f, 5f,
                    "Regeneration: % of max HP healed per second while no frost hit him for 3 s.", "Регенерация: % макс. HP в секунду, пока его 3 с не били морозом.");
                d.CfgAdaptFactor = pl.F(d.Section, "AdaptFactor", 0.6f, 0.1f, 1f,
                    "Adaptation: damage of the type he adapted to (x).", "Адаптация: урон того типа, к которому он приспособился (×).");
                d.CfgAdaptOthers = pl.F(d.Section, "AdaptOthers", 1.1f, 1f, 2f,
                    "Adaptation: damage of every other type (x).", "Адаптация: урон всех остальных типов (×).");
                d.Gates["EchoOfModer"] = pl.B(d.Section, "EchoOfModer", false,
                    "Experiment: at 10% Yagluth calls the echo of Moder (Aspect_Moder, half HP). Off by default.",
                    "Эксперимент: на 10 % Яглут призывает эхо Модер (Aspect_Moder, половина HP). По умолчанию выкл.");
            };
            return b;
        }

        // ------------------------------------------------------------------
        // 6. The Queen - Mistlands: clutches of eggs (smash them before they hatch - they hold
        //    her shield), acid on marks, brutes, a gjall, ticks and brood; bloodthirst (heals from
        //    her hits) alternating with acid thorns (every hit is paid back with poison)
        // ------------------------------------------------------------------
        private static BossDef Queen()
        {
            BossDef b = new BossDef { Prefab = "SeekerQueen", Section = "15 Queen" };
            const string egg = "SeekerEgg_alwayshatch";

            Phase(b, 85f, "queen.85").Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { egg }, Count = 2f, AnyProp = true, ScaleAsAdds = true });

            PhaseDef p = Phase(b, 70f, "queen.70");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { egg }, Count = 2f, AnyProp = true, ScaleAsAdds = true });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Marks, Prefabs = new[] { "SeekerQueen_spithit" }, Prop = "vfx_prespawn", MarkKey = "queen.mark",
                Count = 1f, Damage = 30f, DamageType = HitData.DamageType.Poison
            });

            p = Phase(b, 55f, "queen.55");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { egg }, Count = 3f, AnyProp = true, ScaleAsAdds = true });
            p.Acts.Add(new Act { Kind = ActKind.Shield });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Cycle, Duration = 30f, CooldownMin = 50f, CooldownMax = 70f, Delay = 60f,
                Variants = new List<CycleVariant>
                {
                    // bloodthirst: her hits on players heal her - dodge, block, keep the tank up
                    new CycleVariant { Id = "Bloodthirst", Say = "queen.blood", EndKey = "queen.blood.end", Lifesteal = 40f },
                    // acid thorns: every hit on her is paid back with poison
                    new CycleVariant { Id = "AcidThorns", Say = "queen.acid", EndKey = "queen.acid.end",
                                       ThornsPercent = 10f, ThornsMin = 5f, RetaliateType = HitData.DamageType.Poison },
                }
            });

            p = Phase(b, 45f, "queen.45");
            p.Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "SeekerBrute" }, Count = 1f });
            // cocoon: after each of her teleports guards appear; while they live she takes x0.1
            p.Acts.Add(new Act { Kind = ActKind.Cocoon, Prefabs = new[] { "Seeker", "SeekerBrood" }, Count = 1f, Duration = 20f, Interval = 30f, MarkKey = "queen.cocoon" });
            Phase(b, 30f, "queen.30").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Gjall" } });
            Phase(b, 20f, "queen.20").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Tick", "SeekerBrood" }, Count = 2f });

            b.Reward.Valuables.Add(new Loot("Coins", 360, 540, true));
            b.Reward.Valuables.Add(new Loot("Ruby", 2, 3, false));
            b.Reward.Valuables.Add(new Loot("Softtissue", 4, 8, false));
            b.Reward.Valuables.Add(new Loot("BlackCore", 1, 2, false));
            b.Reward.Valuables.Add(new Loot("Eitr", 2, 4, true));
            b.Reward.NextBiome.Add(new Loot("FlametalOreNew", 2, 4, false));
            b.Reward.NextBiome.Add(new Loot("GemstoneRed", 1, 1, false));
            b.Reward.NextBiome.Add(new Loot("GemstoneGreen", 1, 1, false));
            b.Reward.NextBiome.Add(new Loot("GemstoneBlue", 1, 1, false));
            b.Reward.Gear = new[] { "SwordMistwalker", "AxeJotunBane", "THSwordKrom", "KnifeSkollAndHati", "SpearCarapace", "AtgeirHimminAfl",
                                    "MaceEldner", "BowSpineSnap", "CrossbowArbalest", "ShieldCarapace",
                                    "ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace",
                                    "ArmorMageChest", "ArmorMageLegs", "HelmetMage", "StaffFireball", "StaffIceShards" };
            b.Reward.NextGear = new[] { "SwordNiedhogg", "AxeBerzerkr", "THSwordSlayer", "SpearSplitner", "SledgeDemolisher",
                                        "BowAshlands", "CrossbowRipper", "ShieldFlametal",
                                        "ArmorFlametalChest", "ArmorFlametalLegs", "HelmetFlametal" };

            b.BindExtra = delegate(ExtendedBossesPlugin pl, BossDef d)
            {
                d.CfgNestPrefab = pl.S(d.Section, "EggPrefab", egg,
                    "Vanilla egg laid in clutches at 85/70/55%: they hatch seekers on their own and hold her shield until smashed or hatched.",
                    "Ванильное яйцо, кладки на 85/70/55 %: сами вылупляются ищущими и держат её щит, пока не разбиты или не вылупились.");
                d.CfgMarkPrefab = pl.S(d.Section, "MarkPrefab", "SeekerQueen_spithit",
                    "Vanilla AoE prefab of the acid splash under a marked player from 70%.", "Ванильный AoE-префаб кислотного всплеска под отмеченным игроком с 70 %.");
                d.CfgMarkEffect = pl.S(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect on the marked player before the acid (players with the mod).", "Ванильный эффект на отмеченном игроке до кислоты (у игроков с модом).");
                d.CfgMarkDamage = pl.F(d.Section, "MarkDamage", 30f, 0f, 500f,
                    "Poison damage of the acid splash (before 04 Marks DamageMultiplier).", "Урон ядом кислотного всплеска (до множителя из 04 Marks).");
                pl.BindCycle(d, "Cycle", "Bloodthirst: her hits on players heal her; AcidThorns: every hit on her is paid back with poison",
                    "Bloodthirst — кровожадность: её удары по игрокам лечат её; AcidThorns — кислотные шипы: каждый удар по ней возвращается ядом", "Bloodthirst", "AcidThorns");
                d.CfgLifesteal = pl.F(d.Section, "BloodthirstPercent", 40f, 0f, 200f,
                    "Bloodthirst: % of the raw damage of her hits on players she heals (before armor and block).",
                    "Кровожадность: сколько % сырого урона её ударов по игрокам она лечит (до брони и блока).");
                d.CfgCycleThorns = pl.F(d.Section, "AcidThornsPercent", 10f, 0f, 100f,
                    "Acid thorns: % of each hit's damage dealt back as poison to the attacker (at least 5, at most once a second per player).",
                    "Кислотные шипы: % урона удара, который возвращается атакующему ядом (не меньше 5, не чаще раза в секунду на игрока).");
            };
            return b;
        }

        // ------------------------------------------------------------------
        // 7. Fader - Ashlands: charred spawner stones hold the shield, his own meteors on marks,
        //    Morgen, a fallen valkyrie, Lord Reto, asksvins and lava blobs; molten armor (close
        //    hits x0.5 and burn the attacker) alternating with an ash veil (hits from afar x0.25)
        // ------------------------------------------------------------------
        private static BossDef Fader()
        {
            BossDef b = new BossDef { Prefab = "Fader", Section = "16 Fader" };
            const string stone = "Spawner_CharredStone";

            Phase(b, 85f, "fader.85").Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { stone }, Count = 1f });

            PhaseDef p = Phase(b, 70f, "fader.70");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { stone }, Count = 2f });
            p.Acts.Add(new Act { Kind = ActKind.Marks, Prefabs = new[] { "spawn_fader_meteors" }, Prop = "vfx_prespawn", MarkKey = "fader.mark", Count = 1f });

            p = Phase(b, 55f, "fader.55");
            p.Acts.Add(new Act { Kind = ActKind.Nest, Prefabs = new[] { stone }, Count = 3f });
            p.Acts.Add(new Act { Kind = ActKind.Shield });
            p.Acts.Add(new Act
            {
                Kind = ActKind.Cycle, Duration = 30f, CooldownMin = 50f, CooldownMax = 70f, Delay = 60f,
                Variants = new List<CycleVariant>
                {
                    // molten armor: close hits sink into molten metal (x0.5) and burn the attacker
                    new CycleVariant { Id = "Molten", Say = "fader.molten", EndKey = "fader.molten.end",
                                       MeleeFactor = 0.5f, MeleeRange = 6f, Retaliate = 15f, RetaliateType = HitData.DamageType.Fire },
                    // ash veil: hits from afar x0.25 - everyone in close
                    new CycleVariant { Id = "AshVeil", Say = "fader.ash", EndKey = "fader.ash.end",
                                       MeleeRange = 6f, RangedFactor = 0.25f },
                }
            });

            p = Phase(b, 45f, "fader.45");
            p.Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Morgen" } });
            // his own wall of fire, raised from where he stands every 45 s - the raid must split
            p.Acts.Add(new Act { Kind = ActKind.Hazard, Prefabs = new[] { "Fader_WallOfFire_Spawn" }, Interval = 45f, MarkKey = "fader.wall" });
            Phase(b, 30f, "fader.30").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "FallenValkyrie" } });
            Phase(b, 20f, "fader.20").Acts.Add(new Act { Kind = ActKind.Lieutenant, Prefabs = new[] { "Charred_Melee_Dyrnwyn" } });
            Phase(b, 10f, "fader.10").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Asksvin", "BlobLava" }, Count = 1f });

            b.Reward.Valuables.Add(new Loot("Coins", 450, 750, true));
            b.Reward.Valuables.Add(new Loot("GemstoneRed", 1, 2, false));
            b.Reward.Valuables.Add(new Loot("GemstoneGreen", 1, 2, false));
            b.Reward.Valuables.Add(new Loot("GemstoneBlue", 1, 2, false));
            b.Reward.Valuables.Add(new Loot("FlametalOreNew", 6, 12, false));
            b.Reward.Valuables.Add(new Loot("MoltenCore", 1, 2, false));
            b.Reward.NextBiome.Add(new Loot("FrostCore", 1, 2, false));
            b.Reward.Gear = new[] { "SwordNiedhogg", "AxeBerzerkr", "THSwordSlayer", "SpearSplitner", "SledgeDemolisher",
                                    "BowAshlands", "CrossbowRipper", "ShieldFlametal", "ShieldFlametalTower",
                                    "ArmorFlametalChest", "ArmorFlametalLegs", "HelmetFlametal", "StaffGreenRoots" };

            b.BindExtra = delegate(ExtendedBossesPlugin pl, BossDef d)
            {
                d.CfgNestPrefab = pl.S(d.Section, "NestPrefab", stone,
                    "Vanilla destructible spawner of charred at 85/70/55%. Alternative: Spawner_CharredCross.",
                    "Ванильный разрушаемый спавнер обугленных на 85/70/55 %. Альтернатива — Spawner_CharredCross.");
                d.CfgMarkPrefab = pl.S(d.Section, "MarkPrefab", "spawn_fader_meteors",
                    "Vanilla prefab dropped at a marked player from 70%: spawn_fader_meteors (his meteors, own damage) or an AoE.",
                    "Ванильный префаб под отмеченным игроком с 70 %: spawn_fader_meteors (его метеоры, урон свой) или AoE.");
                d.CfgMarkEffect = pl.S(d.Section, "MarkEffect", "vfx_prespawn",
                    "Vanilla effect on the marked player before the meteors (players with the mod).", "Ванильный эффект на отмеченном игроке до метеоров (у игроков с модом).");
                pl.BindCycle(d, "Cycle", "Molten: close hits x0.5 and burn the attacker; AshVeil: hits from afar x0.25",
                    "Molten — раскалённая броня: удары вблизи ×0.5 и обжигают атакующего; AshVeil — пепельная завеса: удары издалека ×0.25", "Molten", "AshVeil");
                d.CfgCycleMelee = pl.F(d.Section, "MoltenMeleeFactor", 0.5f, 0f, 1f,
                    "Molten armor: damage of hits from within 6 m (x).", "Раскалённая броня: урон ударов ближе 6 м (×).");
                d.CfgCycleRetaliate = pl.F(d.Section, "MoltenFireDamage", 15f, 0f, 200f,
                    "Molten armor: fire dealt back to a close attacker per hit (at most once a second per player).",
                    "Раскалённая броня: огонь, который получает атакующий вблизи за удар (не чаще раза в секунду на игрока).");
                d.CfgCycleRanged = pl.F(d.Section, "AshVeilRangedFactor", 0.25f, 0f, 1f,
                    "Ash veil: damage of hits from farther than 6 m (x).", "Пепельная завеса: урон ударов дальше 6 м (×).");
            };
            return b;
        }
    }
}
