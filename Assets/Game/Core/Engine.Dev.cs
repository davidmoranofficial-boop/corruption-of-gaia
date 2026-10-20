using System;
using System.Collections.Generic;
using System.Linq;

namespace Loran.Core
{
    /// <summary>
    /// Dev mode: jump straight to any part of the game with a ready-made test character, and cheat.
    /// A dev game carries the flag "dev" and is never autosaved over your real save.
    /// </summary>
    public partial class Engine
    {
        public const string DevFlag = "dev";

        public bool IsDev => S.Has(DevFlag);

        /// <summary>The quick starts the dev panel offers: id and button text.</summary>
        public static readonly (string id, string name)[] DevStarts =
        {
            ("village", "Skelmed"),
            ("road", "Walk the coast road"),
            ("hut", "Wake in Yenson's hut"),
            ("hills", "The hills above Skelmed"),
            ("fire", "The net shed fire"),
            ("boss", "Boss: the pack leader, with Rook"),
            ("forge_first", "Forge: your first gear"),
            ("forge_iron", "Forge: buy iron"),
            ("dog", "Fight the wolf"),
            ("crab", "Fight the rock crab"),
        };

        /// <summary>A fresh test character, rested and supplied, standing in Skelmed on the morning of day 2.</summary>
        void DevCharacter()
        {
            NewGame("basic");
            CreateCharacter("Tester", "other", "Average", "Black", "farmer", new[] { 1, 1, 1, 1, 1 });
            S.flags.Add(DevFlag);
            S.hud = true;
            S.minutes = 1440 + 6 * 60;
            S.corruption = Rules.StartCorruption;
            S.coins = 300;
            S.water = Rules.WaterskinSize;
            S.AddItem("bread", 2);
            S.AddItem("flint", 1);
            foreach (var f in new[] { "y_met", "y_what", "y_where", "y_stew", "y_thanks", "y_supplies", "yenson_road", "dog_met",
                                     "quest_done:water", "quest_done:firewood", "quest_done:boar", "boar_seen", "boar_killed" })
                if (!S.Has(f)) S.flags.Add(f);
            if (data.Gear("gutting_knife") != null) MakeGear("gutting_knife", null, null);
            DevRefill();
        }

        public void DevStart(string id)
        {
            DevCharacter();
            S.Toasts.Clear();
            S.result = "Dev mode: " + (DevStarts.FirstOrDefault(d => d.id == id).name ?? id) + ". This game won't overwrite your real save.";
            switch (id)
            {
                case "village":
                    MoveTo("village_gate");
                    Jump(TravelId);
                    break;
                case "hut":
                    S.flags.RemoveAll(f => f.StartsWith("y_") || f.StartsWith("quest") || f.StartsWith("boar_") || f == "yenson_road" || f == "dog_met");
                    S.gearItems.RemoveAll(g => g.id == "gutting_knife");
                    Jump("wake");
                    break;
                case "road":
                    S.flags.Remove("dog_met");
                    MoveTo("shack");
                    Jump(TravelId);
                    break;
                case "hills":
                    MoveTo("hills_gate");
                    Jump(TravelId);
                    break;
                case "fire":
                    foreach (var f in new[] { "quest:orlen", "orlen_met", "brazier_lit", "fire_started" }) Flag(f);
                    S.spells.Add("draw_water");
                    S.spells.Add("kindle");
                    MoveTo("village_gate");
                    S.ax = 12; S.ay = 13; S.facing = "left";
                    Jump(TravelId);
                    break;
                case "boss":
                    foreach (var f in new[] { "quest_done:lodge", "quest:pack" }) Flag(f);
                    MoveTo("hills_gate");
                    S.ax = 28; S.ay = 8; S.facing = "up";
                    Apply("follow:rook", new System.Text.StringBuilder(), ref devRedirect);
                    MakeGear("hunting_bow", null, null);
                    Jump(TravelId);
                    break;
                case "forge_first":
                    MoveTo("village_gate");
                    foreach (var f in new[] { "quest:quarry", "g_met", "ore_in" }) Flag(f);
                    Jump("smith");
                    break;
                case "forge_iron":
                    MoveTo("village_gate");
                    Flag("smith_done");
                    Flag("quest_done:quarry");
                    S.coins = 600;
                    Jump("smith");
                    break;
                case "dog":
                case "crab":
                {
                    MoveTo("headland");
                    S.scene = TravelId;
                    StartCombat(id, id == "dog" ? "dog_won" : "ev_crab_won", TravelId);
                    break;
                }
                default: throw new Exception("No dev start called '" + id + "'");
            }
            // dev starts know every move their skills have unlocked
            S.movesLearned = false;
            MigrateMoves();
            Snapshot();
        }

