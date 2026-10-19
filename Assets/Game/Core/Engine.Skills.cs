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
                S.skillPoints++;
                S.Toasts.Add("Level up! You're level " + S.level + ". More health and stamina, you're patched up, and you have a skill point to spend.");
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
                    S.Toasts.Add("New move to learn: " + m.name + ". Spend a skill point on it in Skills.");
            }
        }

        // ------------------------------------------------------------------ moves and skill points

        public const int MoveCost = 1;

        /// <summary>Can you learn this move now? Your skill must be high enough and you need a skill point.</summary>
        public bool CanLearn(MoveDef m, out string why)
        {
            why = null;
            if (S.moves.Contains(m.id)) { why = "Learned"; return false; }
            if (SkillLevel(m.skill) < m.level) { why = "Needs " + (data.Skill(m.skill)?.name ?? m.skill) + " " + m.level; return false; }
            if (!string.IsNullOrEmpty(m.need) && !Check(m.need)) { why = "Needs " + m.need.Replace("spell:", "the spell ").Replace('_', ' '); return false; }
            if (S.skillPoints < MoveCost) { why = "No skill points"; return false; }
            return true;
        }

        public bool LearnMove(string id)
        {
            var m = data.Move(id);
            if (m == null || !CanLearn(m, out _)) return false;
            S.skillPoints -= MoveCost;
            S.moves.Add(id);
            S.Toasts.Add("Learned " + m.name + ".");
            return true;
        }

        /// <summary>Saves from before skill points: you keep every move your skills had already unlocked.</summary>
        void MigrateMoves()
        {
            if (!S.coinsInCopper)
            {
                // Saves from before silver and gold: prices went up about twentyfold, so your purse does too
                S.coinsInCopper = true;
                S.coins *= 20;
            }
            if (S.movesLearned) return;
            S.movesLearned = true;
            foreach (var m in data.file.moves)
                if (SkillLevel(m.skill) >= m.level && !S.moves.Contains(m.id)) S.moves.Add(m.id);
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
                if (!S.moves.Contains(m.id)) continue;
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

        // ------------------------------------------------------------------ party

        /// <summary>The first person in your party (the one right behind you), or null.</summary>
        public FollowerDef Follower => S.party.Count > 0 ? data.Follower(S.party[0].id) : null;
        public PartyMember Member(string id) => S.party.FirstOrDefault(m => m.id == id);
        public IEnumerable<PartyMember> PartyUp => S.party.Where(m => m.hp > 0 && data.Follower(m.id) != null);
        public bool FollowerUp => PartyUp.Any();

        /// <summary>Someone joins your party, if there's room.</summary>
        public bool Join(string id)
        {
            var f = data.Follower(id) ?? throw new Exception("Unknown follower '" + id + "'");
            if (Member(id) != null) return true;
            if (S.party.Count >= Rules.PartyMax) { S.Toasts.Add("Your party is full. " + f.name + " can't come."); return false; }
            S.party.Add(new PartyMember { id = f.id, hp = f.hp, hunger = f.hunger, thirst = f.thirst, energy = f.energy, morale = f.morale });
            PlaceFollower();
            var m = Member(id);
            m.conditions = Rules.Conditions(m, data);
            S.Toasts.Add(f.name + " joins your party.");
            if (!S.Has("party_hint"))
            {
                S.flags.Add("party_hint");
                S.Toasts.Add("Your party eats your food and drinks your water. Press P to see how they're doing.");
            }
            return true;
        }

        public void Leave(string id)
        {
            var m = Member(id);
            if (m == null) return;
            S.party.Remove(m);
            S.Toasts.Add((data.Follower(id)?.name ?? id) + " goes their own way.");
        }

        /// <summary>Lines your party up behind you, one step apart (or on your tile if there's no room).</summary>
        void PlaceFollower()
        {
            var (dx, dy) = Dir(S.facing);
            int px = S.ax, py = S.ay;
            foreach (var m in S.party)
            {
                int bx = px - dx, by = py - dy;
                bool ok = Area != null && InBounds(Area, bx, by) && !Blocked(bx, by);
                if (ok) { px = bx; py = by; }
                m.x = px; m.y = py;
                m.facing = S.facing;
            }
        }

        /// <summary>You step forward: each member steps into the tile the one ahead of them just left.</summary>
        void PartyFollows(int oldX, int oldY)
        {
            int px = oldX, py = oldY;
            foreach (var m in S.party)
            {
                int ox = m.x, oy = m.y;
                if (ox == px && oy == py) continue;   // already there (bunched up)
                m.facing = ox < px ? "right" : ox > px ? "left" : oy < py ? "down" : oy > py ? "up" : m.facing;
                m.x = px; m.y = py;
                px = ox; py = oy;
            }
        }

        /// <summary>Your party's turn in a fight: each one standing attacks. Hunger, thirst and tiredness make them weaker.</summary>
        void FollowerActs(EnemyDef e, EnemyMove move, List<string> lines)
        {
            int i = 0;
            foreach (var m in S.party.ToList())
            {
                int index = i++;
                var f = data.Follower(m.id);
                if (f == null || m.hp <= 0 || S.enemyHp <= 0) continue;
                if (Rules.NoFight(m.body, data)) { lines.Add(f.name + " is in no state to fight."); continue; }
                float dmg = Roll(f.atkMin, f.atkMax) * Rules.PartyPower(m, data);
                if (move.guard) dmg *= 0.5f;
                int dealt = Math.Max(1, (int)Math.Round(dmg));
                S.enemyHp -= dealt;
                lines.Add(f.name + " " + f.attack + " " + FoeName(e) + " for " + dealt + ".");
                Events.Add(new BattleEvent { target = "enemy", kind = "hit", amount = dealt, text = dealt.ToString() });
            }
        }

        /// <summary>After a fight, fighting has tired everyone.</summary>
        void FollowerAfterFight()
        {
            foreach (var m in S.party)
            {
                var f = data.Follower(m.id);
                if (f == null) continue;
                m.energy -= 3;
                Rules.ClampMember(m, f);
            }
        }

        /// <summary>Gives a party member something to eat from your pack.</summary>
        public bool Feed(string memberId, string itemId)
        {
            var m = Member(memberId);
            var it = data.Item(itemId);
            var f = data.Follower(memberId);
            if (!FreeToAct || m == null || f == null || it == null || !it.food || S.Count(itemId) <= 0) return false;
            S.AddItem(itemId, -1);
            Rules.Advance(S, data, 5, false);
            m.hunger += it.hunger;
            m.thirst += it.thirst;
            m.energy += it.energy;
            m.morale += m.hunger < 60 ? 6 : 2;
            Rules.ClampMember(m, f);
            m.conditions = Rules.Conditions(m, data);
            S.result = "You give " + f.name + " the " + it.name.ToLowerInvariant() + ".";
            GoTo(S.scene);
            return true;
        }

        /// <summary>Passes the waterskin to a party member.</summary>
        public bool GiveWater(string memberId)
        {
            var m = Member(memberId);
            var f = data.Follower(memberId);
            if (!FreeToAct || m == null || f == null || S.water <= 0) return false;
            S.water--;
            Rules.Advance(S, data, 2, false);
            m.thirst += 35;
            m.energy += 4;
            m.morale += 2;
            Rules.ClampMember(m, f);
            m.conditions = Rules.Conditions(m, data);
            S.result = "You pass " + f.name + " the waterskin.";
            GoTo(S.scene);
            return true;
        }

        /// <summary>
        /// At night your party shares your supplies: whoever's hungry eats from your pack, whoever's thirsty
        /// drinks from your waterskin. If there isn't enough, they go without, and it shows in their spirits.
        /// A bed lifts everyone; a cold camp doesn't.
        /// </summary>
        void PartyAtNight(string kind, bool fire, StringBuilder notes)
        {
            foreach (var m in S.party)
            {
                var f = data.Follower(m.id);
                if (f == null) continue;
                var said = new List<string>();
                if (m.hunger < 70)
                {
                    var food = S.items.Where(x => data.Item(x.id)?.food == true && x.count > 0)
                                      .OrderBy(x => data.Item(x.id).hunger).Select(x => x.id).FirstOrDefault();
                    if (food != null) { var it = data.Item(food); S.AddItem(food, -1); m.hunger += it.hunger; m.thirst += it.thirst; said.Add("eats " + Article(it.name) + " " + it.name.ToLowerInvariant()); }
                    else { m.morale -= 10; said.Add("goes to sleep hungry"); }
                }
                if (m.thirst < 70)
                {
                    if (S.water > 0) { S.water--; m.thirst += 35; said.Add("drinks from your waterskin"); }
                    else { m.morale -= 8; said.Add("has nothing to drink"); }
                }
                m.morale += kind == "safe" ? 10 : kind == "camp" && !fire ? -6 : 2;
                Rules.ClampMember(m, f);
                if (said.Count > 0) notes.Append(" " + f.name + " " + string.Join(" and ", said) + ".");
            }
        }
    }
}