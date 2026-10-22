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
        float breathAt, hudDrawnAt;

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
            DrawWalk();

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
            var help = Add(worldView, new Label("WASD  walk     Shift  run     E  talk / use" + (canCamp ? "     R  camp" : "") + "     I  inventory     Tab  menus"), "world-help");
            UpdatePrompt();

            // The box at the bottom: a story scene, a message, or the actions
            shownChoices = new List<ChoiceView>();
            bool scene = s.scene != Engine.TravelId || s.inCombat;
            if (!HasScript) dirScene = null;
            dlgDirector = false;
            if (scene && HasScript) DirectorDraw();
            else if (scene)
            {
                var sc = engine.Current;
                DialogueBox(sc.title, s.result, engine.Paragraphs(), engine.Choices(), false, false, sc.speaker);
            }
            else if (!string.IsNullOrEmpty(s.result)) DialogueBox(null, s.result, null, null, true);
            else if (actionsOpen) DialogueBox("What now?", null, null, engine.Choices(), false, true);

            bool talking = worldView.Q(className: "dialogue") != null;
            help.style.display = talking || DirectorActive ? DisplayStyle.None : DisplayStyle.Flex;
            hud.EnableInClassList("hud-talking", talking);   // the bars step aside while the dialogue box is up
            if (worldTick == null) worldTick = root.schedule.Execute(WorldTick).Every(16);
        }

        // Dialogue, the way most RPGs do it (David, 20 Oct): a portrait of whoever's talking beside a speech bubble with their
        // name on it, the words typed out, one short page at a time. Space finishes the page, then turns to the next;
        // the choices come up on the last page. Narration (nobody talking) has no portrait.
        const float TypeCharsPerSecond = 60f;
        const int PageChars = 150;   // short boxes read better than walls of text (dialogue research, 21 Oct)
        string dlgKey = "";
        int dlgPage, dlgTypedPage = -1;
        List<string> dlgPages = new List<string>();
        List<string> dlgMarks = new List<string>();   // per page: "" or "voice:Strength" or "mood:happy"
        string dlgWho;
        VisualElement dlgBoxEl, dlgPortrait;
        Label dlgSpeaker, dlgBond, dlgMood;
        float[] dlgTimes;     // when each letter of the page appears (pauses after full stops and commas)
        int dlgShown;         // letters typed so far, for the voice blips
        Label dlgText;
        VisualElement dlgChoices;
        Label dlgMore;
        List<ChoiceView> dlgPendingChoices;
        IVisualElementScheduledItem dlgTick;
        float dlgStart;
        bool dlgMessage;
        bool dlgDirector;   // a line in an in-world cutscene: Space moves the cutscene on

        /// <summary>Splits text into short pages, at sentence ends where it can.</summary>
        static List<string> Pages(IEnumerable<string> parts) => Pages(parts, new List<string>());

        static readonly System.Text.RegularExpressions.Regex MarkRx = new System.Text.RegularExpressions.Regex(@"^\[\[(\w+:[^\]]+)\]\]");

        /// <summary>Splits text into short pages, at sentence ends where it can; marks says each page's mood or inner voice.</summary>
        static List<string> Pages(IEnumerable<string> parts, List<string> marks)
        {
            var pages = new List<string>();
            marks.Clear();
            foreach (var raw0 in parts)
            {
                if (string.IsNullOrWhiteSpace(raw0)) continue;
                string raw = raw0.Trim(), mark = "";
                var m = MarkRx.Match(raw);
                if (m.Success) { mark = m.Groups[1].Value; raw = raw.Substring(m.Length); }
                int before = pages.Count;
                string t = raw.Trim();
                while (t.Length > PageChars)
                {
                    int cut = -1;
                    for (int k = PageChars; k > PageChars / 3; k--)
                        if ((t[k - 1] == '.' || t[k - 1] == '?' || t[k - 1] == '!' || (t[k - 1] == '"' && k > 1 && ".?!,".IndexOf(t[k - 2]) >= 0)) && k < t.Length && t[k] == ' ')
                        { cut = k; break; }
                    if (cut < 0) break;
                    pages.Add(t.Substring(0, cut).Trim());
                    t = t.Substring(cut).Trim();
                }
                pages.Add(t);
                while (marks.Count < pages.Count) marks.Add(mark);
            }
            return pages;
        }

        bool DialoguePaging => worldView != null && dlgText != null && dlgText.panel != null && (dlgTick != null || dlgPage < dlgPages.Count - 1);

        void DialogueBox(string title, string result, List<string> paras, List<ChoiceView> choices, bool message, bool actions = false, string speaker = null)
        {
            dlgTick?.Pause();
            dlgTick = null;
            dlgMessage = message;
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(result)) parts.AddRange(result.Split(new[] { "\n\n" }, System.StringSplitOptions.RemoveEmptyEntries));   // a blank line starts a new box (a new mood, or an inner voice)
            if (paras != null) parts.AddRange(paras);
            var marks = new List<string>();
            var pages = Pages(parts, marks);
            string key = engine.S.scene + "|" + string.Join("§", pages) + "|" + (choices?.Count ?? 0) + "|" + message;
            if (key != dlgKey) { dlgKey = key; dlgPage = 0; dlgTypedPage = -1; }
            dlgPages = pages;
            dlgMarks = marks;
            if (dlgPage >= pages.Count) dlgPage = Mathf.Max(0, pages.Count - 1);

            string who = string.IsNullOrEmpty(speaker) ? null : engine.Fill(speaker);
            dlgWho = who;
            var box = Add(worldView, new VisualElement(), "dialogue");
            dlgBoxEl = box;
            box.AddToClassList(who != null ? "dialogue-talk" : "dialogue-narrate");
            var row = Add(box, new VisualElement(), "dialogue-row");
            dlgPortrait = null;
            dlgMood = null;
            if (who != null)
            {
                Portrait(row, who);
                dlgPortrait = row.Q(className: "portrait-frame");
                dlgMood = Add(dlgPortrait, new Label(""), "portrait-mood");
            }
            var bubble = Add(row, new VisualElement(), "dialogue-bubble");
            var head = Add(bubble, new VisualElement(), "dialogue-head");
            dlgSpeaker = Add(head, new Label(who ?? ""), "dialogue-speaker");
            dlgBond = Add(head, new Label(""), "dialogue-bond");
            if (who != null)
            {
                Add(bubble, new VisualElement(), "dialogue-tail");
                var bond = engine.Bond(who);
                if (bond.name != null) dlgBond.text = bond.name;
            }
            else
            {
                Show(head, false);
                if (!string.IsNullOrEmpty(title) && !message) Add(bubble, new Label(engine.Fill(title)), "dialogue-title");
            }
            var scroll = Add(bubble, new ScrollView(ScrollViewMode.Vertical), "dialogue-scroll");
            dlgText = Add(scroll, new Label(""), "dialogue-text");
            dlgMore = Add(bubble, new Label(""), "dialogue-more");
            dlgChoices = Add(scroll, new VisualElement(), "choices");
            box.RegisterCallback<PointerDownEvent>(e => { if (!(e.target is VisualElement ve && ve.GetFirstAncestorOfType<Button>() != null) && !(e.target is Button)) DialogueAdvance(); });

            shownChoices = new List<ChoiceView>();
            dlgPendingChoices = choices;
            if (choices != null) BuildChoices(choices, actions);

            if (pages.Count == 0) { dlgText.style.display = DisplayStyle.None; ShowChoices(); return; }
            PageStyle();
            if (dlgTypedPage >= dlgPage) { dlgText.text = pages[dlgPage]; PageDone(); return; }
            StartTyping();
        }

        /// <summary>Starts typing the current page out: letter by letter, stopping a beat at full stops and commas.</summary>
        void StartTyping()
        {
            dlgText.text = "";
            Show(dlgChoices, false);
            dlgMore.text = "";
            string page = dlgPages[dlgPage];
            dlgTimes = new float[page.Length + 1];
            float t = 0;
            for (int i = 0; i < page.Length; i++)
            {
                dlgTimes[i] = t;
                char c = page[i];
                bool end = i + 1 >= page.Length || page[i + 1] == ' ' || page[i + 1] == '"';
                t += 1f / TypeCharsPerSecond;
                if (end && (c == '.' || c == '!' || c == '?')) t += 0.22f;
                else if (end && (c == ',' || c == ';' || c == ':')) t += 0.08f;
            }
            dlgTimes[page.Length] = t;
            dlgShown = 0;
            dlgStart = Time.realtimeSinceStartup;
            dlgTick = dlgText.schedule.Execute(TypeTick).Every(16);
        }

        /// <summary>The page's look: who's speaking (or which inner voice), and their mood.</summary>
        void PageStyle()
        {
            if (dlgBoxEl == null || dlgPage >= dlgMarks.Count) return;
            string mark = dlgMarks[dlgPage];
            bool voice = mark.StartsWith("voice:");
            string mood = mark.StartsWith("mood:") ? mark.Substring(5) : "";
            dlgBoxEl.EnableInClassList("dialogue-voice", voice);
            if (voice)
            {
                string v = mark.Substring(6);
                dlgSpeaker.text = v.ToUpperInvariant();
                dlgBond.text = "";
                Show(dlgSpeaker.parent, true);
                dlgBoxEl.EnableInClassList("dialogue-rot", v == "The Rot");
            }
            else
            {
                dlgBoxEl.RemoveFromClassList("dialogue-rot");
                dlgSpeaker.text = dlgWho ?? "";
                Show(dlgSpeaker.parent, dlgWho != null);
                var bond = dlgWho != null ? engine.Bond(dlgWho) : (0, null);
                dlgBond.text = bond.name ?? "";
            }
            if (dlgPortrait != null) Show(dlgPortrait, !voice);
            if (dlgMood != null)
            {
                dlgMood.text = mood == "happy" ? "~" : mood == "angry" ? "!" : mood == "sad" ? "..." : mood == "worried" ? "?" : mood == "surprised" ? "!?" : "";
                Show(dlgMood, dlgMood.text != "");
                foreach (var m in new[] { "happy", "angry", "sad", "worried", "surprised" }) dlgBoxEl.EnableInClassList("mood-" + m, m == mood);
            }
        }

        /// <summary>The little sound of each letter: pitched to whoever's talking, lower and slower for an inner voice.</summary>
        void Blip(int letter)
        {
            string mark = dlgPage < dlgMarks.Count ? dlgMarks[dlgPage] : "";
            if (dlgWho == null && !mark.StartsWith("voice:")) return;   // narration types in silence
            if (letter % 2 != 0) return;
            float pitch = mark.StartsWith("voice:") ? 0.62f : engine.data.Npc(dlgWho)?.pitch ?? 1f;
            GameAudio.Blip(pitch);
        }

        void TypeTick()
        {
            if (dlgText == null || dlgText.panel == null) { dlgTick?.Pause(); dlgTick = null; return; }
            string page = dlgPages[dlgPage];
            float el = Time.realtimeSinceStartup - dlgStart;
            int n = 0;
            if (dlgTimes != null && dlgTimes.Length == page.Length + 1) { while (n < page.Length && dlgTimes[n] <= el) n++; }
            else n = Mathf.Min(page.Length, Mathf.FloorToInt(el * TypeCharsPerSecond));
            for (int i = dlgShown; i < n; i++) if (char.IsLetterOrDigit(page[i])) Blip(i);
            dlgShown = n;
            dlgText.text = page.Substring(0, n);
            if (n >= page.Length) FinishTyping();
        }

        void FinishTyping()
        {
            dlgTick?.Pause();
            dlgTick = null;
            if (dlgPage < dlgPages.Count) dlgText.text = dlgPages[dlgPage];
            dlgTypedPage = dlgPage;
            PageDone();
        }

        /// <summary>The page is all there: show the arrow for more, or the choices on the last page.</summary>
        void PageDone()
        {
            bool last = dlgPage >= dlgPages.Count - 1;
            if (!last || dlgDirector) { dlgMore.text = "Space  ▼"; Show(dlgChoices, false); return; }
            if (dlgMessage) { dlgMore.text = "Space  ▼"; return; }
            dlgMore.text = "";
            ShowChoices();
        }

        void ShowChoices()
        {
            Show(dlgChoices, true);
            shownChoices = dlgPendingChoices ?? new List<ChoiceView>();
        }

        /// <summary>Space or a click: finish typing, or turn the page. Returns false if there's nothing left to turn.</summary>
        bool DialogueAdvance()
        {
            if (dlgTick != null) { FinishTyping(); return true; }
            if (dlgPage < dlgPages.Count - 1)
            {
                dlgPage++;
                GameAudio.Play("ui_tick", 0.5f);
                PageStyle();
                StartTyping();
                return true;
            }
            if (dlgMessage) { DismissMessage(); return true; }
            return false;
        }

        void BuildChoices(List<ChoiceView> choices, bool actions)
        {
            for (int i = 0; i < choices.Count; i++)
            {
                var c = choices[i];
                var b = new Button(() => { actionsOpen = false; Pick(c); }) { focusable = false };
                b.AddToClassList("choice");
                b.SetEnabled(c.enabled);
                Add(b, new Label(i < 9 ? (i + 1).ToString() : ""), "choice-num");
                var text = Add(b, new VisualElement(), "choice-text");
                var line = Add(text, new VisualElement(), "choice-line");
                if (!string.IsNullOrEmpty(c.def.tone)) Add(line, new Label(c.def.tone.ToUpperInvariant()), "choice-tone").AddToClassList("tone-" + c.def.tone);
                if (!string.IsNullOrEmpty(c.check)) Add(line, new Label(c.check), "choice-check").AddToClassList(c.checkOk ? "check-ok" : "check-bad");
                Add(line, new Label(engine.Fill(c.def.label)), "choice-label");
                if (c.read) { b.AddToClassList("choice-read"); Add(line, new Label("asked"), "choice-asked"); }
                if (!c.enabled && !string.IsNullOrEmpty(c.reason)) Add(text, new Label(c.reason), "choice-reason");
                else if (!string.IsNullOrEmpty(c.cost)) Add(text, new Label(c.cost), "choice-meta");
                dlgChoices.Add(b);
            }
            if (actions)
            {
                var close = new Button(() => { actionsOpen = false; Render(); }) { focusable = false };
                close.AddToClassList("choice");
                Add(close, new Label("Esc"), "choice-num");
                Add(close, new Label("Keep walking"), "choice-label");
                dlgChoices.Add(close);
            }
        }

        /// <summary>The face of whoever's talking: the top of their walking sprite, blown up in a frame.</summary>
        void Portrait(VisualElement parent, string speaker, int k = 7)
        {
            string sheet = PortraitSheet(speaker);
            var frame = Add(parent, new VisualElement(), "portrait-frame");
            var tex = sheet != null ? Sprite(sheet) : null;
            if (tex == null) { Add(frame, new Label(speaker.Substring(0, 1)), "portrait-letter"); return; }
            var win = Add(frame, new VisualElement { pickingMode = PickingMode.Ignore }, "portrait-window");
            win.style.width = 16 * k;
            win.style.height = 15 * k;
            var img = Add(win, new VisualElement { pickingMode = PickingMode.Ignore }, "walker-sheet");
            img.style.width = 48 * k;
            img.style.height = 96 * k;
            img.style.left = 0;
            img.style.top = -1 * k;
            img.style.backgroundImage = new StyleBackground(tex);
        }

        /// <summary>Which walking sprite belongs to a speaker: whoever in this place opens this scene, or the one named after them.</summary>
        string PortraitSheet(string speaker)
        {
            var o = engine.Objects().FirstOrDefault(x => x.scene == engine.S.scene && !string.IsNullOrEmpty(x.sprite) && x.sprite.StartsWith("npc_"));
            if (o != null) return "Walk/" + o.sprite;
            string first = Key(speaker.Split(' ')[0]);
            if (Sprite("Walk/npc_" + first) != null) return "Walk/npc_" + first;
            var f = engine.data.file.followers.FirstOrDefault(x => x.name == speaker);
            if (f != null && !string.IsNullOrEmpty(f.sprite)) return "Walk/" + f.sprite;
            return null;
        }

        void DismissMessage()
        {
            engine.S.result = "";
            Render();
        }

        static int FacingRow(string f) => f == "up" ? 1 : f == "left" ? 2 : f == "right" ? 3 : 0;

        (VisualElement box, VisualElement sheet) Walker(string sheetPath) => Walker(worldLayer, worldScale, new List<string> { sheetPath });

        /// <summary>A walking figure: a box one frame big, holding the whole sheet (or a stack of layer sheets),
        /// moved about inside it by SetFrame.</summary>
        (VisualElement box, VisualElement sheet) Walker(VisualElement parent, int k, List<string> sheets)
        {
            var box = Add(parent, new VisualElement { pickingMode = PickingMode.Ignore }, "walker");
            box.style.width = 16 * k;
            box.style.height = 24 * k;
            var sheet = Add(box, new VisualElement { pickingMode = PickingMode.Ignore }, "walker-sheet");
            sheet.style.width = 48 * k;
            sheet.style.height = 96 * k;
            bool any = false;
            foreach (var p in sheets)
            {
                var tex = Sprite(p);
                if (tex == null) continue;
                Layer(sheet, tex);
                any = true;
            }
            if (!any) Layer(sheet, Sprite("Walk/body_male_light"));
            return (box, sheet);
        }

        void SetFrame(VisualElement sheet, string facing, int frame) => SetFrame(sheet, facing, frame, worldScale);

        static void SetFrame(VisualElement sheet, string facing, int frame, int k)
        {
            sheet.style.left = -frame * 16 * k;
            sheet.style.top = -FacingRow(facing) * 24 * k;
        }

        void DrawYou()
        {
            (playerBox, playerSheet) = Walker(worldLayer, worldScale, PlayerWalkLayers());
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
            // a tall thing (a tree) stands up out of its tile; a wide one (the wrecked hull) spreads out either side of it
            int extra = tex != null ? Mathf.Max(0, tex.height - Tile) * k : 0;
            int wide = tex != null ? Mathf.Max(0, tex.width - Tile) * k : 0;
            e.style.width = T + wide;
            e.style.height = T + extra;
            e.style.left = o.x * T - wide / 2;
            e.style.top = o.y * T - extra;
        }

        // ------------------------------------------------------------------ people walking about on their own

        // Someone the story moves (Bess heading home once she's patched you up): the game has already put them at the
        // other end, so the screen hides them there and plays them walking the way, tile by tile, then shows them again.
        const float NpcStepSeconds = 0.28f;
        List<Vector2Int> walkPath;
        string walkTo, walkSheet, walkArea;
        float walkStart;
        (VisualElement box, VisualElement sheet) walkSprite;

        void DrawWalk()
        {
            if (walkPath != null && engine.Area.id != walkArea) walkPath = null;
            if (walkPath == null && openMenu == "" && engine.Walks.Count > 0) StartWalk();
            if (walkPath == null) return;
            if (objectSprites.TryGetValue(walkTo, out var there)) there.box.style.display = DisplayStyle.None;
            walkSprite = Walker(walkSheet);
            PlaceWalk();
        }

        void StartWalk()
        {
            var (from, to) = engine.Walks[0];
            engine.Walks.RemoveAt(0);
            var a = engine.Area;
            var f = a.objects.FirstOrDefault(o => o.id == from);
            var t = a.objects.FirstOrDefault(o => o.id == to);
            if (f == null || t == null || string.IsNullOrEmpty(t.sprite)) return;
            var path = NpcPath(new Vector2Int(f.x, f.y), new Vector2Int(t.x, t.y), true) ?? NpcPath(new Vector2Int(f.x, f.y), new Vector2Int(t.x, t.y), false);
            if (path == null || path.Count < 2) return;
            walkPath = path;
            walkTo = to;
            walkSheet = "Walk/" + t.sprite;
            walkArea = a.id;
            walkStart = Time.realtimeSinceStartup;
        }

        /// <summary>The shortest way between two tiles over open ground, round people and things (and round you, if it can).</summary>
        List<Vector2Int> NpcPath(Vector2Int from, Vector2Int to, bool avoidYou)
        {
            var a = engine.Area;
            int w = Engine.Width(a), h = Engine.Height(a);
            var prev = new Dictionary<Vector2Int, Vector2Int>();
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(from);
            prev[from] = from;
            var steps = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                if (c == to) break;
                foreach (var d in steps)
                {
                    var n = c + d;
                    if (n.x < 0 || n.y < 0 || n.x >= w || n.y >= h || prev.ContainsKey(n)) continue;
                    if (n != to)
                    {
                        var tile = engine.data.Tile(a.rows[n.y][n.x]);
                        if (tile == null || tile.solid || engine.ObjectAt(n.x, n.y) != null) continue;
                        if (avoidYou && n.x == engine.S.ax && n.y == engine.S.ay) continue;
                    }
                    prev[n] = c;
                    queue.Enqueue(n);
                }
            }
            if (!prev.ContainsKey(to)) return null;
            var path = new List<Vector2Int>();
            for (var c = to; c != from; c = prev[c]) path.Add(c);
            path.Add(from);
            path.Reverse();
            return path;
        }

        void PlaceWalk()
        {
            if (walkSprite.box == null || walkSprite.box.panel == null) return;
            float t = (Time.realtimeSinceStartup - walkStart) / NpcStepSeconds;
            int i = Mathf.FloorToInt(t);
            if (i >= walkPath.Count - 1)
            {
                walkSprite.box.RemoveFromHierarchy();
                walkSprite = default;
                walkPath = null;
                if (objectSprites.TryGetValue(walkTo, out var there)) there.box.style.display = DisplayStyle.Flex;
                return;
            }
            float frac = t - i;
            Vector2 a = walkPath[i], b = walkPath[i + 1];
            var at = Vector2.Lerp(a, b, frac);
            var dir = walkPath[i + 1] - walkPath[i];
            string facing = dir.x > 0 ? "right" : dir.x < 0 ? "left" : dir.y > 0 ? "down" : "up";
            int k = worldScale, T = Tile * k;
            walkSprite.box.style.left = Mathf.Round(at.x * T);
            walkSprite.box.style.top = Mathf.Round(at.y * T - 8 * k);
            SetFrame(walkSprite.sheet, facing, frac < 0.5f ? 1 + i % 2 : 0);
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
            usePrompt.text = label == null ? "" : "E: " + label;
            usePrompt.style.display = label == null ? DisplayStyle.None : DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ walking

        bool FreeToWalk => engine.Travelling && string.IsNullOrEmpty(engine.S.result) && !actionsOpen && openMenu == "" && !busy;

        void WorldTick()
        {
            if (worldView == null || worldView.panel == null) return;
            MoveWeather();
            if (DirectorActive) { DirectorTick(); return; }
            if (walkPath != null) PlaceWalk();
            var s = engine.S;
            // Out of a fight your breath comes back by itself; redraw the bars a few times a second while it does
            float now = Time.realtimeSinceStartup;
            float dt = breathAt > 0 ? Mathf.Min(0.5f, now - breathAt) : 0f;
            breathAt = now;
            int before = Mathf.CeilToInt(s.stamina);
            if (engine.Breathe(dt, moving && pendingStep != null && pendingStep.label == "Run") && Mathf.CeilToInt(s.stamina) != before
                && openMenu == "" && now - hudDrawnAt > 0.2f && !moving)
            {
                hudDrawnAt = now;
                RenderHud();
            }
            // The clock runs whether you walk or stand still (David, 21 Oct); talking and menus stop it
            if (FreeToWalk && engine.Tick(dt))
            {
                if (!engine.Travelling) { heldDirs.Clear(); moving = false; pendingStep = null; SaveNow(); Render(); return; }
                if (!moving && (Rules.WeatherNow(s, engine.data)?.id ?? "") != drawnWeather) { Render(); return; }
                if (openMenu == "" && now - hudDrawnAt > 0.5f) { hudDrawnAt = now; RenderHud(); }
            }
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
            string dir = NavKey(e.keyCode) switch
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
            bool confirm = e.keyCode == KeyCode.Space || e.keyCode == KeyCode.E || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (confirm && DialoguePaging && DialogueAdvance()) return true;
            if (engine.Travelling && !string.IsNullOrEmpty(s.result))
            {
                if (confirm) { DismissMessage(); return true; }
                return dir != null;
            }
            if (actionsOpen && e.keyCode == KeyCode.Escape) { actionsOpen = false; Render(); return true; }
            if (!engine.Travelling || moving || busy) return false;
            if (e.keyCode == KeyCode.E || e.keyCode == KeyCode.Space)
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
            if ((e.keyCode == KeyCode.R || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && engine.Area.camp && !engine.Area.indoor)
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
            string dir = NavKey(e.keyCode) switch
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
