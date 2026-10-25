using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// Life in the picture: light that follows the time of day (with lamps, fires and the forge glowing at night),
    /// fireflies, smoke, dust when you run, glints on the water, gulls and cloud shadows passing over, a fade between
    /// areas, and the slashes, fire and sparks of a fight. Sprites are in Resources/Sprites/Fx (made by Tools/Art/fx.py).
    /// </summary>
    public partial class GameUI
    {
        /// <summary>One running effect: a sprite sheet that plays through its frames while it drifts.</summary>
        class Fx
        {
            public VisualElement box, sheet;
            public float born, life, frameW, frameH, fade = 0.3f;
            public int frames;
            public bool loop;
            public Vector2 pos, vel;
            public float wobble;     // sideways sway (fireflies, leaves)
            public float baseAlpha = 1f;
        }

        readonly List<Fx> fxs = new List<Fx>();
        VisualElement lightTint, vignette;
        readonly List<(VisualElement el, float phase, float strength)> glows = new List<(VisualElement, float, float)>();
        string fadedArea = "";
        float nextGlint, nextBird, nextCloud, nextSmoke, nextFirefly, nextSplash, nextLeaf;
        System.Random fxRnd = new System.Random();

        static float Now => Time.realtimeSinceStartup;
        float RandF(float a, float b) => a + (float)fxRnd.NextDouble() * (b - a);

        void StartFx()
        {
            root.schedule.Execute(FxTick).Every(16);
        }

        // ------------------------------------------------------------------ the sprites

        /// <summary>Starts an effect: sheet = Fx/name, frames side by side, each fw x fh pixels, shown at scale k.</summary>
        Fx Spawn(VisualElement parent, string name, int frames, int fw, int fh, float k, Vector2 pos, float life,
                 Vector2 vel = default, bool loop = false, int insertAt = -1)
        {
            var tex = Sprite("Fx/" + name);
            if (tex == null || parent == null) return null;
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("fx");
            box.style.width = fw * k;
            box.style.height = fh * k;
            box.style.left = pos.x - fw * k / 2f;
            box.style.top = pos.y - fh * k / 2f;
            var sheet = new VisualElement { pickingMode = PickingMode.Ignore };
            sheet.AddToClassList("fx-sheet");
            sheet.style.width = fw * k * frames;
            sheet.style.height = fh * k;
            sheet.style.backgroundImage = new StyleBackground(tex);
            box.Add(sheet);
            if (insertAt >= 0 && insertAt <= parent.childCount) parent.Insert(insertAt, box); else parent.Add(box);
            var f = new Fx { box = box, sheet = sheet, born = Now, life = life, frames = frames, frameW = fw * k, frameH = fh * k, loop = loop, pos = pos, vel = vel };
            fxs.Add(f);
            return f;
        }

        void FxTick()
        {
            float now = Now;
            for (int i = fxs.Count - 1; i >= 0; i--)
            {
                var f = fxs[i];
                float age = now - f.born;
                if (f.box.panel == null || age >= f.life) { f.box.RemoveFromHierarchy(); fxs.RemoveAt(i); continue; }
                float t = age / f.life;
                int frame = f.loop ? (int)(age * 8f) % f.frames : Mathf.Min(f.frames - 1, (int)(t * f.frames));
                f.sheet.style.left = -frame * f.frameW;
                var p = f.pos + f.vel * age;
                if (f.wobble > 0) p.x += Mathf.Sin(age * 2.3f + f.born) * f.wobble;
                f.box.style.left = p.x - f.frameW / 2f;
                f.box.style.top = p.y - f.frameH / 2f;
                float a = f.baseAlpha;
                if (f.loop && t > 1f - f.fade) a *= (1f - t) / f.fade;
                if (f.loop && t < 0.15f) a *= t / 0.15f;
                f.box.style.opacity = a;
            }

            if (worldView != null && worldView.panel != null && worldLayer != null && !InBattle) { WorldLife(now); YSort(); }
        }

        /// <summary>Draws people and things lower on the screen in front of those higher up, so someone standing
        /// behind you is hidden by you instead of standing on your head. Only walkers and world things are
        /// reordered, among the places they already hold, so tree tops, light and weather stay on top.</summary>
        void YSort()
        {
            var kids = worldLayer.Children().ToList();
            var slots = new List<int>();
            var items = new List<VisualElement>();
            for (int i = 0; i < kids.Count; i++)
                if (kids[i].ClassListContains("walker") || kids[i].ClassListContains("world-thing")) { slots.Add(i); items.Add(kids[i]); }
            var sorted = items.OrderBy(Feet).ToList();
            bool same = true;
            for (int i = 0; i < items.Count && same; i++) same = ReferenceEquals(items[i], sorted[i]);
            if (same) return;
            foreach (var e in items) e.RemoveFromHierarchy();
            for (int i = 0; i < slots.Count; i++) worldLayer.Insert(slots[i], sorted[i]);
        }

        static float Feet(VisualElement e) => e.style.top.value.value + e.style.height.value.value;

        // ------------------------------------------------------------------ light

        /// <summary>How dark it is (0-0.6) and the colour of the light, from the time of day.</summary>
        Color DayLight(out float dark)
        {
            float h = (engine.S.minutes % 1440) / 60f;
            var night = new Color(0.03f, 0.05f, 0.14f);
            var dawn = new Color(0.30f, 0.14f, 0.18f);
            var gold = new Color(0.40f, 0.22f, 0.04f);
            Color c; float a;
            if (h < 4f || h >= 21f) { c = night; a = 0.58f; }
            else if (h < 5.5f) { float u = (h - 4f) / 1.5f; c = Color.Lerp(night, dawn, u); a = Mathf.Lerp(0.58f, 0.22f, u); }
            else if (h < 7f) { float u = (h - 5.5f) / 1.5f; c = dawn; a = Mathf.Lerp(0.22f, 0f, u); }
            else if (h < 17f) { c = gold; a = 0f; }
            else if (h < 19f) { float u = (h - 17f) / 2f; c = gold; a = Mathf.Lerp(0f, 0.18f, u); }
            else { float u = (h - 19f) / 2f; c = Color.Lerp(gold, night, u); a = Mathf.Lerp(0.18f, 0.58f, u); }
            dark = engine.Area != null && engine.Area.indoor ? Mathf.Min(a, 0.25f) : a;
            c.a = dark;
            return c;
        }

        /// <summary>The light over the world, glows at lamps and fires, and a vignette. Called while drawing the world.</summary>
        void DrawLight(AreaDef a, int k)
        {
            glows.Clear();
            lightTint = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "world-light");
            int T = Tile * k;
            // lights: fires and braziers, the forge, the hearth, and a lamp by each door
            var spots = new List<(Vector2 at, float size, float strength)>();
            foreach (var o in engine.Objects())
                if (o.sprite == "obj_fire" || o.sprite == "obj_brazier_lit") spots.Add((new Vector2(o.x + 0.5f, o.y + 0.5f), 7f, 1f));
            for (int y = 0; y < a.rows.Count; y++)
                for (int x = 0; x < a.rows[y].Length; x++)
                {
                    char c = a.rows[y][x];
                    if (c == 'L' || c == 'f') spots.Add((new Vector2(x + 0.5f, y + 0.5f), 6f, 1f));
                    else if (c == 'l' && !a.indoor) spots.Add((new Vector2(x + 0.5f, y - 0.3f), 4.5f, 0.9f));   // lamp posts
                    else if (c == 'D' && !a.indoor) spots.Add((new Vector2(x + 0.5f, y), 4f, 0.7f));
                }
            foreach (var (at, size, strength) in spots)
            {
                var g = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "world-glow");
                var tex = Sprite("Fx/glow_warm");
                if (tex != null) g.style.backgroundImage = new StyleBackground(tex);
                g.style.width = size * T;
                g.style.height = size * T;
                g.style.left = at.x * T - size * T / 2f;
                g.style.top = at.y * T - size * T / 2f;
                glows.Add((g, RandF(0, 10), strength));
            }
            vignette = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "world-vignette");
            var vt = Sprite("Fx/vignette");
            if (vt != null) vignette.style.backgroundImage = new StyleBackground(vt);
            UpdateLight();

            // a fade in from black when you arrive somewhere new
            if (a.id != fadedArea)
            {
                fadedArea = a.id;
                var fade = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "world-fade");
                fade.schedule.Execute(() => fade.AddToClassList("world-fade-out")).StartingIn(30);
                fade.schedule.Execute(() => fade.RemoveFromHierarchy()).StartingIn(700);
            }
        }

        void UpdateLight()
        {
            if (lightTint == null) return;
            var c = DayLight(out float dark);
            lightTint.style.backgroundColor = c;
            float lamp = Mathf.Clamp01((dark - 0.1f) / 0.4f);
            float t = Now;
            foreach (var (el, phase, strength) in glows)
                el.style.opacity = lamp * strength * (0.8f + 0.12f * Mathf.Sin(t * 7.3f + phase) + 0.08f * Mathf.Sin(t * 13.1f + phase * 2f));
            if (vignette != null) vignette.style.opacity = Mathf.Clamp01(dark * 1.4f) * 0.9f + (engine.Area?.indoor == true ? 0.35f : 0f);
        }

        // ------------------------------------------------------------------ the living world

        void WorldLife(float now)
        {
            UpdateLight();
            var a = engine.Area;
            if (a == null) return;
            int k = worldScale, T = Tile * k;
            DayLight(out float dark);
            string weather = Rules.WeatherNow(engine.S, engine.data)?.id ?? "clear";
            bool wet = weather == "rain" || weather == "storm" || weather == "drizzle";

            // the tiles on screen
            float ox = worldLayer.resolvedStyle.left, oy = worldLayer.resolvedStyle.top;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(-ox / T)), y0 = Mathf.Max(0, Mathf.FloorToInt(-oy / T));
            int x1 = Mathf.Min(Engine.Width(a) - 1, Mathf.CeilToInt((-ox + worldSize.x) / T));
            int y1 = Mathf.Min(Engine.Height(a) - 1, Mathf.CeilToInt((-oy + worldSize.y) / T));
            if (x1 < x0 || y1 < y0) return;

            Vector2 RandomTile(System.Func<char, bool> ok)
            {
                for (int tries = 0; tries < 12; tries++)
                {
                    int x = fxRnd.Next(x0, x1 + 1), y = fxRnd.Next(y0, y1 + 1);
                    if (ok(a.rows[y][x])) return new Vector2((x + RandF(0.15f, 0.85f)) * T, (y + RandF(0.15f, 0.85f)) * T);
                }
                return new Vector2(-1, -1);
            }

            // glints of sun on the water
            if (!a.indoor && dark < 0.3f && !wet && now >= nextGlint)
            {
                nextGlint = now + RandF(0.12f, 0.3f);
                var p = RandomTile(c => c == '~' || c == 'w');
                if (p.x >= 0) Spawn(worldLayer, "fx_glint", 4, 7, 7, k, p, 0.6f);
            }
            // rain splashing on the ground and the water
            if (!a.indoor && wet && now >= nextSplash)
            {
                nextSplash = now + (weather == "drizzle" ? 0.25f : 0.07f);
                var p = RandomTile(c => c != 'H' && c != 'T');
                if (p.x >= 0)
                {
                    char under = a.rows[Mathf.Clamp((int)(p.y / T), 0, a.rows.Count - 1)][Mathf.Clamp((int)(p.x / T), 0, Engine.Width(a) - 1)];
                    if (under == '~' || under == 'w') Spawn(worldLayer, "fx_ripple", 3, 16, 8, k * 0.6f, p, 0.5f);
                    else Spawn(worldLayer, "fx_splash", 4, 12, 8, k * 0.6f, p, 0.35f);
                }
            }
            // fireflies over the grass at night
            if (!a.indoor && dark > 0.35f && !wet && now >= nextFirefly && fxs.Count(f => f.frames == 2 && f.loop) < 14)
            {
                nextFirefly = now + RandF(0.3f, 0.8f);
                var p = RandomTile(c => c == '.' || c == 'g' || c == 'o');
                if (p.x >= 0)
                {
                    var f = Spawn(worldLayer, "fx_firefly", 2, 3, 3, k, p, RandF(4f, 8f), new Vector2(RandF(-6, 6), RandF(-5, 3)) * k * 0.4f, true);
                    if (f != null) { f.wobble = 3f * k; f.fade = 0.4f; }
                }
            }
            // smoke from fires and the forge
            if (now >= nextSmoke)
            {
                nextSmoke = now + 0.45f;
                foreach (var o in engine.Objects())
                    if (o.sprite == "obj_fire" || o.sprite == "obj_brazier_lit") Smoke(new Vector2((o.x + 0.5f) * T, o.y * T), k, dark);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                        if (a.rows[y][x] == 'L') Smoke(new Vector2((x + 0.5f) * T, y * T), k, dark);
            }
            // gulls over the coast, with their shadows on the ground
            if (!a.indoor && dark < 0.3f && weather != "storm" && (a.id == "coast_road" || a.id == "skelmed") && now >= nextBird)
            {
                nextBird = now + RandF(7f, 16f);
                bool right = fxRnd.Next(2) == 0;
                float y = (y0 + RandF(0.2f, 0.8f) * (y1 - y0)) * T;
                float speed = RandF(60f, 90f) * k;
                var start = new Vector2(right ? (x0 - 2) * T : (x1 + 3) * T, y);
                var vel = new Vector2(right ? speed : -speed, RandF(-12f, 12f) * k);
                float life = (x1 - x0 + 6) * T / speed;
                var sh = Spawn(worldLayer, "fx_shadow_bird", 1, 9, 4, k, start + new Vector2(10 * k, 26 * k), life, vel);
                if (sh != null) sh.baseAlpha = 0.6f;
                var bird = Spawn(worldLayer, "fx_bird", 2, 9, 5, k, start, life, vel, true);
                if (bird != null) bird.fade = 0.05f;
                // the gull is drawn heading up the image: turn it (and its shadow) to face where it's flying
                var turn = new StyleRotate(new Rotate(new Angle(right ? 90f : -90f)));
                if (bird != null) bird.box.style.rotate = turn;
                if (sh != null) sh.box.style.rotate = turn;
            }
            // cloud shadows drifting over on fair days
            if (!a.indoor && dark < 0.2f && (weather == "clear" || weather == "overcast" || weather == "warm_spell") && now >= nextCloud)
            {
                nextCloud = now + RandF(6f, 12f);
                float speed = RandF(10f, 18f) * k;
                float y = (y0 + RandF(0f, 1f) * (y1 - y0)) * T;
                var start = new Vector2((x0 - 4) * T, y);
                var c = Spawn(worldLayer, "cloud_shadow", 1, 96, 64, k * RandF(1.2f, 2f), start, (x1 - x0 + 10) * T / speed, new Vector2(speed, speed * 0.15f), true);
                if (c != null) { c.fade = 0.15f; c.baseAlpha = weather == "overcast" ? 0.5f : 1f; }
            }
            // a leaf blowing past now and then on windy or fair days
            if (!a.indoor && dark < 0.3f && now >= nextLeaf)
            {
                nextLeaf = now + RandF(2f, 5f);
                var p = RandomTile(c => c == 'T');
                if (p.x >= 0)
                {
                    var f = Spawn(worldLayer, "fx_leaf", 4, 4, 4, k, p, RandF(2.5f, 4f), new Vector2(RandF(10, 25), RandF(14, 22)) * k, true);
                    if (f != null) f.wobble = 4f * k;
                }
            }
        }

        void Smoke(Vector2 at, int k, float dark)
        {
            var f = Spawn(worldLayer, "fx_smoke", 4, 8, 8, k, at + new Vector2(RandF(-3, 3) * k, 0), RandF(1.6f, 2.4f), new Vector2(RandF(2, 6), -RandF(10, 16)) * k);
            if (f != null) f.baseAlpha = 0.75f;
            if (dark > 0.3f)
            {
                var e = Spawn(worldLayer, "fx_ember", 3, 2, 2, k, at + new Vector2(RandF(-4, 4) * k, 4 * k), RandF(0.8f, 1.4f), new Vector2(RandF(-4, 4), -RandF(14, 24)) * k);
                if (e != null) e.wobble = 2f * k;
            }
        }

        /// <summary>A puff of dust behind you when you run.</summary>
        void StepEffects(Vector2 fromTile, bool run)
        {
            if (!run || worldLayer == null || playerBox == null) return;
            char c = engine.TileAt((int)fromTile.x, (int)fromTile.y);
            if (c == 'p' || c == 'b' || c == 'P' || c == 'M') return;   // no dust off planks
            int k = worldScale, T = Tile * k;
            int at = worldLayer.IndexOf(playerBox);
            if (c == '~' || c == 'w') Spawn(worldLayer, "fx_splash", 4, 12, 8, k, new Vector2((fromTile.x + 0.5f) * T, (fromTile.y + 0.85f) * T), 0.35f, default, false, at);
            else Spawn(worldLayer, "fx_dust", 4, 8, 8, k, new Vector2((fromTile.x + 0.5f) * T, (fromTile.y + 0.8f) * T), 0.4f, new Vector2(0, -6 * k), false, at);
        }

        /// <summary>The golden rings rising round you when you go up a level.</summary>
        void LevelUpEffect()
        {
            if (worldLayer == null || playerBox == null || worldLayer.panel == null) return;
            int k = worldScale, T = Tile * k;
            var s = engine.S;
            Spawn(worldLayer, "fx_levelup", 6, 48, 48, k * 0.75f, new Vector2((s.ax + 0.5f) * T, (s.ay + 0.1f) * T), 1.2f);
        }

        // ------------------------------------------------------------------ fights

        void ScreenFlash(VisualElement over)
        {
            var f = Add(over, new VisualElement { pickingMode = PickingMode.Ignore }, "screen-flash");
            f.schedule.Execute(() => f.AddToClassList("screen-flash-out")).StartingIn(40);
            f.schedule.Execute(() => f.RemoveFromHierarchy()).StartingIn(400);
        }

        /// <summary>The flash and wipe as a fight begins.</summary>
        void BattleIntro(VisualElement over)
        {
            var f = Add(over, new VisualElement { pickingMode = PickingMode.Ignore }, "battle-intro");
            f.schedule.Execute(() => f.AddToClassList("battle-intro-out")).StartingIn(60);
            f.schedule.Execute(() => f.RemoveFromHierarchy()).StartingIn(800);
        }
    }
}
