using System;
using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The menu screen, laid out the way most RPGs do it: a row of tabs along the top (Tab / Shift+Tab, or each
    /// menu's own key), the menu in the middle, and the keys you can use along the bottom.
    ///
    /// Inventory: your figure with the equipment slots round it (Diablo, The Witcher, Baldur's Gate), the bag as a
    /// grid of slots with pockets to filter by, and a details panel that compares a piece of gear with what you're
    /// wearing. Character: a character sheet. Skills: each skill with its moves laid out along it.
    /// Journal: a quest list on the left and the chosen quest on the right.
    /// </summary>
    public partial class GameUI
    {
        const int BagColumns = 6, BagRows = 5;

        string invSel = "";        // what's picked in the inventory: "item:bread", "gear:12", or ""
        string invPocket = "all";  // which pocket of the bag is showing
        string skillSel = "";      // the skill picked in the Skills menu
        string questSel = "";      // the quest picked in the Journal
        bool questDone;            // showing finished quests instead of current ones

        static readonly (string id, string name)[] Pockets =
        {
            ("all", "All"), ("gear", "Gear"), ("food", "Food"), ("medicine", "Medicine"),
            ("material", "Materials"), ("tool", "Tools"), ("key", "Key items"),
        };

        /// <summary>Tab / Shift+Tab: the next or previous menu along the top.</summary>
        void NextMenu(int dir)
        {
            int at = Array.FindIndex(Menus, m => m.id == openMenu);
            if (at < 0) return;
            openMenu = Menus[(at + dir + Menus.Length) % Menus.Length].id;
            GameAudio.Play("ui_tick");
            Render();
        }

        void RenderMenu()
        {
            panel.Clear();
            var head = Add(panel, new VisualElement(), "panel-head");
            if (openMenu == "dev") Add(head, new Label("DEV MODE  ·  F1"), "panel-title");
            else
            {
                var tabs = Add(head, new VisualElement(), "menu-tabs");
                foreach (var m in Menus)
                {
                    string id = m.id;
                    var t = Add(tabs, new Button(() => { if (openMenu != id) { openMenu = id; GameAudio.Play("ui_tick"); Render(); } }) { focusable = false }, "menu-tab");
                    if (id == openMenu) t.AddToClassList("menu-tab-on");
                    Add(t, new Label(m.name.ToUpperInvariant()), "menu-tab-name");
                    Add(t, new Label(m.key.ToString()), "menu-tab-key");
                }
            }
            var headRight = Add(head, new VisualElement(), "panel-head-right");
            Add(headRight, new Button(() => ToggleMenu(openMenu)) { text = "×   Esc", focusable = false }, "close-btn");

            var body = Add(panel, new VisualElement(), "menu-body");
            string keys = "Tab / Shift+Tab  next menu      Esc  close      I  menu wheel";
            switch (openMenu)
            {
                case "inventory": MenuInventory(body); keys = "Click an item to see it      Enter  use or wear it      " + keys; break;
                case "character": MenuCharacter(body); break;
                case "skills": MenuSkills(body); break;
                case "journal": MenuJournal(body); break;
                case "survival": MenuSurvival(body); keys = "↑ ↓  wound      ← →  person      Enter  next step      " + keys; break;
                case "party": MenuParty(body); break;
                default:
                {
                    var cols = Add(body, new VisualElement(), "cols");
                    var left = Add(cols, new VisualElement(), "col");
                    var right = Add(cols, new VisualElement(), "col");
                    switch (openMenu)
                    {
                        case "crafting": MenuCrafting(left, right); break;
                        case "world": MenuWorld(left, right); break;
                        case "dev": MenuDev(left, right); break;
                    }
                    break;
                }
            }
            if (openMenu != "dev")
            {
                var foot = Add(panel, new VisualElement(), "menu-foot");
                Add(foot, new Label(keys), "menu-keys");
                var s = engine.S;
                Add(foot, new Label("Day " + s.Day + "  ·  " + Rules.Clock(s) + "      " + Rules.Money(s.coins, true)), "menu-foot-right");
            }
        }

        /// <summary>Keys inside a menu: Tab moves between menus; in the inventory Enter uses or wears the picked thing.</summary>
        bool MenuKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Tab) { NextMenu(e.shiftKey ? -1 : 1); return true; }
            if (openMenu == "survival" && SurvivalKey(e.keyCode)) return true;
            if (openMenu == "inventory" && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                InventoryUse();
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ inventory

        void MenuInventory(VisualElement body)
        {
            var s = engine.S;
            var row = Add(body, new VisualElement(), "inv");

            // Left: you, with what you're wearing round you
            var eq = Add(row, new VisualElement(), "inv-col");
            eq.AddToClassList("inv-equip");
            Add(eq, new Label("EQUIPPED"), "section-head");
            var paper = Add(eq, new VisualElement(), "paper");
            var leftSlots = Add(paper, new VisualElement(), "paper-slots");
            EquipSlot(leftSlots, "head", 0);
            EquipSlot(leftSlots, "body", 0);
            EquipSlot(leftSlots, "legs", 0);
            EquipSlot(leftSlots, "feet", 0);
            var figure = Add(paper, new VisualElement(), "paper-figure");
            PlayerDoll(figure, 6);
            var rightSlots = Add(paper, new VisualElement(), "paper-slots");
            EquipSlot(rightSlots, "neck", 0);
            EquipSlot(rightSlots, "hands", 0);
            EquipSlot(rightSlots, "ring", 0);
            EquipSlot(rightSlots, "ring", 1);
            var hands = Add(eq, new VisualElement(), "paper-hands");
            EquipSlot(hands, "weapon", 0);
            EquipSlot(hands, "offhand", 0);

            var totals = Add(eq, new VisualElement(), "inv-totals");
            Total(totals, "Attack", Mathf.RoundToInt(engine.AttackPower()).ToString());
            Total(totals, "Guard", Mathf.RoundToInt(Rules.GearGuard(s, engine.data) * 100) + "%");
            Total(totals, "Crit", Rules.CritChance(s, engine.data) + "%");
            Total(totals, "Warmth", "+" + Mathf.RoundToInt(Rules.Warmth(s, engine.data)) + "°");

            // Middle: the bag
            var bag = Add(row, new VisualElement(), "inv-col");
            bag.AddToClassList("inv-bag");
            var things = BagContents();
            Add(bag, new Label("PACK  ·  " + things.Count + " / " + BagColumns * BagRows), "section-head");
            var pockets = Add(bag, new VisualElement(), "pockets");
            foreach (var (id, name) in Pockets)
            {
                string pid = id;
                int n = id == "all" ? things.Count : things.Count(t => t.pocket == id);
                var b = Add(pockets, new Button(() => { invPocket = pid; GameAudio.Play("ui_tick"); Render(); }) { text = name + (n > 0 ? " " + n : ""), focusable = false }, "pocket");
                if (invPocket == id) b.AddToClassList("pocket-on");
                if (n == 0 && id != "all") b.AddToClassList("pocket-empty");
            }
            var shown = things.Where(t => invPocket == "all" || t.pocket == invPocket).ToList();
            bool selVisible = invSel.StartsWith("gear:") && s.gearItems.Any(g => g.equipped && "gear:" + g.uid == invSel);
            if (!selVisible && shown.All(t => t.key != invSel)) invSel = shown.Count > 0 ? shown[0].key : "";
            var grid = Add(bag, new VisualElement(), "bag-grid");
            for (int i = 0; i < Math.Max(BagColumns * BagRows, shown.Count); i++)
            {
                if (i >= shown.Count) { Add(grid, new VisualElement(), "bag-cell-empty"); continue; }
                var t = shown[i];
                string key = t.key;
                var cell = Add(grid, new Button(() => { invSel = key; GameAudio.Play("ui_tick"); Render(); }) { focusable = false, tooltip = t.name }, "bag-cell");
                if (key == invSel) cell.AddToClassList("bag-cell-on");
                Icon(cell, t.icon, 3);
                if (t.rank != null) RankEdge(cell, t.rank);
                if (t.count > 1) Add(cell, new Label(t.count.ToString()), "slot-count");
            }

            // Under the bag: coin and water
            var under = Add(bag, new VisualElement(), "bag-under");
            var purse = Add(under, new VisualElement(), "bag-stat");
            Add(purse, new Label("PURSE"), "bag-stat-name");
            Add(purse, new Label(Rules.Money(s.coins)), "bag-stat-value");
            purse.tooltip = "100 copper make a silver, 100 silver a gold, 100 gold a platinum. A week's honest work pays about 30 silver.";
            var skin = Add(under, new VisualElement(), "bag-stat");
            Add(skin, new Label("WATERSKIN"), "bag-stat-name");
            var skinRow = Add(skin, new VisualElement(), "item-row");
            Add(skinRow, new Label(s.water + " / " + Rules.WaterskinSize + " drinks"), "bag-stat-value");
            var drink = Add(skinRow, new Button(() => { engine.Drink(); AfterAction(); }) { text = "Drink", focusable = false }, "small-btn");
            drink.SetEnabled(engine.FreeToAct && s.water > 0);
            if (s.spells.Contains("draw_water"))
            {
                var dw = Add(skinRow, new Button(() => { engine.DrawWater(); AfterAction(); }) { text = "Draw Water", focusable = false }, "small-btn");
                dw.SetEnabled(engine.FreeToAct && s.mana >= engine.ManaCost(Rules.DrawWaterMana) && s.water < Rules.WaterskinSize);
            }

            // Right: the thing you picked
            var det = Add(row, new VisualElement(), "inv-col");
            det.AddToClassList("inv-detail");
            Add(det, new Label("DETAILS"), "section-head");
            ItemDetails(det);
        }

        class BagThing
        {
            public string key, icon, name, pocket, rank;
            public int count;
        }

        /// <summary>Everything in the bag: spare gear first (best rank first), then items by pocket.</summary>
        List<BagThing> BagContents()
        {
            var s = engine.S;
            var list = new List<BagThing>();
            foreach (var g in s.gearItems.Where(g => !g.equipped).OrderByDescending(g => Rules.RankIndex(g.rank)))
                list.Add(new BagThing { key = "gear:" + g.uid, icon = g.id, name = g.name, pocket = "gear", rank = g.rank, count = 1 });
            var order = Pockets.Select(p => p.id).ToList();
            foreach (var it in s.items.Where(i => i.count > 0).OrderBy(i => order.IndexOf(engine.data.Item(i.id)?.Kind() ?? "material")))
            {
                var def = engine.data.Item(it.id);
                if (def == null) continue;
                list.Add(new BagThing { key = "item:" + it.id, icon = it.id, name = def.name, pocket = def.Kind(), count = it.count });
            }
            return list;
        }

        /// <summary>One equipment slot by the figure: the piece's icon with its rank round the edge, or the slot's name if it's empty.</summary>
        void EquipSlot(VisualElement parent, string slot, int index)
        {
            var s = engine.S;
            var g = s.gearItems.Where(x => x.equipped && x.slot == slot).Skip(index).FirstOrDefault();
            var box = Add(parent, new VisualElement(), "equip-slot");
            string label = slot == "ring" ? "Ring" : Engine.SlotName(slot);
            if (g == null)
            {
                var held = slot == "offhand" ? s.Equipped.FirstOrDefault(x => x.twoHanded) : null;
                var cell = Add(box, new VisualElement(), "equip-cell");
                cell.AddToClassList("equip-cell-empty");
                if (held != null) { Icon(cell, held.id, 3).AddToClassList("equip-ghost"); cell.tooltip = "Both hands on your " + held.name.ToLowerInvariant(); }
                Add(box, new Label(label), "equip-name");
                return;
            }
            string key = "gear:" + g.uid;
            var btn = Add(box, new Button(() => { invSel = key; GameAudio.Play("ui_tick"); Render(); }) { focusable = false, tooltip = g.name }, "equip-cell");
            if (invSel == key) btn.AddToClassList("bag-cell-on");
            Icon(btn, g.id, 3);
            RankEdge(btn, g.rank);
            Add(box, new Label(label), "equip-name");
        }

        static void RankEdge(VisualElement cell, string rank)
        {
            ColorUtility.TryParseHtmlString(RankColour(rank), out var c);
            cell.style.borderBottomColor = c;
            var tag = Add(cell, new Label(string.IsNullOrEmpty(rank) ? "F" : rank), "cell-rank");
            tag.style.color = c;
        }

        static void Total(VisualElement parent, string name, string value)
        {
            var t = Add(parent, new VisualElement(), "inv-total");
            Add(t, new Label(name.ToUpperInvariant()), "inv-total-name");
            Add(t, new Label(value), "inv-total-value");
        }

        /// <summary>The details panel: what the picked thing is, what it does, how it compares, and what you can do with it.</summary>
        void ItemDetails(VisualElement parent)
        {
            var s = engine.S;
            var d = engine.data;
            bool free = engine.FreeToAct;
            if (invSel == "") { Add(parent, new Label("Your pack is empty. Pick things up as you go: food, firewood, anything that might be useful."), "line-muted"); return; }

            var top = Add(parent, new VisualElement(), "det-top");
            if (invSel.StartsWith("gear:"))
            {
                int uid = int.Parse(invSel.Substring(5));
                var g = s.gearItems.FirstOrDefault(x => x.uid == uid);
                if (g == null) { invSel = ""; return; }
                var pic = Add(top, new VisualElement(), "det-icon");
                Icon(pic, g.id, 5);
                RankEdge(pic, g.rank);
                var names = Add(top, new VisualElement(), "det-names");
                var nm = Add(names, new Label(g.name), "det-name");
                ColorUtility.TryParseHtmlString(RankColour(g.rank), out var rc);
                nm.style.color = rc;
                var skill = d.Skill(d.Gear(g.id)?.skill ?? "");
                Add(names, new Label(Rules.RankName(g.rank) + "  ·  " + Engine.SlotName(g.slot) + (g.twoHanded ? ", both hands" : "") + (skill != null ? "  ·  " + skill.name : "")), "det-kind");
                if (g.equipped) Add(names, new Label("WORN"), "det-worn");

                // What it does, and next to what you've got on now
                var worn = g.equipped ? null : s.gearItems.FirstOrDefault(x => x.equipped && x.slot == g.slot);
                var stats = Add(parent, new VisualElement(), "det-stats");
                int warmth = d.Gear(g.id)?.warmth ?? 0, wornWarmth = worn != null ? d.Gear(worn.id)?.warmth ?? 0 : 0;
                Compare(stats, "Damage", g.damage, worn?.damage, "0.#", "");
                Compare(stats, "Blocks", g.guard * 100, worn != null ? worn.guard * 100 : (float?)null, "0", "%");
                Compare(stats, "Max stamina", g.stamina, worn?.stamina, "0", "");
                Compare(stats, "Warmth", warmth, worn != null ? wornWarmth : (float?)null, "0", "°");
                if (worn != null) Add(parent, new Label("Compared with your " + worn.name.ToLowerInvariant() + "."), "det-compare");
                else if (!g.equipped) Add(parent, new Label("Nothing worn there now."), "det-compare");
                foreach (var t in g.traits)
                {
                    var td = d.Trait(t);
                    if (td != null) Add(parent, new Label(td.name + ": " + td.desc), "gear-trait");
                }
                if (!string.IsNullOrEmpty(g.desc)) Add(parent, new Label(g.desc), "det-desc");

                // Upkeep: how sharp, or how sound, and keeping it that way
                string blocked = null;
                if (Rules.Wears(g))
                {
                    bool blade = Rules.IsBlade(g);
                    var cond = Add(parent, new VisualElement(), "upkeep");
                    var head = Add(cond, new VisualElement(), "upkeep-head");
                    Add(head, new Label(blade ? "EDGE" : "CONDITION"), "upkeep-name");
                    var state = Add(head, new Label(Rules.ConditionName(g) + "  ·  " + Mathf.RoundToInt(g.condition) + "%"), "upkeep-state");
                    var track = Add(cond, new VisualElement(), "upkeep-track");
                    var fill = Add(track, new VisualElement(), "upkeep-fill");
                    fill.style.width = Length.Percent(g.condition);
                    string band = g.condition >= 60 ? "upkeep-good" : g.condition >= 30 ? "upkeep-fair" : "upkeep-bad";
                    fill.AddToClassList(band);
                    state.AddToClassList(band);
                    float share = blade ? Rules.Edge(g) : Rules.Soundness(g);
                    Add(cond, new Label(share >= 0.999f
                        ? (blade ? "Every blow dulls it a little. Keep it above 70% for full damage." : "Every hit it takes wears it a little. Keep it above 60% to block fully.")
                        : (blade ? "Hitting for " : "Blocking ") + Mathf.RoundToInt(share * 100) + "% of what it should. " + (blade ? "Sharpen it." : "Mend it.")), "upkeep-note");
                    blocked = engine.UpkeepBlocked(g);
                }

                var acts = Add(parent, new VisualElement(), "det-actions");
                var b = Add(acts, new Button(() => InventoryUse()) { text = g.equipped ? "Take off" : "Wear", focusable = false }, "det-btn");
                b.SetEnabled(free);
                if (Rules.Wears(g))
                {
                    int guid = g.uid;
                    var fix = Add(acts, new Button(() => { if (engine.Upkeep(guid)) GameAudio.Play("pickup"); AfterAction(); })
                        { text = Rules.IsBlade(g) ? "Sharpen" : "Mend", focusable = false }, "det-btn");
                    fix.SetEnabled(blocked == null);
                    if (blocked != null) Add(parent, new Label(blocked), "det-compare");
                }
                return;
            }

            string id = invSel.Substring(5);
            var def = d.Item(id);
            int count = s.Count(id);
            if (def == null || count <= 0) { invSel = ""; return; }
            var ipic = Add(top, new VisualElement(), "det-icon");
            Icon(ipic, id, 5);
            var inames = Add(top, new VisualElement(), "det-names");
            Add(inames, new Label(def.name + (count > 1 ? "  ×" + count : "")), "det-name");
            Add(inames, new Label(Pockets.FirstOrDefault(p => p.id == def.Kind()).name ?? ""), "det-kind");
            if (def.food)
            {
                var stats = Add(parent, new VisualElement(), "det-stats");
                FoodStat(stats, "Hunger", def.hunger);
                if (Mathf.Abs(def.thirst) > 0.01f) FoodStat(stats, "Thirst", def.thirst);
                if (def.energy > 0) FoodStat(stats, "Energy", def.energy);
            }
            if (def.heal > 0)
            {
                var stats = Add(parent, new VisualElement(), "det-stats");
                FoodStat(stats, "Health", def.heal);
            }
            if (!string.IsNullOrEmpty(def.desc)) Add(parent, new Label(def.desc), "det-desc");
            var uses = d.file.treatments.Where(t => t.items.Any(x => x.Split('+').Contains(id)) || t.tool == id).Select(t => t.name.ToLowerInvariant()).ToList();
            if (uses.Count > 0) Add(parent, new Label("For wounds: " + string.Join(", ", uses) + ". Treat wounds under Survival (S)."), "det-compare");

            var iacts = Add(parent, new VisualElement(), "det-actions");
            if (def.food)
            {
                var b = Add(iacts, new Button(() => InventoryUse()) { text = "Eat", focusable = false }, "det-btn");
                b.SetEnabled(free);
            }
            else if (def.heal > 0)
            {
                var b = Add(iacts, new Button(() => InventoryUse()) { text = "Drink", focusable = false }, "det-btn");
                b.SetEnabled(free && s.hp < Rules.MaxHp(s));
                foreach (var m in s.party)
                {
                    var f = d.Follower(m.id);
                    if (f == null) continue;
                    string mid = m.id;
                    var g = Add(iacts, new Button(() => { if (engine.UsePotion(mid, id)) GameAudio.Play("pickup"); AfterAction(); }) { text = "Give to " + f.name, focusable = false }, "det-btn");
                    g.SetEnabled(free && m.hp > 0 && m.hp < Rules.MemberMaxHp(m, f));
                }
            }
            else if (uses.Count > 0)
            {
                var b = Add(iacts, new Button(() => { openMenu = "survival"; GameAudio.Play("ui_tick"); Render(); }) { text = "Treat wounds", focusable = false }, "det-btn");
                b.SetEnabled(s.body.afflictions.Count > 0 || s.party.Any(m => m.body.afflictions.Count > 0));
            }
        }

        /// <summary>Enter, or the main button: eat the food, or wear / take off the gear.</summary>
        void InventoryUse()
        {
            if (!engine.FreeToAct || invSel == "") return;
            if (invSel.StartsWith("gear:"))
            {
                int uid = int.Parse(invSel.Substring(5));
                var g = engine.S.gearItems.FirstOrDefault(x => x.uid == uid);
                if (g == null) return;
                bool ok = g.equipped ? engine.Unequip(uid) : engine.Equip(uid);
                if (ok) GameAudio.Play(g.equipped ? "pickup" : "ui_close");
            }
            else
            {
                string id = invSel.Substring(5);
                var it = engine.data.Item(id);
                if (it?.food == true) engine.Eat(id);
                else if (it != null && it.heal > 0 && engine.UsePotion("", id)) GameAudio.Play("pickup");
            }
            AfterAction();
        }

        static void Compare(VisualElement parent, string name, float value, float? now, string fmt, string unit)
        {
            if (Mathf.Abs(value) < 0.01f && (now == null || Mathf.Abs(now.Value) < 0.01f)) return;
            var r = Add(parent, new VisualElement(), "det-stat");
            Add(r, new Label(name), "det-stat-name");
            Add(r, new Label(value.ToString(fmt) + unit), "det-stat-value");
            if (now == null) return;
            float diff = value - now.Value;
            if (Mathf.Abs(diff) < 0.05f) { Add(r, new Label("="), "det-same"); return; }
            Add(r, new Label((diff > 0 ? "▲ +" : "▼ ") + diff.ToString(fmt) + unit), diff > 0 ? "det-better" : "det-worse");
        }

        static void FoodStat(VisualElement parent, string name, float value)
        {
            var r = Add(parent, new VisualElement(), "det-stat");
            Add(r, new Label(name), "det-stat-name");
            Add(r, new Label((value > 0 ? "+" : "") + value.ToString("0")), value >= 0 ? "det-better" : "det-worse");
        }

        // ------------------------------------------------------------------ character sheet

        void MenuCharacter(VisualElement body)
        {
            var s = engine.S;
            var d = engine.data;
            var row = Add(body, new VisualElement(), "inv");

            var who = Add(row, new VisualElement(), "inv-col");
            who.AddToClassList("char-who");
            var fig = Add(who, new VisualElement(), "char-figure");
            PlayerDoll(fig, 7);
            Add(who, new Label(s.name), "char-name");
            var bg = d.Background(s.background);
            string sex = s.gender == "" ? "" : char.ToUpperInvariant(s.gender[0]) + s.gender.Substring(1) + "  ·  ";
            Add(who, new Label(sex + s.build + "  ·  " + (bg != null ? bg.name : "")), "char-sub");
            Add(who, new Label("LEVEL " + s.level), "char-level");
            var t0 = Add(who, new VisualElement(), "xp-track");
            Add(t0, new VisualElement(), "xp-fill").style.width = Length.Percent(Mathf.Clamp01(s.exp / (float)Rules.ExpToNext(s.level)) * 100f);
            Add(who, new Label(s.exp + " / " + Rules.ExpToNext(s.level) + " experience"), "char-sub");
            if (s.skillPoints > 0) Add(who, new Label(s.skillPoints + " skill point" + (s.skillPoints == 1 ? "" : "s") + " to spend under Skills"), "skill-points-have");
            if (bg != null && !string.IsNullOrEmpty(bg.perkName))
            {
                var perk = Add(who, new VisualElement(), "perk-card");
                Add(perk, new Label("PERK  ·  " + bg.perkName.ToUpperInvariant()), "perk-name");
                Add(perk, new Label(bg.perkDesc), "perk-desc");
            }
            var org = d.Origin(s.origin);
            if (org != null)
            {
                var card = Add(who, new VisualElement(), "perk-card");
                Add(card, new Label("FROM " + org.name.ToUpperInvariant() + "  ·  " + org.perkName.ToUpperInvariant()), "perk-name");
                Add(card, new Label(org.perkDesc), "perk-desc");
            }
            foreach (var qid in s.quirks)
            {
                var q = d.Quirk(qid);
                if (q == null) continue;
                var card = Add(who, new VisualElement(), "perk-card");
                card.AddToClassList("quirk-card");
                Add(card, new Label("QUIRK  ·  " + q.name.ToUpperInvariant()), "perk-name");
                Add(card, new Label("+ " + q.up), "quirk-up");
                Add(card, new Label("− " + q.down), "quirk-down");
            }

            var mid = Add(row, new VisualElement(), "inv-col");
            mid.AddToClassList("char-col");
            var attr = Section(mid, "ATTRIBUTES");
            for (int i = 0; i < Stat.Count; i++)
            {
                var r = Add(attr, new VisualElement(), "stat-row");
                Add(r, new Label(Stat.Names[i]), "stat-name");
                Add(r, new Label(s.stats[i].ToString()), "stat-value");
                var t = Add(r, new VisualElement(), "xp-track");
                Add(t, new VisualElement(), "xp-fill").style.width = Length.Percent(Mathf.Clamp01(s.xp[i] / (s.stats[i] * 3f)) * 100f);
            }
            Add(attr, new Label("Attributes grow with practice: fighting, running, working, casting. The bar shows progress to the next point."), "line-muted");
            var spells = Section(mid, "SPELLS");
            if (s.spells.Count == 0) Add(spells, new Label("You don't know any magic yet."), "line-muted");
            foreach (var id in s.spells)
            {
                var sp = d.Spell(id);
                if (sp != null) Add(spells, new Label(sp.name + "  ·  " + sp.desc), "line");
            }

            var right = Add(row, new VisualElement(), "inv-col");
            right.AddToClassList("char-col");
            var fight = Section(right, "IN A FIGHT");
            Sheet(fight, "Health", Mathf.CeilToInt(s.hp) + " / " + Rules.MaxHp(s));
            Sheet(fight, "Mana", Mathf.FloorToInt(s.mana) + " / " + Rules.MaxMana(s));
            Sheet(fight, "Stamina", Mathf.FloorToInt(s.stamina) + " / " + Rules.MaxStamina(s, d));
            Sheet(fight, "Attack", Mathf.RoundToInt(engine.AttackPower()) + "  (" + (d.Skill(engine.WeaponSkill())?.name ?? "Fists") + ")");
            Sheet(fight, "Guard", Mathf.RoundToInt(Rules.GearGuard(s, d) * 100) + "% of each hit");
            Sheet(fight, "Critical hits", Rules.CritChance(s, d) + "% chance");
            Sheet(fight, "Speed", Rules.Speed(s, d).ToString());
            Add(fight, new Label("You act first against anything slower than your speed. Dodging and blocking always come first."), "line-muted");
            var road = Section(right, "ON THE ROAD");
            Sheet(road, "Warmth from clothes", "+" + Mathf.RoundToInt(Rules.Warmth(s, d)) + "°");
            Sheet(road, "Death mode", s.deathMode == "perma" ? "Permadeath" : "Basic");
            var world = Section(right, "IN THE WORLD");
            YouSheet(world, s, false);
            Add(world, new Label("Against normal. Strength eases hard work, Endurance your hunger and thirst, Intellect your learning, Charisma your prices."), "line-muted");
        }

        static void Sheet(VisualElement parent, string name, string value)
        {
            var r = Add(parent, new VisualElement(), "sheet-row");
            Add(r, new Label(name), "sheet-name");
            Add(r, new Label(value), "sheet-value");
        }

        // ------------------------------------------------------------------ skills

        void MenuSkills(VisualElement body)
        {
            var s = engine.S;
            var d = engine.data;
            var row = Add(body, new VisualElement(), "inv");

            var list = Add(row, new VisualElement(), "inv-col");
            list.AddToClassList("skill-list");
            var pts = Add(list, new Label("SKILL POINTS  " + s.skillPoints), "skill-points");
            if (s.skillPoints > 0) pts.AddToClassList("skill-points-have");
            if (d.file.skills.All(k => k.id != skillSel)) skillSel = engine.WeaponSkill();
            if (d.Skill(skillSel) == null) skillSel = d.file.skills.FirstOrDefault()?.id ?? "";
            foreach (var kind in new[] { "combat", "life" })
            {
                Add(list, new Label(kind == "combat" ? "FIGHTING" : "LIFE"), "section-head");
                foreach (var sk in d.file.skills.Where(k => k.kind == kind))
                {
                    string id = sk.id;
                    int lv = engine.SkillLevel(id);
                    int ready = d.file.moves.Count(m => m.skill == id && !s.moves.Contains(m.id) && engine.CanLearn(m, out _));
                    var b = Add(list, new Button(() => { skillSel = id; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "skill-tab");
                    if (id == skillSel) b.AddToClassList("skill-tab-on");
                    var top = Add(b, new VisualElement(), "skill-tab-top");
                    Add(top, new Label(sk.name + (ready > 0 ? "  •" : "")), "skill-tab-name");
                    Add(top, new Label("Lv " + lv), "skill-tab-level");
                    var t = Add(b, new VisualElement(), "xp-track");
                    Add(t, new VisualElement(), "xp-fill").style.width = Length.Percent(Mathf.Clamp01(engine.SkillProgress(id)) * 100f);
                }
            }

            // The picked skill: what it is, and its moves laid out along it by level
            var det = Add(row, new VisualElement(), "inv-col");
            det.AddToClassList("skill-detail");
            var sd = d.Skill(skillSel);
            if (sd == null) return;
            int level = engine.SkillLevel(sd.id);
            Add(det, new Label(sd.name.ToUpperInvariant() + "  ·  LEVEL " + level), "det-name");
            Add(det, new Label(sd.desc), "det-desc");
            if (sd.kind == "combat")
            {
                bool using_ = engine.WeaponSkill() == sd.id || (sd.id == "shield" && engine.HasShield());
                Add(det, new Label(using_ ? "You're using this now: it grows every time you fight." : "Grows when you fight with it."), "line-muted");
                var moves = d.file.moves.Where(m => m.skill == sd.id).OrderBy(m => m.level).ToList();
                if (moves.Count == 0) Add(det, new Label("No moves to learn yet."), "line-muted");
                var track = Add(det, new VisualElement(), "move-track");
                foreach (var m in moves)
                {
                    bool known = s.moves.Contains(m.id);
                    bool can = engine.CanLearn(m, out var why);
                    var node = Add(track, new VisualElement(), "move-node");
                    node.AddToClassList(known ? "move-known" : can ? "move-ready" : "move-locked");
                    var dot = Add(node, new Label(known ? "✓" : "Lv " + m.level), "move-dot");
                    var txt = Add(node, new VisualElement(), "move-text");
                    Add(txt, new Label(m.name), "move-name");
                    Add(txt, new Label(m.desc), "line-muted");
                    var cost = new List<string>();
                    if (m.stamina > 0) cost.Add(m.stamina + " stamina");
                    if (m.mana > 0) cost.Add(m.mana + " mana");
                    if (cost.Count > 0) Add(txt, new Label(string.Join(" · ", cost)), "move-cost");
                    if (!known)
                    {
                        string mid = m.id;
                        if (can) Add(node, new Button(() => { if (engine.LearnMove(mid)) GameAudio.Play("skill_up"); AfterAction(); }) { text = "Learn  ·  1 point", focusable = false }, "det-btn");
                        else Add(node, new Label(level < m.level ? "Needs level " + m.level : why), "move-why");
                    }
                }
            }
            else Add(det, new Label("Life skills grow as you do the work. Every level makes that work lighter: less stamina, up to 40% less."), "line-muted");
        }

        // ------------------------------------------------------------------ journal

        void MenuJournal(VisualElement body)
        {
            var s = engine.S;
            var row = Add(body, new VisualElement(), "inv");
            var list = Add(row, new VisualElement(), "inv-col");
            list.AddToClassList("skill-list");
            var active = engine.ActiveQuests.ToList();
            var done = engine.FinishedQuests.ToList();
            var tabs = Add(list, new VisualElement(), "pockets");
            var ta = Add(tabs, new Button(() => { questDone = false; questSel = ""; Render(); }) { text = "Current " + active.Count, focusable = false }, "pocket");
            var td = Add(tabs, new Button(() => { questDone = true; questSel = ""; Render(); }) { text = "Done " + done.Count, focusable = false }, "pocket");
            (questDone ? td : ta).AddToClassList("pocket-on");
            var shown = questDone ? done : active;
            if (shown.All(q => q.id != questSel)) questSel = shown.FirstOrDefault()?.id ?? "";
            if (shown.Count == 0) Add(list, new Label(questDone ? "Nothing finished yet." : "Nobody's asked anything of you right now."), "line-muted");
            foreach (var q in shown)
            {
                string id = q.id;
                var b = Add(list, new Button(() => { questSel = id; GameAudio.Play("ui_tick"); Render(); }) { focusable = false }, "skill-tab");
                if (id == questSel) b.AddToClassList("skill-tab-on");
                var top = Add(b, new VisualElement(), "skill-tab-top");
                Add(top, new Label((questDone ? "✓  " : "") + q.name), "skill-tab-name");
                bool ready = !questDone && engine.QuestGoals(q).All(g => g.met);
                Add(top, new Label(ready ? "Hand in" : q.giver), ready ? "quest-ready" : "skill-tab-level");
            }
            var journal = engine.Journal();
            if (journal.Count > 0)
            {
                Add(list, new Label("NOTES"), "section-head");
                foreach (var e in journal) Add(list, new Label(e), "journal-entry");
            }

            var det = Add(row, new VisualElement(), "inv-col");
            det.AddToClassList("skill-detail");
            var sel = shown.FirstOrDefault(q => q.id == questSel);
            if (sel == null) return;
            Add(det, new Label(sel.name.ToUpperInvariant()), "det-name");
            Add(det, new Label("From " + sel.giver), "det-kind");
            Add(det, new Label(sel.desc), "det-desc");
            if (!questDone)
            {
                Add(det, new Label("OBJECTIVES"), "section-head");
                var goals = engine.QuestGoals(sel);
                foreach (var (text, met, progress) in goals)
                    Add(det, new Label((met ? "✓  " : "○  ") + text + (progress != "" ? "   " + progress : "")), met ? "quest-goal-done" : "quest-goal");
                if (goals.All(g => g.met)) Add(det, new Label("→  Go back to " + sel.giver + "."), "quest-return");
            }
            else Add(det, new Label("Done."), "quest-goal-done");
        }
    }
}
