using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The opening cutscene: the trade boat in the storm, the wave, black, the beach at dawn, Yenson finding you
    /// and hauling you up to his shack. A scene with "cutscene" set in the story plays here instead of as text;
    /// its text lines are the captions, in order. When it ends (or Enter skips it) the scene's first choice is taken.
    ///
    /// Everything is drawn at 320 x 180 pixels (art in Sprites/Cutscene, made by Tools/Art/cutscene.py) and scaled
    /// up by a whole number, and every frame is worked out from the time since it started, so it can't drift.
    /// </summary>
    public partial class GameUI
    {
        const int CutW = 320, CutH = 180;

        VisualElement cutPage, cutView, cutFade, cutFlash, cutBolt, cutHint;
        Label cutCaption;
        string cutId;
        float cutStart;
        int cutK = 4, cutCue;
        bool cutEnding, cutWaiting;   // waiting: the cutscene's over, black, with Wake up in the middle
        readonly Dictionary<string, VisualElement> cutEls = new Dictionary<string, VisualElement>();
        (VisualElement box, VisualElement sheet) cutYou, cutYenson;
        readonly Dictionary<string, float> cutAmb = new Dictionary<string, float>();

        // when each shot starts and how long the whole thing runs (seconds)
        const float ShotBlack = 17.5f, ShotBeach = 23f, ShotShack = 37f, CutEnd = 49f;

        // captions: (start, end, index into the scene's text, centred on black rather than along the bottom)
        static readonly (float from, float to, int line, bool centre)[] CutCaptions =
        {
            (1.0f, 5.6f, 0, false), (6.0f, 10.6f, 1, false), (11.0f, 15.0f, 2, false),
            (18.2f, 22.4f, 3, true),
            (25.0f, 29.6f, 4, false), (31.0f, 36.0f, 5, false),
            (39.0f, 45.0f, 6, false),
        };

        // sounds: (time, id, volume)
        static readonly (float at, string id, float vol)[] CutSounds =
        {
            (3.9f, "thunder", 0.9f), (7.2f, "timber_creak", 0.8f), (9.4f, "thunder", 1f), (12.4f, "timber_creak", 0.9f),
            (13.3f, "thunder", 1f), (14.2f, "wave_crash", 1f), (ShotShack + 9.6f, "door", 0.8f),
        };

        // lightning: (time, x of the bolt)
        static readonly (float at, int x)[] CutLightning = { (3.9f, 236), (9.4f, 60), (13.3f, 180) };

        static readonly int[] WalkCycle = { 1, 0, 2, 0 };

        bool InCutscene => !string.IsNullOrEmpty(engine.Current?.cutscene) && engine.S.scene != Engine.Title && engine.S.scene != Engine.Creation;

        void RenderCutscene()
        {
            var sc = engine.Current;
            if (cutPage != null && cutPage.panel != null && cutId == sc.id) return;   // already playing: don't restart it
            screen.Clear();
            cutEls.Clear();
            cutId = sc.id;
            cutStart = Time.realtimeSinceStartup;
            cutCue = 0;
            cutEnding = false;
            cutWaiting = false;

            // no painted backdrop behind it (the creation screen's beach was showing through), no padding round it
            screenBox.style.backgroundImage = new StyleBackground();
            screenBox.AddToClassList("front");
            cutPage = Add(screen, new VisualElement(), "cut-page");
            FillScreen(cutPage);
            cutView = Add(cutPage, new VisualElement { pickingMode = PickingMode.Ignore }, "cut-view");

            // the storm
            CutLayer("sky", "cs_storm_sky", CutW, CutH);
            CutLayer("clouds_a", "cs_storm_clouds", 640, 80);
            CutLayer("clouds_b", "cs_storm_clouds", 640, 80);
            CutLayer("waves_back_a", "cs_waves_back", 640, 60);
            CutLayer("waves_back_b", "cs_waves_back", 640, 60);
            CutLayer("ship", "cs_ship", 150, 120);
            CutLayer("ship_flash", "cs_ship_flash", 150, 120);
            CutLayer("waves_front_a", "cs_waves_front", 640, 70);
            CutLayer("waves_front_b", "cs_waves_front", 640, 70);
            var rain = Add(cutView, new VisualElement { pickingMode = PickingMode.Ignore }, "cut-rain");
            cutEls["rain"] = rain;
            for (int y = 0; y < 3; y++)
                for (int x = 0; x < 5; x++)
                {
                    var t = Add(rain, new VisualElement { pickingMode = PickingMode.Ignore }, "cut-layer");
                    t.style.backgroundImage = new StyleBackground(Sprite("World/fx_storm"));
                    t.userData = new Vector2Int(x, y);
                }
            cutBolt = CutLayer("bolt", "cs_bolt", 40, 110);
            CutLayer("big_wave", "cs_big_wave", 360, 180);

            // the beach
            CutLayer("beach", "cs_beach", CutW, CutH);
            CutLayer("surf", "cs_surf", CutW, 24);
            // the shack
            CutLayer("shack", "cs_shack", CutW, CutH);

            // you, lying where the sea left you, and Yenson
            cutYou = Walker(cutView, 1, PlayerWalkLayers());
            cutYou.box.style.rotate = new Rotate(new Angle(90));   // head towards Yenson when he drags you
            cutYenson = Walker(cutView, 1, new List<string> { "Walk/npc_yenson" });

            cutFlash = Add(cutView, new VisualElement { pickingMode = PickingMode.Ignore }, "cut-flash");
            cutFade = Add(cutView, new VisualElement { pickingMode = PickingMode.Ignore }, "cut-fade");
            cutCaption = Add(cutPage, new Label(""), "cut-caption");
            cutHint = Add(cutPage, new Label("Enter  skip"), "cut-hint");

            cutPage.RegisterCallback<GeometryChangedEvent>(_ => CutLayout());
            cutPage.RegisterCallback<PointerDownEvent>(_ => CutSkip());
            cutPage.schedule.Execute(CutTick).Every(16);
            CutLayout();
            CutTick();
        }

        VisualElement CutLayer(string key, string art, int w, int h)
        {
            var e = Add(cutView, new VisualElement { pickingMode = PickingMode.Ignore }, "cut-layer");
            var tex = Sprite("Cutscene/" + art);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            e.userData = new Vector2Int(w, h);
            cutEls[key] = e;
            return e;
        }

        /// <summary>Picks the biggest whole-number scale that fits the window and sizes everything to it.</summary>
        void CutLayout()
        {
            if (cutPage == null) return;
            float W = cutPage.layout.width, H = cutPage.layout.height;
            if (float.IsNaN(W) || W < 10) { W = screenBox.layout.width; H = screenBox.layout.height; }
            if (float.IsNaN(W) || W < 10) { W = 1920; H = 1080; }
            int k = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(W / CutW, H / CutH)));
            cutK = k;
            cutView.style.width = CutW * k;
            cutView.style.height = CutH * k;
            cutView.style.left = Mathf.Round((W - CutW * k) / 2);
            cutView.style.top = Mathf.Round((H - CutH * k) / 2);
            foreach (var kv in cutEls)
            {
                if (kv.Key == "rain")
                {
                    foreach (var t in kv.Value.Children())
                    {
                        var p = (Vector2Int)t.userData;
                        t.style.width = t.style.height = 96 * k;
                        t.style.left = p.x * 96 * k;
                        t.style.top = p.y * 96 * k;
                    }
                    continue;
                }
                var size = (Vector2Int)kv.Value.userData;
                kv.Value.style.width = size.x * k;
                kv.Value.style.height = size.y * k;
            }
            foreach (var w in new[] { cutYou, cutYenson })
            {
                if (w.box == null) continue;
                w.box.style.width = 16 * k;
                w.box.style.height = 24 * k;
                w.sheet.style.width = 48 * k;
                w.sheet.style.height = 96 * k;
            }
            cutCaption.style.fontSize = Mathf.Clamp(6 * k, 18, 34);
        }

        void CutPlace(string key, float x, float y, bool show = true)
        {
            var e = cutEls[key];
            e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            e.style.left = Mathf.Round(x) * cutK;
            e.style.top = Mathf.Round(y) * cutK;
        }

        void CutWalker((VisualElement box, VisualElement sheet) w, float x, float y, string facing, int frame, bool show)
        {
            w.box.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;
            w.box.style.left = Mathf.Round(x) * cutK;
            w.box.style.top = Mathf.Round(y) * cutK;
            SetFrame(w.sheet, facing, frame, cutK);
        }

        void CutTick()
        {
            // a mistake in one frame shouldn't leave you stuck on a frozen picture: say what broke and move on to the hut
            try { CutFrame(); }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
                CutFinish();
            }
        }

        void CutFrame()
        {
            if (cutPage == null || cutPage.panel == null || cutEnding || cutWaiting) return;
            float t = Time.realtimeSinceStartup - cutStart;
            if (t >= CutEnd) { CutWake(); return; }

            while (cutCue < CutSounds.Length && t >= CutSounds[cutCue].at)
            {
                GameAudio.Play(CutSounds[cutCue].id, CutSounds[cutCue].vol, 0.02f);
                cutCue++;
            }

            bool storm = t < ShotBlack, beach = t >= ShotBeach && t < ShotShack, shack = t >= ShotShack;
            float fade = 0, flash = 0;

            // ---- the storm: everything heaving, rain driving, lightning, then the wave
            foreach (var k in new[] { "sky", "clouds_a", "clouds_b", "waves_back_a", "waves_back_b", "ship", "ship_flash",
                                      "waves_front_a", "waves_front_b", "rain", "bolt", "big_wave" })
                cutEls[k].style.display = storm ? DisplayStyle.Flex : DisplayStyle.None;
            if (storm)
            {
                float cloudX = -(t * 9f % 640f);
                CutPlace("sky", 0, 0);
                CutPlace("clouds_a", cloudX, -4);
                CutPlace("clouds_b", cloudX + 640, -4);
                float back = -(t * 14f % 640f), front = -(t * 26f % 640f);
                float swell = Mathf.Sin(t * 1.3f) * 3f;
                CutPlace("waves_back_a", back, 100 + swell * 0.5f);
                CutPlace("waves_back_b", back + 640, 100 + swell * 0.5f);
                float heave = Mathf.Sin(t * 1.3f + 0.6f) * 4f + Mathf.Sin(t * 2.9f) * 1.5f;
                float roll = Mathf.Sin(t * 1.1f) * 4f + Mathf.Sin(t * 2.3f + 1f) * 1.5f;
                if (t > 13.5f) roll += (t - 13.5f) * 9f;                     // the wave lifts and tips her
                CutPlace("ship", 90, 10 + heave);
                CutPlace("ship_flash", 90, 10 + heave);
                cutEls["ship"].style.rotate = new Rotate(new Angle(roll));
                cutEls["ship_flash"].style.rotate = new Rotate(new Angle(roll));
                CutPlace("waves_front_a", front, 118 - swell);
                CutPlace("waves_front_b", front + 640, 118 - swell);
                var rain = cutEls["rain"];
                rain.style.left = -((t * 120f) % 96f) * cutK * 0.35f - 96 * cutK * 0.5f;
                rain.style.top = ((t * 520f) % 96f - 96f) * cutK;

                // lightning: a bright flash, a flicker, the bolt and the ship lit pale
                float lit = 0;
                int boltX = -1;
                foreach (var (at, x) in CutLightning)
                {
                    float d = t - at;
                    if (d < 0 || d > 0.6f) continue;
                    lit = Mathf.Max(lit, d < 0.08f ? 1f : d < 0.14f ? 0.2f : d < 0.22f ? 0.8f : Mathf.Lerp(0.6f, 0, (d - 0.22f) / 0.38f));
                    if (d < 0.3f) boltX = x;
                }
                flash = lit * 0.55f;
                cutEls["ship_flash"].style.opacity = lit > 0.3f ? 1 : 0;
                CutPlace("bolt", boltX, 0, boltX >= 0);

                // the wave: rises in from the right and swallows everything, then white
                float w = Mathf.Clamp01((t - 13.4f) / 2.2f);
                CutPlace("big_wave", Mathf.Lerp(330, -30, w * w * (3 - 2 * w)), 0, t > 13.4f);
                if (t > 15.4f) flash = Mathf.Max(flash, Mathf.Clamp01((t - 15.4f) / 0.4f));
                if (t > 16.4f) { flash = Mathf.Clamp01(1 - (t - 16.4f) / 0.5f); fade = Mathf.Clamp01((t - 16.4f) / 0.3f); }
            }
            else if (!beach && !shack) fade = 1;    // black

            // ---- the beach at dawn: the tide washing in and out round you, then Yenson
            CutPlace("beach", 0, 0, beach);
            float surfY = 87 + (Mathf.Sin((t - ShotBeach) * 1.2f) * 0.5f + 0.5f) * 10f;
            CutPlace("surf", 0, surfY, beach);
            // ---- the shack
            CutPlace("shack", 0, 0, shack);

            if (beach)
            {
                float b = t - ShotBeach;
                fade = Mathf.Max(fade, 1 - Mathf.Clamp01(b / 2.5f));
                if (t > ShotShack - 1.2f) fade = Mathf.Max(fade, Mathf.Clamp01((t - (ShotShack - 1.2f)) / 1.0f));
                CutWalker(cutYou, 152, 124, "down", 0, true);
                // he comes along the sand from the left, stops by you, looks down
                float walkFrom = 6.5f, speed = 24f;
                float wx = -16 + Mathf.Max(0, b - walkFrom) * speed;
                bool arrived = wx >= 124;
                if (arrived) wx = 124;
                int frame = arrived ? 0 : WalkCycle[(int)(Mathf.Max(0, b - walkFrom) * 7f) % 4];   // never negative (before he sets off)
                CutWalker(cutYenson, wx, 112, arrived ? (b > walkFrom + 7f ? "down" : "right") : "right", frame, b > walkFrom);
            }
            else if (shack)
            {
                float s = t - ShotShack;
                fade = 1 - Mathf.Clamp01(s / 1.5f);
                if (t > CutEnd - 2.5f) fade = Mathf.Clamp01((t - (CutEnd - 2.5f)) / 1.5f);
                // he walks backwards up the sand to his door, dragging you by the shoulders
                float speed = 20f, x = Mathf.Min(40 + s * speed, 222);
                bool there = x >= 222;
                int frame = there ? 0 : WalkCycle[(int)(s * 5f) % 4];   // slow, heavy steps
                bool inside = s > 9.6f;
                CutWalker(cutYenson, x, 120, "left", frame, !inside);
                CutWalker(cutYou, x - 18, 122, "down", 0, !inside);
            }
            else
            {
                cutYou.box.style.display = DisplayStyle.None;
                cutYenson.box.style.display = DisplayStyle.None;
            }

            cutFade.style.opacity = fade;
            cutFlash.style.opacity = flash;

            // ---- the words
            string text = "";
            bool centre = false;
            var lines = engine.Current?.text;
            foreach (var (from, to, line, c) in CutCaptions)
            {
                if (t < from || t > to || lines == null || line >= lines.Count) continue;
                string full = engine.Fill(lines[line]);
                int shown = Mathf.Min(full.Length, Mathf.FloorToInt((t - from) * 42f));
                text = full.Substring(0, shown);
                centre = c;
                cutCaption.style.opacity = Mathf.Clamp01((to - t) / 0.4f);
            }
            cutCaption.text = text;
            cutCaption.EnableInClassList("cut-caption-centre", centre);
            cutCaption.style.display = text == "" ? DisplayStyle.None : DisplayStyle.Flex;

            // the sea and the weather, by shot
            cutAmb.Clear();
            if (storm) { cutAmb["amb_rain"] = 1f; cutAmb["amb_wind"] = 1f; cutAmb["amb_sea"] = 1f; }
            else if (beach || shack) { cutAmb["amb_sea"] = 0.6f; cutAmb["amb_wind"] = 0.2f; }
            GameAudio.SetAmbience(cutAmb);
        }

        /// <summary>Enter or a click: skips to the end of the cutscene, or, once it's over, wakes you.</summary>
        void CutSkip()
        {
            if (cutEnding) return;
            GameAudio.Play("ui_confirm");
            if (cutWaiting) CutFinish();
            else CutWake();
        }

        /// <summary>The end: everything black and quiet but the sea, and Wake up in the middle. The game starts when you press it.</summary>
        void CutWake()
        {
            if (cutWaiting || cutPage == null) return;
            cutWaiting = true;
            foreach (var e in cutView.Children()) if (e != cutFade) e.style.display = DisplayStyle.None;
            cutFade.style.display = DisplayStyle.Flex;
            cutFade.style.opacity = 1;
            cutCaption.style.display = DisplayStyle.None;
            cutHint.style.display = DisplayStyle.None;
            cutAmb.Clear();
            cutAmb["amb_sea"] = 0.25f;
            GameAudio.SetAmbience(cutAmb);
            var wake = Add(cutPage, new VisualElement(), "cut-wake");
            Add(wake, new Button(CutSkip) { text = "Wake up", focusable = false }, "front-btn-main");
        }

        void CutFinish()
        {
            if (cutEnding) return;
            cutEnding = true;
            cutPage = null;
            var next = engine.Choices().FirstOrDefault(c => c.enabled);
            if (next != null) engine.Choose(next.def);
            AfterAction();
        }

        bool CutsceneKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Space || e.keyCode == KeyCode.Escape)
            {
                CutSkip();
                return true;
            }
            return true;    // nothing else does anything while it plays
        }
    }
}
