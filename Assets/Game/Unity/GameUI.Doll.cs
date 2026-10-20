using System.Collections.Generic;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// Pixel-art sprites, and the paper doll: your character drawn as a stack of same-sized layers
    /// (body, then each piece of gear you wear, with hair under the helmet), so whatever you wear shows.
    /// The art lives in Resources/Sprites and is made by Tools/Art/build.py.
    /// </summary>
    public partial class GameUI
    {
        public const int DollW = 32, DollH = 48;

        static readonly Dictionary<string, Texture2D> spriteCache = new Dictionary<string, Texture2D>();

        /// <summary>A sprite from Resources/Sprites, set to keep its pixels sharp. Null if there's no such art.</summary>
        static Texture2D Sprite(string path)
        {
            if (spriteCache.TryGetValue(path, out var tex)) return tex;
            tex = Resources.Load<Texture2D>("Sprites/" + path);
            if (tex != null) { tex.filterMode = FilterMode.Point; tex.wrapMode = TextureWrapMode.Clamp; }
            spriteCache[path] = tex;
            return tex;
        }

        static string Key(string s) => string.IsNullOrEmpty(s) ? "" : s.Trim().ToLowerInvariant().Replace(' ', '_');

        /// <summary>The order worn gear is drawn in, front of the body. The off hand is drawn behind it.</summary>
        static readonly string[] DollFront = { "legs", "feet", "body", "hands", "neck", "#beard", "#hair", "head", "weapon" };

        /// <summary>The art name of a hair style; none chosen means short hair, or long on the feminine figure.</summary>
        static string HairStyleKey(string style, string figure)
        {
            string k = Key(style);
            return k != "" ? k : figure == "female" ? "long" : "short";
        }

        static string HairColourKey(string hair)
        {
            string k = Key(hair);
            return k == "" || k == "shaved" ? "dark_brown" : k;
        }

        /// <summary>The beard's art name, or null for none.</summary>
        static string BeardKey(string beard)
        {
            string k = Key(beard);
            return k == "" || k == "none" ? null : k;
        }

        /// <summary>Draws a character at a whole-number scale. worn: (gear id, slot) of everything they have on.</summary>
        VisualElement Doll(VisualElement parent, int scale, string figure, string skin, string hair, IEnumerable<(string id, string slot)> worn,
                           string hairStyle = null, string beard = null)
        {
            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("doll");
            box.style.width = DollW * scale;
            box.style.height = DollH * scale;
            parent.Add(box);

            figure = figure == "female" ? "female" : "male";
            string skinKey = Key(skin);
            if (skinKey == "") skinKey = "light";
            var list = new List<(string id, string slot)>(worn);

            foreach (var w in list) if (w.slot == "offhand") Layer(box, Sprite("Gear/" + w.id));
            Layer(box, Sprite("Body/body_" + figure + "_" + skinKey) ?? Sprite("Body/body_" + figure + "_light"));
            foreach (var slot in DollFront)
            {
                if (slot == "#beard")
                {
                    var b = BeardKey(beard);
                    if (b != null) Layer(box, Sprite("Hair/beard_" + b + "_" + HairColourKey(hair)));
                    continue;
                }
                if (slot == "#hair")
                {
                    string st = HairStyleKey(hairStyle, figure);
                    bool capped = list.Exists(w => w.slot == "head");
                    if (st != "bald") Layer(box, Sprite("Hair/hair_" + st + "_" + HairColourKey(hair) + (capped ? "_cap" : "")));
                    continue;
                }
                foreach (var w in list) if (w.slot == slot) Layer(box, Sprite("Gear/" + w.id));
            }
            return box;
        }

        /// <summary>The player as they are now.</summary>
        VisualElement PlayerDoll(VisualElement parent, int scale)
        {
            var s = engine.S;
            var worn = new List<(string, string)>();
            foreach (var g in s.Equipped) worn.Add((g.id, g.slot));
            return Doll(parent, scale, PlayerFigure(), s.skin, s.hair, worn, s.hairStyle, s.beard);
        }

        string PlayerFigure() => (string.IsNullOrEmpty(engine.S.figure) ? engine.S.gender : engine.S.figure) == "female" ? "female" : "male";

        /// <summary>The order the walking sprite's layers stack in, after the body. Weapons and shields also have a
        /// "_back" layer drawn behind the body (an axe across your back, a knife on the far hip).</summary>
        static readonly string[] WalkFront = { "legs", "feet", "body", "hands", "neck", "#beard", "#hair", "head", "weapon", "offhand" };

        /// <summary>The walking sprite as a stack of same-sized sheets (Sprites/Walk, Sprites/WalkGear; made by Tools/Art/walk.py):
        /// the body, then whatever's worn, so what you have on is what you see walking about.</summary>
        static List<string> WalkLayers(string figure, string skin, string hair, string hairStyle, string beard, IEnumerable<(string id, string slot)> worn)
        {
            figure = figure == "female" ? "female" : "male";
            string skinKey = Key(skin) == "" ? "light" : Key(skin);
            var list = new List<(string id, string slot)>(worn);
            var paths = new List<string>();
            foreach (var w in list)
                if (w.slot == "weapon" || w.slot == "offhand") paths.Add("WalkGear/" + w.id + "_" + figure + "_back");
            paths.Add("Walk/body_" + figure + "_" + skinKey);
            foreach (var slot in WalkFront)
            {
                if (slot == "#beard")
                {
                    var b = BeardKey(beard);
                    if (b != null) paths.Add("Walk/beard_" + b + "_" + HairColourKey(hair));
                    continue;
                }
                if (slot == "#hair")
                {
                    string st = HairStyleKey(hairStyle, figure);
                    if (st != "bald") paths.Add("Walk/hair_" + st + "_" + HairColourKey(hair) + (list.Exists(w => w.slot == "head") ? "_cap" : ""));
                    continue;
                }
                foreach (var w in list) if (w.slot == slot) paths.Add("WalkGear/" + w.id + "_" + figure);
            }
            return paths;
        }

        List<string> PlayerWalkLayers()
        {
            var s = engine.S;
            var worn = new List<(string, string)>();
            foreach (var g in s.Equipped) worn.Add((g.id, g.slot));
            return WalkLayers(PlayerFigure(), s.skin, s.hair, s.hairStyle, s.beard, worn);
        }

        static void Layer(VisualElement box, Texture2D tex)
        {
            if (tex == null) return;
            var l = new VisualElement { pickingMode = PickingMode.Ignore };
            l.AddToClassList("doll-layer");
            l.style.backgroundImage = new StyleBackground(tex);
            box.Add(l);
        }

        /// <summary>A 16 x 16 item icon at a whole-number scale, or an empty frame if the item has none yet.</summary>
        static VisualElement Icon(VisualElement parent, string gearId, int scale)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.AddToClassList("item-icon");
            e.style.width = e.style.height = 16 * scale;
            var tex = Sprite("Icons/" + gearId);
            if (tex != null) e.style.backgroundImage = new StyleBackground(tex);
            parent.Add(e);
            return e;
        }

        /// <summary>An enemy's sprite at the biggest whole-number scale that fits the box. Null if it has no art.</summary>
        static VisualElement EnemySprite(VisualElement parent, EnemyDef e, float fit)
        {
            var tex = Sprite("Enemies/" + (string.IsNullOrEmpty(e.art) ? e.id : e.art));
            if (tex == null) return null;
            int k = Mathf.Max(1, Mathf.FloorToInt(fit / Mathf.Max(tex.width, tex.height)));
            var pic = new VisualElement { pickingMode = PickingMode.Ignore };
            pic.AddToClassList("enemy-sprite");
            pic.style.width = tex.width * k;
            pic.style.height = tex.height * k;
            pic.style.backgroundImage = new StyleBackground(tex);
            parent.Add(pic);
            return pic;
        }

        /// <summary>A quick red flash on a sprite or doll when it's hit.</summary>
        static void HitFlash(VisualElement target)
        {
            if (target == null) return;
            target.AddToClassList("sprite-hit");
            target.schedule.Execute(() => target.RemoveFromClassList("sprite-hit")).StartingIn(160);
        }
    }
}
