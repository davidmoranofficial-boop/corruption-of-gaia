using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    /// <summary>
    /// The walkable world: top-down areas you walk round one tile at a time, like the old console RPGs.
    /// Each step takes a little time. Fights come at random as you walk, the old Final Fantasy way, and other
    /// things turn up now and then. Facing a person, sign or thing and pressing to use it opens its scene.
    /// "travel" is the scene id for being out in the world.
    /// </summary>
    public partial class Engine
    {
        public const string TravelId = "travel";
        public static readonly string[] Facings = { "down", "up", "left", "right" };

        public AreaDef Area => data.Area(S.area);
        public bool Travelling => S.scene == TravelId && !S.inCombat;

        public static (int dx, int dy) Dir(string facing) => facing switch
        {
            "up" => (0, -1),
            "left" => (-1, 0),
            "right" => (1, 0),
            _ => (0, 1),
        };

        public static int Width(AreaDef a) => a.rows.Count == 0 ? 0 : a.rows[0].Length;
        public static int Height(AreaDef a) => a.rows.Count;
        public static bool InBounds(AreaDef a, int x, int y) => x >= 0 && y >= 0 && y < a.rows.Count && x < a.rows[y].Length;

        public char TileAt(int x, int y)
        {
            var a = Area;
            return a != null && InBounds(a, x, y) ? a.rows[y][x] : '\0';
        }

        /// <summary>Everything in the area that's there right now.</summary>
        public IEnumerable<WorldObj> Objects()
        {
            var a = Area;
            if (a == null) yield break;
            foreach (var o in a.objects)
                if (!(o.once && S.Has("used:" + o.id)) && All(o.when)) yield return o;
        }

        public WorldObj ObjectAt(int x, int y) => Objects().FirstOrDefault(o => o.x == x && o.y == y);

        /// <summary>Whether you can't walk onto a tile: off the edge, solid ground, or something standing there.</summary>
        public bool Blocked(int x, int y)
        {
            var a = Area;
            if (a == null || !InBounds(a, x, y)) return true;
            var t = data.Tile(a.rows[y][x]);
            if (t == null || t.solid) return true;
            return ObjectAt(x, y) != null;
        }

        /// <summary>Turns to face a way. Not an action: it takes no time.</summary>
        public void Face(string facing)
        {
            if (Array.IndexOf(Facings, facing) >= 0) S.facing = facing;
        }

        /// <summary>A step the way you're facing, as a choice ready for Choose, or null if something's in the way.</summary>
        public ChoiceDef StepChoice(bool run = false)
        {
            var a = Area;
            if (a == null) return null;
            var (dx, dy) = Dir(S.facing);
            if (Blocked(S.ax + dx, S.ay + dy)) return null;
            return new ChoiceDef
            {
                label = run ? "Run" : "Walk",
                time = run ? Math.Max(1, a.minutes / 2) : a.minutes,
                stamina = run ? 2 : 0,
                effects = { "travel:step" + (run ? ":run" : "") },
            };
        }

        /// <summary>Using what's in front of you (talking, reading, opening), or null if there's nothing there.</summary>
        public ChoiceDef UseChoice()
        {
            var a = Area;
            if (a == null) return null;
            var (dx, dy) = Dir(S.facing);
            int x = S.ax + dx, y = S.ay + dy;
            var o = ObjectAt(x, y);
            if (o != null && (!string.IsNullOrEmpty(o.scene) || !string.IsNullOrEmpty(o.text)))
                return new ChoiceDef { label = o.kind == "npc" ? "Talk" : o.kind == "sign" ? "Read" : "Look", effects = { "travel:use:" + o.id } };
            char t = TileAt(x, y);
            var use = a.uses.FirstOrDefault(u => !string.IsNullOrEmpty(u.tile) && u.tile[0] == t);
            if (use != null) return new ChoiceDef { label = "Look", effects = { "travel:usetile:" + use.tile } };
            return null;
        }

        /// <summary>The thing you're facing, if anything (for the "press to use" prompt).</summary>
        public string UseLabel() => UseChoice()?.label;

        /// <summary>
        /// The shortest walk to a tile, as the facings to step in, or null if there's no way there. Used by the tests
        /// (and handy for clicking to walk later).
        /// </summary>
        public List<string> PathTo(int tx, int ty)
        {
            var a = Area;
            if (a == null || !InBounds(a, tx, ty)) return null;
            int w = Width(a), h = Height(a);
            var prev = new int[w * h];
            var how = new string[w * h];
            for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            int start = S.ay * w + S.ax, goal = ty * w + tx;
            prev[start] = -1;
            var q = new Queue<int>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                int i = q.Dequeue();
                if (i == goal) break;
                int x = i % w, y = i / w;
                foreach (var f in Facings)
                {
                    var (dx, dy) = Dir(f);
                    int nx = x + dx, ny = y + dy;
                    if (!InBounds(a, nx, ny)) continue;
                    int j = ny * w + nx;
                    if (prev[j] != -2) continue;
                    if (j != goal && Blocked(nx, ny)) continue;
                    if (j == goal && data.Tile(a.rows[ny][nx])?.solid != false) continue;
                    prev[j] = i;
                    how[j] = f;
                    q.Enqueue(j);
                }
            }
            if (prev[goal] == -2) return null;
            var path = new List<string>();
            for (int i = goal; i != start; i = prev[i]) path.Add(how[i]);
            path.Reverse();
            return path;
        }

        // ------------------------------------------------------------------ the scene

        /// <summary>Out in the world. Its only choices are the ones in the actions menu.</summary>
        SceneDef TravelScene()
        {
            var a = Area;
            var sc = new SceneDef { id = TravelId, place = a?.name, title = a?.name };
            if (a != null && a.camp && !a.indoor)
                sc.choices.Add(new ChoiceDef { label = "Make camp here for the night", effects = { "travel:camp" } });
            return sc;
        }

        /// <summary>Puts you in the world at a named spot (leaving a building, setting off from the shack).</summary>
        void MoveTo(string spotId)
        {
            var (a, p) = data.Spot(spotId);
            if (p == null) throw new Exception("No spot called '" + spotId + "'");
            S.area = a.id;
            S.ax = p.x;
            S.ay = p.y;
            S.facing = string.IsNullOrEmpty(p.facing) ? "down" : p.facing;
            if (!S.Has("been:" + a.id)) S.flags.Add("been:" + a.id);
            PlaceFollower();
        }

        // ------------------------------------------------------------------ effects

        void Travel(string[] parts, StringBuilder notes, ref string redirect)
        {
            switch (parts[0])
            {
                case "at":
                    // travel:at:<spot>
                    MoveTo(parts[1]);
                    redirect = TravelId;
                    return;
                case "place":
                    // travel:place:<spot>  puts you there without leaving the scene you're going to (waking up in the hut)
                    MoveTo(parts[1]);
                    return;
                case "start":
                    // From before the walkable world: the coast road from either end
                    MoveTo(parts.Length > 2 && parts[2] == "end" ? "skelmed" : "shack");
                    redirect = TravelId;
                    return;
                case "resume":
                    redirect = TravelId;
                    return;
                case "reveal":
                case "chart":
                    return;   // from the old map; nothing to do now
                case "camp":
                    Sleep("camp", notes);
                    redirect = TravelId;
                    return;
                case "step":
                    Step(parts.Length > 1 && parts[1] == "run", notes, ref redirect);
                    return;
                case "use":
                {
                    var o = Area.objects.FirstOrDefault(x => x.id == parts[1]) ?? throw new Exception("Nothing called '" + parts[1] + "'");
                    if (o.once && !S.Has("used:" + o.id)) S.flags.Add("used:" + o.id);
                    if (!string.IsNullOrEmpty(o.text)) notes.Append(Fill(o.text));
                    redirect = string.IsNullOrEmpty(o.scene) ? TravelId : o.scene;
                    return;
                }
                case "usetile":
                {
                    var use = Area.uses.First(u => u.tile == parts[1]);
                    redirect = use.scene;
                    return;
                }
                default:
                    throw new Exception("Unknown travel effect '" + string.Join(":", parts) + "'");
            }
        }

        void Step(bool run, StringBuilder notes, ref string redirect)
        {
            var a = Area;
            redirect = TravelId;
            var (dx, dy) = Dir(S.facing);
            if (Blocked(S.ax + dx, S.ay + dy)) return;
            PartyFollows(S.ax, S.ay);   // your party steps into the tiles ahead of them
            S.ax += dx;
            S.ay += dy;
            S.stepsSinceFight++;
            S.energy -= run ? 0.2f : 0.1f;   // walking tires you a little more than the time alone
            if (run) Rules.Train(S, Stat.End, 0.05f, "running");

            // Walking into somewhere
            foreach (var z in a.zones)
            {
                if (S.ax < z.x0 || S.ax > z.x1 || S.ay < z.y0 || S.ay > z.y1 || !All(z.when)) continue;
                if (!string.IsNullOrEmpty(z.to)) { MoveTo(z.to); return; }
                if (!string.IsNullOrEmpty(z.scene)) { redirect = z.scene; return; }
                var target = z.enter.FirstOrDefault(c => All(c.when));
                if (target != null) { redirect = target.scene; return; }
            }

            char ground = a.rows[S.ay][S.ax];
            bool night = Check("night");
            bool road = data.Tile(ground)?.road == true;

            // A fight, the old way: any step might be one, but never two close together
            if (S.stepsSinceFight > a.fightGap)
            {
                float chance = a.fightChance * (road ? 0.5f : 1f) * (night ? 1.5f : 1f) * (run ? 1.2f : 1f);
                if (Roll(1, 100) <= chance)
                {
                    var fight = Pick(a.events.Where(e => e.kind == "fight"), ground);
                    if (fight != null)
                    {
                        S.stepsSinceFight = 0;
                        StartCombat(fight.enemy, string.IsNullOrEmpty(fight.win) ? TravelId : fight.win, TravelId);
                        return;
                    }
                }
            }

            // Something else on the way?
            if (Roll(1, 100) > a.eventChance) return;
            var ev = Pick(a.events.Where(e => e.kind != "fight"), ground);
            if (ev == null) return;
            if (!S.Has("seen:" + ev.scene)) S.flags.Add("seen:" + ev.scene);
            redirect = ev.scene;
        }

        /// <summary>Picks one event that can happen on this ground, by weight.</summary>
        TravelEvent Pick(IEnumerable<TravelEvent> events, char ground)
        {
            bool night = Check("night");
            var pool = new List<(TravelEvent ev, float w)>();
            foreach (var ev in events)
            {
                if (ev.once && S.Has("seen:" + ev.scene)) continue;
                if (!string.IsNullOrEmpty(ev.terrain) && ev.terrain.IndexOf(ground) < 0) continue;
                if (ev.area.Count == 4 && (S.ax < ev.area[0] || S.ay < ev.area[1] || S.ax > ev.area[2] || S.ay > ev.area[3])) continue;
                if (!All(ev.when)) continue;
                float w = Math.Max(1, ev.weight);
                if (ev.kind == "find" && night) w *= 0.5f;
                pool.Add((ev, w));
            }
            if (pool.Count == 0) return null;
            float total = pool.Sum(p => p.w);
            float pick = Roll(0, 9999) / 10000f * total;
            foreach (var (ev, w) in pool)
            {
                pick -= w;
                if (pick <= 0) return ev;
            }
            return pool[pool.Count - 1].ev;
        }

        // ------------------------------------------------------------------ old saves

        /// <summary>Saves from before the walkable world, out on the road, are put at the nearest named spot.</summary>
        void MigrateRoad()
        {
            if (S.area != "" || (S.scene != TravelId && string.IsNullOrEmpty(S.travelRoute))) return;
            string[] spots = { "shack", "pools", "headland", "stream", "skelmed" };
            int[] at = { 0, 2, 4, 7, 9 };
            int best = 0;
            if (!string.IsNullOrEmpty(S.travelRoute))
            {
                for (int i = 1; i < at.Length; i++) if (Math.Abs(at[i] - S.travelAt) < Math.Abs(at[best] - S.travelAt)) best = i;
            }
            else if (S.Has("arrived")) best = 4;
            S.travelRoute = "";
            S.travelAt = 0;
            S.travelDir = 1;
            MoveTo(spots[best]);
        }
    }
}
