using System.Collections.Generic;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The map screen: the region in pixel art on the left, under a fog that lifts as you explore, with the places
    /// you know marked on it; on the right where you are, your pace, what you can do here, and the places you know.
    /// Click anywhere on the map to walk there. Your marker walks tile by tile, and stops if anything happens.
    /// </summary>
    public partial class GameUI
    {
        const int Tile = 16;               // pixels per tile in the map picture
        const float SideWidth = 460f;

        static readonly string[] PaceHelp =
        {
            "Slower. You notice more, find hidden places from further off, and trouble finds you less.",
            "A normal pace. You get your breath back as you walk.",
            "Faster, but tiring, and easier to walk into trouble.",
        };

        VisualElement mapLayer, youMarker, pathLayer, hoverBox;
        Label mapTip;
        Vector2 mapView;                   // the viewport's size
        int mapScale = 2;
        bool mapZoomOut;
        Vector2Int? walkTarget;
        bool stopWalk;
        Vector2Int hoverTile = new Vector2Int(-1, -1);
        Texture2D fogTex;
        Vector2 travelSize;

        float TravelHeight()
        {
            float h = mainBox != null ? mainBox.layout.height : float.NaN;
            return float.IsNaN(h) || h < 400 ? 900f : h - 56f;
        }

        float TravelWidth()
        {
            float w = mainBox != null ? mainBox.layout.width : float.NaN;
            return float.IsNaN(w) || w < 600 ? 1700f : w - 112f;
        }

        void RenderTravel()
        {
            var s = engine.S;
            var r = engine.Region;
            var sc = engine.Current;
            main.Clear();
            float height = TravelHeight();
            travelSize = new Vector2(TravelWidth(), height);
            var wrap = Add(main, new VisualElement(), "travel");
            wrap.style.height = height;

            // ---- the map
            mapView = new Vector2(Mathf.Max(300, TravelWidth() - SideWidth - 28), height);
            var view = Add(wrap, new VisualElement(), "map-view");
            view.style.width = mapView.x;
            view.style.height = mapView.y;
            DrawMap(view, r);

            // ---- the side panel
            var side = Add(wrap, new VisualElement(), "travel-side");
            side.style.width = SideWidth;
            side.style.height = height;
            var scroll = Add(side, new ScrollView(ScrollViewMode.Vertical), "travel-scroll");

            Add(scroll, new Label(r.name.ToUpperInvariant()), "place");
            Add(scroll, new Label(sc.title), "title");
            if (!string.IsNullOrEmpty(s.result)) Add(scroll, new Label(s.result), "result");
            foreach (var p in engine.Paragraphs()) Add(scroll, new Label(p), "para");

            Add(scroll, new Label("PACE"), "field-label");
            var paces = Add(scroll, new VisualElement(), "pace-row");
            for (int i = 0; i < Engine.Paces.Length; i++)
            {
                int p = i;
                var b = Add(paces, new Button(() => { if (busy) return; engine.SetPace(p); AfterAction(); }) { text = Engine.PaceNames[p], focusable = false }, "pace-chip");
                if (s.pace == p) b.AddToClassList("pace-on");
            }
            Add(scroll, new Label(PaceHelp[s.pace]), "pace-help");

            shownChoices = engine.Choices();
            if (shownChoices.Count > 0)
            {
                Add(scroll, new Label("HERE"), "field-label");
                var list = Add(scroll, new VisualElement(), "choices");
                for (int i = 0; i < shownChoices.Count; i++)
                {
                    var c = shownChoices[i];
                    var b = new Button(() => Pick(c)) { focusable = false };
                    b.AddToClassList("choice");
                    b.SetEnabled(c.enabled);
                    Add(b, new Label(i < 9 ? (i + 1).ToString() : ""), "choice-num");
                    var text = Add(b, new VisualElement(), "choice-text");
                    Add(text, new Label(engine.Fill(c.def.label)), "choice-label");
                    if (!c.enabled && !string.IsNullOrEmpty(c.reason)) Add(text, new Label(c.reason), "choice-reason");
                    else if (!string.IsNullOrEmpty(c.cost)) Add(text, new Label(c.cost), "choice-meta");
                    list.Add(b);
                }
            }

            var known = engine.KnownPlaces();
            if (known.Count > 0)
            {
                Add(scroll, new Label("PLACES YOU KNOW"), "field-label");
                foreach (var (place, state) in known)
                {
                    var pl = place;
                    bool hereNow = pl.x == s.mapX && pl.y == s.mapY;
                    var row = Add(scroll, new Button(() => { if (!hereNow) StartWalk(new Vector2Int(pl.x, pl.y)); }) { focusable = false }, "place-row");
                    if (hereNow) row.AddToClassList("place-here");
                    var ic = Add(row, new VisualElement(), "place-icon");
                    var tex = Sprite("Map/" + (state == "rumoured" ? "rumour" : pl.kind));
                    if (tex != null)
                    {
                        ic.style.backgroundImage = new StyleBackground(tex);
                        ic.style.width = tex.width * 2;
                        ic.style.height = tex.height * 2;
                    }
                    var col = Add(row, new VisualElement(), "place-text");
                    string sub = hereNow ? "You're here" : state == "rumoured" ? "Heard of, not found" : state == "seen" ? "Seen, not visited" : "Visited";
                    var path = hereNow ? null : engine.PathTo(pl.x, pl.y, out int mins);
                    if (path != null) sub += "  ·  about " + Engine.Duration(RoundTo(MinutesFor(path), 10));
                    Add(col, new Label(pl.name), "place-name");
                    Add(col, new Label(sub), "place-sub");
                    string note = state == "rumoured" && !string.IsNullOrEmpty(pl.rumour) ? pl.rumour : pl.note;
                    if (!string.IsNullOrEmpty(note)) Add(col, new Label(note), "place-note");
                }
            }
        }

        static int RoundTo(int v, int step) => Mathf.Max(step, Mathf.RoundToInt(v / (float)step) * step);

        /// <summary>Roughly how long a path takes at your pace (unknown ground is guessed).</summary>
        int MinutesFor(List<(int x, int y)> path)
        {
            int total = 0, px = engine.S.mapX, py = engine.S.mapY;
            foreach (var (x, y) in path)
            {
                bool diag = x != px && y != py;
                total += engine.Known(x, y) ? engine.StepMinutes(x, y, diag) : Mathf.RoundToInt(Engine.TileMinutes * 1.6f * (diag ? 1.4f : 1f));
                px = x; py = y;
            }
            return total;
        }

        // ------------------------------------------------------------------ drawing the map

        void DrawMap(VisualElement view, RegionDef r)
        {
            var s = engine.S;
            int w = Engine.Width(r), h = Engine.Height(r);
            int fit = Mathf.Max(1, Mathf.FloorToInt(Mathf.Min(mapView.x / (w * Tile), mapView.y / (h * Tile))));
            mapScale = mapZoomOut ? fit : Mathf.Max(fit, mapView.x >= 900 ? 2 : 1);
            int k = mapScale, T = Tile * k;

            mapLayer = Add(view, new VisualElement(), "map-layer");
            mapLayer.style.width = w * T;
            mapLayer.style.height = h * T;
            var pic = Sprite("Maps/" + r.id);
            if (pic != null) mapLayer.style.backgroundImage = new StyleBackground(pic);

            if (engine.Check("night")) Add(mapLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "map-night");

            var fog = Add(mapLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "map-fog");
            fog.style.backgroundImage = new StyleBackground(FogTexture(r));

            pathLayer = Add(mapLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "map-paths");

            // Places you know about
            foreach (var (place, state) in engine.KnownPlaces())
            {
                var tex = Sprite("Map/" + (state == "rumoured" ? "rumour" : place.kind));
                float cx = (place.x + 0.5f) * T, cy = (place.y + 0.5f) * T;
                float iw = 0, ih = 0;
                if (tex != null)
                {
                    int ik = Mathf.Max(2, k + 1);
                    iw = tex.width * ik; ih = tex.height * ik;
                    var icon = Add(mapLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "map-icon");
                    icon.style.backgroundImage = new StyleBackground(tex);
                    icon.style.width = iw;
                    icon.style.height = ih;
                    icon.style.left = cx - iw / 2;
                    icon.style.top = cy - ih / 2;
                }
                var label = Add(mapLayer, new Label(state == "rumoured" ? place.name + "?" : place.name) { pickingMode = PickingMode.Ignore }, "map-label");
                if (state == "rumoured") label.AddToClassList("map-label-rumour");
                else if (state == "seen") label.AddToClassList("map-label-seen");
                label.style.left = cx;
                label.style.top = cy + ih / 2 + 2;
                label.style.translate = new Translate(Length.Percent(-50), 0);
            }

            hoverBox = Add(mapLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "map-hover");
            hoverBox.style.width = T;
            hoverBox.style.height = T;
            hoverBox.style.display = DisplayStyle.None;

            youMarker = Add(mapLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "map-you");
            var you = Sprite("Map/you");
            if (you != null)
            {
                youMarker.style.backgroundImage = new StyleBackground(you);
                youMarker.style.width = you.width * (k + 2);
                youMarker.style.height = you.height * (k + 2);
            }
            PlaceYou(new Vector2(s.mapX, s.mapY));

            mapTip = Add(view, new Label("") { pickingMode = PickingMode.Ignore }, "map-tip");
            mapTip.style.display = DisplayStyle.None;
            Add(view, new Label(walkTarget != null ? "Walking... click or press Esc to stop" : "Click the map to walk  ·  arrow keys step one tile") { pickingMode = PickingMode.Ignore }, "map-hint");
            var zoom = Add(view, new Button(() => { mapZoomOut = !mapZoomOut; Render(); }) { text = mapZoomOut ? "Close up" : "Whole map", focusable = false }, "map-zoom");
            zoom.SetEnabled(fit < Mathf.Max(fit, mapView.x >= 900 ? 2 : 1) || mapZoomOut);

            mapLayer.RegisterCallback<PointerMoveEvent>(e => Hover(TileUnder(e.localPosition)));
            mapLayer.RegisterCallback<PointerLeaveEvent>(_ => Hover(new Vector2Int(-1, -1)));
            mapLayer.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.button != 0) return;
                if (walkTarget != null) { stopWalk = true; return; }
                var t = TileUnder(e.localPosition);
                if (t.x == s.mapX && t.y == s.mapY) return;
                StartWalk(t);
            });
            hoverTile = new Vector2Int(-1, -1);
        }

        Vector2Int TileUnder(Vector3 local) => new Vector2Int(Mathf.FloorToInt(local.x / (Tile * mapScale)), Mathf.FloorToInt(local.y / (Tile * mapScale)));

        /// <summary>Puts your marker on a (possibly in-between) tile, and moves the map so it stays in view.</summary>
        void PlaceYou(Vector2 tile)
        {
            if (youMarker == null || mapLayer == null) return;
            float T = Tile * mapScale;
            float cx = (tile.x + 0.5f) * T, cy = (tile.y + 0.5f) * T;
            float mw = youMarker.style.width.value.value, mh = youMarker.style.height.value.value;
            youMarker.style.left = cx - mw / 2;
            youMarker.style.top = cy - mh + mapScale * 2;

            var r = engine.Region;
            float lw = Engine.Width(r) * T, lh = Engine.Height(r) * T;
            float ox = lw <= mapView.x ? (mapView.x - lw) / 2 : Mathf.Clamp(mapView.x / 2 - cx, mapView.x - lw, 0);
            float oy = lh <= mapView.y ? (mapView.y - lh) / 2 : Mathf.Clamp(mapView.y / 2 - cy, mapView.y - lh, 0);
            mapLayer.style.left = Mathf.Round(ox);
            mapLayer.style.top = Mathf.Round(oy);
        }

        /// <summary>The fog as a picture the same size as the map: dark where you haven't been, with a dithered edge.</summary>
        Texture2D FogTexture(RegionDef r)
        {
            int w = Engine.Width(r), h = Engine.Height(r);
            int pw = w * Tile, ph = h * Tile;
            if (fogTex == null || fogTex.width != pw || fogTex.height != ph)
            {
                if (fogTex != null) Destroy(fogTex);
                fogTex = new Texture2D(pw, ph, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }
            var px = new Color32[pw * ph];
            var dark = new Color32(14, 22, 25, 255);
            var dark2 = new Color32(18, 28, 31, 255);
            var clear = new Color32(0, 0, 0, 0);
            int[] bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            bool K(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && engine.Known(x, y);
            for (int ty = 0; ty < h; ty++)
                for (int tx = 0; tx < w; tx++)
                {
                    bool known = K(tx, ty);
                    bool l = K(tx - 1, ty), rt = K(tx + 1, ty), u = K(tx, ty - 1), d = K(tx, ty + 1);
                    bool ul = K(tx - 1, ty - 1), ur = K(tx + 1, ty - 1), dl = K(tx - 1, ty + 1), dr = K(tx + 1, ty + 1);
                    bool edge = !known && (l || rt || u || d || ul || ur || dl || dr);
                    for (int sy = 0; sy < Tile; sy++)
                        for (int sx = 0; sx < Tile; sx++)
                        {
                            Color32 c;
                            if (known) c = clear;
                            else if (!edge) c = ((sx + sy * 3 + tx * 5 + ty * 7) % 23 == 0) ? dark2 : dark;
                            else
                            {
                                // How far into the fog this pixel is from the lit tiles beside it: 0 at the edge, 1 deep in.
                                float f = 1f;
                                if (l) f = Mathf.Min(f, sx / (float)Tile);
                                if (rt) f = Mathf.Min(f, (Tile - 1 - sx) / (float)Tile);
                                if (u) f = Mathf.Min(f, sy / (float)Tile);
                                if (d) f = Mathf.Min(f, (Tile - 1 - sy) / (float)Tile);
                                if (ul) f = Mathf.Min(f, Mathf.Max(sx, sy) / (float)Tile);
                                if (ur) f = Mathf.Min(f, Mathf.Max(Tile - 1 - sx, sy) / (float)Tile);
                                if (dl) f = Mathf.Min(f, Mathf.Max(sx, Tile - 1 - sy) / (float)Tile);
                                if (dr) f = Mathf.Min(f, Mathf.Max(Tile - 1 - sx, Tile - 1 - sy) / (float)Tile);
                                int X = tx * Tile + sx, Y = ty * Tile + sy;
                                c = f * 16f * 1.15f > bayer[(Y % 4) * 4 + X % 4] + 0.5f ? dark : clear;
                            }
                            int X2 = tx * Tile + sx, Y2 = ph - 1 - (ty * Tile + sy);   // textures count rows from the bottom
                            px[Y2 * pw + X2] = c;
                        }
                }
            fogTex.SetPixels32(px);
            fogTex.Apply(false);
            return fogTex;
        }

        // ------------------------------------------------------------------ hovering

        void Hover(Vector2Int t)
        {
            if (t == hoverTile || mapLayer == null) return;
            hoverTile = t;
            pathLayer.Clear();
            var r = engine.Region;
            if (busy || walkTarget != null || !Engine.InBounds(r, t.x, t.y))
            {
                hoverBox.style.display = DisplayStyle.None;
                mapTip.style.display = DisplayStyle.None;
                return;
            }
            float T = Tile * mapScale;
            hoverBox.style.display = DisplayStyle.Flex;
            hoverBox.style.left = t.x * T;
            hoverBox.style.top = t.y * T;

            var place = engine.KnownPlaces().Select(k => k.place).FirstOrDefault(p => p.x == t.x && p.y == t.y);
            bool known = engine.Known(t.x, t.y);
            string what = place != null ? place.name : known ? engine.TerrainAt(r, t.x, t.y)?.name ?? "" : "Unexplored";
            var path = engine.PathTo(t.x, t.y, out _);
            if (t.x == engine.S.mapX && t.y == engine.S.mapY) mapTip.text = what + "  ·  you're here";
            else if (path == null) mapTip.text = what + "  ·  " + (known ? "you can't walk there" : "no way you know of");
            else
            {
                mapTip.text = what + "  ·  about " + Engine.Duration(RoundTo(MinutesFor(path), 5));
                foreach (var (x, y) in path)
                {
                    var dot = Add(pathLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "map-dot");
                    float sz = Mathf.Max(4, mapScale * 3);
                    dot.style.width = dot.style.height = sz;
                    dot.style.left = (x + 0.5f) * T - sz / 2;
                    dot.style.top = (y + 0.5f) * T - sz / 2;
                    if (!engine.Known(x, y)) dot.AddToClassList("map-dot-fog");
                }
            }
            mapTip.style.display = DisplayStyle.Flex;
        }

        // ------------------------------------------------------------------ walking

        void StartWalk(Vector2Int target)
        {
            if (busy || !engine.Travelling) return;
            var path = engine.PathTo(target.x, target.y, out _);
            if (path == null || path.Count == 0)
            {
                engine.S.Toasts.Add("You can't see a way there.");
                Render();
                return;
            }
            walkTarget = target;
            stopWalk = false;
            Render();   // shows the "walking" hint
            WalkNext();
        }

        void EndWalk()
        {
            walkTarget = null;
            stopWalk = false;
            busy = false;
        }

        /// <summary>Takes one step towards the target: the marker walks to the next tile, then the step happens.</summary>
        void WalkNext()
        {
            var s = engine.S;
            if (walkTarget == null || !engine.Travelling || stopWalk) { EndWalk(); Render(); return; }
            var t = walkTarget.Value;
            var path = engine.PathTo(t.x, t.y, out _);
            if (path == null || path.Count == 0)
            {
                if (path == null) s.Toasts.Add("There's no way through. You'll have to find another route.");
                EndWalk(); Render(); return;
            }
            var next = path[0];
            var step = engine.StepChoice(next.x, next.y);
            if (step == null) { EndWalk(); Render(); return; }
            if (s.stamina < step.stamina)
            {
                s.Toasts.Add("Too winded to go on. Catch your breath, or slow your pace.");
                EndWalk(); Render(); return;
            }

            busy = true;
            var from = new Vector2(s.mapX, s.mapY);
            var to = new Vector2(next.x, next.y);
            float seconds = Mathf.Clamp(step.time / 90f, 0.12f, 0.4f);
            float start = Time.realtimeSinceStartup;
            IVisualElementScheduledItem tick = null;
            tick = root.schedule.Execute(() =>
            {
                float k = Mathf.Clamp01((Time.realtimeSinceStartup - start) / seconds);
                PlaceYou(Vector2.Lerp(from, to, k));
                if (k < 1f) return;
                tick?.Pause();
                busy = false;
                engine.Choose(step);
                bool arrived = s.mapX == t.x && s.mapY == t.y;
                bool happened = !engine.Travelling || !string.IsNullOrEmpty(s.result);
                if (arrived || happened || stopWalk) walkTarget = null;
                SaveNow();
                Render();
                if (walkTarget != null) root.schedule.Execute(WalkNext).StartingIn(20);
                else stopWalk = false;
            }).Every(16);
        }

        /// <summary>Arrow keys step one tile; Escape stops a walk. Returns true if it used the key.</summary>
        bool TravelKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape && walkTarget != null) { stopWalk = true; return true; }
            if (busy || walkTarget != null) return e.keyCode != KeyCode.None && e.keyCode != KeyCode.F1;
            int dx = 0, dy = 0;
            switch (e.keyCode)
            {
                case KeyCode.LeftArrow: dx = -1; break;
                case KeyCode.RightArrow: dx = 1; break;
                case KeyCode.UpArrow: dy = -1; break;
                case KeyCode.DownArrow: dy = 1; break;
                default: return false;
            }
            var s = engine.S;
            if (engine.StepChoice(s.mapX + dx, s.mapY + dy) == null) return true;
            StartWalk(new Vector2Int(s.mapX + dx, s.mapY + dy));
            return true;
        }
    }
}
