# Corruption of Gaia

A hand-written text adventure RPG with a HUD, built in Unity 6.3. The game code lives in `Assets/Game` (see `Assets/Game/README.md` for how the story files and systems work). `Tools/CoreTests` plays the game's rules outside Unity: `dotnet run --project Tools/CoreTests`.

## Updates arrive by themselves

`Assets/Game/Editor/LoranAutoUpdate.cs` checks this repository whenever you click back into the Unity editor. If there's anything new on `main`, it copies `Assets/Game` and `Tools` into the project and Unity reloads them. It waits while you're in Play mode. Use the **Corruption of Gaia** menu in Unity to check by hand or turn it off. It needs `git` installed.

Don't edit files inside `Assets/Game` by hand in the Unity project: the next update replaces them. Change them here instead.

## Art

All art is pixel art drawn as text grids in `Tools/Art/src` (one letter per pixel, each letter a colour from `Tools/Art/palette.py`).
`python3 Tools/Art/build.py` turns them into the PNGs in `Assets/Game/Resources/Sprites`: character bodies for each skin tone,
hair for each colour, a layer and an icon for every piece of gear, and enemies. Characters are paper dolls: the body plus one
layer per worn item, all on the same 32 x 48 frame. `Assets/Game/Editor/LoranSpriteImport.cs` keeps the pixels sharp in Unity.
