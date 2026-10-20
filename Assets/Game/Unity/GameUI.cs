using System;
using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// Draws the game with UI Toolkit, built entirely from code: the HUD, the story,
    /// the choices, the menus (inventory, equipment, journal, crafting, world, survival), plus the New Game and character creation screens.
    /// All the rules live in Loran.Core.Engine; this class only shows them.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public partial class GameUI : MonoBehaviour
    {
        const string SaveKey = "loran.save";

        Engine engine;
        readonly UnityJson json = new UnityJson();

        VisualElement root, hud, body, toasts;
        VisualElement main, screen, panel;               // what gets filled in
        VisualElement mainBox, screenBox, panelBox;     // the fixed areas they're shrunk to fit
        VisualElement overlay, wheel;
        string openMenu = "";        // a menu's id, "wheel" for the menu wheel, or "" for none
        int wheelSel;
        readonly List<VisualElement> wheelItems = new List<VisualElement>();
        Label wheelName, wheelDesc;

        // New Game screen
        string deathMode = "basic";

        // Character creation form
        string cName = "";
        string cGender = "";
        string cBuild, cHair, cBackground, cSkin, cFigure = "male";
        readonly int[] alloc = new int[Stat.Count];
        string createError;

        List<ChoiceView> shownChoices = new List<ChoiceView>();

        void Start()
        {
            LoadContent();
            BuildShell();
            Render();
        }

        // ------------------------------------------------------------------ loading

        void LoadContent()
        {
            var stories = Resources.LoadAll<TextAsset>("Story")
                .OrderBy(a => a.name)
                .Select(a => JsonUtility.FromJson<StoryFile>(a.text))
                .ToList();
            var data = JsonUtility.FromJson<DataFile>(Resources.Load<TextAsset>("Data/data").text);
            engine = new Engine(stories, data, json);

            foreach (var p in engine.Validate()) Debug.LogWarning("[Story] " + p);

            cBuild = data.builds.Count > 1 ? data.builds[1] : "";
            cHair = data.hair.Count > 1 ? data.hair[1] : data.hair.Count > 0 ? data.hair[0] : "";
            cSkin = data.skins.Count > 1 ? data.skins[1] : data.skins.Count > 0 ? data.skins[0] : "";
        }

        // ------------------------------------------------------------------ layout

        void BuildShell()
        {
            var docRoot = GetComponent<UIDocument>().rootVisualElement;
            docRoot.Clear();
            var sheet = Resources.Load<StyleSheet>("UI/Game");
            if (sheet != null) docRoot.styleSheets.Add(sheet);
            else Debug.LogWarning("UI/Game.uss is missing from Resources.");

            if (GetComponent<UIDocument>().panelSettings.themeStyleSheet == null)
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                docRoot.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(font));
            }

            root = new VisualElement { focusable = true };
            root.AddToClassList("root");
            root.style.flexGrow = 1;
            docRoot.Add(root);
            root.RegisterCallback<KeyDownEvent>(OnKey);
            root.RegisterCallback<KeyUpEvent>(e => { if (engine.S.scene == Engine.ForgeId) ForgeKeyUp(e); else WorldKeyUp(e); });

            body = new VisualElement();
            body.AddToClassList("body");
            root.Add(body);

            // The HUD floats over the top of the world: your needs on the left, the clock on the right
            hud = new VisualElement { pickingMode = PickingMode.Ignore };
            hud.AddToClassList("hud");
            root.Add(hud);

            (mainBox, main) = FitBox("main");
            body.Add(mainBox);
            // The battle screen fills the main area, so draw it again when that area changes size.
            mainBox.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (InBattle && !busy && timing == null && Mathf.Abs(BattleHeight() - battleHeight) > 4f) Render();
                else if (InWorld && !InBattle && !moving && openMenu == ""
                         && (Mathf.Abs(TravelHeight() - worldSize.y) > 4f || Mathf.Abs(TravelWidth() - worldSize.x) > 4f)) Render();
            });

            overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlay.RegisterCallback<PointerDownEvent>(e => { if (e.target == overlay) { openMenu = ""; Render(); } });
            (panelBox, panel) = FitBox("panel");
            overlay.Add(panelBox);
            wheel = new VisualElement();
            wheel.AddToClassList("wheel");
            overlay.Add(wheel);
            body.Add(overlay);

            (screenBox, screen) = FitBox("screen");
            root.Add(screenBox);
            // The title and creation pages fill the window, so draw them again when it changes size.
            screenBox.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                var sc = engine.S.scene;
                if ((sc == Engine.Title || sc == Engine.Creation) && Mathf.Abs(screenBox.layout.height - frontHeight) > 4f) Render();
            });

            toasts = new VisualElement { pickingMode = PickingMode.Ignore };
            toasts.AddToClassList("toasts");
            root.Add(toasts);
            ClickSounds();
            StartFx();
        }

        /// <summary>
        /// An area that never scrolls: if what's inside is too tall, it's scaled down until it fits.
        /// </summary>
        static (VisualElement box, VisualElement content) FitBox(string cls)
        {
            var box = new VisualElement();
            box.AddToClassList(cls);
            box.style.overflow = Overflow.Hidden;
            var content = new VisualElement();
            content.AddToClassList(cls + "-content");
            content.style.position = Position.Absolute;
            content.style.left = 0;
            content.style.top = 0;
            content.style.width = Length.Percent(100);
            content.style.transformOrigin = new StyleTransformOrigin(new TransformOrigin(Length.Percent(0), Length.Percent(0), 0));
            box.Add(content);
            box.RegisterCallback<GeometryChangedEvent>(_ => Refit(box, content));
            content.RegisterCallback<GeometryChangedEvent>(_ => Refit(box, content));
            return (box, content);
        }

        const float MinScale = 0.55f;

        /// <summary>How many times a fit box has rescaled lately, so a layout that keeps flipping gets frozen.</summary>
        class FitState { public int changes; public float since; }

        static void Refit(VisualElement box, VisualElement content)
        {
            // Guard against a loop: rescaling changes the layout, which can ask for another rescale.
            // If it flips more than a few times in a second, stop and keep what we have.
            var st = content.userData as FitState ?? (FitState)(content.userData = new FitState { since = Time.realtimeSinceStartup });
            if (Time.realtimeSinceStartup - st.since > 1f) { st.changes = 0; st.since = Time.realtimeSinceStartup; }
            if (st.changes >= 6) return;

            float availW = box.contentRect.width, availH = box.contentRect.height;
            float natural = content.layout.height;   // unscaled height at the current width
            if (availW <= 0 || availH <= 0 || float.IsNaN(natural) || natural <= 0) return;

            float current = content.resolvedStyle.scale.value.x;
            if (current <= 0) current = 1;
            float k = Mathf.Clamp(availH / natural * 0.995f, MinScale, 1f);
            // Content gets wider as it shrinks, so it reflows shorter; only settle on changes that matter.
            if (Mathf.Abs(k - current) < 0.01f && Mathf.Abs(content.layout.width - availW / current) < 1f) return;
            if (k > current && k - current < 0.03f && content.layout.height * current <= availH) return;   // fits already; don't wobble
            st.changes++;
            content.style.width = availW / k;
            content.style.scale = new StyleScale(new Scale(new Vector2(k, k)));
        }

        // ------------------------------------------------------------------ rendering

        void Render()
        {
            var s = engine.S;
            ShowToasts();
            if (!string.IsNullOrEmpty(engine.MenuRequest))
            {
                // the story asked for a menu (Bess showing you how to treat a bite)
                openMenu = engine.MenuRequest;
                engine.MenuRequest = "";
                GameAudio.Play("ui_open");
            }

            bool special = s.scene == Engine.Title || s.scene == Engine.Creation;
            bool battle = !special && InBattle && engine.data.Enemy(s.enemy) != null;
            if (!battle) { outro = false; timing = null; battleMenu = ""; }
            bool forge = !special && !battle && s.scene == Engine.ForgeId;
            if (!forge && forgeStage != 3) { forgeStage = -1; stageRunning = false; }
            Show(screenBox, special);
            Show(body, !special);
            Show(hud, s.hud && !special && !battle && !forge && openMenu == "");
            bool menusAllowed = MenusAllowed;
            if (!menusAllowed && !(openMenu == "dev" && !special)) openMenu = "";
            Show(overlay, openMenu != "");
            Show(panelBox, openMenu != "wheel");
            Show(wheel, openMenu == "wheel");

            if (s.scene == Engine.Title) RenderTitle();
            else if (s.scene == Engine.Creation) RenderCreation();
            else
            {
                if (battle) RenderBattle();
                else if (forge) RenderForge();
                else
                {
                    RenderHud();
                    main.EnableInClassList("main-flush", InWorld);
                    hud.RemoveFromClassList("hud-talking");
                    if (InWorld) RenderWorld();
                    else { worldView = null; RenderStory(); }
                }
                if (openMenu == "wheel") RenderWheel();
                else if (openMenu != "") RenderMenu();
            }

            Sounds();
            if (s.scene != Engine.Creation) root.schedule.Execute(() => root.Focus());
        }

        /// <summary>The menus can be opened while you're out in the world (not in a fight, at the forge, or on the title screens).</summary>
        bool MenusAllowed
        {
            get
            {
                var s = engine.S;
                if (s.scene == Engine.Title || s.scene == Engine.Creation) return false;
                bool battle = InBattle && engine.data.Enemy(s.enemy) != null;
                return s.hud && !battle && s.scene != Engine.ForgeId && s.scene != Engine.Death && s.scene != Engine.Collapse;
            }
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        void RenderHud()
        {
            var s = engine.S;
            hud.Clear();

            // Top left: the quests you've taken, and what's left of each
            var top = Add(hud, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-top");
            var quests = Add(top, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-quests");
            foreach (var q in engine.ActiveQuests)
            {
                var t = Add(quests, new VisualElement { pickingMode = PickingMode.Ignore }, "tracker");
                Add(t, new Label(q.name), "tracker-name");
                var goals = engine.QuestGoals(q);
                foreach (var (text, met, progress) in goals)
                    Add(t, new Label((met ? "√  " : "•  ") + text + (progress != "" ? "  " + progress : "")), met ? "tracker-goal-done" : "tracker-goal");
                if (goals.All(g => g.met)) Add(t, new Label("→  Go back to " + q.giver), "tracker-return");
            }

            // Top right: the clock, the place and the weather
            var clock = Add(top, new VisualElement { pickingMode = PickingMode.Ignore }, "clock-card");
            Add(clock, new Label(Rules.Clock(s)), "clock");
            Add(clock, new Label("Day " + s.Day + "  ·  " + char.ToUpperInvariant(Rules.Season(s)[0]) + Rules.Season(s).Substring(1) + "  ·  " + Rules.TimeOfDay(s)), "clock-sub");
            var area = engine.Area;
            if (area != null) Add(clock, new Label(area.name), "clock-place");
            var weather = Rules.WeatherNow(s, engine.data);
            if (weather != null && area != null)
                Add(clock, new Label(area.indoor ? weather.name + " outside" : weather.name + "  ·  " + Mathf.RoundToInt(Rules.AirTemp(s, engine.data)) + "°C"), "clock-weather");
            if (engine.IsDev) Add(clock, new Label("DEV GAME · F1"), "clock-dev");

            // Bottom left: health and mana, with anything wrong with you (and your party) just above
            var left = Add(hud, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-corner");
            left.AddToClassList("hud-bottom-left");
            foreach (var m in s.party)
            {
                var fd = engine.data.Follower(m.id);
                if (fd == null) continue;
                var trouble = Rules.Conditions(m, engine.data);
                var line = Add(left, new Label(fd.name + "  " + Mathf.CeilToInt(Mathf.Max(0, m.hp)) + " / " + Mathf.RoundToInt(fd.hp) +
                                               (trouble.Count > 0 ? "  ·  " + string.Join(", ", trouble) : "")), "hud-follower");
                if (trouble.Count > 0) line.AddToClassList("hud-follower-trouble");
            }
            var states = Rules.BodyStates(s.body, engine.data);
            if (states.Count > 0)
            {
                var chips = Add(left, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-chips");
                foreach (var st in states)
                    Add(chips, new Label(st), st == "cold" || st == "wet" || st == "hot" ? "hud-chip" : "hud-chip-bad");
            }
            Need(left, "HEALTH", s.hp, Rules.MaxHp(s), "#c8573f").AddToClassList("need-big");
            Need(left, "MANA", s.mana, Rules.MaxMana(s), "#4f86d8").AddToClassList("need-big");

            // Bottom right: hunger and thirst side by side, with stamina (and the day's energy) under them
            var right = Add(hud, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-corner");
            right.AddToClassList("hud-bottom-right");
            var small = Add(right, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-small");
            Need(small, "HUNGER", s.hunger, 100, "#d08a4a");
            Need(small, "THIRST", s.thirst, 100, "#4f9b97");
            Need(right, "STAMINA", s.stamina, Rules.MaxStamina(s, engine.data), "#7fae6a").AddToClassList("need-big");
            Need(right, "ENERGY", s.energy, 100, "#9d8fd0").AddToClassList("need-thin");

            // Along the very bottom: experience to the next level
            var exp = Add(hud, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-exp");
            var expFill = Add(exp, new VisualElement { pickingMode = PickingMode.Ignore }, "hud-exp-fill");
            expFill.style.width = Length.Percent(Mathf.Clamp01(s.exp / (float)Rules.ExpToNext(s.level)) * 100f);
            Add(exp, new Label("LV " + s.level + "     " + s.exp + " / " + Rules.ExpToNext(s.level) + " EXP") { pickingMode = PickingMode.Ignore }, "hud-exp-text");
        }

        VisualElement Need(VisualElement parent, string name, float value, float max, string colour)
        {
            var n = Add(parent, new VisualElement { pickingMode = PickingMode.Ignore }, "need");
            if (value / max < 0.2f) n.AddToClassList("need-low");
            var head = Add(n, new VisualElement { pickingMode = PickingMode.Ignore }, "need-head");
            Add(head, new Label(name), "need-name");
            Add(head, new Label(Mathf.CeilToInt(value) + " / " + Mathf.RoundToInt(max)), "need-value");
            var track = Add(n, new VisualElement { pickingMode = PickingMode.Ignore }, "need-track");
            var fill = Add(track, new VisualElement(), "need-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(value / max) * 100f);
            ColorUtility.TryParseHtmlString(colour, out var c);
            fill.style.backgroundColor = c;
            return n;
        }

        void RenderStory()
        {
            var sc = engine.Current;
            main.Clear();
            var story = Add(main, new VisualElement(), "story");

            if (!string.IsNullOrEmpty(sc.place)) Add(story, new Label(sc.place.ToUpperInvariant()), "place");
            Add(story, new Label(engine.Fill(sc.title)), "title");
            if (!string.IsNullOrEmpty(engine.S.result)) Add(story, new Label(engine.S.result), "result");
            foreach (var p in engine.Paragraphs()) Add(story, new Label(p), "para");

            var list = Add(story, new VisualElement(), "choices");
            shownChoices = engine.Choices();
            for (int i = 0; i < shownChoices.Count; i++)
            {
                var cv = shownChoices[i];
                var b = new Button(() => Pick(cv));
                b.AddToClassList("choice");
                b.SetEnabled(cv.enabled);
                Add(b, new Label(i < 9 ? (i + 1).ToString() : ""), "choice-num");
                var text = Add(b, new VisualElement(), "choice-text");
                Add(text, new Label(engine.Fill(cv.def.label)), "choice-label");
                if (!cv.enabled && !string.IsNullOrEmpty(cv.reason)) Add(text, new Label(cv.reason), "choice-reason");
                else if (!string.IsNullOrEmpty(cv.cost)) Add(text, new Label(cv.cost), "choice-meta");
                list.Add(b);
            }
        }

        // ------------------------------------------------------------------ menus

        /// <summary>The menus, in order round the wheel (clockwise from the top). Each also has its own key.</summary>
        static readonly (string id, string name, KeyCode key, string desc)[] Menus =
        {
            ("inventory", "Inventory", KeyCode.B, "Your gear, your pack, food, water and coin"),
            ("character", "Character", KeyCode.E, "Your level, stats and what you can do in a fight"),
            ("skills", "Skills", KeyCode.K, "Stats, fighting skills and life skills"),
            ("party", "Party", KeyCode.P, "Who travels with you, and how they're holding up"),
            ("journal", "Journal", KeyCode.J, "Quests and what you've learned"),
            ("crafting", "Crafting", KeyCode.C, "Make things from what you carry"),
            ("world", "World", KeyCode.W, "Time, your good name and the sound settings"),
            ("survival", "Survival", KeyCode.S, "Needs, weather, wounds and treatment"),
        };

        const float WheelSize = 560f, WheelRadius = 205f, WheelItem = 124f;

        /// <summary>The menu wheel (I): every menu round a ring. Point at one, or turn it with the arrow keys, and open it.</summary>
        void RenderWheel()
        {
            wheel.Clear();
            wheelItems.Clear();
            Add(wheel, new VisualElement { pickingMode = PickingMode.Ignore }, "wheel-ring");
            int n = Menus.Length;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                float a = i / (float)n * Mathf.PI * 2f - Mathf.PI / 2f;
                var b = Add(wheel, new Button(() => OpenFromWheel(idx)) { focusable = false }, "wheel-item");
                b.style.left = WheelSize / 2f + Mathf.Cos(a) * WheelRadius - WheelItem / 2f;
                b.style.top = WheelSize / 2f + Mathf.Sin(a) * WheelRadius - WheelItem / 2f;
                Add(b, new Label(Menus[i].name) { pickingMode = PickingMode.Ignore }, "wheel-item-name");
                Add(b, new Label(Menus[i].key.ToString()) { pickingMode = PickingMode.Ignore }, "wheel-item-key");
                b.RegisterCallback<PointerEnterEvent>(_ => { if (wheelSel != idx) GameAudio.Play("ui_tick", 0.7f); wheelSel = idx; UpdateWheel(); });
                wheelItems.Add(b);
            }
            var centre = Add(wheel, new VisualElement { pickingMode = PickingMode.Ignore }, "wheel-centre");
            wheelName = Add(centre, new Label(""), "wheel-name");
            wheelDesc = Add(centre, new Label(""), "wheel-desc");
            Add(centre, new Label("Enter  open     Esc  close"), "wheel-help");
            UpdateWheel();
        }

        void UpdateWheel()
        {
            wheelSel = (wheelSel % Menus.Length + Menus.Length) % Menus.Length;
            for (int i = 0; i < wheelItems.Count; i++) wheelItems[i].EnableInClassList("wheel-item-on", i == wheelSel);
            if (wheelName != null) wheelName.text = Menus[wheelSel].name;
            if (wheelDesc != null) wheelDesc.text = Menus[wheelSel].desc;
        }

        void OpenFromWheel(int i)
        {
            GameAudio.Play("ui_confirm", 0.8f);
            wheelSel = i;
            openMenu = Menus[i].id;
            Render();
        }

        void OpenWheel()
        {
            int at = Array.FindIndex(Menus, m => m.id == openMenu);
            if (at >= 0) wheelSel = at;
            heldDirs.Clear();
            GameAudio.Play("ui_open");
            openMenu = "wheel";
            Render();
        }

        /// <summary>Keys while the wheel is up: arrows turn it, Enter/Space opens, a menu's own key or 1-8 opens that one, I or Esc closes.</summary>
        void WheelKey(KeyDownEvent e)
        {
            var k = e.keyCode;
            if (k == KeyCode.Escape || k == KeyCode.I) { GameAudio.Play("ui_close"); openMenu = ""; Render(); return; }
            if (k == KeyCode.Return || k == KeyCode.KeypadEnter || k == KeyCode.Space) { OpenFromWheel(wheelSel); return; }
            if (k == KeyCode.RightArrow || k == KeyCode.DownArrow) { wheelSel++; UpdateWheel(); GameAudio.Play("ui_tick", 0.7f); return; }
            if (k == KeyCode.LeftArrow || k == KeyCode.UpArrow) { wheelSel--; UpdateWheel(); GameAudio.Play("ui_tick", 0.7f); return; }
            int n = k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9 ? k - KeyCode.Alpha1 : k >= KeyCode.Keypad1 && k <= KeyCode.Keypad9 ? k - KeyCode.Keypad1 : -1;
            if (n >= 0 && n < Menus.Length) { OpenFromWheel(n); return; }
            for (int i = 0; i < Menus.Length; i++)
                if (k == Menus[i].key) { OpenFromWheel(i); return; }
        }

        void ToggleMenu(string id)
        {
            GameAudio.Play(openMenu == id ? "ui_close" : "ui_open");
            openMenu = openMenu == id ? "" : id;
            Render();
        }

        void MenuParty(VisualElement left, VisualElement right)
        {
            var s = engine.S;
            var d = engine.data;
            bool free = engine.FreeToAct;

            var who = Section(left, "YOUR PARTY  ·  " + s.party.Count + " OF " + Rules.PartyMax);
            if (s.party.Count == 0) Add(who, new Label("Nobody's walking with you. Some jobs are better done with company, and some people out there are worth finding."), "line-muted");
            var foods = s.items.Where(i => d.Item(i.id)?.food == true && i.count > 0).ToList();
            foreach (var m in s.party)
            {
                var f = d.Follower(m.id);
                if (f == null) continue;
                var card = Add(who, new VisualElement(), "party-card");
                var top = Add(card, new VisualElement(), "party-top");
                Add(top, new Label(f.name), "detail-name");
                var trouble = Rules.Conditions(m, d);
                var state = Add(top, new Label(m.hp <= 0 ? "down" : trouble.Count == 0 ? "doing well" : string.Join(", ", trouble)), "party-state");
                if (trouble.Count > 0 || m.hp <= 0) state.AddToClassList("party-state-bad");
                if (!string.IsNullOrEmpty(f.desc)) Add(card, new Label(f.desc), "line-muted");
                if (!string.IsNullOrEmpty(f.until) && engine.All(f.untilWhen)) Add(card, new Label(f.until), "line");
                Need(card, "HEALTH", Mathf.Max(0, m.hp), f.hp, "#c8573f");
                var small = Add(card, new VisualElement(), "hud-small");
                Need(small, "HUNGER", m.hunger, 100, "#d08a4a");
                Need(small, "THIRST", m.thirst, 100, "#4f9b97");
                var small2 = Add(card, new VisualElement(), "hud-small");
                Need(small2, "ENERGY", m.energy, 100, "#9d8fd0");
                Need(small2, "SPIRITS", m.morale, 100, "#d4b45a");
                int power = Mathf.RoundToInt(Rules.PartyPower(m, d) * 100);
                Add(card, new Label(m.hp <= 0 ? "Down: they can't fight until their wound is bound." : Rules.NoFight(m.body, d) ? "In no state to fight." : "Fights at " + power + "% of their best."), "line-muted");
                if (m.body.afflictions.Count > 0) Add(card, new Label("Treat their wounds under Survival (S)."), "line-muted");
                if (!string.IsNullOrEmpty(f.quest))
                {
                    var q = d.Quest(f.quest);
                    string status = engine.Check("done:" + f.quest) ? "done" : engine.Check("quest:" + f.quest) ? "under way" : "not started";
                    if (q != null) Add(card, new Label("Their quest: " + q.name + " (" + status + ")"), "line");
                }
                var row = Add(card, new VisualElement(), "party-btns");
                string id = m.id;
                foreach (var food in foods)
                {
                    var it = d.Item(food.id);
                    string fid = food.id;
                    var b = Add(row, new Button(() => { engine.Feed(id, fid); AfterAction(); }) { text = "Give " + it.name.ToLowerInvariant() + " (" + food.count + ")", focusable = false }, "small-btn");
                    b.SetEnabled(free);
                }
                var w = Add(row, new Button(() => { engine.GiveWater(id); AfterAction(); }) { text = "Give water (" + s.water + ")", focusable = false }, "small-btn");
                w.SetEnabled(free && s.water > 0);
            }

            var how = Section(right, "LOOKING AFTER A PARTY");
            Add(how, new Label("Your party gets hungry, thirsty and tired just like you, and they live off your supplies. When you sleep, anyone hungry eats from your pack and anyone thirsty drinks from your waterskin. If there's nothing to give them, they go without."), "line-muted");
            Add(how, new Label("Each trouble (hungry, thirsty, exhausted, wounded, low spirits) makes them fight worse. Going without, cold camps and being knocked down lower their spirits; a bed, a meal and helping with their own quest raise them. Let their spirits hit nothing and most will leave you."), "line-muted");
            var sup = Section(right, "SUPPLIES");
            int meals = foods.Sum(i => i.count);
            Add(sup, new Label(meals + " thing" + (meals == 1 ? "" : "s") + " to eat  ·  " + s.water + " of " + Rules.WaterskinSize + " drinks of water"), "line");
            int mouths = 1 + s.party.Count;
            Add(sup, new Label("That's " + mouths + " mouth" + (mouths == 1 ? "" : "s") + " to feed."), "line-muted");
        }

        void SkillRow(VisualElement parent, SkillDef sk)
        {
            int lv = engine.SkillLevel(sk.id);
            var r = Add(parent, new VisualElement(), "stat-row");
            Add(r, new Label(sk.name), "stat-name");
            Add(r, new Label(lv.ToString()), "stat-value");
            var t = Add(r, new VisualElement(), "xp-track");
            Add(t, new VisualElement(), "xp-fill").style.width = Length.Percent(Mathf.Clamp01(engine.SkillProgress(sk.id)) * 100f);
            Add(parent, new Label(sk.desc), "skill-desc");
            foreach (var m in engine.data.file.moves.Where(m => m.skill == sk.id))
            {
                bool known = engine.S.moves.Contains(m.id);
                bool can = engine.CanLearn(m, out var why);
                var row = Add(parent, new VisualElement(), "move-row");
                Add(row, new Label((known ? "√  " : "Lv " + m.level + "  ") + m.name + ": " + m.desc), known ? "skill-move" : can ? "skill-move-ready" : "skill-move-locked");
                if (!known)
                {
                    var id = m.id;
                    if (can) Add(row, new Button(() => { if (engine.LearnMove(id)) GameAudio.Play("skill_up"); AfterAction(); }) { text = "Learn  ·  1 point", focusable = false }, "small-btn");
                    else if (lv >= m.level) Add(row, new Label(why), "move-why");
                }
            }
        }

        void GearText(VisualElement row, GearItem g)
        {
            RankBadge(row, g.rank);
            var text = Add(row, new VisualElement(), "item-text");
            Add(text, new Label(g.name), "line");
            string stats = engine.GearSummary(g);
            if (stats != "") Add(text, new Label(stats), "gear-stats");
            foreach (var t in g.traits)
            {
                var td = engine.data.Trait(t);
                if (td != null) Add(text, new Label(td.name + ": " + td.desc), "gear-trait");
            }
            if (!string.IsNullOrEmpty(g.desc)) Add(text, new Label(g.desc), "line-muted");
        }

        void MenuCrafting(VisualElement left, VisualElement right)
        {
            var s = engine.S;
            var known = Section(left, "WHAT YOU CAN MAKE");
            if (!s.Has("smith_done"))
                Add(known, new Label("You don't know how to make anything yet. Someone in Skelmed with a forge could teach you."), "line-muted");
            else
            {
                Add(known, new Label("Basic forging"), "line");
                Add(known, new Label("Garran taught you to make a knife, a buckler or work gloves. He'll sell you iron and the use of his forge for 3 coppers. Heat it, hammer it and quench it well, and it comes out a rank better."), "line-muted");
            }
            var where = Section(right, "WHERE TO CRAFT");
            Add(where, new Label(s.Has("smith_done") ? "Garran's forge, Skelmed" : "Nowhere yet."), "line");
            Add(where, new Label("Crafting takes time, and time never stops."), "line-muted");
        }

        void MenuWorld(VisualElement left, VisualElement right)
        {
            var s = engine.S;

            // The corruption still grows underneath, but it's not shown: the player only sees what it does to the world (David, 20 Oct)
            var when = Section(left, "TIME");
            Add(when, new Label("Day " + s.Day + "  ·  " + Rules.Clock(s) + "  ·  " + Rules.TimeOfDay(s)), "line");
            Add(when, new Label("Time never stops, whatever you're doing."), "line-muted");

            var rep = Section(right, "REPUTATION");
            if (!s.Has("been:skelmed")) Add(rep, new Label("Nobody here knows you yet."), "line-muted");
            else
            {
                int v = s.Rep("skelmed");
                Add(rep, new Label("Skelmed (local): " + RepWord(v) + "  " + (v > 0 ? "+" : "") + v), "line");
                Add(rep, new Label("Loran (regional): Unknown"), "line");
                Add(rep, new Label("Gaia (global): Unknown"), "line");
                Add(rep, new Label("What people think of you changes how they treat you."), "line-muted");
            }

            var inf = Section(right, "INFLUENCE");
            Add(inf, new Label("You haven't left a lasting mark on the world yet."), "line-muted");

            SoundSettings(right);
        }

        void MenuSurvival(VisualElement left, VisualElement right)
        {
            var s = engine.S;
            bool free = engine.FreeToAct;

            var needs = Section(left, "HOW YOU'RE DOING");
            NeedRow(needs, "Health", s.hp, Rules.MaxHp(s), "#c8573f", "Comes back slowly with sleep. Starving or going thirsty wears it down.");
            NeedRow(needs, "Hunger", s.hunger, 100, "#d08a4a", "Eat to fill it. Empty means you start losing health.");
            NeedRow(needs, "Thirst", s.thirst, 100, "#4f9b97", "Drink to fill it. It drops faster than hunger.");
            NeedRow(needs, "Stamina", s.stamina, Rules.MaxStamina(s, engine.data), "#7fae6a", "Spent running, fighting and working. A short rest brings it back.");
            NeedRow(needs, "Energy", s.energy, 100, "#9d8fd0", "Your ability to function. Only sleep, food and drink restore it. At zero you collapse, and mid-fight that usually means death.");

            var rest = Section(right, "REST");
            var breath = Add(rest, new Button(() => { engine.CatchBreath(); AfterAction(); }) { text = "Catch your breath  (15 min, +stamina)" }, "small-btn");
            breath.AddToClassList("wide-btn");
            breath.SetEnabled(free);
            var drink = Add(rest, new Button(() => { engine.Drink(); AfterAction(); }) { text = "Drink from your waterskin  (" + s.water + " left)" }, "small-btn");
            drink.AddToClassList("wide-btn");
            drink.SetEnabled(free && s.water > 0);
            Add(rest, new Label("To sleep, find somewhere to bed down: a fire, an inn, or rough if you must. Sleeping saves your game."), "line-muted");

            // The weather, and what it's doing to you
            var d = engine.data;
            var sky = Section(right, "WEATHER");
            var w = Rules.WeatherNow(s, d);
            bool inside = Rules.Sheltered(s, d);
            if (w != null)
            {
                Add(sky, new Label(w.name + "  ·  " + Mathf.RoundToInt(Rules.AirTemp(s, d)) + "°C" + (inside ? "  (you're under a roof)" : "")), "line");
                Add(sky, new Label(w.desc), "line-muted");
            }
            float warmth = Rules.Warmth(s, d);
            Add(sky, new Label("Your clothes add " + Mathf.RoundToInt(warmth) + "°. It feels like " + Mathf.RoundToInt(Rules.Felt(s, d, s.body, warmth)) + "° to you."), "line-muted");
            NeedRow(sky, "Cold", s.body.cold, 100, "#7fa6c8", "Below about " + Mathf.RoundToInt(Rules.Comfortable) + "° you get colder. Cold, you tire faster and get your breath back slower. All the way: hypothermia.");
            NeedRow(sky, "Wet", s.body.wet, 100, "#4f7f9b", "Rain soaks you, and wet makes the cold far worse. A fire or a roof dries you.");
            if (s.body.heat > 0 || (w != null && w.temp > 5)) NeedRow(sky, "Heat", s.body.heat, 100, "#d08a4a", "Too hot, you get thirsty fast. All the way: heatstroke. Get out of the sun and drink.");

            // Wounds and ailments: yours, then your party's
            var hurt = Section(left, "WOUNDS AND AILMENTS");
            if (s.Has("patch_lesson") && !s.Has("patch_done") && engine.Check("untreated:bite"))
                Add(hurt, new Label("Bess is watching. Clean the bite first (that uses the spirits), then bandage it. This is where you treat every wound: open Survival with S, or from the menu wheel (I)."), "tutorial-hint");
            bool any = false;
            any |= WoundList(hurt, "", "You", s.body, free);
            foreach (var m in s.party)
            {
                var f = d.Follower(m.id);
                if (f != null) any |= WoundList(hurt, m.id, f.name + (m.hp <= 0 ? " (down)" : ""), m.body, free);
            }
            if (!any) Add(hurt, new Label("Nothing, for now. Fights leave bites, cuts and broken bones; the weather leaves chills. Carry bandages, spirits, and a sewing kit if you can."), "line-muted");
            if (s.fallen.Count > 0)
                Add(hurt, new Label("Lost: " + string.Join(", ", s.fallen.Select(id => d.Follower(id)?.name ?? id)) + "."), "line-muted");
        }

        /// <summary>One person's wounds, each with what it's doing to them and buttons for the treatments still to do.</summary>
        bool WoundList(VisualElement parent, string who, string name, Body body, bool free)
        {
            var d = engine.data;
            if (body.afflictions.Count == 0) return false;
            Add(parent, new Label(name.ToUpperInvariant()), "wound-who");
            foreach (var a in body.afflictions.ToList())
            {
                var def = d.Affliction(a.id);
                if (def == null) continue;
                var card = Add(parent, new VisualElement(), "party-card");
                var top = Add(card, new VisualElement(), "party-top");
                Add(top, new Label(def.name), "detail-name");
                bool treated = Rules.Treated(a, def);
                string state = Rules.Bleeding(a, def) ? (def.deadline > 0 ? "bleeding out: " + Mathf.Max(0, Mathf.CeilToInt(def.deadline - a.hours)) + " h left" : "bleeding")
                             : treated && def.heal > 0 ? "healing  " + Mathf.RoundToInt(Mathf.Clamp01(a.healed / def.heal) * 100) + "%"
                             : def.cure == "warm" ? "get warm" : def.cure == "cool" ? "cool down" : "untreated";
                var st = Add(top, new Label(state), "party-state");
                if (!treated) st.AddToClassList("party-state-bad");
                Add(card, new Label(def.desc), "line-muted");
                if (def.worsen != "" && !treated && (def.worsenUnless == "" || !a.done.Contains(def.worsenUnless)))
                    Add(card, new Label("Turns bad in " + Mathf.Max(0, Mathf.CeilToInt(def.worsenAfter - a.hours)) + " h unless " + (d.Treatment(def.worsenUnless)?.name ?? "treated").ToLowerInvariant() + "."), "line-warn");
                var row = Add(card, new VisualElement(), "party-btns");
                foreach (var step in def.treat)
                {
                    var t = d.Treatment(step);
                    if (t == null) continue;
                    if (a.done.Contains(step)) { Add(row, new Label("√ " + t.name), "wound-done"); continue; }
                    var (ok, uses, why) = engine.TreatmentCost(step);
                    string cost = ok ? (uses.Count == 0 ? "" : "  (" + string.Join(", ", uses.Select(u => u == "water" ? "water" : (d.Item(u)?.name ?? u).ToLowerInvariant())) + ")") : "  (" + why + ")";
                    string aid = a.id, sid = step;
                    var b = Add(row, new Button(() => { if (engine.Treat(who, aid, sid)) GameAudio.Play(sid == "bandage" || sid == "splint" || sid == "stitch" ? "bandage" : "pickup"); AfterAction(); }) { text = t.name + cost, focusable = false }, "small-btn");
                    b.SetEnabled(free && ok);
                }
            }
            return true;
        }

        void NeedRow(VisualElement parent, string name, float value, float max, string colour, string help)
        {
            var row = Add(parent, new VisualElement(), "need-row");
            Need(row, name.ToUpperInvariant(), value, max, colour);
            Add(row, new Label(help), "line-muted");
        }

        static string RepWord(int v)
        {
            if (v >= 40) return "Trusted";
            if (v >= 10) return "Liked";
            if (v > -10) return "A stranger";
            if (v > -40) return "Disliked";
            return "Despised";
        }

        static VisualElement Section(VisualElement parent, string head)
        {
            var sec = Add(parent, new VisualElement(), "section");
            Add(sec, new Label(head), "section-head");
            return sec;
        }

        // ------------------------------------------------------------------ New Game screen

        void Option(VisualElement parent, string name, string desc, bool selected, Action onClick)
        {
            var b = new Button(onClick);
            b.AddToClassList("option");
            if (selected) b.AddToClassList("selected");
            Add(b, new Label(name), "option-name");
            if (!string.IsNullOrEmpty(desc)) Add(b, new Label(desc), "option-desc");
            parent.Add(b);
        }

        void Chip(VisualElement parent, string name, bool selected, Action onClick)
        {
            var b = new Button(onClick) { text = name };
            b.AddToClassList("option");
            b.AddToClassList("option-chip");
            if (selected) b.AddToClassList("selected");
            parent.Add(b);
        }

        // ------------------------------------------------------------------ character creation

        void ResetCreationForm()
        {
            cName = "";
            cGender = "";
            cBackground = null;
            Array.Clear(alloc, 0, alloc.Length);
            createError = null;
            cStep = 0;
        }

        // ------------------------------------------------------------------ input

        void Pick(ChoiceView cv)
        {
            if (!cv.enabled || busy) return;
            if (moving) return;
            engine.Choose(cv.def);
            AfterAction();
        }

        void OnKey(KeyDownEvent e)
        {
            if (engine.S.scene == Engine.Creation || engine.S.scene == Engine.Title) return;
            if (e.keyCode == KeyCode.F1 || e.keyCode == KeyCode.BackQuote)
            {
                openMenu = openMenu == "dev" ? "" : "dev";
                Render();
                e.StopPropagation();
                return;
            }
            if (openMenu == "dev")
            {
                if (e.keyCode == KeyCode.Escape) { openMenu = ""; Render(); }
                e.StopPropagation();
                return;
            }
            if (engine.S.scene == Engine.Title && TitleKey(e)) { e.StopPropagation(); return; }
            if (InBattle) { BattleKey(e); e.StopPropagation(); return; }
            if (engine.S.scene == Engine.ForgeId) { ForgeKey(e); e.StopPropagation(); return; }
            if (InWorld && openMenu == "" && WorldKeyDown(e)) { e.StopPropagation(); return; }
            if (openMenu == "wheel") { WheelKey(e); e.StopPropagation(); return; }
            if (e.keyCode == KeyCode.Escape && openMenu != "") { GameAudio.Play("ui_close"); openMenu = ""; Render(); e.StopPropagation(); return; }
            if (openMenu != "" && MenuKey(e)) { e.StopPropagation(); return; }
            if (MenusAllowed)
            {
                if (e.keyCode == KeyCode.I) { OpenWheel(); e.StopPropagation(); return; }
                foreach (var m in Menus)
                    if (e.keyCode == m.key) { ToggleMenu(m.id); e.StopPropagation(); return; }
            }
            if (openMenu != "") return;
            int n = e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9 ? e.keyCode - KeyCode.Alpha1
                  : e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad9 ? e.keyCode - KeyCode.Keypad1
                  : -1;
            if (n >= 0 && n < shownChoices.Count)
            {
                Pick(shownChoices[n]);
                e.StopPropagation();
            }
        }

        void AfterAction()
        {
            SaveNow();
            Render();
        }

        void SaveNow()
        {
            var s = engine.S;
            if (engine.IsDev) return;   // dev games never touch the real save
            if (s.hud && s.scene != Engine.Death) PlayerPrefs.SetString(SaveKey, json.Write(s));
            if (s.scene == Engine.Title || (s.scene == Engine.Death && s.deathMode == "perma")) PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
        }

        void ShowToasts()
        {
            var list = engine.S.Toasts;
            foreach (var t in list)
            {
                ToastSound(t);
                if (t.StartsWith("Level up")) root.schedule.Execute(LevelUpEffect).StartingIn(80);
                var l = Add(toasts, new Label(t), "toast");
                l.schedule.Execute(() => l.RemoveFromHierarchy()).StartingIn(4500);
            }
            list.Clear();
        }

        static T Add<T>(VisualElement parent, T child, string cls) where T : VisualElement
        {
            child.AddToClassList(cls);
            parent.Add(child);
            return child;
        }
    }
}
