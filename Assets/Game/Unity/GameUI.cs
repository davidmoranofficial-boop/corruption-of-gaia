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
                var line = Add(left, new Label(fd.name + "  " + Mathf.CeilToInt(Mathf.Max(0, m.hp)) + " / " + Mathf.RoundToInt(Rules.MemberMaxHp(m, fd)) +
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

        string partySel = "";   // the party member picked in the Party menu

        /// <summary>
        /// The party screen, laid out like most RPGs' (David, 20 Oct): everyone in the party down the left with their level
        /// and health, and the one you pick on the right: who they are, level and experience, their own trait and
        /// abilities (locked ones show the level they open at), how they're holding up, what they wear, and what you can give them.
        /// </summary>
        void MenuParty(VisualElement body)
        {
            var s = engine.S;
            var d = engine.data;
            bool free = engine.FreeToAct;
            var row = Add(body, new VisualElement(), "inv");

            var list = Add(row, new VisualElement(), "inv-col");
            list.AddToClassList("skill-list");
            Add(list, new Label("YOUR PARTY  ·  " + s.party.Count + " OF " + Rules.PartyMax), "section-head");
            if (s.party.All(m => m.id != partySel)) partySel = s.party.FirstOrDefault()?.id ?? "";
            foreach (var m in s.party)
            {
                var f = d.Follower(m.id);
                if (f == null) continue;
                string id = m.id;
                var b = Add(list, new Button(() => { partySel = id; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "party-tab");
                if (id == partySel) b.AddToClassList("party-tab-on");
                var face = Add(b, new VisualElement(), "party-tab-face");
                Portrait(face, f.name, 4);
                var info = Add(b, new VisualElement(), "party-tab-info");
                var top = Add(info, new VisualElement(), "skill-tab-top");
                Add(top, new Label(f.name), "skill-tab-name");
                Add(top, new Label("Lv " + Rules.MemberLevel(m)), "skill-tab-level");
                Need(info, "HEALTH", Mathf.Max(0, m.hp), Rules.MemberMaxHp(m, f), "#c8573f");
                var trouble = Rules.Conditions(m, d);
                Add(info, new Label(trouble.Count == 0 ? "Doing well" : string.Join(", ", trouble)), trouble.Count == 0 ? "party-ok" : "party-trouble");
            }
            for (int k = s.party.Count; k < Rules.PartyMax; k++)
            {
                var empty = Add(list, new VisualElement(), "party-tab-empty");
                Add(empty, new Label("Empty"), "line-muted");
            }
            if (s.party.Count == 0)
                Add(list, new Label("Nobody's walking with you. Some people out there are worth finding, and some jobs are better done with company."), "line-muted");
            if (s.fallen.Count > 0) Add(list, new Label("Lost for good: " + string.Join(", ", s.fallen.Select(x => d.Follower(x)?.name ?? x)) + "."), "line-muted");
            // Supplies, since they live off yours
            var foods = s.items.Where(x => d.Item(x.id)?.food == true && x.count > 0).ToList();
            int meals = foods.Sum(x => x.count);
            Add(list, new Label("SUPPLIES"), "section-head").AddToClassList("surv-gap");
            Add(list, new Label(meals + " thing" + (meals == 1 ? "" : "s") + " to eat  ·  " + s.water + " of " + Rules.WaterskinSize + " drinks  ·  " + s.Count("health_potion") + " potions"), "line");
            Add(list, new Label((1 + s.party.Count) + " mouths to feed. Your party eats and drinks from your pack when they sleep; going without lowers their spirits, and at nothing most will leave you."), "line-muted");

            // The one picked
            var det = Add(row, new VisualElement(), "inv-col");
            det.AddToClassList("skill-detail");
            var sel = s.party.FirstOrDefault(m => m.id == partySel);
            var fd = sel != null ? d.Follower(sel.id) : null;
            if (fd == null) { Add(det, new Label("When someone joins you, you'll see them here: their level, their own trait and abilities, and how they're holding up."), "line-muted"); return; }

            var head = Add(det, new VisualElement(), "party-head");
            var fig = Add(head, new VisualElement(), "party-figure");
            var doll = Doll(fig, 4, fd.figure, fd.skin, fd.hair, fd.worn.Select(g => (g, d.Gear(g)?.slot ?? "body")));
            if (sel.hp <= 0) doll.AddToClassList("portrait-down");
            var names = Add(head, new VisualElement(), "party-names");
            Add(names, new Label(fd.name), "char-name");
            if (!string.IsNullOrEmpty(fd.role)) Add(names, new Label(fd.role), "char-sub");
            int lv = Rules.MemberLevel(sel);
            Add(names, new Label("LEVEL " + lv), "char-level");
            var xt = Add(names, new VisualElement(), "xp-track");
            Add(xt, new VisualElement(), "xp-fill").style.width = Length.Percent(Mathf.Clamp01(sel.exp / (float)Rules.ExpToNext(lv)) * 100f);
            Add(names, new Label(sel.exp + " / " + Rules.ExpToNext(lv) + " experience, from the same fights and jobs as you"), "char-sub");
            if (!string.IsNullOrEmpty(fd.until) && engine.All(fd.untilWhen)) Add(names, new Label(fd.until), "party-until");
            if (!string.IsNullOrEmpty(fd.desc)) Add(names, new Label(fd.desc), "det-desc");

            var cols = Add(det, new VisualElement(), "party-cols");
            var c1 = Add(cols, new VisualElement(), "party-col");
            if (!string.IsNullOrEmpty(fd.traitName))
            {
                var perk = Add(c1, new VisualElement(), "perk-card");
                Add(perk, new Label("TRAIT  ·  " + fd.traitName.ToUpperInvariant()), "perk-name");
                Add(perk, new Label(fd.traitDesc), "perk-desc");
            }
            Add(c1, new Label("ABILITIES"), "section-head").AddToClassList("surv-gap");
            Add(c1, new Label("They use these themselves, when the moment's right."), "line-muted");
            foreach (var ab in fd.abilities.OrderBy(a => a.level))
            {
                bool has = lv >= ab.level;
                var node = Add(c1, new VisualElement(), "move-node");
                node.AddToClassList(has ? "move-known" : "move-locked");
                Add(node, new Label(has ? "✓" : "Lv " + ab.level), "move-dot");
                var txt = Add(node, new VisualElement(), "move-text");
                Add(txt, new Label(ab.name), "move-name");
                Add(txt, new Label(ab.desc), "line-muted");
            }

            var c2 = Add(cols, new VisualElement(), "party-col");
            Add(c2, new Label("HOW THEY'RE DOING"), "section-head");
            var needs = Add(c2, new VisualElement(), "surv-needs");
            Need(needs, "HEALTH", Mathf.Max(0, sel.hp), Rules.MemberMaxHp(sel, fd), "#c8573f");
            Need(needs, "SPIRITS", sel.morale, 100, "#d4b45a");
            Need(needs, "HUNGER", sel.hunger, 100, "#d08a4a");
            Need(needs, "THIRST", sel.thirst, 100, "#4f9b97");
            Need(needs, "ENERGY", sel.energy, 100, "#9d8fd0");
            int power = Mathf.RoundToInt(Rules.PartyPower(sel, d) * 100);
            Add(c2, new Label(sel.hp <= 0 ? "Down and dying: bind the grave wound (Survival) within six hours or they die for good. A potion won't bring them round." : Rules.NoFight(sel.body, d) ? "In no state to fight." : "Fights at " + power + "% of their best."), power < 100 || sel.hp <= 0 ? "line-warn" : "line");
            Sheet(c2, "Blows", (fd.atkMin + lv - 1) + " to " + (fd.atkMax + lv - 1) + " damage");
            if (sel.body.afflictions.Count > 0)
            {
                Add(c2, new Label("Hurt: " + string.Join(", ", sel.body.afflictions.Select(a => d.Affliction(a.id)?.name ?? a.id))), "line-warn");
                var go = Add(c2, new Button(() => { openMenu = "survival"; GameAudio.Play("ui_tick"); Render(); }) { text = "Treat their wounds", focusable = false }, "det-btn");
                go.AddToClassList("party-go");
            }
            if (!string.IsNullOrEmpty(fd.quest))
            {
                var q = d.Quest(fd.quest);
                string status = engine.Check("done:" + fd.quest) ? "done" : engine.Check("quest:" + fd.quest) ? "under way" : "not started";
                if (q != null) Add(c2, new Label("Their quest: " + q.name + " (" + status + "). Helping with it lifts their spirits."), "line");
            }

            Add(c2, new Label("WEARING"), "section-head").AddToClassList("surv-gap");
            Add(c2, new Label(string.Join(", ", fd.worn.Select(g => d.Gear(g)?.name ?? g))), "line-muted");

            Add(c2, new Label("GIVE THEM"), "section-head").AddToClassList("surv-gap");
            var btns = Add(c2, new VisualElement(), "party-btns");
            string sid = sel.id;
            if (s.Count("health_potion") > 0)
            {
                var pb = Add(btns, new Button(() => { if (engine.UsePotion(sid, "health_potion")) GameAudio.Play("pickup"); AfterAction(); }) { text = "Health potion (" + s.Count("health_potion") + ")", focusable = false }, "small-btn");
                pb.SetEnabled(free && sel.hp > 0 && sel.hp < Rules.MemberMaxHp(sel, fd));
            }
            foreach (var food in foods)
            {
                var it = d.Item(food.id);
                string fid = food.id;
                var b = Add(btns, new Button(() => { engine.Feed(sid, fid); AfterAction(); }) { text = it.name + " (" + food.count + ")", focusable = false }, "small-btn");
                b.SetEnabled(free);
            }
            var w = Add(btns, new Button(() => { engine.GiveWater(sid); AfterAction(); }) { text = "Water (" + s.water + ")", focusable = false }, "small-btn");
            w.SetEnabled(free && s.water > 0);
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

        static readonly string[] Medicines = { "health_potion", "bandage", "spirits", "yarrow", "willow_bark", "splint", "sewing_kit" };

        /// <summary>
        /// Survival, rebuilt (David, 20 Oct: "very hard to read, it feels like it's all squashed"). The old page stacked
        /// every wound with every step, so it grew too tall and the screen shrank it. Now it's one person and one wound at
        /// a time, the way RimWorld's health tab and Project Zomboid's health panel do it:
        ///   people along the top (you, then your party);
        ///   a body chart on the left, each part tinted by its worst wound, with numbered markers;
        ///   the wounds as a list in the middle, most urgent first;
        ///   the picked wound on the right, with its treatment as numbered steps;
        ///   your medicine, potions and the weather along the bottom.
        /// Up/Down picks a wound, Left/Right picks a person, Enter does the next step.
        /// </summary>
        string survWho = "";      // whose wounds are showing: "" is you, or a party member's id
        string survSel = "";      // the wound picked

        static readonly Dictionary<string, Vector2> PartSpot = new Dictionary<string, Vector2>
        {
            { "head", new Vector2(24, 9) }, { "chest", new Vector2(24, 23) }, { "belly", new Vector2(24, 34) },
            { "left_arm", new Vector2(12, 31) }, { "right_arm", new Vector2(36, 31) },
            { "left_leg", new Vector2(19, 50) }, { "right_leg", new Vector2(28, 50) },
        };
        static readonly string[] UrgencyColour = { "#c8573f", "#c8573f", "#d08a4a", "#e3a64e", "#7fae6a" };
        const int ChartScale = 4;

        /// <summary>Someone's wounds, most urgent first.</summary>
        List<(Affliction a, AfflictionDef def)> SurvWounds(Body b)
        {
            var d = engine.data;
            if (b == null) return new List<(Affliction, AfflictionDef)>();
            return b.afflictions.Select(a => (a, def: d.Affliction(a.id))).Where(x => x.def != null)
                    .OrderBy(x => Rules.Urgency(x.a, x.def)).ThenBy(x => Array.IndexOf(Rules.BodyParts, x.a.part)).ToList();
        }

        /// <summary>The people on the Survival page: you, then your party.</summary>
        List<string> SurvPeople() => new[] { "" }.Concat(engine.S.party.Where(m => engine.data.Follower(m.id) != null).Select(m => m.id)).ToList();

        void SurvFix()
        {
            if (!SurvPeople().Contains(survWho)) survWho = "";
            var list = SurvWounds(engine.BodyOf(survWho));
            if (list.All(x => x.a.id != survSel)) survSel = list.Count > 0 ? list[0].a.id : "";
        }

        /// <summary>Survival keys: Up/Down a wound, Left/Right a person, Enter the next step.</summary>
        bool SurvivalKey(KeyCode k)
        {
            SurvFix();
            if (k == KeyCode.UpArrow || k == KeyCode.DownArrow)
            {
                var list = SurvWounds(engine.BodyOf(survWho));
                if (list.Count == 0) return true;
                int i = Mathf.Max(0, list.FindIndex(x => x.a.id == survSel));
                i = (i + (k == KeyCode.UpArrow ? -1 : 1) + list.Count) % list.Count;
                survSel = list[i].a.id;
                GameAudio.Play("ui_tick");
                Render();
                return true;
            }
            if (k == KeyCode.LeftArrow || k == KeyCode.RightArrow)
            {
                var people = SurvPeople();
                int i = Mathf.Max(0, people.IndexOf(survWho));
                survWho = people[(i + (k == KeyCode.LeftArrow ? -1 : 1) + people.Count) % people.Count];
                survSel = "";
                GameAudio.Play("ui_tick");
                Render();
                return true;
            }
            if (k == KeyCode.Return || k == KeyCode.KeypadEnter)
            {
                string step = survSel == "" ? null : engine.NextStep(survWho, survSel);
                if (step != null) SurvTreat(survWho, survSel, step);
                else GameAudio.Play("ui_tick", 0.5f);
                return true;
            }
            return false;
        }

        void SurvTreat(string who, string aid, string step)
        {
            if (engine.Treat(who, aid, step)) GameAudio.Play(step == "bandage" || step == "splint" || step == "stitch" ? "bandage" : "pickup");
            AfterAction();
        }

        void MenuSurvival(VisualElement body)
        {
            var s = engine.S;
            var d = engine.data;
            bool free = engine.FreeToAct;
            SurvFix();
            var page = Add(body, new VisualElement(), "surv");

            // Who: you, then your party
            var people = Add(page, new VisualElement(), "surv-people");
            foreach (var who in SurvPeople())
            {
                var m = who == "" ? null : engine.Member(who);
                var f = m == null ? null : d.Follower(who);
                var b = who == "" ? s.body : m.body;
                float hp = who == "" ? s.hp : m.hp, max = who == "" ? Rules.MaxHp(s) : Rules.MemberMaxHp(m, f);
                string id = who;
                var tab = Add(people, new Button(() => { if (survWho != id) { survWho = id; survSel = ""; GameAudio.Play("ui_tick"); Render(); } }) { focusable = false }, "surv-person");
                if (who == survWho) tab.AddToClassList("surv-person-on");
                var top = Add(tab, new VisualElement(), "surv-person-top");
                Add(top, new Label(who == "" ? "You" : f.name), "surv-person-name");
                var wounds = SurvWounds(b);
                int worst = wounds.Count == 0 ? 9 : Rules.Urgency(wounds[0].a, wounds[0].def);
                string status = hp <= 0 ? "DOWN" : wounds.Count == 0 ? "No wounds" : worst <= 1 ? "Bleeding" : wounds.Count + (wounds.Count == 1 ? " wound" : " wounds");
                Add(top, new Label(status), hp <= 0 || worst <= 1 ? "surv-person-bad" : wounds.Count == 0 ? "surv-person-ok" : "surv-person-meh");
                Need(tab, "HEALTH", Mathf.Max(0, hp), max, "#c8573f");
            }
            Add(people, new Label("←  →  person"), "surv-keyhint");

            if (s.Has("patch_lesson") && !s.Has("patch_done") && engine.Check("untreated:bite"))
                Add(page, new Label("Bess is watching. Pick the bite, then do the steps in order: clean it (that uses the spirits), then bandage it. Enter does the next step."), "tutorial-hint");

            var main = Add(page, new VisualElement(), "surv-main");
            var mem = survWho == "" ? null : engine.Member(survWho);
            var fd = mem == null ? null : d.Follower(survWho);
            var bodyNow = engine.BodyOf(survWho);
            var list = SurvWounds(bodyNow);
            var picked = list.FirstOrDefault(x => x.a.id == survSel);

            // The body chart
            var left = Add(main, new VisualElement(), "inv-col");
            left.AddToClassList("surv-chart-col");
            Add(left, new Label("BODY"), "section-head");
            var chart = Add(left, new VisualElement(), "surv-chart");
            chart.style.width = 48 * ChartScale;
            chart.style.height = 64 * ChartScale;
            foreach (var part in Rules.BodyParts)
            {
                var here = list.Where(x => x.a.part == part).ToList();
                var tex = Sprite("Ui/chart_" + part);
                if (tex == null) continue;
                var layer = Add(chart, new VisualElement { pickingMode = PickingMode.Ignore }, "doll-layer");
                layer.style.backgroundImage = new StyleBackground(tex);
                string col = here.Count == 0 ? "#9fa9a6" : UrgencyColour[here.Min(x => Rules.Urgency(x.a, x.def))];
                ColorUtility.TryParseHtmlString(col, out var tint);
                layer.style.unityBackgroundImageTintColor = tint;
            }
            // numbered markers, matching the list
            var perPart = new Dictionary<string, int>();
            for (int i = 0; i < list.Count; i++)
            {
                var (a, def) = list[i];
                if (!PartSpot.TryGetValue(a.part ?? "", out var spot)) continue;
                perPart.TryGetValue(a.part, out int n);
                perPart[a.part] = n + 1;
                string aid = a.id;
                var mark = Add(chart, new Button(() => { survSel = aid; GameAudio.Play("ui_tick"); Render(); }) { text = (i + 1).ToString(), focusable = false }, "surv-mark");
                if (aid == survSel) mark.AddToClassList("surv-mark-on");
                ColorUtility.TryParseHtmlString(UrgencyColour[Rules.Urgency(a, def)], out var mc);
                mark.style.backgroundColor = mc;
                mark.style.left = spot.x * ChartScale - 14 + n * 22;
                mark.style.top = spot.y * ChartScale - 14;
            }
            var allOver = list.Where(x => string.IsNullOrEmpty(x.a.part)).ToList();
            if (allOver.Count > 0) Add(left, new Label("All over: " + string.Join(", ", allOver.Select(x => x.def.name))), "surv-allover");
            float pw = mem == null ? Rules.FightPower(s.body, d) : Rules.PartyPower(mem, d);
            int power = Mathf.RoundToInt(pw * 100);
            bool noFight = bodyNow != null && Rules.NoFight(bodyNow, d);
            Add(left, new Label(mem != null && mem.hp <= 0 ? "Down and dying" : noFight ? "Can't fight" : "Fights at " + power + "%"), power < 100 || noFight ? "surv-power-bad" : "surv-power");
            float bleed = list.Where(x => Rules.Bleeding(x.a, x.def)).Sum(x => x.def.bleed);
            if (bleed > 0) Add(left, new Label("Losing " + bleed.ToString("0.#") + " health an hour"), "surv-bleed");
            var scars = bodyNow?.scars ?? new List<Scar>();
            if (scars.Count > 0)
            {
                Add(left, new Label("SCARS"), "section-head").AddToClassList("surv-gap");
                foreach (var sc in scars.Skip(Mathf.Max(0, scars.Count - 3)))
                    Add(left, new Label(sc.name + "  ·  " + Rules.PartName(sc.part)), "surv-scar").tooltip = Cap(sc.name) + " on the " + Rules.PartName(sc.part) + (sc.from != "" ? ", from " + sc.from : "") + ", day " + sc.day + ".";
                if (scars.Count > 3) Add(left, new Label("and " + (scars.Count - 3) + " older"), "line-muted");
            }

            // The wounds
            var mid = Add(main, new VisualElement(), "inv-col");
            mid.AddToClassList("surv-list");
            Add(mid, new Label(list.Count == 0 ? "WOUNDS" : "WOUNDS  ·  " + list.Count), "section-head");
            if (list.Count == 0)
            {
                var ok = Add(mid, new VisualElement(), "surv-none");
                Add(ok, new Label("No wounds."), "surv-none-head");
                Add(ok, new Label("Fights leave bites, cuts and broken bones, and the weather leaves chills. When something happens it shows here, with each step to treat it."), "line-muted");
            }
            for (int i = 0; i < list.Count; i++)
            {
                var (a, def) = list[i];
                string aid = a.id;
                int u = Rules.Urgency(a, def);
                var row = Add(mid, new Button(() => { survSel = aid; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "surv-row");
                if (aid == survSel) row.AddToClassList("surv-row-on");
                var num = Add(row, new Label((i + 1).ToString()), "surv-row-num");
                ColorUtility.TryParseHtmlString(UrgencyColour[u], out var nc);
                num.style.backgroundColor = nc;
                var txt = Add(row, new VisualElement(), "surv-row-text");
                Add(txt, new Label(def.name), "surv-row-name");
                Add(txt, new Label(Cap(Rules.PartName(a.part)) + "  ·  " + WoundState(a, def)), "surv-row-sub");
            }
            if (list.Count > 1) Add(mid, new Label("↑  ↓  pick a wound"), "surv-keyhint");
            if (s.fallen.Count > 0) Add(mid, new Label("Lost: " + string.Join(", ", s.fallen.Select(id => d.Follower(id)?.name ?? id)) + "."), "line-muted");

            // The one picked
            var right = Add(main, new VisualElement(), "inv-col");
            right.AddToClassList("surv-detail");
            if (picked.a == null) SurvHealthy(right, mem, fd);
            else SurvWound(right, picked.a, picked.def, free);

            // Along the bottom: medicine, potions, water, weather
            var bar = Add(page, new VisualElement(), "surv-bar");
            var meds = Add(bar, new VisualElement(), "surv-meds");
            foreach (var id in Medicines)
            {
                var it = d.Item(id);
                if (it == null) continue;
                int n = s.Count(id);
                var cell = Add(meds, new VisualElement(), n > 0 ? "surv-med" : "surv-med-none");
                Icon(cell, id, 2);
                Add(cell, new Label(n.ToString()), "surv-med-count");
                cell.tooltip = it.name + ": " + it.desc;
            }
            var acts = Add(bar, new VisualElement(), "surv-acts");
            int potions = s.Count("health_potion");
            string pwho = survWho;
            bool down = mem != null && mem.hp <= 0;
            float curHp = mem == null ? s.hp : mem.hp, maxHp = mem == null ? Rules.MaxHp(s) : Rules.MemberMaxHp(mem, fd);
            var pot = Add(acts, new Button(() => { if (engine.UsePotion(pwho, "health_potion")) GameAudio.Play("pickup"); AfterAction(); })
                { text = (mem == null ? "Drink a potion" : "Give " + fd.name + " a potion") + "  (" + potions + ")", focusable = false }, "small-btn");
            pot.SetEnabled(free && potions > 0 && !down && curHp < maxHp);
            if (down) pot.tooltip = "A potion won't bring someone round. Bind the grave wound.";
            var drink = Add(acts, new Button(() => { engine.Drink(); AfterAction(); }) { text = "Drink water  (" + s.water + ")", focusable = false }, "small-btn");
            drink.SetEnabled(free && s.water > 0);
            var w = Rules.WeatherNow(s, d);
            if (w != null)
                Add(bar, new Label(w.name + "  ·  " + Mathf.RoundToInt(Rules.AirTemp(s, d)) + "°C" + (Rules.Sheltered(s, d) ? " (under a roof)" : "") + "  ·  feels " + Mathf.RoundToInt(Rules.Felt(s, d, s.body, Rules.Warmth(s, d))) + "°"), "surv-weather");
        }

        /// <summary>A wound's state in a few words: "Bleeding", "Healing: 12 h", "Needs treating".</summary>
        string WoundState(Affliction a, AfflictionDef def)
        {
            if (Rules.Bleeding(a, def)) return def.deadline > 0 ? "Bleeding out: " + Mathf.Max(0, Mathf.CeilToInt(def.deadline - a.hours)) + " h left" : "Bleeding";
            if (def.cure == "warm") return "Get warm";
            if (def.cure == "cool") return "Cool down";
            if (Rules.Treated(a, def) && def.heal > 0) return "Healing: " + HoursLeft(a, def) + " h";
            return "Needs treating";
        }

        int HoursLeft(Affliction a, AfflictionDef def) => Mathf.CeilToInt((def.heal - a.healed) / (1 + Rules.FirstAidPerLevel * engine.SkillLevel("first_aid")));

        void SurvWound(VisualElement p, Affliction a, AfflictionDef def, bool free)
        {
            var d = engine.data;
            bool treated = Rules.Treated(a, def);
            bool bleeding = Rules.Bleeding(a, def);
            var top = Add(p, new VisualElement(), "party-top");
            Add(top, new Label(def.name), "wound-name");
            string chip = WoundState(a, def).ToUpperInvariant();
            Add(top, new Label(chip.StartsWith("HEALING") ? "HEALING" : chip), bleeding ? "wound-chip-bad" : treated ? "wound-chip-good" : "wound-chip-warn");
            Add(p, new Label(Cap(Rules.PartName(a.part)) + (a.from != "" ? "  ·  from " + a.from : "") + "  ·  " + Ago(a.hours)), "surv-where");
            Add(p, new Label(def.desc), "surv-desc");

            var effects = new List<string>();
            if (bleeding && def.bleed > 0) effects.Add("Loses " + def.bleed.ToString("0.#") + " health an hour");
            if (def.drain > 0 && !treated) effects.Add("Tires you out");
            if (def.noRun) effects.Add("No running");
            if (def.noFight) effects.Add("Can't fight");
            else if (def.fight > 0 && def.fight < 1) effects.Add("Fights at " + Mathf.RoundToInt(def.fight * 100) + "%");
            if (def.stamina < 1) effects.Add("Less stamina");
            if (def.fumble > 0) effects.Add("Blows can go astray");
            if (effects.Count > 0)
            {
                var fx = Add(p, new VisualElement(), "surv-effects");
                foreach (var e in effects) Add(fx, new Label(e), "surv-effect");
            }
            if (def.worsen != "" && !treated && (def.worsenUnless == "" || !a.done.Contains(def.worsenUnless)))
                Add(p, new Label("Turns into " + (d.Affliction(def.worsen)?.name ?? def.worsen).ToLowerInvariant() + " in " + Mathf.Max(0, Mathf.CeilToInt(def.worsenAfter - a.hours)) + " h unless you " + (d.Treatment(def.worsenUnless)?.name ?? "treat it").ToLowerInvariant() + "."), "line-warn");
            if (def.cure == "warm") Add(p, new Label("A fire, a bed or a roof warms and dries you, and then it passes."), "line-warn");
            if (def.cure == "cool") Add(p, new Label("Get out of the sun and drink, and it passes."), "line-warn");

            if (treated && def.heal > 0)
            {
                Need(Add(p, new VisualElement(), "surv-heal"), "HEALED", Mathf.Clamp01(a.healed / def.heal) * 100, 100, "#7fae6a");
                Add(p, new Label("All it needs now is time: about " + HoursLeft(a, def) + " more hours." + (def.scar != "" ? " It'll leave a scar." : "")), "line-muted");
                return;
            }
            if (def.treat.Count == 0) return;

            Add(p, new Label("TREATMENT"), "section-head").AddToClassList("surv-gap");
            string next = engine.NextStep(survWho, a.id);
            int n = 0;
            foreach (var step in def.treat)
            {
                var t = d.Treatment(step);
                if (t == null) continue;
                n++;
                bool done = a.done.Contains(step);
                var sr = Add(p, new VisualElement(), done ? "treat-step-done" : "treat-step");
                if (step == next) sr.AddToClassList("treat-step-next");
                Add(sr, new Label(done ? "✓" : n.ToString()), "treat-num");
                var txt = Add(sr, new VisualElement(), "treat-text");
                Add(txt, new Label(t.name), "treat-name");
                if (done) { Add(txt, new Label("Done"), "treat-cost"); continue; }
                var (ok, uses, why) = engine.TreatmentCost(step);
                string cost = step == "rest" ? "Happens when you sleep" : !ok ? Cap(why)
                            : uses.Count == 0 ? (t.minutes > 0 ? t.minutes + " minutes" : t.desc)
                            : "Uses " + string.Join(" and ", uses.Select(u => u == "water" ? "water" : (d.Item(u)?.name ?? u).ToLowerInvariant() + " (" + engine.S.Count(u) + ")")) + (t.minutes > 0 ? "  ·  " + t.minutes + " min" : "");
                Add(txt, new Label(cost), ok || step == "rest" ? "treat-cost" : "treat-cost-missing");
                if (step == "rest") continue;
                string who = survWho, aid = a.id, sid = step;
                var b = Add(sr, new Button(() => SurvTreat(who, aid, sid)) { text = step == next ? "Do it  ·  Enter" : "Do it", focusable = false }, "det-btn");
                b.SetEnabled(free && ok);
            }
        }

        static string Ago(float hours) => hours < 1 ? "just now" : hours < 48 ? Mathf.FloorToInt(hours) + " h ago" : Mathf.FloorToInt(hours / 24) + " days ago";

        /// <summary>Nothing to treat: how they're holding up instead.</summary>
        void SurvHealthy(VisualElement p, PartyMember mem, FollowerDef fd)
        {
            var s = engine.S;
            var d = engine.data;
            Add(p, new Label(mem == null ? "HOW YOU'RE DOING" : "HOW " + fd.name.ToUpperInvariant() + " IS DOING"), "section-head");
            var needs = Add(p, new VisualElement(), "surv-needs");
            if (mem == null)
            {
                Need(needs, "HEALTH", s.hp, Rules.MaxHp(s), "#c8573f");
                Need(needs, "STAMINA", s.stamina, Rules.MaxStamina(s, d), "#7fae6a");
                Need(needs, "HUNGER", s.hunger, 100, "#d08a4a");
                Need(needs, "THIRST", s.thirst, 100, "#4f9b97");
                Need(needs, "ENERGY", s.energy, 100, "#9d8fd0");
            }
            else
            {
                Need(needs, "HEALTH", Mathf.Max(0, mem.hp), Rules.MemberMaxHp(mem, fd), "#c8573f");
                Need(needs, "SPIRITS", mem.morale, 100, "#d4b45a");
                Need(needs, "HUNGER", mem.hunger, 100, "#d08a4a");
                Need(needs, "THIRST", mem.thirst, 100, "#4f9b97");
                Need(needs, "ENERGY", mem.energy, 100, "#9d8fd0");
            }
            Add(p, new Label("Health comes back with sleep or a potion. Stamina comes back by itself when you stop running. Only sleep, food and drink restore energy; sleeping saves your game."), "line-muted");
            var body = mem == null ? s.body : mem.body;
            Add(p, new Label("WEATHER"), "section-head").AddToClassList("surv-gap");
            var sky = Add(p, new VisualElement(), "surv-needs");
            Need(sky, "COLD", body.cold, 100, "#7fa6c8");
            Need(sky, "WET", body.wet, 100, "#4f7f9b");
            if (body.heat > 0) Need(sky, "HEAT", body.heat, 100, "#d08a4a");
            Add(p, new Label("Cold and wet tire you and slow your breath; all the way, hypothermia. A fire or a roof warms and dries you."), "line-muted");
            Add(p, new Label("Carry bandages and spirits, and a sewing kit if you can. Bad wounds leave scars; they're on the body chart for good."), "line-muted");
        }

        static string Cap(string t) => string.IsNullOrEmpty(t) ? t : char.ToUpperInvariant(t[0]) + t.Substring(1);

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
