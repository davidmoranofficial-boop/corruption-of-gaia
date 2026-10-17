using System;
using System.Collections.Generic;

namespace Loran.Core
{
    // The shapes of the JSON files in Resources/Story and Resources/Data.
    // See Resources/Story/README.md for how to write content.

    [Serializable]
    public class StoryFile
    {
        public List<SceneDef> scenes = new List<SceneDef>();
    }

    [Serializable]
    public class SceneDef
    {
        public string id;
        public string place;                   // shown above the title, e.g. "Skelmed"
        public string title;
        public bool safe;                      // collapsing or sleeping here is safe
        public List<string> text = new List<string>();      // paragraphs; {name} etc. are filled in
        public List<CondText> extra = new List<CondText>(); // paragraphs shown only when their conditions hold
        public List<string> onEnter = new List<string>();   // effects run when the player arrives
        public List<ChoiceDef> choices = new List<ChoiceDef>();
    }

    [Serializable]
    public class CondText
    {
        public List<string> when = new List<string>();
        public string text;
    }

    [Serializable]
    public class ChoiceDef
    {
        public string label;
        public List<string> when = new List<string>();      // hidden unless all hold
        public List<string> need = new List<string>();      // shown but disabled unless all hold
        public string needText;                             // why it's disabled
        public int time;                                    // minutes
        public int stamina;
        public int energy;
        public List<string> train = new List<string>();     // e.g. "str+3"
        public string why;                                  // used in "Strength rose to 4 from splitting wood"
        public List<string> effects = new List<string>();
        public string result;                               // shown at the top of the next scene
        public string go;                                   // next scene; empty = stay
    }

    [Serializable]
    public class DataFile
    {
        public List<ItemDef> items = new List<ItemDef>();
        public List<EnemyDef> enemies = new List<EnemyDef>();
        public List<ClassDef> classes = new List<ClassDef>();
        public List<BackgroundDef> backgrounds = new List<BackgroundDef>();
        public List<GearDef> gear = new List<GearDef>();
        public List<TraitDef> traits = new List<TraitDef>();
        public List<MaterialDef> materials = new List<MaterialDef>();
        public List<TerrainDef> terrains = new List<TerrainDef>();
        public List<RegionDef> regions = new List<RegionDef>();
        public List<SpellDef> spells = new List<SpellDef>();
        public List<JournalDef> journal = new List<JournalDef>();
        public List<string> builds = new List<string>();
        public List<string> hair = new List<string>();
        public List<string> skins = new List<string>();
    }

    [Serializable] public class ItemDef { public string id, name, desc; public float hunger, thirst, energy; public bool food; }
    [Serializable]
    public class EnemyDef
    {
        public string id, name, desc;
        public string rank = "F";
        public float hp;
        public int atkMin, atkMax;
        public int speed = 3;                  // compared with your Agility: the faster side acts first
        public string art = "";                // sprite in Resources/Sprites/Enemies (defaults to the id)
        public List<EnemyMove> moves = new List<EnemyMove>();
    }

    /// <summary>Something an enemy can do. Its tell is shown before it acts, so the player can react.</summary>
    [Serializable]
    public class EnemyMove
    {
        public string id, name;
        public string tell;                    // "It drops low. It's going to lunge."
        public float power = 1;                // multiplies the enemy's attack roll; 0 = no attack
        public bool guard;                     // it holds back: your hits do half damage this round
        public int weight = 1;                 // how often it picks this move
    }
    [Serializable] public class ClassDef { public string id, name, desc, stat; public int statBonus, staminaBonus; }
    [Serializable] public class BackgroundDef { public string id, name, desc, stat; }
    [Serializable]
    public class GearDef
    {
        public string id, name, desc;
        public string slot = "weapon";         // one of Engine.GearSlots: weapon, offhand, head, neck, body, hands, legs, feet, ring
        public bool twoHanded;                 // a weapon that takes both hands, so no shield
        public string rank = "F";              // the rank it has when found or given plainly
        public string part = "";               // where a gem goes, e.g. "pommel"
        public int damage, stamina;
        public float guard;
    }

    /// <summary>A trait gear can carry above F rank. The numbers add up across everything you wear.</summary>
    [Serializable]
    public class TraitDef
    {
        public string id, name, desc;
        public float crit;                     // extra critical multiplier
        public float zone;                     // wider timing zone (fraction of the bar)
        public float guard;                    // extra share of each hit blocked
        public int stamina;                    // extra max stamina
        public float fire;                     // fire damage added to every hit
        public float ward;                     // share of corruption growth held back
        public float light;                    // share of attack stamina saved
        public int speed;                      // added to Agility when working out who acts first
    }

    /// <summary>A material or gem worked into gear. Its trait list decides which traits can appear (repeats make one likelier).</summary>
    [Serializable]
    public class MaterialDef
    {
        public string id, name, desc;          // desc may use {part}
        public List<string> traits = new List<string>();
    }
    [Serializable] public class SpellDef { public string id, name, desc; }
    [Serializable] public class JournalDef { public string text; public List<string> when = new List<string>(); }

