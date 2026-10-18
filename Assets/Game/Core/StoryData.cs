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
        public string speaker;                 // who's talking, shown on the talk box out in the world
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
        public List<TileDef> tiles = new List<TileDef>();
        public List<AreaDef> areas = new List<AreaDef>();
        public List<SpellDef> spells = new List<SpellDef>();
        public List<JournalDef> journal = new List<JournalDef>();
        public List<QuestDef> quests = new List<QuestDef>();
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
    /// A job someone gives you. Started with the effect quest:id and finished with complete:id (usually when you
    /// hand it in). The goals are what the quest log and the tracker show; a goal is met when its count reaches
    /// target (count is a value like item:firewood) or, without a count, when its condition holds.
    /// Conditions: quest:id (taken and not finished), ready:id (taken and every goal met), done:id (finished).
    /// </summary>
    [Serializable]
    public class QuestDef
    {
        public string id, name, giver, desc;
        public List<QuestGoal> goals = new List<QuestGoal>();
    }

    [Serializable]
    public class QuestGoal
    {
        public string text;
        public string count;                   // e.g. item:firewood
        public int target;
        public string when;                    // or a condition, e.g. flag:boar_killed
    }

    /// <summary>One kind of tile in the walkable areas, keyed by the letter used in an area's rows.</summary>
    [Serializable]
    public class TileDef
    {
        public string key, name;
        public bool solid;                                    // can't walk onto it
        public bool road;                                     // fewer fights on it
    }

    /// <summary>
    /// A top-down area you walk round tile by tile. Made by Tools/Art/world.py, which also paints its picture.
    /// </summary>
    [Serializable]
    public class AreaDef
    {
        public string id, name;
        public int minutes = 2;                               // time per step walking (half that running)
        public int eventChance = 3;                           // % per step: finds, meetings, weather
        public int fightChance = 6;                           // % per step: a random fight (halved on roads)
        public int fightGap = 10;                             // at least this many steps between fights
        public bool indoor;                                   // inside a building: no night shade, no camping
        public bool camp = true;                              // you can make camp here
        public List<SpotDef> spots = new List<SpotDef>();     // named places to put the player (travel:at:<id>)
        public List<WorldObj> objects = new List<WorldObj>();
        public List<ZoneDef> zones = new List<ZoneDef>();
        public List<UseDef> uses = new List<UseDef>();
        public List<TravelEvent> events = new List<TravelEvent>();
        public List<string> rows = new List<string>();        // row 0 is the top (north)
    }

    [Serializable]
    public class SpotDef
    {
        public string id;
        public int x, y;
        public string facing = "down";
    }

    /// <summary>Something standing in an area: a person, a sign, a door, a thing to pick up. You face it and press to use it.</summary>
    [Serializable]
    public class WorldObj
    {
        public string id, kind = "npc";                       // npc, sign, door, item, spot
        public string sprite = "";                            // Resources/Sprites/Walk (people) or World (things); empty = nothing drawn
        public int x, y;
        public string facing = "down";
        public string scene;                                  // the scene using it opens
        public string text;                                   // or just a line of text (signs)
        public bool once;                                     // gone after it's used
        public List<string> when = new List<string>();        // only there while these hold
    }

    /// <summary>A rectangle that runs a scene, or takes you somewhere, when you walk into it (while its conditions hold).</summary>
    [Serializable]
    public class ZoneDef
    {
        public int x0, y0, x1, y1;
        public string scene;
        public string to;                                     // or a spot to move you to (doors, the village gate)
        public List<CondScene> enter = new List<CondScene>(); // or the first of these whose conditions hold
        public List<string> when = new List<string>();
    }

    /// <summary>Facing a tile of this kind and pressing to use it opens a scene (drinking from a stream).</summary>
    [Serializable]
    public class UseDef
    {
        public string tile;
        public string scene;
    }

    [Serializable]
    public class CondScene
    {
        public List<string> when = new List<string>();
        public string scene;
    }

    /// <summary>
    /// Something that can happen while walking. kind is fight, find, weather or meet. A fight starts a battle with enemy
    /// straight away and goes to win afterwards; the rest open scene. terrain lists the tile letters it can happen on
    /// (empty = anywhere); area is x0, y0, x1, y1 (empty = the whole area).
    /// </summary>
    [Serializable]
    public class TravelEvent
    {
        public string scene;
        public string enemy = "", win = "";
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
        public SpellDef Spell(string id) => Find(file.spells, x => x.id, id);
        public QuestDef Quest(string id) => Find(file.quests, x => x.id, id);
        public AreaDef Area(string id) => Find(file.areas, x => x.id, id);
        public TileDef Tile(char key)
        {
            foreach (var t in file.tiles) if (!string.IsNullOrEmpty(t.key) && t.key[0] == key) return t;
            return null;
        }

        /// <summary>A named spot, and the area it's in.</summary>
        public (AreaDef area, SpotDef spot) Spot(string id)
        {
            foreach (var a in file.areas)
                foreach (var p in a.spots)
                    if (p.id == id) return (a, p);
            return (null, null);
        }
    }

    /// <summary>Saving and loading is done by whichever JSON library the host has (JsonUtility in Unity).</summary>
    public interface IJson
    {
        string Write(object o);
        T Read<T>(string json);
    }
}
