using System;
using System.Collections.Generic;
using System.Linq;

namespace Loran.Core
{
    /// <summary>One thing someone on your side can do this turn, for the battle screen's action list.</summary>
    public class GridAction
    {
        public string id = "", name = "", desc = "", cost = "";
        public bool enabled = true;
        public string why = "";         // why it's greyed out
        public bool targeted;           // aimed at an enemy (pick who, and where)
    }

    /// <summary>What an attack would do, shown before you commit: the chance, the damage, and why.</summary>
    public class GridPreview
    {
        public bool ok;
        public string why = "";
        public int chance;              // % to land
        public int min, max;            // damage if it lands (before a critical hit)
        public int graze;               // damage if it misses the body (a graze); 0 when a miss is a miss
        public string side = "";        // front, side, back
        public bool kills;              // the least it can do is enough
        public List<string> notes = new List<string>();   // "+25% from behind", "Hard skull: half damage"
    }

    /// <summary>The grid itself, and your side's moves and blows (the flow of the fight and the enemies are in Engine.Combat.cs).</summary>
    public partial class Engine
    {
        public const int SideBonus = 15, BackBonus = 25, PinnedBonus = 10, GangBonus = 5, GangMax = 15;
        public const int FullRange = 4;            // a bow (or sling) at full strength out to this many squares
        public const int CoverPenalty = 15;        // shooting at someone in long grass
        public const int PointBlank = 20;          // a bow at one square

        // ------------------------------------------------------------------ squares

        public bool OnGrid(int x, int y) => x >= 0 && y >= 0 && x < G.w && y < G.h;
        public char Square(int x, int y) => G.rows[y][x];
        public bool Solid(int x, int y) => !OnGrid(x, y) || data.Tile(Square(x, y))?.solid == true;
        public bool Cover(int x, int y) => OnGrid(x, y) && Square(x, y) == 'g';
        public GridUnit Unit(string id) => G.units.FirstOrDefault(u => u.id == id);
        public GridUnit At(int x, int y) => G.units.FirstOrDefault(u => !u.gone && u.x == x && u.y == y);
        static int Dist(GridUnit a, GridUnit b) => Math.Abs(a.x - b.x) + Math.Abs(a.y - b.y);
        static int Dist(int ax, int ay, int bx, int by) => Math.Abs(ax - bx) + Math.Abs(ay - by);

        static string FaceTo(int dx, int dy) =>
            Math.Abs(dx) >= Math.Abs(dy) ? (dx > 0 ? "right" : dx < 0 ? "left" : "down") : (dy > 0 ? "down" : "up");

        /// <summary>Where (ax, ay) is from someone's point of view: in front of them, to the side, or behind.</summary>
        public string SideOf(GridUnit t, int ax, int ay)
        {
            var (fx, fy) = Dir(t.facing);
            int dx = ax - t.x, dy = ay - t.y;
            int along = dx * fx + dy * fy;
            int across = Math.Abs(dx * fy - dy * fx);
            if (along > 0 && along >= across) return "front";
            if (along < 0 && -along >= across) return "back";
            return "side";
        }

        /// <summary>Is there a clear line between two squares (no trees or rocks in the way)?</summary>
        public bool Sight(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
            int x = x0, y = y0;
            while (true)
            {
                if (x == x1 && y == y1) return true;
                if ((x != x0 || y != y0) && Solid(x, y)) return false;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x += sx; }
                if (e2 <= dx) { err += dx; y += sy; }
            }
        }

        public float Hp(GridUnit u) => u.id == "you" ? S.hp : u.side == "party" ? (Member(u.def)?.hp ?? 0) : u.hp;
        public float MaxHpOf(GridUnit u)
        {
            if (u.id == "you") return Rules.MaxHp(S);
            if (u.side == "party") { var m = Member(u.def); var f = data.Follower(u.def); return m == null || f == null ? 1 : Rules.MemberMaxHp(m, f); }
            return u.maxHp;
        }
        bool Standing(GridUnit u) => u != null && !u.gone && !u.down;

        // ------------------------------------------------------------------ walking

        /// <summary>Every square someone can get to within max steps (and how many steps): through their own side, never through the other.</summary>
        Dictionary<int, int> Flood(GridUnit u, int max)
        {
            var dist = new Dictionary<int, int> { [u.x + u.y * G.w] = 0 };
            var q = new Queue<int>();
            q.Enqueue(u.x + u.y * G.w);
            while (q.Count > 0)
            {
                int k = q.Dequeue(), d = dist[k];
                if (d >= max) continue;
                foreach (var (dx, dy) in Steps)
                {
                    int x = k % G.w + dx, y = k / G.w + dy, n = x + y * G.w;
                    if (!OnGrid(x, y) || Solid(x, y) || dist.ContainsKey(n)) continue;
                    var o = At(x, y);
                    if (o != null && o.side != u.side) continue;
                    dist[n] = d + 1;
                    q.Enqueue(n);
                }
            }
            return dist;
        }

