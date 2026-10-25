using System;
using System.Collections.Generic;
using System.Linq;

namespace Loran.Core
{
    /// <summary>
    /// Wounds and ailments in play: fights leave them, you treat them (bandages, spirits, splints, stitches, herbs, rest),
    /// and they slow the whole party down. The numbers and the weather live in Rules.Health.cs; the list of wounds,
    /// treatments and weather in data.json.
    /// </summary>
    public partial class Engine
    {
        /// <summary>Whose body: "" is you, otherwise a party member's id.</summary>
        public Body BodyOf(string who) => who == "" ? S.body : Member(who)?.body;
        string WhoName(string who) => who == "" ? null : data.Follower(who)?.name ?? who;

        /// <summary>The party walks as fast as its slowest: a broken leg slows everyone.</summary>
        float TravelSlow()
        {
            float slow = Rules.Slow(S.body, data);
            foreach (var m in S.party) slow = Math.Max(slow, Rules.Slow(m.body, data));
            return slow;
        }

        /// <summary>One way of doing a treatment step: the items it would use (or why you can't).</summary>
        public (bool ok, List<string> uses, string why) TreatmentCost(string step)
        {
            var t = data.Treatment(step);
            if (t == null) return (false, null, "unknown");
            if (step == "rest") return (false, null, "a night's sleep");
            if (t.tool != "" && S.Count(t.tool) <= 0) return (false, null, "needs " + (data.Item(t.tool)?.name ?? t.tool).ToLowerInvariant());
            if (t.items.Count == 0) return (true, new List<string>(), "");
            foreach (var alt in t.items)
            {
                var parts = alt.Split('+').ToList();
                bool have = parts.GroupBy(p => p).All(g => g.Key == "water" ? S.water >= g.Count() : S.Count(g.Key) >= g.Count());
                if (have) return (true, parts, "");
            }
            var first = t.items[0].Split('+').Select(p => p == "water" ? "water" : (data.Item(p)?.name ?? p).ToLowerInvariant());
            return (false, null, "needs " + string.Join(" and ", first));
        }

        /// <summary>
        /// Someone in the world walking from one place to another (Bess going home): the object they were, then the
        /// object they become. The game has already moved them; the screen plays the walk and then clears this.
        /// </summary>
        public readonly List<(string from, string to)> Walks = new List<(string from, string to)>();

        /// <summary>The first step of a wound's treatment you can do right now, or null (all done, only rest left, or you lack what it needs).</summary>
        public string NextStep(string who, string afflictionId)
        {
            var a = BodyOf(who)?.afflictions.FirstOrDefault(x => x.id == afflictionId);
            var def = data.Affliction(afflictionId);
            if (a == null || def == null) return null;
            foreach (var step in def.treat)
                if (step != "rest" && !a.done.Contains(step) && TreatmentCost(step).ok) return step;
            return null;
        }

        /// <summary>Treats one wound with one step. who "" is you, or a party member's id.</summary>
        public bool Treat(string who, string afflictionId, string step)
        {
            if (!FreeToAct) return false;
            var b = BodyOf(who);
            var a = b?.afflictions.FirstOrDefault(x => x.id == afflictionId);
            var def = data.Affliction(afflictionId);
            var t = data.Treatment(step);
            if (a == null || def == null || t == null || !def.treat.Contains(step) || a.done.Contains(step)) return false;
            var (ok, uses, _) = TreatmentCost(step);
            if (!ok) return false;
            foreach (var u in uses)
            {
                if (u == "water") S.water--;
                else S.AddItem(u, -1);
            }
            Rules.Advance(S, data, Rules.HasPerk(S, data, "healer") ? t.minutes / 2 : t.minutes, false);
            a.done.Add(step);
            TrainSkill("first_aid", 3, "treating wounds");
            string whose = who == "" ? "your" : WhoName(who) + "'s";
            S.result = t.name + ": " + whose + " " + def.name.ToLowerInvariant() + "." + (Rules.Treated(a, def) ? " That's all it needs now: time." : "");
            if (who == "" && Rules.Treated(a, def) && S.Has("patch_lesson") && !S.Has("patch_done"))
            {
                S.flags.Add("patch_done");
                S.result += " Bess nods. \"That'll do. Keep it clean and it'll knit. You'll want your own linen and spirits on the road. Come up to the inn if you need a bed.\" She picks up her fish and heads back up through the village.";
                Walks.Add(("bess_gate", "bess"));
            }

            // A companion who was down gets back on their feet once the grave wound is dealt with
            var m = who == "" ? null : Member(who);
            if (m != null && afflictionId == "grave" && m.hp <= 0 && !Rules.Bleeding(a, def))
            {
                var f = data.Follower(who);
                m.hp = Math.Max(m.hp, Rules.MemberMaxHp(m, f) * 0.2f);
                S.result += " " + f.name + " is back on their feet, just.";
            }
            Rules.Clamp(S, data);
            GoTo(S.scene);
            return true;
        }

        /// <summary>What you're fighting (or just fought), as a wound's cause: "the yellow-eyed wolf". "" when nothing.</summary>
        string WoundedBy() { var e = string.IsNullOrEmpty(S.enemy) ? null : data.Enemy(S.enemy); return e == null ? "" : "the " + e.name.ToLowerInvariant(); }

        /// <summary>A hit might leave a wound: each of the move's wounds has its % chance, more for a crushing blow, less when braced.</summary>
        void MaybeWound(EnemyMove move, Body b, string who, int damage, bool braced)
        {
            if (damage < 2 || move.wounds == null) return;
            foreach (var w in move.wounds)
            {
                float chance = w.chance * (move.power >= 1.5f ? 1.5f : 1f) * (braced ? 0.5f : 1f);
                if (Roll(1, 100) <= chance) { Rules.Afflict(S, data, b, w.id, who, null, WoundedBy()); return; }
            }
        }

        /// <summary>After a fight, anyone who went down is badly hurt: a grave wound that kills them if it isn't bandaged and stitched in time.</summary>
        void DownedAfterFight()
        {
            foreach (var m in S.party.ToList())
            {
                var f = data.Follower(m.id);
                if (f == null || m.hp > 0) continue;
                m.hp = 0;
                m.morale -= 5;
                Rules.Afflict(S, data, m.body, "grave", f.name, null, WoundedBy());
            }
        }
    }
}
