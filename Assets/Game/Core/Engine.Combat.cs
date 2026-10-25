using System;
using System.Collections.Generic;
using System.Linq;

namespace Loran.Core
{
    /// <summary>Something that happened in a fight, for the screen to animate: a step, a blow, a dodge, a number.</summary>
    public class BattleEvent
    {
        public string from = "";        // who did it: a unit id ("you", "rook", "boar#1"), "" for nobody
        public string target = "";      // who it happened to
        public string kind = "";        // move, charge, hit, crit, graze, miss, dodge, parry, block, braced, guard, heal, rage, howl, open, down, dead, flee, yield, stun, bleed, state, turn
        public int amount;
        public string text = "";        // what to pop up: "14", "Dodged", "+30 health"
        public List<int> path = new List<int>();   // move and charge: the squares crossed, x + y * width
    }

    /// <summary>
    /// Fights on the battle grid (David, 25 Oct; design in world/battle-design.md). Your side moves first, everyone in
    /// any order: a move and one action each. Then every enemy does what it showed it would do at the start of your
    /// turn. Where you stand matters: hit from the side or behind, aim for a weak spot, brace for a charge, and don't
    /// turn your back. This file is the flow of a fight and how the enemies think; Engine.Grid.cs is the squares,
    /// the blows and your side's actions.
    /// </summary>
    public partial class Engine
    {
        public const int TimingMiss = 0, TimingGood = 1, TimingPerfect = 2;

        /// <summary>What just happened, for the screen to animate. Not saved.</summary>
        public readonly List<BattleEvent> Events = new List<BattleEvent>();

        GridBattle G => S.grid;
        public GridBattle Grid => S.grid;
        public bool InGrid => S.inCombat && S.grid != null && S.grid.units.Count > 0;
        public EnemyDef Foe => S.inCombat ? data.Enemy(S.enemy) : null;

        /// <summary>Is someone in your party, on their feet, bringing this trait?</summary>
        public bool PartyTrait(string trait) => PartyUp.Any(m => data.Follower(m.id)?.trait == trait);

        public const int BleedOutRounds = 3;       // a companion who's down dies after this many rounds unless someone gets them up
        public const int ChargeLength = 7;         // the most squares a charge runs
        public const float BackDamage = 1.25f, VulnerableTaken = 1.5f, EnragedHits = 1.3f, WeakenedHits = 0.7f;

        // ------------------------------------------------------------------ starting a fight

        void StartCombat(string enemyId, string win, string flee)
        {
            var e = data.Enemy(enemyId) ?? throw new Exception("Unknown enemy '" + enemyId + "'");
            S.inCombat = true;
            S.enemy = e.id;
            S.combatWin = win;
            S.combatFlee = flee;
            S.battleLog.Clear();
            Events.Clear();

            var g = new GridBattle();
            S.grid = g;
            var map = PickBattleMap();
            if (map != null)
            {
                g.map = map.id;
                g.h = map.rows.Count - 2;
                g.w = map.rows[0].Length - 2;
                for (int y = 1; y <= g.h; y++) g.rows.Add(map.rows[y].Substring(1, g.w));
            }
            else
            {
                g.w = 10; g.h = 7;
                for (int y = 0; y < g.h; y++) g.rows.Add(new string('.', g.w));
            }

            // your side on the left, you in the middle of it; them on the right
            int mid = g.h / 2;
            var you = new GridUnit { id = "you", side = "party", facing = "right" };
            Place(you, 1, mid, 0, 2);
            foreach (var m in S.party)
            {
                if (data.Follower(m.id) == null) continue;
                var u = new GridUnit { id = m.id, def = m.id, side = "party", facing = "right", down = m.hp <= 0, bleedOut = BleedOutRounds };
                Place(u, 1, mid, 0, 2);
            }
            var foes = new List<string> { e.id };
            foes.AddRange(e.pack);
            var count = new Dictionary<string, int>();
            foreach (var id in foes)
            {
                var d = data.Enemy(id) ?? throw new Exception("Unknown enemy '" + id + "' in " + e.id + "'s pack");
                count[id] = count.TryGetValue(id, out var c) ? c + 1 : 1;
                var u = new GridUnit { id = id + "#" + count[id], def = id, side = "foe", facing = "left", hp = d.hp, maxHp = d.hp };
                Place(u, g.w - 2, mid, g.w - 3, g.w - 1);
            }
            // the pack leader picks out its prey: whoever looks weakest
            if (G.units.Any(u => u.side == "foe" && FoeDef(u).ai == "leader"))
            {
                var prey = G.units.Where(u => u.side == "party" && !u.down).OrderBy(u => Hp(u) / Math.Max(1, MaxHpOf(u))).ThenBy(u => u.id == "you" ? 1 : 0).First();
                prey.states.Add("prey:999");
            }
            g.round = 1;
            Log(Cap(FoeName(e)) + (e.pack.Count > 0 ? ", and " + e.pack.Count + " more with it" : "") + ". Your side moves first.");
            StartPartyPhase();
        }

        /// <summary>Puts someone on the nearest free open square to (cx, cy), between columns x0 and x1.</summary>
        void Place(GridUnit u, int cx, int cy, int x0, int x1)
        {
            var best = (x: -1, y: -1);
            int bestD = int.MaxValue;
            for (int y = 0; y < G.h; y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(G.w - 1, x1); x++)
                {
                    if (Solid(x, y) || At(x, y) != null) continue;
                    int d = Math.Abs(x - cx) * 2 + Math.Abs(y - cy) * 3;
                    if (d < bestD) { bestD = d; best = (x, y); }
                }
            if (best.x < 0)
                for (int y = 0; y < G.h && best.x < 0; y++)
                    for (int x = 0; x < G.w; x++)
                        if (!Solid(x, y) && At(x, y) == null) { best = (x, y); break; }
            u.x = best.x;
            u.y = best.y;
            G.units.Add(u);
        }

        /// <summary>What kind of ground a fight here is on: forest, hills, road, coast or village.</summary>
        public string BattleTerrain()
        {
            var a = Area;
            if (a == null) return "coast";
            if (a.id == "skelmed" || a.id == "thornby") return "village";
            if (a.id == "forest_road" || a.id == "old_track") return "forest";
            if (a.id == "greyfell" || a.id.Contains("hills")) return "hills";
            if (a.id == "brannock") return "road";
            int trees = 0, sea = 0;
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    char c = TileAt(S.ax + dx, S.ay + dy);
                    if (c == 'T') trees++;
                    if (c == '~' || c == ',') sea++;
                }
            return trees >= 12 ? "forest" : sea >= 6 ? "coast" : "road";
        }

        BattleMapDef PickBattleMap()
        {
            var maps = data.file.battleMaps;
            if (maps.Count == 0) return null;
            string t = BattleTerrain();
            var fit = maps.Where(m => m.terrain == t).ToList();
            if (fit.Count == 0) fit = maps;
            return fit[Roll(0, fit.Count - 1)];
        }

