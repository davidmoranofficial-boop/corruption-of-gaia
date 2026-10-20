using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The battle stage (David, 20 Oct: "better animations and much better models"). Everything on it shares one pixel
    /// grid: the painted backdrop is 360 x 136 pixels and is blown up by a whole number to cover the arena, and the
    /// fighters, shadows and drifting clouds are drawn at that same scale, so nothing looks finer or coarser than
    /// anything else. Feet stand on the line y = 124.
    ///
    /// The beasts are sheets of eleven frames (Tools/Art/beasts2.py): idle 0-3, tell 0-1, attack 0-2, hurt, dead.
    /// They breathe while they wait, gather themselves when they're about to strike (so you can read the tell),
    /// lunge across at whoever they hit, flinch when hit, and drop when they die. You and your party are paper dolls;
    /// they breathe, dash in to strike, get knocked back, hop aside from a dodge, brace for a parry and run to flee.
    /// </summary>
    public partial class GameUI
    {
        const int StageW = 360, StageH = 136, FeetY = 124, BeastFrames = 11;
        const float PlayerX = 100f, EnemyX = 262f;

        /// <summary>Something on the stage, placed in stage pixels, moved by its current animation.</summary>
        class Actor
        {
            public VisualElement el, shadow;
            public float x, y, w, h;               // top left and size, in stage pixels
            public string anim = "";               // what it's doing: "" (standing), dash, hurt, dodge, guard, flee, lunge, step, down
            public float t0, reach;                // when it started, and how far a dash or lunge goes
            public float phase;                    // so everyone doesn't breathe in step
            public bool down;
            public float shadowX, shadowY, shadowW;  // the shadow under its feet (stage pixels)
            public VisualElement tag;                // where its damage numbers pop up (not clipped like a beast's frame)
        }

        VisualElement stage, clouds, enemySheet;
        Actor youActor, foeActor;
        readonly List<Actor> partyActors = new List<Actor>();
        readonly List<Actor> stageActors = new List<Actor>();
        int stageK = 5;
        float foeFrameW, foeFrameH;
        bool foeSheet;
        string foeMode = "idle";                   // idle, tell, attack, hurt, dead
        float foeModeAt;
        IVisualElementScheduledItem stageTick;

        static float StageNow => Time.realtimeSinceStartup;

        void BuildStage(VisualElement arena, EnemyDef e)
        {
            var s = engine.S;
            stageActors.Clear();
            partyActors.Clear();
            stage = Add(arena, new VisualElement { pickingMode = PickingMode.Ignore }, "stage");
            string bd = BackdropFor();
            var backdrop = Sprite("Backdrops/" + bd) ?? Sprite("Backdrops/" + bd.Replace("_night", "")) ?? Sprite("Backdrops/coast");
            if (backdrop != null) stage.style.backgroundImage = new StyleBackground(backdrop);
            var cl = Sprite("Backdrops/" + bd + "_clouds");
            clouds = null;
            if (cl != null)
            {
                clouds = Add(stage, new VisualElement { pickingMode = PickingMode.Ignore }, "stage-clouds");
                clouds.style.backgroundImage = new StyleBackground(cl);
            }

            // your party behind you, back to front
            for (int pi = s.party.Count - 1; pi >= 0; pi--)
            {
                var m = s.party[pi];
                var fol = engine.data.Follower(m.id);
                if (fol == null) { partyActors.Insert(0, null); continue; }
                var a = NewActor(stage, PlayerX - 30 - pi * 22, FeetY - 6 - pi * 5, DollW, DollH, 44);
                a.phase = 0.37f * (pi + 1);
                var doll = Doll(a.el, 1, fol.figure, fol.skin, fol.hair, fol.worn.Select(g => (g, engine.data.Gear(g)?.slot ?? "body")), fol.hairStyle, fol.beard);
                doll.style.width = Length.Percent(100); doll.style.height = Length.Percent(100);
                if (m.hp <= 0) { a.down = true; a.anim = "down"; }
                partyActors.Insert(0, a);
            }

            youActor = NewActor(stage, PlayerX, FeetY, DollW, DollH, 44);
            var you = PlayerDoll(youActor.el, 1);
            you.style.width = Length.Percent(100); you.style.height = Length.Percent(100);
            if (s.hp <= 0) { youActor.down = true; youActor.anim = "down"; }

            // the enemy: an animated sheet if it has one, else its single picture
            var tex = Sprite("Enemies/" + (string.IsNullOrEmpty(e.art) ? e.id : e.art));
            foeSheet = tex != null && tex.width >= tex.height * 4;
            foeFrameW = tex == null ? 48 : foeSheet ? tex.width / (float)BeastFrames : tex.width;
            foeFrameH = tex == null ? 48 : tex.height;
            // the beasts face left and stand a little forward of the middle of their frame
            foeActor = NewActor(stage, EnemyX - foeFrameW * 0.62f + foeFrameW / 2, FeetY, foeFrameW, foeFrameH, foeSheet ? foeFrameH - 3 : foeFrameH - 1);
            foeActor.shadowW = Mathf.Min(foeFrameW * 0.6f, 64);
            foeActor.shadowX = EnemyX;
            foeActor.el.style.overflow = Overflow.Hidden;
            enemySheet = Add(foeActor.el, new VisualElement { pickingMode = PickingMode.Ignore }, "enemy-sprite");
            enemySheet.style.position = Position.Absolute;
            if (tex != null) enemySheet.style.backgroundImage = new StyleBackground(tex);
            else Add(foeActor.el, new Label(e.name.Substring(0, 1)), "portrait-letter");
            if (s.enemyHp <= 0) { foeMode = "dead"; foeModeAt = StageNow - 5; }
            else if (foeMode == "dead" || foeMode == "attack" || foeMode == "hurt") foeMode = "idle";

            // at night the fighters take the moonlight too, so they sit in the painting instead of glowing on it
            if (bd.EndsWith("_night"))
            {
                ColorUtility.TryParseHtmlString("#8e9cc4", out var moon);
                stage.Query(className: "doll-layer").ForEach(l => l.style.unityBackgroundImageTintColor = moon);
                enemySheet.style.unityBackgroundImageTintColor = moon;
            }

            arena.RegisterCallback<GeometryChangedEvent>(_ => LayoutStage(arena));
            LayoutStage(arena);
            stageTick?.Pause();
            stageTick = root.schedule.Execute(StageTick).Every(16);
        }

        Actor NewActor(VisualElement parent, float feetX, float feetY, float w, float h, float feetRow)
        {
            var a = new Actor { w = w, h = h, x = feetX - w / 2, y = feetY - feetRow };
            a.shadow = Add(parent, new VisualElement { pickingMode = PickingMode.Ignore }, "stage-shadow");
            var tex = Sprite("Ui/shadow");
            if (tex != null) a.shadow.style.backgroundImage = new StyleBackground(tex);
            a.shadowX = feetX;
            a.shadowY = feetY;
            a.el = Add(parent, new VisualElement { pickingMode = PickingMode.Ignore }, "stage-actor");
            a.tag = Add(parent, new VisualElement { pickingMode = PickingMode.Ignore }, "stage-tag");
            stageActors.Add(a);
            return a;
        }

        /// <summary>Picks the whole-number scale that covers the arena, then places everything at it.</summary>
        void LayoutStage(VisualElement arena)
        {
            if (stage == null) return;
            float aw = arena.layout.width, ah = arena.layout.height;
            if (float.IsNaN(aw) || aw < 100) aw = TravelWidth();
            if (float.IsNaN(ah) || ah < 100) ah = Mathf.Max(300, BattleHeight() - 300);
            int k = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(aw / StageW, ah / StageH)));
            stageK = k;
            stage.style.width = StageW * k;
            stage.style.height = StageH * k;
            stage.style.left = Mathf.Round((aw - StageW * k) / 2);
            stage.style.top = Mathf.Round((ah - StageH * k) * 0.55f);      // crop more sky than ground
            if (clouds != null) { clouds.style.width = StageW * 2 * k; clouds.style.height = StageH * k; }
            foreach (var a in stageActors)
            {
                a.el.style.left = a.x * k;
                a.el.style.top = a.y * k;
                a.el.style.width = a.w * k;
                a.el.style.height = a.h * k;
                float tw = Mathf.Min(a.w, 60);
                a.tag.style.left = (a.shadowX - tw / 2) * k;
                a.tag.style.top = (a.shadowY - Mathf.Min(a.h, 52)) * k;
                a.tag.style.width = tw * k;
                a.tag.style.height = 20 * k;
                float sw = a.shadowW > 0 ? a.shadowW : 30;
                a.shadow.style.left = (a.shadowX - sw / 2) * k;
                a.shadow.style.top = (a.shadowY - 3) * k;
                a.shadow.style.width = sw * k;
                a.shadow.style.height = 6 * k;
            }
            if (enemySheet != null)
            {
                enemySheet.style.width = (foeSheet ? foeFrameW * BeastFrames : foeFrameW) * k;
                enemySheet.style.height = foeFrameH * k;
            }
            StageTick();
        }

        // ------------------------------------------------------------------ the tick

        void StageTick()
        {
            if (stage == null || stage.panel == null) { stageTick?.Pause(); return; }
            int k = stageK;
            float now = StageNow;
            if (clouds != null) clouds.style.left = -Mathf.Round(now * 2.5f % StageW) * k;

            // the beast's frame
            var s = engine.S;
            if (foeSheet && enemySheet != null)
            {
                // while it waits, its body shows what it's about to do: crouched for a big one (or a feint),
                // half-crouched for a quick snap, head thrown back for a howl, easy for holding back
                string intent = s.inCombat && engine.Intent != null && !busy ? engine.Intent.KindOf() : "";
                string baseMode = s.enemyHp <= 0 && !s.inCombat ? "dead"
                                : intent == "heavy" || intent == "feint" ? "tell" : intent == "strike" ? "ready" : intent == "howl" ? "howl" : "idle";
                float t = now - foeModeAt;
                if (foeMode == "attack" && t > 0.52f) foeMode = baseMode;
                if (foeMode == "hurt" && t > 0.32f) foeMode = baseMode;
                if (foeMode != "attack" && foeMode != "hurt" && foeMode != "dead") foeMode = baseMode;
                int frame = foeMode switch
                {
                    "tell" => 4 + (int)(now / 0.32f) % 2,
                    "ready" => (int)(now / 0.4f) % 2 == 0 ? 4 : 2,
                    "howl" => 9,
                    "attack" => t < 0.12f ? 6 : t < 0.34f ? 7 : 8,
                    "hurt" => 9,
                    "dead" => 10,
                    _ => (int)(now / 0.24f) % 4,
                };
                enemySheet.style.left = -frame * foeFrameW * k;
            }

            foreach (var a in stageActors) Move(a, now, k);
        }

        void Move(Actor a, float now, int k)
        {
            float t = now - a.t0, dx = 0, dy = 0, turn = 0;
            bool foe = a == foeActor;
            switch (a.anim)
            {
                case "dash":     // run in, strike, come back
                    dx = t < 0.14f ? a.reach * Ease(t / 0.14f) : t < 0.26f ? a.reach : t < 0.48f ? a.reach * (1 - Ease((t - 0.26f) / 0.22f)) : 0;
                    dy = t < 0.14f ? -2 * Mathf.Sin(t / 0.14f * Mathf.PI) : 0;
                    turn = t < 0.26f ? 6 : 0;
                    if (t > 0.48f) a.anim = "";
                    break;
                case "lunge":    // the beast springs across (its frames do the rest)
                    dx = t < 0.2f ? -a.reach * Ease(t / 0.2f) : t < 0.32f ? -a.reach : t < 0.52f ? -a.reach * (1 - Ease((t - 0.32f) / 0.2f)) : 0;
                    if (t > 0.52f) a.anim = "";
                    break;
                case "step":     // a companion steps up to loose or strike
                    dx = t < 0.12f ? 6 * Ease(t / 0.12f) : t < 0.3f ? 6 : t < 0.45f ? 6 * (1 - Ease((t - 0.3f) / 0.15f)) : 0;
                    if (t > 0.45f) a.anim = "";
                    break;
                case "hurt":     // knocked back, then steadies
                    float push = foe ? 6 : -6;
                    dx = t < 0.08f ? push * Ease(t / 0.08f) : t < 0.34f ? push * (1 - Ease((t - 0.08f) / 0.26f)) : 0;
                    turn = t < 0.2f ? (foe ? 4 : -7) : 0;
                    if (t > 0.34f) a.anim = "";
                    break;
                case "dodge":    // a hop back out of reach
                    dx = t < 0.16f ? -18 * Ease(t / 0.16f) : t < 0.3f ? -18 : t < 0.5f ? -18 * (1 - Ease((t - 0.3f) / 0.2f)) : 0;
                    dy = t < 0.3f ? -7 * Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI) : 0;
                    if (t > 0.5f) a.anim = "";
                    break;
                case "guard":    // braced: a short skid back
                    dx = t < 0.06f ? -4 * Ease(t / 0.06f) : t < 0.3f ? -4 * (1 - Ease((t - 0.06f) / 0.24f)) : 0;
                    turn = t < 0.2f ? -4 : 0;
                    if (t > 0.3f) a.anim = "";
                    break;
                case "flee":
                    dx = -260 * Ease(Mathf.Clamp01(t / 0.7f));
                    break;
                case "down":
                    turn = foe ? 0 : -78;
                    dy = foe ? 0 : a.h * 0.32f;
                    break;
                default:         // standing: breathing
                    if (!foe && !a.down) dy = Mathf.Sin((now + a.phase) * 3.1f) > 0.2f ? 1 : 0;
                    break;
            }
            a.el.style.translate = new StyleTranslate(new Translate(new Length(Mathf.Round(dx) * k), new Length(Mathf.Round(dy) * k), 0));
            a.el.style.rotate = new StyleRotate(new Rotate(new Angle(turn, AngleUnit.Degree)));
            a.shadow.style.translate = new StyleTranslate(new Translate(new Length(Mathf.Round(dx) * k), 0, 0));
            a.el.style.opacity = a == foeActor && foeMode == "dead" ? Mathf.Clamp(1.4f - (now - foeModeAt) * 0.5f, 0.35f, 1f) : 1f;
        }

        static float Ease(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t); }

        void Play(Actor a, string anim, float reach = 0)
        {
            if (a == null || a.down) return;
            a.anim = anim;
            a.t0 = StageNow;
            a.reach = reach;
        }

        void FoeDoes(string mode)
        {
            foeMode = mode;
            foeModeAt = StageNow;
        }

        Actor ActorFor(string who)
        {
            if (who == "enemy") return foeActor;
            if (who == "player") return youActor;
            if (who != null && who.StartsWith("follower") && int.TryParse(who.Substring(8), out var i) && i >= 0 && i < partyActors.Count) return partyActors[i];
            return null;
        }

        /// <summary>How far the beast springs to reach someone: its jaws (a third of the way into its frame, and the
        /// frames already lunge 16 pixels) to just in front of them.</summary>
        float LungeTo(Actor target)
        {
            if (target == null) return 0;
            float jaws = EnemyX - foeFrameW * 0.31f - 16;
            float front = target.x + target.w / 2 + 10;
            return Mathf.Max(0, jaws - front);
        }

        /// <summary>How far you dash to strike: to just short of the beast's front.</summary>
        float DashReach() => Mathf.Max(0, EnemyX - foeFrameW * 0.28f - (youActor.x + youActor.w / 2) - 14);
    }
}
