using System;
using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The front of the game, laid out like most RPGs. The title screen is a painted scene with the name
    /// and a short menu (arrow keys and Enter, or the mouse); New Game asks how death works before you begin.
    /// Character creation goes in steps along the top (Looks, Background, Stats, Name) with the options on
    /// the left, you standing in the middle, and a summary of who you're making on the right.
    /// </summary>
    public partial class GameUI
    {
        string titleStep = "menu";   // "menu", or "death" for the how-death-works choice before a new game
        int titleSel;                 // the highlighted line of the title menu
        int cStep;                    // which step of character creation

        static readonly string[] CreationSteps = { "Looks", "Background", "Stats", "Name" };

        static readonly string[] StatHelp =
        {
            "Hits harder. Grows when you fight and haul.",
            "More health and stamina. Grows when you run and take hits.",
            "Quicker, more critical hits, an easier dodge. Grows with fighting light.",
            "More mana, stronger spells. Grows when you cast.",
            "People open up to you more. Grows when you talk your way through things.",
        };

        List<(string name, Action act)> TitleMenu()
        {
            var list = new List<(string, Action)>();
            string save = PlayerPrefs.GetString(SaveKey, "");
            if (save != "") list.Add(("Continue", () => ContinueGame(save)));
            list.Add(("New Game", () => { titleStep = "death"; GameAudio.Play("ui_open"); Render(); }));
            list.Add(("Dev mode", () => { engine.DevStart("village"); openMenu = "dev"; Render(); }));
            list.Add(("Quit", () => Application.Quit()));
            return list;
        }

        void ContinueGame(string save)
        {
            try
            {
                engine.LoadState(JsonUtility.FromJson<GameState>(save));
                engine.S.result = "";
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Couldn't load the save: " + ex.Message);
                PlayerPrefs.DeleteKey(SaveKey);
            }
            Render();
        }

        void StartNewGame()
        {
            ResetCreationForm();
            titleStep = "menu";
            engine.NewGame(deathMode);
            AfterAction();
        }

        float frontHeight;

        /// <summary>Makes a front page at least as tall as the window, so its footer sits at the bottom.</summary>
        void FillScreen(VisualElement page)
        {
            float h = screenBox.layout.height;
            frontHeight = float.IsNaN(h) || h < 300 ? 1080f : h;
            page.style.minHeight = frontHeight;
        }

        /// <summary>The painted scene behind the title and creation screens.</summary>
        void FrontBackdrop(string name)
        {
            var tex = Sprite("Backdrops/" + name) ?? Sprite("Backdrops/coast");
            screenBox.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground();
            screenBox.AddToClassList("front");
        }

        void RenderTitle()
        {
            screen.Clear();
            FrontBackdrop("coast_night");
            var page = Add(screen, new VisualElement(), "title-page");
            FillScreen(page);

            var logo = Add(page, new VisualElement(), "title-logo");
            Add(logo, new Label("CORRUPTION"), "title-name");
            Add(logo, new Label("OF  GAIA"), "title-name-sub");
            Add(logo, new VisualElement(), "title-rule");
            Add(logo, new Label("Make your own story in a kingdom going bad."), "title-tag");

            if (titleStep == "death")
            {
                var box = Add(page, new VisualElement(), "title-box");
                Add(box, new Label("HOW DEATH WORKS"), "title-box-head");
                Add(box, new Label("Locked for this playthrough. You can't change it later."), "title-box-sub");
                var row = Add(box, new VisualElement(), "option-row");
                Option(row, "Basic", "If you die, you wake at your last sleep. Anything since then didn't happen.", deathMode == "basic", () => { deathMode = "basic"; Render(); });
                Option(row, "Permadeath", "If you die, your story is over. For hardcore players.", deathMode == "perma", () => { deathMode = "perma"; Render(); });
                var buttons = Add(box, new VisualElement(), "title-box-buttons");
                Add(buttons, new Button(() => { titleStep = "menu"; GameAudio.Play("ui_close"); Render(); }) { text = "Back", focusable = false }, "front-btn");
                Add(buttons, new Button(StartNewGame) { text = "Begin", focusable = false }, "front-btn-main");
            }
            else
            {
                var menu = TitleMenu();
                titleSel = Mathf.Clamp(titleSel, 0, menu.Count - 1);
                var list = Add(page, new VisualElement(), "title-menu");
                for (int i = 0; i < menu.Count; i++)
                {
                    int idx = i;
                    var (name, act) = menu[i];
                    var b = Add(list, new Button(() => { titleSel = idx; act(); }) { focusable = false }, "title-item");
                    b.RegisterCallback<PointerEnterEvent>(_ =>
                    {
                        if (titleSel == idx) return;
                        titleSel = idx;
                        GameAudio.Play("ui_tick");
                        Render();
                    });
                    if (i == titleSel) b.AddToClassList("title-item-on");
                    Add(b, new Label(i == titleSel ? "▸" : ""), "title-item-mark");
                    Add(b, new Label(name.ToUpperInvariant()), "title-item-name");
                }
                Add(page, new Label("↑ ↓  choose      Enter  select"), "title-keys");
            }

            var foot = Add(page, new VisualElement(), "title-foot");
            Add(foot, new Label("UPDATE: 19 OCT, NEW TITLE AND CHARACTER CREATION"), "title-update");   // bumped with each update so you can see it arrived
            Add(foot, new Label("Vertical slice prototype"), "title-update");
        }

        /// <summary>Keys on the title screen: up and down to choose, Enter to pick, Esc to go back.</summary>
        bool TitleKey(KeyDownEvent e)
        {
            if (titleStep == "death")
            {
                if (e.keyCode == KeyCode.Escape) { titleStep = "menu"; Render(); return true; }
                if (e.keyCode == KeyCode.LeftArrow || e.keyCode == KeyCode.RightArrow) { deathMode = deathMode == "basic" ? "perma" : "basic"; GameAudio.Play("ui_tick"); Render(); return true; }
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { StartNewGame(); return true; }
                return false;
            }
            var menu = TitleMenu();
            if (e.keyCode == KeyCode.UpArrow || e.keyCode == KeyCode.W) { titleSel = (titleSel + menu.Count - 1) % menu.Count; GameAudio.Play("ui_tick"); Render(); return true; }
            if (e.keyCode == KeyCode.DownArrow || e.keyCode == KeyCode.S) { titleSel = (titleSel + 1) % menu.Count; GameAudio.Play("ui_tick"); Render(); return true; }
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space)
            {
                GameAudio.Play("ui_confirm");
                menu[Mathf.Clamp(titleSel, 0, menu.Count - 1)].act();
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ character creation

        void RenderCreation()
        {
            var d = engine.data.file;
            cSkin ??= d.skins.Count > 1 ? d.skins[1] : d.skins.FirstOrDefault();
            cBuild ??= d.builds.Count > 2 ? d.builds[2] : d.builds.FirstOrDefault();
            cHair ??= d.hair.FirstOrDefault();
            screen.Clear();
            FrontBackdrop("coast");
            var page = Add(screen, new VisualElement(), "create-page");
            FillScreen(page);

            // Along the top: what this is, and the steps
            var head = Add(page, new VisualElement(), "create-head");
            Add(head, new Label("CREATE YOUR CHARACTER"), "create-title");
            var steps = Add(head, new VisualElement(), "create-steps");
            for (int i = 0; i < CreationSteps.Length; i++)
            {
                int idx = i;
                var b = Add(steps, new Button(() => { cStep = idx; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "create-step");
                if (i == cStep) b.AddToClassList("create-step-on");
                else if (StepDone(i)) b.AddToClassList("create-step-done");
                Add(b, new Label(StepDone(i) && i != cStep ? "✓" : (i + 1).ToString()), "create-step-num");
                Add(b, new Label(CreationSteps[i].ToUpperInvariant()), "create-step-name");
            }

            var body = Add(page, new VisualElement(), "create-body");
            var left = Add(body, new VisualElement(), "create-panel");
            left.AddToClassList("create-options");
            switch (cStep)
            {
                case 0: CreateLooks(left); break;
                case 1: CreateBackground(left); break;
                case 2: CreateStats(left); break;
                default: CreateName(left); break;
            }

            // You, standing in the middle, in the clothes you washed up in
            var stage = Add(body, new VisualElement(), "create-stage");
            var clothes = new List<(string, string)>();
            foreach (var id in Engine.StartingClothes) { var gd = engine.data.Gear(id); if (gd != null) clothes.Add((id, gd.slot)); }
            Doll(stage, 8, cFigure, cSkin, cHair, clothes);
            Add(stage, new VisualElement(), "create-shadow");

            // Who you're making, so far
            var sum = Add(body, new VisualElement(), "create-panel");
            sum.AddToClassList("create-summary");
            Add(sum, new Label(string.IsNullOrWhiteSpace(cName) ? "Nameless" : cName.Trim()), "create-sum-name");
            string sex = cGender == "" ? "Sex not chosen" : char.ToUpperInvariant(cGender[0]) + cGender.Substring(1);
            Add(sum, new Label(sex + "  ·  " + cBuild + "  ·  " + cSkin + " skin  ·  " + cHair + " hair"), "create-sum-sub");
            var bgDef = engine.data.Background(cBackground);
            Add(sum, new Label("BACKGROUND"), "section-head");
            Add(sum, new Label(bgDef != null ? bgDef.name : "Not chosen"), bgDef != null ? "create-sum-line" : "create-sum-missing");
            Add(sum, new Label("STATS"), "section-head");
            for (int i = 0; i < Stat.Count; i++)
            {
                int bonus = bgDef != null && Stat.Parse(bgDef.stat) == i ? 1 : 0;
                Sheet(sum, Stat.Names[i], (3 + alloc[i] + bonus).ToString());
            }
            int spare = Engine.CreationPoints - alloc.Sum();
            if (spare > 0) Add(sum, new Label(spare + " point" + (spare == 1 ? "" : "s") + " still to spend"), "create-sum-missing");
            Add(sum, new Label("Everything else you'll earn by doing it: skills grow with use, and there are no classes."), "line-muted");

            // Along the bottom: back, next, and the problem if there is one
            var foot = Add(page, new VisualElement(), "create-foot");
            Add(foot, new Button(() =>
            {
                if (cStep > 0) cStep--;
                else { engine.GoTo(Engine.Title); titleStep = "menu"; }
                GameAudio.Play("ui_close");
                Render();
            }) { text = cStep > 0 ? "◂  Back" : "◂  Title screen", focusable = false }, "front-btn");
            Add(foot, new Label(createError ?? ""), "create-error");
            if (cStep < CreationSteps.Length - 1)
                Add(foot, new Button(() => { cStep++; createError = null; GameAudio.Play("ui_tick"); Render(); }) { text = "Next  ▸", focusable = false }, "front-btn-main");
            else
                Add(foot, new Button(FinishCreation) { text = "Wake up", focusable = false }, "front-btn-main");
        }

        bool StepDone(int i)
        {
            switch (i)
            {
                case 0: return cGender != "";
                case 1: return cBackground != null;
                case 2: return alloc.Sum() == Engine.CreationPoints;
                default: return !string.IsNullOrWhiteSpace(cName);
            }
        }

        void FinishCreation()
        {
            createError = engine.CreateCharacter(cName, cGender, cBuild, cHair, cBackground, (int[])alloc.Clone(), cSkin, cFigure);
            if (createError == null)
            {
                cStep = 0;
                var next = engine.Choices().FirstOrDefault(c => c.enabled);
                if (next != null) engine.Choose(next.def);
            }
            else
            {
                // take them to the step that needs finishing
                for (int i = 0; i < CreationSteps.Length; i++) if (!StepDone(i)) { cStep = i; break; }
                GameAudio.Play("ui_deny");
            }
            AfterAction();
        }

        void CreateLooks(VisualElement p)
        {
            var d = engine.data.file;
            Add(p, new Label("SEX"), "field-label");
            var sexes = Add(p, new VisualElement(), "option-row");
            Chip(sexes, "Male", cGender == "male", () => { cGender = "male"; cFigure = "male"; Render(); });
            Chip(sexes, "Female", cGender == "female", () => { cGender = "female"; cFigure = "female"; Render(); });
            Chip(sexes, "Other", cGender == "other", () => { cGender = "other"; Render(); });
            Add(p, new Label("Only changes how people speak of you."), "line-muted");

            Add(p, new Label("BODY"), "field-label");
            var figures = Add(p, new VisualElement(), "option-row");
            Chip(figures, "Masculine", cFigure == "male", () => { cFigure = "male"; Render(); });
            Chip(figures, "Feminine", cFigure == "female", () => { cFigure = "female"; Render(); });

            Picker(p, "SKIN", d.skins, cSkin, v => cSkin = v);
            Picker(p, "BUILD", d.builds, cBuild, v => cBuild = v);
            Picker(p, "HAIR", d.hair, cHair, v => cHair = v);
        }

        /// <summary>A ◂ value ▸ picker, the usual way creation screens step through looks.</summary>
        void Picker(VisualElement parent, string label, List<string> values, string now, Action<string> set)
        {
            Add(parent, new Label(label), "field-label");
            var row = Add(parent, new VisualElement(), "picker");
            int at = Mathf.Max(0, values.IndexOf(now));
            Add(row, new Button(() => { set(values[(at + values.Count - 1) % values.Count]); GameAudio.Play("ui_tick"); Render(); }) { text = "◂", focusable = false }, "picker-arrow");
            var mid = Add(row, new VisualElement(), "picker-mid");
            Add(mid, new Label(now ?? ""), "picker-value");
            var dots = Add(mid, new VisualElement(), "picker-dots");
            for (int i = 0; i < values.Count; i++) Add(dots, new VisualElement(), i == at ? "picker-dot-on" : "picker-dot");
            Add(row, new Button(() => { set(values[(at + 1) % values.Count]); GameAudio.Play("ui_tick"); Render(); }) { text = "▸", focusable = false }, "picker-arrow");
        }

        void CreateBackground(VisualElement p)
        {
            Add(p, new Label("THE LIFE YOU'RE LEAVING BEHIND"), "field-label");
            Add(p, new Label("Who you were before the God of Realms called you. Each gives +1 to one stat."), "line-muted");
            foreach (var bg in engine.data.file.backgrounds)
            {
                var v = bg;
                var line = Add(p, new Button(() => { cBackground = v.id; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "option");
                line.AddToClassList("option-line");
                if (cBackground == v.id) line.AddToClassList("selected");
                var top = Add(line, new VisualElement(), "skill-tab-top");
                Add(top, new Label(v.name), "option-line-name");
                Add(top, new Label("+1 " + Stat.Names[Stat.Parse(v.stat)]), "option-line-bonus");
                Add(line, new Label(v.desc), "option-line-desc");
            }
        }

        void CreateStats(VisualElement p)
        {
            int left = Engine.CreationPoints - alloc.Sum();
            Add(p, new Label("STATS  ·  " + left + " POINT" + (left == 1 ? "" : "S") + " TO SPEND"), "field-label");
            Add(p, new Label("Everyone starts at 3. These are only where you begin: every stat grows as you use it."), "line-muted");
            var bgDef = engine.data.Background(cBackground);
            for (int i = 0; i < Stat.Count; i++)
            {
                int idx = i;
                var box = Add(p, new VisualElement(), "alloc-box");
                var r = Add(box, new VisualElement(), "alloc-row");
                Add(r, new Label(Stat.Names[i]), "alloc-name");
                var minus = Add(r, new Button(() => { if (alloc[idx] > 0) { alloc[idx]--; GameAudio.Play("ui_tick"); Render(); } }) { text = "−", focusable = false }, "small-btn");
                minus.SetEnabled(alloc[idx] > 0);
                int bonus = bgDef != null && Stat.Parse(bgDef.stat) == i ? 1 : 0;
                Add(r, new Label((3 + alloc[i] + bonus).ToString()), "alloc-value");
                var plus = Add(r, new Button(() => { if (alloc.Sum() < Engine.CreationPoints) { alloc[idx]++; GameAudio.Play("ui_tick"); Render(); } }) { text = "+", focusable = false }, "small-btn");
                plus.SetEnabled(left > 0);
                Add(box, new Label(StatHelp[i] + (bonus > 0 ? "  (+1 from your background)" : "")), "alloc-help");
            }
        }

        void CreateName(VisualElement p)
        {
            Add(p, new Label("NAME"), "field-label");
            var field = new TextField { maxLength = 20, value = cName };
            field.AddToClassList("name-field");
            field.RegisterValueChangedCallback(e =>
            {
                cName = e.newValue;
                if (summaryName != null) summaryName.text = string.IsNullOrWhiteSpace(cName) ? "Nameless" : cName.Trim();
            });
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { FinishCreation(); e.StopPropagation(); }
            });
            p.Add(field);
            field.schedule.Execute(() => field.Focus());
            Add(p, new Label("What people in Loran will call you."), "line-muted");
            Add(p, new Label("WHEN YOU'RE READY"), "field-label");
            Add(p, new Label("You'll wake on a strange shore, in a fisherman's hut, with the clothes on your back and nothing else."), "line-muted");
        }

        Label summaryName => screen.Q<Label>(className: "create-sum-name");
    }
}