        void Flag(string f) { if (!S.Has(f)) S.flags.Add(f); }
        string devRedirect;

        /// <summary>Adds experience, enough for the next level.</summary>
        /// <summary>Wounds you (or, for a companion-only one, the first in your party).</summary>
        public void DevAfflict(string id)
        {
            var target = id == "grave" && S.party.Count > 0 ? S.party[0] : null;
            if (target != null) { target.hp = 0; Rules.Afflict(S, data, target.body, id, data.Follower(target.id)?.name ?? target.id); }
            else Rules.Afflict(S, data, S.body, id, null);
        }

        public void DevHeal()
        {
            S.body = new Body();
            foreach (var m in S.party) { m.body = new Body(); m.hp = data.Follower(m.id)?.hp ?? m.hp; }
        }

        public void DevMedicine()
        {
            foreach (var (id, n) in new[] { ("bandage", 4), ("spirits", 2), ("splint", 2), ("yarrow", 2), ("willow_bark", 2), ("sewing_kit", 1) })
                if (data.Item(id) != null) S.AddItem(id, n);
        }

        /// <summary>Forces the weather ("" lets it run naturally again).</summary>
        public void DevWeather(string id) => S.devWeather = id;

        public void DevSkillPoints() => S.skillPoints += 3;

        public void DevLevel() => GainExp(Rules.ExpToNext(S.level) - S.exp, "(dev)");

        /// <summary>Raises every skill by one level.</summary>
        public void DevSkills()
        {
            foreach (var sk in data.file.skills)
            {
                int lv = SkillLevel(sk.id);
                float have = S.skills.FirstOrDefault(x => x.id == sk.id)?.xp ?? 0;
                TrainSkill(sk.id, Rules.SkillToNext(lv) - have, "");
            }
        }

        /// <summary>Goes straight to a scene, running its arrival effects. Leaves any fight, walk or forge.</summary>
        public void Jump(string id)
        {
            S.inCombat = false;
            if (id != ForgeId) S.forging = "";
            S.scene = "";   // so arrival effects run even when jumping to the scene you're in
            freshView = true;
            GoTo(id);
        }

        public void DevRefill()
        {
            S.hp = Rules.MaxHp(S);
            S.hunger = S.thirst = S.energy = 100;
            S.stamina = Rules.MaxStamina(S, data);
            S.mana = Rules.MaxMana(S);
            S.water = Rules.WaterskinSize;
        }

        public void DevCoins(int n) => S.coins += n;

        public void DevItems()
        {
            foreach (var it in data.file.items) S.AddItem(it.id, it.food ? 3 : 1);
        }

        public void DevSpells()
        {
            foreach (var sp in data.file.spells) if (!S.spells.Contains(sp.id)) S.spells.Add(sp.id);
        }

        public void DevStat(int stat, int n) => S.stats[stat] = Math.Max(1, S.stats[stat] + n);

        /// <summary>Sets the clock to the next given hour (6 = morning, 21 = night).</summary>
        public void DevTime(int hour)
        {
            int now = S.minutes % 1440;
            int add = hour * 60 - now;
            if (add <= 0) add += 1440;
            S.minutes += add;
        }

        public GearItem DevGear(string baseId, string rank) => MakeGear(baseId, rank, null);

        /// <summary>Wins the current fight on the spot.</summary>
        public void DevWinFight()
        {
            if (!S.inCombat) return;
            S.inCombat = false;
            S.result = "Dev mode: fight won.";
            GoTo(S.combatWin);
        }

        public IEnumerable<string> AllScenes => scenes.Keys.OrderBy(k => k);
    }
}
