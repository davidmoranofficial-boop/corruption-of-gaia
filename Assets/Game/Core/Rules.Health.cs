using System;
using System.Collections.Generic;
using System.Linq;

namespace Loran.Core
{
    /// <summary>
    /// Weather, cold, heat and wet, and wounds and ailments. These run for you and for everyone in your party.
    /// The weather is worked out from the game's seed and the clock, so it's the same every time you load.
    /// </summary>
    public static partial class Rules
    {
        public const float ColdAt = 40, HotAt = 40, WetAt = 40;   // when a body counts as cold, hot or wet
        public const float Comfortable = 8, TooHot = 30;          // felt °C: below this you get colder, above that hotter
        public const float ShelterTemp = 17;                       // indoors
        public const int WeatherHours = 8;                         // the weather can only change every eight hours...
        public const int WeatherHolds = 50;                        // ...and half the time it holds for the next stretch too

        // The calendar: four seasons of 28 days. The game starts in high summer.
        public static readonly string[] Seasons = { "spring", "summer", "autumn", "winter" };
        public const int SeasonDays = 28, StartDayOfYear = 40;
        public static int DayOfYear(int minutes) => (minutes / 1440 + StartDayOfYear) % (SeasonDays * 4);
        public static string Season(int minutes) => Seasons[DayOfYear(minutes) / SeasonDays];
        public static string Season(GameState s) => Season(s.minutes);

        /// <summary>A place's weather for the season at that moment.</summary>
        public static SeasonClimate SeasonAt(GameState s, GameData d, int minutes)
        {
            var c = ClimateAt(s, d);
            if (c == null || c.seasons.Count == 0) return null;
            string season = Season(minutes);
            return c.seasons.FirstOrDefault(x => x.season == season) ?? c.seasons[0];
        }
        public const float FirstAidPerLevel = 0.08f;              // each level of First aid heals wounds 8% faster

        // ------------------------------------------------------------------ weather

