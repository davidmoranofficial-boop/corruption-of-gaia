# Loran: how the game is put together

Press **Play** in any scene. `Unity/GameBoot.cs` builds the whole game at startup, so there are no scenes, prefabs or objects to set up.

## Folders

| Folder | What's in it |
|---|---|
| `Core/` | The rules: time, needs, corruption, stats, combat, saving, and the engine that runs the story. Plain C# with no Unity code, so it can be tested outside Unity. |
| `Unity/` | The screen: HUD, story text, choices, side panel, New Game and character creation. Built with UI Toolkit from code. |
| `Resources/Story/` | The story, one JSON file per area. **This is where the writing lives.** |
| `Resources/Data/data.json` | Items, enemies, skills, moves, followers, backgrounds, gear, spells and journal entries. |
| `Resources/UI/Game.uss` | Colours, sizes and fonts for the screen. |
| `../../Tools/CoreTests/` | Automated playthroughs. Run `dotnet run --project Tools/CoreTests` from the repo root. |

All the balancing numbers (how fast hunger drains, how fast corruption grows, and so on) are at the top of `Core/Rules.cs`.

## Dev mode

Press **F1** (or the key left of 1) in game, or **Dev mode** on the title screen. Quick starts drop a ready-made test character into any part of the game (the forge, either fight, the road, the guild, the pack leader boss and more). You can also jump to any scene, refill everything, give coins, items and spells, change stats, set the time, win a fight, level up, raise every skill, or make any gear at any rank. A game that has used dev mode is never autosaved, so your real save is safe. The quick starts live in `Core/Engine.Dev.cs`.

## Writing a scene

```json
{
  "id": "hunt",
  "place": "Skelmed",
  "title": "The hunters' lodge",
  "safe": true,
  "text": ["First paragraph.", "Second paragraph. {name} is the player's name; {they}, {them} and {their} follow their chosen sex."],
  "extra": [ { "when": ["flag:hunt_done"], "text": "Only shown once the hunters' job is done." } ],
  "onEnter": ["flag:visited_lodge"],
  "choices": [
    {
      "label": "Split and carry firewood",
      "when": ["!flag:hunt_wood"],
      "time": 90, "stamina": 30, "energy": 6,
      "train": ["str+3", "end+1"], "why": "splitting wood",
      "effects": ["flag:hunt_wood", "rep:skelmed+2"],
      "result": "Shown at the top of the next scene.",
      "go": "hunt"
    }
  ]
}
```

- **safe**: collapsing here won't get you robbed.
- **when**: the choice is hidden unless all of these are true. **need** (with **needText**): the choice is shown but greyed out, with that reason.
- **time / stamina / energy**: what the choice costs. Time always passes, and time feeds the corruption.
- **train**: stat practice. Stats go up through repetition (`str`, `end`, `agi`, `int`, `cha`).
- **go**: the next scene. Leave it out to stay in the same scene.

### Conditions (`when`, `need`, `extra`)

Put `!` in front of any condition to flip it. Join conditions with `|` when either will do: `!done:lodge|!done:quarry`.

`flag:name`, `item:bread`, `spell:kindle`, `follower:rook` (in your party; `follower:` alone means anyone), `party>=2`, `morale:rook<30`, `skill:dagger>=2`, `level>=3`, `gear`, `gear:knife`, `mode:basic`, `mode:perma`, `background:runner`, `gender:female`, `night`, and comparisons such as `coins>=3`, `water<4`, `rep:skelmed>=10`, `energy<20`, `stat:str>=5`, `item:bread>=2`, `day>=3`.

### Effects (`effects`, `onEnter`)

