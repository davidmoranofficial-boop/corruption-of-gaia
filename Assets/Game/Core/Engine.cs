using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    public class ChoiceView
    {
        public ChoiceDef def;
        public bool enabled;
        public string reason;   // why it's disabled
        public string cost;     // e.g. "1 h 30 min · -30 stamina"
    }

    /// <summary>
    /// Runs the story. Holds no Unity code, so the same rules run in the editor and in the tests.
    /// </summary>
    public partial class Engine
    {
        public const string Title = "title", Creation = "creation", Death = "death", Collapse = "collapse";

        readonly Dictionary<string, SceneDef> scenes = new Dictionary<string, SceneDef>();
        readonly IJson json;
        public readonly GameData data;
        public GameState S { get; private set; }

        bool enteringFromSafe = true;
        bool freshView;
        string choosingGo;   // the "go" of the choice being taken, while its effects run   // set after a revive so the scene is described again

        public Engine(IEnumerable<StoryFile> files, DataFile dataFile, IJson json, GameState state = null)
        {
            this.json = json;
            data = new GameData(dataFile);
            foreach (var f in files)
                foreach (var sc in f.scenes)
                {
                    if (scenes.ContainsKey(sc.id)) throw new Exception("Two scenes share the id '" + sc.id + "'");
                    Normalize(sc);
                    scenes[sc.id] = sc;
                }
            S = state ?? new GameState();
            Normalize(dataFile);
        }

        static void Normalize(DataFile d)
        {
            d.items ??= new List<ItemDef>();
            d.enemies ??= new List<EnemyDef>();
            d.skills ??= new List<SkillDef>();
            d.moves ??= new List<MoveDef>();
            foreach (var m in d.moves) m.need ??= "";
            d.followers ??= new List<FollowerDef>();
            d.afflictions ??= new List<AfflictionDef>();
            foreach (var x in d.afflictions) { x.treat ??= new List<string>(); x.worsen ??= ""; x.worsenUnless ??= ""; x.cure ??= ""; }
            d.treatments ??= new List<TreatmentDef>();
            foreach (var x in d.treatments) { x.items ??= new List<string>(); x.tool ??= ""; }
            d.climates ??= new List<ClimateDef>();
            foreach (var x in d.climates) x.weather ??= new List<WeatherChance>();
            d.weathers ??= new List<WeatherDef>();
            foreach (var x in d.weathers) x.overlay ??= "";
            foreach (var en in d.enemies) foreach (var mv in en.moves) mv.wounds ??= new List<WoundChance>();
            foreach (var ar in d.areas ?? new List<AreaDef>()) if (string.IsNullOrEmpty(ar.climate)) ar.climate = "coast";
            foreach (var f in d.followers) { f.untilWhen ??= new List<string>(); f.until ??= ""; f.quest ??= ""; }
            d.backgrounds ??= new List<BackgroundDef>();
            d.gear ??= new List<GearDef>();
            d.traits ??= new List<TraitDef>();
            d.materials ??= new List<MaterialDef>();
            d.spells ??= new List<SpellDef>();
            d.journal ??= new List<JournalDef>();
            d.builds ??= new List<string>();
            d.hair ??= new List<string>();
            foreach (var e in d.enemies)
            {
                e.moves ??= new List<EnemyMove>();
                if (e.moves.Count == 0) e.moves.Add(new EnemyMove { id = "attack", name = "Attack", tell = "It squares up to you.", power = 1, weight = 1 });
            }
            foreach (var m in d.materials) m.traits ??= new List<string>();
            d.tiles ??= new List<TileDef>();
            d.areas ??= new List<AreaDef>();
            foreach (var a in d.areas)
            {
                a.spots ??= new List<SpotDef>();
                a.objects ??= new List<WorldObj>();
                a.zones ??= new List<ZoneDef>();
                a.uses ??= new List<UseDef>();
                a.events ??= new List<TravelEvent>();
                a.rows ??= new List<string>();
                foreach (var ev in a.events) { ev.when ??= new List<string>(); ev.area ??= new List<int>(); ev.terrain ??= ""; ev.enemy ??= ""; ev.win ??= ""; }
                foreach (var o in a.objects) { o.when ??= new List<string>(); o.sprite ??= ""; if (string.IsNullOrEmpty(o.facing)) o.facing = "down"; }
                foreach (var z in a.zones) { z.to ??= ""; z.when ??= new List<string>(); z.enter ??= new List<CondScene>(); foreach (var c in z.enter) c.when ??= new List<string>(); }
            }
            foreach (var j in d.journal) j.when ??= new List<string>();
            d.quests ??= new List<QuestDef>();
            foreach (var q in d.quests) q.goals ??= new List<QuestGoal>();
        }

        /// <summary>Missing lists in the JSON become empty lists, whichever JSON library loaded them.</summary>
        static void Normalize(SceneDef sc)
        {
            sc.text ??= new List<string>();
            sc.extra ??= new List<CondText>();
            sc.onEnter ??= new List<string>();
            sc.choices ??= new List<ChoiceDef>();
            foreach (var x in sc.extra) x.when ??= new List<string>();
            foreach (var c in sc.choices)
            {
                c.when ??= new List<string>();
                c.need ??= new List<string>();
                c.train ??= new List<string>();
                c.effects ??= new List<string>();
            }
        }

        public IEnumerable<string> SceneIds => scenes.Keys;
        public SceneDef Scene(string id) => scenes.TryGetValue(id, out var s) ? s : null;
        public SceneDef Current => S.inCombat ? CombatScene() : S.scene == TravelId ? TravelScene() : S.scene == ForgeId ? ForgeScene() : scenes[S.scene];

        // ------------------------------------------------------------------ setup

        public void NewGame(string deathMode)
        {
            S = new GameState
            {
                deathMode = deathMode == "perma" ? "perma" : "basic",
                seed = Environment.TickCount & 0x7fffffff,
                minutes = 22 * 60,
            };
            GoTo(Creation);
        }

        /// <summary>Applies the character creation form. alloc is the bonus points spread over the stats.</summary>
        public string CreateCharacter(string name, string gender, string build, string hair, string backgroundId, int[] alloc,
                                      string skin = null, string figure = null)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "Give your character a name.";
            if (name.Length > 20) return "Keep the name under 20 letters.";
            if (gender != "male" && gender != "female" && gender != "other") return "Choose male, female or other.";
            var bg = data.Background(backgroundId);
            if (bg == null) return "Pick a background.";
            if (alloc == null || alloc.Length != Stat.Count || alloc.Any(a => a < 0) || alloc.Sum() != CreationPoints)
                return "Spend all " + CreationPoints + " stat points.";

            S.name = name;
            S.gender = gender;
            S.build = build;
            S.hair = hair;
            S.skin = string.IsNullOrEmpty(skin) ? (data.file.skins.Count > 1 ? data.file.skins[1] : "") : skin;
            S.figure = figure == "male" || figure == "female" ? figure : gender == "female" ? "female" : "male";
            S.background = bg.id;
            for (int i = 0; i < Stat.Count; i++) S.stats[i] = 3 + alloc[i];
            S.stats[Stat.Parse(bg.stat)] += 1;
            if (!S.Has("created")) S.flags.Add("created");
            GiveClothes();
            return null;
        }

        public const int CreationPoints = 5;

        public void LoadState(GameState state)
        {
            state.Upgrade();
            S = state;
            MigrateGear();
            MigrateScene();
            // Saves from before Garran's quarry job: if you've already made your first gear, the job's done
            if (S.Has("smith_done") && !S.Has("quest_done:quarry")) S.flags.Add("quest_done:quarry");
            if (S.Has("created")) GiveClothes();
        }

        /// <summary>Saves made on the old coast road, or in a scene that no longer exists, are moved onto the map.</summary>
        void MigrateScene()
        {
            if (S.scene == "road1" || S.scene == "road2")
            {
                S.travelRoute = "coast_road";
                S.travelAt = S.scene == "road1" ? 2 : 7;
                S.scene = TravelId;
            }
            MigrateRoad();
            if (S.scene == TravelId ? Area != null : S.scene == ForgeId ? S.forging != "" : scenes.ContainsKey(S.scene)) return;
            // A scene that's been taken out (the old text pages): carry on in the world, or in Yenson's hut
            if (Area == null) MoveTo(S.Has("arrived") || S.Has("been:skelmed") ? "skelmed" : "hut_bed");
            S.scene = TravelId;
        }

        // ------------------------------------------------------------------ reading the scene

        public List<string> Paragraphs()
        {
            var sc = Current;
            var list = new List<string>();
            if (!S.repeat || S.inCombat || S.scene == TravelId)
                foreach (var p in sc.text) list.Add(Fill(p));
            foreach (var x in sc.extra)
                if (All(x.when)) list.Add(Fill(x.text));
            return list;
        }

        public string Fill(string t)
        {
            if (string.IsNullOrEmpty(t)) return "";
            bool m = S.gender == "male", f = S.gender == "female";
            return t.Replace("{they}", m ? "he" : f ? "she" : "they")
                    .Replace("{them}", m ? "him" : f ? "her" : "them")
                    .Replace("{their}", m ? "his" : f ? "her" : "their")
                    .Replace("{name}", S.name).Replace("{coins}", S.coins.ToString()).Replace("{death}", S.deathCause);
        }

        public List<ChoiceView> Choices()
        {
            var list = new List<ChoiceView>();
            foreach (var c in Current.choices)
            {
                if (!All(c.when)) continue;
                list.Add(new ChoiceView { def = c, enabled = Allowed(c, out var why), reason = why, cost = Cost(c) });
            }
            return list;
        }

        bool Allowed(ChoiceDef c, out string reason)
        {
            reason = null;
            if (!All(c.need)) { reason = string.IsNullOrEmpty(c.needText) ? "Not yet." : c.needText; return false; }
            if (StaminaCost(c) > 0 && S.stamina < StaminaCost(c)) { reason = "Too winded. Catch your breath first."; return false; }
            return true;
        }

        public string Cost(ChoiceDef c)
        {
            var parts = new List<string>();
            if (c.time > 0) parts.Add(Duration(c.time));
            int st = StaminaCost(c);
            if (st > 0) parts.Add("-" + st + " stamina");
            if (c.energy > 0) parts.Add("-" + c.energy + " energy");
            return string.Join(" · ", parts);
        }

        public static string Duration(int minutes)
        {
            int h = minutes / 60, m = minutes % 60;
            if (h == 0) return m + " min";
            return m == 0 ? h + " h" : h + " h " + m + " min";
        }

        /// <summary>A quest's goals as they stand: what to do, whether it's done, and progress like "2/3".</summary>
        public List<(string text, bool met, string progress)> QuestGoals(QuestDef q)
        {
            var list = new List<(string, bool, string)>();
            foreach (var g in q.goals)
            {
                if (!string.IsNullOrEmpty(g.count))
                {
                    int have = (int)Value(g.count);
                    list.Add((g.text, have >= g.target, Math.Min(have, g.target) + "/" + g.target));
                }
                else list.Add((g.text, Check(g.when), ""));
            }
            return list;
        }

        public IEnumerable<QuestDef> ActiveQuests => data.file.quests.Where(q => S.Has("quest:" + q.id));
        public IEnumerable<QuestDef> FinishedQuests => data.file.quests.Where(q => S.Has("quest_done:" + q.id));

        public List<string> Journal()
        {
            var list = new List<string>();
            foreach (var j in data.file.journal)
                if (All(j.when)) list.Add(Fill(j.text));
            return list;
        }

        // ------------------------------------------------------------------ acting

        public void Choose(ChoiceDef c)
        {
            if (!Allowed(c, out _)) return;

            int staminaCost = StaminaCost(c);
            S.stamina -= staminaCost;
            S.energy -= c.energy;
            Rules.Advance(S, data, c.time, staminaCost > 0);

            foreach (var t in c.train)
            {
                var (key, op, num) = Split(t);
                if (data.Skill(key) != null) TrainSkill(key, num, c.why);
                else Rules.Train(S, Stat.Parse(key), num, c.why);
            }

            var notes = new StringBuilder();
            string redirect = null;
            choosingGo = c.go;
            foreach (var e in c.effects) Apply(e, notes, ref redirect);
            choosingGo = null;
            Rules.Clamp(S, data);

            string result = Join(Fill(c.result), notes.ToString());
            S.result = result;

            GoTo(redirect ?? (string.IsNullOrEmpty(c.go) ? S.scene : c.go));
        }

        public void GoTo(string id)
        {
            string prev = S.scene;
            if (S.hud)
            {
                if (S.hp <= 0)
                {
                    if (string.IsNullOrEmpty(S.deathCause)) S.deathCause = "Your body gave out.";
                    S.inCombat = false;
                    id = Death;
                }
                else if (S.energy <= 0)
                {
                    if (S.inCombat)
                    {
                        S.deathCause = "Your energy gave out in the middle of the fight, and you couldn't lift your arms to defend yourself.";
                        S.inCombat = false;
                        id = Death;
                    }
                    else
                    {
                        S.afterCollapse = id;
                        id = Collapse;
                    }
                }
            }

            if (id == TravelId && Area == null) throw new Exception("Not out in the world");
            if (id == ForgeId && S.forging == "") throw new Exception("Nothing on the anvil");
            if (!scenes.ContainsKey(id) && id != TravelId && id != ForgeId) throw new Exception("No scene called '" + id + "'");
            S.repeat = id == prev && !freshView;
            freshView = false;
            enteringFromSafe = scenes.TryGetValue(prev, out var p) && p.safe;
            S.scene = id;
            if (id != prev && scenes.ContainsKey(id))
            {
                var notes = new StringBuilder();
                string redirect = null;
                foreach (var e in scenes[id].onEnter) Apply(e, notes, ref redirect);
                Rules.Clamp(S, data);
                if (notes.Length > 0) S.result = Join(S.result, notes.ToString());
                if (redirect != null && redirect != id) GoTo(redirect);
            }
        }

        // Side actions, available whenever the player is free to act.

        public bool FreeToAct => S.hud && !S.inCombat && S.scene != Death && S.scene != Collapse;

        public bool Eat(string itemId)
        {
            var it = data.Item(itemId);
            if (!FreeToAct || it == null || !it.food || S.Count(itemId) <= 0) return false;
            S.AddItem(itemId, -1);
            Rules.Advance(S, data, 10, false);
            S.hunger += it.hunger;
            S.thirst += it.thirst;
            S.energy += it.energy;
            Rules.Clamp(S, data);
            S.result = "You eat the " + it.name.ToLowerInvariant() + ".";
            GoTo(S.scene);
            return true;
        }

        public bool Drink()
        {
            if (!FreeToAct || S.water <= 0) return false;
            S.water--;
            Rules.Advance(S, data, 2, false);
            S.thirst += 35;
            S.energy += 4;
            Rules.Clamp(S, data);
            S.result = "You drink from the waterskin.";
            GoTo(S.scene);
            return true;
        }

        public bool CatchBreath()
        {
            if (!FreeToAct) return false;
            Rules.Advance(S, data, 15, false);
            S.stamina += 30;
            Rules.Clamp(S, data);
            S.result = "You stop and catch your breath.";
            GoTo(S.scene);
            return true;
        }

        public bool DrawWater()
        {
            if (!FreeToAct || !S.spells.Contains("draw_water") || S.energy < 6) return false;
            Rules.Advance(S, data, 5, true);
            S.energy -= 5;
            S.stamina -= 5;
            S.water = Rules.WaterskinSize;
            Rules.Train(S, Stat.Int, 0.5f, "casting Draw Water");
            TrainSkill("magic", 1, "casting Draw Water");
            Rules.Clamp(S, data);
            S.result = "You call water out of the air. It trickles into the skin until it's full.";
            GoTo(S.scene);
            return true;
        }

        int Roll(int min, int max)
        {
            // A hash of (seed, call number), so every roll is independent and replays the same after a load.
            ulong z = (ulong)(uint)S.seed * 0x9E3779B97F4A7C15UL + (ulong)(uint)S.rngCalls * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            S.rngCalls++;
            return min + (int)(z % (ulong)(max - min + 1));
        }

        // ------------------------------------------------------------------ conditions

        public bool All(List<string> conds)
        {
            if (conds == null) return true;
            foreach (var c in conds) if (!Check(c)) return false;
            return true;
        }

        public bool Check(string cond)
        {
            // a|b holds when either does
            if (cond.IndexOf('|') >= 0) return cond.Split('|').Any(Check);
            bool neg = cond.StartsWith("!");
            if (neg) cond = cond.Substring(1);
            bool v = Eval(cond);
            return neg ? !v : v;
        }

        bool Eval(string cond)
        {
            // Comparisons: coins>=3, water<1, rep:skelmed>=10, energy<20 ...
            foreach (var op in new[] { ">=", "<=", ">", "<", "==" })
            {
                int i = cond.IndexOf(op, StringComparison.Ordinal);
                if (i < 0) continue;
                float lhs = Value(cond.Substring(0, i));
                float rhs = float.Parse(cond.Substring(i + op.Length), CultureInfo.InvariantCulture);
                switch (op)
                {
                    case ">=": return lhs >= rhs;
                    case "<=": return lhs <= rhs;
                    case ">": return lhs > rhs;
                    case "<": return lhs < rhs;
                    default: return Math.Abs(lhs - rhs) < 0.001f;
                }
            }

            int colon = cond.IndexOf(':');
            string key = colon < 0 ? cond : cond.Substring(0, colon);
            string arg = colon < 0 ? "" : cond.Substring(colon + 1);
            switch (key)
            {
                case "flag": return S.Has(arg);
                case "item": return S.Count(arg) > 0;
                case "spell": return S.spells.Contains(arg);
                case "follower": return arg == "" ? S.party.Count > 0 : Member(arg) != null;
                case "gear": return arg == "" ? S.gearItems.Count > 0 : S.gearItems.Any(g => g.id == arg);
                case "mode": return S.deathMode == arg;
                case "background": return S.background == arg;
                case "gender": return S.gender == arg;
                case "night": return S.Hour >= 20 || S.Hour < 5;
                case "quest": return S.Has("quest:" + arg);
                case "done": return S.Has("quest_done:" + arg);
                case "ready": return S.Has("quest:" + arg) && QuestGoals(data.Quest(arg) ?? throw new Exception("Unknown quest '" + arg + "'")).All(g => g.met);
                default: throw new Exception("Unknown condition '" + cond + "'");
            }
        }

        float Value(string key)
        {
            if (key.StartsWith("rep:")) return S.Rep(key.Substring(4));
            if (key.StartsWith("item:")) return S.Count(key.Substring(5));
            if (key.StartsWith("stat:")) return S.stats[Stat.Parse(key.Substring(5))];
            if (key.StartsWith("morale:")) return Member(key.Substring(7))?.morale ?? 0;
            if (key == "party") return S.party.Count;
            if (key.StartsWith("skill:")) return data.Skill(key.Substring(6)) != null ? SkillLevel(key.Substring(6)) : throw new Exception("Unknown skill '" + key.Substring(6) + "'");
            switch (key)
            {
                case "coins": return S.coins;
                case "water": return S.water;
                case "hp": return S.hp;
                case "hunger": return S.hunger;
                case "thirst": return S.thirst;
                case "stamina": return S.stamina;
                case "energy": return S.energy;
                case "corruption": return S.corruption;
                case "day": return S.Day;
                case "level": return S.level;
                case "hour": return S.Hour;
                default: throw new Exception("Unknown value '" + key + "'");
            }
        }

        // ------------------------------------------------------------------ effects

        void Apply(string effect, StringBuilder notes, ref string redirect)
        {
            int colon = effect.IndexOf(':');
            string head = colon < 0 ? effect : effect.Substring(0, colon);
            string arg = colon < 0 ? "" : effect.Substring(colon + 1);
            var parts = arg.Split(':');

            switch (head)
            {
                case "flag": if (!S.Has(arg)) S.flags.Add(arg); return;
                case "unflag": S.flags.Remove(arg); return;
                case "spell":
                    if (!S.spells.Contains(arg))
                    {
                        S.spells.Add(arg);
                        S.Toasts.Add("New spell: " + (data.Spell(arg)?.name ?? arg) + ".");
                    }
                    return;
                case "gear":
                {
                    var item = MakeGear(arg, null, null);
                    S.Toasts.Add("New gear: " + item.name + " (" + item.rank + " rank).");
                    return;
                }
                case "craft":
                {
                    // craft:base:rank[:material]  makes a new piece of gear with traits drawn from the material
                    var item = MakeGear(parts[0], parts[1], parts.Length > 2 ? parts[2] : null);
                    S.Toasts.Add("New gear: " + item.name + " (" + item.rank + " rank" +
                                 (item.traits.Count > 0 ? ", " + string.Join(", ", item.traits.Select(t => data.Trait(t)?.name ?? t)) : "") + ").");
                    return;
                }
                case "follow": Join(arg); return;
                case "unfollow":
                    // unfollow:<id> sends one person home; unfollow alone, everyone
                    foreach (var m in S.party.Where(m => arg == "" || m.id == arg).ToList()) Leave(m.id);
                    return;
                case "morale":
                {
                    // morale:rook+10
                    int sign = arg.IndexOfAny(new[] { '+', '-' });
                    var m = Member(arg.Substring(0, sign));
                    if (m == null) return;
                    m.morale += float.Parse(arg.Substring(sign), System.Globalization.CultureInfo.InvariantCulture);
                    Rules.ClampMember(m, data.Follower(m.id));
                    return;
                }
                case "give":
                {
                    // give:gear  puts a piece of gear in your pack without putting it on
                    var item = MakeGear(arg, null, null, equip: false);
                    S.Toasts.Add("New gear: " + item.name + " (" + item.rank + " rank). It's in your pack.");
                    return;
                }
                case "exp": GainExp(int.Parse(arg), ""); return;
                case "quest":
                {
                    var q = data.Quest(arg) ?? throw new Exception("Unknown quest '" + arg + "'");
                    if (S.Has("quest:" + q.id) || S.Has("quest_done:" + q.id)) return;
                    S.flags.Add("quest:" + q.id);
                    S.Toasts.Add("New quest: " + q.name);
                    return;
                }
                case "complete":
                {
                    var q = data.Quest(arg) ?? throw new Exception("Unknown quest '" + arg + "'");
                    S.flags.Remove("quest:" + q.id);
                    bool first = !S.Has("quest_done:" + q.id);
                    if (first) S.flags.Add("quest_done:" + q.id);
                    S.Toasts.Add("Quest complete: " + q.name);
                    if (first && q.exp > 0) GainExp(q.exp, "");
                    // Doing a companion's own quest lifts their spirits
                    foreach (var m in S.party.Where(m => data.Follower(m.id)?.quest == q.id))
                    {
                        m.morale += 30;
                        Rules.ClampMember(m, data.Follower(m.id));
                        S.Toasts.Add(data.Follower(m.id).name + " won't forget this.");
                    }
                    return;
                }
                case "hud": S.hud = arg == "on"; return;
                case "save": Snapshot(); return;
                case "clock":
                    // clock:D:HH:MM sets the absolute day and time
                    S.minutes = (int.Parse(parts[0]) - 1) * 1440 + int.Parse(parts[1]) * 60 + int.Parse(parts[2]);
                    return;
                case "sleep": Sleep(arg, notes); return;
                case "combat":
                {
                    // combat:enemy:winScene:fleeScene
                    StartCombat(parts[0], parts[1], parts[2]);
                    return;
                }
                case "check":
                {
                    // check:stat:baseChance:passScene:failScene  (chance = base + stat x 8 %)
                    int stat = Stat.Parse(parts[0]);
                    int chance = int.Parse(parts[1]) + S.stats[stat] * 8;
                    bool pass = Roll(1, 100) <= chance;
                    redirect = pass ? parts[2] : parts[3];
                    return;
                }
                case "fight": Fight(arg, notes, ref redirect); return;
                case "travel": Travel(parts, notes, ref redirect); return;
                case "forge": Forge(parts, notes, ref redirect); return;
                case "collapse": DoCollapse(notes); return;
                case "revive":
                {
                    if (string.IsNullOrEmpty(S.lastSleep)) { redirect = "title"; return; }
                    string snap = S.lastSleep;
                    var restored = json.Read<GameState>(snap);
                    restored.lastSleep = snap;
                    restored.Upgrade();
                    S = restored;
                    MigrateGear();
                    freshView = true;
                    notes.Append("You wake with a gasp. It's the morning you last slept, and whatever happened after hasn't happened yet.");
                    redirect = S.scene;
                    return;
                }
                case "resume":
                    redirect = string.IsNullOrEmpty(S.afterCollapse) ? S.scene : S.afterCollapse;
                    return;
                case "restart":
                    S = new GameState();
                    redirect = Title;
                    return;
            }

            // Numeric effects: coins+5, item:bread+2, rep:skelmed-5, corruption+0.3, hp=70, water=4
            var (key, op, num) = Split(effect);
            switch (key)
            {
                case "coins": S.coins = (int)Num(S.coins, op, num); if (S.coins < 0) S.coins = 0; return;
                case "water": S.water = (int)Num(S.water, op, num); return;
                case "hp": S.hp = Num(S.hp, op, num); return;
                case "hunger": S.hunger = Num(S.hunger, op, num); return;
                case "thirst": S.thirst = Num(S.thirst, op, num); return;
                case "energy": S.energy = Num(S.energy, op, num); return;
                case "stamina": S.stamina = Num(S.stamina, op, num); return;
                case "corruption":
                {
                    float before = S.corruption;
                    S.corruption = Num(S.corruption, op, num);
                    if (op != "=" && S.hud)
                        S.Toasts.Add(S.corruption > before ? "The corruption grew a little." : "The corruption eased a little.");
                    return;
                }
            }
            if (key.StartsWith("item:"))
            {
                string id = key.Substring(5);
                if (data.Item(id) == null) throw new Exception("Unknown item '" + id + "'");
                int n = (int)num;
                if (op == "-") n = -n;
                S.AddItem(id, n);
                S.Toasts.Add((n > 0 ? "+" : "") + n + " " + data.Item(id).name);
                return;
            }
            if (key.StartsWith("rep:"))
            {
                int n = (int)(op == "-" ? -num : num);
                S.AddRep(key.Substring(4), n);
                if (n != 0) S.Toasts.Add((n > 0 ? "Skelmed thinks better of you." : "Skelmed thinks less of you."));
                return;
            }
            throw new Exception("Unknown effect '" + effect + "'");
        }

        static string Article(string w) => "AEIOU".IndexOf(char.ToUpperInvariant(w[0])) >= 0 ? "an" : "a";

        void Sleep(string kind, StringBuilder notes)
        {
            int mins = Rules.MinutesUntilMorning(S);
            S.flags.RemoveAll(f => f.EndsWith("_today"));   // things that come back each day (herbs) are back in the morning
            var partyNotes = new StringBuilder();
            PartyAtNight(kind, kind != "camp" || S.Count("flint") > 0, partyNotes);
            switch (kind)
            {
                case "safe":
                    Rules.Sleep(S, data, mins, 100, "bed");
                    notes.Append("You sleep through until morning and wake rested.");
                    break;
                case "rough":
                    Rules.Sleep(S, data, mins, 70, "none");
                    notes.Append("You sleep badly, but you sleep. You wake stiff and cold.");
                    break;
                case "camp":
                    if (S.Count("flint") > 0)
                    {
                        Rules.Sleep(S, data, mins, 85, "fire");
                        notes.Append("You scrape together driftwood and get a fire going with Yenson's flint. You sleep warm, with the fire between you and the dark.");
                    }
                    else
                    {
                        Rules.Sleep(S, data, mins, 65, "none");
                        notes.Append("No fire. You curl up out of the wind and sleep in fits.");
                        if (Roll(1, 100) <= 35)
                        {
                            string food = S.items.Where(i => data.Item(i.id)?.food == true).Select(i => i.id).FirstOrDefault();
                            if (food != null)
                            {
                                S.AddItem(food, -1);
                                notes.Append(" Something got into your pack in the night. A " + data.Item(food).name.ToLowerInvariant() + " is gone.");
                            }
                        }
                    }
                    break;
                default: throw new Exception("Unknown sleep '" + kind + "'");
            }
            notes.Append(partyNotes);
            // Out in the world (in a bed, or camping), you wake standing where you slept, not in the bed's talk box
            string scene = S.scene;
            if (Area != null) S.scene = TravelId;
            Snapshot();
            S.scene = scene;
            S.Toasts.Add("Saved. If you die, you'll wake from this sleep.");
        }

        void Snapshot()
        {
            string result = S.result;
            S.lastSleep = "";
            S.result = "";
            S.lastSleep = json.Write(S);
            S.result = result;
        }

        void DoCollapse(StringBuilder notes)
        {
            Rules.Advance(S, data, Rules.CollapseMinutes, false);
            S.energy = 30;
            S.stamina = Rules.MaxStamina(S, data);
            notes.Append("Your legs give out and the world goes dark. You come to hours later, aching.");
            if (!enteringFromSafe)
            {
                int lost = S.coins / 2;
                S.coins -= lost;
                string food = S.items.Where(i => data.Item(i.id)?.food == true).Select(i => i.id).FirstOrDefault();
                if (food != null) S.AddItem(food, -1);
                if (lost > 0 || food != null)
                    notes.Append(" Someone found you while you were out. Your purse is lighter" + (food != null ? " and some of your food is gone." : "."));
            }
        }

        // ------------------------------------------------------------------ helpers

        static (string key, string op, float num) Split(string s)
        {
            for (int i = 1; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch == '+' || ch == '-' || ch == '=')
                {
                    string key = s.Substring(0, i);
                    string rest = s.Substring(i + 1);
                    if (float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out float n))
                        return (key, ch.ToString(), n);
                }
            }
            throw new Exception("Can't read '" + s + "'");
        }

        static float Num(float cur, string op, float n) => op == "+" ? cur + n : op == "-" ? cur - n : n;

        static string Join(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a;
            return a + " " + b;
        }

        // ------------------------------------------------------------------ checking content

        /// <summary>Finds broken links and typos in the story files. Returns an empty list when everything is fine.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var probe = new GameState { hud = true };
            var saved = S;
            S = probe;
            void Scene(string where, string id)
            {
                if (!string.IsNullOrEmpty(id) && id != TravelId && id != ForgeId && !scenes.ContainsKey(id)) problems.Add(where + ": no scene called '" + id + "'");
            }
            void Cond(string where, string c)
            {
                try { Check(c); } catch (Exception ex) { problems.Add(where + ": " + ex.Message); }
            }
            void Effect(string where, string e)
            {
                int colon = e.IndexOf(':');
                string head = colon < 0 ? e : e.Substring(0, colon);
                var parts = colon < 0 ? new string[0] : e.Substring(colon + 1).Split(':');
                string arg = colon < 0 ? "" : e.Substring(colon + 1);
                switch (head)
                {
                    case "combat":
                        if (data.Enemy(parts[0]) == null) problems.Add(where + ": no enemy '" + parts[0] + "'");
                        Scene(where, parts[1]); Scene(where, parts[2]); return;
                    case "check": Scene(where, parts[2]); Scene(where, parts[3]); return;
                    case "follow": if (data.Follower(parts[0]) == null) problems.Add(where + ": no follower '" + parts[0] + "'"); return;
                    case "give": if (data.Gear(parts[0]) == null) problems.Add(where + ": no gear '" + parts[0] + "'"); return;
                    case "unfollow": if (arg != "" && data.Follower(arg) == null) problems.Add(where + ": no follower '" + arg + "'"); return;
                    case "morale": { int sg = arg.IndexOfAny(new[] { '+', '-' }); if (sg < 0 || data.Follower(arg.Substring(0, sg)) == null) problems.Add(where + ": bad morale '" + arg + "'"); return; }
                    case "exp": if (!int.TryParse(parts[0], out _)) problems.Add(where + ": exp needs a number"); return;
                    case "gear": if (data.Gear(parts[0]) == null) problems.Add(where + ": no gear '" + parts[0] + "'"); return;
                    case "craft":
                        if (parts.Length < 2 || data.Gear(parts[0]) == null) problems.Add(where + ": no gear '" + parts[0] + "'");
                        else if (Rules.Ranks.IndexOf(parts[1]) < 0 || parts[1].Length != 1) problems.Add(where + ": no rank '" + parts[1] + "'");
                        if (parts.Length > 2 && data.Material(parts[2]) == null) problems.Add(where + ": no material '" + parts[2] + "'");
                        return;
                    case "spell": if (data.Spell(parts[0]) == null) problems.Add(where + ": no spell '" + parts[0] + "'"); return;
                    case "quest": case "complete":
                        if (data.Quest(parts[0]) == null) problems.Add(where + ": no quest '" + parts[0] + "'");
                        return;
                    case "flag": case "unflag": case "hud": case "save": case "clock": case "sleep":
                    case "fight": case "collapse": case "revive": case "restart": case "resume": return;
                    case "forge":
                        if (parts[0] == "auto") return;
                        if (parts.Length < 2 || data.Gear(parts[0]) == null) problems.Add(where + ": no gear '" + parts[0] + "'");
                        else if (Rules.Ranks.IndexOf(parts[1]) < 0 || parts[1].Length != 1) problems.Add(where + ": no rank '" + parts[1] + "'");
                        if (parts.Length > 2 && data.Material(parts[2]) == null) problems.Add(where + ": no material '" + parts[2] + "'");
                        return;
                    case "travel":
                        if ((parts[0] == "at" || parts[0] == "place") && data.Spot(parts[1]).spot == null) problems.Add(where + ": no spot '" + parts[1] + "'");
                        return;
                }
                if (e.StartsWith("item:"))
                {
                    try
                    {
                        var (key, _, _) = Split(e);
                        if (data.Item(key.Substring(5)) == null) problems.Add(where + ": no item '" + key.Substring(5) + "'");
                    }
                    catch (Exception ex) { problems.Add(where + ": " + ex.Message); }
                    return;
                }
                try { Split(e); } catch (Exception ex) { problems.Add(where + ": " + ex.Message); }
            }

            foreach (var sc in scenes.Values)
            {
                foreach (var x in sc.extra) foreach (var c in x.when) Cond(sc.id + " (extra text)", c);
                foreach (var e in sc.onEnter) Effect(sc.id + " (on enter)", e);
                for (int i = 0; i < sc.choices.Count; i++)
                {
                    var ch = sc.choices[i];
                    string where = sc.id + " > \"" + ch.label + "\"";
                    Scene(where, ch.go);
                    foreach (var c in ch.when) Cond(where, c);
                    foreach (var c in ch.need) Cond(where, c);
                    foreach (var e in ch.effects) Effect(where, e);
                    foreach (var t in ch.train)
                    {
                        try { var k = Split(t).key; if (data.Skill(k) == null) Stat.Parse(k); } catch (Exception ex) { problems.Add(where + ": " + ex.Message); }
                    }
                }
            }
            foreach (var j in data.file.journal) foreach (var c in j.when) Cond("journal \"" + j.text + "\"", c);
            foreach (var a in data.file.afflictions)
            {
                foreach (var t in a.treat) if (data.Treatment(t) == null) problems.Add("affliction " + a.id + ": no treatment '" + t + "'");
                if (a.worsen != "" && data.Affliction(a.worsen) == null) problems.Add("affliction " + a.id + ": no affliction '" + a.worsen + "'");
                if (a.worsenUnless != "" && !a.treat.Contains(a.worsenUnless)) problems.Add("affliction " + a.id + ": worsenUnless isn't one of its treatments");
            }
            foreach (var t in data.file.treatments)
            {
                foreach (var alt in t.items) foreach (var i in alt.Split('+')) if (i != "water" && data.Item(i) == null) problems.Add("treatment " + t.id + ": no item '" + i + "'");
                if (t.tool != "" && data.Item(t.tool) == null) problems.Add("treatment " + t.id + ": no item '" + t.tool + "'");
            }
            foreach (var en in data.file.enemies) foreach (var mv in en.moves) foreach (var w in mv.wounds)
                if (data.Affliction(w.id) == null) problems.Add("enemy " + en.id + ": no affliction '" + w.id + "'");
            foreach (var c in data.file.climates) foreach (var w in c.weather)
                if (data.Weather(w.id) == null) problems.Add("climate " + c.id + ": no weather '" + w.id + "'");
            foreach (var ar in data.file.areas) if (data.Climate(ar.climate) == null) problems.Add("area " + ar.id + ": no climate '" + ar.climate + "'");
            foreach (var id in new[] { "grave", "hypothermia", "heatstroke", "chill" }) if (data.Affliction(id) == null) problems.Add("no affliction '" + id + "' (the engine uses it)");
            foreach (var f in data.file.followers)
            {
                foreach (var c in f.untilWhen) Cond("follower " + f.id, c);
                if (f.quest != "" && data.Quest(f.quest) == null) problems.Add("follower " + f.id + ": no quest '" + f.quest + "'");
            }
            foreach (var q in data.file.quests)
                foreach (var g in q.goals)
                {
                    if (!string.IsNullOrEmpty(g.count)) { try { Value(g.count); } catch (Exception ex) { problems.Add("quest " + q.id + ": " + ex.Message); } }
                    else if (!string.IsNullOrEmpty(g.when)) Cond("quest " + q.id, g.when);
                    else problems.Add("quest " + q.id + ": a goal with nothing to check");
                }
            foreach (var m in data.file.materials)
                foreach (var t in m.traits)
                    if (data.Trait(t) == null) problems.Add("material " + m.id + ": no trait '" + t + "'");
            foreach (var g in data.file.gear)
                if (Array.IndexOf(GearSlots, g.slot) < 0) problems.Add("gear " + g.id + ": no slot '" + g.slot + "'");
            var spotIds = new HashSet<string>();
            foreach (var a in data.file.areas)
            {
                string aw = "area " + a.id;
                if (a.rows.Count == 0) { problems.Add(aw + ": has no rows"); continue; }
                for (int y = 0; y < a.rows.Count; y++)
                {
                    if (a.rows[y].Length != a.rows[0].Length) problems.Add(aw + ": row " + y + " is a different length");
                    foreach (char c in a.rows[y]) if (data.Tile(c) == null) { problems.Add(aw + ": no tile '" + c + "' (row " + y + ")"); break; }
                }
                bool Open(int x, int y) => InBounds(a, x, y) && data.Tile(a.rows[y][x])?.solid == false;
                foreach (var p in a.spots)
                {
                    if (!spotIds.Add(p.id)) problems.Add(aw + ": two spots called '" + p.id + "'");
                    if (!Open(p.x, p.y)) problems.Add(aw + " spot " + p.id + ": stands on ground you can't walk on");
                }
                foreach (var o in a.objects)
                {
                    string where = aw + " > " + o.id;
                    Scene(where, o.scene);
                    foreach (var w in o.when) Cond(where, w);
                    if (!InBounds(a, o.x, o.y)) problems.Add(where + ": off the edge");
                }
                foreach (var z in a.zones)
                {
                    Scene(aw + " zone", z.scene);
                    if (!string.IsNullOrEmpty(z.to) && data.Spot(z.to).spot == null) problems.Add(aw + " zone: no spot '" + z.to + "'");
                    foreach (var w in z.when) Cond(aw + " zone", w);
                    foreach (var c in z.enter) { Scene(aw + " zone", c.scene); foreach (var w in c.when) Cond(aw + " zone", w); }
                }
                foreach (var u in a.uses) Scene(aw + " use " + u.tile, u.scene);
                foreach (var ev in a.events)
                {
                    string where = aw + " event";
                    if (ev.kind == "fight")
                    {
                        if (data.Enemy(ev.enemy) == null) problems.Add(where + ": no enemy '" + ev.enemy + "'");
                        Scene(where, ev.win);
                    }
                    else Scene(where, ev.scene);
                    foreach (var w in ev.when) Cond(where, w);
                }
            }
            foreach (var need in new[] { Title, Creation, Death, Collapse }) Scene("required", need);
            S = saved;
            return problems;
        }
    }
}
