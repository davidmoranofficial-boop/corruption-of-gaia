using System.Collections.Generic;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The forge game, in three steps:
    /// 1. Heat: hold Space to pump the bellows and keep the needle inside the gold band. The band shrinks as you go.
    /// 2. Hammer: dots appear on the piece one at a time. A ring closes in on each; click the dot as the ring meets the gold ring.
    /// 3. Quench: hold Space to plunge the piece into the water, and let go when its colour reaches the mark.
    /// Each step scores 0 to 1, and the engine turns the three scores into a quality and a rank.
    /// </summary>
    public partial class GameUI
    {
        // Heat: the gold band starts this wide and shrinks to BandEnd by the end of the step.
        const float HeatSeconds = 8f, HeatGrace = 1.5f, BandCentre = 69f, BandStart = 18f, BandEnd = 7f;
        // Hammer: sizes in pixels.
        const int Strikes = 6;
        const float DotSize = 30f, GoldSize = 46f, RingStart = 170f, PerfectSlack = 6f, FirstRingSeconds = 1.25f, DotGap = 0.45f;
        // Quench
        const float QuenchSeconds = 2.6f;

        int forgeStage = -1;
        float lastBellows;            // 0 heat, 1 hammer, 2 quench, 3 done
        bool stageRunning;
        readonly float[] forgeScores = new float[3];
        float stageStart, lastTick;
        bool holding, keyHeld;

        // Heat
        float heat, inBand;
        // Hammer
        int strike;
        float hammerTotal, ringSeconds, dotStart, nextDotAt;
        bool dotLive;
        Vector2 dotPos;                 // percent of the anvil area
        int lastSpot = -1;
        // Quench
        float quenchAt, quenchTarget = 0.62f;
        bool dipped, dipByPointer;

        IVisualElementScheduledItem forgeTick;
        VisualElement metal, needle, timerFill, forgeMarker, forgePanel, heatBand;
        VisualElement anvilArea, dotEl, ringEl, goldEl, quenchBlade, steam;
        Label stageHint;

        static readonly string[] StageNames = { "HEAT", "HAMMER", "QUENCH" };

        void RenderForge()
        {
            var s = engine.S;
            if (forgeStage < 0) { forgeStage = 0; stageRunning = false; forgeScores[0] = forgeScores[1] = forgeScores[2] = 0; }
            var g = engine.OnAnvil;
            main.Clear();
            var wrap = Add(main, new VisualElement(), "forge");

            Add(wrap, new Label((engine.Scene(s.forgeNext)?.place ?? "").ToUpperInvariant() + "  ·  THE FORGE"), "place");
            Add(wrap, new Label("At the anvil: " + g.name.ToLowerInvariant()), "title");
            var mat = engine.ForgeMaterial;
            if (mat != null) Add(wrap, new Label(mat.desc.Replace("{part}", string.IsNullOrEmpty(g.part) ? "metal" : g.part)), "para");
            if (!string.IsNullOrEmpty(s.result) && forgeStage == 0 && !stageRunning) Add(wrap, new Label(s.result), "result");

            // The three steps, with scores for the ones done
            var steps = Add(wrap, new VisualElement(), "forge-steps");
            for (int i = 0; i < 3; i++)
            {
                var pill = Add(steps, new VisualElement(), "forge-step");
                if (i == forgeStage) pill.AddToClassList("forge-step-now");
                if (i < forgeStage) pill.AddToClassList("forge-step-done");
                Add(pill, new Label((i + 1) + "  " + StageNames[i]), "forge-step-name");
                Add(pill, new Label(i < forgeStage ? Mathf.RoundToInt(forgeScores[i] * 100) + "%" : ""), "forge-step-score");
            }

            forgePanel = Add(wrap, new VisualElement(), "forge-panel");
            metal = needle = timerFill = forgeMarker = heatBand = anvilArea = dotEl = ringEl = goldEl = quenchBlade = steam = null;

            switch (forgeStage)
            {
                case 0: RenderHeat(); break;
                case 1: RenderHammer(g); break;
                default: RenderQuench(); break;
            }

            stageHint = Add(forgePanel, new Label(stageRunning ? StageHelp() : forgeStage == 1 ? "Click the anvil (or press SPACE) to start" : forgeStage == 2 ? "Hold SPACE to plunge it into the water" : "Press SPACE to start"), "forge-hint");
            if (!stageRunning) stageHint.AddToClassList("forge-hint-start");

            if (forgeTick == null) forgeTick = root.schedule.Execute(ForgeTick).Every(16);
            else forgeTick.Resume();
        }

        string StageHelp()
        {
            switch (forgeStage)
            {
                case 0: return "Hold SPACE to pump the bellows. Keep the needle inside the gold band. It gets narrower as you go.";
                case 1: return "Strike " + Mathf.Min(strike + 1, Strikes) + " of " + Strikes + ". Click the dot when the closing ring meets the gold ring.";
                default: return dipped ? "Hold it under... let go of SPACE when the colour reaches the blue mark."
                                       : "Hold SPACE to plunge it into the water. Let go when the colour reaches the blue mark.";
            }
        }

        // ------------------------------------------------------------------ heat

        float BandWidth(float t) => Mathf.Lerp(BandStart, BandEnd, Mathf.Clamp01((t - HeatGrace) / (HeatSeconds - HeatGrace)));

        void RenderHeat()
        {
            metal = Add(forgePanel, new VisualElement(), "metal");
            metal.style.backgroundColor = HeatColour(heat);

            var gauge = Add(forgePanel, new VisualElement(), "gauge");
            heatBand = Add(gauge, new VisualElement(), "gauge-band");
            SetBand(stageRunning ? BandWidth(Time.realtimeSinceStartup - stageStart) : BandStart);
            needle = Add(gauge, new VisualElement(), "gauge-needle");
            needle.style.left = Length.Percent(heat);
            var labels = Add(forgePanel, new VisualElement(), "gauge-labels");
            Add(labels, new Label("COLD"), "gauge-label");
            Add(labels, new Label("CHERRY RED"), "gauge-label");
            Add(labels, new Label("BURNING"), "gauge-label");

            var bellows = Add(forgePanel, new VisualElement(), "bellows");
            Add(bellows, new Label("Hold to pump the bellows"), "bellows-label");
            bellows.RegisterCallback<PointerDownEvent>(_ => { if (!stageRunning) StartStage(); holding = true; });
            bellows.RegisterCallback<PointerUpEvent>(_ => holding = false);
            bellows.RegisterCallback<PointerLeaveEvent>(_ => holding = false);

            var timer = Add(forgePanel, new VisualElement(), "forge-timer");
            timerFill = Add(timer, new VisualElement(), "forge-timer-fill");
        }

        void SetBand(float width)
        {
            if (heatBand == null) return;
            heatBand.style.left = Length.Percent(BandCentre - width / 2);
            heatBand.style.width = Length.Percent(width);
        }

        // ------------------------------------------------------------------ hammer

        /// <summary>Where dots can appear (percent of the anvil area), for each kind of piece.</summary>
        static List<Vector2> Spots(string slot)
        {
            var list = new List<Vector2>();
            if (slot == "offhand")
            {
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f;
                    list.Add(new Vector2(50f + Mathf.Cos(a) * 13f, 50f + Mathf.Sin(a) * 28f));
                }
                list.Add(new Vector2(50f, 50f));
            }
            else if (slot == "head")
            {
                foreach (float x in new[] { 40f, 50f, 60f }) list.Add(new Vector2(x, 42f));
                foreach (float x in new[] { 34f, 45f, 55f, 66f }) list.Add(new Vector2(x, 58f));
            }
            else if (slot == "hands")
            {
                foreach (float x in new[] { 40f, 50f, 60f })
                    foreach (float y in new[] { 38f, 55f, 72f }) list.Add(new Vector2(x, y));
            }
            else
            {
                foreach (float x in new[] { 38f, 45f, 52f, 59f, 66f, 73f })
                {
                    list.Add(new Vector2(x, 47f));
                    list.Add(new Vector2(x, 53f));
                }
            }
            return list;
        }

        void RenderHammer(GearDef g)
        {
            anvilArea = Add(forgePanel, new VisualElement(), "anvil-area");
            anvilArea.RegisterCallback<PointerDownEvent>(_ => { if (!stageRunning && !busy) StartStage(); });
            var hot = HeatColour(Mathf.Lerp(80f, 62f, strike / (float)Strikes));

            if (g.slot == "offhand") DrawShield(hot);
            else if (g.slot == "hands") DrawGlove(hot);
            else if (g.slot == "head") DrawCap(hot);
            else DrawDagger(hot);

            // The gold ring (the target), the closing ring and the dot. Only the dot takes clicks.
            goldEl = Add(anvilArea, new VisualElement(), "gold-ring");
            ringEl = Add(anvilArea, new VisualElement(), "close-ring");
            dotEl = Add(anvilArea, new VisualElement(), "strike-dot");
            goldEl.pickingMode = ringEl.pickingMode = PickingMode.Ignore;
            Add(dotEl, new VisualElement(), "strike-dot-core").pickingMode = PickingMode.Ignore;
            dotEl.RegisterCallback<PointerDownEvent>(e => { e.StopPropagation(); HammerClick(); });
            PlaceCircle(goldEl, GoldSize);
            PlaceCircle(dotEl, DotSize + 18f);   // a little bigger than it looks, so it's easy to hit
            ShowDot(stageRunning && dotLive);

            var count = Add(forgePanel, new VisualElement(), "strike-row");
            for (int i = 0; i < Strikes; i++)
                Add(count, new VisualElement(), i < strike ? "strike-done" : "strike-left");
        }

        /// <summary>A plain block of colour inside the anvil area, placed in percent.</summary>
        VisualElement Part(float left, float top, float width, float height, Color c, float radius = 3f)
        {
            var e = Add(anvilArea, new VisualElement(), "anvil-part");
            e.pickingMode = PickingMode.Ignore;
            e.style.left = Length.Percent(left);
            e.style.top = Length.Percent(top);
            e.style.width = Length.Percent(width);
            e.style.height = Length.Percent(height);
            e.style.backgroundColor = c;
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = radius;
            return e;
        }

        void DrawDagger(Color hot)
        {
            var wood = new Color32(0x5a, 0x3a, 0x22, 255);
            var iron = (Color)new Color32(0x55, 0x58, 0x5c, 255);
            Part(9f, 44f, 4f, 12f, iron, 8f);          // pommel
            Part(12f, 45.5f, 18f, 9f, wood);             // grip
            Part(30f, 32f, 3f, 36f, iron);               // guard
            Part(33f, 42f, 46f, 16f, hot, 2f);           // blade
            // The point: a square turned on its corner, half hidden by the end of the blade.
            var tip = Add(anvilArea, new VisualElement(), "anvil-part");
            tip.pickingMode = PickingMode.Ignore;
            tip.style.left = Length.Percent(79f);
            tip.style.top = Length.Percent(50f);
            tip.style.width = tip.style.height = 34f;
            tip.style.marginLeft = tip.style.marginTop = -17f;
            tip.style.backgroundColor = hot;
            tip.style.rotate = new StyleRotate(new Rotate(new Angle(45f, AngleUnit.Degree)));
        }

        void DrawShield(Color hot)
        {
            var rim = Part(35f, 14f, 30f, 72f, hot, 999f);
            rim.style.borderTopWidth = rim.style.borderBottomWidth = rim.style.borderLeftWidth = rim.style.borderRightWidth = 6f;
            rim.style.borderTopColor = rim.style.borderBottomColor = rim.style.borderLeftColor = rim.style.borderRightColor = (Color)new Color32(0x55, 0x58, 0x5c, 255);
            Part(46f, 40f, 8f, 20f, new Color32(0x55, 0x58, 0x5c, 255), 999f);   // the boss
        }

        void DrawCap(Color hot)
        {
            Part(32f, 28f, 36f, 52f, hot, 90f);                                  // the dome
            Part(28f, 66f, 44f, 10f, new Color32(0x57, 0x51, 0x58, 255), 4f);   // the rim
        }

        void DrawGlove(Color hot)
        {
            Part(34f, 26f, 32f, 58f, hot, 14f);                                 // back of the hand
            Part(66f, 46f, 9f, 16f, hot, 8f);                                   // thumb
            Part(32f, 80f, 36f, 12f, new Color32(0x5a, 0x3a, 0x22, 255), 4f);   // leather cuff
        }

        /// <summary>Sizes a circle in pixels and centres it on the current dot.</summary>
        void PlaceCircle(VisualElement e, float size)
        {
            if (e == null) return;
            e.style.left = Length.Percent(dotPos.x);
            e.style.top = Length.Percent(dotPos.y);
            e.style.width = e.style.height = size;
            e.style.marginLeft = e.style.marginTop = -size / 2f;
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = size / 2f;
        }

        void ShowDot(bool on)
        {
            var d = on ? DisplayStyle.Flex : DisplayStyle.None;
            if (dotEl != null) dotEl.style.display = d;
            if (ringEl != null) ringEl.style.display = d;
            if (goldEl != null) goldEl.style.display = d;
        }

        void NewDot()
        {
            var spots = Spots(engine.OnAnvil?.slot);
            int pick;
            do pick = Random.Range(0, spots.Count); while (spots.Count > 1 && pick == lastSpot);
            lastSpot = pick;
            dotPos = spots[pick];
            dotStart = Time.realtimeSinceStartup;
            dotLive = true;
            PlaceCircle(goldEl, GoldSize);
            PlaceCircle(dotEl, DotSize + 18f);
            PlaceCircle(ringEl, RingStart);
            ShowDot(true);
        }

        float RingSize(float now) => Mathf.Lerp(RingStart, 0f, (now - dotStart) / ringSeconds);

        /// <summary>How far off the gold ring the closing ring may be and still count as a good hit. Strength helps.</summary>
        float GoodSlack => 16f + engine.S.stats[Stat.Str] * 0.8f;

        void HammerClick()
        {
            if (!stageRunning) { StartStage(); return; }
            if (forgeStage != 1 || !dotLive) return;
            float off = Mathf.Abs(RingSize(Time.realtimeSinceStartup) - GoldSize);
            float score = off <= PerfectSlack ? 1f
                        : off <= GoodSlack ? Mathf.Lerp(0.85f, 0.35f, (off - PerfectSlack) / (GoodSlack - PerfectSlack))
                        : 0f;
            Strike(score);
        }

        void Strike(float score)
        {
            dotLive = false;
            ShowDot(false);
            hammerTotal += score;
            strike++;
            DotPopup(score >= 1f ? "PERFECT" : score >= 0.6f ? "GOOD" : score > 0 ? "OKAY" : "MISS",
                     score >= 1f ? "pop-perfect" : score > 0 ? "pop-good" : "pop-miss");
            if (score > 0) Shake(anvilArea, score >= 1f ? 10f : 5f);
            if (score > 0) GameAudio.PlayVariant("forge_hammer", 3, score >= 1f ? 1f : 0.8f);
            else GameAudio.Play("forge_hammer_bad");
            if (strike >= Strikes) { EndStage(hammerTotal / Strikes); return; }
            ringSeconds *= 0.92f;
            nextDotAt = Time.realtimeSinceStartup + DotGap;
            Render();
        }

        /// <summary>A word that floats up from where the dot was.</summary>
        void DotPopup(string text, string cls)
        {
            if (anvilArea == null) return;
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            l.AddToClassList("pop");
            l.AddToClassList(cls);
            l.style.right = StyleKeyword.Auto;
            l.style.left = Length.Percent(dotPos.x);
            l.style.top = Length.Percent(Mathf.Max(0f, dotPos.y - 22f));
            l.style.marginLeft = -60f;
            anvilArea.Add(l);
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = l.schedule.Execute(() =>
            {
                float t = (Time.realtimeSinceStartup - start) / 0.8f;
                if (t >= 1f || l.panel == null) { tick?.Pause(); l.RemoveFromHierarchy(); return; }
                l.style.translate = new StyleTranslate(new Translate(new Length(0), new Length(-50f * t), 0));
                l.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            }).Every(16);
        }

        // ------------------------------------------------------------------ quench

        void RenderQuench()
        {
            var strip = Add(forgePanel, new VisualElement(), "quench-strip");
            for (int i = 0; i < 24; i++)
            {
                var seg = Add(strip, new VisualElement(), "quench-seg");
                seg.style.backgroundColor = HeatColour(Mathf.Lerp(97f, 15f, i / 23f));
            }
            var mark = Add(strip, new VisualElement(), "quench-mark");
            mark.style.left = Length.Percent(quenchTarget * 100);
            forgeMarker = Add(strip, new VisualElement(), "timing-marker");
            forgeMarker.style.left = Length.Percent(Mathf.Clamp01(quenchAt) * 100f);

            // The trough, with the piece held above it (or under the water while you hold it down)
            var trough = Add(forgePanel, new VisualElement(), "trough");
            quenchBlade = Add(trough, new VisualElement(), "quench-blade");
            quenchBlade.pickingMode = PickingMode.Ignore;
            quenchBlade.style.backgroundColor = HeatColour(Mathf.Lerp(97f, 15f, Mathf.Clamp01(quenchAt)));
            quenchBlade.style.top = holding && dipped ? 120f : 10f;
            var water = Add(trough, new VisualElement(), "trough-water");
            water.pickingMode = PickingMode.Ignore;
            steam = Add(trough, new Label("hssssss"), "steam");
            steam.pickingMode = PickingMode.Ignore;
            steam.style.opacity = 0f;

            trough.RegisterCallback<PointerDownEvent>(_ => { if (!dipped) dipByPointer = true; QuenchDown(); });
            trough.RegisterCallback<PointerUpEvent>(_ => { if (dipByPointer) QuenchUp(); });
            trough.RegisterCallback<PointerLeaveEvent>(_ => { if (dipByPointer) QuenchUp(); });
        }

        void QuenchDown()
        {
            if (busy || forgeStage != 2) return;
            if (!stageRunning) StartStage();
            if (dipped) return;
            dipped = true;
            holding = true;
            GameAudio.Play("quench");
            if (quenchBlade != null) quenchBlade.style.top = 120f;
            if (stageHint != null) stageHint.text = StageHelp();
        }

        void QuenchUp()
        {
            if (forgeStage != 2 || !stageRunning || !dipped || !holding) return;
            holding = false;
            float off = Mathf.Abs(quenchAt - quenchTarget);
            float score = Mathf.Clamp01(1f - off / 0.22f);
            Popup(forgePanel, off <= 0.035f ? "PERFECT" : score > 0.5f ? "GOOD" : quenchAt < quenchTarget ? "TOO SOON" : "TOO LATE",
                  off <= 0.035f ? "pop-perfect" : score > 0.5f ? "pop-good" : "pop-miss", 0);
            if (off <= 0.035f) score = 1f;
            if (quenchBlade != null) quenchBlade.style.top = 10f;
            EndStage(score);
        }

        // ------------------------------------------------------------------ running

        void StartStage()
        {
            stageRunning = true;
            stageStart = lastTick = Time.realtimeSinceStartup;
            if (forgeStage == 0) { heat = 15f; inBand = 0f; }
            if (forgeStage == 1) { strike = 0; hammerTotal = 0; ringSeconds = FirstRingSeconds; dotLive = false; nextDotAt = stageStart + 0.4f; }
            if (forgeStage == 2) { quenchAt = 0; dipped = false; dipByPointer = false; quenchTarget = Random.Range(0.5f, 0.78f); }
            Render();
        }

        void ForgeTick()
        {
            if (engine.S.scene != Engine.ForgeId) { forgeTick?.Pause(); return; }
            if (!stageRunning) return;
            float now = Time.realtimeSinceStartup;
            float dt = Mathf.Min(0.1f, now - lastTick);
            lastTick = now;
            float t = now - stageStart;

            switch (forgeStage)
            {
                case 0:
                {
                    heat += (holding ? 40f : -24f) * dt;
                    if (holding && now - lastBellows > 0.85f) { lastBellows = now; GameAudio.Play("forge_bellows", 0.8f); }
                    heat = Mathf.Clamp(heat, 0f, 100f);
                    float w = BandWidth(t);
                    SetBand(w);
                    if (t > HeatGrace && Mathf.Abs(heat - BandCentre) <= w / 2) inBand += dt;
                    if (needle != null) needle.style.left = Length.Percent(heat);
                    if (metal != null) metal.style.backgroundColor = HeatColour(heat);
                    if (timerFill != null) timerFill.style.width = Length.Percent(Mathf.Clamp01(1f - t / HeatSeconds) * 100f);
                    if (t >= HeatSeconds) EndStage(Mathf.Clamp01(inBand / (HeatSeconds - HeatGrace)));
                    break;
                }
                case 1:
                    if (!dotLive)
                    {
                        if (now >= nextDotAt) NewDot();
                        break;
                    }
                    float size = RingSize(now);
                    if (ringEl != null) PlaceCircle(ringEl, Mathf.Max(0f, size));
                    if (size <= DotSize - GoodSlack * 0.5f) Strike(0f);   // the ring closed and nobody struck
                    break;
                case 2:
                    if (!dipped) break;   // it only cools while you hold it under
                    quenchAt += dt / QuenchSeconds;
                    float c = Mathf.Clamp01(quenchAt);
                    if (forgeMarker != null) forgeMarker.style.left = Length.Percent(c * 100f);
                    if (quenchBlade != null) quenchBlade.style.backgroundColor = HeatColour(Mathf.Lerp(97f, 15f, c));
                    if (steam != null) steam.style.opacity = (1f - c) * (0.6f + 0.4f * Mathf.Sin(now * 30f));
                    if (quenchAt >= 1.1f) { holding = false; Popup(forgePanel, "TOO LATE", "pop-miss", 0); EndStage(0f); }
                    break;
            }
        }

        void EndStage(float score)
        {
            forgeScores[forgeStage] = score;
            stageRunning = false;
            holding = false;
            dotLive = false;
            if (forgeStage < 2)
            {
                forgeStage++;
                strike = 0;
                quenchAt = 0;
                dipped = false;
                Render();
                return;
            }
            // All three done: hand the scores to the engine after a moment to see the last result.
            forgeStage = 3;
            busy = true;
            GameAudio.Play("forge_done");
            root.schedule.Execute(() =>
            {
                busy = false;
                forgeStage = -1;
                engine.FinishForge(forgeScores[0], forgeScores[1], forgeScores[2]);
                AfterAction();
            }).StartingIn(900);
        }

        /// <summary>Keys at the forge. Space starts a step, pumps the bellows (hold) and plunges for the quench (hold, then let go).</summary>
        void ForgeKey(KeyDownEvent e)
        {
            if (busy) return;
            if (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            bool repeat = keyHeld;   // the key auto-repeats while held
            keyHeld = true;
            switch (forgeStage)
            {
                case 0:
                    if (!stageRunning) StartStage();
                    holding = true;
                    break;
                case 1:
                    if (!repeat && !stageRunning) StartStage();   // the strikes themselves are clicks
                    break;
                case 2:
                    if (!repeat) { if (!dipped) dipByPointer = false; QuenchDown(); }
                    break;
            }
        }

        void ForgeKeyUp(KeyUpEvent e)
        {
            if (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            keyHeld = false;
            if (forgeStage == 2) QuenchUp();
            else holding = false;
        }

        // ------------------------------------------------------------------ colour of hot metal

        static readonly (float at, Color c)[] HeatKeys =
        {
            (0f, new Color32(0x3a, 0x3a, 0x3c, 255)),
            (40f, new Color32(0x7a, 0x24, 0x18, 255)),
            (62f, new Color32(0xc8, 0x36, 0x1c, 255)),
            (76f, new Color32(0xe8, 0x70, 0x1f, 255)),
            (88f, new Color32(0xf5, 0xc5, 0x42, 255)),
            (100f, new Color32(0xff, 0xf6, 0xd8, 255)),
        };

        static Color HeatColour(float h)
        {
            for (int i = 0; i < HeatKeys.Length - 1; i++)
                if (h <= HeatKeys[i + 1].at)
                    return Color.Lerp(HeatKeys[i].c, HeatKeys[i + 1].c, Mathf.InverseLerp(HeatKeys[i].at, HeatKeys[i + 1].at, h));
            return HeatKeys[HeatKeys.Length - 1].c;
        }
    }
}
