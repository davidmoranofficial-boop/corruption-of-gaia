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
        VisualElement menuBar, overlay;
        string openMenu = "";

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

            hud = new VisualElement();
            hud.AddToClassList("hud");
            root.Add(hud);

            body = new VisualElement();
            body.AddToClassList("body");
            root.Add(body);

            (mainBox, main) = FitBox("main");
            body.Add(mainBox);
            // The battle screen fills the main area, so draw it again when that area changes size.
            mainBox.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (InBattle && !busy && timing == null && Mathf.Abs(BattleHeight() - battleHeight) > 4f) Render();
                else if (InWorld && !InBattle && !moving && openMenu == ""
                         && (Mathf.Abs(TravelHeight() - worldSize.y) > 4f || Mathf.Abs(TravelWidth() - worldSize.x) > 4f)) Render();
            });

            menuBar = new VisualElement();
            menuBar.AddToClassList("menu-bar");
            root.Add(menuBar);

            overlay = new VisualElement();
            overlay.AddToClassList("overlay");
            overlay.RegisterCallback<PointerDownEvent>(e => { if (e.target == overlay) { openMenu = ""; Render(); } });
            (panelBox, panel) = FitBox("panel");
            overlay.Add(panelBox);
            body.Add(overlay);

            (screenBox, screen) = FitBox("screen");
            root.Add(screenBox);

            toasts = new VisualElement { pickingMode = PickingMode.Ignore };
            toasts.AddToClassList("toasts");
            root.Add(toasts);
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

        static void Refit(VisualElement box, VisualElement content)
        {
            float availW = box.contentRect.width, availH = box.contentRect.height;
            float natural = content.layout.height;   // unscaled height at the current width
            if (availW <= 0 || availH <= 0 || float.IsNaN(natural) || natural <= 0) return;

            float current = content.resolvedStyle.scale.value.x;
            if (current <= 0) current = 1;
            float k = Mathf.Clamp(availH / natural * 0.995f, MinScale, 1f);
            // Content gets wider as it shrinks, so it reflows shorter; only settle on changes that matter.
            if (Mathf.Abs(k - current) < 0.01f && Mathf.Abs(content.layout.width - availW / current) < 1f) return;
            if (k > current && k - current < 0.03f && content.layout.height * current <= availH) return;   // fits already; don't wobble
            content.style.width = availW / k;
            content.style.scale = new StyleScale(new Scale(new Vector2(k, k)));
        }

        // ------------------------------------------------------------------ rendering

        void Render()
        {
            var s = engine.S;
            ShowToasts();

            bool special = s.scene == Engine.Title || s.scene == Engine.Creation;
            bool battle = !special && InBattle && engine.data.Enemy(s.enemy) != null;
            if (!battle) { outro = false; timing = null; battleMenu = ""; }
            bool forge = !special && !battle && s.scene == Engine.ForgeId;
            if (!forge && forgeStage != 3) { forgeStage = -1; stageRunning = false; }
            Show(screenBox, special);
            Show(body, !special);
            Show(hud, s.hud && !special && !battle && !forge);
            bool menusAllowed = s.hud && !special && !battle && !forge && s.scene != Engine.Death && s.scene != Engine.Collapse;
            if (!menusAllowed && !(openMenu == "dev" && !special)) openMenu = "";
            Show(menuBar, menusAllowed);
            Show(overlay, openMenu != "");

            if (s.scene == Engine.Title) RenderTitle();
            else if (s.scene == Engine.Creation) RenderCreation();
            else
            {
                if (battle) RenderBattle();
                else if (forge) RenderForge();
                else
                {
                    RenderHud();
                    if (InWorld) RenderWorld();
                    else { worldView = null; RenderStory(); }
                }
                if (menusAllowed) RenderMenuBar();
                if (openMenu != "") RenderMenu();
            }

            if (s.scene != Engine.Creation) root.schedule.Execute(() => root.Focus());
        }

        static void Show(VisualElement e, bool on) => e.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;

        void RenderHud()
        {
            var s = engine.S;
            hud.Clear();

            var row = Add(hud, new VisualElement(), "hud-row");
            var needs = Add(row, new VisualElement(), "needs");
            Need(needs, "HEALTH", s.hp, Rules.MaxHp(s), "#c8573f");
            Need(needs, "HUNGER", s.hunger, 100, "#d08a4a");
            Need(needs, "THIRST", s.thirst, 100, "#4f9b97");
            Need(needs, "STAMINA", s.stamina, Rules.MaxStamina(s, engine.data), "#7fae6a");
            Need(needs, "ENERGY", s.energy, 100, "#9d8fd0");

            var clock = Add(row, new VisualElement(), "clock-box");
            Add(clock, new Label("Day " + s.Day + "  ·  " + Rules.Clock(s)), "clock");
            Add(clock, new Label(Rules.TimeOfDay(s)), "clock-sub");
            if (engine.IsDev) Add(clock, new Label("DEV GAME · F1"), "clock-sub");
        }

        void Need(VisualElement parent, string name, float value, float max, string colour)
        {
            var n = Add(parent, new VisualElement(), "need");
            if (value / max < 0.2f) n.AddToClassList("need-low");
            var head = Add(n, new VisualElement(), "need-head");
            Add(head, new Label(name), "need-name");
            Add(head, new Label(Mathf.CeilToInt(value) + " / " + Mathf.RoundToInt(max)), "need-value");
            var track = Add(n, new VisualElement(), "need-track");
            var fill = Add(track, new VisualElement(), "need-fill");
            fill.style.width = Length.Percent(Mathf.Clamp01(value / max) * 100f);
            ColorUtility.TryParseHtmlString(colour, out var c);
            fill.style.backgroundColor = c;
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

        static readonly (string id, string name, KeyCode key)[] Menus =
        {
            ("inventory", "Inventory", KeyCode.I),
            ("equipment", "Equipment", KeyCode.E),
            ("journal", "Journal", KeyCode.J),
            ("crafting", "Crafting", KeyCode.C),
            ("world", "World", KeyCode.W),
            ("survival", "Survival", KeyCode.S),
        };

        void RenderMenuBar()
        {
            menuBar.Clear();
            foreach (var m in Menus)
            {
                var id = m.id;
                var b = Add(menuBar, new Button(() => ToggleMenu(id)), "menu-btn");
                if (openMenu == id) b.AddToClassList("menu-btn-open");
                Add(b, new Label(m.name), "menu-btn-name");
                Add(b, new Label(m.key.ToString()), "menu-btn-key");
            }
        }

        void ToggleMenu(string id)
        {
            openMenu = openMenu == id ? "" : id;
            Render();
        }

        void RenderMenu()
        {
            var s = engine.S;
            panel.Clear();

            var head = Add(panel, new VisualElement(), "panel-head");
            var name = openMenu == "dev" ? "Dev mode  (F1)" : Menus.First(m => m.id == openMenu).name;
            Add(head, new Label(name.ToUpperInvariant()), "panel-title");
            Add(head, new Button(() => ToggleMenu(openMenu)) { text = "Close  (Esc)" }, "small-btn");

            var cols = Add(panel, new VisualElement(), "cols");
            var left = Add(cols, new VisualElement(), "col");
            var right = Add(cols, new VisualElement(), "col");

            switch (openMenu)
            {
                case "inventory": MenuInventory(left, right); break;
                case "equipment": MenuEquipment(left, right); break;
                case "journal": MenuJournal(left, right); break;
                case "crafting": MenuCrafting(left, right); break;
                case "world": MenuWorld(left, right); break;
                case "survival": MenuSurvival(left, right); break;
                case "dev": MenuDev(left, right); break;
            }
        }

        void MenuInventory(VisualElement left, VisualElement right)
        {
            var s = engine.S;
            var d = engine.data;
            bool free = engine.FreeToAct;

            var pack = Section(left, "PACK");
            if (s.items.Count == 0) Add(pack, new Label("Your pack is empty."), "line-muted");
            foreach (var it in s.items.ToList())
            {
                var def = d.Item(it.id);
                var r = Add(pack, new VisualElement(), "item-row");
                var text = Add(r, new VisualElement(), "item-text");
                Add(text, new Label(def.name + (it.count > 1 ? "  ×" + it.count : "")), "line");
                if (!string.IsNullOrEmpty(def.desc)) Add(text, new Label(def.desc), "line-muted");
                if (def.food)
                {
                    var b = Add(r, new Button(() => { engine.Eat(it.id); AfterAction(); }) { text = "Eat" }, "small-btn");
                    b.SetEnabled(free);
                }
            }

            var purse = Section(right, "PURSE");
            Add(purse, new Label(s.coins + " copper" + (s.coins == 1 ? "" : "s")), "big-line");

            var water = Section(right, "WATERSKIN");
            var wr = Add(water, new VisualElement(), "item-row");
            Add(wr, new Label(s.water + " of " + Rules.WaterskinSize + " drinks left"), "line");
            var drink = Add(wr, new Button(() => { engine.Drink(); AfterAction(); }) { text = "Drink" }, "small-btn");
            drink.SetEnabled(free && s.water > 0);
            if (s.spells.Contains("draw_water"))
            {
                var b = Add(water, new Button(() => { engine.DrawWater(); AfterAction(); }) { text = "Cast Draw Water to refill it" }, "small-btn");
                b.AddToClassList("wide-btn");
                b.SetEnabled(free && s.energy >= 6);
            }
            else Add(water, new Label("Refill it at a stream or a well."), "line-muted");
        }

        void MenuEquipment(VisualElement left, VisualElement right)
        {
            var s = engine.S;
            var d = engine.data;
            bool free = engine.FreeToAct;

            var worn = Section(left, "WORN");
            var wornRow = Add(worn, new VisualElement(), "worn");
            var dollBox = Add(wornRow, new VisualElement(), "doll-frame");
            PlayerDoll(dollBox, 5);
            var slots = Add(wornRow, new VisualElement(), "worn-slots");
            foreach (var slot in Engine.GearSlots)
            {
                var inSlot = s.gearItems.Where(x => x.equipped && x.slot == slot).ToList();
                for (int n = 0; n < Engine.SlotCapacity(slot); n++)
                {
                    var g = n < inSlot.Count ? inSlot[n] : null;
                    var r = Add(slots, new VisualElement(), "gear-row");
                    if (g != null) Icon(r, g.id, 2);
                    else Add(r, new VisualElement(), "item-icon-empty");
                    Add(r, new Label(Engine.SlotName(slot)), "gear-slot");
                    if (g == null)
                    {
                        var held = slot == "offhand" ? s.Equipped.FirstOrDefault(x => x.twoHanded) : null;
                        Add(r, new Label(held != null ? "Both hands on your " + held.name.ToLowerInvariant() : "Empty"), "gear-empty");
                        continue;
                    }
                    GearText(r, g);
                    var b = Add(r, new Button(() => { engine.Unequip(g.uid); AfterAction(); }) { text = "Take off" }, "small-btn");
                    b.SetEnabled(free);
                }
            }

            var spare = s.gearItems.Where(g => !g.equipped).ToList();
            if (spare.Count > 0)
            {
                var pack = Section(left, "IN YOUR PACK");
                foreach (var g in spare)
                {
                    var r = Add(pack, new VisualElement(), "gear-row");
                    Icon(r, g.id, 2);
                    Add(r, new Label(Engine.SlotName(g.slot) + (g.twoHanded ? " (both hands)" : "")), "gear-slot");
                    GearText(r, g);
                    var b = Add(r, new Button(() => { engine.Equip(g.uid); AfterAction(); }) { text = "Wear" }, "small-btn");
                    b.SetEnabled(free);
                }
            }
            Add(left, new Label("Gear ranks run from F (common) to S (legendary). Above F, each piece has its own traits."), "line-muted");

            var who = Section(right, "YOU");
            Add(who, new Label(s.name), "who");
            var bg = d.Background(s.background);
            string sex = s.gender == "" ? "" : char.ToUpperInvariant(s.gender[0]) + s.gender.Substring(1) + "  ·  ";
            Add(who, new Label(sex + s.build + ", " + s.hair.ToLowerInvariant() + " hair  ·  " + (bg != null ? bg.name : "")), "who-sub");

            var stats = Section(right, "STATS");
            for (int i = 0; i < Stat.Count; i++)
            {
                var r = Add(stats, new VisualElement(), "stat-row");
                Add(r, new Label(Stat.Names[i]), "stat-name");
                Add(r, new Label(s.stats[i].ToString()), "stat-value");
                var t = Add(r, new VisualElement(), "xp-track");
                var f = Add(t, new VisualElement(), "xp-fill");
                f.style.width = Length.Percent(Mathf.Clamp01(s.xp[i] / (s.stats[i] * 3f)) * 100f);
            }
            Add(stats, new Label("Stats grow with practice. The bar shows progress to the next point."), "line-muted");

            var cls = d.Class(s.playerClass);
            var path = Section(right, "CLASS");
            Add(path, new Label(cls != null ? cls.name : "No class yet"), "line");
            Add(path, new Label(cls != null ? cls.desc + " Your class tree is still to come." : "You'll choose one at a class stone, once you've seen what the world needs."), "line-muted");

            var spells = Section(right, "SPELLS");
            if (s.spells.Count == 0) Add(spells, new Label("You don't know any magic yet."), "line-muted");
            foreach (var id in s.spells)
            {
                var def = d.Spell(id);
                Add(spells, new Label(def.name + "  ·  " + def.desc), "line");
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

        void MenuJournal(VisualElement left, VisualElement right)
        {
            var s = engine.S;
            var tasks = Section(left, "WHAT YOU'RE DOING");
            var journal = engine.Journal();
            if (journal.Count == 0) Add(tasks, new Label("Nothing written down yet."), "line-muted");
            foreach (var e in journal) Add(tasks, new Label(e), "journal-entry");

            var notes = Section(right, "THIS PLAYTHROUGH");
            Add(notes, new Label("Death mode: " + (s.deathMode == "perma" ? "Permadeath. If you die, it's over." : "Basic. If you die, you wake at your last sleep.")), "line");
            Add(notes, new Label("Sleeping saves your progress."), "line-muted");
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
            Add(where, new Label("Crafting takes time, and time feeds the corruption."), "line-muted");
        }

        void MenuWorld(VisualElement left, VisualElement right)
        {
            var s = engine.S;

            var cor = Section(left, "THE CORRUPTION");
            var row = Add(cor, new VisualElement(), "corruption");
            var track = Add(row, new VisualElement(), "corruption-track");
            var fill = Add(track, new VisualElement(), "corruption-fill");
            fill.style.width = Length.Percent(s.corruption);
            Add(row, new Label(s.corruption.ToString("0.0") + "%"), "corruption-value");
            float mult = Rules.CorruptionMultiplier(s);
            Add(cor, new Label("Grows by about " + (Rules.CorruptionPerDay * mult).ToString("0.0") + "% a day right now." +
                (mult < 0.99f ? " Your good name is slowing it." : mult > 1.01f ? " Your bad name is speeding it up." : "")), "line");
            Add(cor, new Label("The mark on your wrist from the God of Realms. Time feeds it, and so do your choices. At 100%, the world falls apart."), "line-muted");

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
                Add(rep, new Label("Good standing slows the corruption. Bad standing speeds it up."), "line-muted");
            }

            var inf = Section(right, "INFLUENCE");
            Add(inf, new Label("You haven't left a lasting mark on the world yet."), "line-muted");
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

        void RenderTitle()
        {
            screen.Clear();
            var inner = Add(screen, new VisualElement(), "screen-inner");
            Add(inner, new Label("CORRUPTION OF GAIA"), "game-title");
            Add(inner, new Label("VERTICAL SLICE PROTOTYPE"), "game-sub");

            Add(inner, new Label("HOW DEATH WORKS  ·  LOCKED FOR THIS PLAYTHROUGH"), "field-label");
            var row = Add(inner, new VisualElement(), "option-row");
            Option(row, "Basic", "If you die, you wake at your last sleep. Anything since then didn't happen.", deathMode == "basic", () => { deathMode = "basic"; Render(); });
            Option(row, "Permadeath", "If you die, your story is over. For hardcore players.", deathMode == "perma", () => { deathMode = "perma"; Render(); });

            var buttons = Add(inner, new VisualElement(), "option-row");
            Add(buttons, new Button(() =>
            {
                ResetCreationForm();
                engine.NewGame(deathMode);
                AfterAction();
            }) { text = "New Game" }, "primary");

            string save = PlayerPrefs.GetString(SaveKey, "");
            if (save != "")
            {
                Add(buttons, new Button(() =>
                {
                    try
                    {
                        engine.LoadState(JsonUtility.FromJson<GameState>(save));
                        engine.S.result = "";
                        Render();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("Couldn't load the save: " + ex.Message);
                        PlayerPrefs.DeleteKey(SaveKey);
                        Render();
                    }
                }) { text = "Continue" }, "secondary");
            }

            Add(buttons, new Button(() =>
            {
                engine.DevStart("village");
                openMenu = "dev";
                Render();
            }) { text = "Dev mode" }, "secondary");
        }

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
        }

        void RenderCreation()
        {
            var d = engine.data.file;
            screen.Clear();
            var inner = Add(screen, new VisualElement(), "screen-inner");
            Add(inner, new Label(engine.Current.title), "title");
            foreach (var p in engine.Paragraphs()) Add(inner, new Label(p), "para");

            inner.AddToClassList("screen-wide");
            var cols = Add(inner, new VisualElement(), "cols");
            var leftCol = Add(cols, new VisualElement(), "col");
            var rightCol = Add(cols, new VisualElement(), "col");

            Add(leftCol, new Label("NAME"), "field-label");
            var field = new TextField { maxLength = 20, value = cName };
            field.AddToClassList("name-field");
            field.RegisterValueChangedCallback(e => cName = e.newValue);
            leftCol.Add(field);

            Add(leftCol, new Label("SEX"), "field-label");
            var sexes = Add(leftCol, new VisualElement(), "option-row");
            Chip(sexes, "Male", cGender == "male", () => { cGender = "male"; cFigure = "male"; Render(); });
            Chip(sexes, "Female", cGender == "female", () => { cGender = "female"; cFigure = "female"; Render(); });
            Chip(sexes, "Other", cGender == "other", () => { cGender = "other"; Render(); });

            Add(leftCol, new Label("BODY"), "field-label");
            var figures = Add(leftCol, new VisualElement(), "option-row");
            Chip(figures, "Masculine", cFigure == "male", () => { cFigure = "male"; Render(); });
            Chip(figures, "Feminine", cFigure == "female", () => { cFigure = "female"; Render(); });

            Add(leftCol, new Label("SKIN"), "field-label");
            var skins = Add(leftCol, new VisualElement(), "option-row");
            foreach (var k in d.skins) { var v = k; Chip(skins, v, cSkin == v, () => { cSkin = v; Render(); }); }

            Add(leftCol, new Label("BUILD"), "field-label");
            var builds = Add(leftCol, new VisualElement(), "option-row");
            foreach (var b in d.builds) { var v = b; Chip(builds, v, cBuild == v, () => { cBuild = v; Render(); }); }

            Add(leftCol, new Label("HAIR"), "field-label");
            var hair = Add(leftCol, new VisualElement(), "option-row");
            foreach (var h in d.hair) { var v = h; Chip(hair, v, cHair == v, () => { cHair = v; Render(); }); }

            // How you'll look: washed up in the clothes you had on
            var look = Add(leftCol, new VisualElement(), "create-look");
            var clothes = new List<(string, string)>();
            foreach (var id in Engine.StartingClothes) { var gd = engine.data.Gear(id); if (gd != null) clothes.Add((id, gd.slot)); }
            Doll(look, 5, cFigure, cSkin, cHair, clothes);

            Add(rightCol, new Label("THE LIFE YOU'RE LEAVING BEHIND  ·  +1 TO ONE STAT"), "field-label");
            var bgs = Add(rightCol, new VisualElement(), "option-list");
            foreach (var bg in d.backgrounds)
            {
                var v = bg;
                var line = new Button(() => { cBackground = v.id; Render(); });
                line.AddToClassList("option");
                line.AddToClassList("option-line");
                if (cBackground == v.id) line.AddToClassList("selected");
                Add(line, new Label(v.name), "option-line-name");
                Add(line, new Label(v.desc), "option-line-desc");
                Add(line, new Label("+1 " + Stat.Names[Stat.Parse(v.stat)]), "option-line-bonus");
                bgs.Add(line);
            }

            int left = Engine.CreationPoints - alloc.Sum();
            Add(rightCol, new Label("STATS  ·  " + left + " POINT" + (left == 1 ? "" : "S") + " TO SPEND"), "field-label");
            var bgDef = engine.data.Background(cBackground);
            for (int i = 0; i < Stat.Count; i++)
            {
                int idx = i;
                var r = Add(rightCol, new VisualElement(), "alloc-row");
                Add(r, new Label(Stat.Names[i]), "alloc-name");
                var minus = Add(r, new Button(() => { if (alloc[idx] > 0) { alloc[idx]--; Render(); } }) { text = "−" }, "small-btn");
                minus.SetEnabled(alloc[idx] > 0);
                int bonus = bgDef != null && Stat.Parse(bgDef.stat) == i ? 1 : 0;
                Add(r, new Label((3 + alloc[i] + bonus).ToString()), "alloc-value");
                var plus = Add(r, new Button(() => { if (alloc.Sum() < Engine.CreationPoints) { alloc[idx]++; Render(); } }) { text = "+" }, "small-btn");
                plus.SetEnabled(left > 0);
                if (bonus > 0) Add(r, new Label("includes +1 from your background"), "alloc-bonus");
            }

            var go = Add(inner, new Button(() =>
            {
                createError = engine.CreateCharacter(cName, cGender, cBuild, cHair, cBackground, (int[])alloc.Clone(), cSkin, cFigure);
                if (createError == null)
                {
                    var next = engine.Choices().FirstOrDefault(c => c.enabled);
                    if (next != null) engine.Choose(next.def);
                }
                AfterAction();
            }) { text = "Wake up" }, "primary");

            if (!string.IsNullOrEmpty(createError)) Add(inner, new Label(createError), "error");
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
            if (InBattle) { BattleKey(e); e.StopPropagation(); return; }
            if (engine.S.scene == Engine.ForgeId) { ForgeKey(e); e.StopPropagation(); return; }
            if (InWorld && openMenu == "" && WorldKeyDown(e)) { e.StopPropagation(); return; }
            if (e.keyCode == KeyCode.Escape && openMenu != "") { openMenu = ""; Render(); e.StopPropagation(); return; }
            if (menuBar.resolvedStyle.display == DisplayStyle.Flex)
                foreach (var m in Menus)
                    if (e.keyCode == m.key) { ToggleMenu(m.id); e.StopPropagation(); return; }
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
