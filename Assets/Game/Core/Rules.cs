using System;
using System.Collections.Generic;
using System.Linq;

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
        public const float BreathPerSecond = 5f;       // out of a fight, stamina comes back by itself: empty to full in about 20 real seconds (David, 20 Oct)
        public const float StarveDamagePerHour = 4f;   // per empty need (hunger or thirst)

        // Corruption: starts at 5%, grows with time. Good local reputation slows it, bad speeds it up.
        public const float StartCorruption = 5f;
        public const float CorruptionPerDay = 0.3f;

        // The clock (David, 21 Oct: days passed too fast, and time should pass whether you move or not).
        // Out in the world the clock runs on its own, walking or standing: 20 game seconds a real second,
        // so a day and a night take 72 real minutes. Talking, menus and fights stop it.
        public const float ClockRate = 20f;
        public const float StepSeconds = 12f;          // old: game seconds a step used to take (now only the dev "walk" sums use it)

        public const int WaterskinSize = 4;

        // Money: everything is counted in coppers. 100 copper = 1 silver, 100 silver = 1 gold, 100 gold = 1 platinum.
        // A week's wage is about 30 silver; the king's tax is about 1 gold a month, more than three weeks' pay.
        public const int Silver = 100, Gold = 100 * Silver, Platinum = 100 * Gold;

        /// <summary>Coppers written out as coins, biggest first: "1 gold 20 silver 5 copper". short = "1g 20s 5c".</summary>
        public static string Money(long coppers, bool brief = false)
        {
            if (coppers <= 0) return brief ? "0c" : "no money";
            var parts = new System.Collections.Generic.List<string>();
            long p = coppers / Platinum, g = coppers / Gold % 100, s = coppers / Silver % 100, c = coppers % 100;
            if (p > 0) parts.Add(p + (brief ? "p" : " platinum"));
            if (g > 0) parts.Add(g + (brief ? "g" : " gold"));
            if (s > 0) parts.Add(s + (brief ? "s" : " silver"));
            if (c > 0) parts.Add(c + (brief ? "c" : " copper"));
            return string.Join(" ", parts);
        }
        public const int DrawWaterMana = 10;
        public const int CollapseMinutes = 6 * 60;

        public static int MaxHp(GameState s) => 80 + s.stats[Stat.End] * 5 + (s.level - 1) * HpPerLevel + s.hpBonus;

        // Levels: experience from fights and finished jobs. Each level adds health and stamina.
        public const int HpPerLevel = 8, StaminaPerLevel = 4;
        public static int ExpToNext(int level) => 30 + level * 30;

        // Skills that grow with use. A skill starts untrained (0); each level needs more practice than the last.
        public const int MaxSkill = 10;
        public static float SkillToNext(int level) => 8 + level * 6;

        // Mana: what spells run on. Grows with Intellect and levels; comes back slowly with time and fully with sleep.
        public const int ManaPerLevel = 3;
        public const float ManaRegenPerHour = 6f;
        // ---- what the stats do out in the world (David, 21 Oct): every point of every stat should be felt
        public const float NeedsPerEnd = 0.03f, WorkPerStr = 0.04f, LearnPerInt = 0.04f, PricePerCha = 0.04f;
        public const int MaxQuirks = 2;

        /// <summary>Your quirks, multiplied together (hunger, thirst, weather, dealt, taken) or added (hp, stamina).</summary>
        public static float QuirkMul(GameState s, GameData d, Func<QuirkDef, float> pick)
        {
            float m = 1f;
            foreach (var q in s.quirks) { var def = d.Quirk(q); if (def != null) m *= pick(def); }
            return m;
        }
        public static int QuirkAdd(GameState s, GameData d, Func<QuirkDef, int> pick)
        {
            int n = 0;
            foreach (var q in s.quirks) { var def = d.Quirk(q); if (def != null) n += pick(def); }
            return n;
        }

        /// <summary>Endurance: hunger and thirst drain 3% slower per point over 3 (and faster below).</summary>
        public static float NeedsRate(GameState s) => Math.Clamp(1f - (s.stats[Stat.End] - 3) * NeedsPerEnd, 0.7f, 1.15f);
        /// <summary>Strength: hard work (chopping, hauling, climbing) takes 4% less stamina per point over 3.</summary>
        public static float WorkRate(GameState s) => Math.Clamp(1f - (s.stats[Stat.Str] - 3) * WorkPerStr, 0.6f, 1.15f);
        /// <summary>Charisma (and where you're from): what shops charge you.</summary>
        public static float PriceRate(GameState s, GameData d) =>
            Math.Clamp(1f - (s.stats[Stat.Cha] - 3) * PricePerCha, 0.7f, 1.2f) * (d.Origin(s.origin)?.prices ?? 1f);
        /// <summary>Everything that speeds or slows your hunger and thirst, together (1 = normal).</summary>
        public static float HungerRate(GameState s, GameData d) => (HasPerk(s, d, "hardy") ? HardyNeeds : 1f) * NeedsRate(s) * QuirkMul(s, d, q => q.hunger);
        public static float ThirstRate(GameState s, GameData d) => (HasPerk(s, d, "hardy") ? HardyNeeds : 1f) * NeedsRate(s) * QuirkMul(s, d, q => q.thirst);
        /// <summary>How hard rain and cold bite you (1 = normal).</summary>
        public static float WeatherRate(GameState s, GameData d) => (HasPerk(s, d, "hardy") ? HardyWeather : 1f) * QuirkMul(s, d, q => q.weather);
        /// <summary>The attack number on the character sheet: Strength, the weapon and its skill.</summary>
        public static float Attack(GameState s, GameData d, int weaponSkill) => 4 + s.stats[Stat.Str] * 1.5f + GearDamage(s) + weaponSkill * 0.8f;
        public static int Price(GameState s, GameData d, int coppers) => Math.Max(1, (int)Math.Round(coppers * PriceRate(s, d)));

        public static int MaxMana(GameState s) => 30 + s.stats[Stat.Int] * 5 + (s.level - 1) * ManaPerLevel;

        public static int MaxStamina(GameState s, GameData d)
        {
            int bonus = (s.level - 1) * StaminaPerLevel;
            foreach (var g in s.Equipped) bonus += g.stamina;
            bonus += (int)TraitSum(s, d, t => t.stamina);
            bonus += QuirkAdd(s, d, q => q.stamina);
            return (int)Math.Round((70 + s.stats[Stat.End] * 6 + bonus) * StaminaPower(s.body, d));
        }

        /// <summary>Rep +100 halves the rate, rep -100 makes it 1.5x. Only Skelmed's local rep exists in the slice so far.</summary>
        public static float CorruptionMultiplier(GameState s)
        {
            return Math.Clamp(1f - s.Rep("skelmed") / 200f, 0.5f, 1.5f);
        }

        /// <summary>Does the life you left behind give you this perk? (berserk, nimble, deadeye, hardy, healer, studious)</summary>
        public static bool HasPerk(GameState s, GameData d, string perk) => d.Background(s.background)?.perk == perk;

        // Background perks. Tuned so each is worth about the same: the fight perks by simulated fights (tests: PERKS=1).
        public const float BerserkBelow = 0.3f, BerserkDamage = 1.3f;   // Berserk: under 30% health, blows land 30% harder
        public const int DeadeyeCrit = 12;                              // Deadeye: +12% crit chance (and the cap rises with it)
        public const int NimbleDodge = 4;                               // Nimble: dodging costs half the stamina, and the dodge zone is wider
        public const float HardyNeeds = 0.8f, HardyWeather = 0.6f;     // Hardy: hunger and thirst drain 20% slower, rain and cold 40% slower
        public const float HealerRate = 1.5f;                          // Healer: wounds heal half again as fast, treating takes half the time
        public const float StudiousRate = 1.2f;                        // Studious: stats and skills grow 20% faster

        /// <summary>What a spell really costs you.</summary>
        public static int ManaCost(GameState s, GameData d, int mana) => Math.Max(0, mana);

        /// <summary>Studious is the scholar's perk; training has no GameData to hand, so it goes by the background's id.</summary>
        /// <summary>Intellect: you learn 4% faster per point over 3 (Studious on top).</summary>
        public static float LearnRate(GameState s) => (s.background == "scholar" ? StudiousRate : 1f) * Math.Clamp(1f + (s.stats[Stat.Int] - 3) * LearnPerInt, 0.85f, 1.4f);

        /// <summary>Time passes. Time never stops, whatever the player is doing.</summary>
        public static void Advance(GameState s, GameData d, int minutes, bool exerting)
        {
            if (minutes <= 0) return;
            float h = minutes / 60f;
            s.minutes += minutes;
            if (!s.hud) return;  // the needs and the meter only run once the player is in Loran

            s.hunger -= HungerPerHour * h * HungerRate(s, d);
            s.thirst -= ThirstPerHour * h * ThirstRate(s, d);
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
        public const int MemberHpPerLevel = 6;         // each level a party member gains adds to their health, and 1 to their blows
        public const float AimedShot = 1.5f;           // a party member's aimed shot hits this much harder, and through a guard
        public static int MemberLevel(PartyMember m) => Math.Max(1, m.level);
        public static float MemberMaxHp(PartyMember m, FollowerDef f) => f == null ? 1 : f.hp + (MemberLevel(m) - 1) * MemberHpPerLevel;
        /// <summary>Has this party member reached the level for this ability of theirs?</summary>
        public static bool HasAbility(PartyMember m, FollowerDef f, string id) => f != null && f.abilities.Any(a => a.id == id && MemberLevel(m) >= a.level);
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
            if (f != null && m.hp > 0 && m.hp < MemberMaxHp(m, f) * WoundedAt) c.Add("wounded");
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
                float app = (f?.appetite ?? 1) * (f?.trait == "hill_born" ? 0.5f : 1f);   // Hill-born: lives off little
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
            if (f != null) m.hp = Math.Clamp(m.hp, m.hp <= 0 ? -1000 : 0, MemberMaxHp(m, f));
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
            foreach (var g in s.Equipped) sum += g.damage * Edge(g);
            return sum;
        }

        // ------------------------------------------------------------------ upkeep (David, 22 Oct)
        // Blades dull with every blow and armour wears with every hit it takes. A whetstone or a mending kit
        // fixes them up in the field, but only so far; the rest needs a grindstone or a smith.
        public const float BladeWear = 0.8f, GuardBladeWear = 2.5f, ArmourWear = 1.5f;
        public const float FieldSharpen = 75f, FieldMend = 70f;

        /// <summary>Gear that needs upkeep: weapons with an edge, and anything that blocks blows.</summary>
        public static bool Wears(GearItem g) => g.damage > 0 || g.guard > 0;
        public static bool IsBlade(GearItem g) => g.damage > 0;

        /// <summary>How much of a weapon's damage its edge gives: all of it down to 70, then less, to 45% when blunt.</summary>
        public static float Edge(GearItem g) => g.condition >= 70 ? 1f : 0.45f + 0.55f * Math.Max(0, g.condition) / 70f;
        /// <summary>How much of armour's guard is left: all of it down to 60, then less, to a quarter when falling apart.</summary>
        public static float Soundness(GearItem g) => g.condition >= 60 ? 1f : 0.25f + 0.75f * Math.Max(0, g.condition) / 60f;

        public static string ConditionName(GearItem g)
        {
            float c = g.condition;
            if (IsBlade(g)) return c >= 85 ? "Keen" : c >= 60 ? "Sharp" : c >= 30 ? "Dull" : "Blunt";
            return c >= 85 ? "Sound" : c >= 60 ? "Worn" : c >= 25 ? "Damaged" : "Falling apart";
        }

        /// <summary>Share of each hit that gear blocks. Pieces stack, but never past 80%.</summary>
        public static float GearGuard(GameState s, GameData d)
        {
            float through = 1f;
            foreach (var g in s.Equipped) through *= 1f - g.guard * Soundness(g);
            float guard = 1f - through + TraitSum(s, d, t => t.guard);
            return Math.Clamp(guard, 0f, 0.8f);
        }

        public static float WardFactor(GameState s, GameData d) => Math.Clamp(1f - TraitSum(s, d, t => t.ward), 0.5f, 1f);

        // ------------------------------------------------------------------ combat numbers

        public const int AttackStamina = 14, KindleStamina = 3, KindleMana = 6, FleeStamina = 10, DefendStaminaBack = 12;
        public const int DodgeStamina = 8;
        public const float OpenBonus = 0.75f;       // a blow into an enemy left open by your dodge: crit and then some
        public const float ParryCounter = 1f;       // a perfect block strikes back with a full blow
        public const float RagePerPoint = 0.3f;     // each point of enemy rage adds this to its blows
        public const float WindedTaken = 1.35f;     // blows hurt this much more when you're out of breath
        public const float GuardCounter = 0.6f;     // how hard a guarding enemy snaps back when you swing into it

        /// <summary>Out of breath: below a fifth of your stamina. You can't dodge, can't land crits, and blows hurt more.</summary>
        public static bool Winded(GameState s, GameData d) => s.stamina < MaxStamina(s, d) * 0.2f;
        public const float CritMultiplier = 1.75f, GlanceMultiplier = 0.4f;
        public const int GlanceChance = 6;   // % of blows that only glance

        /// <summary>% chance a blow is a critical hit: 5% plus 1% per point of Agility, plus half a % per level of the weapon's skill.</summary>
        public static int CritChance(GameState s, GameData d)
        {
            int skill = 0;
            var w = s.InSlot("weapon");
            string id = w != null ? d.Gear(w.id)?.skill ?? "" : "";
            if (id == "") id = "unarmed";
            foreach (var e in s.skills) if (e.id == id) skill = e.level;
            int eye = HasPerk(s, d, "deadeye") ? DeadeyeCrit : 0;
            return Math.Clamp(5 + s.stats[Stat.Agi] + skill / 2 + eye, 5, 40 + eye);
        }

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
            s.xp[stat] += amount * LearnRate(s);
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
