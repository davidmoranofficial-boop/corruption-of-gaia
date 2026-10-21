using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// The wolf on the coast road (David, 21 Oct): you walk up the road, the wolf goes for you and gets its teeth in
    /// your arm, two arrows, and Ida comes down the road from Skelmed and sends you to Bess. Played on the real
    /// coast road (its ground and tree tops, cropped round the spot below the headland) with the walking sprites.
    /// </summary>
    public partial class GameUI
    {
        const float WolfEnd = 28f;
        // the crop of the coast road shown: tile (6, 33) at the top left (the road runs up the middle at tiles 15-16)
        const int WolfCropX = 6 * 16, WolfCropY = 33 * 16;

        static readonly (float from, float to, int line, bool centre)[] WolfCaptions =
        {
            (1.0f, 5.5f, 0, false), (6.2f, 10.0f, 1, false), (10.2f, 14.0f, 2, false),
            (14.4f, 20.0f, 3, false), (20.4f, 26.6f, 4, false),
        };

        static readonly (float at, string id, float vol)[] WolfSounds =
        {
            (3.4f, "wolf_growl", 0.9f), (5.5f, "wolf_snarl", 1f), (6.1f, "player_hurt", 1f), (6.15f, "hit", 0.8f),
            (8.5f, "swing", 0.6f), (8.85f, "hit_glance", 0.7f), (9.3f, "swing", 0.6f), (9.6f, "beast_hurt", 1f),
            (9.9f, "enemy_die", 0.7f),
        };

        (VisualElement box, VisualElement sheet) cutIda;

        void WolfBuild()
        {
            CutLayer("ground", "World/coast_road", 512, 1920);
            CutArrow("a1");
            CutArrow("a2");
            CutLayer("wolf", "World/obj_wolf", 16, 16);
            cutYou = CutWalkerAdd(PlayerWalkLayers());
            cutIda = CutWalkerAdd(new List<string> { "Walk/npc_ida" });
            CutLayer("over", "World/coast_road_over", 512, 1920);
        }

        /// <summary>An arrow, flying straight down the road: a shaft, a pale head below it and fletching on top.</summary>
        void CutArrow(string key)
        {
            var colours = new (string part, int w, int h, Color c)[]
            {
                ("_shaft", 1, 8, new Color32(0x6b, 0x4a, 0x2b, 255)), ("_tip", 1, 2, new Color32(0xd8, 0xd8, 0xd0, 255)),
                ("_fletch", 3, 2, new Color32(0xe8, 0xe0, 0xd0, 255)),
            };
            foreach (var (part, w, h, c) in colours)
                CutLayer(key + part, "", w, h).style.backgroundColor = c;
        }

        void CutPlaceArrow(string key, float x, float y, bool show)
        {
            // (x, y) is the tip
            CutPlace(key + "_tip", x, y - 2, show);
            CutPlace(key + "_shaft", x, y - 10, show);
            CutPlace(key + "_fletch", x - 1, y - 11, show);
        }

        void WolfFrame(float t)
        {
            float fade = 0, flash;
            if (t < 1.2f) fade = 1 - t / 1.2f;
            if (t > WolfEnd - 1.6f) fade = Mathf.Clamp01((t - (WolfEnd - 1.6f)) / 1.2f);

            CutPlace("ground", -WolfCropX, -WolfCropY);
            CutPlace("over", -WolfCropX, -WolfCropY);

            // ---- you: up the road from the bottom, stop when you see it; bitten and down; up again once Ida's done
            const float youX = 152, youStop = 128;
            float walk = Mathf.Min(t, 3f);
            float youY = Mathf.Lerp(190, youStop, walk / 3f);
            bool down = t >= 6.1f && t < 20.2f;
            cutYou.box.style.rotate = new Rotate(new Angle(down ? 90 : 0));
            if (down) CutWalker(cutYou, youX - 4, youStop + 6, "down", 0, true);
            else if (t < 3f) CutWalker(cutYou, youX, youY, "up", WalkCycle[(int)(t * 7f) % 4], true);
            else CutWalker(cutYou, youX, youStop, t > 20.2f ? "right" : "up", 0, true);

            // ---- the wolf: in the road, staring; the lunge; on top of you, worrying at your arm; the arrows; still
            float wx = 152, wy = 74, wolfRot = 0;
            if (t >= 5.7f && t < 6.1f)
            {
                float u = (t - 5.7f) / 0.4f;
                wy = Mathf.Lerp(74, 124, u * u);
            }
            else if (t >= 6.1f && t < 9.6f)
            {
                wy = 124 + (Mathf.Sin(t * 31f) > 0 ? 1 : 0);
                wx = 150 + (Mathf.Sin(t * 23f) > 0 ? 1 : -1);
            }
            else if (t >= 9.6f)
            {
                // hit: it leaps back off you and drops in the road
                float u = Mathf.Clamp01((t - 9.6f) / 0.35f);
                wx = Mathf.Lerp(150, 164, u);
                wy = Mathf.Lerp(124, 112, u) - Mathf.Sin(u * Mathf.PI) * 6;
                wolfRot = u >= 1 ? 90 : 0;
            }
            CutPlace("wolf", wx, wy);
            cutEls["wolf"].style.rotate = new Rotate(new Angle(wolfRot));

            // ---- the arrows, down the road from the north: the first into the dirt, the second into the wolf
            float a1 = Mathf.Clamp01((t - 8.5f) / 0.35f), a2 = Mathf.Clamp01((t - 9.25f) / 0.35f);
            CutPlaceArrow("a1", 170, Mathf.Lerp(-12, 132, a1), t >= 8.5f);
            float a2y = Mathf.Lerp(-12, 128, a2);
            // once it's in the wolf it goes where the wolf goes
            if (t >= 9.6f) CutPlaceArrow("a2", wx + 9, wy + 6, true);
            else CutPlaceArrow("a2", 159, a2y, t >= 9.25f);

            // ---- Ida: down the road from Skelmed, bow in hand, and kneels by you
            bool idaIn = t >= 10.6f;
            float idaY = Mathf.Min(-24 + Mathf.Max(0, t - 10.6f) * 34f, 112);
            bool idaThere = idaY >= 112;
            int idaFrame = idaThere ? 0 : WalkCycle[(int)(Mathf.Max(0, t - 10.6f) * 7f) % 4];
            CutWalker(cutIda, 172, idaY, idaThere ? (t > 14.2f ? "left" : "down") : "down", idaFrame, idaIn);

            // ---- the bite: a red flash and the picture jolts
            float red = t >= 6.1f && t < 6.9f ? 1 - (t - 6.1f) / 0.8f : 0;
            cutFlash.style.backgroundColor = new Color(0.55f, 0.05f, 0.05f, 1);
            flash = red * 0.55f;
            float shake = t >= 6.1f && t < 6.6f ? (1 - (t - 6.1f) / 0.5f) * 3f : 0;
            cutView.style.translate = new Translate(Mathf.Round(Mathf.Sin(t * 90f) * shake) * cutK, Mathf.Round(Mathf.Cos(t * 70f) * shake) * cutK);

            cutFade.style.opacity = fade;
            cutFlash.style.opacity = flash;

            cutAmb.Clear();
            cutAmb["amb_sea"] = 0.35f;
            cutAmb["amb_wind"] = 0.35f;
            cutAmb["amb_day"] = 0.5f;
            GameAudio.SetAmbience(cutAmb);
        }
    }
}
