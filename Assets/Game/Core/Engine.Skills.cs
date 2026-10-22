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
            PartyExp(n);
        }

        /// <summary>Your party learns from the same fights and jobs: everyone with you (and on their feet) gets the same experience.</summary>
        void PartyExp(int n)
        {
            foreach (var m in S.party)
            {
                var f = data.Follower(m.id);
                if (f == null || m.hp <= 0) continue;
                m.level = Rules.MemberLevel(m);
                m.exp += n;
                while (m.exp >= Rules.ExpToNext(m.level))
                {
                    m.exp -= Rules.ExpToNext(m.level);
                    m.level++;
                    m.hp = Rules.MemberMaxHp(m, f);
                    var opened = f.abilities.Where(a => a.level == m.level).Select(a => a.name).ToList();
                    S.Toasts.Add(f.name + " reached level " + m.level + "." + (opened.Count > 0 ? " New ability: " + string.Join(", ", opened) + "." : ""));
                }
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
            e.xp += amount * Rules.LearnRate(S);
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
            // Strength makes hard work lighter (not walking, not fighting)
            float str = c.train.Count > 0 && !S.inCombat ? Rules.WorkRate(S) : 1f;
            return Math.Max(1, (int)Math.Round(c.stamina * (1 - cut) * str));
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
            var joining = new PartyMember { id = f.id, level = Math.Max(1, f.level), hunger = f.hunger, thirst = f.thirst, energy = f.energy, morale = f.morale };
            joining.hp = Rules.MemberMaxHp(joining, f);
            S.party.Add(joining);
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

        /// <summary>
        /// Your party's turn in a fight: each one standing attacks, using their own abilities when the moment's right.
        /// Hunger, thirst and tiredness make them weaker. Returns true if one of them pinned the enemy before it could act.
        /// </summary>
        bool FollowerActs(EnemyDef e, EnemyMove move, List<string> lines, bool enemyStillToAct)
        {
            bool pinned = false;
            foreach (var m in S.party.ToList())
            {
                var f = data.Follower(m.id);
                if (f == null || m.hp <= 0 || S.enemyHp <= 0) continue;
                if (Rules.NoFight(m.body, data)) { lines.Add(f.name + " is in no state to fight."); continue; }
                // Pin down: once a fight, they stop a big attack before it lands
                if (enemyStillToAct && !S.pinUsed && move.KindOf() == "heavy" && Rules.HasAbility(m, f, "pin_down"))
                {
                    S.pinUsed = true;
                    S.enemyStun = 1;
                    pinned = true;
                    lines.Add(f.name + " pins " + FoeName(e) + " down before it can spring!");
                    Events.Add(new BattleEvent { target = "enemy", kind = "guard", text = "Pinned" });
                    continue;
                }
                float dmg = (Roll(f.atkMin, f.atkMax) + Rules.MemberLevel(m) - 1) * Rules.PartyPower(m, data);
                // Aimed shot: every third round they take their time over it
                bool aimed = S.round % 3 == 0 && Rules.HasAbility(m, f, "aimed_shot");
                if (aimed) dmg *= Rules.AimedShot;
                else if (move.KindOf() == "guard") dmg *= 0.5f;
                int dealt = Math.Max(1, (int)Math.Round(dmg));
                S.enemyHp -= dealt;
                lines.Add(aimed ? f.name + " takes careful aim. An aimed shot hits " + FoeName(e) + " for " + dealt + "!"
                                : f.name + " " + f.attack + " " + FoeName(e) + " for " + dealt + ".");
                Events.Add(new BattleEvent { from = "follower" + S.party.IndexOf(m), target = "enemy", kind = aimed ? "crit" : "hit", amount = dealt, text = dealt.ToString() });
            }
            return pinned;
        }

        /// <summary>Field dressing: after a fight, a party member who knows how binds the worst bleeding wound, without using your linen.</summary>
        void FieldDressing()
        {
            foreach (var m in PartyUp.ToList())
            {
                var f = data.Follower(m.id);
                if (!Rules.HasAbility(m, f, "field_dressing") || Rules.NoFight(m.body, data)) continue;
                var bodies = new List<(Body b, string whose)> { (S.body, "your") };
                foreach (var o in S.party) bodies.Add((o.body, o == m ? "their own" : (data.Follower(o.id)?.name ?? o.id) + "'s"));
                var worst = bodies.SelectMany(x => x.b.afflictions.Select(a => (a, x.whose)))
                                  .Select(x => (x.a, x.whose, def: data.Affliction(x.a.id)))
                                  .Where(x => x.def != null && x.def.treat.Contains("bandage") && !x.a.done.Contains("bandage") && Rules.Bleeding(x.a, x.def))
                                  .OrderByDescending(x => x.def.bleed).FirstOrDefault();
                if (worst.a == null) continue;
                worst.a.done.Add("bandage");
                S.Toasts.Add(f.name + " binds " + worst.whose + " " + worst.def.name.ToLowerInvariant() + " with a strip of cloth.");
                return;
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
                // Forager (Wren): she comes back from the edge of camp with something to eat, before anyone's fed
                if (f.trait == "forager" && m.hp > 0)
                {
                    string found = Roll(1, 3) == 1 ? "mushrooms" : "berries";
                    S.AddItem(found, 1);
                    said.Add("finds " + data.Item(found).name.ToLowerInvariant() + " round the camp");
                }
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