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
        public int mana;                                    // spells
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
        public List<SkillDef> skills = new List<SkillDef>();
        public List<MoveDef> moves = new List<MoveDef>();
        public List<FollowerDef> followers = new List<FollowerDef>();
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
        public List<AfflictionDef> afflictions = new List<AfflictionDef>();
        public List<TreatmentDef> treatments = new List<TreatmentDef>();
        public List<ClimateDef> climates = new List<ClimateDef>();
        public List<WeatherDef> weathers = new List<WeatherDef>();
    }

    /// <summary>
    /// A wound or ailment: a bite, a broken bone, a fever, hypothermia. It hurts you while you have it (bleeding,
    /// weakness, no running) and goes once it's treated and has had time to heal, or by itself, or gets worse.
    /// </summary>
    [Serializable]
    public class AfflictionDef
    {
        public string id, name, desc;
        public float bleed;                    // health lost per hour until it's treated
        public float drain;                    // extra energy lost per hour until it's healed
        public float fight = 1;                // multiplies the damage you (or a companion) deal
        public float stamina = 1;              // multiplies your top stamina
        public bool noRun;                     // can't run, and fleeing a fight is near hopeless
        public bool noFight;                   // a companion with this can't fight
        public int fumble;                     // % chance a fight action goes wrong
        public float slow = 1;                 // walking takes this many times as long
        public float thirst;                   // extra thirst per hour
        public List<string> treat = new List<string>();   // treatment steps, any order
        public float heal;                     // hours to heal once every step is done
        public float selfHeal;                 // untreated, it goes by itself after this many hours (0 = never)
        public string worsen = "";             // it turns into this...
        public float worsenAfter;              // ...after this many hours...
        public string worsenUnless = "";       // ...unless this step was done
        public float deadline;                 // untreated after this many hours, it kills (0 = never)
        public string cure = "";               // warm / cool: it goes once your body is warm (or cool) again
    }

    /// <summary>One step in treating a wound. items are alternatives ("split_wood+bandage" needs both); a tool is needed but not used up.</summary>
    [Serializable]
    public class TreatmentDef
    {
        public string id, name, desc;
        public List<string> items = new List<string>();
        public string tool = "";
        public int minutes = 10;
    }

    /// <summary>The temperatures and weather of a kind of country. Areas name theirs with climate.</summary>
    [Serializable]
    public class ClimateDef
    {
        public string id, name;
        public List<SeasonClimate> seasons = new List<SeasonClimate>();
    }

    /// <summary>A place's weather in one season: the warmest and coldest hours, and how likely each weather is.</summary>
    [Serializable]
    public class SeasonClimate
    {
        public string season;                  // spring, summer, autumn, winter
        public float day = 15, night = 6;      // °C at the warmest and coldest hours
        public List<WeatherChance> weather = new List<WeatherChance>();
    }
    [Serializable] public class WeatherChance { public string id; public int weight = 1; }
    [Serializable] public class WoundChance { public string id; public int chance; }

    [Serializable]
    public class WeatherDef
    {
        public string id, name, desc;
        public float temp;                     // added to the air temperature
        public float wet;                      // how fast it soaks you, per hour, out in it
        public float dry = 10;                 // how fast you dry, per hour, out in it (when it isn't soaking you)
        public string overlay = "";            // rain, storm, fog, heat: drawn over the world
    }

    [Serializable]
    public class ItemDef
    {
        public string id, name, desc;
        public float hunger, thirst, energy;
        public bool food;
        public string kind = "";               // which pocket of the bag: food, medicine, material, tool or key (left empty: food if it's food)
        public string Kind() => !string.IsNullOrEmpty(kind) ? kind : food ? "food" : "material";
    }
    [Serializable]
    public class EnemyDef
    {
        public string id, name, desc;
        public string rank = "F";
        public float hp;
        public int atkMin, atkMax;
        public int speed = 3;                  // compared with your Agility: the faster side acts first
        public float enrageBelow;              // below this share of its health it flies into a rage (0 = never)
        public string art = "";                // sprite in Resources/Sprites/Enemies (defaults to the id)
        public int exp;                        // experience for beating it
        public float size = 1;                 // how big it's drawn in battle (a boss is bigger)
        public bool boss;
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
        // What sort of move it is, and so the right answer to it:
        //   strike: an ordinary blow. Block it, or dodge it cleanly.
        //   heavy:  a big blow. It smashes through a block; dodge it and it's left wide open.
        //   feint:  looks like a heavy, but it's a trick. Dodge and you're caught; block and it's nothing.
        //   guard:  it holds back. Hit it and your blow lands half as hard and it snaps back; catch your breath instead.
        //   howl:   it works itself up. Hurt it this round or it's enraged for the rest of the fight.
        // Left empty, it's worked out from power and guard.
        public string kind = "";
        public List<WoundChance> wounds = new List<WoundChance>();   // % chances a hit from this leaves a wound

        public string KindOf() => !string.IsNullOrEmpty(kind) ? kind : guard || power <= 0 ? "guard" : power >= 1.5f ? "heavy" : "strike";
        public bool Attacks() { var k = KindOf(); return k == "strike" || k == "heavy" || k == "feint"; }
    }

    /// <summary>
    /// A skill that grows with use. Combat skills (daggers, axes, bows, shields, fists, magic) grow by fighting with them
    /// and unlock moves; life skills (mining, smithing, woodcutting, hunting) grow by doing the work and make it easier.
    /// </summary>
    [Serializable] public class SkillDef { public string id, name, kind = "combat", desc; }

    /// <summary>A combat move a skill unlocks at a level. It shows under Skills in a fight while you can use it.</summary>
    [Serializable]
    public class MoveDef
    {
        public string id, name, skill, desc;
        public int level = 2;                  // the skill level that unlocks it
        public int stamina, energy, mana;
        public float power = 1;                // multiplies the hit
        public int hits = 1;                   // strikes in one go
        public bool first;                     // always goes before the enemy
        public bool stagger;                   // the enemy loses its next action
        public bool pierce;                    // ignores the enemy holding back
        public bool timing = true;             // uses the timing bar
        public string need = "";               // a condition, e.g. spell:kindle
    }

    /// <summary>Someone who can join your party: walks with you, fights at your side, and eats your food.</summary>
    [Serializable]
    public class FollowerDef
    {
        public string id, name, sprite, desc;
        public float hp;
        public int atkMin, atkMax;
        public string attack = "strikes at";   // "looses an arrow at"
        public float appetite = 1;             // how fast they get hungry and thirsty, compared with you
        public float hunger = 80, thirst = 80, energy = 90, morale = 60;   // how they are when they join
        public bool leaves = true;             // false: they stick with you however low their spirits get (a story companion)
        public string quest = "";              // their own quest, if they have one: doing it lifts their spirits
        public string until = "";              // a companion who's only with you for a while: how long (shown in the Party menu)
        public List<string> untilWhen = new List<string>();   // when that line shows
        public int warmth = 6;                 // how well dressed they are for the cold, in °C
        // How they look in battle, drawn with the same paper doll as you
        public string figure = "male", skin = "light", hair = "dark brown";
        public List<string> worn = new List<string>();   // gear ids they wear
    }
    [Serializable] public class BackgroundDef { public string id, name, desc, stat; }
    [Serializable]
    public class GearDef
    {
        public string id, name, desc;
        public string slot = "weapon";         // one of Engine.GearSlots: weapon, offhand, head, neck, body, hands, legs, feet, ring
        public bool twoHanded;                 // a weapon that takes both hands, so no shield
        public string rank = "F";              // the rank it has when found or given plainly
        public string part = "";               // where a gem goes, e.g. "pommel"
        public string skill = "";              // the combat skill a weapon or shield trains: dagger, axe, bow, shield
        public int damage, stamina;
        public float guard;
        public int warmth;                     // keeps out the cold, in °C
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
        public int exp;                        // experience for finishing it
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
        public string climate = "coast";                      // its temperatures and weather (climates in data.json)
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
        public SkillDef Skill(string id) => Find(file.skills, x => x.id, id);
        public MoveDef Move(string id) => Find(file.moves, x => x.id, id);
        public FollowerDef Follower(string id) => Find(file.followers, x => x.id, id);
        public BackgroundDef Background(string id) => Find(file.backgrounds, x => x.id, id);
        public GearDef Gear(string id) => Find(file.gear, x => x.id, id);
        public TraitDef Trait(string id) => Find(file.traits, x => x.id, id);
        public MaterialDef Material(string id) => Find(file.materials, x => x.id, id);
        public SpellDef Spell(string id) => Find(file.spells, x => x.id, id);
        public QuestDef Quest(string id) => Find(file.quests, x => x.id, id);
        public AreaDef Area(string id) => Find(file.areas, x => x.id, id);
        public AfflictionDef Affliction(string id) => Find(file.afflictions, x => x.id, id);
        public TreatmentDef Treatment(string id) => Find(file.treatments, x => x.id, id);
        public ClimateDef Climate(string id) => Find(file.climates, x => x.id, id);
        public WeatherDef Weather(string id) => Find(file.weathers, x => x.id, id);
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