        /// <summary>Saves from before the grid (an old one-on-one fight in progress): start it again on the grid.</summary>
        void MigrateCombat()
        {
            if (!S.inCombat) return;
            S.grid ??= new GridBattle();
            if (S.grid.units.Count > 0 && S.grid.phase != "over")
            {
                // stopped for the old timing bar (no cue): let that blow go by and carry on with the enemies' turn
                if (!string.IsNullOrEmpty(S.grid.reactAttacker) && string.IsNullOrEmpty(S.grid.reactCue)) { ClearReact(); G.enemyIndex++; G.enemyStage = 0; RunFoes(); }
                return;
            }
            if (data.Enemy(S.enemy) == null) { S.inCombat = false; return; }
            StartCombat(S.enemy, S.combatWin, S.combatFlee);
        }

        /// <summary>While you're fighting, the scene underneath is just a title: the battle screen does the rest.</summary>
        SceneDef CombatScene()
        {
            var e = data.Enemy(S.enemy);
            var under = Scene(S.scene) ?? new SceneDef { place = Area?.name };
            return new SceneDef { id = "combat", place = under.place, title = "Fight: " + (e?.name ?? "?"), text = new List<string> { e?.desc ?? "" } };
        }

        // ------------------------------------------------------------------ the round

        void StartPartyPhase()
        {
            G.phase = "party";
            G.swung = false;
            foreach (var u in G.units.Where(u => !u.gone))
            {
                u.reaction = true;
                u.hitBy = 0;
                if (u.side != "party") continue;
                u.acted = u.down;
                u.moveLeft = u.down ? 0 : MoveOf(u);
            }
            foreach (var u in G.units.Where(u => u.side == "foe" && !u.gone).ToList()) PlanFoe(u);
        }

        /// <summary>How many squares someone can move this turn.</summary>
        public int MoveOf(GridUnit u)
        {
            int n = u.id == "you" ? 3 + (S.stats[Stat.Agi] >= 5 ? 1 : 0) : u.side == "party" ? 3 : FoeDef(u).move;
            if (u.id == "you" && Rules.NoRun(S.body, data)) n = Math.Min(n, 1);
            if (HasState(u, "hobbled")) n -= 2;
            if (HasState(u, "prone")) n -= 1;
            return Math.Max(0, n);
        }

        /// <summary>You've done what you want with your side: the enemies take their turn.</summary>
        public bool EndTurn()
        {
            if (!InGrid || G.phase != "party") return false;
            Events.Clear();
            // a round you didn't swing is a round you got some wind back
            if (!G.swung) S.stamina += 6;
            S.energy -= 1;
            Rules.Advance(S, data, 1, G.swung);
            Rules.Clamp(S, data);
            if (S.energy <= 0)
            {
                S.deathCause = "Your energy gave out in the middle of the fight, and you couldn't lift your arms to defend yourself.";
                S.hp = 0;
                Finish("dead");
                return true;
            }
            TickStates("party");
            G.phase = "foe";
            G.enemyIndex = 0;
            G.enemyStage = 0;
            RunFoes();
            return true;
        }

        void RunFoes()
        {
            while (G.enemyIndex < G.units.Count)
            {
                if (G.phase == "over") return;
                var u = G.units[G.enemyIndex];
                if (u.side == "foe" && !u.gone)
                {
                    if (!FoeTurn(u)) return;          // waiting on you to read and react
                    if (G.phase == "over") return;
                }
                G.enemyIndex++;
                G.enemyStage = 0;
            }
            EndRound();
        }

        void EndRound()
        {
            TickStates("foe");
            foreach (var u in G.units.Where(u => !u.gone).ToList())
            {
                if (HasState(u, "bleeding") && !u.down)
                {
                    int n = Math.Max(2, (int)Math.Round(MaxHpOf(u) * 0.05f));
                    Events.Add(new BattleEvent { target = u.id, kind = "bleed", amount = n, text = n.ToString() });
                    if (u.side == "foe") DamageFoe(u, n, null);
                    else HurtParty(u, n, "bleeding");
                    if (G.phase == "over") return;
                }
                if (u.down && !u.gone)
                {
                    u.bleedOut--;
                    if (u.bleedOut <= 0)
                    {
                        var m = Member(u.def);
                        Log(UnitName(u) + " bleeds out where they fell.");
                        Gone(u, "killed", "dead");
                        if (m != null) Rules.Die(S, data, m, "their wounds, bleeding out in a fight");
                    }
                    else Log(UnitName(u) + " is bleeding out: " + u.bleedOut + (u.bleedOut == 1 ? " round" : " rounds") + " left to get them up.");
                }
            }
            if (CheckEnd()) return;
            G.round++;
            StartPartyPhase();
        }

        /// <summary>Is it over? You dead, or nobody left on their side who still wants to fight.</summary>
        bool CheckEnd()
        {
            if (G.phase == "over") return true;
            if (S.hp <= 0) { Finish("dead"); return true; }
            if (!G.units.Any(u => u.side == "foe" && !u.gone && !HasState(u, "fleeing"))) { Finish("won"); return true; }
            return false;
        }

        void Finish(string outcome)
        {
            G.phase = "over";
            G.outcome = outcome;
            ClearReact();
            if (S.enemy == "boar" && outcome != "dead") S.flags.Add("react_taught");
            S.inCombat = false;
            var main = data.Enemy(S.enemy);
            string said = "";
            if (outcome == "won")
            {
                foreach (var u in G.units.Where(u => u.side == "foe" && !u.gone).ToList()) Gone(u, "fled", "flee");
                float exp = 0;
                foreach (var u in G.units.Where(u => u.side == "foe"))
                    exp += FoeDef(u).exp * (u.goneHow == "fled" ? 0.5f : 1f);
                var killed = G.units.Count(u => u.side == "foe" && u.goneHow == "killed");
                said = killed > 0 ? "The fight's over." : "They've had enough. The fight's over.";
                GainExp((int)Math.Round(exp), "for beating " + FoeName(main));
            }
            else if (outcome == "fled") said = "You break away and get clear.";
            if (outcome != "dead")
            {
                foreach (var u in G.units.Where(u => u.side == "party" && u.def != ""))
                {
                    var m = Member(u.def);
                    if (m != null && u.down) m.hp = Math.Min(m.hp, 0);
                }
                DownedAfterFight();
                FollowerAfterFight();
                FieldDressing();
            }
            Rules.Clamp(S, data);
            if (said != "") S.result = Join(string.Join(" ", S.battleLog.Skip(Math.Max(0, S.battleLog.Count - 2))), said);
            GoTo(outcome == "won" ? S.combatWin : outcome == "fled" ? S.combatFlee : S.scene);
        }

        void Log(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            S.battleLog.Add(line);
            while (S.battleLog.Count > 8) S.battleLog.RemoveAt(0);
        }

