using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Loran.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

// Plays the story outside Unity: checks the content for broken links, plays the
// whole slice start to finish, and throws thousands of random choices at it.
// Run from the repo root:  dotnet run --project Tools/CoreTests

static class Program
{
    static string Root;
    static int failures;

    static int Main()
    {
        Root = FindRoot();
        Test("content has no broken links", ContentIsValid);
        Test("full playthrough reaches the end", FullPlaythrough);
        Test("save and load round trip", SaveLoad);
        Test("dying in basic mode wakes you at your last sleep", BasicDeath);
        Test("dying in permadeath ends the game", PermaDeath);
        Test("running out of energy makes you collapse", Collapse);
        Test("running out of energy mid-fight kills you", CombatExhaustion);
        if (Environment.GetEnvironmentVariable("QUICK") != "1") Test("dog fight never breaks, across 500 seeds", DogFights);
        Test("timing: perfect hits beat mistimed ones, parries block", Timing);
        Test("gear ranks, traits and slots", GearRanks);
        Test("equipment slots: clothes, rings, two hands", Slots);
        Test("walking the coast road", Travel);
        Test("forging: how well you play sets the rank", Forging);
        Test("dev mode quick starts and cheats", DevMode);
        if (Environment.GetEnvironmentVariable("QUICK") != "1") Test("random play never gets stuck, across 300 seeds", RandomPlay);
        Console.WriteLine(failures == 0 ? "\nAll tests passed." : "\n" + failures + " test(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ tests

    static void ContentIsValid()
    {
        var problems = NewEngine().Validate();
        foreach (var p in problems) Console.WriteLine("    " + p);
        Assert(problems.Count == 0, problems.Count + " problem(s) in the story files");
    }

    static void FullPlaythrough()
    {
        var e = NewEngine();
        e.NewGame("basic");
        Pick(e, "Close your eyes");
        Pick(e, "Where am I");
        Assert(e.Paragraphs().Count == 0 && e.S.result.Contains("Between"), "after a dialogue choice only the answer should show");
        Pick(e, "What do you want");
        Pick(e, "Why not send");
        Pick(e, "Do I have a choice");
        Pick(e, "Decide who");
        Assert(e.S.scene == "creation", "should be at creation");
        Assert(e.CreateCharacter("", "female", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 }) != null, "empty name should be refused");
        Assert(e.CreateCharacter("Ash", "female", "Lean", "Red", "runner", new[] { 2, 2, 1, 1, 1 }) != null, "too many points should be refused");
        Assert(e.CreateCharacter("Ash", "female", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 }) == null, "valid character refused");
        Assert(e.S.stats[Stat.Agi] == 5, "runner should get +1 agility");
        Assert(e.CreateCharacter("Ash", "", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 }) != null, "sex must be chosen");
        Assert(e.S.stats.Length == 5 && e.S.stats[Stat.Cha] == 4, "charisma should exist and take points");
        Assert(e.Fill("{they} nods at {them}") == "she nods at her", "pronouns should follow the chosen sex");
        Pick(e, "Go");
        Pick(e, "Help the sailors");
        Pick(e, "…");
        Assert(e.S.scene == "shack" && e.S.hud, "should wake in the shack with the HUD on");
        Assert(Math.Abs(e.S.corruption - 5) < 0.01, "corruption should start at 5%");
        Assert(e.S.lastSleep != "", "waking in the shack should count as a save");
        Pick(e, "Eat the stew");
        Pick(e, "What should I do now");
        Assert(e.S.Count("bread") == 2 && e.S.water == 4, "Yenson should give supplies");
        Pick(e, "Sleep by the fire");
        Assert(e.S.Day == 2 && e.S.Hour == 6, "should wake on day 2 at 06:00, was day " + e.S.Day + " " + Rules.Clock(e.S));
        Pick(e, "Set off");
        Assert(e.S.scene == "travel" && AtSpot(e, "shack"), "setting off should put you outside the shack");
        e.S.stepsSinceFight = -1000;   // this run is about the story; random fights are tested on their own
        WalkUntil(e, Gate(e), () => e.S.scene == "dog");
        Pick(e, "Throw it some bread");
        Pick(e, "Walk on");
        WalkUntil(e, Gate(e), () => e.S.scene == "skelmed_arrive");
        Pick(e, "village square");

        Pick(e, "hunters' lodge");
        Pick(e, "Split and carry firewood");
        Pick(e, "Haul water");
        Pick(e, "snare line");
        Pick(e, "Tell Ida");
        Pick(e, "leave the coin");
        Assert(e.S.Rep("skelmed") == 10, "refusing payment should give +10 rep, got " + e.S.Rep("skelmed"));
        Pick(e, "Back to the square");

