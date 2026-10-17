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
            ("village", "Skelmed square"),
            ("road", "The map, from Yenson's shack"),
            ("map", "The map, all of it uncovered"),
            ("forge_first", "Forge: your first gear"),
            ("forge_iron", "Forge: buy iron (with a gem)"),
            ("dog", "Fight the wolf"),
            ("crab", "Fight the rock crab"),
            ("stone", "The class stone"),
            ("shack", "Wake in Yenson's shack"),
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
            foreach (var f in new[] { "y_what", "y_where", "y_stew", "y_thanks", "y_supplies", "yenson_road", "arrived", "dog_met" })
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
                case "village": Jump("village"); break;
                case "shack":
                    S.flags.RemoveAll(f => f.StartsWith("y_") || f == "yenson_road" || f == "arrived" || f == "dog_met");
                    Jump("shack");
                    break;
                case "road":
                    S.flags.Remove("arrived");
                    S.flags.Remove("dog_met");
                    S.region = ""; S.fog = "";
                    MoveTo("shack");
                    Jump(TravelId);
                    break;
                case "map":
                {
                    Flag("arrived");
                    S.region = ""; S.fog = "";
                    MoveTo("skelmed");
                    S.fog = new string('1', S.fog.Length);
                    foreach (var p in Region.places) Flag("place_seen:" + p.id);
                    S.Toasts.Clear();
                    Jump(TravelId);
                    break;
                }
                case "forge_first":
                    Flag("smith_mined"); Flag("gem_shown");
                    Jump("smith_craft");
                    break;
                case "forge_iron":
                    Flag("smith_mined"); Flag("smith_done");
                    S.coins = 30;
                    S.AddItem("gem", 1);
                    Jump("smith_forge");
                    break;
                case "dog":
                case "crab":
                {
                    S.flags.Remove("arrived");
                    MoveTo("headland");
                    S.mapY -= 1;                         // just past the headland
                    Reveal(S.mapX, S.mapY, Sight());
                    S.scene = id == "dog" ? "dog" : "ev_crab";
                    StartCombat(id, id == "dog" ? "dog_won" : "ev_crab_won", TravelId);
                    break;
                }
                case "stone":
                    Flag("hunt_done"); Flag("smith_mined"); Flag("smith_done"); Flag("mage_done");
                    if (!S.spells.Contains("draw_water")) S.spells.Add("draw_water");
                    if (!S.spells.Contains("kindle")) S.spells.Add("kindle");
                    Jump("stone");
                    break;
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
