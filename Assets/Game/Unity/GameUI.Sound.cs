using System.Collections.Generic;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// What plays when: the music and ambience for where you are (and the time and weather), footsteps by what
    /// you're walking on, and a sound for whatever just happened (worked out by comparing the game before and after).
    /// The sounds themselves are in GameAudio.
    /// </summary>
    public partial class GameUI
    {
        class Heard
        {
            public int coins, water, level;
            public float hunger, thirst, mana;
            public string area, scene;
            public bool battle;
        }
        Heard heard;

        /// <summary>After the screen is drawn: set the music and ambience, and play a sound for anything that just changed.</summary>
        void Sounds()
        {
            var s = engine.S;
            SetSoundscape();
            var now = new Heard
            {
                coins = s.coins, water = s.water, level = s.level, hunger = s.hunger, thirst = s.thirst, mana = s.mana,
                area = s.area, scene = s.scene, battle = InBattle && s.inCombat,
            };
            var was = heard;
            heard = now;
            if (was == null || s.scene == Engine.Title || s.scene == Engine.Creation) return;

            if (now.scene != was.scene)
            {
                if (now.scene == Engine.Death) GameAudio.Play("death");
                else if (now.scene == Engine.Collapse) GameAudio.Play("collapse");
            }
            if (now.battle && !was.battle)
            {
                GameAudio.Play("battle_start");
                if (arena != null) BattleIntro(arena);
                if (engine.data.Enemy(s.enemy)?.art == "wolf_alpha") GameAudio.Play("wolf_howl", 0.9f, 0f, 0.6f);
            }
            if (now.area != was.area && !string.IsNullOrEmpty(now.area) && !string.IsNullOrEmpty(was.area))
            {
                bool inside = engine.data.Area(now.area)?.indoor == true || engine.data.Area(was.area)?.indoor == true;
                if (inside) GameAudio.Play("door", 0.8f);
            }
            if (was.level != now.level) return;   // the level-up fanfare covers it

            bool spell = now.mana < was.mana - 0.5f;
            if (spell) GameAudio.Play(now.water > was.water ? "spell_water" : "spell_kindle");
            else if (now.water > was.water) GameAudio.Play("water_fill");
            if (now.coins != was.coins) GameAudio.Play("coin");
            if (now.hunger > was.hunger + 1.5f) GameAudio.Play("eat");
            else if (now.thirst > was.thirst + 1.5f) GameAudio.Play("drink");
        }

        /// <summary>Music and ambience for where you are, the time of day and the weather.</summary>
        void SetSoundscape()
        {
            var s = engine.S;
            var amb = new Dictionary<string, float>();
            string music;
            var area = engine.Area;
            bool special = s.scene == Engine.Title || s.scene == Engine.Creation;
            bool night = !special && engine.Check("night");
            string weather = special ? "" : Rules.WeatherNow(s, engine.data)?.id ?? "";

            if (InCutscene)
            {
                GameAudio.SetMusic("");     // the cutscene sets its own ambience as it plays (GameUI.Cutscene.cs)
                return;
            }
            if (special)
            {
                music = "mus_title";
                amb["amb_sea"] = 0.35f;
            }
            else if (s.scene == Engine.Death) music = "";
            else
            {
                if (InBattle) music = "mus_battle";
                else if (s.scene == Engine.ForgeId) music = "mus_forge";
                else if (area == null) music = "mus_road";
                else if (area.indoor) music = night ? "mus_night" : "mus_title";
                else if (night) music = "mus_night";
                else music = area.id == "skelmed" ? "mus_village" : "mus_road";

                if (s.scene == Engine.ForgeId)
                {
                    amb["amb_fire"] = 0.9f;
                    amb["amb_village"] = 0.35f;
                }
                else if (area != null && area.indoor)
                {
                    amb["amb_indoor"] = 0.9f;
                    if (weather == "rain" || weather == "storm" || weather == "drizzle") amb["amb_rain"] = 0.3f;
                }
                else
                {
                    float scale = InBattle ? 0.5f : 1f;   // quieter under the battle music
                    amb[night ? "amb_night" : "amb_day"] = 0.7f * scale;
                    string id = area?.id ?? "";
                    if (id == "skelmed") amb["amb_village"] = 0.8f * scale;
                    else if (id == "coast_road") amb["amb_sea"] = 0.6f * scale;
                    if (weather == "drizzle") amb["amb_rain"] = 0.45f * scale;
                    if (weather == "rain") amb["amb_rain"] = 0.85f * scale;
                    if (weather == "storm") { amb["amb_rain"] = 1f * scale; amb["amb_wind"] = 0.7f * scale; }
                    if (weather == "fog") amb["amb_wind"] = 0.3f * scale;
                }
            }
            GameAudio.SetMusic(music);
            GameAudio.SetAmbience(amb);
        }

        /// <summary>A footstep on whatever's underfoot.</summary>
        void StepSound(int x, int y, bool run)
        {
            char t = engine.TileAt(x, y);
            string surface = t switch
            {
                ',' => "step_sand",
                '=' => "step_dirt",
                'c' or 's' or 'D' => "step_stone",
                'b' or 'P' or 'p' or 'M' => "step_wood",
                _ => "step_grass",
            };
            GameAudio.PlayVariant(surface, 3, run ? 1f : 0.75f);
        }

        /// <summary>A sound for a message that pops up.</summary>
        static void ToastSound(string t)
        {
            if (t.StartsWith("Level up")) GameAudio.Play("levelup");
            else if (t.StartsWith("New quest")) GameAudio.Play("quest_new");
            else if (t.StartsWith("Quest complete")) GameAudio.Play("quest_done");
            else if (t.StartsWith("New spell") || t.StartsWith("New move") || t.Contains(" rose to ")) GameAudio.Play("skill_up");
            else if (t.StartsWith("New gear") || t.StartsWith("Forged") || (t.StartsWith("+") && !t.Contains("EXP"))) GameAudio.Play("pickup");
            else if (t.StartsWith("Saved.")) GameAudio.Play("sleep");
            else if (t.Contains(" is dead")) GameAudio.Play("defeat");
            else if (!t.StartsWith("+") && !t.StartsWith("-")) GameAudio.Play("toast", 0.7f);
        }

        /// <summary>Any button click gets a soft tick.</summary>
        void ClickSounds()
        {
            root.RegisterCallback<ClickEvent>(e =>
            {
                if (e.target is Button b && b.enabledSelf) GameAudio.Play("ui_click", 0.8f);
            }, TrickleDown.TrickleDown);
        }

        /// <summary>The SOUND section: four volume sliders.</summary>
        void SoundSettings(VisualElement parent)
        {
            var sec = Section(parent, "SOUND");
            Volume(sec, "Master", GameAudio.Master, v => GameAudio.Master = v);
            Volume(sec, "Music", GameAudio.Music, v => GameAudio.Music = v);
            Volume(sec, "Effects", GameAudio.Effects, v => { GameAudio.Effects = v; });
            Volume(sec, "Ambience", GameAudio.Ambience, v => GameAudio.Ambience = v);
        }

        void Volume(VisualElement parent, string name, float value, System.Action<float> set)
        {
            var row = Add(parent, new VisualElement(), "volume-row");
            Add(row, new Label(name), "volume-name");
            var slider = Add(row, new Slider(0f, 1f) { value = value, focusable = false }, "volume-slider");
            var pct = Add(row, new Label(Mathf.RoundToInt(value * 100) + "%"), "volume-value");
            slider.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                pct.text = Mathf.RoundToInt(e.newValue * 100) + "%";
                GameAudio.SaveVolumes();
            });
            slider.RegisterCallback<PointerUpEvent>(_ => GameAudio.Play("ui_tick"), TrickleDown.TrickleDown);
        }
    }
}