        // ------------------------------------------------------------------ states

        public bool HasState(GridUnit u, string name)
        {
            foreach (var s in u.states) if (s.StartsWith(name + ":")) return true;
            return false;
        }

        /// <summary>
        /// Gives someone a state for some rounds. States count down at the end of their own side's turn, so a state
        /// put on someone during their own side's turn gets one more round (or it would be gone before it mattered).
        /// </summary>
        void AddState(GridUnit u, string name, int rounds)
        {
            if (G.phase == u.side) rounds++;
            int had = 0;
            for (int i = u.states.Count - 1; i >= 0; i--)
                if (u.states[i].StartsWith(name + ":")) { int.TryParse(u.states[i].Substring(name.Length + 1), out had); u.states.RemoveAt(i); }
            u.states.Add(name + ":" + Math.Max(had, rounds));
        }

        void ClearState(GridUnit u, string name) => u.states.RemoveAll(s => s.StartsWith(name + ":"));

        void TickStates(string side)
        {
            foreach (var u in G.units.Where(u => u.side == side && !u.gone))
                for (int i = u.states.Count - 1; i >= 0; i--)
                {
                    var p = u.states[i].Split(':');
                    int n = int.Parse(p[1]) - 1;
                    if (n <= 0) u.states.RemoveAt(i);
                    else u.states[i] = p[0] + ":" + n;
                }
        }

        /// <summary>The states worth showing on someone, in words: Open, Vulnerable, Hobbled...</summary>
        public List<string> StatesOf(GridUnit u) =>
            u.states.Select(s => s.Split(':')[0]).Where(s => !s.StartsWith("cd_")).Distinct().ToList();

        // ------------------------------------------------------------------ how the enemies think

        public EnemyDef FoeDef(GridUnit u) => data.Enemy(u.def);

        IEnumerable<GridUnit> StandingParty => G.units.Where(u => u.side == "party" && !u.gone && !u.down);

        /// <summary>Picks a move by weight, from those that pass the test.</summary>
        EnemyMove PickMove(EnemyDef e, Func<EnemyMove, bool> ok)
        {
            var list = e.moves.Where(ok).ToList();
            if (list.Count == 0) return e.moves.FirstOrDefault(m => m.Attacks()) ?? e.moves[0];
            int total = list.Sum(m => Math.Max(1, m.weight));
            int r = Roll(1, total);
            foreach (var m in list)
            {
                r -= Math.Max(1, m.weight);
                if (r <= 0) return m;
            }
            return list[0];
        }

        static bool Plain(EnemyMove m) => !m.charge && m.KindOf() != "howl";

        /// <summary>At the start of your turn, each enemy decides what it'll do, and shows it.</summary>
        void PlanFoe(GridUnit u)
        {
            var e = FoeDef(u);
            u.intent = ""; u.intentTarget = ""; u.intentDir = ""; u.lane.Clear();
            if (HasState(u, "fleeing")) { u.intent = "flee"; return; }
            var party = StandingParty.ToList();
            if (party.Count == 0) { u.intent = "wait"; return; }
            GridUnit Nearest() => party.OrderBy(p => Dist(u, p)).ThenBy(p => p.id == "you" ? 0 : 1).First();
            GridUnit adjacent = party.Where(p => Dist(u, p) == 1).OrderBy(p => SideOf(u, p.x, p.y) == "front" ? 0 : 1).FirstOrDefault();

            switch (e.ai)
            {
                case "charger":
                {
                    if (adjacent != null)
                    {
                        var m = PickMove(e, x => Plain(x));
                        u.intent = m.id; u.intentTarget = adjacent.id;
                        return;
                    }
                    var charge = e.moves.FirstOrDefault(x => x.charge);
                    if (charge != null && !HasState(u, "hobbled") && !HasState(u, "dazed"))
                    {
                        var lanes = new List<(string dir, List<int> lane, GridUnit first)>();
                        foreach (var dir in new[] { "left", "up", "down", "right" })
                        {
                            var (lane, first) = Lane(u, u.x, u.y, dir);
                            if (first != null && Dist(u, first) >= 2) lanes.Add((dir, lane, first));
                        }
                        if (lanes.Count > 0 && Roll(1, 100) <= 80)
                        {
                            var pick = lanes.OrderBy(l => Dist(u, l.first)).First();
                            u.intent = charge.id; u.intentDir = pick.dir; u.intentTarget = pick.first.id;
                            u.lane.AddRange(pick.lane);
                            u.facing = pick.dir;
                            return;
                        }
                    }
                    var t = Nearest();
                    if (charge != null && !HasState(u, "hobbled") && FoeApproach(u, t, false) == null) { u.intent = "line"; u.intentTarget = t.id; return; }
                    var mv = PickMove(e, x => Plain(x) && x.Attacks());
                    u.intent = mv.id; u.intentTarget = t.id;
                    return;
                }
                case "leader":
                {
                    if (!G.leaderHowled && G.round == 1)
                    {
                        var howl = e.moves.FirstOrDefault(x => x.KindOf() == "howl");
                        if (howl != null) { u.intent = howl.id; return; }
                    }
                    bool pack = G.units.Any(o => o != u && o.side == "foe" && !o.gone && !HasState(o, "fleeing"));
                    if (pack && adjacent == null) { u.intent = "hang"; return; }
                    var t = adjacent ?? HuntTarget(u, party);
                    var m = PickMove(e, x => Plain(x) && x.Attacks());
                    u.intent = m.id; u.intentTarget = t.id;
                    return;
                }
                case "hunter":
                {
                    var t = HuntTarget(u, party);
                    var m = PickMove(e, x => Plain(x));
                    u.intent = m.id; u.intentTarget = t.id;
                    return;
                }
                case "soldier":
                {
                    int round = party.Count(p => Dist(u, p) == 1);
                    var guard = e.moves.FirstOrDefault(x => x.KindOf() == "guard");
                    if (round >= 2 && guard != null) { u.intent = guard.id; return; }
                    var hitter = party.FirstOrDefault(p => p.id == u.hitByWho);
                    var t = adjacent ?? hitter ?? Nearest();
                    var m = PickMove(e, x => Plain(x) && x.Attacks());
                    u.intent = m.id; u.intentTarget = t.id;
                    return;
                }
                default:   // brute: the nearest, with whatever it's got
                {
                    var t = adjacent ?? Nearest();
                    var m = PickMove(e, x => Plain(x));
                    u.intent = m.id; u.intentTarget = t.id;
                    return;
                }
            }
        }

        /// <summary>Who a wolf goes for: the leader's marked prey, else whoever's alone or most hurt.</summary>
        GridUnit HuntTarget(GridUnit u, List<GridUnit> party)
        {
            var prey = party.FirstOrDefault(p => HasState(p, "prey"));
            if (prey != null) return prey;
            return party.OrderBy(p => (party.Any(o => o != p && Dist(o, p) <= 1) ? 1f : 0f) + Hp(p) / Math.Max(1, MaxHpOf(p)) + Dist(u, p) * 0.05f).First();
        }

