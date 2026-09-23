using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ExtendedBosses
{
    // A fight is data: phases at HP thresholds, each a list of actions (the "bricks" of
    // ROADMAP.md, section A). The fight controller knows how to run each kind of action; a boss
    // is only a table here.
    internal enum ActKind { Wave, Lieutenant, Nest, Totem, Marks, Charge, Shield, Resist, Cycle, Fusion }

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
        public string EndKey;           // announced when it ends (falls back to the act's EndKey)
    }

    internal class Act
    {
        public ActKind Kind;
        public string[] Prefabs;        // creatures (Wave, Lieutenant, Totem, Fusion), the nest (Nest), the AoE (Marks)
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
        public ConfigEntry<float> CfgFusionInterval;
        public ConfigEntry<float> CfgFusionHeal;
    }

    public partial class ExtendedBossesPlugin
    {
        internal const int RoleHealer = 1;
        internal const int RoleFuse = 2;
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
            Phase(b, 35f, "bonemass.35").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Surtling" }, Count = 2f });
            Phase(b, 25f, "bonemass.25").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Draugr_Elite" }, Count = 1f });
            Phase(b, 15f, "bonemass.15").Acts.Add(new Act { Kind = ActKind.Wave, Prefabs = new[] { "Wraith", "Bat_Swamp", "Bat_Swamp" }, Count = 1f });

            b.Reward.Valuables.Add(new Loot("Coins", 60, 90, true));
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
    }
}
