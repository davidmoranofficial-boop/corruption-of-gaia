using System;

namespace Loran.Core
{
    /// <summary>
    /// The numbers behind the core systems. Every tuning value lives here so it can be balanced in one place.
    /// </summary>
    public static class Rules
    {
        // Needs drain per in-game hour while awake
        public const float HungerPerHour = 3f;
        public const float ThirstPerHour = 5f;
        public const float EnergyPerHour = 4.5f;       // daily energy: only sleep, food and drink restore it
        public const float StaminaRegenPerHour = 25f;  // stamina comes back with any rest
        public const float StarveDamagePerHour = 4f;   // per empty need (hunger or thirst)

        // Corruption: starts at 5%, grows with time. Good local reputation slows it, bad speeds it up.
        public const float StartCorruption = 5f;
        public const float CorruptionPerDay = 1f;

        public const int WaterskinSize = 4;
        public const int CollapseMinutes = 6 * 60;

        public static int MaxHp(GameState s) => 80 + s.stats[Stat.End] * 5 + (s.level - 1) * HpPerLevel;

        // Levels: experience from fights and finished jobs. Each level adds health and stamina.
        public const int HpPerLevel = 8, StaminaPerLevel = 4;
        public static int ExpToNext(int level) => 30 + level * 30;

        // Skills that grow with use. A skill starts untrained (0); each level needs more practice than the last.
        public const int MaxSkill = 10;
        public static float SkillToNext(int level) => 8 + level * 6;

        public static int MaxStamina(GameState s, GameData d)
        {
            int bonus = (s.level - 1) * StaminaPerLevel;
            foreach (var g in s.Equipped) bonus += g.stamina;
            bonus += (int)TraitSum(s, d, t => t.stamina);
            return 70 + s.stats[Stat.End] * 6 + bonus;
        }

        /// <summary>Rep +100 halves the rate, rep -100 makes it 1.5x. Only Skelmed's local rep exists in the slice so far.</summary>
        public static float CorruptionMultiplier(GameState s)
        {
            return Math.Clamp(1f - s.Rep("skelmed") / 200f, 0.5f, 1.5f);
        }

        /// <summary>Time passes. Time never stops, whatever the player is doing.</summary>
        public static void Advance(GameState s, GameData d, int minutes, bool exerting)
        {
            if (minutes <= 0) return;
            float h = minutes / 60f;
            s.minutes += minutes;
            if (!s.hud) return;  // the needs and the meter only run once the player is in Loran

            s.hunger -= HungerPerHour * h;
            s.thirst -= ThirstPerHour * h;
            s.energy -= EnergyPerHour * h;
            if (!exerting) s.stamina += StaminaRegenPerHour * h;
            s.corruption += CorruptionPerDay / 24f * h * CorruptionMultiplier(s) * WardFactor(s, d);

            if (s.hunger <= 0) s.hp -= StarveDamagePerHour * h;
            if (s.thirst <= 0) s.hp -= StarveDamagePerHour * h;
            Clamp(s, d);
        }

        public static void Sleep(GameState s, GameData d, int minutes, float energyAfter)
        {
            float h = minutes / 60f;
            s.minutes += minutes;
            s.hunger -= 2f * h;
            s.thirst -= 3f * h;
            s.corruption += CorruptionPerDay / 24f * h * CorruptionMultiplier(s) * WardFactor(s, d);
            s.energy = Math.Max(s.energy, energyAfter);
            s.stamina = MaxStamina(s, d);
            s.hp += 3f * h;
            Clamp(s, d);
        }

        // ------------------------------------------------------------------ gear ranks

        /// <summary>Gear ranks from worst to best. F is common, everyday gear; S is legendary.</summary>
        public const string Ranks = "FEDCBAS";
        public static readonly string[] RankNames = { "Common", "Fine", "Quality", "Superior", "Masterwork", "Heroic", "Legendary" };
        static readonly int[] RankTraits = { 0, 1, 1, 2, 2, 3, 3 };          // S also gets a unique ability (later)
        static readonly float[] RankPower = { 1f, 1.15f, 1.3f, 1.5f, 1.75f, 2.1f, 2.6f };

        public static int RankIndex(string rank)
        {
            int i = string.IsNullOrEmpty(rank) ? 0 : Ranks.IndexOf(rank[0]);
            return i < 0 ? 0 : i;
        }
        public static string RankName(string rank) => RankNames[RankIndex(rank)];
        public static int TraitsForRank(string rank) => RankTraits[RankIndex(rank)];
        public static float RankMultiplier(string rank) => RankPower[RankIndex(rank)];