        /// <summary>The squares a charge would cross going that way, and the first of your side standing in it.</summary>
        (List<int> lane, GridUnit first) Lane(GridUnit u, int x, int y, string dir)
        {
            var (dx, dy) = Dir(dir);
            var lane = new List<int>();
            GridUnit first = null;
            for (int i = 1; i <= ChargeLength; i++)
            {
                int nx = x + dx * i, ny = y + dy * i;
                if (!OnGrid(nx, ny) || Solid(nx, ny)) break;
                var o = At(nx, ny);
                if (o != null && o != u && o.side == u.side) break;
                lane.Add(nx + ny * G.w);
                if (o != null && o.side != u.side && first == null && !o.down) first = o;
            }
            return (lane, first);
        }

        /// <summary>What an enemy is about to do, in words: its tell.</summary>
        public string IntentText(GridUnit u)
        {
            if (u == null || u.side != "foe") return "";
            var e = FoeDef(u);
            switch (u.intent)
            {
                case "flee": return "It's had enough. It's going to run.";
                case "hang": return "It hangs back and lets its wolves do the work, watching for an opening.";
                case "line": return "It swings round, snorting, trying to get one of you in a straight line.";
                case "wait": case "": return "";
            }
            var m = e.moves.FirstOrDefault(x => x.id == u.intent);
            return m?.tell ?? "";
        }

        /// <summary>The enemy's move it's about to make (null for a plan that isn't a move, like hanging back).</summary>
        public EnemyMove IntentMove(GridUnit u) => u == null || u.side != "foe" ? null : FoeDef(u).moves.FirstOrDefault(x => x.id == u.intent);

        /// <summary>One enemy's turn.</summary>
        bool FoeTurn(GridUnit u)
        {
            var e = FoeDef(u);
            if (G.enemyStage == 1)
            {
                AfterFoeAttack(u);
                return true;
            }
            u.moveLeft = MoveOf(u);
            if (HasState(u, "staggered"))
            {
                Log(Cap(UnitName(u)) + " is still reeling and can't act.");
                Events.Add(new BattleEvent { target = u.id, kind = "stun", text = "Staggered" });
                return true;
            }
            if (HasState(u, "fleeing")) { FoeRuns(u); return true; }
            var move = IntentMove(u);
            if (move != null && (move.charge || move.KindOf() == "howl") && HasState(u, "dazed"))
            {
                Log(Cap(UnitName(u)) + " shakes its head, dazed. Whatever it was winding up to, it's lost it.");
                Events.Add(new BattleEvent { target = u.id, kind = "stun", text = "Dazed" });
                return true;
            }
            switch (u.intent)
            {
                case "hang":
                    FoeHangsBack(u);
                    return true;
                case "line":
                    FoeLinesUp(u);
                    return true;
                case "wait": case "":
                    return true;
            }
            if (move == null) return true;
            string kind = move.KindOf();
            if (kind == "howl")
            {
                G.leaderHowled = true;
                if (u.hitBy > 0)
                {
                    Log("You cut " + UnitName(u) + "'s howl short.");
                    Events.Add(new BattleEvent { target = u.id, kind = "stun", text = "Cut short" });
                    return true;
                }
                Log(Cap(UnitName(u)) + " " + move.name + ". The whole pack is in a rage now: their bites will land harder.");
                Events.Add(new BattleEvent { from = u.id, target = u.id, kind = "howl", text = "Howl" });
                foreach (var o in G.units.Where(o => o.side == "foe" && !o.gone)) AddState(o, "enraged", 999);
                return true;
            }
            if (kind == "guard")
            {
                AddState(u, "guard", 1);
                Log(Cap(UnitName(u)) + " " + move.name + ".");
                Events.Add(new BattleEvent { target = u.id, kind = "guard", text = "On guard" });
                return true;
            }
            if (move.charge) { Charge(u, move); return true; }

            // an ordinary blow: get to whoever it's after (or whoever's nearer now), then strike
            var t = Unit(u.intentTarget);
            if (t == null || t.gone || t.down) t = StandingParty.OrderBy(p => Dist(u, p)).FirstOrDefault();
            if (t == null) return true;
            if (Dist(u, t) > 1)
            {
                var spot = FoeApproach(u, t, e.ai == "hunter" || e.ai == "leader");
                if (spot != null) FoeWalk(u, spot.Value.x, spot.Value.y);
                else FoeWalkToward(u, t);
                if (u.gone || G.phase == "over") return true;
            }
            if (Dist(u, t) != 1)
            {
                // still out of reach: anyone else in reach will do
                var other = StandingParty.FirstOrDefault(p => Dist(u, p) == 1);
                if (other == null) return true;
                t = other;
            }
            if (!FoeAttack(u, t, move)) { G.enemyStage = 1; return false; }
            if (G.phase == "over") return true;
            AfterFoeAttack(u);
            return true;
        }

        /// <summary>After a bite, a wolf with legs left in it backs off a step: hit and run.</summary>
        void AfterFoeAttack(GridUnit u)
        {
            if (G.phase == "over" || u.gone || FoeDef(u).ai != "hunter" || u.moveLeft <= 0) return;
            var reach = Flood(u, 1);
            var away = reach.Keys.Where(k => k != u.x + u.y * G.w && At(k % G.w, k / G.w) == null)
                            .Where(k => !StandingParty.Any(p => Math.Abs(p.x - k % G.w) + Math.Abs(p.y - k / G.w) <= 1)).ToList();
            if (away.Count == 0) return;
            int sq = away[0];
            FoeWalk(u, sq % G.w, sq / G.w);
        }

        /// <summary>The square next to the target it'll go for: behind or beside them if it's a hunter. null: can't get there this turn.</summary>
        (int x, int y)? FoeApproach(GridUnit u, GridUnit t, bool flank)
        {
            var reach = Flood(u, MoveOf(u));
            (int x, int y)? best = null;
            float bestScore = float.MinValue;
            foreach (var (dx, dy) in Steps)
            {
                int x = t.x + dx, y = t.y + dy;
                int k = x + y * G.w;
                if (!OnGrid(x, y) || !reach.TryGetValue(k, out int steps)) continue;
                var o = At(x, y);
                if (o != null && o != u) continue;
                string side = SideOf(t, x, y);
                float score = -steps * 0.1f;
                if (flank) score += side == "back" ? 3 : side == "side" ? 2 : 1;
                score -= StandingParty.Count(p => p != t && Math.Abs(p.x - x) + Math.Abs(p.y - y) == 1) * 1.2f;
                if (score > bestScore) { bestScore = score; best = (x, y); }
            }
            return best;
        }

        static readonly (int dx, int dy)[] Steps = { (1, 0), (-1, 0), (0, 1), (0, -1) };