        static ulong Hash(int seed, long n, int salt)
        {
            ulong z = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + (ulong)n * 0xBF58476D1CE4E5B9UL + (ulong)salt * 0x94D049BB133111EBUL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static ClimateDef ClimateAt(GameState s, GameData d)
        {
            var a = d.Area(s.area);
            return d.Climate(a?.climate ?? "coast") ?? d.file.climates.FirstOrDefault();
        }

        public static bool Sheltered(GameState s, GameData d) => d.Area(s.area)?.indoor == true;

        /// <summary>The weather at a moment. It tends to hold: often a stretch is the same as the one before.</summary>
        public static WeatherDef WeatherAt(GameState s, GameData d, int minutes)
        {
            if (!string.IsNullOrEmpty(s.devWeather) && d.Weather(s.devWeather) != null) return d.Weather(s.devWeather);
            var c = SeasonAt(s, d, minutes);
            if (c == null || c.weather.Count == 0) return null;
            long block = minutes / (WeatherHours * 60);
            long b = block;
            while (b > block - 6 && Hash(s.seed, b, 1) % 100 < (ulong)WeatherHolds) b--;
            var w = PickWeather(s, c, b, d);
            // Fog is a morning thing: it burns off by late morning
            float hour = (minutes % 1440) / 60f;
            if (w != null && w.id == "fog" && (hour < 3 || hour >= 11)) return d.Weather("clear") ?? w;
            return w;
        }

        static WeatherDef PickWeather(GameState s, SeasonClimate c, long b, GameData d)
        {
            int total = c.weather.Sum(w => Math.Max(0, w.weight));
            if (total <= 0) return d.Weather(c.weather[0].id);
            int roll = (int)(Hash(s.seed, b, 2) % (ulong)total);
            foreach (var w in c.weather)
            {
                roll -= Math.Max(0, w.weight);
                if (roll < 0) return d.Weather(w.id);
            }
            return d.Weather(c.weather[0].id);
        }

        public static WeatherDef WeatherNow(GameState s, GameData d) => WeatherAt(s, d, s.minutes);

        /// <summary>The air temperature outside, °C: coldest before dawn, warmest mid-afternoon, plus the weather.</summary>
        public static float AirTemp(GameState s, GameData d, int minutes)
        {
            var c = SeasonAt(s, d, minutes);
            if (c == null) return 15;
            float hour = (minutes % 1440) / 60f;
            float t = c.night + (c.day - c.night) * (0.5f - 0.5f * (float)Math.Cos(2 * Math.PI * (hour - 4) / 24));
            return t + (WeatherAt(s, d, minutes)?.temp ?? 0);
        }
        public static float AirTemp(GameState s, GameData d) => AirTemp(s, d, s.minutes);

        /// <summary>How warm your clothes keep you, °C.</summary>
        public static float Warmth(GameState s, GameData d) => s.Equipped.Sum(g => (float)(d.Gear(g.id)?.warmth ?? 0));

        /// <summary>How warm a body feels: the air, plus clothes and any fire, less being wet. Indoors it's always mild.</summary>
        public static float Felt(GameState s, GameData d, Body b, float warmth, float bonus = 0, int? at = null)
        {
            if (Sheltered(s, d)) return ShelterTemp + warmth * 0.25f - b.wet * 0.05f;
            return AirTemp(s, d, at ?? s.minutes) + warmth + bonus - b.wet * 0.05f;
        }

        /// <summary>The weather works on a body for a while: rain soaks it, cold air chills it, heat bakes it.</summary>
        static void Expose(GameState s, GameData d, Body b, float warmth, float h, float bonus, int at)
        {
            var w = WeatherAt(s, d, at);
            float hardy = b == s.body && HasPerk(s, d, "hardy") ? HardyWeather : 1f;   // Hardy: used to all weathers
            if (Sheltered(s, d)) b.wet -= 40 * h;
            else if (w != null && w.wet > 0) b.wet += w.wet * h * hardy;
            else b.wet -= (w?.dry ?? 10) * h;
            b.wet = Math.Clamp(b.wet, 0, 100);

            float felt = Felt(s, d, b, warmth, bonus, at);
            if (felt < Comfortable) b.cold += (Comfortable - felt) * 1.5f * h * hardy;
            else b.cold -= Math.Max(10, (felt - Comfortable) * 4f) * h;
            if (felt > TooHot) b.heat += (felt - TooHot) * 4f * h;
            else b.heat -= 15 * h;
            b.cold = Math.Clamp(b.cold, 0, 100);
            b.heat = Math.Clamp(b.heat, 0, 100);
        }

        // ------------------------------------------------------------------ wounds and ailments

        public static bool Has(Body b, string id) => b.afflictions.Any(a => a.id == id);

        /// <summary>Every treatment step done (rest included, which a night's sleep does)?</summary>
        public static bool Treated(Affliction a, AfflictionDef def) => def.treat.All(t => a.done.Contains(t));

        /// <summary>Bleeding stops once it's bandaged or stitched, or fully treated.</summary>
        public static bool Bleeding(Affliction a, AfflictionDef def) =>
            (def.bleed > 0 || def.deadline > 0) && !Treated(a, def) && !a.done.Contains("bandage") && !a.done.Contains("stitch") && !a.done.Contains("medicine");

        public static float FightPower(Body b, GameData d)
        {
            float p = 1;
            foreach (var a in b.afflictions) p *= d.Affliction(a.id)?.fight ?? 1;
            if (b.cold >= ColdAt) p *= 0.85f;
            if (b.heat >= HotAt) p *= 0.9f;
            return p;
        }
        public static float StaminaPower(Body b, GameData d)
        {
            float p = 1;
            foreach (var a in b.afflictions) p *= d.Affliction(a.id)?.stamina ?? 1;
            return p;
        }
        public static bool NoRun(Body b, GameData d) => b.afflictions.Any(a => d.Affliction(a.id)?.noRun == true);
        public static bool NoFight(Body b, GameData d) => b.afflictions.Any(a => d.Affliction(a.id)?.noFight == true);
        public static int Fumble(Body b, GameData d) => b.afflictions.Select(a => d.Affliction(a.id)?.fumble ?? 0).DefaultIfEmpty(0).Max();
        public static float Slow(Body b, GameData d) => b.afflictions.Select(a => d.Affliction(a.id)?.slow ?? 1).DefaultIfEmpty(1).Max();

        /// <summary>How a body is doing, in words: cold, wet, hot, and each wound.</summary>
        public static List<string> BodyStates(Body b, GameData d)
        {
            var c = new List<string>();
            if (b.cold >= ColdAt && !Has(b, "hypothermia")) c.Add("cold");
            if (b.heat >= HotAt && !Has(b, "heatstroke")) c.Add("hot");
            if (b.wet >= WetAt) c.Add("wet");
            foreach (var a in b.afflictions) c.Add((d.Affliction(a.id)?.name ?? a.id).ToLowerInvariant());
            return c;
        }

        /// <summary>Gives someone a wound or ailment, unless they already have it. who is null for you.</summary>
        public static bool Afflict(GameState s, GameData d, Body b, string id, string who)
        {
            var def = d.Affliction(id) ?? throw new Exception("Unknown affliction '" + id + "'");
            if (Has(b, id)) return false;
            b.afflictions.Add(new Affliction { id = id });
            s.Toasts.Add(who == null ? "You've got: " + def.name + "." : who + " has: " + def.name + ".");
            return true;
        }

        /// <summary>
        /// Time passes for a body: weather, then each wound bleeds, heals, worsens or kills.
        /// Returns what it costs in health, energy and thirst; sets died if a wound ran out its deadline.
        /// </summary>
        static (float hp, float energy, float thirst) AdvanceBody(GameState s, GameData d, Body b, float warmth, float h, string who,
                                                                    bool sleeping, float bonus, int at, out string killedBy)
        {
            killedBy = null;
            if (h <= 0) return (0, 0, 0);
            if (!sleeping) Expose(s, d, b, warmth, h, bonus, at);

            float hp = 0, energy = 0, thirst = 0;
            if (b.cold >= ColdAt) energy += 1f * h;
            if (b.heat >= HotAt) thirst += 3f * h;
            if (b.cold >= 100) Afflict(s, d, b, "hypothermia", who);
            if (b.heat >= 100) Afflict(s, d, b, "heatstroke", who);

            float rate = 1 + FirstAidPerLevel * SkillOf(s, "first_aid");
            foreach (var a in b.afflictions.ToList())
            {
                var def = d.Affliction(a.id);
                if (def == null) { b.afflictions.Remove(a); continue; }
                a.hours += h;
                string name = def.name.ToLowerInvariant();
                string whose = who == null ? "Your " : who + "'s ";

                if (def.cure == "warm" && b.cold < 30) { b.afflictions.Remove(a); s.Toasts.Add(who == null ? "You're warm again." : who + " is warm again."); continue; }
                if (def.cure == "cool" && b.heat < 30) { b.afflictions.Remove(a); s.Toasts.Add(who == null ? "You've cooled down." : who + " has cooled down."); continue; }

                bool treated = Treated(a, def);
                if (treated && def.heal > 0)
                {
                    a.healed += h * rate * (b == s.body && HasPerk(s, d, "healer") ? HealerRate : 1f);
                    if (a.healed >= def.heal) { b.afflictions.Remove(a); s.Toasts.Add(whose + name + " has healed."); continue; }
                }
                else if (!treated && def.selfHeal > 0 && a.hours >= def.selfHeal)
                {
                    b.afflictions.Remove(a); s.Toasts.Add(whose + name + " has passed."); continue;
                }
                if (!treated && def.worsen != "" && a.hours >= def.worsenAfter && (def.worsenUnless == "" || !a.done.Contains(def.worsenUnless)))
                {
                    b.afflictions.Remove(a);
                    s.Toasts.Add(whose + name + " has turned bad.");
                    Afflict(s, d, b, def.worsen, who);
                    continue;
                }
                if (def.deadline > 0 && Bleeding(a, def) && a.hours >= def.deadline) { killedBy = def.name; return (hp, energy, thirst); }

                if (Bleeding(a, def)) hp += def.bleed * h;
                if (!treated || def.cure != "") energy += def.drain * h;
                thirst += def.thirst * h;
                if (def.cure == "warm" || def.cure == "cool") hp += 3f * h;
            }
            return (hp, energy, thirst);
        }

        static int SkillOf(GameState s, string id) => s.skills.FirstOrDefault(k => k.id == id)?.level ?? 0;

        /// <summary>Your body over a stretch of waking time.</summary>
        static void AdvanceYourBody(GameState s, GameData d, float h, bool sleeping, float bonus)
        {
            int at = s.minutes;
            var (hp, energy, thirst) = AdvanceBody(s, d, s.body, Warmth(s, d), h, null, sleeping, bonus, at, out var killedBy);
            s.hp -= hp;
            s.energy -= energy;
            s.thirst -= thirst;
            if (killedBy != null) { s.hp = 0; s.deathCause = "Your " + killedBy.ToLowerInvariant() + " killed you."; }
            else if (s.hp <= 0 && string.IsNullOrEmpty(s.deathCause))
            {
                var worst = s.body.afflictions.Select(a => d.Affliction(a.id)).FirstOrDefault(a => a != null && (a.bleed > 0 || a.cure != ""));
                if (worst != null) s.deathCause = "You died of your " + worst.name.ToLowerInvariant() + ".";
            }
        }

        /// <summary>
        /// A night's sleep, for a body: rest counts as treatment, and where you slept decides how warm and dry you wake.
        /// shelter: bed (warm and dry), fire (a camp with a fire), none (out in the open).
        /// </summary>
        static void SleepBody(GameState s, GameData d, Body b, float warmth, int start, int minutes, string shelter, string who)
        {
            foreach (var a in b.afflictions)
                if (d.Affliction(a.id)?.treat.Contains("rest") == true && !a.done.Contains("rest")) a.done.Add("rest");
            if (shelter == "bed") { b.cold = 0; b.heat = 0; b.wet = 0; }
            else if (shelter == "fire") { b.cold = Math.Min(b.cold, 15); b.wet = 0; b.heat = Math.Max(0, b.heat - 30); }
            else
            {
                // curled up out of the wind, it's a few degrees better than standing in it, but a long night
                for (int m = 0; m < minutes; m += 60)
                    Expose(s, d, b, warmth, Math.Min(60, minutes - m) / 60f, 4, start + m);
            }
            if (shelter != "bed" && (b.cold >= 60 || (b.wet >= 60 && shelter == "none"))) Afflict(s, d, b, "chill", who);
        }
    }
}
