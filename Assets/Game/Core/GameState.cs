using System;
using System.Collections.Generic;

namespace Loran.Core
{
    /// <summary>Stat indices. Stats start at character creation and grow through training and repetition.</summary>
    public static class Stat
    {
        public const int Str = 0, End = 1, Agi = 2, Int = 3, Cha = 4;
        public const int Count = 5;
        public static readonly string[] Ids = { "str", "end", "agi", "int", "cha" };
        public static readonly string[] Names = { "Strength", "Endurance", "Agility", "Intellect", "Charisma" };

        public static int Parse(string id)
        {
            int i = Array.IndexOf(Ids, id);
            if (i < 0) throw new ArgumentException("Unknown stat '" + id + "'");
            return i;
        }
    }

    [Serializable] public class ItemStack { public string id; public int count; }
    [Serializable] public class RepEntry { public string place; public int value; }

    /// <summary>
    /// One piece of gear the player owns. Each one is its own object, so two "iron knives" can have
    /// different ranks, names and traits.
    /// </summary>
    [Serializable]
    public class GearItem
    {
        public int uid;
        public string id = "";                 // the base gear in data.json, e.g. "knife"
        public string name = "";
        public string desc = "";
        public string rank = "F";              // F (common) up to S (legendary)
        public string slot = "weapon";         // weapon, offhand, body, hands, trinket
        public float damage, guard;
        public int stamina;
        public List<string> traits = new List<string>();
        public bool equipped;
    }

    /// <summary>
    /// Everything about one playthrough. Plain fields only, so Unity's JsonUtility
    /// (and Newtonsoft in the tests) can save and load it.
    /// </summary>
    [Serializable]
    public class GameState
    {
        public string scene = "title";
        public string deathMode = "basic";     // "basic" (restart at last sleep) or "perma"; locked at New Game

        // Character creation
        public string name = "";
        public string gender = "";             // "male", "female" or "other"; used for how people refer to you
        public string build = "";
        public string hair = "";
        public string background = "";
        public int[] stats = { 3, 3, 3, 3, 3 };
        public float[] xp = { 0, 0, 0, 0, 0 };

        // Needs. 100 = full/fine, 0 = empty.
        public float hp = 100, hunger = 100, thirst = 100, stamina = 100, energy = 100;

        public int minutes;                    // in-game minutes since Day 1 00:00
        public float corruption;               // 0-100 %
        public bool hud;                       // the HUD appears once the God gives the boon

        public int coins;
        public int water;                      // drinks left in the waterskin
        public List<string> flags = new List<string>();
        public List<ItemStack> items = new List<ItemStack>();
        public List<string> spells = new List<string>();
        public string gear = "";               // old saves only: the single piece of gear from before gear ranks
        public List<GearItem> gearItems = new List<GearItem>();
        public int nextGearId = 1;
        public string playerClass = "";
        public List<RepEntry> rep = new List<RepEntry>();

        // Combat (JsonUtility can't store a null object, so these are flat fields)
        public bool inCombat;
        public string enemy = "";
        public float enemyHp;
        public string combatWin = "", combatFlee = "";
        public bool defending;
        public string enemyMove = "";          // what the enemy is about to do (shown to the player as a tell)
        public int round;
        public List<string> battleLog = new List<string>();

        // Travel: which road, how many steps along it, and which way you're facing (1 = on, -1 = back)
        public string travelRoute = "";
        public int travelAt;
        public int travelDir = 1;

        // The forge: what's being made, from what, the best rank it can reach, and where to go after
        public string forging = "";
        public string forgeMaterial = "";
        public string forgeMax = "";
        public string forgeNext = "";
        public string forgeIntro = "";

        public string afterCollapse = "";
        public string deathCause = "";
        public string result = "";             // what just happened, shown above the scene text
        public bool repeat;                    // stayed in the same scene, so only the result is shown, not the scene text again
        public string lastSleep = "";          // JSON snapshot taken when the player sleeps
        public int rngCalls;
        public int seed;

        [NonSerialized] public List<string> toasts;
        public List<string> Toasts => toasts ??= new List<string>();

        public bool Has(string flag) => flags.Contains(flag);

        /// <summary>Brings older saves up to date (e.g. saves from before Charisma existed).</summary>
        public void Upgrade()
        {
            if (stats == null || stats.Length < Stat.Count)
            {
                var s = new int[Stat.Count];
                for (int i = 0; i < s.Length; i++) s[i] = stats != null && i < stats.Length ? stats[i] : 3;
                stats = s;
            }
            if (xp == null || xp.Length < Stat.Count)
            {
                var x = new float[Stat.Count];
                if (xp != null) Array.Copy(xp, x, Math.Min(xp.Length, x.Length));
                xp = x;
            }
            gender ??= "";
            gearItems ??= new List<GearItem>();
            foreach (var g in gearItems) g.traits ??= new List<string>();
            battleLog ??= new List<string>();
            enemyMove ??= "";
            travelRoute ??= "";
            forging ??= ""; forgeMaterial ??= ""; forgeMax ??= ""; forgeNext ??= ""; forgeIntro ??= "";
            if (travelDir == 0) travelDir = 1;
            if (nextGearId < 1) nextGearId = 1;
        }

        public int Count(string itemId)
        {
            foreach (var it in items) if (it.id == itemId) return it.count;
            return 0;
        }

        public void AddItem(string itemId, int n)
        {
            foreach (var it in items)
            {
                if (it.id != itemId) continue;
                it.count = Math.Max(0, it.count + n);
                if (it.count == 0) items.Remove(it);
                return;
            }
            if (n > 0) items.Add(new ItemStack { id = itemId, count = n });
        }

        public IEnumerable<GearItem> Equipped
        {
            get { foreach (var g in gearItems) if (g.equipped) yield return g; }
        }

        public GearItem InSlot(string slot)
        {
            foreach (var g in gearItems) if (g.equipped && g.slot == slot) return g;
            return null;
        }

        public int Rep(string place)
        {
            foreach (var r in rep) if (r.place == place) return r.value;
            return 0;
        }

        public void AddRep(string place, int n)
        {
            foreach (var r in rep)
                if (r.place == place) { r.value = Math.Clamp(r.value + n, -100, 100); return; }
            rep.Add(new RepEntry { place = place, value = Math.Clamp(n, -100, 100) });
        }

        public int Day => minutes / 1440 + 1;
        public int Hour => minutes % 1440 / 60;
        public int Minute => minutes % 60;
    }
}