        /// <summary>Adds up one number across the traits of everything the player is wearing.</summary>
        public static float TraitSum(GameState s, GameData d, Func<TraitDef, float> pick)
        {
            float sum = 0;
            foreach (var g in s.Equipped)
                foreach (var t in g.traits)
                {
                    var def = d.Trait(t);
                    if (def != null) sum += pick(def);
                }
            return sum;
        }

        public static float GearDamage(GameState s)
        {
            float sum = 0;
            foreach (var g in s.Equipped) sum += g.damage;
            return sum;
        }

        /// <summary>Share of each hit that gear blocks. Pieces stack, but never past 80%.</summary>
        public static float GearGuard(GameState s, GameData d)
        {
            float through = 1f;
            foreach (var g in s.Equipped) through *= 1f - g.guard;
            float guard = 1f - through + TraitSum(s, d, t => t.guard);
            return Math.Clamp(guard, 0f, 0.8f);
        }

        public static float WardFactor(GameState s, GameData d) => Math.Clamp(1f - TraitSum(s, d, t => t.ward), 0.5f, 1f);

        // ------------------------------------------------------------------ combat numbers

        public const int AttackStamina = 14, KindleStamina = 8, KindleEnergy = 4, FleeStamina = 10, DefendStaminaBack = 12;
        public const float CritMultiplier = 1.75f, GlanceMultiplier = 0.4f;

        /// <summary>Who acts first: your Agility (plus Swift gear) against the enemy's speed. Ties go to you.</summary>
        public static int Speed(GameState s, GameData d) => s.stats[Stat.Agi] + (int)TraitSum(s, d, t => t.speed);

        /// <summary>
        /// The timing bar: the share of the bar that counts as a good hit, and the smaller share in its middle that counts as perfect.
        /// Agility and Balanced gear widen both.
        /// </summary>
        public static (float good, float perfect) TimingZone(GameState s, GameData d)
        {
            float bonus = TraitSum(s, d, t => t.zone);
            float good = Math.Clamp(0.26f + s.stats[Stat.Agi] * 0.012f + bonus, 0.2f, 0.6f);
            float perfect = Math.Clamp(0.07f + s.stats[Stat.Agi] * 0.004f + bonus * 0.5f, 0.05f, 0.25f);
            return (good, perfect);
        }

        /// <summary>Minutes until the next 06:00, at least four hours.</summary>
        public static int MinutesUntilMorning(GameState s)
        {
            int now = s.minutes % 1440;
            int target = 6 * 60;
            int m = target - now;
            if (m <= 0) m += 1440;
            if (m < 4 * 60) m += 1440;
            return m;
        }

        /// <summary>Stats grow through repetition. Each point needs (current level x 3) training. Charisma grows by talking people round.</summary>
        public static void Train(GameState s, int stat, float amount, string why)
        {
            s.xp[stat] += amount;
            float need = s.stats[stat] * 3f;
            while (s.xp[stat] >= need)
            {
                s.xp[stat] -= need;
                s.stats[stat]++;
                s.Toasts.Add(Stat.Names[stat] + " rose to " + s.stats[stat] + (string.IsNullOrEmpty(why) ? "" : " from " + why) + ".");
                need = s.stats[stat] * 3f;
            }
        }

        public static void Clamp(GameState s, GameData d)
        {
            s.hp = Math.Clamp(s.hp, 0, MaxHp(s));
            s.hunger = Math.Clamp(s.hunger, 0, 100);
            s.thirst = Math.Clamp(s.thirst, 0, 100);
            s.energy = Math.Clamp(s.energy, 0, 100);
            s.stamina = Math.Clamp(s.stamina, 0, MaxStamina(s, d));
            s.corruption = Math.Clamp(s.corruption, 0, 100);
            s.water = Math.Clamp(s.water, 0, WaterskinSize);
        }

        public static string Clock(GameState s) => s.Hour.ToString("00") + ":" + s.Minute.ToString("00");

        public static string TimeOfDay(GameState s)
        {
            int h = s.Hour;
            if (h < 5) return "Night";
            if (h < 8) return "Dawn";
            if (h < 12) return "Morning";
            if (h < 17) return "Afternoon";
            if (h < 20) return "Evening";
            return "Night";
        }
    }
}
