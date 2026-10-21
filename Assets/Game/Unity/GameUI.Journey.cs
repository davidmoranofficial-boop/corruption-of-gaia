using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The journey screen (David, 21 Oct): a long road between towns, walked side-on. The land scrolls past, the sky
    /// turns from day to night, the clock runs, and you stop to eat, rest, or make camp when the light goes.
    /// </summary>
    public partial class GameUI
    {
        VisualElement journeyLand, journeyFar, journeyMarker, journeyFire;
        Label journeyWhen, journeyWhere;
        VisualElement journeySheet;
        readonly List<(VisualElement el, float x, float speed)> journeyScenery = new List<(VisualElement, float, float)>();
        float journeyScroll, journeyLastTick, journeyHudAt, journeySavedAt;
        int journeyK = 6;

        static readonly Dictionary<string, (Color far, Color near, Color ground, string[] props)> JourneyLooks = new Dictionary<string, (Color, Color, Color, string[])>
        {
            { "heath",  (new Color(0.36f, 0.42f, 0.36f), new Color(0.42f, 0.47f, 0.30f), new Color(0.47f, 0.48f, 0.30f), new[] { "obj_cairn", "obj_stump", "obj_yarrow", "obj_yarrow", "obj_log" }) },
            { "forest", (new Color(0.16f, 0.27f, 0.22f), new Color(0.20f, 0.33f, 0.20f), new Color(0.27f, 0.36f, 0.21f), new[] { "obj_tree", "obj_tree", "obj_tree", "obj_willow", "obj_log", "obj_stump" }) },
            { "hills",  (new Color(0.40f, 0.44f, 0.46f), new Color(0.38f, 0.45f, 0.33f), new Color(0.43f, 0.46f, 0.32f), new[] { "obj_cairn", "obj_tree", "obj_stump", "obj_yarrow" }) },
            { "farm",   (new Color(0.42f, 0.50f, 0.34f), new Color(0.55f, 0.56f, 0.30f), new Color(0.58f, 0.55f, 0.32f), new[] { "obj_tree", "obj_crate", "obj_barrel", "obj_yarrow", "obj_sign" }) },
        };

        void RenderJourney()
        {
            var s = engine.S;
            var j = engine.Journey;
            main.Clear();
            objectSprites.Clear();
            worldLayer = null;
            glows.Clear();
            journeyScenery.Clear();
            worldSize = new Vector2(TravelWidth(), TravelHeight());
            float W = worldSize.x, H = worldSize.y;
            journeyK = Mathf.Max(4, Mathf.FloorToInt(H / 150f));
            int k = journeyK;
            var leg = engine.Leg;
            var look = JourneyLooks.TryGetValue(leg?.look ?? "", out var lk) ? lk : JourneyLooks["heath"];

            worldView = Add(main, new VisualElement(), "world-view");
            worldView.AddToClassList("journey");
            worldView.style.width = W;
            worldView.style.height = H;

            // Sky, sun or moon, far hills, near ground and the road
            var sky = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-sky");
            sky.style.backgroundColor = SkyColour();
            float horizon = H * 0.55f;
            var orb = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-orb");
            PlaceOrb(orb, W, horizon);
            journeyFar = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-layer");
            var rnd = new System.Random((j?.id ?? "").GetHashCode() ^ (leg?.name ?? "").GetHashCode());
            for (int i = 0; i < 9; i++)
            {
                var hill = Add(journeyFar, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-hill");
                float hw = W * (0.25f + (float)rnd.NextDouble() * 0.3f), hh = H * (0.10f + (float)rnd.NextDouble() * 0.14f);
                hill.style.width = hw;
                hill.style.height = hh * 2;
                hill.style.top = horizon - hh;
                hill.style.backgroundColor = look.far;
                journeyScenery.Add((hill, i * W * 0.28f, 0.12f));
            }
            var land = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-ground");
            land.style.top = horizon;
            land.style.backgroundColor = look.ground;
            var near = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-ground");
            near.style.top = horizon;
            near.style.bottom = StyleKeyword.Auto;
            near.style.height = H * 0.06f;
            near.style.backgroundColor = look.near;
            float roadY = H * 0.74f;
            var road = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-road");
            road.style.top = roadY;
            road.style.height = H * 0.07f;

            // Things by the road, sliding past as you walk: some behind the road, some in front of it
            journeyLand = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-layer");
            for (int i = 0; i < 14; i++)
            {
                string id = look.props[rnd.Next(look.props.Length)];
                var tex = Sprite("World/" + id);
                if (tex == null) continue;
                bool front = i % 3 == 0;
                int pk = front ? k + 1 : k - 1;
                var p = Add(journeyLand, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-prop");
                p.style.backgroundImage = new StyleBackground(tex);
                p.style.width = tex.width * pk;
                p.style.height = tex.height * pk;
                float baseY = front ? roadY + H * 0.16f : roadY - H * 0.02f;
                p.style.top = baseY - tex.height * pk;
                journeyScenery.Add((p, i * W * 0.16f + (float)rnd.NextDouble() * W * 0.1f, front ? 1.25f : 0.8f));
            }

            // You, walking the road (your party's with you; as in the world, only you are drawn)
            var you = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-you");
            you.style.left = W * 0.42f;
            you.style.top = roadY + H * 0.035f - 24 * k;
            var (_, sheet) = Walker(you, k, PlayerWalkLayers());
            journeySheet = sheet;
            SetFrame(sheet, s.journeyBack ? "left" : "right", 0, k);
            journeyFire = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-prop");
            var fireTex = Sprite("World/obj_campfire");
            if (fireTex != null)
            {
                journeyFire.style.backgroundImage = new StyleBackground(fireTex);
                journeyFire.style.width = fireTex.width * k;
                journeyFire.style.height = fireTex.height * k;
                journeyFire.style.left = W * 0.42f + (s.journeyBack ? -20 : 20) * k;
                journeyFire.style.top = roadY + H * 0.05f - fireTex.height * k;
                var glow = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "world-glow");
                var gt = Sprite("Fx/glow_warm");
                if (gt != null) glow.style.backgroundImage = new StyleBackground(gt);
                float g = 60 * k;
                glow.style.width = g;
                glow.style.height = g;
                glow.style.left = W * 0.42f + (s.journeyBack ? -12 : 28) * k - g / 2f;
                glow.style.top = roadY - g / 2f;
                glows.Add((glow, 0f, 1f));
            }
            journeyFire.style.display = !s.journeyWalking && engine.Dark ? DisplayStyle.Flex : DisplayStyle.None;

            // Night falls over all of it
            lightTint = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "world-light");
            vignette = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "world-vignette");
            var vt = Sprite("Fx/vignette");
            if (vt != null) vignette.style.backgroundImage = new StyleBackground(vt);
            UpdateLight();
            if (engine.Area != null) DrawWeather(engine.Area, k);

            // Along the top: the road from end to end, where you are on it, the day and the hour
            var bar = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "journey-bar");
            var ends = Add(bar, new VisualElement(), "journey-ends");
            Add(ends, new Label(j.from.ToUpperInvariant()), "journey-town");
            var track = Add(ends, new VisualElement(), "journey-track");
            foreach (var st in j.stops)
            {
                var tick = Add(track, new VisualElement(), "journey-tick");
                tick.style.left = Length.Percent(st.mile / j.miles * 100f);
            }
            journeyMarker = Add(track, new VisualElement(), "journey-marker");
            Add(ends, new Label(j.to.ToUpperInvariant()), "journey-town");
            journeyWhen = Add(bar, new Label(""), "journey-when");
            journeyWhere = Add(bar, new Label(""), "journey-where");
            UpdateJourneyBar();

            var help = Add(worldView, new Label(s.journeyWalking ? "Space  stop     I  inventory     H  survival     Tab  menus"
                                                                  : "Space  walk on     R  make camp     I  inventory     Tab  menus"), "world-help");

            // The box at the bottom: something met on the road, a message, or what to do now you've stopped
            shownChoices = new List<ChoiceView>();
            dlgDirector = false;
            if (s.scene != Engine.JourneyId)
            {
                var sc = engine.Current;
                DialogueBox(sc.title, s.result, engine.Paragraphs(), engine.Choices(), false, false, sc.speaker);
            }
            else if (!string.IsNullOrEmpty(s.result)) DialogueBox(null, s.result, null, null, true);
            else if (!s.journeyWalking) DialogueBox(engine.Leg?.name ?? j.name, null, null, engine.Choices(), false, true);
            bool talking = worldView.Q(className: "dialogue") != null;
            help.style.display = talking ? DisplayStyle.None : DisplayStyle.Flex;
            hud.EnableInClassList("hud-talking", talking);

            PlaceJourney();
            journeyLastTick = Now;
            if (worldTick == null) worldTick = root.schedule.Execute(WorldTick).Every(16);
        }

        /// <summary>The sky's colour at this hour: night, dawn, day, dusk.</summary>
        Color SkyColour()
        {
            float h = (engine.S.minutes % 1440) / 60f;
            var night = new Color(0.05f, 0.08f, 0.15f);
            var dawn = new Color(0.85f, 0.55f, 0.42f);
            var day = new Color(0.55f, 0.74f, 0.86f);
            var dusk = new Color(0.78f, 0.42f, 0.30f);
            if (h < 4.5f || h >= 21f) return night;
            if (h < 6f) return Color.Lerp(night, dawn, (h - 4.5f) / 1.5f);
            if (h < 8f) return Color.Lerp(dawn, day, (h - 6f) / 2f);
            if (h < 18f) return day;
            if (h < 19.5f) return Color.Lerp(day, dusk, (h - 18f) / 1.5f);
            return Color.Lerp(dusk, night, (h - 19.5f) / 1.5f);
        }

        /// <summary>The sun crosses the sky from 6 to 20; the moon has the night.</summary>
        void PlaceOrb(VisualElement orb, float W, float horizon)
        {
            float h = (engine.S.minutes % 1440) / 60f;
            bool sun = h >= 6f && h < 20f;
            float u = sun ? (h - 6f) / 14f : ((h + 24f - 20f) % 24f) / 10f;
            orb.EnableInClassList("journey-moon", !sun);
            float size = horizon * 0.16f;
            orb.style.width = size;
            orb.style.height = size;
            orb.style.left = W * (0.08f + 0.84f * u) - size / 2f;
            orb.style.top = horizon - Mathf.Sin(Mathf.Clamp01(u) * Mathf.PI) * horizon * 0.75f - size * 0.3f;
        }

        /// <summary>Slides the scenery along by how far you've walked (wrapping round), and moves the marker.</summary>
        void PlaceJourney()
        {
            float W = worldSize.x;
            float span = W * 2.3f;
            float dir = engine.S.journeyBack ? -1f : 1f;
            foreach (var (el, x, speed) in journeyScenery)
            {
                float at = x - journeyScroll * speed * dir;
                at = ((at % span) + span) % span - W * 0.3f;
                el.style.left = at;
            }
        }

        void UpdateJourneyBar()
        {
            var s = engine.S;
            var j = engine.Journey;
            if (j == null || journeyMarker == null) return;
            journeyMarker.style.left = Length.Percent(engine.JourneyProgress * 100f);
            journeyWhen.text = "DAY " + engine.JourneyDay + " ON THE ROAD  ·  " + Rules.Clock(s) + "  ·  " + Rules.TimeOfDay(s).ToUpperInvariant();
            string going = s.journeyWalking ? (s.journeyDark ? "Walking on in the dark" : "Walking") : "Stopped";
            journeyWhere.text = (engine.Leg?.name ?? j.name) + "  ·  " + going + "  ·  " + Engine.Miles(engine.MilesLeft) + " to " + engine.JourneyTarget;
        }

        /// <summary>Every frame on the road: walk, scroll the land, and redraw when something happens.</summary>
        void JourneyTick()
        {
            float now = Now;
            float dt = Mathf.Min(0.25f, now - journeyLastTick);
            journeyLastTick = now;
            var s = engine.S;
            UpdateLight();
            MoveWeather();
            if (InBattle || openMenu != "" || s.scene != Engine.JourneyId || !string.IsNullOrEmpty(s.result) || !s.journeyWalking) return;
            bool happened = engine.March(dt);
            if (!engine.OnJourney || s.scene != Engine.JourneyId || !string.IsNullOrEmpty(s.result) || !s.journeyWalking || InBattle)
            {
                SaveNow();
                Render();
                return;
            }
            journeyScroll += dt * 60f * journeyK / 6f;
            PlaceJourney();
            if (journeySheet != null)
            {
                int[] steps = { 1, 0, 2, 0 };
                SetFrame(journeySheet, s.journeyBack ? "left" : "right", steps[(int)(now * 6f) % 4], journeyK);
            }
            if (happened && now - journeyHudAt > 0.4f)
            {
                journeyHudAt = now;
                UpdateJourneyBar();
                RenderHud();
                var sky = worldView?.Q(className: "journey-sky");
                if (sky != null) sky.style.backgroundColor = SkyColour();
                var orb = worldView?.Q(className: "journey-orb");
                if (orb != null) PlaceOrb(orb, worldSize.x, worldSize.y * 0.55f);
            }
            if (now - journeySavedAt > 30f) { journeySavedAt = now; SaveNow(); }
        }

        /// <summary>Keys on the road. Returns true if it used the key.</summary>
        bool JourneyKeyDown(KeyDownEvent e)
        {
            var s = engine.S;
            bool confirm = e.keyCode == KeyCode.Space || e.keyCode == KeyCode.E || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (confirm && DialoguePaging && DialogueAdvance()) return true;
            if (s.scene != Engine.JourneyId) return false;
            if (!string.IsNullOrEmpty(s.result))
            {
                if (confirm) { DismissMessage(); return true; }
                return false;
            }
            if (s.journeyWalking && (confirm || e.keyCode == KeyCode.Escape))
            {
                engine.Halt(false);
                GameAudio.Play("ui_tick");
                SaveNow();
                Render();
                return true;
            }
            if (!s.journeyWalking && (e.keyCode == KeyCode.Space || e.keyCode == KeyCode.E) && !engine.Dark)
            {
                engine.Halt(true);
                GameAudio.Play("ui_tick");
                Render();
                return true;
            }
            if (!s.journeyWalking && e.keyCode == KeyCode.R)
            {
                var camp = engine.Choices().FirstOrDefault(c => c.enabled && c.def.effects.Contains("journey:camp"));
                if (camp != null) { Pick(camp); return true; }
            }
            return false;
        }
    }
}