        foreach (var food in e.S.items.Where(i => e.data.Item(i.id).food).Select(i => i.id).ToList()) e.Eat(food);
        e.Drink();
        Pick(e, "Garran's forge");
        Pick(e, "quarry with Garran");
        Pick(e, "Swing the pick");
        Pick(e, "spoil heap");
        Pick(e, "Show it to Garran");
        Pick(e, "Carry the stone");
        Pick(e, "first gear");
        Pick(e, "with the gem in the pommel");
        Assert(e.S.scene == "forge" && e.Current.title.Contains("knife"), "making gear should go to the anvil, was " + e.S.scene + " / " + e.Current.title);
        Pick(e, "Work the forge");
        Assert(e.S.scene == "smith" && e.S.result.Contains("Quality"), "after forging, back to Garran with the quality, was " + e.S.scene);
        var knife = e.S.InSlot("weapon");
        Assert(knife != null && knife.id == "knife" && knife.rank == "E" && knife.traits.Count == 1, "should have an E-rank knife with one trait");
        Assert(e.S.gearItems.Any(g => g.id == "gutting_knife" && !g.equipped), "Yenson's knife should go back in the pack");
        Pick(e, "Back to the square");

        Pick(e, "The guild post");
        Pick(e, "class stone");
        Assert(!e.Choices().First(c => c.def.label.Contains("Adventurer")).enabled, "class stone should be locked before Orlen");
        Pick(e, "Step away");
        Pick(e, "Back to the square");

        Pick(e, "The Net & Anchor");
        Pick(e, "Sleep rough");
        Pick(e, "Back to the square");
        Pick(e, "Orlen's cottage");
        Pick(e, "his years in Zaron");
        Pick(e, "teach you magic");
        Assert(e.S.spells.Contains("draw_water") && e.S.spells.Contains("kindle"), "should learn both spells");
        Assert(e.S.scene == "mage_fire", "the fire should break out, was " + e.S.scene + " energy " + e.S.energy + " result " + e.S.result);
        Pick(e, "cast Draw Water");
        Pick(e, "Go back to the square");

