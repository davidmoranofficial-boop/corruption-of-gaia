using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The battle screen: enemy top right, you bottom left, the enemy's tell in the middle, and a command menu
    /// (Attack, Skills, Items, Defend, Dodge, Flee). Defending and dodging use a timing bar: press Space in the bright zone.
    /// Reading the tell is the skill: block quick blows, dodge big ones (it's left open), don't dodge a feint,
    /// hit it while it howls, and catch your breath while it holds back.
    /// </summary>
    public partial class GameUI
    {
        const float TimingSeconds = 1.05f;   // how long the marker takes to cross the bar

        static readonly (string id, string name)[] BattleCommands =
        {
            ("attack", "Attack"), ("skill", "Skills"), ("item", "Items"), ("defend", "Defend"), ("dodge", "Dodge"), ("flee", "Flee"),
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

            // The stage: the painted backdrop, you and your party on the left, the beast on the right (GameUI.Stage.cs)
            arena = Add(battle, new VisualElement(), "arena");
            BuildStage(arena, e);
            playerPic = youActor.el;
            enemyPic = foeActor.el;
            playerSpot = youActor.el;
            enemySpot = foeActor.el;
            followerSpots.Clear();
            followerPics.Clear();
            foreach (var a in partyActors) { followerSpots.Add(a?.el); followerPics.Add(a?.el); }

            // You: top left
            playerCard = Add(arena, new VisualElement(), "fighter-card");
            playerCard.AddToClassList("card-player");
            Add(playerCard, new Label(s.name), "fighter-name");
            Bar(playerCard, "HEALTH", s.hp, Rules.MaxHp(s), "#c8573f", 1, false);
            Bar(playerCard, "MANA", s.mana, Rules.MaxMana(s), "#4f86d8", 4, false);
            Bar(playerCard, "STAMINA", s.stamina, Rules.MaxStamina(s, engine.data), "#7fae6a", 2, false);
            Bar(playerCard, "ENERGY", s.energy, 100, "#9d8fd0", 3, false);
            // your party, in brief, under your own bars
            foreach (var m in s.party)
            {
                var fol = engine.data.Follower(m.id);
                if (fol == null) continue;
                var row = Add(playerCard, new VisualElement(), "fighter-ally");
                Add(row, new Label(fol.name + (m.hp <= 0 ? "  ·  DOWN" : "")), "fighter-ally-name");
                var tr = Add(row, new VisualElement(), "need-track");
                tr.AddToClassList("fighter-ally-track");
                var fl = Add(tr, new VisualElement(), "need-fill");
                fl.style.width = Length.Percent(Mathf.Clamp01(m.hp / Rules.MemberMaxHp(m, fol)) * 100f);
                ColorUtility.TryParseHtmlString("#c8573f", out var ac);
                fl.style.backgroundColor = ac;
            }
            var weapon = s.InSlot("weapon");
            Add(playerCard, new Label(weapon != null ? weapon.name + "  (" + weapon.rank + ")" : "Bare hands"), "fighter-sub");
            if (s.inCombat && engine.Berserk) StatusChip(playerCard, "BERSERK", "chip-bad", "Below 30% health: your blows land 30% harder.");
            if (s.inCombat && Rules.Winded(s, engine.data)) StatusChip(playerCard, "WINDED", "chip-bad", "Out of breath: you can't dodge or land crits, and blows hurt more. Defend to get it back.");

            // The enemy: top right, with what it's about to do, so you can choose to attack or defend
            enemyCard = Add(arena, new VisualElement(), "fighter-card");
            enemyCard.AddToClassList("card-enemy");
            var nameRow = Add(enemyCard, new VisualElement(), "fighter-name-row");
            Add(nameRow, new Label(e.name), "fighter-name");
            RankBadge(nameRow, e.rank);
            if (e.boss) Add(enemyCard, new Label("BOSS"), "boss-tag");
            Bar(enemyCard, "HEALTH", Mathf.Max(0, s.enemyHp), e.hp, "#c8573f", 0, true);
            if (s.inCombat && (s.enemyOpen > 0 || s.enemyRage > 0))
            {
                var chips = Add(enemyCard, new VisualElement(), "battle-chips");
                if (s.enemyOpen > 0) StatusChip(chips, "OPEN", "chip-good", "Off balance from your dodge: your next blow is a sure critical hit, and it can't strike first.");
                if (s.enemyRage > 0) StatusChip(chips, s.enemyRage > 1 ? "ENRAGED x" + s.enemyRage : "ENRAGED", "chip-bad", "Each rage makes its blows 30% harder.");
            }
            if (s.inCombat && engine.Intent != null)
            {
                Add(enemyCard, new Label(engine.Intent.tell), "intent-text");
                bool itFirst = engine.Intent.Attacks() && !engine.PlayerActsFirst && s.enemyOpen == 0;
                Add(enemyCard, new Label(itFirst ? "It's quicker: it acts first, unless you defend or dodge." : "You act first."), "intent-order");
            }

            // The last few things that happened
            var log = Add(battle, new VisualElement(), "battle-log");
            var lines = s.battleLog.Skip(Mathf.Max(0, s.battleLog.Count - 3)).ToList();
            if (lines.Count == 0)
            {
                Add(log, new Label(engine.Fill(e.desc)), "log-line");
                Add(log, new Label("Watch what it's about to do. Block quick blows, dodge big ones, and don't fall for a feint."), "log-line-new");
            }
            for (int i = 0; i < lines.Count; i++)
                Add(log, new Label(lines[i]), i == lines.Count - 1 ? "log-line-new" : "log-line");

            // Timing bar, or the commands
            if (timing != null) RenderTimingBar(battle);
            else if (s.inCombat && !busy && !outro) RenderCommands(battle);
            else Add(battle, new VisualElement(), "commands-placeholder");

            before = null;
        }

        /// <summary>Which painted backdrop suits where you are: by the sea, in the woods, up in the hills, at the village; darker at night.</summary>
        string BackdropFor()
        {
            var s = engine.S;
            var a = engine.Area;
            string kind = "coast";
            if (a != null)
            {
                if (a.id == "skelmed") kind = "village";
                else if (a.id.Contains("hills")) kind = "hills";
                else if (a.id == "coast_road")
                {
                    // on the coast road it depends where you are: the woods to the east, the road, or the shore
                    int n = 0, sea = 0;
                    for (int dy = -3; dy <= 3; dy++)
                        for (int dx = -3; dx <= 3; dx++)
                        {
                            char c = engine.TileAt(s.ax + dx, s.ay + dy);
                            if (c == 'T') n++;
                            if (c == '~' || c == ',') sea++;
                        }
                    kind = n >= 12 ? "forest" : sea >= 6 ? "coast" : "road";
                }
            }
            return engine.Check("night") && (kind == "coast" || kind == "forest" || kind == "hills") ? kind + "_night" : kind;
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
                               : first == null ? "" : !first.enabled ? first.reason
                               : id == "dodge" && Rules.Winded(engine.S, engine.data) ? "Too winded to dodge"
                               : first.cost;
                    bool on = group.Any(c => c.enabled);
                    var b = CommandButton(row, i + 1, name, sub, on, () =>
                    {
                        if (id == "skill" || id == "item") { battleMenu = id; Render(); }
                        else Command(first);
                    });
                    battleButtons.Add(group.Count == 1 || id == "attack" || id == "defend" || id == "dodge" || id == "flee" ? first : null);
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
            Add(box, new Label(defend ? "BRACE  ·  press SPACE in the bright zone to parry" : "DODGE  ·  press SPACE in the bright zone"), "timing-head");
            var track = Add(box, new VisualElement(), "timing-track");
            track.RegisterCallback<PointerDownEvent>(_ => StopTiming());
            var (good, perfect) = Zone();
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

        /// <summary>A small coloured tag on a fighter's card: OPEN, ENRAGED, WINDED.</summary>
        static void StatusChip(VisualElement parent, string text, string cls, string tip)
        {
            var l = Add(parent, new Label(text), "battle-chip");
            l.AddToClassList(cls);
            l.tooltip = tip;
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
            var (good, _) = Zone();
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

        /// <summary>The bright zone. Dodging is harder to time than bracing, so its zone is narrower.</summary>
        (float good, float perfect) Zone()
        {
            var (good, perfect) = Rules.TimingZone(engine.S, engine.data);
            bool dodge = timing != null && timing.def.effects[0] == "fight:dodge";
            float k = Rules.HasPerk(engine.S, engine.data, "nimble") ? 1.15f : 0.75f;   // Nimble: dodging comes easily
            return dodge ? (good * k, perfect * (k + 0.05f)) : (good, perfect);
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
            var (good, perfect) = Zone();
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
            before = new[] { s.enemyHp, s.hp, s.stamina, s.energy, s.mana };
            lastAct = cv.def.effects.Count > 0 ? cv.def.effects[0] : "";
            foeArt = engine.data.Enemy(s.enemy) is var ed && ed != null ? (string.IsNullOrEmpty(ed.art) ? ed.id : ed.art) : "";
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

        string lastAct = "", foeArt = "";

        /// <summary>The sound for one thing that happens in a fight: your blow (or spell) landing, the enemy's bite, a parry...</summary>
        void BattleSound(BattleEvent e)
        {
            bool hitKind = e.kind == "hit" || e.kind == "crit" || e.kind == "glance";
            if (e.target == "enemy" && hitKind)
            {
                bool fire = lastAct == "fight:kindle" || lastAct == "fight:move:flare";
                if (lastAct == "fight:move:flare") GameAudio.Play("spell_flare");
                else if (fire) GameAudio.Play("spell_kindle");
                else GameAudio.Play("swing", 0.8f);
                GameAudio.Play(e.kind == "crit" ? "hit_crit" : e.kind == "glance" ? "hit_glance" : "hit", 1f, 0.05f, fire ? 0.15f : 0.08f);
                if (e.kind != "glance") GameAudio.Play(foeArt == "boar" ? "boar_squeal" : "beast_hurt", 0.7f, 0.08f, 0.2f);
            }
            else if (hitKind)
            {
                // the enemy attacks you or one of your party
                GameAudio.Play(foeArt == "boar" ? "boar_charge" : "wolf_snarl", 0.9f);
                GameAudio.Play(e.target == "player" ? "player_hurt" : "hit", 1f, 0.05f, 0.12f);
            }
            else if (e.kind == "parry") GameAudio.Play("parry");
            else if (e.kind == "dodge") GameAudio.Play("swing", 0.7f, 0.1f);
            else if (e.kind == "open") GameAudio.Play("hit_glance", 0.6f);
            else if (e.kind == "rage") GameAudio.Play(foeArt.StartsWith("wolf") ? "wolf_howl" : foeArt == "boar" ? "boar_squeal" : "wolf_growl", 0.9f);
            else if (e.kind == "guard") GameAudio.Play("block");
            else if (e.kind == "flee") GameAudio.Play("flee");
            else if (e.kind == "heal" && !lastAct.StartsWith("fight:eat") && lastAct != "fight:drink") GameAudio.Play("heal");
        }

        /// <summary>
        /// Plays what just happened, one thing after another: whoever acts moves first (you dash in, the beast springs,
        /// a companion steps up), and the blow lands as they arrive: the number, the flash, the shake and the flinch.
        /// </summary>
        void PlayEvents(List<BattleEvent> events)
        {
            float delay = 0.12f;
            foreach (var ev in events)
            {
                var e = ev;
                var by = ActorFor(e.from);
                float lead = 0f, after = 0.42f;
                if (by == foeActor) { lead = 0.2f; after = 0.5f; }
                else if (by == youActor) { lead = 0.15f; after = 0.45f; }
                else if (by != null) { lead = 0.13f; after = 0.4f; }
                float start = delay;
                if (by != null)
                    root.schedule.Execute(() =>
                    {
                        if (by == foeActor) { FoeDoes("attack"); Play(foeActor, "lunge", LungeTo(ActorFor(e.target))); }
                        else if (by == youActor) Play(youActor, "dash", DashReach());
                        else Play(by, "step");
                    }).StartingIn(Mathf.RoundToInt(start * 1000));
                root.schedule.Execute(() => Land(e)).StartingIn(Mathf.RoundToInt((start + lead) * 1000));
                delay = start + lead + after;
            }
            root.schedule.Execute(() =>
            {
                busy = false;
                if (outro)
                {
                    if (engine.S.enemyHp <= 0 && engine.S.hp > 0)
                    {
                        FoeDoes("dead");
                        GameAudio.Play("enemy_die");
                        GameAudio.Play("victory", 0.9f, 0f, 0.5f);
                    }
                    root.schedule.Execute(() => { outro = false; Render(); }).StartingIn(1100);
                }
                else Render();
            }).StartingIn(Mathf.RoundToInt((delay + 0.15f) * 1000));
        }

        /// <summary>One blow (or dodge, or parry) arriving.</summary>
        void Land(BattleEvent e)
        {
            var target = ActorFor(e.target);
            int fi = e.target.StartsWith("follower") && int.TryParse(e.target.Substring(8), out var n) ? n : -1;
            bool isF = fi >= 0;
            var card = e.target == "enemy" ? enemyCard : isF ? (fi < followerSpots.Count ? followerSpots[fi] : null) : playerCard;
            var pic = target?.el;
            string cls = e.kind == "crit" ? "pop-crit" : e.kind == "glance" ? "pop-glance" : e.kind == "heal" ? "pop-heal"
                       : e.kind == "parry" || e.kind == "dodge" || e.kind == "open" ? "pop-perfect" : e.kind == "rage" ? "pop-crit"
                       : e.kind == "guard" || e.kind == "flee" ? "pop-good" : "pop-hit";
            Popup(target?.tag ?? card, e.text, cls, 0);
            BattleSound(e);
            BattleEffect(e, pic, arena);
            bool hit = e.kind == "hit" || e.kind == "crit" || e.kind == "glance";
            if (hit && !(e.kind == "glance" && e.from == "enemy"))
            {
                if (card != null && card != pic) Flash(card);
                HitFlash(target == foeActor ? enemySheet : pic);
                if (target == foeActor) FoeDoes("hurt");
                Play(target, "hurt");
                if (e.kind == "crit") Shake(stage, target == youActor ? 10f : 7f);
            }
            else if (e.kind == "glance") Play(target, "guard");
            else if (e.kind == "dodge") Play(target, "dodge");
            else if (e.kind == "parry") Play(target, "guard");
            else if (e.kind == "flee" && e.text == "Escaped") Play(youActor, "flee");
            else if (e.kind == "rage") { FoeDoes("tell"); Shake(pic, 5f); }
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
