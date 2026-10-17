# Loran: how the game is put together

Press **Play** in any scene. `Unity/GameBoot.cs` builds the whole game at startup, so there are no scenes, prefabs or objects to set up.

## Folders

| Folder | What's in it |
|---|---|
| `Core/` | The rules: time, needs, corruption, stats, combat, saving, and the engine that runs the story. Plain C# with no Unity code, so it can be tested outside Unity. |
| `Unity/` | The screen: HUD, story text, choices, side panel, New Game and character creation. Built with UI Toolkit from code. |
| `Resources/Story/` | The story, one JSON file per area. **This is where the writing lives.** |
| `Resources/Data/data.json` | Items, enemies, classes, backgrounds, gear, spells and journal entries. |
| `Resources/UI/Game.uss` | Colours, sizes and fonts for the screen. |
| `../../Tools/CoreTests/` | Automated playthroughs. Run `dotnet run --project Tools/CoreTests` from the repo root. |

All the balancing numbers (how fast hunger drains, how fast corruption grows, and so on) are at the top of `Core/Rules.cs`.

## Dev mode

Press **F1** (or the key left of 1) in game, or **Dev mode** on the title screen. Quick starts drop a ready-made test character into any part of the game (the forge, either fight, the road, the class stone and more). You can also jump to any scene, refill everything, give coins, items and spells, change stats, set the time, win a fight, or make any gear at any rank. A game that has used dev mode is never autosaved, so your real save is safe. The quick starts live in `Core/Engine.Dev.cs`.

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

Put `!` in front of any condition to flip it.

`flag:name`, `item:bread`, `spell:kindle`, `class` (has any class), `class:mage`, `gear`, `gear:knife`, `mode:basic`, `mode:perma`, `background:runner`, `gender:female`, `night`, and comparisons such as `coins>=3`, `water<4`, `rep:skelmed>=10`, `energy<20`, `stat:str>=5`, `item:bread>=2`, `day>=3`.

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
| `spell:draw_water`, `gear:knife`, `class:mage` | Learn a spell, give and equip gear at its own rank (usually F), take a class |
| `forge:knife:D:green_gem` | Put a piece on the anvil and play the forge game. Quality decides the rank (a material gives +1 rank, quality 75+ gives +1, below 30 it's crude), never above the rank given. After forging, the player goes where the choice's `go` points |
| `craft:knife:E:green_gem` | Make gear at a rank (F to S). Above F it rolls traits from the material's list (or from all traits if no material) |
| `sleep:safe` / `sleep:rough` / `sleep:camp` | Sleep until morning. Sleeping saves: in basic mode you wake here if you die |
| `combat:dog:winScene:fleeScene` | Start a fight |
| `check:agi:40:passScene:failScene` | A stat check: 40% plus 8% per point of the stat |
| `clock:1:17:30`, `hud:on`, `save` | Set the time, show the HUD, take a save |

## Gear ranks

Gear in `data.json` is the base: name, slot (`weapon`, `offhand`, `body`, `hands`, `trinket`), damage, guard, stamina, and `part` (where a gem sits). Every piece the player gets is its own copy with a rank: F, E, D, C, B, A or S. A higher rank multiplies the numbers and adds traits: none at F, 1 at E and D, 2 at C and B, 3 at A and S. Traits are listed under `traits`, and `materials` say which traits a gem or metal can give (list one more than once to make it likelier).

## Roads and travel

Roads live under `routes` in `data.json`. A road has a list of `stops`, each with a map position (`x`, `y` in % of the map), a `desc`, optional `choices` (things to do there, written like scene choices; leave `go` empty to stay on the road) and `forage` items. Each stop also describes the leg to the next stop: how many `steps` it takes, how many `minutes` at a steady pace, the `leg` text, and an optional fixed story `beat` scene (with `beatWhen`, usually a flag so it only happens once).

`events` are scenes that can turn up between stops (`eventChance` % per step). Their `kind` changes the odds: careful walkers find more `find` events and walk into fewer `danger` ones, hurrying does the opposite, and night doubles danger. `once` events happen only once. An event scene sends the player back to the road with `"go": "travel"`.

`start` and `end` say where walking off each end of the road takes you (the first one whose `when` holds). Start a walk with the effect `travel:start:coast_road` (from the start) or `travel:start:coast_road:end` (from the far end).

## Enemies and combat

Each enemy has a rank, health, an attack range, a `speed` (compared with your Agility to see who acts first) and `moves`. Each move has a `tell`, which is shown before the enemy acts, a `power` that multiplies its attack, and a `weight` for how often it's picked. A move with `"guard": true` means the enemy holds back and your hits do half damage that round.

Attacking and defending use the timing bar. A perfect press is a critical hit (or a parry when defending), a good press is a normal hit, and a mistimed one glances off.

When you change the story, run the tests. They check every link, item and scene name, and play the whole slice through. Unity also prints any broken links as warnings in the Console when you press Play.
