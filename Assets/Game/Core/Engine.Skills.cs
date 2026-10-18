using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    /// <summary>
    /// Levels, skills and followers. There are no classes: you get better at whatever you actually do.
    /// Experience from fights and finished jobs raises your level (more health and stamina). Skills grow with use:
    /// fight with a dagger and Daggers rises and unlocks dagger moves; block with a shield and Shields rises; dig, smith,
    /// split wood or check snares and those life skills rise and the work gets easier.
    /// </summary>
    public partial class Engine
    {
        // ------------------------------------------------------------------ level and experience

        public void GainExp(int n, string why)
        {
            if (n <= 0) return;
            S.exp += n;
            S.Toasts.Add("+" + n + " EXP" + (string.IsNullOrEmpty(why) ? "" : " " + why));
            while (S.exp >= Rules.ExpToNext(S.level))
            {
                S.exp -= Rules.ExpToNext(S.level);
                S.level++;
                S.hp = Rules.MaxHp(S);
                S.stamina = Rules.MaxStamina(S, data);
                S.Toasts.Add("Level up! You're level " + S.level + ". More health and stamina, and you're patched up.");
            }
        }

        // ------------------------------------------------------------------ skills

        SkillEntry Entry(string id, bool make)
        {
            var e = S.skills.FirstOrDefault(x => x.id == id);
            if (e == null && make) { e = new SkillEntry { id = id }; S.skills.Add(e); }
            return e;
        }

        public int SkillLevel(string id) => Entry(id, false)?.level ?? 0;
        public float SkillProgress(string id)
        {
            var e = Entry(id, false);
            return e == null ? 0 : e.level >= Rules.MaxSkill ? 1 : e.xp / Rules.SkillToNext(e.level);
        }

        /// <summary>Practice at a skill. Each level unlocks the moves set at that level.</summary>
        public void TrainSkill(string id, float amount, string why)
        {
            var def = data.Skill(id) ?? throw new Exception("Unknown skill '" + id + "'");
            var e = Entry(id, true);
            if (e.level >= Rules.MaxSkill) return;
            e.xp += amount;
            while (e.level < Rules.MaxSkill && e.xp >= Rules.SkillToNext(e.level))
            {
                e.xp -= Rules.SkillToNext(e.level);
                e.level++;
                S.Toasts.Add(def.name + " rose to " + e.level + (string.IsNullOrEmpty(why) ? "" : " from " + why) + ".");
                foreach (var m in data.file.moves.Where(m => m.skill == id && m.level == e.level))
                    S.Toasts.Add("New move: " + m.name + ". " + m.desc);
            }
        }

        /// <summary>The combat skill your weapon trains: dagger, axe, bow... or fists with nothing in your hand.</summary>
        public string WeaponSkill()
        {
            var w = S.InSlot("weapon");
            var def = w != null ? data.Gear(w.id) : null;
            return def != null && !string.IsNullOrEmpty(def.skill) ? def.skill : "unarmed";
        }

        public bool HasShield()
        {
            var o = S.InSlot("offhand");
            return o != null && data.Gear(o.id)?.skill == "shield";
        }

        /// <summary>Moves you've unlocked and can use right now, with what's in your hands.</summary>
        public IEnumerable<MoveDef> UsableMoves()
        {
            string weapon = WeaponSkill();
            foreach (var m in data.file.moves)
            {
                if (SkillLevel(m.skill) < m.level) continue;
                bool fits = m.skill == weapon || (m.skill == "shield" && HasShield()) || m.skill == "magic";
                if (fits && (string.IsNullOrEmpty(m.need) || Check(m.need))) yield return m;
            }
        }

        /// <summary>Life skills make their work lighter: 6% less stamina per level, up to 40%.</summary>
        public int StaminaCost(ChoiceDef c)
        {
            if (c.stamina <= 0) return c.stamina;
            int best = 0;
            foreach (var t in c.train)
            {
                int plus = t.IndexOf('+');
                if (plus <= 0) continue;
                var sk = data.Skill(t.Substring(0, plus));
                if (sk != null && sk.kind == "life") best = Math.Max(best, SkillLevel(sk.id));
            }
            float cut = Math.Min(0.4f, best * 0.06f);
            return (int)Math.Round(c.stamina * (1 - cut));
        }

        // ------------------------------------------------------------------ followers

        public FollowerDef Follower => data.Follower(S.follower);
        public bool FollowerUp => Follower != null && S.followerHp > 0;

        /// <summary>Puts your follower a step behind you (or on your tile if there's no room).</summary>
        void PlaceFollower()
        {
            if (S.follower == "") return;
            var (dx, dy) = Dir(S.facing);
            int bx = S.ax - dx, by = S.ay - dy;
            bool ok = Area != null && !Blocked(bx, by);
            S.fx = ok ? bx : S.ax;
            S.fy = ok ? by : S.ay;
            S.followerFacing = S.facing;
        }

        /// <summary>Your follower's turn in a fight: one attack on the enemy.</summary>
        void FollowerActs(EnemyDef e, EnemyMove move, List<string> lines)
        {
            var f = Follower;
            float dmg = Roll(f.atkMin, f.atkMax);
            if (move.guard) dmg *= 0.5f;
            int dealt = Math.Max(1, (int)Math.Round(dmg));
            S.enemyHp -= dealt;
            lines.Add(f.name + " " + f.attack + " " + FoeName(e) + " for " + dealt + ".");
            Events.Add(new BattleEvent { target = "enemy", kind = "hit", amount = dealt, text = dealt.ToString() });
        }

        /// <summary>After a fight, a follower who went down gets back up, hurt.</summary>
        void FollowerAfterFight()
        {
            var f = Follower;
            if (f != null && S.followerHp <= 0) S.followerHp = Math.Max(1, f.hp * 0.25f);
        }
    }
}
