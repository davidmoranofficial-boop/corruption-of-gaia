using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    /// <summary>
    /// Walking a road, stop by stop. Each leg between stops is a few steps; each step costs time, stamina and
    /// energy depending on pace, and may turn up an event from the road's list. Some legs have a fixed story beat.
    /// </summary>
    public partial class Engine
    {
        public const string TravelId = "travel";

        public static readonly string[] Paces = { "careful", "steady", "hurried" };
        static readonly float[] PaceTime = { 1.4f, 1f, 0.65f };
        static readonly int[] PaceStamina = { 0, 0, 12 };   // walking steadily or carefully lets you get your breath back; hurrying doesn't
        static readonly int[] PaceEnergy = { 1, 1, 2 };

        public RouteDef Route => data.Route(S.travelRoute);
        public bool Travelling => S.scene == TravelId && !S.inCombat;

        public int TotalSteps(RouteDef r)
        {
            int n = 0;
            for (int i = 0; i < r.stops.Count - 1; i++) n += r.stops[i].steps;
            return n;
        }

        /// <summary>The step count at which you reach stop i.</summary>
        int StepsTo(RouteDef r, int stop)
        {
            int n = 0;
            for (int i = 0; i < stop; i++) n += r.stops[i].steps;
            return n;
        }

        /// <summary>The stop you're standing at, or -1 if you're between stops.</summary>
        public int StopIndexAt(RouteDef r, int at)
        {
            for (int i = 0; i < r.stops.Count; i++) if (StepsTo(r, i) == at) return i;
            return -1;
        }

        /// <summary>The leg (numbered by its starting stop) that a step lies on. lo is the lower of the two step counts.</summary>
        int LegAt(RouteDef r, int lo)
        {
            for (int i = 0; i < r.stops.Count - 1; i++)
                if (lo >= StepsTo(r, i) && lo < StepsTo(r, i + 1)) return i;
            return r.stops.Count - 2;
        }

        /// <summary>Where a step count sits along the route, in stops: 1.5 is halfway between the second and third stop. For the map.</summary>
        public float RoutePosition(RouteDef r, int at)
        {
            at = Math.Clamp(at, 0, TotalSteps(r));
            int leg = LegAt(r, Math.Min(at, TotalSteps(r) - 1));
            return leg + (at - StepsTo(r, leg)) / (float)r.stops[leg].steps;
        }

        /// <summary>The next stop in the direction you're facing.</summary>
        public StopDef NextStop
        {
            get
            {
                var r = Route;
                if (r == null) return null;
                float pos = RoutePosition(r, S.travelAt);
                int i = S.travelDir > 0 ? (int)Math.Floor(pos + 0.0001f) + 1 : (int)Math.Ceiling(pos - 0.0001f) - 1;
                return i >= 0 && i < r.stops.Count ? r.stops[i] : null;
            }
        }

        public string NextPlaceName
        {
            get
            {
                var n = NextStop;
                if (n != null) return n.name;
                return S.travelDir > 0 ? "the end of the road" : "the start of the road";
            }
        }

        int StepMinutes(RouteDef r, int pace)
        {
            int lo = S.travelDir > 0 ? S.travelAt : S.travelAt - 1;
            if (lo < 0 || lo >= TotalSteps(r)) return 0;
            var st = r.stops[LegAt(r, lo)];
            return Math.Max(5, (int)Math.Round(st.minutes / (float)st.steps * PaceTime[pace]));
        }

        SceneDef TravelScene()
        {
            var r = Route;
            int stop = StopIndexAt(r, S.travelAt);
            var sc = new SceneDef { id = TravelId, place = r.name };
            if (stop >= 0)
            {
                var st = r.stops[stop];
                sc.title = st.name;
                if (!string.IsNullOrEmpty(st.desc)) sc.text.Add(st.desc);
                foreach (var c in st.choices) sc.choices.Add(c);
                if (st.forage.Count > 0)
                    sc.choices.Add(new ChoiceDef { label = "Forage around here", time = 30, stamina = 6, energy = 1, train = { "agi+1" }, why = "foraging", effects = { "travel:forage" } });
            }
            else
            {
                int leg = LegAt(r, S.travelAt);
                sc.title = "On the road to " + NextPlaceName;
                if (!string.IsNullOrEmpty(r.stops[leg].leg)) sc.text.Add(r.stops[leg].leg);
            }
            if (Check("night") && !string.IsNullOrEmpty(r.night)) sc.text.Add(r.night);

            bool canGo = S.travelDir > 0 ? S.travelAt < TotalSteps(r) : S.travelAt > 0;
            if (canGo)
            {
                string[] labels = { "Go carefully", "Walk on", "Hurry" };
                for (int p = 0; p < Paces.Length; p++)
                    sc.choices.Add(new ChoiceDef
                    {
                        label = labels[p],
                        time = StepMinutes(r, p),
                        stamina = PaceStamina[p],
                        energy = PaceEnergy[p],
                        effects = { "travel:walk:" + Paces[p] },
                    });
            }
            bool atEnd = S.travelAt == 0 || S.travelAt == TotalSteps(r);
            if (atEnd) sc.choices.Add(new ChoiceDef { label = "Go back to " + r.stops[stop].name, effects = { "travel:leave" } });
            else sc.choices.Add(new ChoiceDef { label = "Turn back towards " + PrevName(), effects = { "travel:turn" } });
            sc.choices.Add(new ChoiceDef { label = "Make camp here for the night", effects = { "travel:camp" } });
            return sc;
        }

        /// <summary>The name of the place behind you.</summary>
        string PrevName()
        {
            S.travelDir = -S.travelDir;
            string n = NextPlaceName;
            S.travelDir = -S.travelDir;
            return n;
        }


        void Travel(string[] parts, StringBuilder notes, ref string redirect)
        {
            switch (parts[0])
            {
                case "start":
                {
                    // travel:start:road:0  or  travel:start:road:end
                    var r = data.Route(parts[1]) ?? throw new Exception("Unknown road '" + parts[1] + "'");
                    S.travelRoute = r.id;
                    bool fromEnd = parts.Length > 2 && parts[2] == "end";
                    S.travelAt = fromEnd ? TotalSteps(r) : 0;
                    S.travelDir = fromEnd ? -1 : 1;
                    redirect = TravelId;
                    return;
                }
                case "resume":
                    redirect = TravelId;
                    return;
                case "turn":
                    S.travelDir = -S.travelDir;
                    notes.Append("You turn round and face back towards " + NextPlaceName + ".");
                    redirect = TravelId;
                    return;
                case "camp":
                    Sleep("camp", notes);
                    redirect = TravelId;
                    return;
                case "forage":
                {
                    var st = Route.stops[StopIndexAt(Route, S.travelAt)];
                    if (Roll(1, 100) <= 55 + S.stats[Stat.Agi] * 3)
                    {
                        string id = st.forage[Roll(0, st.forage.Count - 1)];
                        S.AddItem(id, 1);
                        S.Toasts.Add("+1 " + data.Item(id).name);
                        notes.Append("You search the verges and turn up some " + data.Item(id).name.ToLowerInvariant() + ".");
                    }
                    else notes.Append("You search for half an hour and find nothing worth carrying.");
                    redirect = TravelId;
                    return;
                }
                case "leave":
                    Leave(Route, notes, ref redirect);
                    return;
                case "walk":
                    Walk(Array.IndexOf(Paces, parts[1]), notes, ref redirect);
                    return;
                default:
                    throw new Exception("Unknown travel effect '" + string.Join(":", parts) + "'");
            }
        }

        void Leave(RouteDef r, StringBuilder notes, ref string redirect)
        {
            var ends = S.travelAt <= 0 ? r.start : r.end;
            var target = ends.FirstOrDefault(c => All(c.when)) ?? ends.Last();
            S.travelRoute = "";
            S.travelAt = 0;
            S.travelDir = 1;
            redirect = target.scene;
        }

        void Walk(int pace, StringBuilder notes, ref string redirect)
        {
            var r = Route;
            int from = S.travelAt;
            S.travelAt += S.travelDir;
            redirect = TravelId;
            if (pace == 0) Rules.Train(S, Stat.Int, 0.3f, "keeping your eyes open");
            if (pace == 2) Rules.Train(S, Stat.End, 0.5f, "pushing on");

            // Off either end of the road
            if (S.travelAt >= TotalSteps(r) || S.travelAt <= 0)
            {
                Leave(r, notes, ref redirect);
                return;
            }

            // A fixed story beat on this leg
            var leg = r.stops[LegAt(r, Math.Min(from, S.travelAt))];
            if (!string.IsNullOrEmpty(leg.beat) && leg.beatWhen.Count > 0 && All(leg.beatWhen))
            {
                redirect = leg.beat;
                return;
            }

            int stop = StopIndexAt(r, S.travelAt);
            if (stop >= 0)
            {
                notes.Append("You reach " + Lower(r.stops[stop].name) + ".");
                return;
            }

            // Something on the road?
            if (Roll(1, 100) > r.eventChance) return;
            bool night = Check("night");
            var pool = new List<(TravelEvent ev, float w)>();
            foreach (var ev in r.events)
            {
                if (ev.once && S.Has("seen:" + ev.scene)) continue;
                if (!All(ev.when)) continue;
                float w = Math.Max(1, ev.weight);
                if (ev.kind == "find") w *= pace == 0 ? 1.8f : pace == 2 ? 0.4f : 1f;
                if (ev.kind == "find" && night) w *= 0.5f;
                if (ev.kind == "danger") w *= (pace == 0 ? 0.5f : pace == 2 ? 1.6f : 1f) * (night ? 2f : 1f);
                pool.Add((ev, w));
            }
            if (pool.Count == 0) return;
            float total = pool.Sum(p => p.w);
            float pick = Roll(0, 9999) / 10000f * total;
            foreach (var (ev, w) in pool)
            {
                pick -= w;
                if (pick > 0) continue;
                if (!S.Has("seen:" + ev.scene)) S.flags.Add("seen:" + ev.scene);
                redirect = ev.scene;
                return;
            }
        }
    }
}
