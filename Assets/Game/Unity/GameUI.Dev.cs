using System;
using System.Linq;
using Loran.Core;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The dev panel (F1): quick starts that drop a ready-made test character into any part of the game,
    /// a jump to any scene, and cheats. Dev games are never saved over your real save.
    /// </summary>
    public partial class GameUI
    {
        void MenuDev(VisualElement left, VisualElement right)
        {
            var s = engine.S;

            var starts = Section(left, "QUICK START  ·  A FRESH TEST CHARACTER");
            var grid = Add(starts, new VisualElement(), "dev-grid");
            foreach (var (id, name) in Engine.DevStarts)
            {
                var v = id;
                Add(grid, new Button(() => { engine.DevStart(v); openMenu = ""; ResetScreens(); Render(); }) { text = name }, "dev-btn");
            }

            var jump = Section(left, "JUMP TO A SCENE  ·  KEEPS THIS CHARACTER");
            var chips = Add(jump, new VisualElement(), "dev-grid");
            foreach (var id in engine.AllScenes)
            {
                var v = id;
                Add(chips, new Button(() => Cheat(() => engine.Jump(v), true)) { text = v }, "dev-chip");
            }

            var cheats = Section(right, "CHEATS");
            var row = Add(cheats, new VisualElement(), "dev-grid");
            Add(row, new Button(() => Cheat(engine.DevRefill)) { text = "Refill everything" }, "dev-btn");
            Add(row, new Button(() => Cheat(() => engine.DevCoins(Rules.Silver * 5))) { text = "+5 silver" }, "dev-btn");
            Add(row, new Button(() => Cheat(engine.DevItems)) { text = "Give every item" }, "dev-btn");
            Add(row, new Button(() => Cheat(engine.DevSpells)) { text = "Learn all spells" }, "dev-btn");
            Add(row, new Button(() => Cheat(engine.DevLevel)) { text = "Level up" }, "dev-btn");
            Add(row, new Button(() => Cheat(engine.DevSkills)) { text = "All skills +1" }, "dev-btn");
            Add(row, new Button(() => Cheat(engine.DevSkillPoints)) { text = "+3 skill points" }, "dev-btn");
            if (s.inCombat) Add(row, new Button(() => Cheat(engine.DevWinFight, true)) { text = "Win this fight" }, "dev-btn");
            Add(row, new Button(() => Cheat(() => engine.DevTime(6))) { text = "Morning" }, "dev-btn");
            Add(row, new Button(() => Cheat(() => engine.DevTime(21))) { text = "Night" }, "dev-btn");

            var hurt = Section(right, "WOUNDS AND WEATHER");
            var hr = Add(hurt, new VisualElement(), "dev-grid");
            foreach (var af in engine.data.file.afflictions)
            {
                string id = af.id;
                Add(hr, new Button(() => Cheat(() => engine.DevAfflict(id))) { text = af.name }, "dev-btn");
            }
            Add(hr, new Button(() => Cheat(engine.DevHeal)) { text = "Heal all wounds" }, "dev-btn");
            Add(hr, new Button(() => Cheat(engine.DevMedicine)) { text = "Medical supplies" }, "dev-btn");
            var wr = Add(hurt, new VisualElement(), "dev-grid");
            Add(wr, new Button(() => Cheat(() => engine.DevWeather(""))) { text = "Natural weather" }, "dev-btn");
            foreach (var w in engine.data.file.weathers)
            {
                string id = w.id;
                Add(wr, new Button(() => Cheat(() => engine.DevWeather(id))) { text = w.name }, "dev-btn");
            }

            var stats = Section(right, "STATS");
            for (int i = 0; i < Stat.Count; i++)
            {
                int idx = i;
                var r = Add(stats, new VisualElement(), "alloc-row");
                Add(r, new Label(Stat.Names[i]), "alloc-name");
                Add(r, new Button(() => Cheat(() => engine.DevStat(idx, -1))) { text = "−" }, "small-btn");
                Add(r, new Label(s.stats[i].ToString()), "alloc-value");
                Add(r, new Button(() => Cheat(() => engine.DevStat(idx, 1))) { text = "+" }, "small-btn");
            }

            var gear = Section(right, "MAKE GEAR AND WEAR IT");
            foreach (var g in engine.data.file.gear)
            {
                var r = Add(gear, new VisualElement(), "gear-row");
                Add(r, new Label(g.name), "dev-gear-name");
                foreach (char rank in Rules.Ranks)
                {
                    string rk = rank.ToString();
                    var b = Add(r, new Button(() => Cheat(() => engine.DevGear(g.id, rk))) { text = rk }, "dev-rank");
                    ColorUtilityTint(b, rk);
                }
            }

            Add(right, new Label("Quick starts and cheats make this a dev game. Dev games are never saved, so your real save is safe. Press F1 to close."), "line-muted");
        }

        /// <summary>Runs a cheat, marks the game as a dev game, and redraws. close: also shut the panel.</summary>
        void Cheat(Action a, bool close = false)
        {
            if (!engine.S.Has(Engine.DevFlag)) engine.S.flags.Add(Engine.DevFlag);
            a();
            if (close) { openMenu = ""; ResetScreens(); }
            Render();
        }

        /// <summary>Clears any half-played fight, timing bar or forge step so a jump starts clean.</summary>
        void ResetScreens()
        {
            timing = null;
            busy = outro = false;
            battleMenu = "";
            forgeStage = -1;
            stageRunning = false;
        }

        static void ColorUtilityTint(VisualElement b, string rank)
        {
            UnityEngine.ColorUtility.TryParseHtmlString(RankColour(rank), out var c);
            b.style.color = c;
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = c;
        }
    }
}