| Effect | What it does |
|---|---|
| `flag:name` / `unflag:name` | Remember (or forget) that something happened |
| `item:bread+2` / `item:bread-1` | Give or take items |
| `coins+5`, `coins-3` | Money |
| `rep:skelmed+10` | Local reputation (-100 to 100) |
| `corruption+0.3`, `corruption-0.2` | Push the corruption meter up or down |
| `hunger+40`, `thirst+20`, `energy+10`, `stamina+10`, `hp+10` (or `=` to set) | Needs |
| `water=4` | Fill the waterskin |
| `spell:draw_water`, `gear:knife`, `give:hunting_bow` | Learn a spell; give and equip gear at its own rank (usually F); `give` puts it in the pack without equipping |
| `exp:20` | Give EXP |
| `follow:rook` / `unfollow:rook` (`unfollow` alone: everyone) | Someone joins your party (if there's room), or leaves it |
| `morale:rook+10` | Lift or lower a party member's spirits |
| `forge:knife:D:green_gem` | Put a piece on the anvil and play the forge game. Quality decides the rank (a material gives +1 rank, quality 75+ gives +1, below 30 it's crude), never above the rank given. After forging, the player goes where the choice's `go` points |
| `craft:knife:E:green_gem` | Make gear at a rank (F to S). Above F it rolls traits from the material's list (or from all traits if no material) |
| `sleep:safe` / `sleep:rough` / `sleep:camp` | Sleep until morning. Sleeping saves: in basic mode you wake here if you die |
| `combat:dog:winScene:fleeScene` | Start a fight |
| `check:agi:40:passScene:failScene` | A stat check: 40% plus 8% per point of the stat |
| `clock:1:17:30`, `hud:on`, `save` | Set the time, show the HUD, take a save |

## Gear ranks

Gear in `data.json` is the base: name, slot (`weapon`, `offhand`, `body`, `hands`, `trinket`), damage, guard, stamina, and `part` (where a gem sits). Every piece the player gets is its own copy with a rank: F, E, D, C, B, A or S. A higher rank multiplies the numbers and adds traits: none at F, 1 at E and D, 2 at C and B, 3 at A and S. Traits are listed under `traits`, and `materials` say which traits a gem or metal can give (list one more than once to make it likelier).

## The walkable world

The whole game happens in the world now: there are no text pages. Areas are top-down maps you walk round tile by tile with the arrow keys (Shift runs, Space talks to or uses what you face, Enter makes camp where that's allowed). There are four: the coast road, Yenson's hut (an inside area), the village of Skelmed and the hills above it (the lane north out of the village). A thing in `World` sprites taller than 16 pixels stands up out of its tile. People talk in a box at the bottom of the screen; a scene's `speaker` puts their name on it. Each area lives under `areas` in `data.json` and is made by `Tools/Art/world.py` (run `python3 Tools/Art/build.py`), which also paints its picture, so to change an area, edit that script. The tile letters are listed under `tiles` (`solid` ones can't be walked on; `road` ones have fewer fights).

An area has `spots` (named places to put the player, used by the effect `travel:at:<spot>`), `objects` (people, signs, doors and things: facing one and pressing Space opens its `scene` or shows its `text`; `once` ones vanish after use; `when` says when they're there), `zones` (walking into the rectangle runs `scene`, or moves you to the spot `to`, which is how doors and the village gate work; or runs the first of `enter` whose `when` holds) and `uses` (facing a kind of tile, like the stream, the well or a bed, opens a scene). `indoor` areas have no night shade, and `camp: false` stops camping. Going into an area sets the flag `been:<area id>`.

`events` happen as you walk. `"kind": "fight"` events start a battle with `enemy` straight away, the old Final Fantasy way, and go to `win` afterwards; `fightChance` is the % per step (halved on roads), and there are always at least `fightGap` steps between fights. Other events open `scene` (`eventChance` % per step). `terrain` limits an event to some tile letters, `once` events happen once, and an event scene sends the player back out with `"go": "travel"`.

## Quests

Quests live under `quests` in `data.json`: an `id`, a `name`, who gives it (`giver`), a line of description and its `goals`. A goal is met when the value `count` (like `item:firewood`) reaches `target`, or, without a count, when its `when` condition holds. The effect `quest:<id>` gives a quest (it shows on the HUD tracker and in the Journal) and `complete:<id>` finishes it, usually in the giver's talk scene. Conditions: `quest:<id>` (taken, not finished), `ready:<id>` (taken and every goal met, so the giver can offer the hand-in line) and `done:<id>` (finished).

Yenson's three jobs run one after the other: fill his bucket at the spring in the copse south of the shack, bring three armfuls of deadwood, then kill the boar by the spring. He pays you with supplies and five coppers and sends you north to Skelmed.

Skelmed has three tutorial jobs, in any order:
- **Ida** (hunters' lodge, `lodge`): three hares from Rook's snares in the hills (a wolf is robbing the last one), four logs split at the chopping block, and the water butt filled with two pails from the hill stream. She pays five coppers and fish; you can turn the coin down or push for more.
- **Garran** (forge, `quarry`): dig three lumps of ore at the quarry with his pick, bring them back, forge your first piece. A green gem in the spoil heap can be shown to him (he lets you set it) or pocketed.
- **Orlen** (cottage in the hills, `orlen`): learn Draw Water and Kindle, light his brazier, then the net shed in the village catches fire. Put out its three fires with Draw Water or wet nets, or stay put and let it burn.

After the lodge job Ida asks one more thing (`pack`): Rook comes with you, with a hunting bow for you, to kill the pack leader in its den in the north-east of the hills. That's the first boss. Rook turns up hungry and Ida gives you a fish for him, which teaches feeding your party. He's a temporary companion: after the wolf he has business in the town inland and walks with you that far (`flag:rook_escort`), then goes his own way. With all three jobs done, Mira at the guild post puts your name in the guild book and gives you a guild card. Bess at the inn rents beds and sells bread.

## Levels and skills

There are no classes. Quests (`exp` on the quest) and fights (`exp` on the enemy) give EXP, and each level adds health and stamina and heals you (`Core/Rules.cs`). Skills, under `skills` in `data.json`, grow by doing:
- **Combat skills** (`kind: combat`): Fists, Dagger, Axe, Bow, Shield, Magic. Attacking trains the skill of the weapon in hand (gear has a `skill`), blocking with a shield trains Shield, casting trains Magic. Each skill adds damage and unlocks `moves` at set levels, which show in the fight menu while you hold the right weapon. A move can hit more than once (`hits`), go first (`first`), stun (`stagger`), ignore a guard (`pierce`), or skip the timing bar (`timing: false`).
- **Life skills** (`kind: life`): Mining, Smithing, Woodcutting, Hunting. A choice's `train` list can name a skill (`mining+4`). Each level makes that work cost less stamina, and Smithing raises forge quality.

Skills go up to 10. The Skills menu (K) shows them all.

## Party

`followers` in `data.json` are people who can join your party (`follow:<id>`), up to three at once (`Rules.PartyMax`). They walk in a line behind you, each attacks after you in a fight, and the enemy sometimes goes for them instead. If they go down they stay down with a grave wound until you bind it (see Death in the party).

A party lives off your supplies, so recruiting someone is a real cost. Each member has the same hunger, thirst and energy as you (`appetite` makes some eat faster), plus spirits (`morale`). At night, whoever's hungry eats from your pack and whoever's thirsty drinks from your waterskin; if there's nothing, they go without. You can also feed them or pass the waterskin from the Party menu (P). Being hungry, thirsty, exhausted, wounded or in low spirits each takes 15% off how hard they hit. Going without, cold camps and being knocked down lower their spirits; a bed, food and finishing their own `quest` raise them. At zero spirits a companion leaves, unless they have `leaves: false` (a story companion like Rook). `until` and `untilWhen` show a line in the Party menu for someone who's only with you for a while.

## Wounds and ailments

`afflictions` in `data.json` are wounds and illnesses, for you and for each companion: bites, cuts, deep gashes, broken arms and legs, cracked ribs, sprains, concussion, infection, a chill, hypothermia, heatstroke, and the grave wound a companion gets when they're knocked down. Each can make you bleed (`bleed`, health per hour), drain energy (`drain`), hit weaker (`fight`), lower your top stamina (`stamina`), stop you running and fleeing (`noRun`), slow the whole party (`slow`), make fight actions go astray (`fumble`), or stop a companion fighting at all (`noFight`).

Treating one means doing each of its `treat` steps, from `treatments`: clean (spirits, yarrow or two drinks of water), bandage, stitch (needs a sewing kit, not used up), splint (a splint, or split wood or firewood with a bandage), bring a fever down (willow bark), and rest (a night's sleep does it). Once every step is done it heals over `heal` hours (First aid, a life skill trained by treating, speeds that up). Some go by themselves (`selfHeal`); some turn into something worse if a step isn't done in time (`worsen`, `worsenAfter`, `worsenUnless`: an unclean bite becomes an infection); a grave wound kills unless it's bandaged within `deadline` hours. Enemy moves list the `wounds` a hit can leave, with % chances (more for a heavy blow, half when you defend). Treat wounds from the Survival menu (S).

Supplies: Ida gives two bandages and spirits with the wolf job, Bess sells bandages, spirits, willow bark and a sewing kit, and yarrow and a willow grow in the hills (they grow back each day: flags ending `_today` clear when you sleep).

## Weather

Each area has a `climate` (`climates` in `data.json`: day and night temperatures and how likely each weather is). The weather (`weathers`: clear, overcast, drizzle, rain, storm, fog, a warm spell) changes every few hours, worked out from the seed and the clock, so it's the same on every load. Rain soaks you (`wet`); being wet makes the cold far worse; clothes add `warmth` (on gear, and on each companion). Too cold for long and you get cold (tire faster, slower to get your breath back), then hypothermia; too hot and you get thirsty, then heatstroke. A roof, a fire or a bed puts it right: a bed resets everything, a camp fire warms and dries you, sleeping in the open soaks and chills you and can give you a chill. Dev mode can force the weather and hand out wounds.

## Death in the party

Companions can die, and they're gone for good. A huge blow can kill them outright; knocked down, they get a grave wound and bleed out unless bandaged in time; starving, thirst, cold and untreated wounds kill them out in the world. The dead are listed under Survival, and the flag `dead_<id>` is set (Ida has her own line if Rook doesn't come back).

## Enemies and combat

Each enemy has a rank, health, `exp`, an optional `boss` flag and `size` (how big it's drawn), an attack range, a `speed` (compared with your Agility to see who acts first) and `moves`. Each move has a `tell`, which is shown before the enemy acts, a `power` that multiplies its attack, and a `weight` for how often it's picked. A move with `"guard": true` means the enemy holds back and your hits do half damage that round.

Attacking and defending use the timing bar. A perfect press is a critical hit (or a parry when defending), a good press is a normal hit, and a mistimed one glances off.

When you change the story, run the tests. They check every link, item and scene name, and play the whole slice through. Unity also prints any broken links as warnings in the Console when you press Play.
