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
            ("forge_first", "Forge: your first gear"),
            ("forge_iron", "Forge: buy iron"),
            ("dog", "Fight the wolf"),
            ("crab", "Fight the rock crab"),
        };

        /// <summary>A fresh test character, rested and supplied, standing in Skelmed on the morning of day 2.</summary>
        void DevCharacter()
        {
            NewGame("basic");
            CreateCharacter("Tester", "other", "Average", "Black", "warehouse", new[] { 1, 1, 1, 1, 1 });
            S.flags.Add(DevFlag);
            S.hud = true;
            S.minutes = 1440 + 6 * 60;
            S.corruption = Rules.StartCorruption;
            S.coins = 10;
            S.water = Rules.WaterskinSize;
            S.AddItem("bread", 2);
            S.AddItem("flint", 1);
            foreach (var f in new[] { "y_met", "y_what", "y_where", "y_stew", "y_thanks", "y_supplies", "yenson_road", "dog_met" })
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
                    S.flags.RemoveAll(f => f.StartsWith("y_") || f == "yenson_road" || f == "dog_met");
                    Jump("wake");
                    break;
                case "road":
                    S.flags.Remove("dog_met");
                    MoveTo("shack");
                    Jump(TravelId);
                    break;
                case "forge_first":
                    MoveTo("village_gate");
                    Jump("smith");
                    break;
                case "forge_iron":
                    MoveTo("village_gate");
                    Flag("smith_done");
                    S.coins = 30;
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
            Snapshot();
        }

        void Flag(string f) { if (!S.Has(f)) S.flags.Add(f); }

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
