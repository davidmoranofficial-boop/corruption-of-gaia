# Corruption of Gaia

A hand-written text adventure RPG with a HUD, built in Unity 6.3. The game code lives in `Assets/Game` (see `Assets/Game/README.md` for how the story files and systems work). `Tools/CoreTests` plays the game's rules outside Unity: `dotnet run --project Tools/CoreTests`.

## Updates arrive by themselves

`Assets/Game/Editor/LoranAutoUpdate.cs` checks this repository whenever you click back into the Unity editor. If there's anything new on `main`, it copies `Assets/Game` and `Tools` into the project and Unity reloads them. It waits while you're in Play mode. Use the **Corruption of Gaia** menu in Unity to check by hand or turn it off. It needs `git` installed.

Don't edit files inside `Assets/Game` by hand in the Unity project: the next update replaces them. Change them here instead.
