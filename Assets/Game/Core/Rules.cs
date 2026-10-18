using System;
using System.Collections.Generic;

namespace Loran.Core
{
    /// <summary>
    /// The numbers behind the core systems. Every tuning value lives here so it can be balanced in one place.
    /// </summary>
    public static partial class Rules
    {
        // Needs drain per in-game hour while awake
        public const float HungerPerHour = 2.5f;
        public const float ThirstPerHour = 4f;
        public const float EnergyPerHour = 3f;         // daily energy: only sleep, food and drink restore it
        public const float StaminaRegenPerHour = 25f;  // stamina comes back with any rest
        public const float StarveDamagePerHour = 4f;   // per empty need (hunger or thirst)

        // Corruption: starts at 5%, grows with time. Good local reputation slows it, bad speeds it up.
        public const float StartCorruption = 5f;
        public const float CorruptionPerDay = 0.3f;

        // Time walking: each step takes this many seconds (half running). At normal walking pace that's
        // about an hour of game time for a minute of real time.
        public const float StepSeconds = 12f;

        public const int WaterskinSize = 4;
        public const int DrawWaterMana = 10;
        public const int CollapseMinutes = 6 * 60;

        public static int MaxHp(GameState s) => 80 + s.stats[Stat.End] * 5 + (s.level - 1) * HpPerLevel;

        // Levels: experience from fights and finished jobs. Each level adds health and stamina.
        public const int HpPerLevel = 8, StaminaPerLevel = 4;
        public static int ExpToNext(int level) => 30 + level * 30;

        // Skills that grow with use. A skill starts untrained (0); each level needs more practice than the last.
        public const int MaxSkill = 10;
        public static float SkillToNext(int level) => 8 + level * 6;

        // Mana: what spells run on. Grows with Intellect and levels; comes back slowly with time and fully with sleep.
        public const int ManaPerLevel = 3;
        public const float ManaRegenPerHour = 6f;
        public static int MaxMana(GameState s) => 30 + s.stats[Stat.Int] * 5 + (s.level - 1) * ManaPerLevel;

        public static int MaxStamina(GameState s, GameData d)
        {
            int bonus = (s.level - 1) * StaminaPerLevel;
            foreach (var g in s.Equipped) bonus += g.stamina;
            bonus += (int)TraitSum(s, d, t => t.stamina);
            return (int)Math.Round((70 + s.stats[Stat.End] * 6 + bonus) * StaminaPower(s.body, d));
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
            s.mana += ManaRegenPerHour * h;
            if (!exerting) s.stamina += StaminaRegenPerHour * h * (s.body.cold >= ColdAt ? 0.5f : 1f);
            s.corruption += CorruptionPerDay / 24f * h * CorruptionMultiplier(s) * WardFactor(s, d);

            if (s.hunger <= 0) s.hp -= StarveDamagePerHour * h;
            if (s.thirst <= 0) s.hp -= StarveDamagePerHour * h;
            AdvanceYourBody(s, d, h, false, 0);
            AdvanceParty(s, d, h, false, 0, "");
            Clamp(s, d);
        }

        // ------------------------------------------------------------------ party

        public const int PartyMax = 3;
        public const float NeedLowAt = 25, TiredAt = 20, WoundedAt = 0.4f, MoraleLowAt = 25;
        public const float MoraleLossPerHour = 1.5f;   // for each need that's low
        public const float MoraleRestPerHour = 0.5f;   // when nothing's wrong, spirits slowly recover (up to 60)

        static readonly string[] Announce = { "starving", "hungry", "parched", "thirsty", "exhausted", "low spirits", "cold", "hot" };

        /// <summary>A party member's troubles right now: hungry, thirsty, exhausted, wounded, low spirits, then cold, wet, hot and each wound.</summary>
        public static List<string> Conditions(PartyMember m, GameData d)
        {
            var c = NeedTroubles(m, d);
            c.AddRange(BodyStates(m.body, d));
            if (m.hp <= 0) c.Insert(0, "down");
            return c;
        }

        static List<string> NeedTroubles(PartyMember m, GameData d)
        {
            var c = new List<string>();
            var f = d.Follower(m.id);
            if (m.hunger <= 0) c.Add("starving"); else if (m.hunger < NeedLowAt) c.Add("hungry");
            if (m.thirst <= 0) c.Add("parched"); else if (m.thirst < NeedLowAt) c.Add("thirsty");
            if (m.energy < TiredAt) c.Add("exhausted");
            if (f != null && m.hp > 0 && m.hp < f.hp * WoundedAt) c.Add("wounded");
            if (m.morale < MoraleLowAt) c.Add("low spirits");
            return c;
        }

        /// <summary>How hard a party member hits: each trouble takes 15% off, down to 40%.</summary>
        public static float PartyPower(PartyMember m, GameData d) => Math.Max(0.4f, 1f - 0.15f * NeedTroubles(m, d).Count) * FightPower(m.body, d);