        Pick(e, "The guild post");
        Pick(e, "class stone");
        Pick(e, "become a Mage");
        Assert(e.S.playerClass == "mage", "should be a mage");
        Pick(e, "Go back out");
        Pick(e, "Talk to Ida");
        Pick(e, "nearest town");
        Assert(e.S.scene == "end_slice", "should reach the end of the slice");
        Assert(e.S.corruption > 5, "corruption should have grown with time");
        Console.WriteLine("    finished on day " + e.S.Day + " at " + Rules.Clock(e.S) + ", corruption " + e.S.corruption.ToString("0.00") +
                          "%, rep " + e.S.Rep("skelmed") + ", stats " + string.Join("/", e.S.stats) + ", journal: " + string.Join(" | ", e.Journal()));
    }

    static void SaveLoad()
    {
        var oldSave = new GameState { stats = new[] { 4, 4, 4, 4 }, xp = new float[] { 1, 1, 1, 1 } };
        var up = NewEngine();
        up.LoadState(oldSave);
        Assert(up.S.stats.Length == 5 && up.S.stats[Stat.Cha] == 3 && up.S.xp.Length == 5, "old saves should gain Charisma");
        var onOldRoad = NewEngine();
        onOldRoad.LoadState(new GameState { scene = "road2", hud = true });
        Assert(onOldRoad.S.scene == "travel" && AtSpot(onOldRoad, "stream"), "saves on the old road scenes should land by the stream");
        var midRoad = NewEngine();
        midRoad.LoadState(new GameState { scene = "travel", hud = true, travelRoute = "coast_road", travelAt = 3 });
        Assert(midRoad.S.scene == "travel" && midRoad.Area != null && midRoad.S.travelRoute == "", "saves walking the old road should land in the world");

        var e = NewEngine();
        ToShack(e);
        string a = Json.Write(e.S);
        var back = Json.Read<GameState>(a);
        Assert(Json.Write(back) == a, "state should survive a save and load unchanged");
    }

    static void BasicDeath()
    {
        var e = NewEngine();
        ToShack(e);
        Pick(e, "What should I do now");
        Pick(e, "Sleep by the fire");
        int savedTime = e.S.minutes;
        Pick(e, "Set off");
        UseThing(e, "pools");
        Pick(e, "Search the tide pools");
        e.S.hp = 0;
        e.GoTo(e.S.scene);
        Assert(e.S.scene == "death", "hp 0 should kill you");
        Pick(e, "Wake at your last sleep");
        Assert(e.S.scene == "shack" && e.S.minutes == savedTime, "should wake in the shack at the time of the last sleep");
        Assert(!e.S.Has("pools"), "things after the last sleep should be undone");
        Assert(e.S.result.Contains("You wake with a gasp"), "should explain what happened");
        Assert(e.Paragraphs().Count > 0, "after waking the scene should be described again");
    }

    static void PermaDeath()
    {
        var e = NewEngine();
        e.NewGame("perma");
        Walk(e, "Close", "What do you want", "Do I have", "Decide");
        e.CreateCharacter("Rook", "male", "Lean", "Red", "desk", new[] { 1, 1, 1, 1, 1 });
        Walk(e, "Go", "Hold on", "…");
        e.S.hp = 0;
        e.GoTo(e.S.scene);
        Assert(e.S.scene == "death", "should be dead");
        Assert(e.Choices().All(c => !c.def.label.Contains("last sleep")), "permadeath must not offer a revive");
        Pick(e, "Start a new game");
        Assert(e.S.scene == "title", "should go back to the title");
    }

    static void Collapse()
    {
        var e = NewEngine();
        ToShack(e);
        Pick(e, "What should I do now");
        Pick(e, "Set off");
        e.S.energy = 0.05f;
        e.S.coins = 6;
        StepToward(e, Gate(e));
        Assert(e.S.scene == "collapse", "should collapse, was " + e.S.scene);
        Assert(e.S.coins == 3, "collapsing on the road should cost half your coins, had " + e.S.coins + " result " + e.S.result);
        Pick(e, "Get up");
        Assert(e.S.scene != "collapse" && e.S.energy > 0, "should get up where you fell, was " + e.S.scene);
    }

    static void CombatExhaustion()
    {
        var e = NewEngine();
        ToShack(e);
        Walk(e, "What should I do now", "Set off");
        e.S.stepsSinceFight = -1000;   // no random fights on the way
        WalkUntil(e, Gate(e), () => e.S.scene == "dog");
        Pick(e, "Fight it");
        e.S.energy = 0.5f;
        Pick(e, "Defend");
        Assert(e.S.scene == "death" && e.S.deathCause.Contains("energy"), "should die of exhaustion mid-fight");
    }

    static void DogFights()
    {
        int wins = 0, deaths = 0, fled = 0;
        float rounds = 0, lost = 0;
        for (int seed = 0; seed < 500; seed++)
        {
            var e = NewEngine();
            ToShack(e, seed);
            Walk(e, "What should I do now", "Set off");
            e.S.stepsSinceFight = -1000;
            WalkUntil(e, Gate(e), () => e.S.scene == "dog");
            Pick(e, "Fight it");
            float hp0 = e.S.hp;
            for (int turn = 0; turn < 60 && e.S.inCombat; turn++)
            {
                var opts = e.Choices().Where(c => c.enabled).ToList();
                var pick = opts.FirstOrDefault(c => c.def.label == "Attack") ?? opts.First(c => c.def.label.StartsWith("Defend"));
                e.Act(pick.def, (seed + turn) % 3);   // a mix of mistimed, good and perfect presses
            }
            rounds += e.S.round; lost += hp0 - e.S.hp;
            if (e.S.scene == "dog_won") wins++;
            else if (e.S.scene == "death") deaths++;
            else if (e.S.scene == "dog_fled") fled++;
            else throw new Exception("fight ended in " + e.S.scene);
        }
        Console.WriteLine("    average rounds " + (rounds / 500f).ToString("0.0") + ", average health lost " + (lost / 500f).ToString("0.0"));
        Console.WriteLine("    attacking every turn: " + wins + " wins, " + deaths + " deaths out of 500");
        Assert(wins > 450, "the first fight should be low danger");
    }

    static Engine AtDog(int seed)
    {
        var e = NewEngine();
        ToShack(e, seed);
        Walk(e, "What should I do now", "Set off");
        e.S.stepsSinceFight = -1000;   // no random fights on the way
        WalkUntil(e, Gate(e), () => e.S.scene == "dog");
        Pick(e, "Fight it");
        return e;
    }

    static void Timing()
    {
        Assert(AtDog(3).Intent != null && AtDog(3).Paragraphs().Count == 2, "the dog should show what it's about to do");
        float perfect = 0, miss = 0;
        for (int seed = 0; seed < 50; seed++)
        {
            var a = AtDog(seed);
            float before = a.S.enemyHp;
            a.Act(a.Choices().First(c => c.def.label == "Attack").def, Engine.TimingPerfect);
            perfect += before - Math.Max(0, a.S.enemyHp);
            Assert(a.Events.Any(ev => ev.target == "enemy" && (ev.kind == "crit" || ev.kind == "hit")), "a hit should be recorded for the screen");

            var b = AtDog(seed);
            before = b.S.enemyHp;
            b.Act(b.Choices().First(c => c.def.label == "Attack").def, Engine.TimingMiss);
            miss += before - Math.Max(0, b.S.enemyHp);
        }
        Assert(perfect > miss * 2.5f, "perfect hits should do far more than mistimed ones (" + perfect + " vs " + miss + ")");

        int parried = 0;
        for (int seed = 0; seed < 50; seed++)
        {
            var e = AtDog(seed);
            if (e.Intent.guard) continue;
            float hp = e.S.hp;
            e.Act(e.Choices().First(c => c.def.label == "Defend").def, Engine.TimingPerfect);
            Assert(e.S.hp >= hp, "a perfect parry should take no damage");
            parried++;
        }
        Assert(parried > 10, "should have tested some parries");
    }

    static void GearRanks()
    {
        var e = NewEngine();
        ToShack(e);
        Pick(e, "What should I do now");
        var gk = e.S.InSlot("weapon");
        Assert(gk != null && gk.rank == "F" && gk.traits.Count == 0, "basic gear is F rank with no traits");

        var a = e.MakeGear("knife", "C", "green_gem");
        Assert(a.traits.Count == 2 && a.traits.Distinct().Count() == 2, "C rank should roll two different traits");
        Assert(a.damage > 4 && a.equipped && !gk.equipped, "a better rank should hit harder and replace the old weapon");
        var s = e.MakeGear("knife", "S", null);
        Assert(s.traits.Count == 3, "S rank should have three traits");
        Assert(e.Equip(gk.uid) && gk.equipped && !s.equipped, "equipping should swap the slot");

        int before = Rules.MaxStamina(e.S, e.data);
        e.MakeGear("gloves", "F", null);
        Assert(Rules.MaxStamina(e.S, e.data) == before + 10, "gloves should add stamina");

        var old = new GameState { gear = "buckler_gem", stats = new[] { 3, 3, 3, 3, 3 } };
        var up = NewEngine();
        up.LoadState(old);
        Assert(up.S.gear == "" && up.S.InSlot("offhand")?.rank == "E", "old saves should turn their gear into ranked gear");

        string json = Json.Write(e.S);
        Assert(Json.Write(Json.Read<GameState>(json)) == json, "gear should survive a save and load");
    }

    static void Travel()
    {
        var e = NewEngine();
        ToShack(e);
        Walk(e, "What should I do now", "Sleep by the fire", "Set off");
        var a = e.Area;
        Assert(a != null && AtSpot(e, "shack"), "should be outside the shack");

        // Walking and running
        e.Face("down");
        var walk = e.StepChoice(false);
        var run = e.StepChoice(true);
        Assert(walk != null && run != null && run.time < walk.time && run.stamina > walk.stamina, "running should be quicker but tiring");
        e.Face("up");
        Assert(e.StepChoice() == null, "you can't walk into the shack");
        Assert(e.UseChoice() != null, "you can knock at the shack door");
        e.Choose(e.UseChoice());
        Assert(e.S.scene == "shack_visit", "the door should open Yenson's shack");
        Pick(e, "Head out");
        Assert(e.S.scene == "travel" && AtSpot(e, "shack"), "heading out should put you back outside");

        // The sign by the road
        UseThing(e, "sign_shack");
        Assert(e.S.scene == "travel" && e.S.result.Contains("Skelmed"), "reading a sign should show what it says");

        // The coast road to Skelmed: a couple of hours, past the wolf
        int t0 = e.S.minutes;
        WalkUntil(e, Gate(e), () => e.S.scene == "dog");
        Pick(e, "Throw it some bread");
        Pick(e, "Walk on");
        Assert(e.ObjectAt(a.objects.First(o => o.id == "wolf").x, a.objects.First(o => o.id == "wolf").y) == null, "the wolf should be gone from the road");
        WalkUntil(e, Gate(e), () => e.S.scene == "skelmed_arrive");
        Assert(e.S.minutes - t0 >= 100, "the walk should take a couple of hours, took " + (e.S.minutes - t0) + " min");

        // Back out, and down to the stream
        Pick(e, "village square");
        Pick(e, "Leave the village");
        Assert(e.S.scene == "travel" && AtSpot(e, "skelmed"), "leaving should put you outside the gate");
        var st = a.spots.First(p => p.id == "stream");
        WalkUntil(e, (st.x, st.y), () => e.S.ax == st.x && e.S.ay == st.y);
        e.Face("up");
        bool stream = false;
        foreach (var f in Engine.Facings)
        {
            e.Face(f);
            var u = e.UseChoice();
            if (u != null && u.effects[0].StartsWith("travel:usetile")) { e.Choose(u); stream = true; break; }
        }
        Assert(stream && e.S.scene == "stream_drink", "you should be able to drink from the stream");
        Pick(e, "Drink");
        Assert(e.S.water == 4, "the waterskin should be full");

        // Camping saves the game
        e.S.lastSleep = "";
        e.S.result = "";
        e.Choose(e.Choices().First(c => c.def.label.StartsWith("Make camp")).def);
        Assert(e.S.lastSleep != "" && e.S.Hour == 6, "camping should sleep until morning and save");

        // Everything you can use opens a scene that leads back out
        foreach (var o in a.objects.Where(o => !string.IsNullOrEmpty(o.scene) && o.id != "wolf"))
        {
            var v = NewEngine();
            v.DevStart("road");
            v.S.coins = 20;
            v.S.flags.Add("dog_met");
            v.S.stepsSinceFight = -1000;
            UseThing(v, o.id);
            Assert(v.S.scene != "travel" || v.S.inCombat, "using " + o.id + " should open its scene");
            for (int i = 0; i < 30 && (v.S.scene != "travel" || v.S.inCombat) && v.S.scene != "death"; i++)
            {
                if (v.S.inCombat) { v.DevWinFight(); continue; }
                v.Choose(v.Choices().Where(c => c.enabled).Last().def);
            }
            Assert(v.S.scene == "travel", "after " + o.id + " you should be back on the road, was " + v.S.scene);
        }

        // Fights come at random as you walk, never too close together
        var fights = new Dictionary<string, int>();
        var seen = new HashSet<string>();
        int closest = 999;
        for (int seed = 0; seed < 60; seed++)
        {
            var w = NewEngine();
            ToShack(w, seed);
            Walk(w, "What should I do now", "Sleep by the fire", "Set off");
            int last = -1, steps = 0;
            var goal = Gate(w);
            for (int i = 0; i < 400 && w.S.scene != "skelmed_arrive" && w.S.scene != "death"; i++)
            {
                if (w.S.inCombat) { w.DevWinFight(); continue; }
                if (w.S.scene.StartsWith("ev_")) seen.Add(w.S.scene);
                if (w.S.scene == "dog") { Pick(w, "Throw it some bread"); continue; }
                bool walking = w.S.scene == "travel";
                if (walking) steps++;
                StepToward(w, goal);
                if (walking && w.S.inCombat)
                {
                    // a random fight, straight off a step
                    fights[w.S.enemy] = fights.TryGetValue(w.S.enemy, out var n) ? n + 1 : 1;
                    if (last >= 0) closest = Math.Min(closest, steps - last);
                    last = steps;
                }
            }
        }
        Console.WriteLine("    fights: " + string.Join(", ", fights.Select(f => f.Key + " " + f.Value)) + "; closest two fights " + closest + " steps apart");
        Console.WriteLine("    events seen: " + string.Join(", ", seen.OrderBy(x => x)));
        Assert(fights.ContainsKey("dog") && fights.ContainsKey("crab") || fights.Values.Sum() >= 20, "random fights should happen on the walk");
        Assert(closest > a.fightGap, "fights should never come close together");
        Assert(seen.Count >= 4, "other things should turn up on the road too");
    }

    static void Forging()
    {
        GearItem ForgeOnce(string effect, float h, float m, float q)
        {
            var e = NewEngine();
            ToShack(e);
            e.S.scene = "smith";
            e.S.coins = 10;
            e.Choose(new ChoiceDef { label = "test", effects = { effect }, go = "smith" });
            Assert(e.S.scene == "forge", "should be at the anvil");
            e.FinishForge(h, m, q);
            Assert(e.S.scene == "smith", "should return to the smith");
            return e.S.InSlot(e.data.Gear(e.S.gearItems.Last().id).slot);
        }
        var great = ForgeOnce("forge:knife:D:green_gem", 1, 1, 1);
        Assert(great.rank == "D" && great.traits.Count == 1, "great forging with a gem should lift it to D, got " + great.rank);
        var plain = ForgeOnce("forge:knife:E", 0.6f, 0.6f, 0.6f);
        Assert(plain.rank == "F" && plain.traits.Count == 0 && !plain.name.StartsWith("Crude"), "middling forging of plain iron stays F");
        var good = ForgeOnce("forge:knife:E", 1, 0.9f, 1);
        Assert(good.rank == "E" && good.traits.Count == 1, "fine work lifts plain iron to E with a trait");
        var bad = ForgeOnce("forge:knife:E", 0, 0.1f, 0);
        Assert(bad.rank == "F" && bad.name.StartsWith("Crude") && bad.damage < 4, "botched forging makes a crude, weaker piece");
        var capped = ForgeOnce("forge:knife:F:green_gem", 1, 1, 1);
        Assert(capped.rank == "F", "the max rank should cap it");
    }

    static void DevMode()
    {
        var expect = new Dictionary<string, string>
        {
            { "village", "village" }, { "road", "travel" }, { "forge_first", "smith_craft" }, { "forge_iron", "smith_forge" },
            { "dog", "dog" }, { "crab", "ev_crab" }, { "stone", "stone" }, { "shack", "shack" },
        };
        foreach (var (id, _) in Engine.DevStarts)
        {
            var e = NewEngine();
            e.DevStart(id);
            Assert(e.IsDev && e.S.scene == expect[id], id + " should land in " + expect[id] + ", was " + e.S.scene);
            Assert(e.Choices().Any(c => c.enabled), id + " should have something to do");
            if (id == "dog" || id == "crab") Assert(e.S.inCombat && e.S.enemy == id, id + " should start the fight");
            if (id == "stone") Assert(e.Choices().Count(c => c.enabled) >= 3, "all three classes should be open");
            if (id == "forge_first") { Pick(e, "with the gem"); Assert(e.S.scene == "forge", "should be at the anvil"); }
            if (id == "forge_iron") { Pick(e, "green gem set"); Assert(e.S.scene == "forge", "should be at the anvil"); e.FinishForge(1, 1, 1); Assert(e.S.scene == "smith", "back to Garran"); }
            if (id == "dog") { e.DevWinFight(); Pick(e, "Walk on"); Assert(e.S.scene == "travel", "winning should go back to the road"); }
        }
        var d = NewEngine();
        d.DevStart("village");
        d.S.hp = 5; d.DevRefill();
        Assert(d.S.hp == Rules.MaxHp(d.S), "refill should heal");
        var g = d.DevGear("knife", "S");
        Assert(g.rank == "S" && g.traits.Count == 3 && g.equipped, "should make S gear");
        d.DevTime(21);
        Assert(d.S.Hour == 21 && d.Check("night"), "should be night");
        foreach (var id in d.AllScenes.ToList()) { var j = NewEngine(); j.DevStart("village"); j.Jump(id); Assert(j.S.scene != "", "jump to " + id); }
    }

    static bool AtSpot(Engine e, string id)
    {
        var p = e.data.Spot(id).spot;
        return e.S.ax == p.x && e.S.ay == p.y;
    }

    /// <summary>The tile just inside Skelmed's gate.</summary>
    static (int x, int y) Gate(Engine e)
    {
        var z = e.Area.zones.First(z => z.enter.Count > 0);
        return ((z.x0 + z.x1) / 2, z.y1);
    }

    /// <summary>
    /// One step towards a tile: in the world step along the shortest path, in a fight attack,
    /// at an event take its last (move on) option.
    /// </summary>
    static void StepToward(Engine e, (int x, int y) goal)
    {
        if (e.S.inCombat)
        {
            var a = e.Choices().First(c => c.def.label == "Attack");
            e.Act(a.enabled ? a.def : e.Choices().First(c => c.def.label == "Defend").def, Engine.TimingGood);
        }
        else if (e.S.scene == "travel")
        {
            var path = e.PathTo(goal.x, goal.y);
            if (path == null || path.Count == 0) throw new Exception("no way to " + goal + " from " + e.S.ax + "," + e.S.ay);
            e.Face(path[0]);
            var step = e.StepChoice(e.S.stamina > 50);
            e.Choose(step);
        }
        else if (e.S.scene.StartsWith("ev_") || e.S.scene.StartsWith("dog_") || e.S.scene == "collapse")
            e.Choose(e.Choices().Where(c => c.enabled).Last().def);
        else throw new Exception("walking stopped in " + e.S.scene + ": " + e.S.result);
    }

    static void WalkUntil(Engine e, (int x, int y) goal, Func<bool> done)
    {
        for (int i = 0; i < 400 && !done(); i++) StepToward(e, goal);
        if (!done()) throw new Exception("never got there; stuck in " + e.S.scene + " at " + e.S.ax + "," + e.S.ay);
    }

    /// <summary>Walks up to a thing in the world, faces it and uses it.</summary>
    static void UseThing(Engine e, string id)
    {
        var o = e.Area.objects.First(x => x.id == id);
        (int, int)? best = null;
        foreach (var f in Engine.Facings)
        {
            var (dx, dy) = Engine.Dir(f);
            int x = o.x - dx, y = o.y - dy;
            if (!e.Blocked(x, y) || (e.S.ax == x && e.S.ay == y)) { best = (x, y); break; }
        }
        if (best == null) throw new Exception("can't get next to " + id);
        var goal = best.Value;
        WalkUntil(e, goal, () => e.S.scene == "travel" && !e.S.inCombat && e.S.ax == goal.Item1 && e.S.ay == goal.Item2);
        e.Face(o.x < e.S.ax ? "left" : o.x > e.S.ax ? "right" : o.y < e.S.ay ? "up" : "down");
        var use = e.UseChoice() ?? throw new Exception("nothing to use facing " + id);
        e.Choose(use);
    }

    static void RandomPlay()
    {
        var reached = new HashSet<string>();
        int endings = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            var e = NewEngine();
            var rng = new Random(seed);
            e.NewGame(seed % 2 == 0 ? "basic" : "perma");
            for (int step = 0; step < 600; step++)
            {
                reached.Add(e.S.scene);
                if (e.S.scene == "title") { e.NewGame("basic"); continue; }
                if (e.S.scene == "creation" && !e.S.Has("created"))
                    e.CreateCharacter("Test", "other", "Lean", "Red", "nights", new[] { 5, 0, 0, 0, 0 });
                if (e.S.scene == "end_slice") { endings++; break; }

                var opts = e.Choices().Where(c => c.enabled).ToList();
                if (opts.Count == 0)
                {
                    // Only allowed when side actions can fix it (out of stamina)
                    Assert(e.FreeToAct, "stuck with no choices in " + e.S.scene);
                    if (!e.CatchBreath()) throw new Exception("stuck in " + e.S.scene);
                    continue;
                }
                int r = rng.Next(10);
                if (r == 0 && e.FreeToAct) { e.CatchBreath(); continue; }
                if (r == 1 && e.FreeToAct)
                {
                    var food = e.S.items.FirstOrDefault(i => e.data.Item(i.id).food);
                    if (food != null) { e.Eat(food.id); continue; }
                }
                if (r == 2 && e.FreeToAct) { e.Drink(); e.DrawWater(); continue; }
                if (e.Travelling && r >= 3)
                {
                    // Walk: mostly towards Skelmed, sometimes towards something to use, sometimes anywhere
                    (int x, int y) goal = Gate(e);
                    if (r == 8) { var o = e.Area.objects[rng.Next(e.Area.objects.Count)]; goal = (o.x, o.y + 1); }
                    if (r == 9) { e.Face(Engine.Facings[rng.Next(4)]); var u = e.UseChoice(); if (u != null) { e.Choose(u); continue; } }
                    var path = e.PathTo(goal.x, goal.y);
                    if (path != null && path.Count > 0)
                    {
                        e.Face(path[0]);
                        var walk = e.StepChoice(false);
                        if (walk != null) { e.Choose(walk); continue; }
                    }
                }
                e.Choose(opts[rng.Next(opts.Count)].def);
                Rules.Clamp(e.S, e.data);
                Assert(!float.IsNaN(e.S.hp) && e.S.corruption >= 0 && e.S.corruption <= 100, "numbers out of range in " + e.S.scene);
            }
        }
        var never = NewEngine().SceneIds.Where(id => !reached.Contains(id)).ToList();
        Console.WriteLine("    random players reached the end " + endings + " times; scenes never visited: " + (never.Count == 0 ? "none" : string.Join(", ", never)));
    }

    // ------------------------------------------------------------------ helpers

    static void ToShack(Engine e, int seed = 1)
    {
        e.NewGame("basic");
        e.S.seed = seed;
        Walk(e, "Close", "What do you want", "Do I have", "Decide");
        e.CreateCharacter("Ash", "female", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 });
        Walk(e, "Go", "Help the sailors", "…");
    }

    static void Walk(Engine e, params string[] labels)
    {
        foreach (var l in labels) Pick(e, l);
    }

    static void Pick(Engine e, string label)
    {
        for (int tries = 0; tries < 10; tries++)
        {
            var c = e.Choices().FirstOrDefault(x => x.def.label.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0);
            if (c == null)
                throw new Exception("No choice like \"" + label + "\" in " + e.S.scene + ". Choices: " + string.Join(" / ", e.Choices().Select(x => x.def.label)));
            if (c.enabled) { if (Environment.GetEnvironmentVariable("TRACE") == "1") Console.WriteLine("      " + e.S.scene + " > " + c.def.label + "  energy " + e.S.energy.ToString("0") + " " + Rules.Clock(e.S)); e.Choose(c.def); return; }
            if (c.reason != null && c.reason.StartsWith("Too winded")) { e.CatchBreath(); continue; }
            throw new Exception("\"" + c.def.label + "\" is disabled in " + e.S.scene + ": " + c.reason);
        }
        throw new Exception("Couldn't get enough stamina for \"" + label + "\"");
    }

    static void Slots()
    {
        var e = NewEngine();
        e.DevStart("village");
        foreach (var id in Engine.StartingClothes)
            Assert(e.S.Equipped.Any(g => g.id == id), "should start wearing " + id);
        Assert(e.S.figure == "male" && e.S.skin != "", "the body and skin should default from creation");

        var shield = e.DevGear("buckler", "F");
        var axe = e.DevGear("wood_axe", "F");
        Assert(axe.equipped && axe.twoHanded && !shield.equipped, "a two-handed axe should take the shield off");
        e.Equip(shield.uid);
        Assert(shield.equipped && !axe.equipped, "a shield should take the two-handed axe off");

        var r1 = e.DevGear("copper_ring", "F");
        var r2 = e.DevGear("copper_ring", "F");
        Assert(r1.equipped && r2.equipped, "two rings can be worn at once");
        var r3 = e.DevGear("copper_ring", "F");
        Assert(r3.equipped && r2.equipped && !r1.equipped, "a third ring pushes out the first one put on");

        var cap = e.DevGear("iron_cap", "F");
        var jerkin = e.DevGear("leather_jerkin", "F");
        Assert(cap.equipped && jerkin.equipped && !e.S.Equipped.Any(g => g.id == "castaway_shirt"), "the jerkin should replace the shirt");
        Assert(Rules.GearGuard(e.S, e.data) > 0.2f, "armour pieces should add up");

        // An old save: a trinket slot and no clothes
        var old = NewEngine();
        old.DevStart("village");
        var st = old.S;
        st.gearItems.Clear();
        st.flags.Remove("clothed");
        st.gearItems.Add(new GearItem { uid = 90, id = "bone_charm", slot = "trinket", equipped = true });
        var json = Json.Write(st);
        var back = NewEngine();
        back.LoadState(Json.Read<GameState>(json));
        Assert(back.S.gearItems.Any(g => g.id == "bone_charm" && g.slot == "neck"), "an old trinket should move to the neck");
        Assert(Engine.StartingClothes.All(id => back.S.Equipped.Any(g => g.id == id)), "an old save should be given clothes");
    }

    static Engine NewEngine()
    {
        var dir = Path.Combine(Root, "Assets/Game/Resources");
        var stories = Directory.GetFiles(Path.Combine(dir, "Story"), "*.json").OrderBy(f => f)
            .Select(f => Json.Read<StoryFile>(File.ReadAllText(f))).ToList();
        var data = Json.Read<DataFile>(File.ReadAllText(Path.Combine(dir, "Data/data.json")));
        return new Engine(stories, data, Json);
    }

    static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets/Game"))) d = d.Parent;
        return d?.FullName ?? throw new Exception("Run this from inside the repo");
    }

    static void Test(string name, Action body)
    {
        try { body(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + "\n    " + ex.Message); if (Environment.GetEnvironmentVariable("STACK") == "1") Console.WriteLine(ex.StackTrace); }
    }

    static void Assert(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
    }

    static readonly NewtonsoftJson Json = new NewtonsoftJson();

    /// <summary>Behaves like Unity's JsonUtility: public fields only, no properties.</summary>
    class NewtonsoftJson : IJson
    {
        readonly JsonSerializerSettings settings = new JsonSerializerSettings { ContractResolver = new FieldsOnly() };
        public string Write(object o) => JsonConvert.SerializeObject(o, settings);
        public T Read<T>(string json) => JsonConvert.DeserializeObject<T>(json, settings);

        class FieldsOnly : DefaultContractResolver
        {
            protected override List<MemberInfo> GetSerializableMembers(Type t) =>
                t.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => !f.IsNotSerialized).Cast<MemberInfo>().ToList();

            protected override JsonProperty CreateProperty(MemberInfo m, MemberSerialization ms)
            {
                var p = base.CreateProperty(m, ms);
                p.Readable = p.Writable = true;
                return p;
            }

            protected override JsonObjectContract CreateObjectContract(Type objectType)
            {
                var c = base.CreateObjectContract(objectType);
                return c;
            }
        }
    }
}
