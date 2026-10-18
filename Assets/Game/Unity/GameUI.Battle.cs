using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The battle screen: enemy top right, you bottom left, the enemy's tell in the middle, and a command menu
    /// (Attack, Skills, Items, Defend, Flee). Attacking and defending use a timing bar: press Space in the bright zone.
    /// </summary>
    public partial class GameUI
    {
        const float TimingSeconds = 1.05f;   // how long the marker takes to cross the bar

        static readonly (string id, string name)[] BattleCommands =
        {
            ("attack", "Attack"), ("skill", "Skills"), ("item", "Items"), ("defend", "Defend"), ("flee", "Flee"),
        };

        string battleMenu = "";               // "" = the five commands, or "skill" / "item" for a sub-list
        List<ChoiceView> battleButtons = new List<ChoiceView>();   // what keys 1-9 pick
        bool busy;                             // animations playing; input waits
        bool outro;                            // the fight just ended; show its last blows before moving on

        // The timing bar
        ChoiceView timing;
        float timingStart, zoneCentre;
        IVisualElementScheduledItem timingTick;
        VisualElement timingMarker;

        // Bars animate from what they showed before the action to what they are now.
        float[] before;
        VisualElement enemyCard, playerCard, arena, enemyPic, playerPic;
        readonly List<VisualElement> followerSpots = new List<VisualElement>(), followerPics = new List<VisualElement>();

        bool InBattle => engine.S.inCombat || outro;

        // ------------------------------------------------------------------ drawing

        VisualElement enemySpot, playerSpot;
        float battleHeight;

        /// <summary>The height the battle screen fills: all of the main area, so it's never a small box in the middle.</summary>
        float BattleHeight()
        {
            float h = mainBox != null ? mainBox.layout.height : float.NaN;
            return float.IsNaN(h) || h < 400 ? 960f : h - 56f;   // 56: the main area's top and bottom padding
        }

        void RenderBattle()
        {
            var s = engine.S;
            var e = engine.data.Enemy(s.enemy);
            main.Clear();
            battleHeight = BattleHeight();
            var battle = Add(main, new VisualElement(), "battle");
            battle.style.height = battleHeight;

            var head = Add(battle, new VisualElement(), "battle-head");
            string place = engine.Scene(s.scene)?.place ?? "";
            Add(head, new Label(("Fight  ·  " + place).ToUpperInvariant()), "place");
            Add(head, new Label("ROUND " + s.round), "battle-round");

            // The stage: a painted backdrop with you on the left and the enemy on the right, both on the ground.
            arena = Add(battle, new VisualElement(), "arena");
            var backdrop = Sprite("Backdrops/" + BackdropFor(place)) ?? Sprite("Backdrops/coast");
            if (backdrop != null) arena.style.backgroundImage = new StyleBackground(backdrop);
            float stageH = battleHeight - 300f;                 // what's left after the header, log and commands
            int scale = Mathf.Clamp(Mathf.FloorToInt(stageH * 0.5f / DollH), 3, 9);

            playerSpot = Add(arena, new VisualElement(), "spot-player");
            playerPic = PlayerDoll(playerSpot, scale);
            if (s.hp <= 0) playerSpot.AddToClassList("portrait-down");

            // Your party stands a little behind you, staggered so everyone can be seen
            followerSpots.Clear();
            followerPics.Clear();
            for (int pi = 0; pi < s.party.Count; pi++)
            {
                var m = s.party[pi];
                var fol = engine.data.Follower(m.id);
                if (fol == null) { followerSpots.Add(null); followerPics.Add(null); continue; }
                var spot = Add(arena, new VisualElement(), "spot-follower");
                spot.style.left = Length.Percent(5 - pi * 3.5f);
                spot.style.bottom = Length.Percent(9 + pi * 14);
                int fk = Mathf.Max(2, scale * 2 - pi);
                var box = Add(spot, new VisualElement { pickingMode = PickingMode.Ignore }, "walker");
                box.style.position = Position.Relative;
                box.style.width = 16 * fk;
                box.style.height = 24 * fk;
                var sheet = Add(box, new VisualElement { pickingMode = PickingMode.Ignore }, "walker-sheet");
                sheet.style.width = 48 * fk;
                sheet.style.height = 96 * fk;
                sheet.style.top = -3 * 24 * fk;      // the row facing right
                var tex = Sprite("Walk/" + fol.sprite);
                if (tex != null) sheet.style.backgroundImage = new StyleBackground(tex);
                if (m.hp <= 0) spot.AddToClassList("portrait-down");
                var tag = Add(spot, new VisualElement(), "follower-tag");
                var trouble = Rules.Conditions(m, engine.data);
                Add(tag, new Label(fol.name + (trouble.Count > 0 ? "  ·  " + trouble[0] : "")), "follower-name");
                var ft = Add(tag, new VisualElement(), "need-track");
                var ff = Add(ft, new VisualElement(), "need-fill");
                ff.style.width = Length.Percent(Mathf.Clamp01(m.hp / fol.hp) * 100f);
                ColorUtility.TryParseHtmlString("#c8573f", out var fc);
                ff.style.backgroundColor = fc;
                followerSpots.Add(spot);
                followerPics.Add(box);
            }

            enemySpot = Add(arena, new VisualElement(), "spot-enemy");
            enemyPic = EnemySprite(enemySpot, e, DollH * scale * 1.15f * Mathf.Max(1f, e.size));
            if (enemyPic == null) Add(enemySpot, new Label(e.name.Substring(0, 1)), "portrait-letter");
            if (s.enemyHp <= 0) enemySpot.AddToClassList("portrait-down");

            // You: top left
            playerCard = Add(arena, new VisualElement(), "fighter-card");
            playerCard.AddToClassList("card-player");
            Add(playerCard, new Label(s.name), "fighter-name");
            Bar(playerCard, "HEALTH", s.hp, Rules.MaxHp(s), "#c8573f", 1, false);
            Bar(playerCard, "STAMINA", s.stamina, Rules.MaxStamina(s, engine.data), "#7fae6a", 2, false);
            Bar(playerCard, "ENERGY", s.energy, 100, "#9d8fd0", 3, false);
            var weapon = s.InSlot("weapon");
            Add(playerCard, new Label(weapon != null ? weapon.name + "  (" + weapon.rank + ")" : "Bare hands"), "fighter-sub");

            // The enemy: top right, with what it's about to do, so you can choose to attack or defend
            enemyCard = Add(arena, new VisualElement(), "fighter-card");
            enemyCard.AddToClassList("card-enemy");
            var nameRow = Add(enemyCard, new VisualElement(), "fighter-name-row");
            Add(nameRow, new Label(e.name), "fighter-name");
            RankBadge(nameRow, e.rank);
            if (e.boss) Add(enemyCard, new Label("BOSS"), "boss-tag");
            Bar(enemyCard, "HEALTH", Mathf.Max(0, s.enemyHp), e.hp, "#c8573f", 0, true);
            if (s.inCombat && engine.Intent != null)
            {
                Add(enemyCard, new Label(engine.Intent.tell), "intent-text");
                Add(enemyCard, new Label(engine.PlayerActsFirst || engine.Intent.guard ? "You're quicker: you act first." : "It's quicker: it acts first, unless you defend."), "intent-order");
            }

            // The last few things that happened
            var log = Add(battle, new VisualElement(), "battle-log");
            var lines = s.battleLog.Skip(Mathf.Max(0, s.battleLog.Count - 3)).ToList();
            if (lines.Count == 0) Add(log, new Label(engine.Fill(e.desc)), "log-line-new");
            for (int i = 0; i < lines.Count; i++)
                Add(log, new Label(lines[i]), i == lines.Count - 1 ? "log-line-new" : "log-line");

            // Timing bar, or the commands
            if (timing != null) RenderTimingBar(battle);
            else if (s.inCombat && !busy && !outro) RenderCommands(battle);
            else Add(battle, new VisualElement(), "commands-placeholder");

            before = null;
        }

        /// <summary>Which painted backdrop suits a place. Only the coast exists so far.</summary>
        static string BackdropFor(string place)
        {
            return "coast";
        }

        void RenderCommands(VisualElement parent)
        {
            var all = engine.Choices();
            battleButtons.Clear();
            var row = Add(parent, new VisualElement(), "commands");

            if (battleMenu == "")
            {
                for (int i = 0; i < BattleCommands.Length; i++)
                {
                    var (id, name) = BattleCommands[i];
                    var group = all.Where(c => Engine.CommandOf(c.def) == id).ToList();
                    var first = group.FirstOrDefault();
                    string sub = id == "skill" ? (group.Count == 0 ? "No skills yet" : group.Count + " known")
                               : id == "item" ? (group.Count == 0 ? "Nothing to use" : group.Count + " to use")
                               : first == null ? "" : !first.enabled ? first.reason : first.cost;
                    bool on = group.Any(c => c.enabled);
                    var b = CommandButton(row, i + 1, name, sub, on, () =>
                    {
                        if (id == "skill" || id == "item") { battleMenu = id; Render(); }
                        else Command(first);
                    });
                    battleButtons.Add(group.Count == 1 || id == "attack" || id == "defend" || id == "flee" ? first : null);
                }
            }
            else
            {
                var group = all.Where(c => Engine.CommandOf(c.def) == battleMenu).ToList();
                for (int i = 0; i < group.Count; i++)
                {
                    var cv = group[i];
                    CommandButton(row, i + 1, cv.def.label, cv.enabled ? cv.cost : cv.reason, cv.enabled, () => Command(cv));
                    battleButtons.Add(cv);
                }
                CommandButton(row, 0, "Back", "Esc", true, () => { battleMenu = ""; Render(); });
            }
        }

        Button CommandButton(VisualElement row, int n, string name, string sub, bool enabled, System.Action onClick)
        {
            var b = new Button(onClick) { focusable = false };
            b.AddToClassList("command");
            b.SetEnabled(enabled);
            Add(b, new Label(n > 0 ? n.ToString() : ""), "command-num");
            Add(b, new Label(name), "command-name");
            if (!string.IsNullOrEmpty(sub)) Add(b, new Label(sub), "command-sub");
            row.Add(b);
            return b;
        }

        void RenderTimingBar(VisualElement parent)
        {
            var box = Add(parent, new VisualElement(), "timing");
            bool defend = timing.def.effects[0] == "fight:defend";
            Add(box, new Label(defend ? "BRACE  ·  press SPACE in the bright zone to parry" : "STRIKE  ·  press SPACE in the bright zone"), "timing-head");
            var track = Add(box, new VisualElement(), "timing-track");
            track.RegisterCallback<PointerDownEvent>(_ => StopTiming());
            var (good, perfect) = Rules.TimingZone(engine.S, engine.data);
            var g = Add(track, new VisualElement(), "timing-good");
            g.style.left = Length.Percent((zoneCentre - good / 2) * 100);
            g.style.width = Length.Percent(good * 100);
            var p = Add(track, new VisualElement(), "timing-perfect");
            p.style.left = Length.Percent((zoneCentre - perfect / 2) * 100);
            p.style.width = Length.Percent(perfect * 100);
            timingMarker = Add(track, new VisualElement(), "timing-marker");
            MoveMarker();
        }

        void Bar(VisualElement parent, string name, float value, float max, string colour, int slot, bool big)
        {
            var n = Add(parent, new VisualElement(), big ? "fighter-bar-big" : "fighter-bar");
            var head = Add(n, new VisualElement(), "need-head");
            Add(head, new Label(name), "need-name");
            Add(head, new Label(Mathf.CeilToInt(Mathf.Max(0, value)) + " / " + Mathf.RoundToInt(max)), "need-value");
            var track = Add(n, new VisualElement(), "need-track");
            var fill = Add(track, new VisualElement(), "need-fill");
            fill.AddToClassList("bar-anim");
            ColorUtility.TryParseHtmlString(colour, out var c);
            fill.style.backgroundColor = c;
            float now = Mathf.Clamp01(value / max) * 100f;
            if (before != null)
            {
                fill.style.width = Length.Percent(Mathf.Clamp01(before[slot] / max) * 100f);
                fill.schedule.Execute(() => fill.style.width = Length.Percent(now)).StartingIn(30);
            }
            else fill.style.width = Length.Percent(now);
        }

        static readonly string[] RankColours = { "#8a918d", "#7fae6a", "#4f9b97", "#5b8fd0", "#a77fd8", "#e3a64e", "#f2d36b" };

        static string RankColour(string rank) => RankColours[Rules.RankIndex(rank)];

        static Label RankBadge(VisualElement parent, string rank)
        {
            var l = Add(parent, new Label(string.IsNullOrEmpty(rank) ? "F" : rank), "rank-badge");
            ColorUtility.TryParseHtmlString(RankColour(rank), out var c);
            l.style.color = c;
            l.style.borderTopColor = l.style.borderBottomColor = l.style.borderLeftColor = l.style.borderRightColor = c;
            l.tooltip = Rules.RankName(rank) + " rank";
            return l;
        }

        // ------------------------------------------------------------------ acting

        void Command(ChoiceView cv)
        {
            if (cv == null || !cv.enabled || busy || timing != null) return;
            if (engine.NeedsTiming(cv.def)) StartTiming(cv);
            else Resolve(cv, Engine.TimingGood);
        }

        void StartTiming(ChoiceView cv)
        {
            timing = cv;
            var (good, _) = Rules.TimingZone(engine.S, engine.data);
            zoneCentre = Random.Range(0.35f + good / 2, 0.92f - good / 2);
            timingStart = Time.realtimeSinceStartup;
            Render();
            timingTick = root.schedule.Execute(() =>
            {
                if (timing == null) return;
                if (TimingPos() >= 1f) StopTiming();
                else MoveMarker();
            }).Every(10);
        }

        float TimingPos() => (Time.realtimeSinceStartup - timingStart) / TimingSeconds;

        void MoveMarker()
        {
            if (timingMarker != null) timingMarker.style.left = Length.Percent(Mathf.Clamp01(TimingPos()) * 100f);
        }

        /// <summary>The player pressed (or the marker ran off the end). Work out how well they timed it.</summary>
        void StopTiming()
        {
            if (timing == null) return;
            timingTick?.Pause();
            float pos = TimingPos();
            var (good, perfect) = Rules.TimingZone(engine.S, engine.data);
            float off = Mathf.Abs(pos - zoneCentre);
            int quality = pos >= 1f ? Engine.TimingMiss : off <= perfect / 2 ? Engine.TimingPerfect : off <= good / 2 ? Engine.TimingGood : Engine.TimingMiss;
            var cv = timing;
            timing = null;
            Resolve(cv, quality);
            Popup(playerCard, quality == Engine.TimingPerfect ? "PERFECT" : quality == Engine.TimingGood ? "GOOD" : "MISTIMED",
                  quality == Engine.TimingPerfect ? "pop-perfect" : quality == Engine.TimingGood ? "pop-good" : "pop-miss", -60);
        }

        void Resolve(ChoiceView cv, int quality)
        {
            var s = engine.S;
            before = new[] { s.enemyHp, s.hp, s.stamina, s.energy };
            engine.Act(cv.def, quality);
            battleMenu = "";
            var events = engine.Events.ToList();
            outro = !engine.S.inCombat;
            if (outro) before = null;   // the fight is over; the screen below shows its end state
            busy = true;
            SaveNow();
            Render();
            PlayEvents(events);
        }

        void PlayEvents(List<BattleEvent> events)
        {
            int delay = 120;
            foreach (var ev in events)
            {
                var e = ev;
                root.schedule.Execute(() =>
                {
                    int fi = e.target.StartsWith("follower") && int.TryParse(e.target.Substring(8), out var n) ? n : -1;
                    var fSpot = fi >= 0 && fi < followerSpots.Count ? followerSpots[fi] : null;
                    var fPic = fi >= 0 && fi < followerPics.Count ? followerPics[fi] : null;
                    bool isF = e.target.StartsWith("follower");
                    var card = e.target == "enemy" ? enemyCard : isF ? fSpot : playerCard;
                    if (card == null) return;
                    var spot = e.target == "enemy" ? enemySpot : isF ? fSpot : playerSpot;
                    string cls = e.kind == "crit" ? "pop-crit" : e.kind == "glance" ? "pop-glance" : e.kind == "heal" ? "pop-heal"
                               : e.kind == "parry" ? "pop-perfect" : e.kind == "guard" || e.kind == "flee" ? "pop-good" : "pop-hit";
                    Popup(spot ?? card, e.text, cls, 0);
                    if (e.kind == "hit" || e.kind == "crit" || e.kind == "glance")
                    {
                        Flash(card);
                        var pic = e.target == "enemy" ? enemyPic : isF ? fPic : playerPic;
                        HitFlash(pic);
                        Shake(pic, e.kind == "crit" ? 12f : 6f);
                        Shake(e.kind == "crit" ? (e.target == "player" ? arena : card) : card, e.kind == "crit" ? 14f : e.kind == "glance" ? 4f : 8f);
                    }
                }).StartingIn(delay);
                delay += 420;
            }
            root.schedule.Execute(() =>
            {
                busy = false;
                if (outro)
                {
                    root.schedule.Execute(() => { outro = false; Render(); }).StartingIn(700);
                }
                else Render();
            }).StartingIn(delay + 150);
        }

        // ------------------------------------------------------------------ feedback

        static void Popup(VisualElement card, string text, string cls, float yOffset)
        {
            if (card == null || string.IsNullOrEmpty(text)) return;
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("pop");
            l.AddToClassList(cls);
            card.Add(l);
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = l.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - start) / 0.9f;
                if (t >= 1f) { tick?.Pause(); l.RemoveFromHierarchy(); return; }
                l.style.translate = new StyleTranslate(new Translate(new Length(0), new Length(yOffset - 70f * t), 0));
                l.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }).Every(16);
        }

        static void Flash(VisualElement card)
        {
            card.AddToClassList("flash");
            card.schedule.Execute(() => card.RemoveFromClassList("flash")).StartingIn(140);
        }

        static void Shake(VisualElement target, float strength)
        {
            if (target == null) return;
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = target.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - start) / 0.28f;
                if (t >= 1f)
                {
                    tick?.Pause();
                    target.style.translate = new StyleTranslate(new Translate(new Length(0), new Length(0), 0));
                    return;
                }
                float x = Mathf.Sin(t * 40f) * strength * (1f - t);
                target.style.translate = new StyleTranslate(new Translate(new Length(x), new Length(0), 0));
            }).Every(16);
        }

        // ------------------------------------------------------------------ keys

        /// <summary>Keys during a fight: Space strikes on the timing bar, 1-9 pick commands, Esc goes back.</summary>
        bool BattleKey(KeyDownEvent e)
        {
            if (timing != null)
            {
                if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) StopTiming();
                return true;
            }
            if (busy || outro) return true;
            if ((e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Backspace) && battleMenu != "") { battleMenu = ""; Render(); return true; }
            int n = e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9 ? e.keyCode - KeyCode.Alpha1
                  : e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad9 ? e.keyCode - KeyCode.Keypad1
                  : -1;
            if (n < 0 || n >= battleButtons.Count) return true;
            if (battleMenu == "")
            {
                var (id, _) = BattleCommands[n];
                if (id == "skill" || id == "item")
                {
                    if (engine.Choices().Any(c => Engine.CommandOf(c.def) == id && c.enabled)) { battleMenu = id; Render(); }
                    return true;
                }
            }
            Command(battleButtons[n]);
            return true;
        }
    }
}
