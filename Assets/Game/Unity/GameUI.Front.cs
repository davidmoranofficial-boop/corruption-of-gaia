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
            Add(foot, new Label("UPDATE: 25 OCT, NO TIMING BAR"), "title-update");   // bumped with each update so you can see it arrived
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

        /// <summary>The question each creation step asks, and one line on what it's for.</summary>
        static readonly string[][] StepAsks =
        {
            new[] { "How do you look?", "Only looks. None of this changes your numbers." },
            new[] { "Where did you grow up?", "Changes how people in Loran see you, and what you can talk to them about." },
            new[] { "What did you do before?", "The life you're leaving behind. Each one gives you a perk." },
            new[] { "Any quirks?", "Optional. Pick up to two. Each one is a trade: a gain and a price." },
            new[] { "Where are you strongest?", "Everyone starts at 3. Every stat still grows as you use it." },
            new[] { "What's your name?", "What people in Loran will call you." },
        };

        VisualElement infoBox;    // the card on the right, refilled when you point at an option

        void RenderCreation()
        {
            var d = engine.data.file;
            cSkin ??= d.skins.Count > 1 ? d.skins[1] : d.skins.FirstOrDefault();
            cBuild ??= d.builds.Count > 2 ? d.builds[2] : d.builds.FirstOrDefault();
            cHair ??= d.hair.FirstOrDefault();
            cHairStyle ??= "";
            cBeard ??= "None";
            screen.Clear();
            FrontBackdrop("creation");
            var page = Add(screen, new VisualElement(), "create-page");
            FillScreen(page);

            // Along the top: what this is, and the steps
            var head = Add(page, new VisualElement(), "create-head");
            Add(head, new Label("CREATE YOUR CHARACTER"), "create-title");
            var steps = Add(head, new VisualElement(), "create-steps");
            for (int i = 0; i < CreationSteps.Length; i++)
            {
                int idx = i;
                var b = Add(steps, new Button(() => { cStep = idx; createError = null; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "create-step");
                if (i == cStep) b.AddToClassList("create-step-on");
                else if (StepDone(i)) b.AddToClassList("create-step-done");
                Add(b, new Label(StepDone(i) && i != cStep ? "✓" : (i + 1).ToString()), "create-step-num");
                Add(b, new Label(CreationSteps[i].ToUpperInvariant()), "create-step-name");
            }

            var body = Add(page, new VisualElement(), "create-body");

            // Left: this step's question and its choices, nothing else
            var left = Add(body, new VisualElement(), "create-panel");
            left.AddToClassList("create-options");
            Add(left, new Label(StepAsks[cStep][0]), "create-ask");
            Add(left, new Label(StepAsks[cStep][1]), "create-ask-sub");
            switch (cStep)
            {
                case 0: CreateLooks(left); break;
                case 1: CreatePick(left, d.origins.Select(o => (o.id, o.name, o.perkName)), cOrigin, id => cOrigin = id); break;
                case 2: CreatePick(left, d.backgrounds.Select(b => (b.id, b.name, b.perkName)), cBackground, id => cBackground = id); break;
                case 3: CreateQuirks(left); break;
                case 4: CreateStats(left); break;
                default: CreateName(left); break;
            }

            // Middle: you, standing in a pool of light, in the clothes you washed up in
            var stage = Add(body, new VisualElement(), "create-stage");
            var clothes = new List<(string, string)>();
            foreach (var id in Engine.StartingClothes) { var gd = engine.data.Gear(id); if (gd != null) clothes.Add((id, gd.slot)); }
            var stand = Add(stage, new VisualElement { pickingMode = PickingMode.Ignore }, "create-stand");
            Doll(stand, 9, cFigure, cSkin, cHair, clothes, CreationHairStyle(), cBeard);
            Add(stand, new VisualElement(), "create-shadow");
            Add(stand, new Label(string.IsNullOrWhiteSpace(cName) ? "" : cName.Trim()), "create-stand-name");
            WalkPreview(stage, clothes);

            // Right: about whatever you're pointing at, or what you've picked
            infoBox = Add(body, new VisualElement(), "create-panel");
            infoBox.AddToClassList("create-info");
            FillInfo(null);

            // Along the bottom: back, next, and the problem if there is one
            var foot = Add(page, new VisualElement(), "create-foot");
            Add(foot, new Button(() =>
            {
                if (cStep > 0) cStep--;
                else { engine.GoTo(Engine.Title); titleStep = "menu"; }
                createError = null;
                GameAudio.Play("ui_close");
                Render();
            }) { text = cStep > 0 ? "◂  Back" : "◂  Title screen", focusable = false }, "front-btn");
            Add(foot, new Label(createError ?? ""), "create-error");
            if (cStep < CreationSteps.Length - 1)
                Add(foot, new Button(() => { cStep++; createError = null; GameAudio.Play("ui_tick"); Render(); }) { text = "Next  ▸", focusable = false }, "front-btn-main");
            else
                Add(foot, new Button(FinishCreation) { text = "Begin your story", focusable = false }, "front-btn-main");
        }

        /// <summary>Shows the card for what's under the pointer, and goes back to the picked one when it leaves.</summary>
        void HoverInfo(VisualElement e, string id)
        {
            e.RegisterCallback<PointerEnterEvent>(_ => FillInfo(id));
            e.RegisterCallback<PointerLeaveEvent>(_ => FillInfo(null));
        }

        /// <summary>The right-hand card. hover is the option under the pointer, or null for what's picked.</summary>
        void FillInfo(string hover)
        {
            if (infoBox == null) return;
            infoBox.Clear();
            var d = engine.data;
            switch (cStep)
            {
                case 0:
                    Add(infoBox, new Label("YOUR LOOKS"), "info-kicker");
                    string sex = cGender == "" ? "Not chosen" : char.ToUpperInvariant(cGender[0]) + cGender.Substring(1);
                    Sheet(infoBox, "Sex", sex);
                    Sheet(infoBox, "Body", cFigure == "female" ? "Feminine" : "Masculine");
                    Sheet(infoBox, "Skin", cSkin);
                    Sheet(infoBox, "Build", cBuild);
                    Sheet(infoBox, "Hair", CreationHairStyle() + ", " + cHair.ToLowerInvariant());
                    if (d.file.beards.Count > 0) Sheet(infoBox, "Beard", cBeard);
                    Add(infoBox, new Label("Sex only changes how people speak of you."), "info-note");
                    break;
                case 1:
                {
                    var o = d.Origin(hover ?? cOrigin);
                    if (o == null) { InfoEmpty("Point at a place to read about it."); break; }
                    InfoCard("WHERE YOU GREW UP", o.name, o.desc, o.perkName, o.perkDesc, hover != null && hover != cOrigin);
                    break;
                }
                case 2:
                {
                    var b = d.Background(hover ?? cBackground);
                    if (b == null) { InfoEmpty("Point at a life to read about it."); break; }
                    InfoCard("YOUR OLD LIFE", b.name, b.desc, b.perkName, b.perkDesc, hover != null && hover != cBackground);
                    break;
                }
                case 3:
                {
                    var q = hover != null ? d.Quirk(hover) : null;
                    if (q != null)
                    {
                        Add(infoBox, new Label("QUIRK"), "info-kicker");
                        Add(infoBox, new Label(q.name), "info-name");
                        InfoTrade(q.up, q.down);
                        if (!cQuirks.Contains(q.id) && cQuirks.Count >= Rules.MaxQuirks)
                            Add(infoBox, new Label("You already have two. Take one off first."), "info-warn");
                        break;
                    }
                    Add(infoBox, new Label("YOUR QUIRKS  ·  " + cQuirks.Count + " OF " + Rules.MaxQuirks), "info-kicker");
                    if (cQuirks.Count == 0) Add(infoBox, new Label("None yet. That's fine: quirks are optional."), "info-text");
                    foreach (var id in cQuirks)
                    {
                        var qq = d.Quirk(id);
                        if (qq == null) continue;
                        Add(infoBox, new Label(qq.name), "info-sub");
                        InfoTrade(qq.up, qq.down);
                    }
                    break;
                }
                case 4:
                {
                    var you = engine.PreviewCharacter(cBackground, alloc, cOrigin, cQuirks);
                    int stat = hover != null && hover.StartsWith("stat:") ? int.Parse(hover.Substring(5)) : -1;
                    if (stat >= 0)
                    {
                        Add(infoBox, new Label(Stat.Names[stat].ToUpperInvariant()), "info-kicker");
                        Add(infoBox, new Label(StatHelp[stat]), "info-text");
                    }
                    InfoNumbers(you);
                    break;
                }
                default:
                {
                    var you = engine.PreviewCharacter(cBackground, alloc, cOrigin, cQuirks);
                    Add(infoBox, new Label("WHO YOU ARE"), "info-kicker");
                    Add(infoBox, new Label(string.IsNullOrWhiteSpace(cName) ? "Nameless" : cName.Trim()), "info-name");
                    var o = d.Origin(cOrigin);
                    var b = d.Background(cBackground);
                    Sheet(infoBox, "From", o != null ? o.name : "Not chosen");
                    Sheet(infoBox, "Was a", b != null ? b.name : "Not chosen");
                    var perks = new List<string>();
                    if (o != null) perks.Add(o.perkName);
                    if (b != null) perks.Add(b.perkName);
                    perks.AddRange(cQuirks.Select(q => d.Quirk(q)?.name).Where(n => n != null));
                    if (perks.Count > 0) Sheet(infoBox, "Perks", string.Join(", ", perks));
                    var row = Add(infoBox, new VisualElement(), "info-stats");
                    for (int i = 0; i < Stat.Count; i++)
                    {
                        var cell = Add(row, new VisualElement(), "info-stat");
                        Add(cell, new Label(you.stats[i].ToString()), "info-stat-value");
                        Add(cell, new Label(Stat.Names[i].Substring(0, 3).ToUpperInvariant()), "info-stat-name");
                    }
                    InfoNumbers(you);
                    break;
                }
            }
            // the top heading sits closer to the card's edge (USS has no :first-child)
            if (infoBox.childCount > 0 && infoBox[0].ClassListContains("info-kicker")) infoBox[0].AddToClassList("info-kicker-top");
        }

        void InfoEmpty(string text) => Add(infoBox, new Label(text), "info-text");

        /// <summary>A card for an origin or a background: what it is, then its perk in its own box.</summary>
        void InfoCard(string kicker, string name, string desc, string perkName, string perkDesc, bool preview)
        {
            Add(infoBox, new Label(kicker), "info-kicker");
            Add(infoBox, new Label(name), "info-name");
            Add(infoBox, new Label(desc), "info-text");
            var perk = Add(infoBox, new VisualElement(), "info-perk");
            Add(perk, new Label("PERK  ·  " + perkName.ToUpperInvariant()), "info-perk-name");
            Add(perk, new Label(perkDesc), "info-perk-text");
            if (preview) Add(infoBox, new Label("Click to pick this."), "info-note");
        }

        /// <summary>A quirk's gain and price, one per line.</summary>
        void InfoTrade(string up, string down)
        {
            Add(infoBox, new Label("▲  " + up), "info-up");
            Add(infoBox, new Label("▼  " + down), "info-down");
        }

        /// <summary>Your fighting numbers as tiles, then only the everyday rates that aren't normal.</summary>
        void InfoNumbers(GameState you)
        {
            var d = engine.data;
            Add(infoBox, new Label("IN A FIGHT"), "info-kicker");
            var grid = Add(infoBox, new VisualElement(), "info-tiles");
            void Tile(string name, string value)
            {
                var t = Add(grid, new VisualElement(), "info-tile");
                Add(t, new Label(value), "info-tile-value");
                Add(t, new Label(name), "info-tile-name");
            }
            Tile("Health", Rules.MaxHp(you).ToString());
            Tile("Stamina", Rules.MaxStamina(you, d).ToString());
            Tile("Mana", Rules.MaxMana(you).ToString());
            Tile("Attack", Mathf.RoundToInt(Rules.Attack(you, d, 0)).ToString());
            Tile("Crit chance", Rules.CritChance(you, d) + "%");
            Tile("Speed", Rules.Speed(you, d).ToString());

            Add(infoBox, new Label("EVERY DAY"), "info-kicker");
            int shown = 0;
            void Day(string name, float rate, bool upIsGood)
            {
                if (Signed(rate) == "normal") return;
                shown++;
                bool good = (rate > 1f) == upIsGood;
                var r = Add(infoBox, new VisualElement(), "info-day");
                Add(r, new Label(name), "info-day-name");
                Add(r, new Label(Signed(rate)), good ? "info-day-good" : "info-day-bad");
            }
            Day("Your blows", Rules.QuirkMul(you, d, q => q.dealt), true);
            Day("Harm you take", Rules.QuirkMul(you, d, q => q.taken), false);
            Day("Shop prices", Rules.PriceRate(you, d), false);
            Day("Hunger", Rules.HungerRate(you, d), false);
            Day("Thirst", Rules.ThirstRate(you, d), false);
            Day("Cold and rain", Rules.WeatherRate(you, d), false);
            Day("Hard work", Rules.WorkRate(you), false);
            Day("Learning", Rules.LearnRate(you), true);
            if (shown == 0) Add(infoBox, new Label("Hunger, thirst, prices and the rest are all normal."), "info-text");
            else Add(infoBox, new Label("Green is in your favour. Everything not listed is normal."), "info-note");
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

        /// <summary>One line per choice: its name, and its perk on the right. Point at one to read about it.</summary>
        void CreatePick(VisualElement p, IEnumerable<(string id, string name, string perk)> options, string now, Action<string> set)
        {
            foreach (var (id, name, perk) in options)
            {
                var v = id;
                var row = Add(p, new Button(() => { set(v); GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "create-row");
                if (now == id) row.AddToClassList("create-row-on");
                Add(row, new Label(name), "create-row-name");
                Add(row, new Label(perk), "create-row-tag");
                HoverInfo(row, id);
            }
        }

        void CreateQuirks(VisualElement p)
        {
            var scroll = Add(p, new ScrollView(ScrollViewMode.Vertical), "create-opt-scroll");
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            foreach (var q in engine.data.file.quirks)
            {
                var v = q;
                bool on = cQuirks.Contains(v.id);
                var row = Add(scroll, new Button(() =>
                {
                    if (cQuirks.Contains(v.id)) cQuirks.Remove(v.id);
                    else if (cQuirks.Count < Rules.MaxQuirks) cQuirks.Add(v.id);
                    else { GameAudio.Play("ui_deny"); return; }
                    GameAudio.Play("ui_tick");
                    Render();
                }) { focusable = false }, "create-row");
                row.AddToClassList("create-quirk");
                if (on) row.AddToClassList("create-row-on");
                Add(row, new Label(on ? "✓" : ""), "create-check");
                Add(row, new Label(v.name), "create-row-name");
                HoverInfo(row, v.id);
            }
        }

        void CreateStats(VisualElement p)
        {
            int left = Engine.CreationPoints - alloc.Sum();
            var you = engine.PreviewCharacter(cBackground, alloc, cOrigin, cQuirks);
            var pips = Add(p, new VisualElement(), "create-pips");
            Add(pips, new Label("POINTS LEFT"), "create-pips-label");
            for (int i = 0; i < Engine.CreationPoints; i++) Add(pips, new VisualElement(), i < left ? "create-pip-on" : "create-pip");
            for (int i = 0; i < Stat.Count; i++)
            {
                int idx = i;
                var box = Add(p, new VisualElement(), "create-stat");
                HoverInfo(box, "stat:" + i);
                var r = Add(box, new VisualElement(), "create-stat-row");
                Add(r, new Label(Stat.Names[i]), "create-stat-name");
                var minus = Add(r, new Button(() => { if (alloc[idx] > 0) { alloc[idx]--; GameAudio.Play("ui_tick"); Render(); } }) { text = "−", focusable = false }, "create-stat-btn");
                minus.SetEnabled(alloc[idx] > 0);
                Add(r, new Label(you.stats[i].ToString()), "create-stat-value");
                var plus = Add(r, new Button(() => { if (alloc.Sum() < Engine.CreationPoints) { alloc[idx]++; GameAudio.Play("ui_tick"); Render(); } }) { text = "+", focusable = false }, "create-stat-btn");
                plus.SetEnabled(left > 0);
                Add(box, new Label(StatNow(you, i)), "create-stat-now");
            }
        }

        /// <summary>What one stat gives you right now, in a few words. Leaves out anything that's still normal.</summary>
        string StatNow(GameState s, int stat)
        {
            var d = engine.data;
            var bits = new List<string>();
            void Rate(string what, float rate) { if (Signed(rate) != "normal") bits.Add(what + " " + Signed(rate)); }
            switch (stat)
            {
                case Stat.Str: bits.Add("Attack " + Mathf.RoundToInt(Rules.Attack(s, d, 0))); Rate("hard work", Rules.WorkRate(s)); break;
                case Stat.End: bits.Add("Health " + Rules.MaxHp(s)); bits.Add("stamina " + Rules.MaxStamina(s, d)); Rate("hunger and thirst", Rules.NeedsRate(s)); break;
                case Stat.Agi: bits.Add("Crit " + Rules.CritChance(s, d) + "%"); bits.Add("speed " + Rules.Speed(s, d)); break;
                case Stat.Int: bits.Add("Mana " + Rules.MaxMana(s)); Rate("learning", Rules.LearnRate(s)); break;
                default: bits.Add("Shop prices " + Signed(Rules.PriceRate(s, d))); break;
            }
            return string.Join("  ·  ", bits);
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
            var field = new TextField { maxLength = 20, value = cName };
            field.AddToClassList("name-field");
            field.RegisterValueChangedCallback(e =>
            {
                cName = e.newValue;
                var shown = screen.Q<Label>(className: "create-stand-name");
                if (shown != null) shown.text = string.IsNullOrWhiteSpace(cName) ? "" : cName.Trim();
                FillInfo(null);
            });
            field.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { FinishCreation(); e.StopPropagation(); }
            });
            p.Add(field);
            field.schedule.Execute(() => field.Focus());
            Add(p, new Label("You'll wake on a strange shore with the clothes on your back and nothing else. Press Enter or \"Begin your story\" when you're ready."), "create-ask-sub");
        }
    }
}
