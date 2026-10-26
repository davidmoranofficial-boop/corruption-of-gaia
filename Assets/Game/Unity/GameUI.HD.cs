using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The 3D-rendered world (25 Oct test of how far the art can go). An area with a folder in Resources/Sprites/HD
    /// is drawn from pictures rendered in Blender (Tools/Blender) instead of the pixel-art tiles:
    /// the ground in a few big bands, plus, for each tile row, a cut-out of everything standing on that row
    /// (trees, houses, the wreck). Those cut-outs are drawn in order with the people, so you walk behind a tree
    /// when you're north of it and in front of it when you're south of it.
    /// F9 switches between the 3D pictures and the old pixel art, to compare.
    /// </summary>
    public partial class GameUI
    {
        [Serializable] class HdPiece { public string img; public int row, x, y, w, h; }
        [Serializable] class HdLayout { public int ppt, w, h; public List<HdPiece> ground = new List<HdPiece>(); public List<HdPiece> occluders = new List<HdPiece>(); }

        static readonly Dictionary<string, HdLayout> hdLayouts = new Dictionary<string, HdLayout>();
        static readonly Dictionary<string, Texture2D> hdCache = new Dictionary<string, Texture2D>();
        const string HdOffKey = "CorruptionOfGaia.HdOff";
        static bool HdOff => PlayerPrefs.GetInt(HdOffKey, 0) == 1;

        /// <summary>The 3D pictures for an area, or null when it has none (or they're switched off).</summary>
        static HdLayout Hd(string areaId)
        {
            if (HdOff || string.IsNullOrEmpty(areaId)) return null;
            if (hdLayouts.TryGetValue(areaId, out var l)) return l;
            var txt = Resources.Load<TextAsset>("Sprites/HD/" + areaId + "/layout");
            l = txt != null ? JsonUtility.FromJson<HdLayout>(txt.text) : null;
            if (l != null && l.ppt <= 0) l = null;
            hdLayouts[areaId] = l;
            return l;
        }

        /// <summary>A rendered picture: smooth filtering, not the sharp pixel-art kind.</summary>
        static Texture2D HdSprite(string path)
        {
            if (hdCache.TryGetValue(path, out var tex)) return tex;
            tex = Resources.Load<Texture2D>("Sprites/" + path);
            if (tex != null) { tex.filterMode = FilterMode.Trilinear; tex.wrapMode = TextureWrapMode.Clamp; }
            hdCache[path] = tex;
            return tex;
        }

        /// <summary>The rendered walking sheet for a figure drawn from these pixel-art layers, or null to keep the pixel art.
        /// You are one sheet per figure for now (what you wear isn't shown yet); people have one sheet each.</summary>
        string HdWalkSheet(List<string> sheets)
        {
            if (engine.Area == null || Hd(engine.Area.id) == null || sheets == null) return null;
            foreach (var p in sheets)
            {
                string hd = null;
                if (p.StartsWith("Walk/body_")) hd = "HD/Walk/player_" + (p.Contains("female") ? "female" : "male");
                else if (p.StartsWith("Walk/npc_")) hd = "HD/Walk/" + p.Substring(5);
                if (hd == null) continue;
                return HdSprite(hd) != null ? hd : null;
            }
            return null;
        }

        /// <summary>A rendered walker. The box is the same as a pixel-art walker's (a tile wide, a tile and a half tall)
        /// so everything that moves walkers keeps working; inside it, a bigger frame (1.5 x 2 tiles) shows the picture.</summary>
        (VisualElement box, VisualElement sheet) HdWalker(VisualElement parent, int k, string sheetPath)
        {
            var box = Add(parent, new VisualElement { pickingMode = PickingMode.Ignore }, "walker-hd");
            box.style.width = 16 * k;
            box.style.height = 24 * k;
            var frame = Add(box, new VisualElement { pickingMode = PickingMode.Ignore }, "walker-hd-frame");
            frame.style.left = -4 * k;
            frame.style.top = -8 * k;
            frame.style.width = 24 * k;
            frame.style.height = 32 * k;
            var sheet = Add(frame, new VisualElement { pickingMode = PickingMode.Ignore }, "walker-sheet");
            sheet.AddToClassList("hd-sheet");
            sheet.style.width = 72 * k;
            sheet.style.height = 128 * k;
            sheet.style.backgroundImage = new StyleBackground(HdSprite(sheetPath));
            return (box, sheet);
        }

        // the opening cutscene filmed in 3D: Sprites/HD/Cutscene/<shot>_<frame>.jpg (Tools/Blender/cutscene.py)
        const int FilmStormFps = 8, FilmFps = 10, FilmStormFrames = 132, FilmBeachFrames = 140, FilmShackFrames = 120;

        /// <summary>One frame of the filmed cutscene, or null when there's no film (or the 3D art is switched off).</summary>
        static Texture2D HdFilm(string shot, int frame)
        {
            if (HdOff) return null;
            var tex = Resources.Load<Texture2D>("Sprites/HD/Cutscene/" + shot + "_" + frame.ToString("000"));
            if (tex != null) { tex.filterMode = FilterMode.Bilinear; tex.wrapMode = TextureWrapMode.Clamp; }
            return tex;
        }

        /// <summary>Lets a frame that's been shown go, so the whole film isn't held in memory.</summary>
        static void HdFilmDrop(string key)
        {
            var tex = Resources.Load<Texture2D>("Sprites/HD/Cutscene/" + key);
            if (tex != null) Resources.UnloadAsset(tex);
        }

        void ToggleHd()
        {
            PlayerPrefs.SetInt(HdOffKey, HdOff ? 0 : 1);
            PlayerPrefs.Save();
            engine.S.Toasts.Add(HdOff ? "Pixel-art world (F9 for 3D)" : "3D-rendered world (F9 for pixel art)");
            Render();
        }

        /// <summary>Lays the ground bands into the world layer, scaled so ppt pixels fill one tile.</summary>
        void DrawHdGround(HdLayout l, string areaId, int T)
        {
            float f = (float)T / l.ppt;
            foreach (var g in l.ground)
            {
                var e = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "hd-ground");
                var tex = HdSprite("HD/" + areaId + "/" + g.img);
                if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
                e.style.left = 0;
                e.style.top = g.y * f;
                e.style.width = l.w * f;
                e.style.height = g.h * f + 1;   // a pixel of overlap hides any seam between bands
            }
        }

        void DrawHdOccluder(HdPiece p, HdLayout l, string areaId, int T)
        {
            float f = (float)T / l.ppt;
            var e = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "hd-thing");
            var tex = HdSprite("HD/" + areaId + "/" + p.img);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            e.style.left = p.x * f;
            e.style.top = p.y * f;
            e.style.width = p.w * f;
            e.style.height = p.h * f;
        }
    }
}
