using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    /// <summary>
    /// The region map. You walk it tile by tile, anywhere you like. Each tile costs time, stamina and energy
    /// depending on the ground and your pace, may turn up an event, and lights up the map around you.
    /// Places show on your map once you've seen them; hidden ones only when you walk right next to them.
    /// </summary>
    public partial class Engine
    {
        public const string TravelId = "travel";
        public const int TileMinutes = 12;     // one road tile at a steady pace

        public static readonly string[] Paces = { "careful", "steady", "hurried" };
        public static readonly string[] PaceNames = { "Careful", "Steady", "Hurried" };
        static readonly float[] PaceTime = { 1.4f, 1f, 0.65f };
        static readonly int[] PaceStamina = { 0, 0, 6 };    // walking steadily or carefully lets you get your breath back; hurrying doesn't
        static readonly float[] PaceEnergy = { 0.75f, 1f, 1.6f };   // energy per 20 minutes of walking

        /// <summary>The map icons there are (Resources/Sprites/Map), one per kind of place.</summary>
        public static readonly string[] PlaceKinds = { "hut", "village", "town", "quarry", "camp", "cave", "ruin", "stones", "cove", "blight", "spot" };

        public RegionDef Region => data.Region(S.region);
        public bool Travelling => S.scene == TravelId && !S.inCombat;

        // ------------------------------------------------------------------ the grid

        public static int Width(RegionDef r) => r.rows.Count == 0 ? 0 : r.rows[0].Length;
        public static int Height(RegionDef r) => r.rows.Count;
        public static bool InBounds(RegionDef r, int x, int y) => x >= 0 && y >= 0 && y < r.rows.Count && x < r.rows[y].Length;

        public TerrainDef TerrainAt(RegionDef r, int x, int y) => InBounds(r, x, y) ? data.Terrain(r.rows[y][x]) : null;

        public bool Passable(RegionDef r, int x, int y)
        {
            var t = TerrainAt(r, x, y);
            return t != null && t.cost > 0;
        }

        /// <summary>Whether you've had this tile in sight.</summary>
        public bool Known(int x, int y)
        {
            var r = Region;
            if (r == null || !InBounds(r, x, y)) return false;
            int i = y * Width(r) + x;
            return i < S.fog.Length && S.fog[i] == '1';
        }

        void EnsureFog(RegionDef r)
        {
            int n = Width(r) * Height(r);
            if (S.fog == null || S.fog.Length != n) S.fog = new string('0', n);
        }

        /// <summary>Lights up the map within radius tiles of a point.</summary>
        void Reveal(int cx, int cy, int radius)
        {
            var r = Region;
            if (r == null) return;
            EnsureFog(r);
            var fog = S.fog.ToCharArray();
            int w = Width(r);
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                    if (InBounds(r, x, y) && (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius + radius)
                        fog[y * w + x] = '1';
            S.fog = new string(fog);
        }

        /// <summary>Standing on a road or track, you can see it run on further than the land around it.</summary>
        void RevealRoad(int cx, int cy, int radius)
        {
            var r = Region;
            if (r == null || "=:b".IndexOf(r.rows[cy][cx]) < 0) return;
            var fog = S.fog.ToCharArray();
            int w = Width(r);
            for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                    if (InBounds(r, x, y) && "=:b".IndexOf(r.rows[y][x]) >= 0) fog[y * w + x] = '1';
            S.fog = new string(fog);
        }

        /// <summary>How far you can see from where you stand: further from hills, less in woods and at night.</summary>
        public int Sight()
        {
            var t = TerrainAt(Region, S.mapX, S.mapY);
            int s = (t?.sight ?? 2) - (Check("night") ? 1 : 0);
            return Math.Max(1, s);
        }

        // ------------------------------------------------------------------ places

        public string PlaceState(PlaceDef p)
        {
            if (S.Has("place_visited:" + p.id)) return "visited";
            if (S.Has("place_seen:" + p.id)) return "seen";
            if (p.rumourWhen.Count > 0 && All(p.rumourWhen)) return "rumoured";
            return "unknown";
        }

        public bool Found(PlaceDef p) => S.Has("place_seen:" + p.id) || S.Has("place_visited:" + p.id);

        /// <summary>The place on a tile, if you know about it.</summary>
        public PlaceDef PlaceAt(int x, int y)
        {
            var r = Region;
            if (r == null) return null;
            foreach (var p in r.places) if (p.x == x && p.y == y && Found(p)) return p;
            return null;
        }

        public PlaceDef Here => Region == null ? null : PlaceAt(S.mapX, S.mapY);

        /// <summary>Places you've found or heard of, nearest first.</summary>
        public List<(PlaceDef place, string state)> KnownPlaces()
        {
            var list = new List<(PlaceDef, string)>();
            var r = Region;
            if (r == null) return list;
            foreach (var p in r.places)
            {
                string st = PlaceState(p);
                if (st != "unknown") list.Add((p, st));
            }
            return list.OrderBy(t => Math.Max(Math.Abs(t.Item1.x - S.mapX), Math.Abs(t.Item1.y - S.mapY))).ToList();
        }

        /// <summary>Checks every place you haven't found yet against what you can see now. Returns how many were new.</summary>
        int Discover(StringBuilder notes)
        {
            var r = Region;
            int found = 0;
            if (r == null) return 0;
            foreach (var p in r.places)
            {
                if (Found(p)) continue;
                int dx = p.x - S.mapX, dy = p.y - S.mapY;
                int near = Math.Max(Math.Abs(dx), Math.Abs(dy));
                bool seen = p.hidden
                    ? near <= (S.pace == 0 ? 2 : 1)
                    : Known(p.x, p.y) || dx * dx + dy * dy <= p.farSight * p.farSight;
                if (!seen) continue;
                S.flags.Add("place_seen:" + p.id);
                found++;
                S.Toasts.Add("New place: " + p.name);
                if (p.x != S.mapX || p.y != S.mapY)
                    notes.Append((notes.Length > 0 ? " " : "") + (p.hidden ? "You find " : "You spot ") + Lower(p.name) + ".");
                Rules.Train(S, Stat.Int, 0.4f, "finding new places");
            }
            return found;
        }

        /// <summary>Puts you on the map at a place (leaving a village, setting off from the shack).</summary>
        void MoveTo(string placeId)
        {
            var (r, p) = data.Place(placeId);
            if (p == null) throw new Exception("No place called '" + placeId + "'");
            if (S.region != r.id) { S.region = r.id; S.fog = ""; }
            S.mapX = p.x;
            S.mapY = p.y;
            EnsureFog(r);
            if (!S.Has("place_seen:" + p.id)) S.flags.Add("place_seen:" + p.id);
            if (!S.Has("place_visited:" + p.id)) S.flags.Add("place_visited:" + p.id);
            Reveal(p.x, p.y, Sight());
            RevealRoad(p.x, p.y, Sight() + 3);
            Discover(new StringBuilder());
        }

        // ------------------------------------------------------------------ costs and routes

        public int StepMinutes(int x, int y, bool diagonal, int pace = -1)
        {
            if (pace < 0) pace = S.pace;
            var t = TerrainAt(Region, x, y);
            float cost = t == null || t.cost <= 0 ? 2 : t.cost;
            return Math.Max(3, (int)Math.Round(TileMinutes * cost * PaceTime[pace] * (diagonal ? 1.4f : 1f)));
        }

        /// <summary>The energy a step onto a tile takes, over and above the time passing.</summary>
        public float StepEnergy(int x, int y)
        {
            bool diag = x != S.mapX && y != S.mapY;
            return StepMinutes(x, y, diag) / 20f * PaceEnergy[S.pace];
        }

        /// <summary>The walk onto a neighbouring tile, as a choice: its time, stamina and energy, ready for Choose.</summary>
        public ChoiceDef StepChoice(int x, int y)
        {
            var r = Region;
            int dx = x - S.mapX, dy = y - S.mapY;
            if (r == null || Math.Abs(dx) > 1 || Math.Abs(dy) > 1 || (dx == 0 && dy == 0) || !CanStep(r, S.mapX, S.mapY, x, y, true)) return null;
            var t = TerrainAt(r, x, y);
            int p = S.pace;
            return new ChoiceDef
            {
                label = "Walk",
                time = StepMinutes(x, y, dx != 0 && dy != 0),
                stamina = PaceStamina[p] + t.stamina,
                effects = { "travel:step:" + x + ":" + y },
            };
        }

        /// <summary>
        /// Whether you can walk from one tile to its neighbour. With strict off, ground you haven't seen counts as walkable
        /// (for planning a route into the fog). No cutting corners past water.
        /// </summary>
        bool CanStep(RegionDef r, int x0, int y0, int x1, int y1, bool strict)
        {
            bool Open(int x, int y) => InBounds(r, x, y) && (Passable(r, x, y) || (!strict && !Known(x, y)));
            if (!Open(x1, y1)) return false;
            if (x0 != x1 && y0 != y1 && (!Open(x1, y0) || !Open(x0, y1))) return false;
            return true;
        }

        /// <summary>
        /// The quickest way from where you are to a tile, over ground you know (and through the fog where you don't).
        /// Returns the tiles to walk, not counting where you stand, or null if there's no way there. minutes is a guess.
        /// </summary>
        public List<(int x, int y)> PathTo(int tx, int ty, out int minutes)
        {
            minutes = 0;
            var r = Region;
            if (r == null || !InBounds(r, tx, ty)) return null;
            if (Known(tx, ty) && !Passable(r, tx, ty)) return null;
            if (tx == S.mapX && ty == S.mapY) return new List<(int, int)>();
            int w = Width(r), h = Height(r);
            var dist = new float[w * h];
            var prev = new int[w * h];
            for (int i = 0; i < dist.Length; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            int start = S.mapY * w + S.mapX, goal = ty * w + tx;
            dist[start] = 0;
            var open = new SortedSet<(float d, int i)>();
            open.Add((0, start));
            while (open.Count > 0)
            {
                var (d, i) = open.Min;
                open.Remove(open.Min);
                if (i == goal) break;
                if (d > dist[i]) continue;
                int x = i % w, y = i / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = x + dx, ny = y + dy;
                        if (!CanStep(r, x, y, nx, ny, false)) continue;
                        bool diag = dx != 0 && dy != 0;
                        float c = Known(nx, ny) ? StepMinutes(nx, ny, diag) : TileMinutes * 1.6f * PaceTime[S.pace] * (diag ? 1.4f : 1f);
                        int j = ny * w + nx;
                        if (d + c < dist[j])
                        {
                            open.Remove((dist[j], j));
                            dist[j] = d + c;
                            prev[j] = i;
                            open.Add((dist[j], j));
                        }
                    }
            }
            if (prev[goal] < 0) return null;
            var path = new List<(int, int)>();
            for (int i = goal; i != start; i = prev[i]) path.Add((i % w, i / w));
            path.Reverse();
            minutes = (int)Math.Round(dist[goal]);
            return path;
        }

        // ------------------------------------------------------------------ the scene

        SceneDef TravelScene()
        {
            var r = Region;
            var here = Here;
            var t = TerrainAt(r, S.mapX, S.mapY);
            var sc = new SceneDef { id = TravelId, place = r.name };
            if (here != null)
            {
                sc.title = here.name;
                if (!string.IsNullOrEmpty(here.desc)) sc.text.Add(here.desc);
                if (here.enter.Count > 0)
                    sc.choices.Add(new ChoiceDef { label = string.IsNullOrEmpty(here.enterLabel) ? "Go into " + here.name : here.enterLabel, effects = { "travel:enter" } });
                foreach (var c in here.choices) sc.choices.Add(c);
            }
            else
            {
                sc.title = string.IsNullOrEmpty(t?.title) ? "On the move" : t.title;
                if (!string.IsNullOrEmpty(t?.desc)) sc.text.Add(t.desc);
            }
            if (Check("night") && !string.IsNullOrEmpty(r.night)) sc.text.Add(r.night);
            if (ForageList().Count > 0)
                sc.choices.Add(new ChoiceDef { label = "Forage around here", time = 30, stamina = 6, energy = 1, train = { "agi+1" }, why = "foraging", effects = { "travel:forage" } });
            sc.choices.Add(new ChoiceDef { label = "Make camp here for the night", effects = { "travel:camp" } });
            return sc;
        }

        List<string> ForageList()
        {
            var here = Here;
            if (here != null && here.forage.Count > 0) return here.forage;
            return TerrainAt(Region, S.mapX, S.mapY)?.forage ?? new List<string>();
        }

        /// <summary>Changes walking pace. Not an action: it takes no time.</summary>
        public void SetPace(int pace)
        {
            S.pace = Math.Clamp(pace, 0, Paces.Length - 1);
            S.result = "";
            S.repeat = true;
        }

        // ------------------------------------------------------------------ effects

        void Travel(string[] parts, StringBuilder notes, ref string redirect)
        {
            switch (parts[0])
            {
                case "at":
                    // travel:at:<place>  puts you on the map there
                    MoveTo(parts[1]);
                    redirect = TravelId;
                    return;
                case "start":
                    // Old effect from before the map: the coast road from either end
                    MoveTo(parts.Length > 2 && parts[2] == "end" ? "skelmed" : "shack");
                    redirect = TravelId;
                    return;
                case "resume":
                    redirect = TravelId;
                    return;
                case "pace":
                    S.pace = Math.Max(0, Array.IndexOf(Paces, parts[1]));
                    redirect = TravelId;
                    return;
                case "reveal":
                    Reveal(S.mapX, S.mapY, int.Parse(parts[1]));
                    Discover(notes);
                    return;
                case "chart":
                {
                    // A map of the region: every road, track and crossing, and every place that isn't hidden.
                    var r = Region;
                    if (r == null) return;
                    EnsureFog(r);
                    var fog = S.fog.ToCharArray();
                    int w = Width(r);
                    for (int y = 0; y < Height(r); y++)
                        for (int x = 0; x < w; x++)
                            if ("=:b".IndexOf(r.rows[y][x]) >= 0)
                                for (int yy = y - 1; yy <= y + 1; yy++)
                                    for (int xx = x - 1; xx <= x + 1; xx++)
                                        if (InBounds(r, xx, yy)) fog[yy * w + xx] = '1';
                    S.fog = new string(fog);
                    foreach (var p in r.places)
                        if (!p.hidden && !Found(p)) { S.flags.Add("place_seen:" + p.id); S.Toasts.Add("New place: " + p.name); }
                    return;
                }
                case "enter":
                {
                    var here = Here ?? throw new Exception("Not at a place");
                    var target = here.enter.FirstOrDefault(c => All(c.when)) ?? here.enter.Last();
                    redirect = target.scene;
                    return;
                }
                case "camp":
                    Sleep("camp", notes);
                    redirect = TravelId;
                    return;
                case "forage":
                {
                    var list = ForageList();
                    if (list.Count > 0 && Roll(1, 100) <= 55 + S.stats[Stat.Agi] * 3)
                    {
                        string id = list[Roll(0, list.Count - 1)];
                        S.AddItem(id, 1);
                        S.Toasts.Add("+1 " + data.Item(id).name);
                        notes.Append("You search around and turn up some " + data.Item(id).name.ToLowerInvariant() + ".");
                    }
                    else notes.Append("You search for half an hour and find nothing worth carrying.");
                    redirect = TravelId;
                    return;
                }
                case "step":
                    Step(int.Parse(parts[1]), int.Parse(parts[2]), notes, ref redirect);
                    return;
                default:
                    throw new Exception("Unknown travel effect '" + string.Join(":", parts) + "'");
            }
        }

        void Step(int x, int y, StringBuilder notes, ref string redirect)
        {
            var r = Region;
            redirect = TravelId;
            S.energy -= StepEnergy(x, y);
            S.mapX = x;
            S.mapY = y;
            int pace = S.pace;
            if (pace == 0) Rules.Train(S, Stat.Int, 0.15f, "keeping your eyes open");
            if (pace == 2) Rules.Train(S, Stat.End, 0.25f, "pushing on");

            var t = TerrainAt(r, x, y);
            if (t.corruption > 0) S.corruption += t.corruption * Rules.WardFactor(S, data);

            Reveal(x, y, Sight());
            RevealRoad(x, y, Sight() + 3);
            int found = Discover(notes);

            // Arriving somewhere
            var here = Here;
            if (here != null)
            {
                if (!S.Has("place_visited:" + here.id)) S.flags.Add("place_visited:" + here.id);
                notes.Append((notes.Length > 0 ? " " : "") + "You reach " + Lower(here.name) + ".");
                return;
            }

            // A fixed story beat waiting here
            foreach (var b in r.beats)
                if (Math.Max(Math.Abs(b.x - x), Math.Abs(b.y - y)) <= b.radius && All(b.when))
                {
                    redirect = b.scene;
                    return;
                }

            if (found > 0) return;

            // Something on the way?
            if (Roll(1, 100) > r.eventChance * t.danger) return;
            bool night = Check("night");
            char ground = r.rows[y][x];
            var pool = new List<(TravelEvent ev, float w)>();
            foreach (var ev in r.events)
            {
                if (ev.once && S.Has("seen:" + ev.scene)) continue;
                if (!string.IsNullOrEmpty(ev.terrain) && ev.terrain.IndexOf(ground) < 0) continue;
                if (ev.area.Count == 4 && (x < ev.area[0] || y < ev.area[1] || x > ev.area[2] || y > ev.area[3])) continue;
                if (!All(ev.when)) continue;
                float w = Math.Max(1, ev.weight);
                if (ev.kind == "find") w *= pace == 0 ? 1.8f : pace == 2 ? 0.4f : 1f;
                if (ev.kind == "find" && night) w *= 0.5f;
                if (ev.kind == "danger") w *= (pace == 0 ? 0.5f : pace == 2 ? 1.6f : 1f) * (night ? 2f : 1f) * t.danger;
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

        // ------------------------------------------------------------------ old saves

        /// <summary>Saves from before the map, made on the coast road, are put at the nearest of its old stops.</summary>
        void MigrateRoad()
        {
            if (S.region != "" || string.IsNullOrEmpty(S.travelRoute)) return;
            string[] stops = { "shack", "pools", "headland", "stream", "skelmed" };
            int[] at = { 0, 2, 4, 7, 9 };
            int best = 0;
            for (int i = 1; i < at.Length; i++) if (Math.Abs(at[i] - S.travelAt) < Math.Abs(at[best] - S.travelAt)) best = i;
            S.travelRoute = "";
            S.travelAt = 0;
            S.travelDir = 1;
            MoveTo(stops[best]);
        }
    }
}