        /// <summary>Can't reach them this turn: get as close as it can.</summary>
        void FoeWalkToward(GridUnit u, GridUnit t)
        {
            var reach = Flood(u, MoveOf(u));
            int best = -1, bestD = Dist(u, t);
            foreach (var kv in reach)
            {
                int x = kv.Key % G.w, y = kv.Key / G.w;
                if (At(x, y) != null && At(x, y) != u) continue;
                int d = Math.Abs(x - t.x) + Math.Abs(y - t.y);
                if (d < bestD) { bestD = d; best = kv.Key; }
            }
            if (best >= 0) FoeWalk(u, best % G.w, best / G.w);
        }

        /// <summary>The pack leader keeps its distance while its wolves work: two squares from anyone if it can.</summary>
        void FoeHangsBack(GridUnit u)
        {
            var reach = Flood(u, MoveOf(u));
            int best = -1;
            float bestScore = float.MinValue;
            foreach (var kv in reach)
            {
                int x = kv.Key % G.w, y = kv.Key / G.w;
                if (At(x, y) != null && At(x, y) != u) continue;
                int near = StandingParty.Select(p => Math.Abs(p.x - x) + Math.Abs(p.y - y)).DefaultIfEmpty(9).Min();
                float score = -Math.Abs(near - 3) - kv.Value * 0.1f;
                if (score > bestScore) { bestScore = score; best = kv.Key; }
            }
            if (best >= 0 && best != u.x + u.y * G.w) FoeWalk(u, best % G.w, best / G.w);
            var t = StandingParty.OrderBy(p => Dist(u, p)).FirstOrDefault();
            if (t != null) u.facing = FaceTo(t.x - u.x, t.y - u.y);
            Log(Cap(UnitName(u)) + " circles at the back, watching you.");
        }

        /// <summary>The boar turns to get one of you in a straight line for next time.</summary>
        void FoeLinesUp(GridUnit u)
        {
            var reach = Flood(u, MoveOf(u));
            int best = -1;
            float bestScore = float.MinValue;
            foreach (var kv in reach)
            {
                int x = kv.Key % G.w, y = kv.Key / G.w;
                if (At(x, y) != null && At(x, y) != u) continue;
                float score = -kv.Value * 0.2f;
                foreach (var dir in new[] { "left", "up", "down", "right" })
                {
                    var (_, first) = Lane(u, x, y, dir);
                    if (first != null && Math.Abs(first.x - x) + Math.Abs(first.y - y) >= 2) { score += 5; break; }
                }
                if (score > bestScore) { bestScore = score; best = kv.Key; }
            }
            if (best >= 0 && best != u.x + u.y * G.w) FoeWalk(u, best % G.w, best / G.w);
            var t = StandingParty.OrderBy(p => Dist(u, p)).FirstOrDefault();
            if (t != null) u.facing = FaceTo(t.x - u.x, t.y - u.y);
            Log(Cap(UnitName(u)) + " wheels round, snorting, lining you up.");
        }

        /// <summary>A beast that's had enough runs for the far edge, and is gone when it gets there.</summary>
        void FoeRuns(GridUnit u)
        {
            var reach = Flood(u, MoveOf(u) + 1);
            int best = -1, bestX = -1;
            foreach (var kv in reach)
            {
                int x = kv.Key % G.w, y = kv.Key / G.w;
                if (At(x, y) != null && At(x, y) != u) continue;
                if (x > bestX) { bestX = x; best = kv.Key; }
            }
            if (best >= 0 && best != u.x + u.y * G.w) FoeWalk(u, best % G.w, best / G.w);
            if (!u.gone && (u.x >= G.w - 1 || best < 0))
            {
                Log(Cap(UnitName(u)) + " crashes off into the brush and is gone.");
                Gone(u, "fled", "flee");
            }
            CheckEnd();
        }

        /// <summary>An enemy walks to a square, step by step; walking away from one of you who's facing it costs it a free hit.</summary>
        void FoeWalk(GridUnit u, int x, int y)
        {
            var path = GridPath(u, x, y);
            if (path == null || path.Count == 0) return;
            WalkPath(u, path);
        }

        // ------------------------------------------------------------------ the boar's charge

        void Charge(GridUnit u, EnemyMove move)
        {
            var e = FoeDef(u);
            var (dx, dy) = Dir(u.intentDir);
            u.facing = u.intentDir;
            var path = new List<int>();
            GridUnit hit = null;
            foreach (int sq in u.lane)
            {
                int x = sq % G.w, y = sq / G.w;
                var o = At(x, y);
                if (o != null && o != u)
                {
                    if (o.side == "party") hit = o;
                    break;
                }
                if (Solid(x, y)) break;
                path.Add(sq);
                u.x = x; u.y = y;
            }
            bool wall = hit == null && OnGrid(u.x + dx, u.y + dy) && Solid(u.x + dx, u.y + dy);
            Events.Add(new BattleEvent { from = u.id, target = hit?.id ?? "", kind = "charge", path = path });
            if (hit != null)
            {
                float dmg = Roll(e.atkMin, e.atkMax) * move.power * FoeRage(u);
                if (HasState(hit, "braced"))
                {
                    Log((hit.id == "you" ? "You're braced. " : UnitName(hit) + " is braced. ") + Cap(UnitName(u)) + " slams into " + (hit.id == "you" ? "you" : UnitName(hit)) +
                        " and stops dead, stunned, its flank wide open!");
                    Events.Add(new BattleEvent { from = u.id, target = hit.id, kind = "braced", text = "Braced!" });
                    AddState(u, "vulnerable", 1);
                    AddState(u, "open", 1);
                    Events.Add(new BattleEvent { target = u.id, kind = "open", text = "Open!" });
                    if (hit.id == "you") { Rules.Train(S, Stat.End, 1f, "taking a charge"); if (HasShield()) TrainSkill("shield", 2, "bracing"); }
                    LandFoeHit(u, hit, move, dmg * 0.25f, "front", true, null);
                    return;
                }
                // knocked flat, and back a square if there's room
                int bx = hit.x + dx, by = hit.y + dy;
                if (OnGrid(bx, by) && !Solid(bx, by) && At(bx, by) == null)
                {
                    hit.x = bx; hit.y = by;
                    Events.Add(new BattleEvent { from = u.id, target = hit.id, kind = "knock", path = new List<int> { bx + by * G.w } });
                }
                AddState(hit, "prone", 1);
                Log(Cap(UnitName(u)) + " " + move.name.Replace(" you", hit.id == "you" ? " you" : " " + UnitName(hit)) + ", and knocks " + (hit.id == "you" ? "you" : UnitName(hit)) + " flat.");
                LandFoeHit(u, hit, move, dmg, "front", false, "");
                return;
            }
            if (wall)
            {
                Log(Cap(UnitName(u)) + " charges into the " + (data.Tile(Square(u.x + dx, u.y + dy))?.name ?? "tree").ToLowerInvariant() + " head first and staggers, dazed!");
                Events.Add(new BattleEvent { target = u.id, kind = "stun", text = "Stunned" });
                AddState(u, "staggered", 1);
                AddState(u, "vulnerable", 1);
                return;
            }
            Log(Cap(UnitName(u)) + " thunders down the lane at nothing, and pulls up.");
        }