        /// <summary>The shortest way to a square, not counting where they stand. null if there's no way.</summary>
        List<int> GridPath(GridUnit u, int tx, int ty)
        {
            int start = u.x + u.y * G.w, goal = tx + ty * G.w;
            if (start == goal) return new List<int>();
            var from = new Dictionary<int, int> { [start] = -1 };
            var q = new Queue<int>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                int k = q.Dequeue();
                if (k == goal) break;
                foreach (var (dx, dy) in Steps)
                {
                    int x = k % G.w + dx, y = k / G.w + dy, n = x + y * G.w;
                    if (!OnGrid(x, y) || Solid(x, y) || from.ContainsKey(n)) continue;
                    var o = At(x, y);
                    if (o != null && o.side != u.side) continue;
                    from[n] = k;
                    q.Enqueue(n);
                }
            }
            if (!from.ContainsKey(goal)) return null;
            var path = new List<int>();
            for (int k = goal; k != start; k = from[k]) path.Add(k);
            path.Reverse();
            return path;
        }

        /// <summary>The squares one of your side can move to now.</summary>
        public List<(int x, int y)> Reachable(string id)
        {
            var u = Unit(id);
            var list = new List<(int x, int y)>();
            if (!InGrid || G.phase != "party" || !Standing(u) || u.side != "party") return list;
            foreach (var kv in Flood(u, u.moveLeft))
            {
                int x = kv.Key % G.w, y = kv.Key / G.w;
                if (kv.Value > 0 && At(x, y) == null) list.Add((x, y));
            }
            return list;
        }

        /// <summary>Can this one strike someone who walks away from them? Hand-to-hand fighters only, facing the right way, with their reaction left.</summary>
        bool CanFreeHit(GridUnit o, GridUnit mover, int fromX, int fromY)
        {
            if (!Standing(o) || o.side == mover.side || !o.reaction || HasState(o, "fleeing")) return false;
            if (HasState(o, "staggered") || HasState(o, "dazed") || HasState(o, "prone")) return false;
            if (Dist(o.x, o.y, fromX, fromY) != 1 || SideOf(o, fromX, fromY) == "back") return false;
            if (o.side == "party")
            {
                if (RangeOf(o, "attack").max > 1) return false;
                if (o.id != "you" && Rules.NoFight(Member(o.def)?.body ?? new Body(), data)) return false;
            }
            return true;
        }

        /// <summary>
        /// Walks someone along a path, a square at a time. Stepping out of reach of someone facing them who could
        /// hit them gives that one a free blow on the way past.
        /// </summary>
        void WalkPath(GridUnit u, List<int> path)
        {
            var walked = new List<int>();
            void Flush()
            {
                if (walked.Count == 0) return;
                Events.Add(new BattleEvent { from = u.id, kind = "move", path = new List<int>(walked) });
                walked.Clear();
            }
            foreach (int sq in path)
            {
                int nx = sq % G.w, ny = sq / G.w;
                foreach (var o in G.units.ToList())
                {
                    if (!CanFreeHit(o, u, u.x, u.y) || Dist(o.x, o.y, nx, ny) <= 1) continue;
                    o.reaction = false;
                    Flush();
                    if (o.side == "foe")
                    {
                        var e = FoeDef(o);
                        var m = e.moves.Where(x => x.Attacks() && !x.charge).OrderBy(x => x.power).FirstOrDefault() ?? e.moves[0];
                        o.facing = FaceTo(u.x - o.x, u.y - o.y);
                        Log("As " + (u.id == "you" ? "you back" : UnitName(u) + " backs") + " away, " + UnitName(o) + " gets a free hit in.");
                        LandFoeHit(o, u, m, Roll(e.atkMin, e.atkMax) * m.power * FoeRage(o), SideOf(u, o.x, o.y), false, null);
                    }
                    else
                    {
                        Log("As " + UnitName(u) + " pulls away, " + (o.id == "you" ? "you get" : UnitName(o) + " gets") + " a free hit in.");
                        o.facing = FaceTo(u.x - o.x, u.y - o.y);
                        PartyStrike(o, u, BodyPart(u), "attack", 0.75f, true);
                    }
                    if (!Standing(u) || G.phase == "over") return;
                }
                u.facing = FaceTo(nx - u.x, ny - u.y);
                u.x = nx; u.y = ny;
                u.moveLeft = Math.Max(0, u.moveLeft - 1);
                walked.Add(sq);
            }
            Flush();
        }

        /// <summary>Moves one of your side to a square they can reach. Moving takes them off guard (and out of a brace).</summary>
        public bool GridMove(string id, int x, int y)
        {
            if (!InGrid || G.phase != "party") return false;
            var u = Unit(id);
            if (!Standing(u) || u.side != "party" || !OnGrid(x, y) || At(x, y) != null) return false;
            var path = GridPath(u, x, y);
            if (path == null || path.Count == 0 || path.Count > u.moveLeft) return false;
            Events.Clear();
            ClearState(u, "braced");
            ClearState(u, "guard");
            ClearState(u, "prone");
            WalkPath(u, path);
            Rules.Clamp(S, data);
            CheckEnd();
            return true;
        }

        /// <summary>Turns one of your side to face a way. Free, any time in your turn: mind your back.</summary>
        public bool GridFace(string id, string dir)
        {
            var u = Unit(id);
            if (!InGrid || G.phase != "party" || !Standing(u) || u.side != "party") return false;
            if (dir != "up" && dir != "down" && dir != "left" && dir != "right") return false;
            u.facing = dir;
            return true;
        }

        // ------------------------------------------------------------------ reach and body parts

        /// <summary>How near and how far someone can hit with an action. A bow (or sling) reaches far; a blade, one square.</summary>
        public (int min, int max) RangeOf(GridUnit u, string action)
        {
            if (u.side == "foe") return (1, 1);
            if (u.id == "you")
            {
                if (action == "spell:kindle" || (action.StartsWith("move:") && data.Move(action.Substring(5))?.skill == "magic")) return (1, 4);
                return WeaponSkill() == "bow" ? (1, 8) : (1, 1);
            }
            var f = data.Follower(u.def);
            return f != null && f.range > 1 ? (1, f.range) : (1, 1);
        }

        /// <summary>Is this a shot (bow or sling), with the falloff and point-blank rules?</summary>
        bool Shot(GridUnit u, string action) => RangeOf(u, action).max > 1 && !action.StartsWith("spell:") && !(action.StartsWith("move:") && data.Move(action.Substring(5))?.skill == "magic");

        static readonly PartDef PartBody = new PartDef { id = "body", name = "Body", hit = 0, dmg = 1 };

        /// <summary>Where you can aim on someone: their body, and the harder places that do something.</summary>
        public List<PartDef> PartsOf(GridUnit t)
        {
            var list = new List<PartDef> { PartBody };
            var e = t.side == "foe" ? FoeDef(t) : null;
            if (e != null && e.parts.Count > 0) { list.AddRange(e.parts.Where(p => p.id != "body")); return list; }
            list.Add(new PartDef { id = "head", name = "Head", hit = -25, dmg = 1.5f, effect = "daze" });
            if (e != null && !string.IsNullOrEmpty(e.figure)) list.Add(new PartDef { id = "arm", name = "Weapon arm", hit = -15, dmg = 1f, effect = "weaken" });
            list.Add(new PartDef { id = "legs", name = "Legs", hit = -10, dmg = 1f, effect = "hobble" });
            list.Add(new PartDef { id = "vital", name = "Vital point", hit = -30, dmg = 2f, effect = "bleed", vital = true });
            return list;
        }

        string BodyPart(GridUnit t) => "body";

        /// <summary>Can you aim at this part from where you are? The vital point needs you beside or behind it (or it open); some parts only face front.</summary>
        string PartBlocked(GridUnit t, PartDef p, string side)
        {
            if (p.vital && side == "front" && !HasState(t, "open")) return "Only from the side or behind";
            if (p.front && side != "front") return "Only from in front";
            return null;
        }

        // ------------------------------------------------------------------ the numbers

        int SkillFor(GridUnit a, string action)
        {
            if (a.id == "you")
            {
                bool magic = action == "spell:kindle" || (action.StartsWith("move:") && data.Move(action.Substring(5))?.skill == "magic");
                return SkillLevel(magic ? "magic" : WeaponSkill());
            }
            return Rules.MemberLevel(Member(a.def) ?? new PartyMember { level = 1 });
        }

        /// <summary>The % chance a blow from (ax, ay) lands on that part, and what went into it.</summary>
        int HitChance(GridUnit a, int ax, int ay, GridUnit t, PartDef p, string action, List<string> notes)
        {
            if (HasState(t, "open")) { notes?.Add("Open: a sure hit"); return 100; }
            var e = t.side == "foe" ? FoeDef(t) : null;
            int c = 80 + SkillFor(a, action) * 2 - (e?.defence ?? 0) + p.hit;
            if (p.hit != 0) notes?.Add(p.name + ": " + p.hit + "%");
            if (e != null && e.defence > 0) notes?.Add("Its defence: -" + e.defence + "%");
            string side = SideOf(t, ax, ay);
            if (side == "side") { c += SideBonus; notes?.Add("From the side: +" + SideBonus + "%"); }
            if (side == "back") { c += BackBonus; notes?.Add("From behind: +" + BackBonus + "%"); }
            var mates = G.units.Where(o => o != a && o.side == a.side && Standing(o) && Dist(o, t) == 1).ToList();
            bool pinned = mates.Any(o => mates.Any(q => q != o && q.x - t.x == t.x - o.x && q.y - t.y == t.y - o.y))
                       || (Dist(ax, ay, t.x, t.y) == 1 && mates.Any(o => o.x - t.x == t.x - ax && o.y - t.y == t.y - ay));
            if (pinned) { c += PinnedBonus; notes?.Add("Pinned between you: +" + PinnedBonus + "%"); }
            int gang = Math.Min(GangMax, mates.Count * GangBonus);
            if (gang > 0) { c += gang; notes?.Add("Ganged up on: +" + gang + "%"); }
            if (HasState(t, "prone")) { c += 15; notes?.Add("Knocked down: +15%"); }
            if (HasState(a, "dazed")) { c -= 10; notes?.Add("You're dazed: -10%"); }
            if (Shot(a, action))
            {
                int d = Dist(ax, ay, t.x, t.y);
                if (d == 1) { c -= PointBlank; notes?.Add("Point blank: -" + PointBlank + "%"); }
                if (d > FullRange) { c -= 5 * (d - FullRange); notes?.Add("Long range: -" + 5 * (d - FullRange) + "%"); }
                if (Cover(t.x, t.y)) { c -= CoverPenalty; notes?.Add("In long grass: -" + CoverPenalty + "%"); }
            }
            var mv = action.StartsWith("move:") ? data.Move(action.Substring(5)) : null;
            if (mv != null && mv.first) { c += 15; notes?.Add(mv.name + ": +15%"); }
            if (action == "ab:aimed_shot" || action == "move:aimed_shot") { c += 10; notes?.Add("Aimed: +10%"); }
            return Math.Clamp(c, 10, 95);
        }

        /// <summary>Everything that scales a blow's damage, from where it's struck, and what went into it.</summary>
        float DamageMul(GridUnit a, int ax, int ay, GridUnit t, PartDef p, string action, List<string> notes)
        {
            var e = t.side == "foe" ? FoeDef(t) : null;
            var mv = action.StartsWith("move:") ? data.Move(action.Substring(5)) : null;
            bool pierce = (mv != null && mv.pierce) || action == "ab:aimed_shot";
            bool open = HasState(t, "open");
            float m = p.dmg * (mv != null ? mv.power : 1f);
            if (p.dmg != 1) notes?.Add(p.name + ": x" + p.dmg.ToString("0.#") + " damage");
            string side = SideOf(t, ax, ay);
            if (side == "back") { m *= BackDamage; notes?.Add("From behind: +25% damage"); }
            if (HasState(t, "vulnerable")) { m *= VulnerableTaken; notes?.Add("Vulnerable: +50% damage"); }
            if (p.id == "body" && side == "front" && !pierce && !open && e != null && e.frontGuard < 1)
            {
                m *= e.frontGuard;
                notes?.Add((e.ai == "charger" ? "Thick skull" : string.IsNullOrEmpty(e.figure) ? "Hard shell" : "Shield") + " in front: x" + e.frontGuard.ToString("0.#") + " damage");
            }
            if (HasState(t, "guard") && side == "front" && !pierce && !open) { m *= 0.5f; notes?.Add("On guard: half damage from in front"); }
            if (Shot(a, action))
            {
                int d = Dist(ax, ay, t.x, t.y);
                if (d > FullRange) { m *= Math.Max(0.3f, 1f - 0.1f * (d - FullRange)); notes?.Add("Long range: -" + 10 * (d - FullRange) + "% damage"); }
            }
            if (action == "ab:aimed_shot") m *= Rules.AimedShot;
            if (action == "ab:pin_down") m *= 0.5f;
            if (a.id == "you")
            {
                m *= Rules.FightPower(S.body, data) * Rules.QuirkMul(S, data, q => q.dealt);
                if (Berserk) { m *= Rules.BerserkDamage; notes?.Add("Berserk: +30% damage"); }
            }
            else
            {
                var mem = Member(a.def);
                if (mem != null) m *= Rules.PartyPower(mem, data);
            }
            return m;
        }

        /// <summary>The blow before where it lands: your weapon (or spell), or a companion's.</summary>
        (float min, float max) BaseRange(GridUnit a, string action)
        {
            if (a.id == "you")
            {
                bool magic = action == "spell:kindle" || (action.StartsWith("move:") && data.Move(action.Substring(5))?.skill == "magic");
                if (magic) { float b = 6 + S.stats[Stat.Int] * 2f + SkillLevel("magic") * 1.5f; return (b, b + 2); }
                float w = 4 + S.stats[Stat.Str] * 1.5f + Rules.GearDamage(S) + SkillLevel(WeaponSkill()) * 0.8f;
                return (w - 1, w + 2);
            }
            var f = data.Follower(a.def);
            int lv = Rules.MemberLevel(Member(a.def) ?? new PartyMember { level = 1 });
            return (f.atkMin + lv - 1, f.atkMax + lv - 1);
        }

        float BaseRoll(GridUnit a, string action)
        {
            if (a.id == "you")
            {
                bool magic = action == "spell:kindle" || (action.StartsWith("move:") && data.Move(action.Substring(5))?.skill == "magic");
                return magic ? SpellHit() : WeaponHit();
            }
            var f = data.Follower(a.def);
            return Roll(f.atkMin, f.atkMax) + Rules.MemberLevel(Member(a.def) ?? new PartyMember { level = 1 }) - 1;
        }

        /// <summary>Your usual blow with what's in your hand, before luck: shown on the character sheet.</summary>
        public float AttackPower() => Rules.Attack(S, data, SkillLevel(WeaponSkill()));

        /// <summary>A hit with what's in your hand: Strength, the weapon, and how well you know it.</summary>
        float WeaponHit() => 4 + S.stats[Stat.Str] * 1.5f + Rules.GearDamage(S) + SkillLevel(WeaponSkill()) * 0.8f + Roll(-1, 2);

        float SpellHit() => 6 + S.stats[Stat.Int] * 2f + SkillLevel("magic") * 1.5f + Roll(0, 2);

        /// <summary>Berserk (the soldier's perk): badly hurt, your blows land harder.</summary>
        public bool Berserk => Rules.HasPerk(S, data, "berserk") && S.hp > 0 && S.hp < Rules.MaxHp(S) * Rules.BerserkBelow;

        // ------------------------------------------------------------------ attacking

        /// <summary>Who this one could hit with that action from where they stand.</summary>
        public List<string> Targets(string id, string action)
        {
            var u = Unit(id);
            var list = new List<string>();
            if (!InGrid || !Standing(u)) return list;
            var (min, max) = RangeOf(u, action);
            foreach (var t in G.units.Where(t => t.side == "foe" && !t.gone))
            {
                int d = Dist(u, t);
                if (d < min || d > max) continue;
                if (d > 1 && !Sight(u.x, u.y, t.x, t.y)) continue;
                list.Add(t.id);
            }
            return list;
        }

        /// <summary>What an attack would do, before you commit.</summary>
        public GridPreview Preview(string id, string targetId, string part, string action)
        {
            var pv = new GridPreview();
            var a = Unit(id);
            var t = Unit(targetId);
            if (!InGrid || !Standing(a) || t == null || t.gone || t.side != "foe") { pv.why = "No one to hit"; return pv; }
            var p = PartsOf(t).FirstOrDefault(x => x.id == part) ?? PartBody;
            pv.side = SideOf(t, a.x, a.y);
            if (!Targets(id, action).Contains(targetId))
            {
                var (min, max) = RangeOf(a, action);
                pv.why = Dist(a, t) > max ? (max == 1 ? "Too far: get next to it" : "Out of range") : Dist(a, t) < min ? "Too close" : "Can't see it from here";
                return pv;
            }
            var blocked = PartBlocked(t, p, pv.side);
            if (blocked != null) { pv.why = blocked; return pv; }
            pv.chance = HitChance(a, a.x, a.y, t, p, action, pv.notes);
            float mul = DamageMul(a, a.x, a.y, t, p, action, pv.notes);
            var (bmin, bmax) = BaseRange(a, action);
            var mv = action.StartsWith("move:") ? data.Move(action.Substring(5)) : null;
            int hits = Math.Max(1, mv?.hits ?? 1);
            pv.min = Math.Max(1, (int)Math.Round(bmin * mul)) * hits;
            pv.max = Math.Max(1, (int)Math.Round(bmax * mul)) * hits;
            if (p.id == "body" && pv.chance < 100) pv.graze = Math.Max(1, (int)Math.Round(bmin * mul / 3f)) * hits;
            pv.kills = pv.min >= t.hp;
            if (!string.IsNullOrEmpty(p.effect)) pv.notes.Add(EffectWords(p.effect));
            if (mv != null && mv.stagger) pv.notes.Add("Staggers it: it loses its next action");
            if (action == "ab:pin_down") pv.notes.Add("Pins it: it loses its next action");
            pv.ok = true;
            return pv;
        }

        static string EffectWords(string effect) => effect switch
        {
            "daze" => "Dazes: it can't react, and loses whatever it's winding up to",
            "hobble" => "Hobbles: 2 fewer squares, no dodging, no charging",
            "weaken" => "Weakens: its blows land softer",
            "bleed" => "Bleeds: it loses health every round",
            _ => effect,
        };

        /// <summary>One of your side attacks an enemy (part: body, head, legs...; action: attack, a skill move, a spell or an ability).</summary>
        public bool GridAttack(string id, string targetId, string part, string action)
        {
            if (!InGrid || G.phase != "party") return false;
            var a = Unit(id);
            var t = Unit(targetId);
            if (!Standing(a) || a.side != "party" || a.acted || t == null || t.gone || t.side != "foe") return false;
            var act = ActionsFor(id).FirstOrDefault(x => x.id == action);
            if (act == null || !act.enabled || !act.targeted) return false;
            if (!Targets(id, action).Contains(targetId)) return false;
            var p = PartsOf(t).FirstOrDefault(x => x.id == (part ?? "body"));
            if (p == null || PartBlocked(t, p, SideOf(t, a.x, a.y)) != null) return false;
            Events.Clear();
            a.facing = FaceTo(t.x - a.x, t.y - a.y);
            var mv = action.StartsWith("move:") ? data.Move(action.Substring(5)) : null;
            if (a.id == "you")
            {
                PayFor(action);
                G.swung = true;
            }
            a.acted = true;
            ClearState(a, "guard");
            if (action == "ab:aimed_shot") a.states.Add("cd_aimed_shot:3");
            if (action == "ab:pin_down") a.states.Add("cd_pin_down:999");

            if (a.id == "you" && FumbleNow())
            {
                Rules.Clamp(S, data);
                return true;
            }
            bool landed = false;
            for (int i = 0; i < Math.Max(1, mv?.hits ?? 1) && !t.gone; i++)
                landed |= PartyStrike(a, t, p.id, action, 1f, false) > 0;
            if (!t.gone && landed && ((mv != null && mv.stagger) || action == "ab:pin_down"))
            {
                AddState(t, "staggered", 1);
                Log(Cap(UnitName(t)) + (action == "ab:pin_down" ? " is pinned where it stands." : " reels."));
                Events.Add(new BattleEvent { target = t.id, kind = "stun", text = action == "ab:pin_down" ? "Pinned" : "Staggered" });
            }
            if (a.id == "you")
            {
                if (mv != null) { TrainSkill(mv.skill, 3, "fighting"); if (mv.skill == "magic") Rules.Train(S, Stat.Int, 1f, "casting"); }
                if (action == "spell:kindle") { Rules.Train(S, Stat.Int, 1f, "casting Kindle"); TrainSkill("magic", 3, "casting Kindle"); }
            }
            Rules.Clamp(S, data);
            CheckEnd();
            return true;
        }

        /// <summary>Your wounds may make a blow go wrong (a concussion, say).</summary>
        bool FumbleNow()
        {
            int f = Rules.Fumble(S.body, data);
            if (f <= 0 || Roll(1, 100) > f) return false;
            Log("The world swims. Your blow goes nowhere near.");
            Events.Add(new BattleEvent { target = "you", kind = "miss", text = "Dizzy" });
            return true;
        }

        void PayFor(string action)
        {
            if (action == "attack")
            {
                float light = Math.Clamp(Rules.TraitSum(S, data, t => t.light), 0f, 0.5f);
                S.stamina -= (int)Math.Round(Rules.AttackStamina * (1 - light));
                S.energy -= 0;
            }
            else if (action == "spell:kindle") { S.stamina -= Rules.KindleStamina; S.mana -= ManaCost(Rules.KindleMana); }
            else if (action.StartsWith("move:"))
            {
                var m = data.Move(action.Substring(5));
                S.stamina -= m.stamina;
                S.mana -= ManaCost(m.mana);
                S.energy -= m.energy;
            }
        }

        /// <summary>One blow from one of your side: does it land, where, how hard. Returns the damage done (0: a miss).</summary>
        int PartyStrike(GridUnit a, GridUnit t, string part, string action, float extra, bool free)
        {
            var p = PartsOf(t).FirstOrDefault(x => x.id == part) ?? PartBody;
            bool open = HasState(t, "open");
            int chance = HitChance(a, a.x, a.y, t, p, action, null);
            bool hit = open || Roll(1, 100) <= chance;
            float mul = DamageMul(a, a.x, a.y, t, p, action, null) * extra;
            string who = a.id == "you" ? "You" : UnitName(a);
            string where = p.id == "body" ? UnitName(t) : UnitName(t) + "'s " + p.name.ToLowerInvariant();
            bool shot = Shot(a, action);
            if (!hit && p.id != "body")
            {
                Log(who + (shot ? " shoot" + (a.id == "you" ? "" : "s") + " for " : " go" + (a.id == "you" ? "" : "es") + " for ") + where + ", and miss" + (a.id == "you" ? "." : "es."));
                Events.Add(new BattleEvent { from = a.id, target = t.id, kind = "miss", text = "Miss" });
                return 0;
            }
            string how = hit ? "hit" : "graze";
            if (!hit) mul /= 3f;
            else if (open || (!free && Roll(1, 100) <= (a.id == "you" ? (Rules.Winded(S, data) ? 0 : Rules.CritChance(S, data)) : 8)))
            {
                how = "crit";
                mul *= Rules.CritMultiplier + (a.id == "you" ? Rules.TraitSum(S, data, x => x.crit) : 0) + (open ? Rules.OpenBonus : 0);
            }
            float dmg = BaseRoll(a, action) * mul;
            if (a.id == "you" && action == "attack") dmg += Rules.TraitSum(S, data, x => x.fire);
            int n = Math.Max(1, (int)Math.Round(dmg));
            if (open && hit) ClearState(t, "open");
            string verb = a.id == "you" ? (how == "graze" ? "only graze " : how == "crit" ? "land a perfect blow on " : "hit ")
                                        : (how == "graze" ? "only grazes " : how == "crit" ? "lands a perfect blow on " : (shot ? "hits " : "hits "));
            Log(who + " " + verb + where + " for " + n + (how == "crit" ? "!" : "."));
            Events.Add(new BattleEvent { from = a.id, target = t.id, kind = how, amount = n, text = n.ToString() });
            if (hit && !string.IsNullOrEmpty(p.effect) && t.hp - n > 0) ApplyEffect(t, p.effect);
            if (a.id == "you")
            {
                var blade = S.InSlot("weapon");
                if (blade != null && Rules.IsBlade(blade) && !shot) WearDown(blade, HasState(t, "guard") ? Rules.GuardBladeWear : Rules.BladeWear);
                if (!action.StartsWith("spell:"))
                {
                    Rules.Train(S, Stat.Str, 1f, "fighting");
                    Rules.Train(S, Stat.Agi, how == "crit" ? 1f : 0.5f, "fighting");
                    TrainSkill(WeaponSkill(), how == "crit" ? 3 : 2, "fighting");
                }
            }
            DamageFoe(t, n, a);
            return n;
        }

        void ApplyEffect(GridUnit t, string effect)
        {
            switch (effect)
            {
                case "daze":
                    AddState(t, "dazed", 1);
                    Events.Add(new BattleEvent { target = t.id, kind = "state", text = "Dazed" });
                    Log(Cap(UnitName(t)) + " is dazed.");
                    if (t.lane.Count > 0) { t.lane.Clear(); t.intent = "wait"; Log("Its charge is broken."); }
                    break;
                case "hobble":
                    AddState(t, "hobbled", 2);
                    Events.Add(new BattleEvent { target = t.id, kind = "state", text = "Hobbled" });
                    Log(Cap(UnitName(t)) + " is hobbled.");
                    if (t.lane.Count > 0) { t.lane.Clear(); t.intent = "wait"; Log("It can't charge on that leg."); }
                    break;
                case "weaken":
                    AddState(t, "weakened", 2);
                    Events.Add(new BattleEvent { target = t.id, kind = "state", text = "Weakened" });
                    break;
                case "bleed":
                    AddState(t, "bleeding", 3);
                    Events.Add(new BattleEvent { target = t.id, kind = "state", text = "Bleeding" });
                    Log(Cap(UnitName(t)) + " is bleeding.");
                    break;
            }
        }

        // ------------------------------------------------------------------ what your side can do

        /// <summary>Everything this one of your side can do now (greyed out with a reason when they can't).</summary>
        public List<GridAction> ActionsFor(string id)
        {
            var list = new List<GridAction>();
            var u = Unit(id);
            if (!InGrid || !Standing(u) || u.side != "party") return list;
            string done = u.acted ? "Already acted this turn" : G.phase != "party" ? "Not your turn" : "";
            void Add(GridAction a)
            {
                if (done != "" && a.id != "wait") { a.enabled = false; a.why = done; }
                list.Add(a);
            }
            bool you = u.id == "you";
            var f = you ? null : data.Follower(u.def);
            var m = you ? null : Member(u.def);
            bool noFight = !you && m != null && Rules.NoFight(m.body, data);
            var (_, reach) = RangeOf(u, "attack");

            if (you)
            {
                var w = S.InSlot("weapon");
                float light = Math.Clamp(Rules.TraitSum(S, data, t => t.light), 0f, 0.5f);
                Add(new GridAction { id = "attack", name = "Attack", targeted = true, cost = (int)Math.Round(Rules.AttackStamina * (1 - light)) + " stamina",
                    desc = (w != null ? w.name : "Bare hands") + (reach > 1 ? ": reaches " + reach + " squares, full strength to " + FullRange + "." : ": the square next to you.") });
                foreach (var mv in UsableMoves())
                {
                    var a = new GridAction { id = "move:" + mv.id, name = mv.name, desc = mv.desc, targeted = true,
                        cost = mv.stamina + " stamina" + (mv.mana > 0 ? ", " + ManaCost(mv.mana) + " mana" : "") };
                    if (S.stamina < mv.stamina) { a.enabled = false; a.why = "Too tired"; }
                    else if (S.mana < ManaCost(mv.mana)) { a.enabled = false; a.why = "Not enough mana"; }
                    Add(a);
                }
                if (S.spells.Contains("kindle"))
                {
                    var a = new GridAction { id = "spell:kindle", name = "Kindle", desc = "Fire from your hand, up to 4 squares.", targeted = true,
                        cost = Rules.KindleStamina + " stamina, " + ManaCost(Rules.KindleMana) + " mana" };
                    if (S.mana < ManaCost(Rules.KindleMana)) { a.enabled = false; a.why = "Not enough mana"; }
                    Add(a);
                }
            }
            else
            {
                var a = new GridAction { id = "attack", name = reach > 1 ? (f.attack.Contains("sling") ? "Sling" : "Shoot") : "Attack", targeted = true,
                    desc = reach > 1 ? "Reaches " + reach + " squares, full strength to " + FullRange + "." : "The square next to them." };
                if (noFight) { a.enabled = false; a.why = "In no state to fight"; }
                Add(a);
                foreach (var ab in f.abilities)
                {
                    if (ab.id != "aimed_shot" && ab.id != "pin_down") continue;
                    var b = new GridAction { id = "ab:" + ab.id, name = ab.name, desc = ab.id == "aimed_shot" ? "Takes a breath and aims: +10% to hit, half again the damage, through any guard. Every third round."
                                                                                                          : "A shot that pins an enemy where it stands: it loses its next action. Once a fight.", targeted = true };
                    if (!Rules.HasAbility(m, f, ab.id)) { b.enabled = false; b.why = "Level " + ab.level; }
                    else if (HasState(u, "cd_" + ab.id)) { b.enabled = false; b.why = ab.id == "pin_down" ? "Used this fight" : "Not ready yet"; }
                    else if (noFight) { b.enabled = false; b.why = "In no state to fight"; }
                    Add(b);
                }
            }
            Add(new GridAction { id = "guard", name = "Guard", desc = you ? "Get your breath back, and meet the next blow with the timing bar: parry with a blade, block with a shield." : "Get ready to take a blow: half damage from in front." ,
                                 cost = you ? "+" + Rules.DefendStaminaBack + " stamina" : "" });
            Add(new GridAction { id = "brace", name = "Brace", desc = "Set your feet for a charge. A beast that charges into you stops dead, stunned and open." });
            foreach (var d in G.units.Where(o => o.side == "party" && o.down && !o.gone && Dist(o, u) == 1))
                Add(new GridAction { id = "help:" + d.id, name = "Help " + UnitName(d) + " up", desc = "Gets them back on their feet, hurt (it leaves a wound). They can't act this turn." });
            if (you)
            {
                foreach (var it in S.items)
                {
                    var def = data.Item(it.id);
                    if (def == null || it.count <= 0) continue;
                    if (def.heal > 0) Add(new GridAction { id = "item:" + it.id, name = def.name + (it.count > 1 ? " (" + it.count + ")" : ""), desc = "Drink it: +" + def.heal + " health." });
                    else if (def.food) Add(new GridAction { id = "item:" + it.id, name = def.name + (it.count > 1 ? " (" + it.count + ")" : ""), desc = "Wolf it down between blows." });
                }
                if (S.water > 0) Add(new GridAction { id = "drink", name = "Waterskin (" + S.water + ")", desc = "A mouthful of water." });
                var run = new GridAction { id = "run", name = "Run", desc = "Break away off the left edge, and take everyone with you." };
                if (u.x != 0) { run.enabled = false; run.why = "Get to the left edge first"; }
                else if (Rules.NoRun(S.body, data)) { run.enabled = false; run.why = "You can't run like this"; }
                Add(run);
            }
            Add(new GridAction { id = "wait", name = "Done", desc = "Nothing more for " + (you ? "you" : UnitName(u)) + " this turn." });
            return list;
        }

        /// <summary>One of your side does something that isn't an attack: guard, brace, help someone up, use something, run, or finish.</summary>
        public bool GridDo(string id, string action)
        {
            if (!InGrid || G.phase != "party") return false;
            var u = Unit(id);
            var act = ActionsFor(id).FirstOrDefault(x => x.id == action);
            if (!Standing(u) || act == null || !act.enabled || act.targeted) return false;
            Events.Clear();
            bool you = u.id == "you";
            string name = you ? "You" : UnitName(u);
            if (action == "wait")
            {
                u.acted = true;
                u.moveLeft = 0;
                return true;
            }
            u.acted = true;
            switch (action)
            {
                case "guard":
                    AddState(u, "guard", 1);
                    ClearState(u, "braced");
                    if (you) { S.stamina += Rules.DefendStaminaBack; if (HasShield()) TrainSkill("shield", 1, "guarding"); }
                    Log(name + (you ? " get" : " gets") + " on guard.");
                    Events.Add(new BattleEvent { target = u.id, kind = "guard", text = "On guard", amount = you ? Rules.DefendStaminaBack : 0 });
                    break;
                case "brace":
                    AddState(u, "braced", 1);
                    ClearState(u, "guard");
                    if (you) S.stamina += 6;
                    Log(name + (you ? " brace" : " braces") + ", feet set.");
                    Events.Add(new BattleEvent { target = u.id, kind = "brace", text = "Braced" });
                    break;
                case "drink":
                    S.water--;
                    S.thirst += 35;
                    S.energy += 4;
                    Log("You snatch a mouthful from the waterskin.");
                    Events.Add(new BattleEvent { target = u.id, kind = "heal", amount = 35, text = "+35 thirst" });
                    break;
                case "run":
                    Log("You turn and run, and your party with you.");
                    Events.Add(new BattleEvent { from = u.id, kind = "flee", text = "Escaped" });
                    Rules.Train(S, Stat.Agi, 1f, "escaping");
                    Finish("fled");
                    return true;
                default:
                    if (action.StartsWith("help:"))
                    {
                        var d = Unit(action.Substring(5));
                        var m = Member(d.def);
                        var f = data.Follower(d.def);
                        d.down = false;
                        d.acted = true;
                        d.moveLeft = 0;
                        m.hp = Math.Max(1, Rules.MemberMaxHp(m, f) * 0.2f);
                        Rules.Afflict(S, data, m.body, "gash", f.name, null, WoundedBy());
                        TrainSkill("first_aid", 2, "getting someone back on their feet");
                        Log(name + (you ? " haul " : " hauls ") + f.name + " back up. They're hurt, but standing.");
                        Events.Add(new BattleEvent { from = u.id, target = d.id, kind = "heal", text = "Back up" });
                        break;
                    }
                    if (action.StartsWith("item:"))
                    {
                        string iid = action.Substring(5);
                        var it = data.Item(iid);
                        S.AddItem(iid, -1);
                        if (it.heal > 0)
                        {
                            int back = (int)Math.Min(it.heal, Rules.MaxHp(S) - S.hp);
                            S.hp += it.heal;
                            Log("You gulp down the " + it.name.ToLowerInvariant() + ".");
                            Events.Add(new BattleEvent { target = u.id, kind = "heal", amount = back, text = "+" + back + " health" });
                        }
                        else
                        {
                            S.hunger += it.hunger;
                            S.thirst += it.thirst;
                            S.energy += it.energy;
                            Log("You wolf down the " + it.name.ToLowerInvariant() + " between blows.");
                            Events.Add(new BattleEvent { target = u.id, kind = "heal", amount = (int)it.energy, text = "+" + it.energy.ToString("0") + " energy" });
                        }
                        break;
                    }
                    u.acted = false;
                    return false;
            }
            Rules.Clamp(S, data);
            return true;
        }

        // ------------------------------------------------------------------ the party on its own (tests, and anyone who'd rather)

        /// <summary>Squares a charge is about to cross: stand there braced, or not at all.</summary>
        HashSet<int> Lanes() => new HashSet<int>(G.units.Where(f => f.side == "foe" && !f.gone).SelectMany(f => f.lane));

        /// <summary>Plays one of your side's turn sensibly: get a friend up, step out of a charge, flank, hit the best spot.</summary>
        public void AutoUnit(string id)
        {
            var u = Unit(id);
            if (!InGrid || G.phase != "party" || !Standing(u) || u.side != "party" || u.acted) return;
            var lanes = Lanes();
            bool you = u.id == "you";

            // a friend down: get them up
            var downed = G.units.FirstOrDefault(o => o.side == "party" && o.down && !o.gone);
            if (downed != null)
            {
                if (Dist(u, downed) != 1)
                {
                    var spot = Reachable(u.id).Where(s => Dist(s.x, s.y, downed.x, downed.y) == 1).OrderBy(s => lanes.Contains(s.x + s.y * G.w) ? 1 : 0).FirstOrDefault();
                    if (spot != default) GridMove(u.id, spot.x, spot.y);
                }
                if (InGrid && Standing(u) && Dist(u, downed) == 1 && downed.down && GridDo(u.id, "help:" + downed.id)) return;
                if (!InGrid || !Standing(u)) return;
            }
            // badly hurt: drink a potion
            if (you && S.hp < Rules.MaxHp(S) * 0.35f)
            {
                var pot = S.items.FirstOrDefault(i => i.count > 0 && data.Item(i.id)?.heal > 0);
                if (pot != null && GridDo(u.id, "item:" + pot.id)) { StepOutOfLane(u, lanes); return; }
            }

            // nearly out of breath: guard and get it back, unless something's wide open or about to howl
            if (you && S.stamina - Rules.AttackStamina < Rules.MaxStamina(S, data) * 0.2f
                && !G.units.Any(f => f.side == "foe" && !f.gone && (HasState(f, "open") || f.intent == "howl")))
            {
                StepOutOfLane(u, lanes);
                if (InGrid && Standing(u) && !u.acted) GridDo(u.id, lanes.Contains(u.x + u.y * G.w) ? "brace" : "guard");
                return;
            }

            var acts = ActionsFor(u.id).Where(a => a.enabled && a.targeted).ToList();
            // pin down something about to do something big
            var pin = acts.FirstOrDefault(a => a.id == "ab:pin_down");
            if (pin != null)
            {
                var big = G.units.FirstOrDefault(f => f.side == "foe" && !f.gone && (f.lane.Count > 0 || IntentMove(f)?.KindOf() == "heavy") && Targets(u.id, pin.id).Contains(f.id));
                if (big != null && GridAttack(u.id, big.id, "body", pin.id)) { StepOutOfLane(u, Lanes()); return; }
            }
            string action = acts.Any(a => a.id == "ab:aimed_shot") ? "ab:aimed_shot" : acts.Any(a => a.id == "attack") ? "attack" : null;

            // where to stand, and who to hit from there
            float bestV = 0;
            (int x, int y) bestSq = (u.x, u.y);
            GridUnit bestT = null;
            string bestPart = "body";
            if (action != null)
            {
                var squares = new List<(int x, int y)> { (u.x, u.y) };
                squares.AddRange(Reachable(u.id));
                var (rmin, rmax) = RangeOf(u, action);
                bool ranged = rmax > 1;
                var near = G.units.Where(f => f.side == "foe" && !f.gone && Dist(f, u) == 1).ToList();
                foreach (var (sx, sy) in squares)
                {
                    float safety = lanes.Contains(sx + sy * G.w) ? 0.25f : 1f;
                    if (near.Any(f => CanFreeHit(f, u, u.x, u.y) && Dist(f.x, f.y, sx, sy) > 1)) safety *= 0.3f;
                    int foesNext = G.units.Count(f => f.side == "foe" && !f.gone && !HasState(f, "fleeing") && Dist(f.x, f.y, sx, sy) == 1);
                    if (ranged && foesNext > 0) safety *= 0.7f;
                    foreach (var t in G.units.Where(f => f.side == "foe" && !f.gone))
                    {
                        int d = Dist(sx, sy, t.x, t.y);
                        if (d < rmin || d > rmax || (d > 1 && !Sight(sx, sy, t.x, t.y))) continue;
                        if (ranged && d == 1 && foesNext == 0) continue;
                        string side = SideOf(t, sx, sy);
                        foreach (var p in PartsOf(t))
                        {
                            if (PartBlocked(t, p, side) != null) continue;
                            float c = HitChance(u, sx, sy, t, p, action, null) / 100f;
                            float m = DamageMul(u, sx, sy, t, p, action, null);
                            float v = p.id == "body" ? c * m + (1 - c) * m / 3f : c * m * (string.IsNullOrEmpty(p.effect) ? 1f : 1.1f);
                            v *= safety * (HasState(t, "fleeing") ? 0.5f : 1f) * (t.intent == "howl" ? 2f : 1f) * (1.3f - t.hp / Math.Max(1, t.maxHp) * 0.3f);
                            if (v > bestV + 0.001f) { bestV = v; bestSq = (sx, sy); bestT = t; bestPart = p.id; }
                        }
                    }
                }
            }
            if (bestT != null)
            {
                if (bestSq != (u.x, u.y)) GridMove(u.id, bestSq.x, bestSq.y);
                if (!InGrid || !Standing(u)) return;
                if (!GridAttack(u.id, bestT.id, bestPart, action) && action != "attack") GridAttack(u.id, bestT.id, "body", "attack");
                if (InGrid && Standing(u) && lanes.Contains(u.x + u.y * G.w)) StepOutOfLane(u, lanes);
                return;
            }
            // nobody in reach: close in (keeping out of any charge lane)
            var foe = G.units.Where(f => f.side == "foe" && !f.gone).OrderBy(f => Dist(f, u)).FirstOrDefault();
            if (foe != null)
            {
                var go = Reachable(u.id).Where(s => !lanes.Contains(s.x + s.y * G.w))
                                         .OrderBy(s => Dist(s.x, s.y, foe.x, foe.y)).FirstOrDefault();
                if (go != default && Dist(go.x, go.y, foe.x, foe.y) < Dist(u, foe)) GridMove(u.id, go.x, go.y);
            }
            if (!InGrid || !Standing(u) || u.acted) return;
            if (lanes.Contains(u.x + u.y * G.w)) { StepOutOfLane(u, lanes); if (InGrid && Standing(u) && !u.acted) GridDo(u.id, "brace"); }
            else GridDo(u.id, "guard");
        }

        /// <summary>Gets out of a charge's way if there's anywhere to go; braces if not.</summary>
        void StepOutOfLane(GridUnit u, HashSet<int> lanes)
        {
            if (!InGrid || !Standing(u) || !lanes.Contains(u.x + u.y * G.w)) return;
            var safe = Reachable(u.id).Where(s => !lanes.Contains(s.x + s.y * G.w)).OrderBy(s => Dist(s.x, s.y, u.x, u.y)).FirstOrDefault();
            if (safe != default) { GridMove(u.id, safe.x, safe.y); return; }
            if (!u.acted) GridDo(u.id, "brace");
        }

        /// <summary>A whole round played for your side (tests, and anyone who'd rather): everyone acts, then the enemies, with your timing as given.</summary>
        public void AutoRound(Func<int> timing)
        {
            if (!InGrid || G.phase != "party") return;
            foreach (var u in G.units.Where(u => u.side == "party").ToList())
            {
                if (!InGrid) return;
                AutoUnit(u.id);
            }
            if (!InGrid) return;
            EndTurn();
            for (int i = 0; i < 60 && AwaitingReaction; i++) React(timing());
        }
    }
}
