using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The road screen: a map of the route on the left with your marker on it, and on the right where you are,
    /// what's happened, the three paces to walk on at, and what you can do at this stop.
    /// </summary>
    public partial class GameUI
    {
        const float WalkSeconds = 0.75f;   // how long the marker takes to walk one step on the map

        static readonly Dictionary<string, string> PaceHelp = new Dictionary<string, string>
        {
            { "careful", "Slower. You spot more, and less catches you off guard." },
            { "steady", "A normal pace. You get your breath back as you walk." },
            { "hurried", "Faster, but tiring, and easier to walk into trouble." },
        };

        VisualElement youMarker;

        void RenderTravel()
        {
            var s = engine.S;
            var r = engine.Route;
            var sc = engine.Current;
            main.Clear();
            var wrap = Add(main, new VisualElement(), "travel");

            var map = Add(wrap, new VisualElement(), "route-map");
            if (engine.Check("night")) map.AddToClassList("route-night");
            DrawRoute(map, r, engine.RoutePosition(r, s.travelAt));

            var side = Add(wrap, new VisualElement(), "travel-side");
            Add(side, new Label(r.name.ToUpperInvariant()), "place");
            Add(side, new Label(sc.title), "title");
            if (!string.IsNullOrEmpty(s.result)) Add(side, new Label(s.result), "result");
            foreach (var p in engine.Paragraphs()) Add(side, new Label(p), "para");

            var all = engine.Choices();
            var walks = all.Where(c => c.def.effects.Count > 0 && c.def.effects[0].StartsWith("travel:walk:")).ToList();
            var rest = all.Where(c => !walks.Contains(c)).ToList();
            shownChoices = walks.Concat(rest).ToList();
            int n = 1;

            if (walks.Count > 0)
            {
                Add(side, new Label("WALK ON TOWARDS " + engine.NextPlaceName.ToUpperInvariant()), "field-label");
                var row = Add(side, new VisualElement(), "pace-row");
                foreach (var cv in walks)
                {
                    var c = cv;
                    string pace = c.def.effects[0].Substring("travel:walk:".Length);
                    var b = new Button(() => Pick(c)) { focusable = false };
                    b.AddToClassList("pace");
                    b.SetEnabled(c.enabled);
                    var top = Add(b, new VisualElement(), "pace-top");
                    Add(top, new Label(n++.ToString()), "choice-num");
                    Add(top, new Label(c.def.label), "pace-name");
                    Add(b, new Label(c.enabled ? c.cost : c.reason), c.enabled ? "choice-meta" : "choice-reason");
                    Add(b, new Label(PaceHelp.TryGetValue(pace, out var help) ? help : ""), "pace-help");
                    row.Add(b);
                }
            }

            if (rest.Count > 0)
            {
                Add(side, new Label("HERE"), "field-label");
                var list = Add(side, new VisualElement(), "choices");
                foreach (var cv in rest)
                {
                    var c = cv;
                    var b = new Button(() => Pick(c)) { focusable = false };
                    b.AddToClassList("choice");
                    b.SetEnabled(c.enabled);
                    Add(b, new Label(n <= 9 ? n.ToString() : ""), "choice-num");
                    n++;
                    var text = Add(b, new VisualElement(), "choice-text");
                    Add(text, new Label(engine.Fill(c.def.label)), "choice-label");
                    if (!c.enabled && !string.IsNullOrEmpty(c.reason)) Add(text, new Label(c.reason), "choice-reason");
                    else if (!string.IsNullOrEmpty(c.cost)) Add(text, new Label(c.cost), "choice-meta");
                    list.Add(b);
                }
            }
        }

        // ------------------------------------------------------------------ the map

        void DrawRoute(VisualElement map, RouteDef r, float pos)
        {
            // The sea: strips down the left edge that follow the line of the road, so the coast has a shape.
            const int strips = 25;
            for (int i = 0; i < strips; i++)
            {
                float y = (i + 0.5f) * 100f / strips;
                float coast = Mathf.Max(4f, CoastAt(r, y) - 9f + Mathf.Sin(i * 1.7f) * 2.5f);
                var strip = Add(map, new VisualElement(), "sea");
                strip.style.top = Length.Percent(i * 100f / strips);
                strip.style.height = Length.Percent(100f / strips + 0.5f);
                strip.style.width = Length.Percent(coast);
            }
            var seaLabel = Add(map, new Label("THE SEA"), "sea-label");

            // The road: a dotted trail. Dots you've already walked past are lit.
            for (int leg = 0; leg < r.stops.Count - 1; leg++)
            {
                var a = r.stops[leg];
                var b = r.stops[leg + 1];
                float dist = Mathf.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                int dots = Mathf.Max(3, Mathf.RoundToInt(dist / 2.6f));
                for (int k = 1; k < dots; k++)
                {
                    float f = k / (float)dots;
                    var dot = Add(map, new VisualElement(), "trail-dot");
                    dot.style.left = Length.Percent(Mathf.Lerp(a.x, b.x, f));
                    dot.style.top = Length.Percent(Mathf.Lerp(a.y, b.y, f));
                    float at = leg + f;
                    if (engine.S.travelDir > 0 ? at <= pos : at >= pos) dot.AddToClassList("trail-walked");
                }
            }

            // Stops
            for (int i = 0; i < r.stops.Count; i++)
            {
                var st = r.stops[i];
                var node = Add(map, new VisualElement(), "stop-node");
                node.style.left = Length.Percent(st.x);
                node.style.top = Length.Percent(st.y);
                if (Mathf.Abs(pos - i) < 0.01f) node.AddToClassList("stop-here");
                var label = Add(map, new Label(st.name), "stop-label");
                label.style.left = Length.Percent(st.x + 3f);
                label.style.top = Length.Percent(st.y - 1.8f);
            }

            youMarker = Add(map, new VisualElement(), "you-marker");
            PlaceMarker(r, pos);
        }

        /// <summary>The x position of the road at a given height on the map, to draw the coastline beside it.</summary>
        static float CoastAt(RouteDef r, float y)
        {
            var pts = r.stops.OrderBy(s => s.y).ToList();
            if (y <= pts[0].y) return pts[0].x;
            for (int i = 0; i < pts.Count - 1; i++)
                if (y <= pts[i + 1].y)
                    return Mathf.Lerp(pts[i].x, pts[i + 1].x, (y - pts[i].y) / Mathf.Max(0.01f, pts[i + 1].y - pts[i].y));
            return pts[pts.Count - 1].x;
        }

        static Vector2 MapPoint(RouteDef r, float pos)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(pos), 0, r.stops.Count - 2);
            float f = Mathf.Clamp01(pos - i);
            var a = r.stops[i];
            var b = r.stops[i + 1];
            return new Vector2(Mathf.Lerp(a.x, b.x, f), Mathf.Lerp(a.y, b.y, f));
        }

        void PlaceMarker(RouteDef r, float pos)
        {
            if (youMarker == null) return;
            var p = MapPoint(r, pos);
            youMarker.style.left = Length.Percent(p.x);
            youMarker.style.top = Length.Percent(p.y);
        }

        // ------------------------------------------------------------------ walking

        /// <summary>Walks the marker one step along the map, then takes the step (which may turn up an event).</summary>
        void WalkStep(ChoiceView cv)
        {
            var r = engine.Route;
            var s = engine.S;
            float from = engine.RoutePosition(r, s.travelAt);
            float to = engine.RoutePosition(r, s.travelAt + s.travelDir);
            busy = true;
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = root.schedule.Execute(() =>
            {
                float t = Mathf.Clamp01((Time.realtimeSinceStartup - start) / WalkSeconds);
                PlaceMarker(r, Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t)));
                if (t < 1f) return;
                tick?.Pause();
                busy = false;
                engine.Choose(cv.def);
                AfterAction();
            }).Every(16);
        }
    }
}
