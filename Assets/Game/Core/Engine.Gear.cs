using System;
using System.Collections.Generic;
using System.Linq;

namespace Loran.Core
{
    /// <summary>Gear: ranks, traits, slots, equipping.</summary>
    public partial class Engine
    {
        public static readonly string[] GearSlots = { "weapon", "offhand", "head", "neck", "body", "hands", "legs", "feet", "ring" };
        public static readonly string[] GearSlotNames = { "Main hand", "Off hand", "Head", "Neck", "Body", "Hands", "Legs", "Feet", "Ring" };

        /// <summary>How many of each slot you can wear at once. Two rings; one of everything else.</summary>
        public static int SlotCapacity(string slot) => slot == "ring" ? 2 : 1;

        public static string SlotName(string slot)
        {
            int i = Array.IndexOf(GearSlots, slot);
            return i < 0 ? slot : GearSlotNames[i];
        }

        /// <summary>The clothes you wash up in. Everyone starts wearing these.</summary>
        public static readonly string[] StartingClothes = { "castaway_shirt", "castaway_trousers", "castaway_boots" };

        /// <summary>Gives the starting clothes once (also to older saves made before clothes existed).</summary>
        void GiveClothes()
        {
            if (S.Has("clothed")) return;
            S.flags.Add("clothed");
            foreach (var id in StartingClothes)
                if (data.Gear(id) != null && !S.gearItems.Any(g => g.id == id)) MakeGear(id, null, null);
        }

        /// <summary>
        /// Makes a new piece of gear and puts it on, moving whatever was in that slot to the pack.
        /// rank defaults to the base gear's own rank. Above F, it rolls traits: from the material's list if there is one, otherwise from all traits.
        /// </summary>
        public GearItem MakeGear(string baseId, string rank, string materialId, float statScale = 1f, string namePrefix = null, bool equip = true)
        {
            var def = data.Gear(baseId) ?? throw new Exception("Unknown gear '" + baseId + "'");
            var mat = data.Material(materialId);
            rank = string.IsNullOrEmpty(rank) ? (string.IsNullOrEmpty(def.rank) ? "F" : def.rank) : rank;
            float k = Rules.RankMultiplier(rank) * statScale;

            var item = new GearItem
            {
                uid = S.nextGearId++,
                id = def.id,
                rank = rank,
                slot = def.slot,
                twoHanded = def.twoHanded,
                damage = (float)Math.Round(def.damage * k, 1),
                guard = Math.Min(0.6f, def.guard * k),
                stamina = (int)Math.Round(def.stamina * k),
            };

            var pool = mat != null && mat.traits.Count > 0 ? mat.traits : data.file.traits.Select(t => t.id).ToList();
            int want = Rules.TraitsForRank(rank);
            for (int guard = 0; item.traits.Count < want && guard < 50; guard++)
            {
                string t = pool[Roll(0, pool.Count - 1)];
                if (!item.traits.Contains(t)) item.traits.Add(t);
                if (item.traits.Count >= pool.Distinct().Count()) break;
            }

            // The first trait gives the piece its name: "Warded iron knife".
            var first = item.traits.Count > 0 ? data.Trait(item.traits[0]) : null;
            item.name = first != null ? first.name + " " + Lower(def.name) : def.name;
            if (!string.IsNullOrEmpty(namePrefix)) item.name = namePrefix + " " + Lower(item.name);
            item.desc = def.desc;
            if (mat != null && !string.IsNullOrEmpty(mat.desc))
                item.desc = (item.desc + " " + mat.desc.Replace("{part}", string.IsNullOrEmpty(def.part) ? "metal" : def.part)).Trim();

            S.gearItems.Add(item);
            if (equip) Equip(item.uid, force: true);
            return item;
        }

        static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        /// <summary>Puts a piece of gear on. Whatever was in its slot goes back in the pack.</summary>
        public bool Equip(int uid, bool force = false)
        {
            if (!force && !FreeToAct) return false;
            var item = S.gearItems.FirstOrDefault(g => g.uid == uid);
            if (item == null) return false;
            if (item.equipped) return true;
            // Make room in the slot: a third ring pushes out the one you put on first.
            var same = S.gearItems.Where(g => g.equipped && g.slot == item.slot).ToList();
            for (int i = 0; i <= same.Count - SlotCapacity(item.slot); i++) same[i].equipped = false;
            // A two-handed weapon and a shield can't be held together.
            if (item.twoHanded)
                foreach (var g in S.gearItems) if (g.equipped && g.slot == "offhand") g.equipped = false;
            if (item.slot == "offhand")
                foreach (var g in S.gearItems) if (g.equipped && g.slot == "weapon" && g.twoHanded) g.equipped = false;
            S.gearItems.Remove(item);
            S.gearItems.Add(item);   // most recently worn goes last, so it's the last to be pushed out
            item.equipped = true;
            Rules.Clamp(S, data);
            return true;
        }

        public bool Unequip(int uid)
        {
            if (!FreeToAct) return false;
            var item = S.gearItems.FirstOrDefault(g => g.uid == uid);
            if (item == null || !item.equipped) return false;
            item.equipped = false;
            Rules.Clamp(S, data);
            return true;
        }

        /// <summary>One line describing what a piece of gear does, e.g. "+4.6 damage · Warded".</summary>
        public string GearSummary(GearItem g)
        {
            var parts = new List<string>();
            if (g.damage > 0) parts.Add("+" + g.damage.ToString("0.#") + " damage");
            if (g.guard > 0) parts.Add("blocks " + Math.Round(g.guard * 100) + "% of each hit");
            if (g.stamina > 0) parts.Add("+" + g.stamina + " max stamina");
            int warmth = data.Gear(g.id)?.warmth ?? 0;
            if (warmth > 0) parts.Add("+" + warmth + "° warmth");
            return string.Join(" · ", parts);
        }

        /// <summary>Saves from before gear ranks had a single gear id. Turn it into a real piece of gear.</summary>
        void MigrateGear()
        {
            foreach (var g in S.gearItems)
            {
                if (g.slot == "trinket") g.slot = "neck";   // the old single trinket slot
                var def = data.Gear(g.id);
                if (def != null) g.twoHanded = def.twoHanded;
            }
            if (string.IsNullOrEmpty(S.gear)) return;
            string id = S.gear;
            S.gear = "";
            if (S.gearItems.Count > 0) return;
            if (id.EndsWith("_gem") && data.Gear(id.Substring(0, id.Length - 4)) != null)
                MakeGear(id.Substring(0, id.Length - 4), "E", data.Material("green_gem") != null ? "green_gem" : null);
            else if (data.Gear(id) != null)
                MakeGear(id, null, null);
        }
    }
}