        // ------------------------------------------------------------------ an enemy's blow

        float FoeRage(GridUnit u) => (HasState(u, "enraged") ? EnragedHits : 1f) * (HasState(u, "weakened") ? WeakenedHits : 1f);

        /// <summary>An enemy strikes one of you. Returns false when it waits for you to read and react.</summary>
        bool FoeAttack(GridUnit u, GridUnit t, EnemyMove move)
        {
            var e = FoeDef(u);
            u.facing = FaceTo(t.x - u.x, t.y - u.y);
            string side = SideOf(t, u.x, u.y);
            float dmg = Roll(e.atkMin, e.atkMax) * move.power * FoeRage(u);
            if (side == "back") dmg *= BackDamage;
            Events.Add(new BattleEvent { from = u.id, target = t.id, kind = "windup", text = "" });   // the screen shows it gathering itself
            if (t.id == "you" && CanReact(t, side))
            {
                string kind = HasState(t, "guard") ? (HasShield() ? "block" : "parry") : "dodge";
                float r = SpeedRatio(u);
                if (r >= Untouchable)
                {
                    // far quicker than it: you see it all coming, feints too
                    Log("It's slow next to you. You see it coming the whole way.");
                    Defend(u, t, move, dmg, kind, side, TimingGood, true);
                    return true;
                }
                if (r < 1f)
                {
                    // quicker than you: no time to do anything but take it (on your guard, if it's up)
                    t.reaction = false;
                    if (kind == "dodge") LandFoeHit(u, t, move, dmg, side, false, Cap(UnitName(u)) + " is too quick for you.");
                    else Defend(u, t, move, dmg, kind, side, kind == "block" ? TimingGood : TimingMiss, true);
                    return true;
                }
                // read and react: where the blow comes from, and (a feint) where it pretends to
                G.reactAttacker = u.id;
                G.reactTarget = t.id;
                G.reactMove = move.id;
                G.reactKind = kind;
                G.reactSide = side;
                G.reactDamage = dmg;
                G.reactCue = Cues[Roll(0, 3)];
                G.reactFake = move.KindOf() == "feint" ? Cues.Where(c => c != G.reactCue).ToList()[Roll(0, 2)] : "";
                return false;
            }
            // your companions get out of the way by themselves, or don't
            bool covered = false;
            string note = null;
            if (t.id != "you" && t.reaction && side != "back" && !HasState(t, "staggered") && !HasState(t, "dazed") && !HasState(t, "prone"))
            {
                t.reaction = false;
                if (HasState(t, "guard")) { dmg *= 0.5f; covered = true; note = UnitName(t) + " takes it on guard."; }
                else if (!HasState(t, "hobbled") && Roll(1, 100) <= 15 + 3 * Rules.MemberLevel(Member(t.def)))
                {
                    if (move.KindOf() == "feint") { dmg *= 1.4f; note = UnitName(t) + " jumps aside, but it was a feint."; }
                    else
                    {
                        Log(Cap(UnitName(u)) + " " + Aimed(move.name, t) + ", but " + UnitName(t) + " gets out of the way.");
                        Events.Add(new BattleEvent { from = u.id, target = t.id, kind = "dodge", text = "Dodged" });
                        return true;
                    }
                }
            }
            if (t.id == "you" && side == "back") note = "It gets you from behind.";
            else if (t.id == "you" && Rules.Winded(S, data) && !HasState(t, "guard")) note = "You're too winded to get out of the way.";
            LandFoeHit(u, t, move, dmg, side, covered, note);
            return true;
            return true;
        }

        /// <summary>Can you see this one coming and react (dodge, or parry or block when on guard)?</summary>
        bool CanReact(GridUnit t, string side)
        {
            if (!t.reaction || side == "back" || HasState(t, "staggered") || HasState(t, "dazed") || HasState(t, "prone")) return false;
            if (HasState(t, "guard")) return true;
            return !HasState(t, "hobbled") && !Rules.Winded(S, data);
        }

        /// <summary>How well you meet a blow (no timing bar since 25 Oct): your Agility for a dodge, your weapon
        /// skill (or shield) and Agility for a parry or block. 0 caught, 1 good, 2 perfect.</summary>
        int ReactRoll(string kind)
        {
            int agi = S.stats[Stat.Agi];
            int chance = kind == "dodge" ? DodgeBase + DodgePerAgi * agi + (Rules.HasPerk(S, data, "nimble") ? NimbleDodgeBonus : 0)
                       : ParryBase + 2 * SkillLevel(kind == "block" ? "shield" : WeaponSkill()) + 2 * agi + (kind == "block" ? BlockBonus : 0);
            chance += (int)Math.Round(Rules.TraitSum(S, data, t => t.zone) * 100);   // Balanced gear
            chance = Math.Clamp(chance, 10, 90);
            int r = Roll(1, 100);
            return r <= chance / 4 ? TimingPerfect : r <= chance ? TimingGood : TimingMiss;
        }

        public const int DodgeBase = 40, DodgePerAgi = 4, NimbleDodgeBonus = 15, ParryBase = 50, BlockBonus = 15;

        // ------------------------------------------------------------------ read and react (David, 25 Oct)
        //
        // When an enemy goes for you and you're at least as quick as it, the fight stops for you: the screen shows
        // which way the blow comes from (left, right, up or down, picked at random each time), and you answer with
        // a direction. Away from it dodges (or blocks, on guard). Into it counters, if you're the quicker one. The
        // wrong way, too early or too late, and it lands. A feint shows one way first, then comes from another.
        // How long you get depends on how much quicker you are. Twice as quick: it never touches you.
        // Slower than it: no chance to react at all. The Auto setting answers from your stats instead.

        public static readonly string[] Cues = { "left", "right", "up", "down" };
        public const float Untouchable = 2f;        // this many times quicker and it can't touch you
        public const float ReactBase = 0.6f;         // seconds to answer at the same speed (times the speed ratio squared)

        public bool AwaitingReaction => InGrid && !string.IsNullOrEmpty(G.reactAttacker) && !string.IsNullOrEmpty(G.reactCue);
        /// <summary>Where the blow comes from: left, right, up or down (on the screen, around you).</summary>
        public string ReactCue => G.reactCue;
        /// <summary>A feint: where it pretends to come from first ("" if it's not a feint).</summary>
        public string ReactFake => G.reactFake;
        public string ReactKindNow => G.reactKind;
        public GridUnit ReactAttacker => AwaitingReaction ? Unit(G.reactAttacker) : null;
        /// <summary>You're quicker than it: going into the blow counters.</summary>
        public bool ReactCanCounter => AwaitingReaction && SpeedRatio(Unit(G.reactAttacker)) > 1f;
        /// <summary>The boar fight teaches it: no clock, and an explanation first (until it's been fought once).</summary>
        public bool ReactTutorial => AwaitingReaction && S.enemy == "boar" && !S.Has("react_taught");
        /// <summary>Seconds to answer (0: as long as you like).</summary>
        public float ReactWindow => !AwaitingReaction || ReactTutorial ? 0f : Math.Min(2.5f, ReactBase * Sq(SpeedRatio(Unit(G.reactAttacker))));
        static float Sq(float x) => x * x;

