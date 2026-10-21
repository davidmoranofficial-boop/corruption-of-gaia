using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    /// <summary>Something that happened in a round of combat, for the screen to animate (damage numbers, flashes).</summary>
    public class BattleEvent
    {
        public string target;   // "enemy" or "player": where the number pops up
        public string kind;     // hit, crit, glance, parry, guard, heal, flee
        public int amount;
        public string text;     // what to show, e.g. "14" or "+30 hunger"
        public string from = ""; // who did it, for the screen to animate: "player", "enemy", "follower0" ... ("" = nobody moves)
    }

    /// <summary>
    /// Turn-based combat. Each round the enemy shows what it's about to do (its tell), you pick a command,
    /// and the faster side acts first. Attacks and defending take a timing: 0 = mistimed, 1 = good, 2 = perfect.
    /// </summary>
    public partial class Engine
    {
        public const int TimingMiss = 0, TimingGood = 1, TimingPerfect = 2;

        int pendingTiming = TimingGood;
        string fightRedirect;

        /// <summary>What happened in the last round, for the screen to animate. Not saved.</summary>
        public readonly List<BattleEvent> Events = new List<BattleEvent>();

        public EnemyDef Foe => S.inCombat ? data.Enemy(S.enemy) : null;

        /// <summary>The move the enemy will make this round. Shown to the player as its tell.</summary>
        public EnemyMove Intent
        {
            get
            {
                var e = data.Enemy(S.enemy);
                if (e == null) return null;
                return e.moves.FirstOrDefault(m => m.id == S.enemyMove) ?? e.moves[0];
            }
        }

        public bool PlayerActsFirst => Rules.Speed(S, data) >= (data.Enemy(S.enemy)?.speed ?? 0);

        /// <summary>Is someone in your party, on their feet, bringing this trait?</summary>
        public bool PartyTrait(string trait) => PartyUp.Any(m => data.Follower(m.id)?.trait == trait);

        /// <summary>Does this combat choice use the timing bar?</summary>
        public bool NeedsTiming(ChoiceDef c)
        {
            string e = c.effects.Count > 0 ? c.effects[0] : "";
            // Only defending and dodging use the bar (a perfect brace parries, a clean dodge leaves it open).
            // Attacks crit by chance, like most RPGs.
            return e == "fight:defend" || e == "fight:dodge";
        }

        /// <summary>The command a combat choice belongs to: attack, skill, item, defend or flee.</summary>
        public static string CommandOf(ChoiceDef c)
        {
            string e = c.effects.Count > 0 ? c.effects[0] : "";
            if (e == "fight:attack") return "attack";
            if (e == "fight:kindle" || e.StartsWith("fight:move:")) return "skill";
            if (e.StartsWith("fight:eat:") || e.StartsWith("fight:potion:") || e == "fight:drink") return "item";
            if (e == "fight:defend") return "defend";
            if (e == "fight:dodge") return "dodge";
            if (e == "fight:flee") return "flee";
            return "";
        }

        /// <summary>Takes a combat action with the timing the player managed on the bar.</summary>
        public void Act(ChoiceDef c, int timing)
        {
            pendingTiming = Math.Clamp(timing, TimingMiss, TimingPerfect);
            try { Choose(c); }
            finally { pendingTiming = TimingGood; }
        }

        void StartCombat(string enemyId, string win, string flee)
        {
            var e = data.Enemy(enemyId) ?? throw new Exception("Unknown enemy '" + enemyId + "'");
            S.inCombat = true;
            S.enemy = e.id;
            S.enemyHp = e.hp;
            S.combatWin = win;
            S.combatFlee = flee;
            S.defending = false;
            S.round = 1;
            S.enemyStun = 0;
            S.enemyOpen = 0;
            S.enemyRage = 0;
            S.enemyFury = false;
            S.pinUsed = false;
            S.battleLog.Clear();
            S.enemyMove = PickMove(e).id;
            Events.Clear();
        }

        EnemyMove PickMove(EnemyDef e)
        {
            int total = e.moves.Sum(m => Math.Max(1, m.weight));
            int r = Roll(1, total);
            foreach (var m in e.moves)
            {
                r -= Math.Max(1, m.weight);
                if (r <= 0) return m;
            }
            return e.moves[0];
        }

        SceneDef CombatScene()
        {
            var e = data.Enemy(S.enemy);
            var under = Scene(S.scene) ?? new SceneDef { place = Area?.name };
            var sc = new SceneDef
            {
                id = "combat",
                place = under.place,
                title = "Fight: " + e.name,
                text = new List<string> { e.desc, Intent?.tell ?? "" },
            };
            float light = Math.Clamp(Rules.TraitSum(S, data, t => t.light), 0f, 0.5f);
            sc.choices.Add(new ChoiceDef { label = "Attack", stamina = (int)Math.Round(Rules.AttackStamina * (1 - light)), energy = 1, time = 1, effects = { "fight:attack" } });
            if (S.spells.Contains("kindle"))
                sc.choices.Add(new ChoiceDef { label = "Cast Kindle", stamina = Rules.KindleStamina, mana = Rules.KindleMana, time = 1, effects = { "fight:kindle" } });
            foreach (var m in UsableMoves())
                sc.choices.Add(new ChoiceDef { label = m.name, stamina = m.stamina, energy = m.energy, mana = m.mana, time = 1, effects = { "fight:move:" + m.id } });
            foreach (var it in S.items)
            {
                var def = data.Item(it.id);
                if (def != null && def.food)
                    sc.choices.Add(new ChoiceDef { label = "Eat: " + def.name + (it.count > 1 ? " (" + it.count + ")" : ""), time = 1, effects = { "fight:eat:" + it.id } });
                if (def != null && def.heal > 0 && it.count > 0)
                    sc.choices.Add(new ChoiceDef { label = "Drink: " + def.name + (it.count > 1 ? " (" + it.count + ")" : ""), time = 1, effects = { "fight:potion:" + it.id } });
            }
            if (S.water > 0)
                sc.choices.Add(new ChoiceDef { label = "Drink from your waterskin (" + S.water + ")", time = 1, effects = { "fight:drink" } });
            sc.choices.Add(new ChoiceDef { label = "Defend", energy = 1, time = 1, effects = { "fight:defend" } });
            sc.choices.Add(new ChoiceDef { label = "Dodge", stamina = Rules.HasPerk(S, data, "nimble") ? Rules.NimbleDodge : Rules.DodgeStamina, energy = 1, time = 1, effects = { "fight:dodge" } });
            sc.choices.Add(new ChoiceDef { label = "Try to get away", stamina = Rules.FleeStamina, energy = 1, time = 1, effects = { "fight:flee" } });
            return sc;
        }

        void Fight(string kind, StringBuilder notes, ref string redirect)
        {
            Events.Clear();
            fightRedirect = null;
            var e = data.Enemy(S.enemy);
            var move = Intent;
            var lines = new List<string>();

            S.defending = kind == "defend";
            dodging = kind == "dodge";
            struckGuard = false;
            youHurtIt = false;
            if (dodging && Rules.Winded(S, data))
            {
                pendingTiming = TimingMiss;
                lines.Add("You're too winded to get out of the way in time.");
            }
            var mv = kind.StartsWith("move:") ? data.Move(kind.Substring(5)) : null;
            // Only an attack can beat you to it. Defending, dodging and quick moves always come first,
            // and an enemy that's off balance from your dodge is too slow to strike first.
            bool enemyFirst = move.Attacks() && !S.defending && !dodging && S.enemyOpen == 0 && !PlayerActsFirst && !(mv != null && mv.first);

            if (enemyFirst)
            {
                EnemyActs(e, move, lines);
                if (S.hp <= 0) { Finish(lines, notes); redirect = fightRedirect; return; }
                lines.Add("");   // marks the break between their turn and yours
            }

            if (!Fumbles(kind, lines)) PlayerActs(kind, e, move, lines);
            bool pinned = false;
            if (S.inCombat && S.enemyHp > 0 && FollowerUp) pinned = FollowerActs(e, move, lines, !enemyFirst);
            Fury(e, lines);

            if (S.inCombat && S.enemyHp <= 0)
            {
                lines.Add(Cap(FoeName(e)) + " goes down and stays down.");
                S.inCombat = false;
                fightRedirect = S.combatWin;
                GainExp(e.exp, "for beating " + FoeName(e));
            }
            else if (S.inCombat && !enemyFirst)
            {
                EnemyActs(e, move, lines);
            }
            if (!S.inCombat) { DownedAfterFight(); FollowerAfterFight(); FieldDressing(); }

            if (S.inCombat && S.hp > 0)
            {
                Fury(e, lines);
                if (S.enemyOpen > 0) S.enemyOpen--;
                S.enemyMove = PickMove(e).id;
                S.round++;
            }
            dodging = false;
            Finish(lines, notes);
            redirect = fightRedirect;
        }

        bool dodging, struckGuard, youHurtIt;

        /// <summary>A boss that's badly hurt flies into a rage, once.</summary>
        void Fury(EnemyDef e, List<string> lines)
        {
            if (e.enrageBelow <= 0 || S.enemyFury || S.enemyHp <= 0 || S.enemyHp >= e.hp * e.enrageBelow) return;
            S.enemyFury = true;
            S.enemyRage++;
            lines.Add(Cap(FoeName(e)) + " is bleeding badly, and it's furious now. Its blows will hit harder.");
            Events.Add(new BattleEvent { target = "enemy", kind = "rage", text = "Enraged" });
        }

        void Finish(List<string> lines, StringBuilder notes)
        {
            var real = lines.Where(l => l != "").ToList();
            S.battleLog.AddRange(real);
            while (S.battleLog.Count > 6) S.battleLog.RemoveAt(0);
            notes.Append(string.Join(" ", real));
        }

        void PlayerActs(string kind, EnemyDef e, EnemyMove move, List<string> lines)
        {
            string foe = FoeName(e);
            var mv = kind.StartsWith("move:") ? data.Move(kind.Substring(5)) ?? throw new Exception("Unknown move '" + kind + "'") : null;
            switch (kind)
            {
                case "attack":
                {
                    float dmg = WeaponHit();
                    int dealt = HitEnemy(dmg, move, out string how);
                    lines.Add(how == "crit" ? "A perfect strike! You hit " + foe + " for " + dealt + "."
                            : how == "glance" ? "You mistime it, and the blow only glances " + foe + ". " + dealt + " damage."
                            : "You hit " + foe + " for " + dealt + ".");
                    if (move.KindOf() == "guard" && how != "crit") lines.Add("It was ready for you, so the blow lands half as hard.");
                    Rules.Train(S, Stat.Str, 1f, "fighting");
                    Rules.Train(S, Stat.Agi, pendingTiming == TimingPerfect ? 1f : 0.5f, "fighting");
                    TrainSkill(WeaponSkill(), pendingTiming == TimingPerfect ? 3 : 2, "fighting");
                    break;
                }
                case "kindle":
                {
                    float dmg = SpellHit();
                    int dealt = HitEnemy(dmg, move, out string how);
                    lines.Add(how == "crit" ? "Fire leaps from your hand and engulfs " + foe + ". " + dealt + " damage!"
                            : how == "glance" ? "The spark sputters and only singes " + foe + ". " + dealt + " damage."
                            : "Fire snaps from your fingers and catches " + foe + " for " + dealt + ".");
                    Rules.Train(S, Stat.Int, 1f, "casting Kindle");
                    TrainSkill("magic", 3, "casting Kindle");
                    break;
                }
                case "defend":
                {
                    if (HasShield()) TrainSkill("shield", 1.5f, "blocking");
                    // it's holding back too: a real chance to get your breath
                    int back = Rules.DefendStaminaBack * (move.Attacks() ? 1 : 2);
                    S.stamina += back;
                    if (!move.Attacks()) lines.Add("It's holding back, so you hold back too and get your breath.");
                    else lines.Add(pendingTiming == TimingPerfect ? "You read it perfectly and set yourself to turn the blow."
                            : pendingTiming == TimingGood ? "You brace yourself and get your breath back."
                            : "You brace yourself, a moment too late.");
                    Events.Add(new BattleEvent { target = "player", kind = "heal", amount = back, text = "+" + back + " stamina" });
                    break;
                }
                case "dodge":
                    Rules.Train(S, Stat.Agi, 0.5f, "dodging");
                    break;
                case "drink":
                    if (S.water > 0)
                    {
                        S.water--;
                        S.thirst += 35;
                        S.energy += 4;
                        lines.Add("You snatch a mouthful from the waterskin.");
                        Events.Add(new BattleEvent { target = "player", kind = "heal", amount = 35, text = "+35 thirst" });
                    }
                    break;
                case "flee":
                {
                    int chance = Rules.NoRun(S.body, data) ? 5 : Math.Min(95, 35 + S.stats[Stat.Agi] * 8);
                    if (Roll(1, 100) <= chance)
                    {
                        S.inCombat = false;
                        Rules.Train(S, Stat.Agi, 1f, "escaping");
                        lines.Add("You break away and get clear.");
                        Events.Add(new BattleEvent { target = "player", kind = "flee", text = "Escaped" });
                        fightRedirect = S.combatFlee;
                        return;
                    }
                    lines.Add("You try to break away, but " + foe + " cuts you off.");
                    Events.Add(new BattleEvent { target = "player", kind = "flee", text = "Cut off" });
                    break;
                }
                default:
                    if (mv != null) { UseMove(mv, e, move, lines); break; }
                    if (kind.StartsWith("potion:"))
                    {
                        string id = kind.Substring(7);
                        var it = data.Item(id);
                        if (it != null && it.heal > 0 && S.Count(id) > 0)
                        {
                            S.AddItem(id, -1);
                            int back = (int)Math.Min(it.heal, Rules.MaxHp(S) - S.hp);
                            S.hp += it.heal;
                            lines.Add("You gulp down the " + it.name.ToLowerInvariant() + ".");
                            Events.Add(new BattleEvent { target = "player", kind = "heal", amount = back, text = "+" + back + " health" });
                        }
                        break;
                    }
                    if (kind.StartsWith("eat:"))
                    {
                        string id = kind.Substring(4);
                        var it = data.Item(id);
                        if (it != null && it.food && S.Count(id) > 0)
                        {
                            S.AddItem(id, -1);
                            S.hunger += it.hunger;
                            S.thirst += it.thirst;
                            S.energy += it.energy;
                            lines.Add("You wolf down the " + it.name.ToLowerInvariant() + " between blows.");
                            Events.Add(new BattleEvent { target = "player", kind = "heal", amount = (int)it.energy, text = "+" + it.energy.ToString("0") + " energy" });
                        }
                        break;
                    }
                    throw new Exception("Unknown fight action '" + kind + "'");
            }
            Rules.Clamp(S, data);
        }

        /// <summary>Your usual blow with what's in your hand, before luck: shown on the character sheet.</summary>
        public float AttackPower() => Rules.Attack(S, data, SkillLevel(WeaponSkill()));

        /// <summary>A hit with what's in your hand: Strength, the weapon, and how well you know it.</summary>
        float WeaponHit() => 4 + S.stats[Stat.Str] * 1.5f + Rules.GearDamage(S) + SkillLevel(WeaponSkill()) * 0.8f + Roll(-1, 2);

        float SpellHit() => 6 + S.stats[Stat.Int] * 2f + SkillLevel("magic") * 1.5f + Roll(0, 2);

        void UseMove(MoveDef m, EnemyDef e, EnemyMove move, List<string> lines)
        {
            string foe = FoeName(e);
            int total = 0;
            string how = "hit";
            for (int i = 0; i < Math.Max(1, m.hits) && S.enemyHp > 0; i++)
            {
                float dmg = (m.skill == "magic" ? SpellHit() : WeaponHit()) * m.power;
                total += HitEnemy(dmg, m.pierce ? null : move, out how);
            }
            lines.Add(m.name + (how == "crit" ? ", perfectly timed! " : how == "glance" ? ", but you mistime it. " : ". ") +
                      Cap(foe) + " takes " + total + ".");
            if (m.stagger && S.enemyHp > 0)
            {
                S.enemyStun = 1;
                lines.Add(Cap(foe) + " reels.");
            }
            TrainSkill(m.skill, how == "crit" ? 4 : 3, "fighting");
            if (m.skill == "magic") Rules.Train(S, Stat.Int, 1f, "casting");
            else Rules.Train(S, Stat.Str, 1f, "fighting");
        }

        /// <summary>Applies timing, fire and the enemy's guard to a hit, and records it. move null = ignores its guard.</summary>
        int HitEnemy(float dmg, EnemyMove move, out string how)
        {
            int roll = Roll(1, 100);
            bool open = S.enemyOpen > 0;
            how = open || (roll <= Rules.CritChance(S, data) && !Rules.Winded(S, data)) ? "crit" : roll > 100 - Rules.GlanceChance ? "glance" : "hit";
            if (open) { S.enemyOpen = 0; move = null; }   // off balance: a sure crit, and its guard is no help
            if (how == "crit") dmg *= Rules.CritMultiplier + Rules.TraitSum(S, data, t => t.crit) + (open ? Rules.OpenBonus : 0);
            else if (how == "glance") dmg *= Rules.GlanceMultiplier;
            dmg += Rules.TraitSum(S, data, t => t.fire);
            dmg *= Rules.FightPower(S.body, data);
            dmg *= Rules.QuirkMul(S, data, q => q.dealt);   // Hot-headed, Cautious
            if (Berserk) dmg *= Rules.BerserkDamage;
            if (move != null && move.KindOf() == "guard") { dmg *= 0.5f; struckGuard = true; }
            int dealt = Math.Max(1, (int)Math.Round(dmg));
            S.enemyHp -= dealt;
            youHurtIt = true;
            Events.Add(new BattleEvent { from = "player", target = "enemy", kind = how, amount = dealt, text = dealt.ToString() });
            return dealt;
        }

        void EnemyActs(EnemyDef e, EnemyMove move, List<string> lines)
        {
            string foe = Cap(FoeName(e));
            if (S.enemyStun > 0)
            {
                S.enemyStun--;
                lines.Add(foe + " is still reeling and can't act.");
                Events.Add(new BattleEvent { target = "enemy", kind = "guard", text = "Staggered" });
                return;
            }
            string kind = move.KindOf();
            if (kind == "guard")
            {
                if (struckGuard && S.hp > 0)
                {
                    // it was waiting for that
                    float back = Roll(e.atkMin, e.atkMax) * Rules.GuardCounter * Rage();
                    if (Rules.Winded(S, data)) back *= Rules.WindedTaken;
                    back *= (1f - Rules.GearGuard(S, data)) * Rules.QuirkMul(S, data, q => q.taken);
                    int t = Math.Max(1, (int)Math.Round(back));
                    S.hp -= t;
                    lines.Add(foe + " was waiting for that, and snaps back at you for " + t + ".");
                    Events.Add(new BattleEvent { from = "enemy", target = "player", kind = "hit", amount = t, text = t.ToString() });
                    if (S.hp <= 0) S.deathCause = "You were killed by " + FoeName(e) + ".";
                    return;
                }
                lines.Add(foe + " " + move.name + ".");
                Events.Add(new BattleEvent { target = "enemy", kind = "guard", text = "Wary" });
                return;
            }
            if (kind == "howl" || move.power <= 0)
            {
                if (youHurtIt)   // it takes a blow from you to break its focus; your companions' don't
                {
                    lines.Add("You cut " + FoeName(e) + "'s howl short.");
                    Events.Add(new BattleEvent { target = "enemy", kind = "guard", text = "Cut short" });
                    return;
                }
                S.enemyRage++;
                lines.Add(foe + " " + move.name + ", and works itself into a rage. Its blows will hit harder.");
                Events.Add(new BattleEvent { target = "enemy", kind = "rage", text = "Enraged" });
                return;
            }

            float hit = Roll(e.atkMin, e.atkMax) * move.power * Rage();
            // Sometimes it goes for someone in your party instead
            var up = PartyUp.ToList();
            if (up.Count > 0 && Roll(1, 100) <= 20 + 10 * up.Count)
            {
                var m = up[Roll(0, up.Count - 1)];
                var f = data.Follower(m.id);
                int ft = Math.Max(1, (int)Math.Round(hit));
                m.hp -= ft;
                lines.Add(foe + " " + move.name.Replace("your", f.name + "'s").Replace(" you", " " + f.name) + " for " + ft + ".");
                Events.Add(new BattleEvent { from = "enemy", target = "follower" + S.party.IndexOf(m), kind = "hit", amount = ft, text = ft.ToString() });
                if (m.hp <= -Rules.MemberMaxHp(m, f) * 0.5f)
                {
                    lines.Add(f.name + " is killed!");
                    Rules.Die(S, data, m, "a blow from " + FoeName(e));
                }
                else if (m.hp <= 0) lines.Add(f.name + " goes down!");
                else MaybeWound(move, m.body, f.name, ft, false);
                return;
            }

            bool covered = false;   // blocked, so wounds are less likely
            if (dodging)
            {
                if (kind == "feint")
                {
                    hit *= 1.4f;
                    lines.Add("You throw yourself aside, but it was a feint. It catches you as you land.");
                    Events.Add(new BattleEvent { from = "enemy", target = "player", kind = "glance", text = "Feint!" });
                }
                else if (pendingTiming >= TimingGood)
                {
                    bool opens = kind == "heavy" || pendingTiming == TimingPerfect;
                    lines.Add(foe + " " + move.name + ", but you slip aside" + (opens ? " and it overreaches. It's wide open!" : "."));
                    Events.Add(new BattleEvent { from = "enemy", target = "player", kind = "dodge", text = "Dodged" });
                    if (opens)
                    {
                        S.enemyOpen = 2;
                        S.stamina += Rules.DodgeStamina;   // its own lunge carries it past you: the dodge cost you nothing
                        Events.Add(new BattleEvent { target = "enemy", kind = "open", text = "Open!" });
                    }
                    Rules.Train(S, Stat.Agi, 1f, "dodging");
                    return;
                }
                else lines.Add("You move a moment too late.");
            }
            else if (S.defending)
            {
                covered = true;
                if (pendingTiming == TimingPerfect && (kind != "heavy" || HasShield()))
                {
                    Parry(e, move, lines);
                    return;
                }
                if (kind == "heavy")
                {
                    hit *= pendingTiming == TimingPerfect ? 0.45f : pendingTiming == TimingGood ? 0.65f : 0.9f;
                    S.stamina -= 10;
                    lines.Add("The blow smashes through your guard and knocks the wind out of you.");
                    Events.Add(new BattleEvent { from = "enemy", target = "player", kind = "glance", text = "-10 stamina" });
                }
                else hit *= pendingTiming == TimingGood ? 0.4f : 0.7f;
            }
            if (Rules.Winded(S, data))
            {
                hit *= Rules.WindedTaken;
                lines.Add("You're out of breath and slow to cover.");
            }
            hit *= 1f - Rules.GearGuard(S, data);
            hit *= Rules.QuirkMul(S, data, q => q.taken);
            int taken = Math.Max(0, (int)Math.Round(hit));
            S.hp -= taken;
            lines.Add(foe + " " + move.name + " for " + taken + ".");
            Events.Add(new BattleEvent { from = "enemy", target = "player", kind = kind == "heavy" && !covered ? "crit" : "hit", amount = taken, text = taken.ToString() });
            if (S.defending) Rules.Train(S, Stat.End, 0.5f, "taking hits");
            if (S.hp > 0) MaybeWound(move, S.body, null, taken, covered);
            if (S.hp <= 0) S.deathCause = "You were killed by " + FoeName(e) + ".";
        }

        /// <summary>Berserk (the soldier's perk): badly hurt, your blows land harder.</summary>
        public bool Berserk => Rules.HasPerk(S, data, "berserk") && S.hp > 0 && S.hp < Rules.MaxHp(S) * Rules.BerserkBelow;

        /// <summary>How much harder its blows land for being worked up.</summary>
        float Rage() => 1f + S.enemyRage * Rules.RagePerPoint;

        /// <summary>A perfect block: turn the blow aside and strike back.</summary>
        void Parry(EnemyDef e, EnemyMove move, List<string> lines)
        {
            string foe = Cap(FoeName(e));
            int counter = Math.Max(1, (int)Math.Round(WeaponHit() * Rules.ParryCounter));
            S.enemyHp -= counter;
            lines.Add(foe + " " + move.name + ", but you turn it aside and strike back for " + counter + "!");
            Events.Add(new BattleEvent { from = "enemy", target = "player", kind = "parry", text = "Parry!" });
            Events.Add(new BattleEvent { from = "player", target = "enemy", kind = "hit", amount = counter, text = counter.ToString() });
            Rules.Train(S, Stat.Agi, 1f, "parrying");
            if (HasShield()) TrainSkill("shield", 2, "parrying");
            if (S.enemyHp <= 0)
            {
                lines.Add(foe + " goes down and stays down.");
                S.inCombat = false;
                fightRedirect = S.combatWin;
                GainExp(e.exp, "for beating " + FoeName(e));
            }
        }

        static string FoeName(EnemyDef e) => "the " + e.name.ToLowerInvariant();
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
