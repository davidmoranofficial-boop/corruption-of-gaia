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

        /// <summary>Does this combat choice use the timing bar?</summary>
        public static bool NeedsTiming(ChoiceDef c)
        {
            string e = c.effects.Count > 0 ? c.effects[0] : "";
            return e == "fight:attack" || e == "fight:kindle" || e == "fight:defend";
        }

        /// <summary>The command a combat choice belongs to: attack, skill, item, defend or flee.</summary>
        public static string CommandOf(ChoiceDef c)
        {
            string e = c.effects.Count > 0 ? c.effects[0] : "";
            if (e == "fight:attack") return "attack";
            if (e == "fight:kindle") return "skill";
            if (e.StartsWith("fight:eat:") || e == "fight:drink") return "item";
            if (e == "fight:defend") return "defend";
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
            var under = Scene(S.scene) ?? new SceneDef { place = Region?.name };
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
                sc.choices.Add(new ChoiceDef { label = "Cast Kindle", stamina = Rules.KindleStamina, energy = Rules.KindleEnergy, time = 1, effects = { "fight:kindle" } });
            foreach (var it in S.items)
            {
                var def = data.Item(it.id);
                if (def != null && def.food)
                    sc.choices.Add(new ChoiceDef { label = "Eat: " + def.name + (it.count > 1 ? " (" + it.count + ")" : ""), time = 1, effects = { "fight:eat:" + it.id } });
            }
            if (S.water > 0)
                sc.choices.Add(new ChoiceDef { label = "Drink from your waterskin (" + S.water + ")", time = 1, effects = { "fight:drink" } });
            sc.choices.Add(new ChoiceDef { label = "Defend", energy = 1, time = 1, effects = { "fight:defend" } });
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
            // Defending and the enemy holding back always happen at the start of the round.
            bool enemyFirst = !S.defending && !move.guard && !PlayerActsFirst;

            if (enemyFirst)
            {
                EnemyActs(e, move, lines);
                if (S.hp <= 0) { Finish(lines, notes); redirect = fightRedirect; return; }
                lines.Add("");   // marks the break between their turn and yours
            }

            PlayerActs(kind, e, move, lines);

            if (S.inCombat && S.enemyHp <= 0)
            {
                lines.Add(Cap(FoeName(e)) + " goes down and stays down.");
                S.inCombat = false;
                fightRedirect = S.combatWin;
            }
            else if (S.inCombat && !enemyFirst)
            {
                EnemyActs(e, move, lines);
            }

            if (S.inCombat && S.hp > 0)
            {
                S.enemyMove = PickMove(e).id;
                S.round++;
            }
            Finish(lines, notes);
            redirect = fightRedirect;
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
            switch (kind)
            {
                case "attack":
                {
                    float dmg = 4 + S.stats[Stat.Str] * 1.5f + Rules.GearDamage(S) + Roll(-1, 2);
                    int dealt = HitEnemy(dmg, move, out string how);
                    lines.Add(how == "crit" ? "A perfect strike! You hit " + foe + " for " + dealt + "."
                            : how == "glance" ? "You mistime it, and the blow only glances " + foe + ". " + dealt + " damage."
                            : "You hit " + foe + " for " + dealt + ".");
                    if (move.guard) lines.Add("It was ready for you, so the blow lands half as hard.");
                    Rules.Train(S, Stat.Str, 1f, "fighting");
                    Rules.Train(S, Stat.Agi, pendingTiming == TimingPerfect ? 1f : 0.5f, "fighting");
                    break;
                }
                case "kindle":
                {
                    float dmg = 6 + S.stats[Stat.Int] * 2f + Roll(0, 2);
                    int dealt = HitEnemy(dmg, move, out string how);
                    lines.Add(how == "crit" ? "Fire leaps from your hand and engulfs " + foe + ". " + dealt + " damage!"
                            : how == "glance" ? "The spark sputters and only singes " + foe + ". " + dealt + " damage."
                            : "Fire snaps from your fingers and catches " + foe + " for " + dealt + ".");
                    Rules.Train(S, Stat.Int, 1f, "casting Kindle");
                    break;
                }
                case "defend":
                    S.stamina += Rules.DefendStaminaBack;
                    lines.Add(pendingTiming == TimingPerfect ? "You read it perfectly and set yourself to turn the blow."
                            : pendingTiming == TimingGood ? "You brace yourself and get your breath back."
                            : "You brace yourself, a moment too late.");
                    Events.Add(new BattleEvent { target = "player", kind = "heal", amount = Rules.DefendStaminaBack, text = "+" + Rules.DefendStaminaBack + " stamina" });
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
                    int chance = Math.Min(95, 35 + S.stats[Stat.Agi] * 8);
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

        /// <summary>Applies timing, fire and the enemy's guard to a hit, and records it.</summary>
        int HitEnemy(float dmg, EnemyMove move, out string how)
        {
            how = pendingTiming == TimingPerfect ? "crit" : pendingTiming == TimingMiss ? "glance" : "hit";
            if (how == "crit") dmg *= Rules.CritMultiplier + Rules.TraitSum(S, data, t => t.crit);
            else if (how == "glance") dmg *= Rules.GlanceMultiplier;
            dmg += Rules.TraitSum(S, data, t => t.fire);
            if (move.guard) dmg *= 0.5f;
            int dealt = Math.Max(1, (int)Math.Round(dmg));
            S.enemyHp -= dealt;
            Events.Add(new BattleEvent { target = "enemy", kind = how, amount = dealt, text = dealt.ToString() });
            return dealt;
        }

        void EnemyActs(EnemyDef e, EnemyMove move, List<string> lines)
        {
            string foe = Cap(FoeName(e));
            if (move.guard || move.power <= 0)
            {
                lines.Add(foe + " " + move.name + ".");
                Events.Add(new BattleEvent { target = "enemy", kind = "guard", text = "Wary" });
                return;
            }

            float hit = Roll(e.atkMin, e.atkMax) * move.power;
            if (S.defending)
            {
                if (pendingTiming == TimingPerfect)
                {
                    int counter = Math.Max(1, (int)Math.Round((4 + S.stats[Stat.Str] * 1.5f + Rules.GearDamage(S)) * 0.5f));
                    S.enemyHp -= counter;
                    lines.Add(foe + " " + move.name + ", but you turn it aside and strike back for " + counter + "!");
                    Events.Add(new BattleEvent { target = "player", kind = "parry", text = "Parry!" });
                    Events.Add(new BattleEvent { target = "enemy", kind = "hit", amount = counter, text = counter.ToString() });
                    Rules.Train(S, Stat.Agi, 1f, "parrying");
                    if (S.enemyHp <= 0)
                    {
                        lines.Add(foe + " goes down and stays down.");
                        S.inCombat = false;
                        fightRedirect = S.combatWin;
                    }
                    return;
                }
                hit *= pendingTiming == TimingGood ? 0.4f : 0.7f;
            }
            hit *= 1f - Rules.GearGuard(S, data);
            int taken = Math.Max(0, (int)Math.Round(hit));
            S.hp -= taken;
            lines.Add(foe + " " + move.name + " for " + taken + ".");
            Events.Add(new BattleEvent { target = "player", kind = move.power > 1.2f ? "crit" : "hit", amount = taken, text = taken.ToString() });
            if (S.defending) Rules.Train(S, Stat.End, 0.5f, "taking hits");
            if (S.hp <= 0) S.deathCause = "You were killed by " + FoeName(e) + ".";
        }

        static string FoeName(EnemyDef e) => "the " + e.name.ToLowerInvariant();
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
