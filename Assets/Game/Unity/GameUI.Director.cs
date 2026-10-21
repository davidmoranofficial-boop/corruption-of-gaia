using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Loran.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Loran.UnityLayer
{
    /// <summary>
    /// Cutscenes played in the real world (David, 21 Oct): a scene with a "script" is acted out on the map, people
    /// walking, turning, emoting and talking in the normal dialogue box while the camera pans, then the scene's first
    /// choice is taken. Space moves the talk on; Esc skips to the end.
    ///
    /// Script steps, one per line ("id" is you, a person or thing in the area by its object id, or one made with spawn;
    /// "Name" in say is who's talking, which also picks the portrait):
    ///   say:Name:words            narrate:words           wait:seconds
    ///   walk:id:x:y[:nowait][:run]                         face:id:up|down|left|right
    ///   spawn:id:sheet:x:y[:facing]   (sheet npc_rook = a walking sprite, obj_wolf = a still picture)
    ///   hide:id   show:id   lie:id   stand:id   emote:id:! ? ... ♥ ~ ♪
    ///   camera:x:y[:seconds]   camera:id[:seconds]   (follows id)    fade:out|in[:seconds]
    ///   flash:red|white[:seconds]   shake[:seconds]   sound:id[:volume]   arrow:x0:y0:x1:y1[:seconds]
    /// Tiles may be fractions (x 15.5).
    /// </summary>
    public partial class GameUI
    {
        class CastMember
        {
            public string id, sheet;
            public bool still, shown = true;
            public Vector2 pos;
            public string facing = "down";
            public float rot;
            public List<Vector2> path;
            public float walkStart, stepSeconds;
            public VisualElement box, img;
        }

        string dirScene;                 // the scene being acted out, or null
        List<string> dirSteps;
        int dirStep;
        bool dirStarted, dirSayDone;
        float dirStepStart;
        readonly Dictionary<string, CastMember> actors = new Dictionary<string, CastMember>();
        string camFollow = "you";
        Vector2 camFrom, camTo;
        float camStart, camSeconds;
        bool camFixed;
        float fadeFrom, fadeTo, fadeStart, fadeSeconds, fadeNow;
        Color flashColour;
        float flashStart = -10, flashSeconds, shakeStart = -10, shakeSeconds;
        readonly List<(VisualElement el, CastMember who, float until)> emotes = new List<(VisualElement, CastMember, float)>();
        readonly List<(VisualElement el, Vector2 from, Vector2 to, float start, float secs)> arrows = new List<(VisualElement, Vector2, Vector2, float, float)>();
        VisualElement dirFadeEl, dirFlashEl;

        bool DirectorActive => dirScene != null && engine.S.scene == dirScene;

        bool HasScript => engine.Current != null && engine.Current.script != null && engine.Current.script.Count > 0 && !engine.S.inCombat;

        static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        /// <summary>Called from RenderWorld when the scene has a script: starts it, or redraws it as it stands.</summary>
        void DirectorDraw()
        {
            var sc = engine.Current;
            if (dirScene != sc.id)
            {
                dirScene = sc.id;
                dirSteps = sc.script.ToList();
                dirStep = 0;
                dirStarted = false;
                actors.Clear();
                camFollow = "you";
                camFixed = false;
                fadeNow = fadeFrom = fadeTo = 0;
                fadeSeconds = 0;
                flashStart = shakeStart = -10;
                actors["you"] = new CastMember { id = "you", pos = new Vector2(engine.S.ax, engine.S.ay), facing = engine.S.facing };
                // your party isn't drawn out in the world, only you are; in a cutscene they step out from behind you
                foreach (var m in engine.S.party)
                {
                    var f = engine.data.Follower(m.id);
                    if (f == null || string.IsNullOrEmpty(f.sprite)) continue;
                    actors[m.id] = new CastMember { id = m.id, sheet = f.sprite, pos = new Vector2(m.x, m.y), facing = m.facing };
                }
            }
            emotes.Clear();
            arrows.Clear();
            int k = worldScale;

            // your party walks in the world but not in a cutscene: whoever's in it is spawned as an actor
            foreach (var (box, _) in partyWalkers) if (box != null) box.style.display = DisplayStyle.None;
            hud.style.display = DisplayStyle.None;

            // the people and things in it: drawn by us, the area's own sprites of them hidden
            var over = worldLayer.Q(className: "world-over");
            foreach (var a in actors.Values)
            {
                a.box = null;
                if (a.id == "you") continue;
                if (objectSprites.TryGetValue(a.id, out var os)) os.box.style.display = DisplayStyle.None;
                MakeActorSprite(a, over);
            }

            Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "dir-bar").AddToClassList("dir-bar-top");
            Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "dir-bar").AddToClassList("dir-bar-bottom");
            dirFlashEl = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "dir-cover");
            dirFadeEl = Add(worldView, new VisualElement { pickingMode = PickingMode.Ignore }, "dir-cover");
            dirFadeEl.style.backgroundColor = Color.black;
            Add(worldView, new Label("Space  next      Esc  skip"), "dir-hint");

            // a line being said when the screen was redrawn: say it again
            if (dirStarted && dirStep < dirSteps.Count && IsTalk(dirSteps[dirStep]) && !dirSayDone) OpenTalk(dirSteps[dirStep]);
            DirectorFrame();
        }

        void MakeActorSprite(CastMember a, VisualElement before)
        {
            int k = worldScale;
            if (a.still)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList("world-thing");
                var tex = Sprite("World/" + a.sheet);
                if (tex != null) { e.style.backgroundImage = new StyleBackground(tex); e.style.width = tex.width * k; e.style.height = tex.height * k; }
                else { e.style.width = 16 * k; e.style.height = 16 * k; }
                if (before != null) worldLayer.Insert(worldLayer.IndexOf(before), e); else worldLayer.Add(e);
                a.box = e;
            }
            else
            {
                var w = Walker(worldLayer, k, new List<string> { "Walk/" + a.sheet });
                if (before != null) { w.box.RemoveFromHierarchy(); worldLayer.Insert(worldLayer.IndexOf(before), w.box); }
                a.box = w.box;
                a.img = w.sheet;
            }
        }

        CastMember GetActor(string id)
        {
            if (actors.TryGetValue(id, out var a)) return a;
            var o = engine.Area.objects.FirstOrDefault(x => x.id == id);
            if (o == null) return null;
            a = new CastMember { id = id, sheet = o.sprite, still = !o.sprite.StartsWith("npc_"), pos = new Vector2(o.x, o.y), facing = o.facing };
            actors[id] = a;
            if (objectSprites.TryGetValue(id, out var os)) os.box.style.display = DisplayStyle.None;
            MakeActorSprite(a, worldLayer.Q(className: "world-over"));
            return a;
        }

        static bool IsTalk(string step) => step.StartsWith("say:") || step.StartsWith("narrate:");

        void OpenTalk(string step)
        {
            worldView.Q(className: "dialogue")?.RemoveFromHierarchy();
            string who = null, words;
            if (step.StartsWith("say:"))
            {
                var p = step.Split(new[] { ':' }, 3);
                who = p[1];
                words = p.Length > 2 ? p[2] : "";
            }
            else words = step.Substring("narrate:".Length);
            dlgKey = "";
            dlgDirector = true;
            DialogueBox(null, null, new List<string> { engine.Fill(words) }, null, false, false, who);
            // a click on the last page moves the cutscene on, like Space
            var box = worldView.Q(className: "dialogue");
            box?.RegisterCallback<PointerDownEvent>(e => { if (!DialoguePaging) dirSayDone = true; }, TrickleDown.TrickleDown);
        }

        void StartStep(string step)
        {
            var p = step.Split(':');
            float now = Time.realtimeSinceStartup;
            dirStepStart = now;
            dirSayDone = false;
            CastMember a;
            switch (p[0])
            {
                case "say":
                case "narrate":
                    OpenTalk(step);
                    break;
                case "spawn":
                    a = new CastMember { id = p[1], sheet = p[2], still = !p[2].StartsWith("npc_"), pos = new Vector2(F(p[3]), F(p[4])), facing = p.Length > 5 ? p[5] : "down" };
                    if (actors.TryGetValue(a.id, out var old) && old.box != null) old.box.RemoveFromHierarchy();
                    actors[a.id] = a;
                    MakeActorSprite(a, worldLayer.Q(className: "world-over"));
                    break;
                case "hide": if ((a = GetActor(p[1])) != null) a.shown = false; break;
                case "show": if ((a = GetActor(p[1])) != null) a.shown = true; break;
                case "lie": if ((a = GetActor(p[1])) != null) a.rot = 90; break;
                case "stand": if ((a = GetActor(p[1])) != null) a.rot = 0; break;
                case "face": if ((a = GetActor(p[1])) != null) a.facing = p[2]; break;
                case "walk":
                    if ((a = GetActor(p[1])) == null) break;
                    var to = new Vector2(F(p[2]), F(p[3]));
                    var from = a.pos;
                    var grid = NpcPath(Vector2Int.RoundToInt(from), Vector2Int.RoundToInt(to), false);
                    a.path = new List<Vector2> { from };
                    if (grid != null && grid.Count > 1) foreach (var g in grid.Skip(1)) a.path.Add(g);
                    else a.path.Add(Vector2Int.RoundToInt(to));
                    a.path[a.path.Count - 1] = to;
                    a.walkStart = now;
                    a.stepSeconds = p.Contains("run") ? 0.16f : 0.3f;
                    break;
                case "emote":
                    if ((a = GetActor(p[1])) == null) break;
                    var bubble = Add(worldLayer, new Label(p.Length > 2 ? p[2] : "!"), "dir-emote");
                    bubble.style.fontSize = 9 * worldScale;
                    emotes.Add((bubble, a, now + 1.5f));
                    GameAudio.Play("ui_tick", 0.6f);
                    break;
                case "camera":
                    camFrom = CamPoint();
                    camStart = now;
                    if (p.Length >= 3 && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out _) && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        camTo = new Vector2(F(p[1]), F(p[2]));
                        camFixed = true;
                        camSeconds = p.Length > 3 ? F(p[3]) : 1f;
                    }
                    else
                    {
                        camFollow = p[1];
                        camFixed = false;
                        camSeconds = p.Length > 2 ? F(p[2]) : 1f;
                    }
                    break;
                case "fade":
                    fadeFrom = fadeNow;
                    fadeTo = p[1] == "out" ? 1 : 0;
                    fadeStart = now;
                    fadeSeconds = p.Length > 2 ? F(p[2]) : 0.8f;
                    break;
                case "flash":
                    flashColour = p.Length > 1 && p[1] == "red" ? new Color(0.6f, 0.04f, 0.04f) : Color.white;
                    flashStart = now;
                    flashSeconds = p.Length > 2 ? F(p[2]) : 0.6f;
                    break;
                case "shake":
                    shakeStart = now;
                    shakeSeconds = p.Length > 1 ? F(p[1]) : 0.4f;
                    break;
                case "sound":
                    GameAudio.Play(p[1], p.Length > 2 ? F(p[2]) : 1f, 0.02f);
                    break;
                case "arrow":
                {
                    var el = Add(worldLayer, new VisualElement { pickingMode = PickingMode.Ignore }, "dir-arrow");
                    el.style.width = 1 * worldScale;
                    el.style.height = 10 * worldScale;
                    arrows.Add((el, new Vector2(F(p[1]), F(p[2])), new Vector2(F(p[3]), F(p[4])), now, p.Length > 5 ? F(p[5]) : 0.35f));
                    GameAudio.Play("swing", 0.6f, 0.05f);
                    break;
                }
            }
        }

        bool StepDone(string step)
        {
            var p = step.Split(':');
            float t = Time.realtimeSinceStartup - dirStepStart;
            switch (p[0])
            {
                case "say":
                case "narrate": return dirSayDone;
                case "wait": return t >= F(p[1]);
                case "walk":
                    if (p.Contains("nowait")) return true;
                    var a = GetActor(p[1]);
                    return a == null || a.path == null;
                case "camera": return t >= camSeconds;
                case "fade": return t >= fadeSeconds;
                default: return true;
            }
        }

        /// <summary>Every frame while a script runs: on through the steps, then everyone moved and drawn.</summary>
        void DirectorTick()
        {
            if (!DirectorActive || worldView == null || worldView.panel == null) return;
            for (int guard = 0; guard < 50 && dirStep < dirSteps.Count; guard++)
            {
                if (!dirStarted) { StartStep(dirSteps[dirStep]); dirStarted = true; }
                if (!StepDone(dirSteps[dirStep])) break;
                if (IsTalk(dirSteps[dirStep])) worldView.Q(className: "dialogue")?.RemoveFromHierarchy();
                dirStep++;
                dirStarted = false;
            }
            if (dirStep >= dirSteps.Count) { DirectorFinish(); return; }
            DirectorFrame();
        }

        Vector2 CamPoint()
        {
            if (camFixed) return camTo;
            var a = actors.TryGetValue(camFollow, out var f) ? f : actors["you"];
            return a.pos;
        }

        void DirectorFrame()
        {
            float now = Time.realtimeSinceStartup;
            int k = worldScale, T = Tile * k;

            foreach (var a in actors.Values)
            {
                int frame = 0;
                if (a.path != null)
                {
                    float t = (now - a.walkStart) / a.stepSeconds;
                    int i = Mathf.FloorToInt(t);
                    if (i >= a.path.Count - 1) { a.pos = a.path[a.path.Count - 1]; a.path = null; }
                    else
                    {
                        float frac = t - i;
                        Vector2 from = a.path[i], to = a.path[i + 1];
                        a.pos = Vector2.Lerp(from, to, frac);
                        var d = to - from;
                        if (d.sqrMagnitude > 0.0001f)
                            a.facing = Mathf.Abs(d.x) > Mathf.Abs(d.y) ? (d.x > 0 ? "right" : "left") : (d.y > 0 ? "down" : "up");
                        frame = frac < 0.5f ? 1 + i % 2 : 0;
                    }
                }
                var box = a.id == "you" ? playerBox : a.box;
                var sheet = a.id == "you" ? playerSheet : a.img;
                if (box == null) continue;
                box.style.display = a.shown ? DisplayStyle.Flex : DisplayStyle.None;
                box.style.rotate = new Rotate(new Angle(a.rot));
                if (a.still)
                {
                    float w = box.resolvedStyle.width > 0 ? box.resolvedStyle.width : T, h = box.resolvedStyle.height > 0 ? box.resolvedStyle.height : T;
                    box.style.left = Mathf.Round(a.pos.x * T + (T - w) / 2);
                    box.style.top = Mathf.Round(a.pos.y * T + T - h);
                    // a still picture faces down or is flipped to face the other way
                    box.style.scale = new Scale(new Vector3(a.facing == "left" ? -1 : 1, 1, 1));
                }
                else
                {
                    box.style.left = Mathf.Round(a.pos.x * T);
                    box.style.top = Mathf.Round(a.pos.y * T - 8 * k);
                    if (sheet != null) SetFrame(sheet, a.facing, frame);
                }
            }

            // emote bubbles over heads
            for (int i = emotes.Count - 1; i >= 0; i--)
            {
                var (el, who, until) = emotes[i];
                if (now > until || el.panel == null) { el.RemoveFromHierarchy(); emotes.RemoveAt(i); continue; }
                el.style.left = Mathf.Round(who.pos.x * T + 2 * k);
                el.style.top = Mathf.Round(who.pos.y * T - 22 * k - Mathf.Abs(Mathf.Sin((until - now) * 6f)) * 2 * k);
            }
            // arrows in flight
            for (int i = arrows.Count - 1; i >= 0; i--)
            {
                var (el, from, to, start, secs) = arrows[i];
                float u = (now - start) / secs;
                if (u >= 1f || el.panel == null) { el.RemoveFromHierarchy(); arrows.RemoveAt(i); GameAudio.Play("hit_glance", 0.5f, 0.05f); continue; }
                var at = Vector2.Lerp(from, to, u);
                var d = to - from;
                el.style.left = Mathf.Round(at.x * T);
                el.style.top = Mathf.Round(at.y * T);
                el.style.rotate = new Rotate(new Angle(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - 90));
            }

            // the camera: where it's looking, eased from where it was
            float cu = camSeconds <= 0 ? 1 : Mathf.Clamp01((now - camStart) / camSeconds);
            cu = cu * cu * (3 - 2 * cu);
            var look = Vector2.Lerp(camFrom == Vector2.zero && camStart == 0 ? CamPoint() : camFrom, CamPoint(), camStart == 0 ? 1 : cu);
            float shake = now - shakeStart < shakeSeconds ? (1 - (now - shakeStart) / shakeSeconds) * 3 * k : 0;
            DirCamera(look, new Vector2(Mathf.Sin(now * 90f) * shake, Mathf.Cos(now * 70f) * shake));

            // fades and flashes
            float fu = fadeSeconds <= 0 ? 1 : Mathf.Clamp01((now - fadeStart) / fadeSeconds);
            fadeNow = Mathf.Lerp(fadeFrom, fadeTo, fu);
            if (dirFadeEl != null) dirFadeEl.style.opacity = fadeNow;
            if (dirFlashEl != null)
            {
                float fl = now - flashStart < flashSeconds ? 1 - (now - flashStart) / flashSeconds : 0;
                dirFlashEl.style.backgroundColor = flashColour;
                dirFlashEl.style.opacity = fl * 0.6f;
            }
        }

        void DirCamera(Vector2 tile, Vector2 jolt)
        {
            int T = Tile * worldScale;
            var a = engine.Area;
            float lw = Engine.Width(a) * T, lh = Engine.Height(a) * T;
            float cx = (tile.x + 0.5f) * T, cy = (tile.y + 0.5f) * T;
            float ox = lw <= worldSize.x ? (worldSize.x - lw) / 2 : Mathf.Clamp(worldSize.x / 2 - cx, worldSize.x - lw, 0);
            float oy = lh <= worldSize.y ? (worldSize.y - lh) / 2 : Mathf.Clamp(worldSize.y / 2 - cy, worldSize.y - lh, 0);
            worldLayer.style.left = Mathf.Round(ox + jolt.x);
            worldLayer.style.top = Mathf.Round(oy + jolt.y);
        }

        void DirectorFinish()
        {
            if (dirScene == null) return;
            dirScene = null;
            dlgDirector = false;
            // you stay where the cutscene left you
            if (actors.TryGetValue("you", out var you))
            {
                var at = Vector2Int.RoundToInt(you.path != null ? you.path[you.path.Count - 1] : you.pos);
                if (Engine.InBounds(engine.Area, at.x, at.y) && !engine.Blocked(at.x, at.y)) { engine.S.ax = at.x; engine.S.ay = at.y; }
                engine.S.facing = you.facing;
            }
            worldView?.Q(className: "dialogue")?.RemoveFromHierarchy();
            var next = engine.Choices().FirstOrDefault(c => c.enabled);
            if (next != null) engine.Choose(next.def);
            AfterAction();
        }

        bool DirectorKey(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Escape) { GameAudio.Play("ui_confirm"); DirectorFinish(); return true; }
            bool confirm = e.keyCode == KeyCode.Space || e.keyCode == KeyCode.E || e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
            if (confirm && dirStarted && dirStep < dirSteps.Count && IsTalk(dirSteps[dirStep]))
            {
                if (DialoguePaging && DialogueAdvance()) return true;
                dirSayDone = true;
                GameAudio.Play("ui_tick", 0.5f);
            }
            return true;
        }
    }
}
