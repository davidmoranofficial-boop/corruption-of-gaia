using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The forge game, in three steps:
    /// 1. Heat: hold Space to pump the bellows and keep the heat inside the band.
    /// 2. Hammer: six timed strikes on a marker that swings back and forth, faster each time.
    /// 3. Quench: watch the metal cool and press Space when its colour passes the mark.
    /// Each step scores 0 to 1, and the engine turns the three scores into a quality and a rank.
    /// </summary>
    public partial class GameUI
    {
        const float HeatSeconds = 7f, HeatGrace = 2f, BandLow = 60f, BandHigh = 78f;
        const int Strikes = 6;
        const float QuenchSeconds = 3.2f, QuenchTarget = 0.62f;

        int forgeStage = -1;            // 0 heat, 1 hammer, 2 quench, 3 done
        bool stageRunning;
        readonly float[] forgeScores = new float[3];
        float stageStart, lastTick;
        bool holding, keyHeld;

        // Heat
        float heat, inBand;
        // Hammer
        int strike;
        float hammerTotal, swingStart, swingCycle, hammerZone;
        // Quench
        float quenchAt;

        IVisualElementScheduledItem forgeTick;
        VisualElement metal, needle, timerFill, forgeMarker, forgePanel;
        Label stageHint;

        static readonly string[] StageNames = { "HEAT", "HAMMER", "QUENCH" };

        void RenderForge()
        {
            var s = engine.S;
            if (forgeStage < 0) { forgeStage = 0; stageRunning = false; forgeScores[0] = forgeScores[1] = forgeScores[2] = 0; }
            var g = engine.OnAnvil;
            main.Clear();
            var wrap = Add(main, new VisualElement(), "forge");

            Add(wrap, new Label((engine.Scene(s.forgeNext)?.place ?? "") .ToUpperInvariant() + "  ·  THE FORGE"), "place");
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
            metal = Add(forgePanel, new VisualElement(), "metal");
            metal.style.backgroundColor = HeatColour(forgeStage == 0 ? heat : forgeStage == 1 ? 72f : 95f);

            switch (forgeStage)
            {
                case 0: RenderHeat(); break;
                case 1: RenderHammer(); break;
                default: RenderQuench(); break;
            }

            stageHint = Add(forgePanel, new Label(stageRunning ? StageHelp() : "Press SPACE to start"), "forge-hint");
            if (!stageRunning) stageHint.AddToClassList("forge-hint-start");

            if (forgeTick == null) forgeTick = root.schedule.Execute(ForgeTick).Every(16);
            else forgeTick.Resume();
        }

        string StageHelp()
        {
            switch (forgeStage)
            {
                case 0: return "Hold SPACE to pump the bellows. Keep the needle inside the bright band until the time runs out.";
                case 1: return "Strike " + Mathf.Min(strike + 1, Strikes) + " of " + Strikes + ". Press SPACE when the hammer is over the bright mark.";
                default: return "The metal is cooling. Press SPACE to plunge it into the trough as its colour passes the mark.";
            }
        }

        void RenderHeat()
        {
            var gauge = Add(forgePanel, new VisualElement(), "gauge");
            var band = Add(gauge, new VisualElement(), "gauge-band");
            band.style.left = Length.Percent(BandLow);
            band.style.width = Length.Percent(BandHigh - BandLow);
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

        void RenderHammer()
        {
            var bar = Add(forgePanel, new VisualElement(), "timing-track");
            bar.AddToClassList("forge-bar");
            float good = 0.2f + engine.S.stats[Stat.Str] * 0.006f, perfect = 0.07f;
            var g = Add(bar, new VisualElement(), "timing-good");
            g.style.left = Length.Percent((hammerZone - good / 2) * 100);
            g.style.width = Length.Percent(good * 100);
            var p = Add(bar, new VisualElement(), "timing-perfect");
            p.style.left = Length.Percent((hammerZone - perfect / 2) * 100);
            p.style.width = Length.Percent(perfect * 100);
            forgeMarker = Add(bar, new VisualElement(), "timing-marker");
            bar.RegisterCallback<PointerDownEvent>(_ => ForgePress());
            var count = Add(forgePanel, new VisualElement(), "strike-row");
            for (int i = 0; i < Strikes; i++)
                Add(count, new VisualElement(), i < strike ? "strike-done" : "strike-left");
        }

        void RenderQuench()
        {
            var strip = Add(forgePanel, new VisualElement(), "quench-strip");
            for (int i = 0; i < 24; i++)
            {
                var seg = Add(strip, new VisualElement(), "quench-seg");
                seg.style.backgroundColor = HeatColour(Mathf.Lerp(97f, 15f, i / 23f));
            }
            var mark = Add(strip, new VisualElement(), "quench-mark");
            mark.style.left = Length.Percent(QuenchTarget * 100);
            forgeMarker = Add(strip, new VisualElement(), "timing-marker");
            strip.RegisterCallback<PointerDownEvent>(_ => ForgePress());
        }

        // ------------------------------------------------------------------ running

        void StartStage()
        {
            stageRunning = true;
            stageStart = lastTick = Time.realtimeSinceStartup;
            if (forgeStage == 0) { heat = 15f; inBand = 0f; }
            if (forgeStage == 1) { strike = 0; hammerTotal = 0; swingCycle = 1.15f; swingStart = stageStart; hammerZone = Random.Range(0.25f, 0.75f); }
            if (forgeStage == 2) quenchAt = 0;
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
                    heat += (holding ? 40f : -24f) * dt;
                    heat = Mathf.Clamp(heat, 0f, 100f);
                    if (t > HeatGrace && heat >= BandLow && heat <= BandHigh) inBand += dt;
                    if (needle != null) needle.style.left = Length.Percent(heat);
                    if (metal != null) metal.style.backgroundColor = HeatColour(heat);
                    if (timerFill != null) timerFill.style.width = Length.Percent(Mathf.Clamp01(1f - t / HeatSeconds) * 100f);
                    if (t >= HeatSeconds) EndStage(Mathf.Clamp01(inBand / (HeatSeconds - HeatGrace)));
                    break;
                case 1:
                {
                    float phase = Mathf.PingPong((now - swingStart) / swingCycle * 2f, 1f);
                    if (forgeMarker != null) forgeMarker.style.left = Length.Percent(phase * 100f);
                    break;
                }
                case 2:
                    quenchAt = t / QuenchSeconds;
                    if (forgeMarker != null) forgeMarker.style.left = Length.Percent(Mathf.Clamp01(quenchAt) * 100f);
                    if (metal != null) metal.style.backgroundColor = HeatColour(Mathf.Lerp(97f, 15f, Mathf.Clamp01(quenchAt)));
                    if (quenchAt >= 1f) { Popup(forgePanel, "TOO LATE", "pop-miss", 0); EndStage(0f); }
                    break;
            }
        }

        /// <summary>Space (or a click) during the hammer or quench step.</summary>
        void ForgePress()
        {
            if (!stageRunning) { StartStage(); return; }
            float now = Time.realtimeSinceStartup;
            if (forgeStage == 1)
            {
                float phase = Mathf.PingPong((now - swingStart) / swingCycle * 2f, 1f);
                float good = 0.2f + engine.S.stats[Stat.Str] * 0.006f, perfect = 0.07f;
                float off = Mathf.Abs(phase - hammerZone);
                float score = off <= perfect / 2 ? 1f : off <= good / 2 ? 0.6f : 0f;
                hammerTotal += score;
                strike++;
                Popup(forgePanel, score >= 1f ? "PERFECT" : score > 0 ? "GOOD" : "MISS", score >= 1f ? "pop-perfect" : score > 0 ? "pop-good" : "pop-miss", 0);
                if (score > 0) Shake(metal, score >= 1f ? 10f : 5f);
                if (strike >= Strikes) { EndStage(hammerTotal / Strikes); return; }
                swingCycle *= 0.9f;
                hammerZone = Random.Range(0.2f, 0.8f);
                Render();
            }
            else if (forgeStage == 2)
            {
                float off = Mathf.Abs(quenchAt - QuenchTarget);
                float score = Mathf.Clamp01(1f - off / 0.25f);
                Popup(forgePanel, score > 0.85f ? "PERFECT" : score > 0.5f ? "GOOD" : "TOO SOON" , score > 0.85f ? "pop-perfect" : score > 0.5f ? "pop-good" : "pop-miss", 0);
                if (quenchAt > QuenchTarget + 0.25f) score = 0;
                EndStage(score);
            }
        }

        void EndStage(float score)
        {
            forgeScores[forgeStage] = score;
            stageRunning = false;
            holding = false;
            if (forgeStage < 2)
            {
                forgeStage++;
                hammerZone = 0.5f;
                strike = 0;
                Render();
                return;
            }
            // All three done: hand the scores to the engine after a moment to see the last result.
            forgeStage = 3;
            busy = true;
            root.schedule.Execute(() =>
            {
                busy = false;
                forgeStage = -1;
                engine.FinishForge(forgeScores[0], forgeScores[1], forgeScores[2]);
                AfterAction();
            }).StartingIn(700);
        }

        /// <summary>Keys at the forge. Space starts a step, pumps the bellows (hold), strikes and quenches.</summary>
        void ForgeKey(KeyDownEvent e)
        {
            if (busy) return;
            if (e.keyCode != KeyCode.Space && e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter) return;
            bool repeat = keyHeld;   // the key auto-repeats while held; only the bellows care about that
            keyHeld = true;
            if (repeat && forgeStage != 0) return;
            if (!stageRunning) { StartStage(); if (forgeStage == 0) holding = true; return; }
            if (forgeStage == 0) holding = true;
            else ForgePress();
        }

        void ForgeKeyUp(KeyUpEvent e)
        {
            if (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) { holding = false; keyHeld = false; }
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
