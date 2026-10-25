using System;
using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The battle screen (David, 25 Oct): the fight on a grid, drawn in the world's own pixel art, and a side panel.
    /// Your side moves first: pick someone, walk them (the blue squares), then act: attack (pick where to aim and
    /// see the chance and damage first), guard, brace, help someone up, use something. Then end the turn and the
    /// enemies do what they showed they'd do: the boar's charge lane is drawn in red, each enemy says who it's after.
    /// When one of them goes for you, you dodge by yourself (or parry or block, if you took Guard), as well as your
    /// Agility and weapon skill let you. No timing bar (David, 25 Oct).
    ///
    /// Keys: WASD or arrows move the cursor, E or Enter acts on it (select, walk, aim), 1-9 pick an action, Tab picks
    /// the next of your side, R turns them, T ends the turn, Esc goes back. While aiming: A/D change target, W/S
    /// change where to aim, E strikes. The mouse does all of it too.
    /// </summary>
    public partial class GameUI
    {
        const int MapTilesW = 12, MapTilesH = 9;

        bool busy;                             // events playing; input waits
        bool outro;                            // the fight just ended; show its last blows before moving on
        bool InBattle => engine.S.inCombat || outro;

        // what you've picked
        string selUnit = "you";
        string selAction = "";                 // an attack being aimed ("" = not aiming)
        string selTarget = "";
        int selPart;
        int curX = 1, curY = 3;                // the keyboard cursor, in fight squares
        bool cursorOn;

        // the field
        VisualElement arena, field, fieldMarks, fieldUnits, fieldTags, fieldFx, cursorEl;
        int fk = 5;
        float battleHeight;
        IVisualElementScheduledItem battleTick;
        string lastAct = "";

        /// <summary>One person or beast on the field, as drawn.</summary>
        class UnitView
        {
            public VisualElement box, sheet, shadow, hpFill, hpBox;
            public bool beast;
            public float frameW, frameH;
            public float px, py;               // where it's drawn now (the square's top left, in screen pixels)
            public string facing = "right", lastH = "right";
            public string mode = "";           // attack, hurt, tell (a beast's frames)
            public float modeAt;
            public float hp, max;
            public bool down;
        }
        readonly Dictionary<string, UnitView> views = new Dictionary<string, UnitView>();

        /// <summary>Where everyone was before the last action, so it can be played out step by step.</summary>
        class Snap { public int x, y; public string facing; public bool gone, down; public float hp; }
        Dictionary<string, Snap> shown;

        // ------------------------------------------------------------------ drawing

        /// <summary>The height the battle screen fills: all of the main area.</summary>
        float BattleHeight()
        {
            float h = mainBox != null ? mainBox.layout.height : float.NaN;
            return float.IsNaN(h) || h < 400 ? 960f : h - 56f;
        }

        void RenderBattle()
        {
            var s = engine.S;
            var g = engine.Grid;
            main.Clear();
            views.Clear();
            battleHeight = BattleHeight();
            var battle = Add(main, new VisualElement(), "battle");
            battle.style.height = battleHeight;

            var head = Add(battle, new VisualElement(), "battle-head");
            string place = engine.Scene(s.scene)?.place ?? engine.Area?.name ?? "";
            Add(head, new Label(("Fight  ·  " + place).ToUpperInvariant()), "place");
            string turn = g.phase == "party" ? "YOUR TURN" : g.phase == "foe" ? "THEIR TURN" : "";
            Add(head, new Label("ROUND " + g.round + (turn != "" ? "  ·  " + turn : "")), "battle-round");

            var row = Add(battle, new VisualElement(), "grid-row");
            arena = Add(row, new VisualElement(), "grid-arena");
            var side = Add(row, new VisualElement(), "grid-side");

            float aw = Mathf.Max(400, TravelWidth() - 560), ah = Mathf.Max(300, battleHeight - 60);
            fk = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(aw / (MapTilesW * Tile), ah / (MapTilesH * Tile))), 2, 8);
            BuildField();
            BuildSide(side);

            battleTick?.Pause();
            battleTick = root.schedule.Execute(BattleTick).Every(16);
        }

        int T16 => Tile * fk;

        /// <summary>Top left of a fight square on the field (the scenery ring is one square all round).</summary>
        Vector2 SquarePos(float x, float y) => new Vector2((x + 1) * T16, (y + 1) * T16);

        void BuildField()
        {
            var g = engine.Grid;
            field = Add(arena, new VisualElement(), "grid-field");
            field.style.width = MapTilesW * T16;
            field.style.height = MapTilesH * T16;
            var ground = Add(field, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-layer");
            var tex = Sprite("Battle/" + g.map);
            if (tex != null) ground.style.backgroundImage = new StyleBackground(tex);
            fieldMarks = Add(field, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-layer");
            fieldUnits = Add(field, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-layer");
            var over = Add(field, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-layer");
            var otex = Sprite("Battle/" + g.map + "_over");
            if (otex != null) over.style.backgroundImage = new StyleBackground(otex);
            if (engine.Check("night")) Add(field, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-layer").AddToClassList("grid-night");
            fieldTags = Add(field, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-layer");
            fieldFx = Add(field, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-layer");

            DrawMarks();
            DrawUnits();

            field.RegisterCallback<PointerDownEvent>(e =>
            {
                var p = field.WorldToLocal(e.position);
                int x = Mathf.FloorToInt(p.x / T16) - 1, y = Mathf.FloorToInt(p.y / T16) - 1;
                if (!engine.OnGrid(x, y)) return;
                curX = x; curY = y;
                OnSquare(x, y);
            });
            field.RegisterCallback<PointerMoveEvent>(e =>
            {
                var p = field.WorldToLocal(e.position);
                int x = Mathf.FloorToInt(p.x / T16) - 1, y = Mathf.FloorToInt(p.y / T16) - 1;
                if (!engine.OnGrid(x, y) || (x == curX && y == curY && cursorOn)) return;
                curX = x; curY = y;
                cursorOn = true;
                PlaceCursor();
            });
        }

        bool YourTurn => engine.InGrid && engine.Grid.phase == "party" && !busy;

        void DrawMarks()
        {
            var g = engine.Grid;
            // faint lines on every square, so you can count
            for (int y = 0; y < g.h; y++)
                for (int x = 0; x < g.w; x++)
                    if (!engine.Solid(x, y)) Mark(x, y, "grid-cell");
            // the charge lanes: where not to stand (unless braced)
            if (!busy)
                foreach (var u in g.units.Where(u => u.side == "foe" && !u.gone))
                    foreach (int k in u.lane) Mark(k % g.w, k / g.w, "grid-lane");
            var sel = engine.Unit(selUnit);
            if (YourTurn && sel != null && !sel.gone && !sel.down)
            {
                Mark(sel.x, sel.y, "grid-sel");
                if (selAction == "")
                    foreach (var (x, y) in engine.Reachable(selUnit)) Mark(x, y, "grid-reach");
                else
                {
                    foreach (var id in engine.Targets(selUnit, selAction))
                    {
                        var t = engine.Unit(id);
                        Mark(t.x, t.y, id == selTarget ? "grid-target-sel" : "grid-target");
                    }
                }
            }
            // which way everyone faces: a bar on the front edge of their square
            foreach (var u in g.units.Where(u => !u.gone && !u.down))
            {
                var snap = Shown(u);
                if (snap.gone) continue;
                var bar = Add(fieldMarks, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-face");
                bar.AddToClassList(u.side == "party" ? "grid-face-party" : "grid-face-foe");
                var p = SquarePos(snap.x, snap.y);
                int t = T16, th = Mathf.Max(2, fk);
                switch (snap.facing)
                {
                    case "left": bar.style.left = p.x; bar.style.top = p.y + t * 0.2f; bar.style.width = th; bar.style.height = t * 0.6f; break;
                    case "up": bar.style.left = p.x + t * 0.2f; bar.style.top = p.y; bar.style.width = t * 0.6f; bar.style.height = th; break;
                    case "down": bar.style.left = p.x + t * 0.2f; bar.style.top = p.y + t - th; bar.style.width = t * 0.6f; bar.style.height = th; break;
                    default: bar.style.left = p.x + t - th; bar.style.top = p.y + t * 0.2f; bar.style.width = th; bar.style.height = t * 0.6f; break;
                }
            }
            cursorEl = Add(fieldMarks, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-cursor");
            PlaceCursor();
        }

        VisualElement Mark(int x, int y, string cls)
        {
            var m = Add(fieldMarks, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-mark");
            m.AddToClassList(cls);
            var p = SquarePos(x, y);
            m.style.left = p.x; m.style.top = p.y;
            m.style.width = T16; m.style.height = T16;
            return m;
        }

        void PlaceCursor()
        {
            if (cursorEl == null) return;
            var p = SquarePos(curX, curY);
            cursorEl.style.left = p.x; cursorEl.style.top = p.y;
            cursorEl.style.width = T16; cursorEl.style.height = T16;
            cursorEl.style.display = cursorOn && YourTurn ? DisplayStyle.Flex : DisplayStyle.None;
        }

        Snap Shown(GridUnit u) =>
            shown != null && shown.TryGetValue(u.id, out var s) ? s
            : new Snap { x = u.x, y = u.y, facing = u.facing, gone = u.gone, down = u.down, hp = engine.Hp(u) };

        void DrawUnits()
        {
            var g = engine.Grid;
            // back to front, so the nearer ones stand in front
            foreach (var u in g.units.OrderBy(u => Shown(u).y))
            {
                var snap = Shown(u);
                if (snap.gone) continue;
                var v = new UnitView { facing = snap.facing, hp = snap.hp, max = engine.MaxHpOf(u), down = snap.down };
                v.lastH = u.side == "foe" ? "left" : "right";
                if (snap.facing == "left" || snap.facing == "right") v.lastH = snap.facing;
                v.shadow = Add(fieldUnits, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-shadow");
                var sh = Sprite("Ui/shadow");
                if (sh != null) v.shadow.style.backgroundImage = new StyleBackground(sh);
                var e = u.side == "foe" ? engine.FoeDef(u) : null;
                if (e != null && string.IsNullOrEmpty(e.figure))
                {
                    v.beast = true;
                    var bt = Sprite("Battle/Units/" + (string.IsNullOrEmpty(e.art) ? e.id : e.art));
                    v.frameW = bt != null ? bt.width / 11f : 24;
                    v.frameH = bt != null ? bt.height : 18;
                    v.box = Add(fieldUnits, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-unit");
                    v.box.style.width = v.frameW * fk;
                    v.box.style.height = v.frameH * fk;
                    v.box.style.overflow = Overflow.Hidden;
                    v.sheet = Add(v.box, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-sheet");
                    v.sheet.style.width = v.frameW * 11 * fk;
                    v.sheet.style.height = v.frameH * fk;
                    if (bt != null) v.sheet.style.backgroundImage = new StyleBackground(bt);
                }
                else
                {
                    List<string> layers;
                    if (u.id == "you") layers = PlayerWalkLayers();
                    else if (u.side == "party") layers = new List<string> { "Walk/" + engine.data.Follower(u.def)?.sprite };
                    else layers = WalkLayers(e.figure, e.skin, e.hair, e.hairStyle, e.beard, e.worn.Select(w => (w, engine.data.Gear(w)?.slot ?? "body")));
                    (v.box, v.sheet) = Walker(fieldUnits, fk, layers);
                    v.box.AddToClassList("grid-unit");
                    v.frameW = 16; v.frameH = 24;
                }
                if (v.down) v.box.AddToClassList("grid-down");
                // health over their head
                v.hpBox = Add(fieldTags, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-hp");
                v.hpBox.AddToClassList(u.side == "party" ? "grid-hp-party" : "grid-hp-foe");
                v.hpFill = Add(v.hpBox, new VisualElement { pickingMode = PickingMode.Ignore }, "grid-hp-fill");
                SetHp(v);
                views[u.id] = v;
                Put(u.id, snap.x, snap.y);
                Face(v, snap.facing);
                // what an enemy's about to do, over its head
                if (u.side == "foe" && !busy && g.phase == "party")
                {
                    string tag = IntentTag(u);
                    if (tag != "")
                    {
                        var l = Add(fieldTags, new Label(tag) { pickingMode = PickingMode.Ignore }, "grid-intent");
                        var mv = engine.IntentMove(u);
                        if (mv != null && (mv.charge || mv.KindOf() == "heavy" || mv.KindOf() == "howl")) l.AddToClassList("grid-intent-big");
                        var p = SquarePos(snap.x, snap.y);
                        l.style.left = p.x - T16;
                        l.style.width = T16 * 3;
                        l.style.top = p.y - T16 * 0.9f - 22;
                    }
                }
                if (u.down && !busy)
                {
                    var l = Add(fieldTags, new Label("DOWN · " + u.bleedOut) { pickingMode = PickingMode.Ignore }, "grid-intent");
                    l.AddToClassList("grid-intent-big");
                    var p = SquarePos(snap.x, snap.y);
                    l.style.left = p.x - T16; l.style.width = T16 * 3; l.style.top = p.y - 20;
                }
            }
        }

        /// <summary>A word or two over an enemy's head: what it's about to do and to whom.</summary>
        string IntentTag(GridUnit u)
        {
            if (engine.HasState(u, "staggered")) return "REELING";
            if (engine.HasState(u, "fleeing")) return "RUNNING";
            switch (u.intent)
            {
                case "hang": return "HANGING BACK";
                case "line": return "LINING UP";
                case "wait": case "": return "";
            }
            var m = engine.IntentMove(u);
            if (m == null) return "";
            var t = engine.Unit(u.intentTarget);
            string who = t == null ? "" : t.id == "you" ? "YOU" : engine.UnitName(t).ToUpperInvariant();
            if (m.charge) return "CHARGE!";
            string k = m.KindOf();
            if (k == "howl") return "HOWL";
            if (k == "guard") return "ON GUARD";
            if (engine.FoeDef(u).ai == "hunter" || engine.FoeDef(u).ai == "leader") return "HUNTING " + who;
            return (k == "heavy" || k == "feint" ? "BIG BLOW: " : "") + who;
        }

        void SetHp(UnitView v)
        {
            v.hpFill.style.width = Length.Percent(Mathf.Clamp01(v.hp / Mathf.Max(1, v.max)) * 100f);
        }

        /// <summary>Puts a unit's sprite, shadow and health bar on a square (fractions for the in-between steps).</summary>
        void Put(string id, float x, float y)
        {
            if (!views.TryGetValue(id, out var v)) return;
            var p = SquarePos(x, y);
            v.px = p.x; v.py = p.y;
            float feet = p.y + T16 - fk * 2;
            v.box.style.left = Mathf.Round(p.x + T16 / 2f - v.frameW * fk / 2f);
            v.box.style.top = Mathf.Round(feet - v.frameH * fk + (v.beast ? fk * 2 : 0));
            v.shadow.style.left = p.x + T16 * 0.1f;
            v.shadow.style.top = feet - fk * 3;
            v.shadow.style.width = T16 * 0.8f;
            v.shadow.style.height = fk * 5;
            v.hpBox.style.left = p.x + T16 * 0.15f;
            v.hpBox.style.width = T16 * 0.7f;
            v.hpBox.style.top = feet - v.frameH * fk - fk * 2;
        }

        void Face(UnitView v, string facing)
        {
            v.facing = facing;
            if (facing == "left" || facing == "right") v.lastH = facing;
            if (v.beast) v.box.style.scale = new Scale(new Vector3(v.lastH == "right" ? -1 : 1, 1, 1));
            else SetFrame(v.sheet, facing, 0, fk);
        }

        /// <summary>The beasts breathe and show their tells; the walkers stand still between steps.</summary>
        void BattleTick()
        {
            if (field == null || field.panel == null) { battleTick?.Pause(); return; }
            float now = Time.realtimeSinceStartup;
            foreach (var kv in views)
            {
                var v = kv.Value;
                if (!v.beast) continue;
                var u = engine.Unit(kv.Key);
                float t = now - v.modeAt;
                if (v.mode == "attack" && t > 0.5f) v.mode = "";
                if (v.mode == "hurt" && t > 0.3f) v.mode = "";
                int frame;
                if (v.mode == "attack") frame = t < 0.12f ? 6 : t < 0.3f ? 7 : 8;
                else if (v.mode == "hurt") frame = 9;
                else if (v.mode == "dead") frame = 10;
                else
                {
                    var mv = u != null && !busy ? engine.IntentMove(u) : null;
                    bool tell = mv != null && (mv.charge || mv.KindOf() == "heavy" || mv.KindOf() == "feint");
                    frame = tell ? 4 + (int)(now / 0.32f) % 2 : (int)(now / 0.24f + kv.Key.Length) % 4;
                }
                v.sheet.style.left = -frame * v.frameW * fk;
            }
        }

        // ------------------------------------------------------------------ the side panel

        void BuildSide(VisualElement side)
        {
            var g = engine.Grid;
            var s = engine.S;

            // whose turn, and what to do
            var banner = Add(side, new VisualElement(), "grid-banner");
            if (g.phase == "party" && !busy)
            {
                Add(banner, new Label("YOUR TURN"), "grid-banner-head");
                Add(banner, new Label(selAction == "" ? "Pick one of your side, walk them (blue squares), then act. End the turn when everyone's done."
                                                      : "Pick who to hit and where to aim. The chance and the damage are shown before you strike."), "grid-banner-text");
            }
            else
            {
                banner.AddToClassList("grid-banner-foe");
                Add(banner, new Label(g.phase == "over" ? "IT'S OVER" : "THEIR TURN"), "grid-banner-head");
            }

            // your side
            var party = Add(side, new VisualElement(), "grid-box");
            foreach (var u in g.units.Where(u => u.side == "party"))
            {
                if (u.gone && u.id != "you") continue;
                var r = Add(party, new VisualElement(), "grid-who");
                if (u.id == selUnit) r.AddToClassList("grid-who-sel");
                var top = Add(r, new VisualElement(), "grid-who-top");
                Add(top, new Label(u.id == "you" ? s.name : engine.UnitName(u)), "grid-who-name");
                string status = u.down ? "DOWN · " + u.bleedOut + " rounds to get them up"
                              : g.phase != "party" ? "" : u.acted ? (u.moveLeft > 0 ? "Acted · " + u.moveLeft + " moves left" : "Done")
                              : (u.moveLeft > 0 ? u.moveLeft + " moves · action ready" : "Action ready");
                Add(top, new Label(status), u.down ? "grid-who-down" : "grid-who-status");
                MiniBar(r, engine.Hp(u), engine.MaxHpOf(u), "#c8573f", u.id == "you" ? "HEALTH" : "");
                if (u.id == "you")
                {
                    MiniBar(r, s.stamina, Rules.MaxStamina(s, engine.data), "#7fae6a", Rules.Winded(s, engine.data) ? "STAMINA · WINDED" : "STAMINA");
                    if (Rules.MaxMana(s) > 0 && s.spells.Count > 0) MiniBar(r, s.mana, Rules.MaxMana(s), "#4f86d8", "MANA");
                }
                Chips(r, u);
                string id = u.id;
                r.RegisterCallback<PointerDownEvent>(_ => { if (YourTurn && !engine.Unit(id).down) { Select(id); Render(); } });
            }

            // what the selected one can do, or where to aim
            if (YourTurn)
            {
                var sel = engine.Unit(selUnit);
                if (selAction != "" && sel != null) AimPanel(side, sel);
                else if (sel != null && !sel.gone && !sel.down) ActionPanel(side, sel);
            }

            // the enemies, and what each is about to do
            var foes = Add(side, new VisualElement(), "grid-box");
            foreach (var u in g.units.Where(u => u.side == "foe" && !u.gone))
            {
                var r = Add(foes, new VisualElement(), "grid-who");
                r.AddToClassList("grid-who-foe");
                if (u.id == selTarget && selAction != "") r.AddToClassList("grid-who-sel");
                var top = Add(r, new VisualElement(), "grid-who-top");
                var e = engine.FoeDef(u);
                Add(top, new Label(Cap(engine.UnitName(u))), "grid-who-name");
                RankBadge(top, e.rank);
                MiniBar(r, u.hp, u.maxHp, "#c8573f", "");
                if (g.phase == "party" && !busy)
                {
                    var tell = engine.IntentText(u);
                    if (tell != "") Add(r, new Label(tell), "grid-tell");
                }
                Chips(r, u);
                string id = u.id;
                r.RegisterCallback<PointerDownEvent>(_ => { if (YourTurn) AimAt(id); });
            }

            // the last few things that happened
            var log = Add(side, new VisualElement(), "grid-log");
            var lines = s.battleLog.Skip(Mathf.Max(0, s.battleLog.Count - 4)).ToList();
            for (int i = 0; i < lines.Count; i++)
                Add(log, new Label(lines[i]), i == lines.Count - 1 ? "log-line-new" : "log-line");

            if (YourTurn)
            {
                var end = Add(side, new Button(EndTurn) { text = "End turn  (T)", focusable = false }, "grid-end");
                bool anyLeft = g.units.Any(u => u.side == "party" && !u.gone && !u.down && !u.acted);
                if (!anyLeft) end.AddToClassList("grid-end-ready");
            }
            var keys = Add(side, new Label(selAction != "" ? "A / D  target   ·   W / S  aim   ·   E  strike   ·   ESC  back"
                                                    : "WASD  cursor   ·   E  select / walk / aim   ·   1-9  act   ·   TAB  next   ·   R  turn   ·   T  end turn"), "grid-keys");
        }

        void MiniBar(VisualElement parent, float value, float max, string colour, string name)
        {
            var n = Add(parent, new VisualElement(), "grid-bar");
            if (name != "")
            {
                var head = Add(n, new VisualElement(), "need-head");
                Add(head, new Label(name), "need-name");
                Add(head, new Label(Mathf.CeilToInt(Mathf.Max(0, value)) + " / " + Mathf.RoundToInt(max)), "need-value");
            }
            var track = Add(n, new VisualElement(), "need-track");
            var fill = Add(track, new VisualElement(), "need-fill");
            ColorUtility.TryParseHtmlString(colour, out var c);
            fill.style.backgroundColor = c;
            fill.style.width = Length.Percent(Mathf.Clamp01(value / Mathf.Max(1, max)) * 100f);
        }

        static readonly Dictionary<string, (string word, bool good, string tip)> StateWords = new Dictionary<string, (string, bool, string)>
        {
            ["open"] = ("OPEN", true, "Off balance: the next blow is a sure hit and a sure critical."),
            ["vulnerable"] = ("VULNERABLE", true, "Takes half again the damage."),
            ["staggered"] = ("STAGGERED", true, "Loses its next action, and can't react."),
            ["dazed"] = ("DAZED", true, "Can't react, -10% to hit, and loses whatever it was winding up to."),
            ["hobbled"] = ("HOBBLED", true, "2 fewer squares, no dodging, no charging."),
            ["bleeding"] = ("BLEEDING", true, "Loses health every round."),
            ["weakened"] = ("WEAKENED", true, "Its blows land softer."),
            ["prone"] = ("KNOCKED DOWN", false, "Easier to hit, can't react, and getting up costs a square."),
            ["enraged"] = ("ENRAGED", false, "Its blows land 30% harder."),
            ["guard"] = ("ON GUARD", false, "Half damage from in front; meets the next blow."),
            ["braced"] = ("BRACED", false, "A charge into them stops dead."),
            ["fleeing"] = ("RUNNING", true, "It's had enough."),
            ["prey"] = ("MARKED", false, "The pack leader has picked this one out. Its wolves will go for them."),
        };

        void Chips(VisualElement parent, GridUnit u)
        {
            var list = engine.StatesOf(u).Where(StateWords.ContainsKey).ToList();
            if (list.Count == 0) return;
            var row = Add(parent, new VisualElement(), "battle-chips");
            foreach (var st in list)
            {
                var (word, good, tip) = StateWords[st];
                bool forYou = u.side == "foe" ? good : !good;
                StatusChip(row, word, forYou ? "chip-good" : "chip-bad", tip);
            }
        }

        void ActionPanel(VisualElement side, GridUnit sel)
        {
            var box = Add(side, new VisualElement(), "grid-box");
            Add(box, new Label((sel.id == "you" ? "YOU" : engine.UnitName(sel).ToUpperInvariant()) + "  ·  " + (sel.acted ? "ACTED" : "ACT")), "grid-sub");
            var acts = engine.ActionsFor(sel.id);
            var grid = Add(box, new VisualElement(), "grid-acts");
            for (int i = 0; i < acts.Count; i++)
            {
                var a = acts[i];
                int n = i + 1;
                var b = new Button(() => DoAction(a)) { focusable = false };
                b.AddToClassList("grid-act");
                b.SetEnabled(a.enabled);
                b.tooltip = a.desc + (a.cost != "" ? "  (" + a.cost + ")" : "");
                Add(b, new Label(n <= 9 ? n.ToString() : ""), "command-num");
                Add(b, new Label(a.name), "grid-act-name");
                Add(b, new Label(a.enabled ? a.cost : a.why), "grid-act-sub");
                grid.Add(b);
            }
        }

        /// <summary>Aiming: the target, where on it you could aim, and for each the chance and the damage.</summary>
        void AimPanel(VisualElement side, GridUnit sel)
        {
            var box = Add(side, new VisualElement(), "grid-box");
            var act = engine.ActionsFor(sel.id).FirstOrDefault(a => a.id == selAction);
            Add(box, new Label(((act?.name ?? "Attack") + "  ·  " + (sel.id == "you" ? "you" : engine.UnitName(sel))).ToUpperInvariant()), "grid-sub");
            var t = engine.Unit(selTarget);
            if (t == null || t.gone)
            {
                Add(box, new Label("Nobody in reach. Walk closer first (Esc to go back)."), "grid-tell");
                return;
            }
            var parts = engine.PartsOf(t);
            selPart = Mathf.Clamp(selPart, 0, parts.Count - 1);
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var pv = engine.Preview(sel.id, t.id, p.id, selAction);
                int idx = i;
                var b = new Button(() => { if (selPart == idx) Strike(); else { selPart = idx; Render(); } }) { focusable = false };
                b.AddToClassList("grid-part");
                if (i == selPart) b.AddToClassList("grid-part-sel");
                b.SetEnabled(pv.ok);
                var top = Add(b, new VisualElement(), "grid-part-top");
                Add(top, new Label(p.name), "grid-part-name");
                Add(top, new Label(pv.ok ? pv.chance + "%" : pv.why), pv.ok ? "grid-part-chance" : "grid-part-why");
                if (pv.ok) Add(b, new Label(pv.min + "-" + pv.max + " damage" + (pv.graze > 0 ? "  ·  graze " + pv.graze + " if it misses" : "  ·  a miss is a miss") + (pv.kills ? "  ·  KILLS" : "")), "grid-part-sub");
                box.Add(b);
            }
            var sp = engine.Preview(sel.id, t.id, parts[selPart].id, selAction);
            if (sp.ok && sp.notes.Count > 0) Add(box, new Label(string.Join("\n", sp.notes)), "grid-notes");
            var row = Add(box, new VisualElement(), "grid-aim-row");
            var go = Add(row, new Button(Strike) { text = "Strike  (E)", focusable = false }, "grid-strike");
            go.SetEnabled(sp.ok);
            Add(row, new Button(() => { selAction = ""; Render(); }) { text = "Back  (Esc)", focusable = false }, "grid-back");
        }


        // ------------------------------------------------------------------ choosing

        void Select(string id)
        {
            var u = engine.Unit(id);
            if (u == null || u.gone || u.down || u.side != "party") return;
            selUnit = id;
            selAction = "";
            curX = u.x; curY = u.y;
        }

        /// <summary>Picks the next of your side who still has something to do (or simply the next).</summary>
        void SelectNext(bool onlyBusy)
        {
            var list = engine.Grid.units.Where(u => u.side == "party" && !u.gone && !u.down).ToList();
            if (list.Count == 0) return;
            int i = list.FindIndex(u => u.id == selUnit);
            for (int n = 1; n <= list.Count; n++)
            {
                var u = list[(i + n + list.Count) % list.Count];
                if (!onlyBusy || !u.acted || u.moveLeft > 0) { Select(u.id); return; }
            }
        }

        void OnSquare(int x, int y)
        {
            if (!YourTurn) return;
            var who = engine.At(x, y);
            if (selAction != "")
            {
                if (who != null && who.side == "foe")
                {
                    if (who.id == selTarget) Strike();
                    else if (engine.Targets(selUnit, selAction).Contains(who.id)) { selTarget = who.id; selPart = 0; Render(); }
                }
                return;
            }
            if (who != null && who.side == "party" && !who.down) { Select(who.id); Render(); return; }
            if (who != null && who.side == "foe") { AimAt(who.id); return; }
            if (engine.Reachable(selUnit).Contains((x, y)))
            {
                string id = selUnit;
                Do(() => engine.GridMove(id, x, y), "move");
            }
        }

        /// <summary>Starts aiming at an enemy with the plain attack (if they're in reach).</summary>
        void AimAt(string foeId)
        {
            var acts = engine.ActionsFor(selUnit);
            var a = acts.FirstOrDefault(x => x.id == "attack" && x.enabled);
            if (a == null) return;
            selAction = "attack";
            var targets = engine.Targets(selUnit, selAction);
            selTarget = targets.Contains(foeId) ? foeId : targets.FirstOrDefault() ?? foeId;
            selPart = 0;
            Render();
        }

        void DoAction(GridAction a)
        {
            if (!YourTurn || a == null || !a.enabled) return;
            if (a.targeted)
            {
                selAction = a.id;
                var targets = engine.Targets(selUnit, a.id);
                selTarget = targets.Contains(selTarget) ? selTarget : targets.FirstOrDefault() ?? "";
                selPart = 0;
                Render();
                return;
            }
            string id = selUnit;
            Do(() => engine.GridDo(id, a.id), a.id);
        }

        void Strike()
        {
            if (!YourTurn || selAction == "" || selTarget == "") return;
            var t = engine.Unit(selTarget);
            if (t == null) return;
            var parts = engine.PartsOf(t);
            string part = parts[Mathf.Clamp(selPart, 0, parts.Count - 1)].id;
            if (!engine.Preview(selUnit, selTarget, part, selAction).ok) return;
            string id = selUnit, target = selTarget, act = selAction;
            selAction = "";
            Do(() => engine.GridAttack(id, target, part, act), act);
        }

        void EndTurn()
        {
            if (!YourTurn) return;
            selAction = "";
            Do(engine.EndTurn, "end");
        }

        /// <summary>Does something on the grid, then plays what happened step by step.</summary>
        void Do(Func<bool> act, string what)
        {
            var snap = new Dictionary<string, Snap>();
            foreach (var u in engine.Grid.units)
                snap[u.id] = new Snap { x = u.x, y = u.y, facing = u.facing, gone = u.gone, down = u.down, hp = engine.Hp(u) };
            lastAct = what;
            if (!act()) return;
            var events = engine.Events.ToList();
            outro = !engine.S.inCombat;
            shown = snap;
            busy = true;
            SaveNow();
            Render();
            PlayEvents(events);
        }

        // ------------------------------------------------------------------ playing it out

        void PlayEvents(List<BattleEvent> events)
        {
            float at = 0.05f;
            foreach (var ev in events)
            {
                var e = ev;
                float start = at;
                at += Animate(e, start);
            }
            root.schedule.Execute(() =>
            {
                busy = false;
                shown = null;
                if (outro)
                {
                    if (engine.Grid.outcome == "won") { GameAudio.Play("victory", 0.9f); }
                    root.schedule.Execute(() => { outro = false; Render(); }).StartingIn(900);
                    Render();
                    return;
                }
                var g = engine.Grid;
                if (g.phase == "party")
                {
                    var sel = engine.Unit(selUnit);
                    if (lastAct == "end" || sel == null || sel.gone || sel.down) { var you = engine.Unit("you"); Select(you != null && !you.gone ? "you" : selUnit); SelectIfStuck(); }
                    else if (sel.acted && sel.moveLeft <= 0) SelectNext(true);
                }
                Render();
            }).StartingIn(Mathf.RoundToInt((at + 0.1f) * 1000));
        }

        void SelectIfStuck()
        {
            var sel = engine.Unit(selUnit);
            if (sel == null || sel.gone || sel.down) SelectNext(false);
        }

        /// <summary>Schedules one event's animation starting at `start` seconds; returns how long it takes.</summary>
        float Animate(BattleEvent e, float start)
        {
            int ms(float s) => Mathf.RoundToInt(s * 1000);
            views.TryGetValue(e.from ?? "", out var by);
            views.TryGetValue(e.target ?? "", out var to);
            int w = engine.Grid.w;
            switch (e.kind)
            {
                case "move":
                case "charge":
                case "knock":
                {
                    float step = e.kind == "charge" ? 0.06f : e.kind == "knock" ? 0.12f : 0.12f;
                    string id = e.kind == "knock" ? e.target : e.from;
                    if (!views.TryGetValue(id, out var v)) return 0;
                    float t = start;
                    if (e.kind == "charge") root.schedule.Execute(() => GameAudio.Play("boar_charge", 0.9f)).StartingIn(ms(start));
                    for (int i = 0; i < e.path.Count; i++)
                    {
                        int k = e.path[i];
                        int n = i;
                        root.schedule.Execute(() =>
                        {
                            var cur = engine.Grid;
                            float ox = (v.px / T16) - 1, oy = (v.py / T16) - 1;
                            int nx = k % w, ny = k / w;
                            if (e.kind != "knock") Face(v, FaceToward(nx - ox, ny - oy, v.facing));
                            if (!v.beast) SetFrame(v.sheet, v.facing, 1 + n % 2, fk);
                            Put(id, nx, ny);
                            if (e.kind == "move" && n % 2 == 0) GameAudio.PlayVariant("step_grass", 3, 0.35f);
                        }).StartingIn(ms(t));
                        t += step;
                    }
                    root.schedule.Execute(() => { if (!v.beast) SetFrame(v.sheet, v.facing, 0, fk); }).StartingIn(ms(t));
                    return t - start + 0.05f;
                }
                case "windup":
                    root.schedule.Execute(() => { if (by != null) { Shake(by.box, 4f); if (by.beast) { by.mode = "tell"; by.modeAt = Time.realtimeSinceStartup; } } }).StartingIn(ms(start));
                    return 0.35f;
                case "hit":
                case "crit":
                case "graze":
                {
                    if (by != null)
                        root.schedule.Execute(() =>
                        {
                            if (to != null) Face(by, FaceToward(to.px - by.px, to.py - by.py, by.facing));
                            Bump(by, to);
                            if (by.beast) { by.mode = "attack"; by.modeAt = Time.realtimeSinceStartup; }
                        }).StartingIn(ms(start));
                    float hitAt = start + (by != null ? 0.14f : 0f);
                    root.schedule.Execute(() => Land(e, to)).StartingIn(ms(hitAt));
                    return (by != null ? 0.14f : 0f) + 0.5f;
                }
                case "dead":
                case "flee":
                case "yield":
                    root.schedule.Execute(() =>
                    {
                        if (to == null) return;
                        if (e.kind == "dead") { if (to.beast) { to.mode = "dead"; to.modeAt = Time.realtimeSinceStartup; } else to.box.AddToClassList("grid-down"); GameAudio.Play("enemy_die", 0.8f); }
                        if (e.text != "") Popup(fieldTags, to, e.text, "pop-good");
                        if (e.kind == "flee") GameAudio.Play("flee");
                        to.box.AddToClassList("grid-fade");
                        to.shadow.AddToClassList("grid-fade");
                        to.hpBox.AddToClassList("grid-fade");
                    }).StartingIn(ms(start));
                    return 0.6f;
                case "down":
                    root.schedule.Execute(() =>
                    {
                        if (to == null) return;
                        to.box.AddToClassList("grid-down");
                        Popup(fieldTags, to, "DOWN!", "pop-crit");
                        GameAudio.Play("player_hurt", 0.8f);
                    }).StartingIn(ms(start));
                    return 0.55f;
                default:
                    root.schedule.Execute(() => Land(e, to ?? by)).StartingIn(ms(start));
                    return e.kind == "dodge" || e.kind == "parry" || e.kind == "braced" ? 0.5f : 0.4f;
            }
        }

        static string FaceToward(float dx, float dy, string keep) =>
            Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f ? keep : Mathf.Abs(dx) >= Mathf.Abs(dy) ? (dx > 0 ? "right" : "left") : (dy > 0 ? "down" : "up");

        /// <summary>A quick lunge toward the target and back.</summary>
        void Bump(UnitView v, UnitView target)
        {
            if (v == null) return;
            float dx = target != null ? Mathf.Sign(target.px - v.px) * (Mathf.Abs(target.px - v.px) > 1 ? 1 : 0) : 0;
            float dy = target != null ? Mathf.Sign(target.py - v.py) * (Mathf.Abs(target.py - v.py) > 1 ? 1 : 0) : 0;
            float reach = T16 * 0.35f;
            float t0 = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = v.box.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - t0) / 0.3f;
                if (t >= 1f) { tick?.Pause(); v.box.style.translate = new StyleTranslate(new Translate(0, 0)); return; }
                float f = t < 0.4f ? t / 0.4f : 1f - (t - 0.4f) / 0.6f;
                v.box.style.translate = new StyleTranslate(new Translate(Mathf.Round(dx * reach * f), Mathf.Round(dy * reach * f)));
            }).Every(16);
            if (v.beast) return;
            GameAudio.Play("swing", 0.7f, 0.1f);
        }

        /// <summary>Something arriving on someone: the number, the flash, the sound, the effect.</summary>
        void Land(BattleEvent e, UnitView v)
        {
            if (v == null) return;
            string cls = e.kind == "crit" ? "pop-crit" : e.kind == "graze" ? "pop-glance" : e.kind == "heal" ? "pop-heal"
                       : e.kind == "dodge" || e.kind == "parry" || e.kind == "open" || e.kind == "braced" ? "pop-perfect"
                       : e.kind == "rage" || e.kind == "howl" ? "pop-crit"
                       : e.kind == "miss" ? "pop-miss"
                       : e.kind == "hit" || e.kind == "bleed" ? "pop-hit" : "pop-good";
            Popup(fieldTags, v, e.text, cls);
            bool hit = e.kind == "hit" || e.kind == "crit" || e.kind == "graze" || e.kind == "bleed";
            if (hit)
            {
                v.hp -= e.amount;
                SetHp(v);
                HitFlash(v.box);
                if (v.beast) { v.mode = "hurt"; v.modeAt = Time.realtimeSinceStartup; }
                if (e.kind == "crit") Shake(field, 6f);
            }
            if (e.kind == "heal" && e.text.Contains("health")) { v.hp += e.amount; SetHp(v); }
            if (e.kind == "heal" && e.text == "Back up") { v.box.RemoveFromClassList("grid-down"); v.hp = v.max * 0.2f; SetHp(v); }
            if (e.kind == "dodge") Hop(v);
            BattleSound(e);
            BattleEffect(e, v);
        }

        void Hop(UnitView v)
        {
            float t0 = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = v.box.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - t0) / 0.35f;
                if (t >= 1f) { tick?.Pause(); v.box.style.translate = new StyleTranslate(new Translate(0, 0)); return; }
                v.box.style.translate = new StyleTranslate(new Translate(0, -Mathf.Round(Mathf.Sin(t * Mathf.PI) * T16 * 0.4f)));
            }).Every(16);
        }

        /// <summary>The sound for one thing that happens in a fight.</summary>
        void BattleSound(BattleEvent e)
        {
            var from = engine.Unit(e.from ?? "");
            var target = engine.Unit(e.target ?? "");
            string art = target?.side == "foe" ? (engine.FoeDef(target).art ?? "") : from?.side == "foe" ? (engine.FoeDef(from).art ?? "") : "";
            bool hit = e.kind == "hit" || e.kind == "crit" || e.kind == "graze";
            if (hit && target?.side == "foe")
            {
                bool fire = lastAct == "spell:kindle" || lastAct == "move:flare";
                if (lastAct == "move:flare") GameAudio.Play("spell_flare");
                else if (fire) GameAudio.Play("spell_kindle");
                GameAudio.Play(e.kind == "crit" ? "hit_crit" : e.kind == "graze" ? "hit_glance" : "hit", 1f, 0.05f);
                if (e.kind != "graze") GameAudio.Play(art == "boar" ? "boar_squeal" : "beast_hurt", 0.7f, 0.08f, 0.15f);
            }
            else if (hit)
            {
                if (from?.side == "foe") GameAudio.Play(art == "boar" ? "boar_charge" : art.StartsWith("wolf") ? "wolf_snarl" : "swing", 0.8f);
                GameAudio.Play(e.target == "you" ? "player_hurt" : "hit", 1f, 0.05f, 0.1f);
            }
            else if (e.kind == "parry" || e.kind == "braced") GameAudio.Play("parry");
            else if (e.kind == "dodge" || e.kind == "miss") GameAudio.Play("swing", 0.7f, 0.1f);
            else if (e.kind == "open") GameAudio.Play("hit_glance", 0.6f);
            else if (e.kind == "howl" || e.kind == "rage") GameAudio.Play(art.StartsWith("wolf") ? "wolf_howl" : art == "boar" ? "boar_squeal" : "wolf_growl", 0.9f);
            else if (e.kind == "guard" || e.kind == "brace") GameAudio.Play("block");
            else if (e.kind == "stun") GameAudio.Play("hit_glance", 0.7f);
            else if (e.kind == "heal" && lastAct != "drink" && !lastAct.StartsWith("item:") ) GameAudio.Play("heal");
            else if (e.kind == "heal") GameAudio.Play("heal", 0.7f);
        }

        /// <summary>The effect for one thing that happens in a fight, played over whoever it happens to.</summary>
        void BattleEffect(BattleEvent e, UnitView v)
        {
            if (fieldFx == null || fieldFx.panel == null) return;
            var centre = new Vector2(v.px + T16 / 2f, v.py + T16 / 2f - v.frameH * fk * 0.25f);
            float size = T16 * 1.6f;
            var target = engine.Unit(e.target ?? "");
            bool hit = e.kind == "hit" || e.kind == "crit" || e.kind == "graze";
            if (hit && target?.side == "foe")
            {
                if (lastAct == "move:flare") Spawn(fieldFx, "fx_flare", 6, 64, 64, size * 1.2f / 64f, centre, 0.6f);
                else if (lastAct == "spell:kindle") Spawn(fieldFx, "fx_fire", 6, 48, 48, size / 48f, centre, 0.55f);
                else if (e.kind == "crit") Spawn(fieldFx, "fx_slash_crit", 5, 64, 64, size * 1.15f / 64f, centre, 0.4f);
                else if (e.kind == "graze") Spawn(fieldFx, "fx_hit", 4, 32, 32, size * 0.45f / 32f, centre, 0.25f);
                else Spawn(fieldFx, "fx_slash", 4, 48, 48, size / 48f, centre, 0.3f);
                if (e.kind == "crit") ScreenFlash(arena);
            }
            else if (hit) Spawn(fieldFx, "fx_bite", 4, 32, 32, size * 0.7f / 32f, centre, 0.3f);
            else if (e.kind == "parry" || e.kind == "braced") Spawn(fieldFx, "fx_parry", 4, 32, 32, size * 0.6f / 32f, centre, 0.3f);
            else if (e.kind == "heal") Spawn(fieldFx, "fx_heal", 5, 32, 32, size * 0.7f / 32f, centre, 0.7f);
        }

        // ------------------------------------------------------------------ feedback

        /// <summary>A number or word rising off someone on the field.</summary>
        void Popup(VisualElement layer, UnitView v, string text, string cls)
        {
            if (layer == null || v == null || string.IsNullOrEmpty(text)) return;
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("pop");
            l.AddToClassList("grid-pop");
            l.AddToClassList(cls);
            l.style.left = v.px - T16;
            l.style.width = T16 * 3;
            float top = v.py - v.frameH * fk * 0.5f;
            l.style.top = top;
            layer.Add(l);
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = l.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - start) / 0.9f;
                if (t >= 1f) { tick?.Pause(); l.RemoveFromHierarchy(); return; }
                l.style.translate = new StyleTranslate(new Translate(0, -60f * t));
                l.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }).Every(16);
        }

        /// <summary>A word rising off a panel (the forge uses it).</summary>
        static void Popup(VisualElement card, string text, string cls, float yOffset)
        {
            if (card == null || string.IsNullOrEmpty(text)) return;
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("pop");
            l.AddToClassList(cls);
            card.Add(l);
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = l.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - start) / 0.9f;
                if (t >= 1f) { tick?.Pause(); l.RemoveFromHierarchy(); return; }
                l.style.translate = new StyleTranslate(new Translate(0, yOffset - 70f * t));
                l.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }).Every(16);
        }

        static void Shake(VisualElement target, float strength)
        {
            if (target == null) return;
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = target.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - start) / 0.28f;
                if (t >= 1f) { tick?.Pause(); target.style.translate = new StyleTranslate(new Translate(0, 0)); return; }
                float x = Mathf.Sin(t * 40f) * strength * (1f - t);
                target.style.translate = new StyleTranslate(new Translate(x, 0));
            }).Every(16);
        }

        /// <summary>A small coloured tag: OPEN, ENRAGED, WINDED.</summary>
        static void StatusChip(VisualElement parent, string text, string cls, string tip)
        {
            var l = Add(parent, new Label(text), "battle-chip");
            l.AddToClassList(cls);
            l.tooltip = tip;
        }

        static readonly string[] RankColours = { "#8a918d", "#7fae6a", "#4f9b97", "#5b8fd0", "#a77fd8", "#e3a64e", "#f2d36b" };

        static string RankColour(string rank) => RankColours[Rules.RankIndex(rank)];

        static Label RankBadge(VisualElement parent, string rank)
        {
            var l = Add(parent, new Label(string.IsNullOrEmpty(rank) ? "F" : rank), "rank-badge");
            ColorUtility.TryParseHtmlString(RankColour(rank), out var c);
            l.style.color = c;
            l.style.borderTopColor = l.style.borderBottomColor = l.style.borderLeftColor = l.style.borderRightColor = c;
            l.tooltip = Rules.RankName(rank) + " rank";
            return l;
        }

        // ------------------------------------------------------------------ keys

        /// <summary>Keys during a fight.</summary>
        bool BattleKey(KeyDownEvent e)
        {
            if (!YourTurn) return true;
            var k = NavKey(e.keyCode);
            bool confirm = e.keyCode == KeyCode.E || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space;
            if (selAction != "")
            {
                var targets = engine.Targets(selUnit, selAction);
                if (k == KeyCode.LeftArrow || k == KeyCode.RightArrow)
                {
                    if (targets.Count > 0)
                    {
                        int i = targets.IndexOf(selTarget);
                        selTarget = targets[((i < 0 ? 0 : i) + (k == KeyCode.RightArrow ? 1 : -1) + targets.Count) % targets.Count];
                        selPart = 0;
                        Render();
                    }
                }
                else if (k == KeyCode.UpArrow || k == KeyCode.DownArrow)
                {
                    var t = engine.Unit(selTarget);
                    if (t != null) { int n = engine.PartsOf(t).Count; selPart = (selPart + (k == KeyCode.DownArrow ? 1 : -1) + n) % n; Render(); }
                }
                else if (confirm) Strike();
                else if (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Backspace) { selAction = ""; Render(); }
                return true;
            }
            if (k == KeyCode.LeftArrow || k == KeyCode.RightArrow || k == KeyCode.UpArrow || k == KeyCode.DownArrow)
            {
                if (!cursorOn) { var su = engine.Unit(selUnit); if (su != null) { curX = su.x; curY = su.y; } }
                cursorOn = true;
                int nx = curX + (k == KeyCode.RightArrow ? 1 : k == KeyCode.LeftArrow ? -1 : 0);
                int ny = curY + (k == KeyCode.DownArrow ? 1 : k == KeyCode.UpArrow ? -1 : 0);
                if (engine.OnGrid(nx, ny)) { curX = nx; curY = ny; }
                PlaceCursor();
                return true;
            }
            if (confirm) { if (cursorOn) OnSquare(curX, curY); return true; }
            if (e.keyCode == KeyCode.Tab) { SelectNext(false); Render(); return true; }
            if (e.keyCode == KeyCode.T) { EndTurn(); return true; }
            if (e.keyCode == KeyCode.R)
            {
                var u = engine.Unit(selUnit);
                if (u != null)
                {
                    string next = u.facing == "right" ? "down" : u.facing == "down" ? "left" : u.facing == "left" ? "up" : "right";
                    engine.GridFace(selUnit, next);
                    Render();
                }
                return true;
            }
            if (e.keyCode == KeyCode.Escape) { cursorOn = false; PlaceCursor(); return true; }
            int num = e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9 ? e.keyCode - KeyCode.Alpha1
                    : e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad9 ? e.keyCode - KeyCode.Keypad1 : -1;
            if (num >= 0)
            {
                var acts = engine.ActionsFor(selUnit);
                if (num < acts.Count) DoAction(acts[num]);
            }
            return true;
        }
    }
}
