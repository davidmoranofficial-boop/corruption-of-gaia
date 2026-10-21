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

        static readonly string[] CreationSteps = { "Looks", "Origin", "Background", "Quirks", "Stats", "Name" };

        static readonly string[] StatHelp =
        {
            "Hits harder, and hard work (chopping, hauling, climbing) costs less stamina. Heave things others can't.",
            "More health and stamina, and hunger and thirst drain slower. Grows when you run and take hits.",
            "More critical hits, an easier dodge, and you act first. Climb and slip past what others can't.",
            "More mana, and every stat and skill grows faster. Read old writing and notice what others miss.",
            "Better prices in every shop, and people open up to you. Talk your way through things.",
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
            // one line, both words the same weight, so it reads as the whole name and not just "Corruption"
            var nameRow = Add(logo, new VisualElement(), "title-name-row");
            Add(nameRow, new Label("CORRUPTION"), "title-name");
            Add(nameRow, new Label("of"), "title-name-of");
            Add(nameRow, new Label("GAIA"), "title-name");
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
            Add(foot, new Label("UPDATE: 22 OCT, SLOWER DAYS AND THE ROAD TO EASTMERE"), "title-update");   // bumped with each update so you can see it arrived
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
            cHairStyle ??= "";
            cBeard ??= "None";
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
                case 1: CreateOrigin(left); break;
                case 2: CreateBackground(left); break;
                case 3: CreateQuirks(left); break;
                case 4: CreateStats(left); break;
                default: CreateName(left); break;
            }

            // You, standing in the middle, in the clothes you washed up in
            var stage = Add(body, new VisualElement(), "create-stage");
            var clothes = new List<(string, string)>();
            foreach (var id in Engine.StartingClothes) { var gd = engine.data.Gear(id); if (gd != null) clothes.Add((id, gd.slot)); }
            Doll(stage, 8, cFigure, cSkin, cHair, clothes, CreationHairStyle(), cBeard);
            Add(stage, new VisualElement(), "create-shadow");
            WalkPreview(stage, clothes);

            // Who you're making, so far
            var sum = Add(body, new VisualElement(), "create-panel");
            sum.AddToClassList("create-summary");
            Add(sum, new Label(string.IsNullOrWhiteSpace(cName) ? "Nameless" : cName.Trim()), "create-sum-name");
            string sex = cGender == "" ? "Sex not chosen" : char.ToUpperInvariant(cGender[0]) + cGender.Substring(1);
            string beardNote = BeardKey(cBeard) == null ? "" : "  ·  " + cBeard.ToLowerInvariant();
            Add(sum, new Label(sex + "  ·  " + cBuild + "  ·  " + cSkin + " skin  ·  " + cHair.ToLowerInvariant() + " hair, "
                                + CreationHairStyle().ToLowerInvariant() + beardNote), "create-sum-sub");
            var you = engine.PreviewCharacter(cBackground, alloc, cOrigin, cQuirks);
            var scroll = Add(sum, new ScrollView(ScrollViewMode.Vertical), "create-sum-scroll");
            var orDef = engine.data.Origin(cOrigin);
            var bgDef = engine.data.Background(cBackground);
            Add(scroll, new Label("FROM"), "section-head");
            Add(scroll, new Label(orDef != null ? orDef.name + "  ·  " + orDef.perkName : "Not chosen"), orDef != null ? "create-sum-line" : "create-sum-missing");
            Add(scroll, new Label("BACKGROUND"), "section-head");
            Add(scroll, new Label(bgDef != null ? bgDef.name + "  ·  " + bgDef.perkName : "Not chosen"), bgDef != null ? "create-sum-line" : "create-sum-missing");
            if (cQuirks.Count > 0)
            {
                Add(scroll, new Label("QUIRKS"), "section-head");
                Add(scroll, new Label(string.Join(", ", cQuirks.Select(q => engine.data.Quirk(q)?.name))), "create-sum-line");
            }
            Add(scroll, new Label("STATS"), "section-head");
            for (int i = 0; i < Stat.Count; i++) Sheet(scroll, Stat.Names[i], you.stats[i].ToString());
            int spare = Engine.CreationPoints - alloc.Sum();
            if (spare > 0) Add(scroll, new Label(spare + " point" + (spare == 1 ? "" : "s") + " still to spend"), "create-sum-missing");
            Add(scroll, new Label("WHAT THAT MAKES YOU"), "section-head");
            YouSheet(scroll, you, true);
            Add(scroll, new Label("Everything else you'll earn by doing it: skills grow with use, and there are no classes."), "line-muted");

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
                Add(foot, new Button(FinishCreation) { text = "Create character", focusable = false }, "front-btn-main");
        }

        bool StepDone(int i)
        {
            switch (i)
            {
                case 0: return cGender != "";
                case 1: return cOrigin != null || engine.data.file.origins.Count == 0;
                case 2: return cBackground != null;
                case 3: return true;   // quirks are optional
                case 4: return alloc.Sum() == Engine.CreationPoints;
                default: return !string.IsNullOrWhiteSpace(cName);
            }
        }

        void FinishCreation()
        {
            createError = engine.data.file.origins.Count > 0 && cOrigin == null ? "Pick where you're from."
                : engine.CreateCharacter(cName, cGender, cBuild, cHair, cBackground, (int[])alloc.Clone(), cSkin, cFigure,
                                         CreationHairStyle(), cBeard, cOrigin, cQuirks.ToList());
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

            var pair = Add(p, new VisualElement(), "picker-pair");
            Picker(Add(pair, new VisualElement(), "picker-col"), "SKIN", d.skins, cSkin, v => cSkin = v);
            Picker(Add(pair, new VisualElement(), "picker-col"), "BUILD", d.builds, cBuild, v => cBuild = v);
            pair = Add(p, new VisualElement(), "picker-pair");
            if (d.hairStyles.Count > 0)
                Picker(Add(pair, new VisualElement(), "picker-col"), "HAIR STYLE", d.hairStyles, CreationHairStyle(), v => cHairStyle = v);
            Picker(Add(pair, new VisualElement(), "picker-col"), "HAIR COLOUR", d.hair, cHair, v => cHair = v);
            if (d.beards.Count > 0)
            {
                pair = Add(p, new VisualElement(), "picker-pair");
                Picker(Add(pair, new VisualElement(), "picker-col"), "BEARD", d.beards, cBeard, v => cBeard = v);
                Add(pair, new VisualElement(), "picker-col");
            }
        }

        /// <summary>The hair style being made: until one's picked, short, or long on the feminine figure.</summary>
        string CreationHairStyle()
        {
            if (!string.IsNullOrEmpty(cHairStyle)) return cHairStyle;
            var styles = engine.data.file.hairStyles;
            string want = cFigure == "female" ? "Long" : "Short";
            return styles.Contains(want) ? want : styles.FirstOrDefault() ?? "";
        }

        /// <summary>How you'll look walking about: the world-map sprite, walking on the spot and turning.</summary>
        void WalkPreview(VisualElement stage, List<(string, string)> clothes)
        {
            const int k = 5;
            var frame = Add(stage, new VisualElement { pickingMode = PickingMode.Ignore }, "create-walk");
            Add(frame, new Label("ON THE ROAD"), "create-walk-label");
            var holder = Add(frame, new VisualElement { pickingMode = PickingMode.Ignore }, "create-walk-sprite");
            holder.style.width = 16 * k;
            holder.style.height = 24 * k;
            var (box, sheet) = Walker(holder, k, WalkLayers(cFigure, cSkin, cHair, CreationHairStyle(), cBeard, clothes));
            box.style.left = 0;
            box.style.top = 0;
            string[] facings = { "down", "left", "up", "right" };
            int[] steps = { 1, 0, 2, 0 };
            int tick = 0;
            SetFrame(sheet, "down", 0, k);
            sheet.schedule.Execute(() =>
            {
                tick++;
                SetFrame(sheet, facings[(tick / 12) % 4], steps[tick % 4], k);
            }).Every(170);
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
            Add(p, new Label("Who you were before the boat went down. Each life gives you one perk: your own way of getting by."), "line-muted");
            foreach (var bg in engine.data.file.backgrounds)
            {
                var v = bg;
                var line = Add(p, new Button(() => { cBackground = v.id; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "option");
                line.AddToClassList("option-line");
                if (cBackground == v.id) line.AddToClassList("selected");
                var top = Add(line, new VisualElement(), "skill-tab-top");
                Add(top, new Label(v.name), "option-line-name");
                Add(top, new Label(v.perkName.ToUpperInvariant()), "option-line-bonus");
                Add(line, new Label(v.desc), "option-line-desc");
                Add(line, new Label(v.perkDesc), "perk-desc");
            }
        }

        void CreateOrigin(VisualElement p)
        {
            Add(p, new Label("WHERE YOU GREW UP"), "field-label");
            Add(p, new Label("Where you're from changes how people in Loran see you, and what you can talk to them about."), "line-muted");
            foreach (var o in engine.data.file.origins)
            {
                var v = o;
                var line = Add(p, new Button(() => { cOrigin = v.id; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "option");
                line.AddToClassList("option-line");
                if (cOrigin == v.id) line.AddToClassList("selected");
                var top = Add(line, new VisualElement(), "skill-tab-top");
                Add(top, new Label(v.name), "option-line-name");
                Add(top, new Label(v.perkName.ToUpperInvariant()), "option-line-bonus");
                Add(line, new Label(v.desc), "option-line-desc");
                Add(line, new Label(v.perkDesc), "perk-desc");
            }
        }

        void CreateQuirks(VisualElement p)
        {
            Add(p, new Label("QUIRKS  ·  " + cQuirks.Count + " OF " + Rules.MaxQuirks), "field-label");
            Add(p, new Label("Optional. Each quirk is a trade: something you're good at, and the price you pay for it."), "line-muted");
            var scroll = Add(p, new ScrollView(ScrollViewMode.Vertical), "create-opt-scroll");
            foreach (var q in engine.data.file.quirks)
            {
                var v = q;
                bool on = cQuirks.Contains(v.id);
                var line = Add(scroll, new Button(() =>
                {
                    if (cQuirks.Contains(v.id)) cQuirks.Remove(v.id);
                    else if (cQuirks.Count < Rules.MaxQuirks) cQuirks.Add(v.id);
                    else { GameAudio.Play("ui_deny"); return; }
                    GameAudio.Play("ui_tick");
                    Render();
                }) { focusable = false }, "option");
                line.AddToClassList("option-line");
                line.AddToClassList("quirk-line");
                if (on) line.AddToClassList("selected");
                Add(line, new Label((on ? "✓  " : "") + v.name), "option-line-name");
                var trade = Add(line, new VisualElement(), "quirk-trade");
                Add(trade, new Label("+ " + v.up), "quirk-up");
                Add(trade, new Label("− " + v.down), "quirk-down");
            }
        }

        void CreateStats(VisualElement p)
        {
            int left = Engine.CreationPoints - alloc.Sum();
            var you = engine.PreviewCharacter(cBackground, alloc, cOrigin, cQuirks);
            Add(p, new Label("STATS  ·  " + left + " POINT" + (left == 1 ? "" : "S") + " TO SPEND"), "field-label");
            Add(p, new Label("Everyone starts at 3. These are only where you begin: every stat grows as you use it."), "line-muted");
            for (int i = 0; i < Stat.Count; i++)
            {
                int idx = i;
                var box = Add(p, new VisualElement(), "alloc-box");
                var r = Add(box, new VisualElement(), "alloc-row");
                Add(r, new Label(Stat.Names[i]), "alloc-name");
                int extra = you.stats[i] - (3 + alloc[i]);
                if (extra != 0) Add(r, new Label((extra > 0 ? "+" : "") + extra + " from origin/quirks"), "alloc-bonus");
                var minus = Add(r, new Button(() => { if (alloc[idx] > 0) { alloc[idx]--; GameAudio.Play("ui_tick"); Render(); } }) { text = "−", focusable = false }, "small-btn");
                minus.SetEnabled(alloc[idx] > 0);
                Add(r, new Label(you.stats[i].ToString()), "alloc-value");
                var plus = Add(r, new Button(() => { if (alloc.Sum() < Engine.CreationPoints) { alloc[idx]++; GameAudio.Play("ui_tick"); Render(); } }) { text = "+", focusable = false }, "small-btn");
                plus.SetEnabled(left > 0);
                Add(box, new Label(StatHelp[i]), "alloc-help");
                Add(box, new Label(StatNow(you, i)), "alloc-now");
            }
        }

        /// <summary>What one stat gives you right now, in numbers.</summary>
        string StatNow(GameState s, int stat)
        {
            var d = engine.data;
            switch (stat)
            {
                case Stat.Str: return "Attack " + Mathf.RoundToInt(Rules.Attack(s, d, 0)) + "  ·  hard work " + Signed(Rules.WorkRate(s)) + " stamina";
                case Stat.End: return "Health " + Rules.MaxHp(s) + "  ·  stamina " + Rules.MaxStamina(s, d) + "  ·  hunger and thirst " + Signed(Rules.NeedsRate(s));
                case Stat.Agi: return "Critical hits " + Rules.CritChance(s, d) + "%  ·  speed " + Rules.Speed(s, d);
                case Stat.Int: return "Mana " + Rules.MaxMana(s) + "  ·  learning " + Signed(Rules.LearnRate(s));
                default: return "Shop prices " + Signed(Rules.PriceRate(s, d));
            }
        }

        /// <summary>A rate as a change from normal: "normal", "-12%", "+8%".</summary>
        static string Signed(float rate)
        {
            int pct = Mathf.RoundToInt((rate - 1f) * 100f);
            return pct == 0 ? "normal" : (pct > 0 ? "+" : "−") + Mathf.Abs(pct) + "%";
        }

        /// <summary>The numbers your stats, background, origin and quirks add up to. Used in creation and on the character sheet.</summary>
        void YouSheet(VisualElement parent, GameState s, bool creating)
        {
            var d = engine.data;
            if (creating)
            {
                Sheet(parent, "Health", Rules.MaxHp(s).ToString());
                Sheet(parent, "Stamina", Rules.MaxStamina(s, d).ToString());
                Sheet(parent, "Mana", Rules.MaxMana(s).ToString());
                Sheet(parent, "Attack (fists)", Mathf.RoundToInt(Rules.Attack(s, d, 0)).ToString());
                Sheet(parent, "Critical hits", Rules.CritChance(s, d) + "%");
                Sheet(parent, "Speed", Rules.Speed(s, d).ToString());
            }
            float dealt = Rules.QuirkMul(s, d, q => q.dealt), taken = Rules.QuirkMul(s, d, q => q.taken);
            if (dealt != 1f) Rate(parent, "Your blows", dealt, true);
            if (taken != 1f) Rate(parent, "Harm you take", taken, false);
            Rate(parent, "Shop prices", Rules.PriceRate(s, d), false);
            Rate(parent, "Hunger", Rules.HungerRate(s, d), false);
            Rate(parent, "Thirst", Rules.ThirstRate(s, d), false);
            Rate(parent, "Cold and rain", Rules.WeatherRate(s, d), false);
            Rate(parent, "Hard work", Rules.WorkRate(s), false);
            Rate(parent, "Learning", Rules.LearnRate(s), true);
        }

        /// <summary>One rate on a sheet, green when it's in your favour and red when it isn't.</summary>
        static void Rate(VisualElement parent, string name, float rate, bool upIsGood)
        {
            var r = Add(parent, new VisualElement(), "sheet-row");
            Add(r, new Label(name), "sheet-name");
            string text = Signed(rate);
            var v = Add(r, new Label(text), "sheet-value");
            if (text != "normal") v.AddToClassList((rate > 1f) == upIsGood ? "sheet-good" : "sheet-bad");
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