        /// <summary>Time passes for your party too: they get hungry, thirsty and tired, and unhappy if it goes on.</summary>
        static void AdvanceParty(GameState s, GameData d, float h, bool sleeping, float energyAfter, string shelter, int start = 0)
        {
            for (int i = s.party.Count - 1; i >= 0; i--)
            {
                var m = s.party[i];
                var f = d.Follower(m.id);
                float app = f?.appetite ?? 1;
                string name = f?.name ?? m.id;
                if (sleeping) SleepBody(s, d, m.body, f?.warmth ?? 6, start, (int)(h * 60), shelter, name);
                var (bhp, ben, bth) = AdvanceBody(s, d, m.body, f?.warmth ?? 6, h, name, sleeping, 0, s.minutes, out var killedBy);
                bool down = m.hp <= 0;
                if (m.hunger <= 0) bhp += StarveDamagePerHour * h;
                if (m.thirst <= 0) bhp += StarveDamagePerHour * h;
                if (!down) m.hp -= bhp;
                m.energy -= ben;
                m.thirst -= bth;
                if (killedBy != null || (!down && m.hp <= 0 && !Has(m.body, "grave")))
                {
                    Die(s, d, m, killedBy != null ? killedBy.ToLowerInvariant() : "their wounds");
                    continue;
                }
                if (sleeping)
                {
                    m.hunger -= 2f * h * app;
                    m.thirst -= 3f * h * app;
                    m.energy = Math.Max(m.energy, energyAfter);
                    if (m.hp > 0) m.hp += 3f * h;
                }
                else
                {
                    m.hunger -= HungerPerHour * h * app;
                    m.thirst -= ThirstPerHour * h * app;
                    m.energy -= EnergyPerHour * h;
                    int low = (m.hunger < NeedLowAt ? 1 : 0) + (m.thirst < NeedLowAt ? 1 : 0) + (m.energy < TiredAt ? 1 : 0);
                    if (low > 0) m.morale -= MoraleLossPerHour * low * h;
                    else if (m.morale < 60) m.morale = Math.Min(60, m.morale + MoraleRestPerHour * h);
                }
                ClampMember(m, f);

                // Tell the player when something new goes wrong (wounds announce themselves)
                var now = Conditions(m, d);
                foreach (var c in now)
                    if (!m.conditions.Contains(c) && System.Array.IndexOf(Announce, c) >= 0) s.Toasts.Add(name + (c == "low spirits" ? " is in low spirits." : " is " + c + "."));
                m.conditions = now;

                if (m.morale <= 0 && (f == null || f.leaves))
                {
                    s.party.RemoveAt(i);
                    s.Toasts.Add(name + " has had enough, and leaves you.");
                }
            }
        }

        public static void ClampMember(PartyMember m, FollowerDef f)
        {
            m.hunger = Math.Clamp(m.hunger, 0, 100);
            m.thirst = Math.Clamp(m.thirst, 0, 100);
            m.energy = Math.Clamp(m.energy, 0, 100);
            m.morale = Math.Clamp(m.morale, f != null && !f.leaves ? 1 : 0, 100);
            if (f != null) m.hp = Math.Clamp(m.hp, m.hp <= 0 ? -1000 : 0, f.hp);
        }

        /// <summary>A companion dies. They're gone for good.</summary>
        public static void Die(GameState s, GameData d, PartyMember m, string cause)
        {
            string name = d.Follower(m.id)?.name ?? m.id;
            s.party.Remove(m);
            if (!s.fallen.Contains(m.id)) s.fallen.Add(m.id);
            if (!s.flags.Contains("dead_" + m.id)) s.flags.Add("dead_" + m.id);
            s.Toasts.Add(name + " is dead, of " + cause + ". They won't be coming back.");
        }

        /// <summary>shelter: bed (warm and dry), fire (a camp with a fire) or none (out in the open).</summary>
        public static void Sleep(GameState s, GameData d, int minutes, float energyAfter, string shelter = "bed")
        {
            float h = minutes / 60f;
            int start = s.minutes;
            AdvanceParty(s, d, h, true, energyAfter, shelter, start);
            SleepBody(s, d, s.body, Warmth(s, d), start, minutes, shelter, null);
            AdvanceYourBody(s, d, h, true, 0);
            s.minutes += minutes;
            s.hunger -= 2f * h;
            s.thirst -= 3f * h;
            s.corruption += CorruptionPerDay / 24f * h * CorruptionMultiplier(s) * WardFactor(s, d);
            s.energy = Math.Max(s.energy, energyAfter);
            s.stamina = MaxStamina(s, d);
            s.mana = MaxMana(s);
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

        public const int AttackStamina = 14, KindleStamina = 3, KindleMana = 6, FleeStamina = 10, DefendStaminaBack = 12;
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
            if (s.mana < 0) s.mana = MaxMana(s);
            s.mana = Math.Clamp(s.mana, 0, MaxMana(s));
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
