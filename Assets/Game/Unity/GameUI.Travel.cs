using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The walkable world, seen from above and zoomed right in. Arrow keys walk one tile at a time (hold Shift to run),
    /// Space talks to or uses what you're facing, Enter opens the actions (camping). Story scenes that happen out here
    /// show in a box at the bottom of the screen, over the world.
    /// </summary>
    public partial class GameUI
    {
        const int Tile = 16;
        const float WalkSeconds = 0.2f, RunSeconds = 0.11f;

        VisualElement worldView, worldLayer, playerBox, playerSheet;
        // your party's walkers, in party order, and where each is walking from and to
        readonly List<(VisualElement box, VisualElement sheet)> partyWalkers = new List<(VisualElement, VisualElement)>();
        readonly List<(Vector2 from, Vector2 to, string facing)> partySteps = new List<(Vector2, Vector2, string)>();
        Label usePrompt;
        readonly Dictionary<string, (VisualElement box, VisualElement sheet)> objectSprites = new Dictionary<string, (VisualElement, VisualElement)>();
        readonly Dictionary<string, string> npcFacing = new Dictionary<string, string>();
        int worldScale = 4;
        Vector2 worldSize;
        VisualElement weatherFx;               // rain streaks, moved every frame
        float weatherTile, weatherSpeed;
        string drawnWeather = "";
        IVisualElementScheduledItem worldTick;

        // input and movement
        readonly List<string> heldDirs = new List<string>();
        bool runHeld, actionsOpen, moving;
        float moveStart, moveSeconds, turnedAt;
        Vector2 moveFrom, moveTo;
        ChoiceDef pendingStep;
        int stepCount, unsavedSteps;

        public bool InWorld => engine.Area != null && engine.S.scene != Engine.Death && engine.S.scene != Engine.Collapse;

        float TravelHeight()
        {
            float h = mainBox != null ? mainBox.layout.height : float.NaN;
            return float.IsNaN(h) || h < 400 ? 900f : h;
        }

        float TravelWidth()
        {
            float w = mainBox != null ? mainBox.layout.width : float.NaN;
            return float.IsNaN(w) || w < 600 ? 1700f : w;
        }

        // ------------------------------------------------------------------ drawing

        void RenderWorld()
        {
            var s = engine.S;
            var a = engine.Area;
            main.Clear();
            objectSprites.Clear();
            worldSize = new Vector2(TravelWidth(), TravelHeight());

            worldView = Add(main, new VisualElement(), "world-view");
            worldView.style.width = worldSize.x;
            worldView.style.height = worldSize.y;
            worldScale = Mathf.Max(3, Mathf.FloorToInt(worldSize.y / (11 * Tile)));
            int k = worldScale, T = Tile * k;

            worldLayer = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "world-layer");
            worldLayer.style.width = Engine.Width(a) * T;
            worldLayer.style.height = Engine.Height(a) * T;
            var ground = Sprite("World/" + a.id);
            if (ground != null) worldLayer.style.backgroundImage = new StyleBackground(ground);

            // People and things, then you, drawn top to bottom so whoever is lower stands in front
            var things = engine.Objects().Where(o => !string.IsNullOrEmpty(o.sprite)).OrderBy(o => o.y).ToList();
            bool youDrawn = false;
            partyWalkers.Clear();
            foreach (var o in things)
            {
                if (!youDrawn && o.y > s.ay) { DrawYouAndFollower(); youDrawn = true; }
                DrawObject(o);
            }
            if (!youDrawn) DrawYouAndFollower();

            var over = Sprite("World/" + a.id + "_over");
            if (over != null)
            {
                var ov = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "world-over");
                ov.style.backgroundImage = new StyleBackground(over);
            }
            DrawLight(a, k);
            DrawWeather(a, k);

            PlaceYou(new Vector2(s.ax, s.ay), moving ? 1 + stepCount % 2 : 0);
            PlaceParty();

            usePrompt = Add(worldView, new Label(""), "world-prompt");
            bool canCamp = a.camp && !a.indoor;
            var help = Add(worldView, new Label("Arrows  walk     Shift  run     Space  talk / use" + (canCamp ? "     Enter  camp" : "") + "     I  menus"), "world-help");
            UpdatePrompt();

            // The box at the bottom: a story scene, a message, or the actions
            shownChoices = new List<ChoiceView>();
            bool scene = s.scene != Engine.TravelId || s.inCombat;
            if (scene)
            {
                var sc = engine.Current;
                DialogueBox(sc.title, s.result, engine.Paragraphs(), engine.Choices(), false, false, sc.speaker);
            }
            else if (!string.IsNullOrEmpty(s.result)) DialogueBox(null, s.result, null, null, true);
            else if (actionsOpen) DialogueBox("What now?", null, null, engine.Choices(), false, true);

            bool talking = worldView.Q(className: "dialogue") != null;
            help.style.display = talking ? DisplayStyle.None : DisplayStyle.Flex;
            hud.EnableInClassList("hud-talking", talking);   // the bars step aside while the dialogue box is up
            if (worldTick == null) worldTick = root.schedule.Execute(WorldTick).Every(16);
        }

        void DialogueBox(string title, string result, List<string> paras, List<ChoiceView> choices, bool message, bool actions = false, string speaker = null)
        {
            var box = Add(worldView, new VisualElement(), "dialogue");
            if (!string.IsNullOrEmpty(speaker)) Add(box, new Label(engine.Fill(speaker)), "dialogue-speaker");
            var scroll = Add(box, new ScrollView(ScrollViewMode.Vertical), "dialogue-scroll");
            if (string.IsNullOrEmpty(speaker) && !string.IsNullOrEmpty(title)) Add(scroll, new Label(engine.Fill(title)), "dialogue-title");
            if (!string.IsNullOrEmpty(result)) Add(scroll, new Label(result), message ? "dialogue-text" : "result");
            if (paras != null) foreach (var p in paras) Add(scroll, new Label(p), "dialogue-text");
            if (message)
            {
                Add(box, new Label("Space ▼"), "dialogue-more");
                box.RegisterCallback<PointerDownEvent>(_ => DismissMessage());
                return;
            }
            if (choices == null) return;
            shownChoices = choices;
            var list = Add(scroll, new VisualElement(), "choices");
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                var b = new Button(() => { actionsOpen = false; Pick(c); }) { focusable = false };
                b.AddToClassList("choice");
                b.SetEnabled(c.enabled);
                Add(b, new Label(i < 9 ? (i + 1).ToString() : ""), "choice-num");
                var text = Add(b, new VisualElement(), "choice-text");
                Add(text, new Label(engine.Fill(c.def.label)), "choice-label");
                if (!c.enabled && !string.IsNullOrEmpty(c.reason)) Add(text, new Label(c.reason), "choice-reason");
                else if (!string.IsNullOrEmpty(c.cost)) Add(text, new Label(c.cost), "choice-meta");
                list.Add(b);
            }
            if (actions)
            {
                var close = new Button(() => { actionsOpen = false; Render(); }) { focusable = false };
                close.AddToClassList("choice");
                Add(close, new Label("Esc"), "choice-num");
                Add(close, new Label("Keep walking"), "choice-label");
                list.Add(close);
            }
        }

        void DismissMessage()
        {
            engine.S.result = "";
            Render();
        }

        string PlayerSheet()
        {
            var s = engine.S;
            string fig = (string.IsNullOrEmpty(s.figure) ? s.gender : s.figure) == "female" ? "female" : "male";
            string skin = Key(s.skin) == "" ? "light" : Key(s.skin);
            string hair = Key(s.hair) == "" || Key(s.hair) == "shaved" ? "dark_brown" : Key(s.hair);
            return "Walk/walk_" + fig + "_" + skin + "_" + hair;
        }

        static int FacingRow(string f) => f == "up" ? 1 : f == "left" ? 2 : f == "right" ? 3 : 0;

        (VisualElement box, VisualElement sheet) Walker(string sheetPath)
        {
            int k = worldScale;
            var box = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "walker");
            box.style.width = 16 * k;
            box.style.height = 24 * k;
            var sheet = Add(box, new VisualElement { pickingMode = PickingMode.Ignore }, "walker-sheet");
            sheet.style.width = 48 * k;
            sheet.style.height = 96 * k;
            var tex = Sprite(sheetPath) ?? Sprite("Walk/walk_male_light_dark_brown");
            if (tex != null) sheet.style.backgroundImage = new StyleBackground(tex);
            return (box, sheet);
        }

        void SetFrame(VisualElement sheet, string facing, int frame)
        {
            int k = worldScale;
            sheet.style.left = -frame * 16 * k;
            sheet.style.top = -FacingRow(facing) * 24 * k;
        }

        void DrawYou()
        {
            (playerBox, playerSheet) = Walker(PlayerSheet());
        }

        /// <summary>You, and whoever's walking with you (drawn behind you when they're above you on the screen).</summary>
        /// <summary>The weather over the world: a tint for rain, fog or heat, and falling rain drawn as repeating tiles.</summary>
        void DrawWeather(AreaDef a, int k)
        {
            weatherFx = null;
            var w = Rules.WeatherNow(engine.S, engine.data);
            drawnWeather = w?.id ?? "";
            if (w == null || a.indoor || string.IsNullOrEmpty(w.overlay)) return;
            var tint = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "weather-tint");
            tint.AddToClassList("weather-" + w.overlay);
            var tex = Sprite("World/fx_" + w.overlay);
            if (tex == null) return;
            weatherTile = 96 * k;
            weatherSpeed = w.overlay == "storm" ? 1100 : w.overlay == "rain" ? 700 : 260;
            weatherFx = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "weather-fx");
            int cols = Mathf.CeilToInt(worldSize.x / weatherTile) + 2, rows = Mathf.CeilToInt(worldSize.y / weatherTile) + 2;
            weatherFx.style.width = cols * weatherTile;
            weatherFx.style.height = rows * weatherTile;
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    var t = Add(weatherFx, new VisualElement { pickingMode = PickingMode.Ignore }, "weather-tile");
                    t.style.left = x * weatherTile;
                    t.style.top = y * weatherTile;
                    t.style.width = weatherTile;
                    t.style.height = weatherTile;
                    t.style.backgroundImage = new StyleBackground(tex);
                }
        }

        void MoveWeather()
        {
            if (weatherFx == null || weatherTile <= 0) return;
            float t = Time.realtimeSinceStartup * weatherSpeed;
            weatherFx.style.top = (t % weatherTile) - weatherTile;
            weatherFx.style.left = -((t * 0.33f) % weatherTile) - 1;
        }

        /// <summary>
        /// You. Like the old Final Fantasy games, only you are drawn walking about: your party is with you,
        /// and they show up beside you in battle.
        /// </summary>
        void DrawYouAndFollower()
        {
            DrawYou();
        }

        void PlaceParty()
        {
            var s = engine.S;
            for (int i = 0; i < s.party.Count && i < partyWalkers.Count; i++)
                PlaceMemberSprite(i, new Vector2(s.party[i].x, s.party[i].y), s.party[i].facing, 0);
        }

        void PlaceMemberSprite(int i, Vector2 tile, string facing, int frame)
        {
            var (box, sheet) = partyWalkers[i];
            if (box == null) return;
            int k = worldScale, T = Tile * k;
            box.style.left = Mathf.Round(tile.x * T);
            box.style.top = Mathf.Round(tile.y * T - 8 * k);
            SetFrame(sheet, facing, frame);
        }

        void DrawObject(WorldObj o)
        {
            int k = worldScale, T = Tile * k;
            if (o.sprite.StartsWith("npc_"))
            {
                var w = Walker("Walk/" + o.sprite);
                SetFrame(w.sheet, npcFacing.TryGetValue(o.id, out var f) ? f : o.facing, 0);
                w.box.style.left = o.x * T;
                w.box.style.top = o.y * T - 8 * k;
                objectSprites[o.id] = w;
                return;
            }
            var e = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "world-thing");
            var tex = Sprite("World/" + o.sprite);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            // a tall thing (the class stone) stands up out of its tile
            int extra = tex != null ? Mathf.Max(0, tex.height - Tile) * k : 0;
            e.style.width = T;
            e.style.height = T + extra;
            e.style.left = o.x * T;
            e.style.top = o.y * T - extra;
        }

        /// <summary>Puts you on a (possibly in-between) tile and moves the camera to keep you in the middle.</summary>
        void PlaceYou(Vector2 tile, int frame)
        {
            if (playerBox == null || worldLayer == null) return;
            int k = worldScale, T = Tile * k;
            playerBox.style.left = Mathf.Round(tile.x * T);
            playerBox.style.top = Mathf.Round(tile.y * T - 8 * k);
            SetFrame(playerSheet, engine.S.facing, frame);

            var a = engine.Area;
            float lw = Engine.Width(a) * T, lh = Engine.Height(a) * T;
            float cx = (tile.x + 0.5f) * T, cy = (tile.y + 0.5f) * T;
            float ox = lw <= worldSize.x ? (worldSize.x - lw) / 2 : Mathf.Clamp(worldSize.x / 2 - cx, worldSize.x - lw, 0);
            float oy = lh <= worldSize.y ? (worldSize.y - lh) / 2 : Mathf.Clamp(worldSize.y / 2 - cy, worldSize.y - lh, 0);
            worldLayer.style.left = Mathf.Round(ox);
            worldLayer.style.top = Mathf.Round(oy);
        }

        void UpdatePrompt()
        {
            if (usePrompt == null) return;
            string label = engine.Travelling && string.IsNullOrEmpty(engine.S.result) && !actionsOpen ? engine.UseLabel() : null;
            usePrompt.text = label == null ? "" : "Space: " + label;
            usePrompt.style.display = label == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ walking

        bool FreeToWalk => engine.Travelling && string.IsNullOrEmpty(engine.S.result) && !actionsOpen && openMenu == "" && !busy;

        void WorldTick()
        {
            if (worldView == null || worldView.panel == null) return;
            MoveWeather();
            var s = engine.S;
            if (moving)
            {
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - moveStart) / moveSeconds);
                PlaceYou(Vector2.Lerp(moveFrom, moveTo, t), t < 0.55f ? 1 + stepCount % 2 : 0);
                for (int i = 0; i < partySteps.Count && i < partyWalkers.Count; i++)
                    if (partySteps[i].from != partySteps[i].to)
                        PlaceMemberSprite(i, Vector2.Lerp(partySteps[i].from, partySteps[i].to, t), partySteps[i].facing, t < 0.55f ? 1 + (stepCount + i + 1) % 2 : 0);
                if (t < 1f) return;
                moving = false;
                var step = pendingStep;
                pendingStep = null;
                string areaBefore = s.area;
                engine.Choose(step);
                unsavedSteps++;
                if (!engine.Travelling || !string.IsNullOrEmpty(s.result) || s.inCombat || s.area != areaBefore)
                {
                    heldDirs.Clear();
                    SaveNow();
                    unsavedSteps = 0;
                    Render();
                    return;
                }
                if ((Rules.WeatherNow(s, engine.data)?.id ?? "") != drawnWeather) { Render(); return; }   // the weather's turned
                RenderHud();
                UpdatePrompt();
                PlaceYou(new Vector2(s.ax, s.ay), 0);
                PlaceParty();
            }
            if (!FreeToWalk) return;
            if (heldDirs.Count == 0)
            {
                if (unsavedSteps > 0) { SaveNow(); unsavedSteps = 0; }
                return;
            }
            string dir = heldDirs[heldDirs.Count - 1];
            if (s.facing != dir)
            {
                engine.Face(dir);
                turnedAt = Time.realtimeSinceStartup;
                SetFrame(playerSheet, dir, 0);
                UpdatePrompt();
                return;
            }
            if (Time.realtimeSinceStartup - turnedAt < 0.08f) return;   // a quick tap just turns you
            var c = engine.StepChoice(runHeld);
            if (c != null && c.stamina > 0 && s.stamina < c.stamina) c = engine.StepChoice(false);
            if (c == null) return;
            var (dx, dy) = Engine.Dir(dir);
            moveFrom = new Vector2(s.ax, s.ay);
            moveTo = moveFrom + new Vector2(dx, dy);
            // each member walks to where the one ahead of them is now (the same rule as Engine.PartyFollows)
            partySteps.Clear();
            var ahead = moveFrom;
            foreach (var m in s.party)
            {
                var from = new Vector2(m.x, m.y);
                if (from == ahead) { partySteps.Add((from, from, m.facing)); continue; }
                var fd = ahead - from;
                partySteps.Add((from, ahead, fd.x > 0 ? "right" : fd.x < 0 ? "left" : fd.y > 0 ? "down" : fd.y < 0 ? "up" : m.facing));
                ahead = from;
            }
            moveSeconds = c.label == "Run" ? RunSeconds : WalkSeconds;
            moveStart = Time.realtimeSinceStartup;
            pendingStep = c;
            moving = true;
            stepCount++;
            StepSound((int)moveTo.x, (int)moveTo.y, c.label == "Run");
            StepEffects(moveFrom, c.label == "Run");
        }

        /// <summary>Keys in the world. Returns true if it used the key.</summary>
        bool WorldKeyDown(KeyDownEvent e)
        {
            runHeld = e.shiftKey;
            string dir = e.keyCode switch
            {
                KeyCode.UpArrow => "up",
                KeyCode.DownArrow => "down",
                KeyCode.LeftArrow => "left",
                KeyCode.RightArrow => "right",
                _ => null,
            };
            if (dir != null)
            {
                heldDirs.Remove(dir);
                heldDirs.Add(dir);
                return true;
            }
            if (e.keyCode == KeyCode.LeftShift || e.keyCode == KeyCode.RightShift) { runHeld = true; return true; }

            var s = engine.S;
            bool confirm = e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (engine.Travelling && !string.IsNullOrEmpty(s.result))
            {
                if (confirm) { DismissMessage(); return true; }
                return dir != null;
            }
            if (actionsOpen && e.keyCode == KeyCode.Escape) { actionsOpen = false; Render(); return true; }
            if (!engine.Travelling || moving || busy) return false;
            if (e.keyCode == KeyCode.Space)
            {
                var use = engine.UseChoice();
                if (use == null) return true;
                var (dx, dy) = Engine.Dir(s.facing);
                var o = engine.ObjectAt(s.ax + dx, s.ay + dy);
                if (o != null && o.kind == "npc") npcFacing[o.id] = Opposite(s.facing);
                heldDirs.Clear();
                engine.Choose(use);
                AfterAction();
                return true;
            }
            if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && engine.Area.camp && !engine.Area.indoor)
            {
                actionsOpen = !actionsOpen;
                heldDirs.Clear();
                Render();
                return true;
            }
            return false;
        }

        void WorldKeyUp(KeyUpEvent e)
        {
            runHeld = e.shiftKey && e.keyCode != KeyCode.LeftShift && e.keyCode != KeyCode.RightShift;
            string dir = e.keyCode switch
            {
                KeyCode.UpArrow => "up",
                KeyCode.DownArrow => "down",
                KeyCode.LeftArrow => "left",
                KeyCode.RightArrow => "right",
                _ => null,
            };
            if (dir != null) heldDirs.Remove(dir);
        }

        static string Opposite(string f) => f == "up" ? "down" : f == "down" ? "up" : f == "left" ? "right" : "left";
    }
}