    /// <summary>
    /// One kind of ground on the region maps, keyed by the letter used in a region's rows.
    /// cost is walking time against a road (road = 1); 0 means you can't walk on it at all.
    /// </summary>
    [Serializable]
    public class TerrainDef
    {
        public string key, name;
        public string title;                                  // shown as the heading while you stand on it, e.g. "In the forest"
        public string desc;
        public float cost = 1.5f;
        public int sight = 2;                                 // how many tiles you can see from it
        public int stamina;                                   // extra stamina per tile, for hard going
        public float danger = 1;                              // multiplies the chance of trouble
        public float corruption;                              // added to corruption for each tile walked
        public List<string> forage = new List<string>();      // items you might find foraging
    }

    /// <summary>
    /// A region map: a grid of terrain letters (made by Tools/Art/regions.py, with its picture), the places on it,
    /// the random events that can happen walking it, and fixed story beats on certain tiles.
    /// </summary>
    [Serializable]
    public class RegionDef
    {
        public string id, name;
        public string night;                                  // extra line shown while walking after dark
        public int eventChance = 15;                          // % chance of an event per tile walked
        public List<PlaceDef> places = new List<PlaceDef>();
        public List<TravelEvent> events = new List<TravelEvent>();
        public List<BeatDef> beats = new List<BeatDef>();
        public List<string> rows = new List<string>();        // row 0 is the top (north) of the map
    }

    /// <summary>
    /// A place on a region map. It's unknown until you see it (or hear a rumour), then seen, then visited.
    /// Hidden places only show up when you walk right next to them.
    /// </summary>
    [Serializable]
    public class PlaceDef
    {
        public string id, name;
        public string kind = "spot";                          // the map icon: hut, village, town, quarry, camp, cave, ruin, stones, cove, blight, spot
        public string desc;                                   // shown while you stand there
        public string note;                                   // one line for the list of places you know
        public string rumour;                                 // shown for a rumoured place you haven't found yet
        public int x, y;
        public bool hidden;
        public int farSight;                                  // seen from this many tiles away even through fog (towers, smoke, lights)
        public List<string> rumourWhen = new List<string>();  // marked with a ? on your map while these hold
        public string enterLabel;                             // e.g. "Walk into Skelmed"
        public List<CondScene> enter = new List<CondScene>(); // where going in takes you (the first whose conditions hold)
        public List<ChoiceDef> choices = new List<ChoiceDef>();   // things to do here without leaving the map
        public List<string> forage = new List<string>();
    }

    /// <summary>A fixed story scene waiting on the map, within radius tiles of x, y, while its conditions hold.</summary>
    [Serializable]
    public class BeatDef
    {
        public int x, y, radius;
        public string scene;
        public List<string> when = new List<string>();
    }

    [Serializable]
    public class CondScene
    {
        public List<string> when = new List<string>();
        public string scene;
    }

    /// <summary>
    /// Something that can happen while walking. kind is find, danger, weather or meet: pace, night and the ground change the odds.
    /// terrain lists the letters it can happen on (empty = anywhere); area is x0, y0, x1, y1 (empty = the whole map).
    /// </summary>
    [Serializable]
    public class TravelEvent
    {
        public string scene;
        public string terrain = "";
        public List<int> area = new List<int>();
        public string kind = "meet";
        public int weight = 1;
        public bool once;
        public List<string> when = new List<string>();
    }

    /// <summary>Lookups over the loaded data.</summary>
    public class GameData
    {
        public readonly DataFile file;
        public GameData(DataFile file) { this.file = file; }

        static T Find<T>(List<T> list, Func<T, string> id, string key) where T : class
        {
            if (string.IsNullOrEmpty(key)) return null;
            foreach (var x in list) if (id(x) == key) return x;
            return null;
        }

        public ItemDef Item(string id) => Find(file.items, x => x.id, id);
        public EnemyDef Enemy(string id) => Find(file.enemies, x => x.id, id);
        public ClassDef Class(string id) => Find(file.classes, x => x.id, id);
        public BackgroundDef Background(string id) => Find(file.backgrounds, x => x.id, id);
        public GearDef Gear(string id) => Find(file.gear, x => x.id, id);
        public TraitDef Trait(string id) => Find(file.traits, x => x.id, id);
        public MaterialDef Material(string id) => Find(file.materials, x => x.id, id);
        public RegionDef Region(string id) => Find(file.regions, x => x.id, id);
        public TerrainDef Terrain(char key)
        {
            foreach (var t in file.terrains) if (!string.IsNullOrEmpty(t.key) && t.key[0] == key) return t;
            return null;
        }

        /// <summary>A place by id, and the region it's in.</summary>
        public (RegionDef region, PlaceDef place) Place(string id)
        {
            foreach (var r in file.regions)
                foreach (var p in r.places)
                    if (p.id == id) return (r, p);
            return (null, null);
        }
        public SpellDef Spell(string id) => Find(file.spells, x => x.id, id);
    }

    /// <summary>Saving and loading is done by whichever JSON library the host has (JsonUtility in Unity).</summary>
    public interface IJson
    {
        string Write(object o);
        T Read<T>(string json);
    }
}