        /// <summary>Your speed over this enemy's: above 1 you're the quicker.</summary>
        public float SpeedRatio(GridUnit foe) => foe == null ? 1f : Rules.Speed(S, data) / (float)Math.Max(1, FoeDef(foe).speed);

        public static string Opposite(string d) => d == "left" ? "right" : d == "right" ? "left" : d == "up" ? "down" : d == "down" ? "up" : "";

        /// <summary>Your answer to the blow: a direction (left, right, up, down), "early" (you went before it showed),
        /// "late" (too slow), or "auto" (your stats decide). The enemies then carry on.</summary>
        public bool React(string answer)
        {
            if (!AwaitingReaction) return false;
            Events.Clear();
            var u = Unit(G.reactAttacker);
            var t = Unit(G.reactTarget);
            var move = FoeDef(u).moves.First(m => m.id == G.reactMove);
            float dmg = G.reactDamage;
            string kind = G.reactKind, side = G.reactSide, cue = G.reactCue, fake = G.reactFake;
            bool counter = SpeedRatio(u) > 1f;
            ClearReact();
            string foe = Cap(UnitName(u));
            if (answer == "auto") Defend(u, t, move, dmg, kind, side, ReactRoll(kind), false);
            else if (answer == Opposite(cue)) Defend(u, t, move, dmg, kind, side, TimingGood, true);
            else if (answer == cue && counter)
            {
                if (kind != "dodge") Defend(u, t, move, dmg, kind, side, TimingPerfect, true);
                else
                {
                    // inside the blow before it lands
                    t.reaction = false;
                    int hit = Math.Max(1, (int)Math.Round(WeaponHit() * Rules.ParryCounter));
                    Log(foe + " " + move.name + ", but you're quicker: you slip inside it and strike first for " + hit + "!");
                    Events.Add(new BattleEvent { from = u.id, target = t.id, kind = "dodge", text = "Counter!" });
                    Events.Add(new BattleEvent { from = t.id, target = u.id, kind = "hit", amount = hit, text = hit.ToString() });
                    Rules.Train(S, Stat.Agi, 1f, "countering");
                    TrainSkill(WeaponSkill(), 2, "countering");
                    DamageFoe(u, hit, t);
                }
            }
            else
            {
                // the wrong way, too early or too late. Going for the feint's first move is exactly what it wanted.
                bool fooled = fake != "" && (answer == "early" || answer == Opposite(fake) || answer == fake);
                string note = answer == "late" ? "Too slow." : answer == "early" ? "You moved before it did." : fooled ? "" : "Wrong way.";
                if (note != "") Log(note);
                Defend(u, t, move, dmg, kind, side, TimingMiss, !fooled);
            }
            Rules.Clamp(S, data);
            if (G.phase == "over" || CheckEnd()) return true;
            RunFoes();
            return true;
        }

        void ClearReact()
        {
            G.reactAttacker = G.reactTarget = G.reactMove = G.reactKind = G.reactSide = G.reactCue = G.reactFake = "";
            G.reactDamage = 0;
        }

        /// <summary>You dodge (or parry or block, on guard) an enemy's blow: 0 caught, 1 good, 2 perfect.
        /// sawFeint: you read a feint for what it was (otherwise a feint catches a dodge).</summary>
        void Defend(GridUnit u, GridUnit t, EnemyMove move, float dmg, string kind, string side, int timing, bool sawFeint)
        {
            t.reaction = false;
            string mk = move.KindOf();
            string foe = Cap(UnitName(u));
            if (kind == "dodge")
            {
                S.stamina -= Rules.HasPerk(S, data, "nimble") ? Rules.NimbleDodge : Rules.DodgeStamina;
                if (mk == "feint" && !sawFeint)
                {
                    dmg *= 1.4f;
                    LandFoeHit(u, t, move, dmg, side, false, "You throw yourself aside, but it was a feint. It catches you as you land.");
                }
                else if (timing >= TimingGood)
                {
                    bool opens = mk == "heavy" || timing == TimingPerfect;
                    Log(foe + " " + move.name + ", but you slip aside" + (opens ? " and it overreaches. It's wide open!" : "."));
                    Events.Add(new BattleEvent { from = u.id, target = t.id, kind = "dodge", text = "Dodged" });
                    if (opens)
                    {
                        AddState(u, "open", 1);
                        S.stamina += Rules.DodgeStamina;
                        Events.Add(new BattleEvent { target = u.id, kind = "open", text = "Open!" });
                    }
                    Rules.Train(S, Stat.Agi, 1f, "dodging");
                }
                else LandFoeHit(u, t, move, dmg, side, false, null);
            }
            else
            {
                // on guard: parry with a blade, block with a shield
                bool shield = kind == "block";
                if (timing == TimingPerfect && (mk != "heavy" || shield))
                {
                    int counter = Math.Max(1, (int)Math.Round(WeaponHit() * Rules.ParryCounter));
                    Log(foe + " " + move.name + ", but you turn it aside and strike back for " + counter + "!");
                    Events.Add(new BattleEvent { from = u.id, target = t.id, kind = "parry", text = shield ? "Blocked!" : "Parry!" });
                    Events.Add(new BattleEvent { from = t.id, target = u.id, kind = "hit", amount = counter, text = counter.ToString() });
                    Rules.Train(S, Stat.Agi, 1f, "parrying");
                    if (shield) TrainSkill("shield", 2, "blocking");
                    DamageFoe(u, counter, t);
                }
                else if (mk == "heavy")
                {
                    dmg *= timing == TimingPerfect ? 0.45f : timing == TimingGood ? 0.65f : 0.9f;
                    S.stamina -= 10;
                    LandFoeHit(u, t, move, dmg, side, true, "The blow smashes through your guard and knocks the wind out of you.");
                }
                else
                {
                    dmg *= timing == TimingGood ? 0.4f : 0.7f;
                    if (shield) TrainSkill("shield", 1.5f, "blocking");
                    LandFoeHit(u, t, move, dmg, side, true, timing == TimingGood ? "You catch it on your guard." : "You get your guard up, just.");
                }
            }
            Rules.Clamp(S, data);
            CheckEnd();
        }

        /// <summary>"lunges at your throat" said of a companion: "lunges at Rook's throat".</summary>
        string Aimed(string moveName, GridUnit t) =>
            t.id == "you" ? moveName : moveName.Replace("your", UnitName(t) + "'s").Replace(" you", " " + UnitName(t));

        /// <summary>A blow from an enemy lands on one of you.</summary>
        void LandFoeHit(GridUnit u, GridUnit t, EnemyMove move, float dmg, string side, bool covered, string note)
        {
            if (!string.IsNullOrEmpty(note)) Log(note);
            if (HasState(t, "vulnerable")) dmg *= VulnerableTaken;
            if (t.id == "you")
            {
                if (Rules.Winded(S, data)) dmg *= Rules.WindedTaken;
                dmg *= 1f - Rules.GearGuard(S, data);
                dmg *= Rules.QuirkMul(S, data, q => q.taken);
            }
            int n = Math.Max(0, (int)Math.Round(dmg));
            string mk = move.KindOf();
            if (!move.charge) Log(Cap(UnitName(u)) + " " + Aimed(move.name, t) + " for " + n + ".");
            else Log((t.id == "you" ? "You take " : UnitName(t) + " takes ") + n + ".");
            Events.Add(new BattleEvent { from = move.charge ? "" : u.id, target = t.id, kind = mk == "heavy" && !covered ? "crit" : "hit", amount = n, text = n.ToString() });
            if (t.id == "you")
            {
                S.hp -= n;
                if (n > 0) WearArmour(mk == "heavy" ? 2f : 1f);
                if (covered) Rules.Train(S, Stat.End, 0.5f, "taking hits");
                if (S.hp > 0) MaybeWound(move, S.body, null, n, covered);
                else S.deathCause = "You were killed by " + FoeName(FoeDef(u)) + ".";
                CheckEnd();
                return;
            }
            HurtParty(t, n, "a blow from " + FoeName(FoeDef(u)), move, covered);
        }

        /// <summary>A companion takes damage: down at 0, killed outright by a big enough blow or a blow while they're down.</summary>
        void HurtParty(GridUnit t, int n, string cause, EnemyMove move = null, bool covered = false)
        {
            if (t.id == "you")
            {
                S.hp -= n;
                if (S.hp <= 0 && string.IsNullOrEmpty(S.deathCause)) S.deathCause = "You died of " + cause + ".";
                CheckEnd();
                return;
            }
            var m = Member(t.def);
            var f = data.Follower(t.def);
            if (m == null || f == null) { Gone(t, "killed", "dead"); return; }
            bool wasDown = t.down;
            m.hp -= n;
            float max = Rules.MemberMaxHp(m, f);
            if (wasDown || m.hp <= -max * 0.5f)
            {
                Log(f.name + " is killed!");
                Gone(t, "killed", "dead");
                Rules.Die(S, data, m, cause);
            }
            else if (m.hp <= 0)
            {
                m.hp = 0;
                t.down = true;
                t.bleedOut = BleedOutRounds;
                t.acted = true;
                t.moveLeft = 0;
                t.states.RemoveAll(s => !s.StartsWith("cd_"));
                Log(f.name + " goes down! Get to them within " + BleedOutRounds + " rounds or they'll bleed out.");
                Events.Add(new BattleEvent { target = t.id, kind = "down", text = "Down!" });
            }
            else if (move != null) MaybeWound(move, m.body, f.name, n, covered);
            CheckEnd();
        }

        /// <summary>Someone leaves the field: killed, fled, or given up.</summary>
        void Gone(GridUnit u, string how, string eventKind)
        {
            u.gone = true;
            u.goneHow = how;
            u.down = false;
            Events.Add(new BattleEvent { target = u.id, kind = eventKind, text = how == "killed" ? "" : how == "fled" ? "Fled" : "Yields" });
        }

        /// <summary>One of you hurt an enemy. It might die, run, give up, or fly into a rage.</summary>
        void DamageFoe(GridUnit u, int n, GridUnit by)
        {
            var e = FoeDef(u);
            u.hp -= n;
            if (by != null && n > u.hitBy) { u.hitBy = n; u.hitByWho = by.id; }
            if (u.hp <= 0)
            {
                Log(Cap(UnitName(u)) + " goes down and stays down.");
                Gone(u, "killed", "dead");
                if (e.ai == "leader")
                {
                    var rest = G.units.Where(o => o.side == "foe" && !o.gone).ToList();
                    if (rest.Count > 0) Log("With their leader dead, the pack breaks and runs.");
                    foreach (var o in rest) AddState(o, "fleeing", 999);
                }
                CheckEnd();
                return;
            }
            if (e.enrageBelow > 0 && u.hp < u.maxHp * e.enrageBelow && !HasState(u, "fury"))
            {
                u.states.Add("fury:999");
                AddState(u, "enraged", 999);
                Log(Cap(UnitName(u)) + " is bleeding badly, and it's furious now. Its blows will hit harder.");
                Events.Add(new BattleEvent { target = u.id, kind = "rage", text = "Enraged" });
            }
            if (e.flee > 0 && u.hp < u.maxHp * e.flee && !HasState(u, "fleeing"))
            {
                if (!string.IsNullOrEmpty(e.figure))
                {
                    Log(Cap(UnitName(u)) + " throws down his weapon. \"Enough! I yield, I yield!\"");
                    Gone(u, "surrendered", "yield");
                }
                else
                {
                    Log(Cap(UnitName(u)) + " has had enough. It turns to run.");
                    AddState(u, "fleeing", 999);
                    Events.Add(new BattleEvent { target = u.id, kind = "state", text = "Fleeing" });
                }
            }
            CheckEnd();
        }

        // ------------------------------------------------------------------ names

        /// <summary>"you", "Rook", "the wolf" (or "the second wolf" when there are more).</summary>
        public string UnitName(GridUnit u)
        {
            if (u.id == "you") return "you";
            if (u.side == "party") return data.Follower(u.def)?.name ?? u.def;
            var e = FoeDef(u);
            int same = G.units.Count(o => o.def == u.def);
            if (same <= 1) return FoeName(e);
            int i = G.units.Where(o => o.def == u.def).ToList().IndexOf(u);
            string[] nth = { "first", "second", "third", "fourth", "fifth" };
            return "the " + nth[Math.Min(i, nth.Length - 1)] + " " + e.name.ToLowerInvariant();
        }

        // ------------------------------------------------------------------ dev

        /// <summary>Wins the current fight on the spot.</summary>
        public void DevWinFight()
        {
            if (!S.inCombat) return;
            Events.Clear();
            if (!InGrid) { S.inCombat = false; GoTo(S.combatWin); return; }
            foreach (var u in G.units.Where(u => u.side == "foe" && !u.gone).ToList()) { u.hp = 0; Gone(u, "killed", "dead"); }
            S.result = "Dev mode: fight won.";
            Finish("won");
        }

        static string FoeName(EnemyDef e) => e == null ? "the enemy"
            : e.name.StartsWith("The ") ? "the " + e.name.Substring(4) : "the " + e.name.ToLowerInvariant();
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
