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
        Test("full playthrough: hut, coast road, Skelmed's jobs, the pack leader, the guild", FullPlaythrough);
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
        Test("levels, skills, moves and followers", Progression);
        Test("a party eats, drinks, tires and can walk out on you", Party);
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
        Assert(e.S.scene == "creation", "a new game should start at character creation");
        Assert(e.CreateCharacter("", "female", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 }) != null, "empty name should be refused");
        Assert(e.CreateCharacter("Ash", "female", "Lean", "Red", "runner", new[] { 2, 2, 1, 1, 1 }) != null, "too many points should be refused");
        Assert(e.CreateCharacter("Ash", "female", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 }) == null, "valid character refused");
        Assert(e.S.stats[Stat.Agi] == 5, "runner should get +1 agility");
        Assert(e.CreateCharacter("Ash", "", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 }) != null, "sex must be chosen");
        Assert(e.S.stats.Length == 5 && e.S.stats[Stat.Cha] == 4, "charisma should exist and take points");
        Assert(e.Fill("{they} nods at {them}") == "she nods at her", "pronouns should follow the chosen sex");
        Pick(e, "Go");
        Assert(e.S.scene == "wake" && e.S.hud && e.Area?.id == "yenson_hut" && AtSpot(e, "hut_bed"), "should wake in Yenson's hut, by the bed");
        Assert(Math.Abs(e.S.corruption - 5) < 0.01, "corruption should start at 5%");
        Assert(e.S.lastSleep != "", "waking in the hut should count as a save");
        Pick(e, "Sit up");
        Assert(e.S.scene == "yenson" && e.Current.speaker == "Yenson", "Yenson should be talking");
        Pick(e, "Where am I");
        Assert(e.S.result.Contains("Loran"), "his answer should show");
        Pick(e, "Eat the stew");
        Pick(e, "What can I do for you");
        Assert(e.Check("quest:water") && e.S.Count("bucket") == 1, "Yenson should ask for water and lend his bucket");
        Assert(e.ActiveQuests.Count() == 1 && !e.QuestGoals(e.data.Quest("water"))[0].met, "the water quest should be open");
        Pick(e, "Goodbye");
        Assert(e.Travelling && e.Area.id == "yenson_hut", "should be free to walk round the hut");

        // Sleep in the bed
        e.Face("left");
        Assert(e.UseLabel() == "Look", "facing the bed you can use it");
        e.Choose(e.UseChoice());
        Pick(e, "Sleep until morning");
        Assert(e.S.Day == 2 && e.S.Hour == 6 && e.Travelling, "should wake on day 2 at 06:00, was day " + e.S.Day + " " + Rules.Clock(e.S));

        // Yenson's jobs: water from the spring, deadwood from the copse, the boar
        LeaveHut(e);
        Assert(e.Area.id == "coast_road" && AtSpot(e, "shack"), "the door should put you outside the shack");
        int t0 = e.S.minutes;
        var sp = e.data.Spot("spring").spot;
        WalkUntil(e, (sp.x, sp.y), () => e.S.ax == sp.x && e.S.ay == sp.y);
        e.Face("left");
        e.Choose(e.UseChoice());
        Pick(e, "Fill Yenson's bucket");
        Assert(e.Check("ready:water"), "a full bucket should finish the goal");
        BackToYenson(e);
        Pick(e, "Here's your water");
        Assert(e.Check("done:water") && e.Check("quest:firewood") && e.S.Count("bucket") == 0 && e.S.Count("water_bucket") == 0, "handing in the water should start the firewood");
        Pick(e, "Goodbye");
        LeaveHut(e);
        foreach (var log in new[] { "log1", "log2", "log3" }) { UseThing(e, log); Pick(e, "Gather it"); }
        Assert(e.S.Count("firewood") == 3 && e.Check("ready:firewood"), "three armfuls of deadwood");
        BackToYenson(e);
        Pick(e, "Here's the firewood");
        Assert(e.Check("quest:boar") && e.S.InSlot("weapon")?.id == "gutting_knife", "Yenson should send you after the boar with his knife");
        Pick(e, "Goodbye");
        LeaveHut(e);
        var boar = e.Area.objects.First(o => o.id == "boar");
        WalkUntil(e, (boar.x - 1, boar.y), () => e.S.scene == "boar");
        Pick(e, "Fight it");
        Assert(e.S.inCombat && e.S.enemy == "boar", "the boar should fight");
        for (int i = 0; i < 80 && e.S.inCombat; i++) StepToward(e, (0, 0));
        Assert(e.S.scene == "boar_won" && e.Check("ready:boar"), "should beat the boar, was " + e.S.scene);
        Pick(e, "Walk on");
        Assert(e.Objects().All(o => o.id != "boar"), "the boar should be gone");
        BackToYenson(e);
        Pick(e, "The boar's dead");
        Assert(e.Check("done:boar") && !e.ActiveQuests.Any() && e.S.coins >= 5 && e.S.Count("bread") == 2 && e.S.water == 4, "Yenson should pay you and send you off");
        Assert(e.Journal().Any(j => j.Contains("Skelmed")), "the journal should point to Skelmed");
        Console.WriteLine("    Yenson's three jobs took " + Engine.Duration(e.S.minutes - t0));
        Pick(e, "Goodbye");
        LeaveHut(e);

        e.S.stepsSinceFight = -1000;   // this run is about the story; random fights are tested on their own
        WalkUntil(e, Gate(e), () => e.S.scene == "dog");
        Pick(e, "Throw it some bread");
        Pick(e, "Walk on");
        WalkUntil(e, Gate(e), () => e.Area.id == "skelmed");
        Assert(e.Travelling && AtSpot(e, "village_gate"), "the gate should take you into Skelmed");
        Assert(e.Journal().Any(j => j.Contains("Garran") && j.Contains("Orlen")), "the journal should point to the people with work");

        // Skelmed's three jobs: Ida's lodge, Garran's quarry, Orlen's lessons. Then the pack leader and the guild.
        foreach (var food in e.S.items.Where(i => e.data.Item(i.id).food).Select(i => i.id).ToList()) e.Eat(food);
        e.S.result = "";
        e.S.stepsSinceFight = -100000;
        int t1 = e.S.minutes;
        Keep(e); UseThing(e, "ida");
        Assert(e.S.scene == "ida" && e.Current.speaker == "Ida", "Ida should talk");
        Pick(e, "Any work going");
        Assert(e.Check("quest:lodge") && e.S.Count("pail") == 1, "Ida should give you the lodge job and a pail");
        Pick(e, "Goodbye");
        Keep(e); UseThing(e, "garran");
        Assert(e.S.scene == "smith", "Garran should talk about his forge");
        Assert(!e.Choices().Any(c => c.def.label.Contains("3 coppers")), "buying iron comes after the first piece");
        Assert(!e.Choices().Any(c => c.def.label == "An iron knife"), "no forging before there's ore");
        Pick(e, "Is there any work");
        Assert(e.Check("quest:quarry") && e.S.Count("pick") == 1, "Garran should send you to the quarry with his pick");
        Pick(e, "Another time");
        Keep(e); UseThing(e, "mira");
        Assert(e.S.scene == "mira" && !e.Choices().Any(c => c.def.label.Contains("guild book")), "no guild book before the three jobs");
        Pick(e, "How do I join");
        Assert(e.S.result.Contains("all three"), "Mira should say to help all three");
        Leave(e);

        Keep(e); ToArea(e, "hills_gate");
        Assert(e.Area.id == "skelmed_hills" && AtSpot(e, "hills_gate"), "the lane north should lead up into the hills");
        Keep(e); UseThing(e, "orlen");
        Pick(e, "Will you teach me magic");
        Pick(e, "Try to fill the cup");
        Pick(e, "What comes next");
        Assert(e.S.spells.Contains("draw_water") && e.S.spells.Contains("kindle"), "Orlen should teach Draw Water and Kindle");
        Pick(e, "Goodbye");
        Keep(e); UseThing(e, "rook");
        Pick(e, "Goodbye");
        Keep(e); UseThing(e, "snare1"); Pick(e, "Take the hare");
        Keep(e); UseThing(e, "snare2"); Pick(e, "Take the hare");
        Keep(e); UseThing(e, "snare3");
        Pick(e, "Fight it");
        Assert(e.S.inCombat && e.S.enemy == "dog", "a wolf should be robbing the last snare");
        for (int i = 0; i < 80 && e.S.inCombat; i++) StepToward(e, (0, 0));
        Assert(e.S.scene == "snare3_won", "should beat the snare robber, was " + e.S.scene);
        Pick(e, "Take it");
        Assert(e.S.Count("hare") == 3 && e.Objects().All(o => !o.id.StartsWith("snare")), "three hares, snare line done");
        Keep(e); UseTile(e, 'w');
        Pick(e, "Fill the pail");
        Assert(e.S.Count("water_pail") == 1, "a pail of stream water");
        Keep(e); UseTile(e, 'q');
        Assert(e.S.scene == "quarry_dig", "the rock face should be minable");
        for (int i = 0; i < 3; i++) { Keep(e); Pick(e, "Swing the pick"); }
        Assert(e.S.Count("ore") == 3 && e.Check("ready:quarry") == false && !e.Choices().Any(c => c.def.label == "Swing the pick"), "three lumps of ore, then no more digging");
        Pick(e, "Leave it");
        Keep(e); UseThing(e, "glint");
        Pick(e, "Take it, to show Garran");
        Assert(e.S.Count("gem") == 1, "the green gem");
        Keep(e); UseThing(e, "brazier");
        Pick(e, "Cast Kindle");
        Assert(e.S.scene == "mage_fire", "lighting the brazier should ring the fire bell");
        Pick(e, "Run down to the village");
        Assert(e.Objects().All(o => o.id != "orlen"), "Orlen should have gone down to the fire");

        Keep(e); ToArea(e, "skelmed_north");
        Assert(e.Area.id == "skelmed" && e.Objects().Count(o => o.id.StartsWith("fire")) == 3, "the net shed should be burning");
        Keep(e); UseThing(e, "butt");
        Pick(e, "Pour in the pail");
        Assert(e.Check("flag:butt_half") && e.S.Count("pail") == 1, "half the water butt");
        Keep(e);
        Keep(e); UseThing(e, "fire1"); Pick(e, "Cast Draw Water");
        Keep(e); UseThing(e, "fire2"); Pick(e, "Cast Draw Water");
        Assert(!e.Check("done:orlen"), "two fires out, one to go");
        Keep(e); UseThing(e, "fire3"); Pick(e, "Beat it out");
        Assert(e.S.scene == "fire_after" && e.Check("done:orlen") && e.Check("flag:fire_out"), "the fire's out and Orlen's lessons done, was " + e.S.scene);
        Pick(e, "Catch your breath");
        Assert(e.Objects().Any(o => o.id == "orlen_fire") == false, "Orlen goes home after the fire");
        Keep(e); UseThing(e, "block");
        for (int i = 0; i < 4; i++) { Keep(e); Pick(e, "Split a log"); }
        Assert(e.S.Count("split_wood") == 4 && !e.Choices().Any(c => c.def.label == "Split a log"), "four logs split, then enough");
        Pick(e, "Leave it");
        Keep(e); ToArea(e, "hills_gate");
        Keep(e); UseTile(e, 'w');
        Pick(e, "Fill the pail");
        Keep(e); ToArea(e, "skelmed_north");
        Keep(e); UseThing(e, "butt");
        Pick(e, "Pour in the pail");
        Assert(e.Check("ready:lodge"), "hares, wood and water: the lodge job is ready");
        Keep(e); UseThing(e, "ida");
        Pick(e, "It's all done");
        Assert(e.S.scene == "ida_pay", "Ida should pay you");
        int c0 = e.S.coins;
        Pick(e, "Take it and thank her");
        Assert(e.Check("done:lodge") && e.S.coins == c0 + 5 && e.S.Count("hare") == 0 && e.S.Count("pail") == 0, "the lodge job is done and paid");

        // Ida's last job: the pack leader, with Rook beside you
        Keep(e); UseThing(e, "ida");
        Pick(e, "Anything else");
        Assert(e.Check("quest:pack") && e.Member("rook") != null && e.S.gearItems.Any(g => g.id == "hunting_bow" && !g.equipped), "Ida should send Rook with you, and a bow");
        Leave(e);
        int lv0 = e.S.level;
        Keep(e); ToArea(e, "hills_gate");
        Assert(e.Objects().All(o => o.id != "rook"), "Rook walks with you now, not at his snares");
        Keep(e);
        WalkUntil(e, (29, 5), () => e.S.scene == "den");
        Pick(e, "Fight it");
        Assert(e.S.inCombat && e.S.enemy == "pack_leader", "the pack leader should fight");
        int rounds = 0;
        for (; rounds < 200 && e.S.inCombat; rounds++) StepToward(e, (0, 0));
        Assert(e.S.scene == "den_won", "should beat the pack leader with Rook, was " + e.S.scene);
        Console.WriteLine("    pack leader took " + rounds + " rounds, hp " + e.S.hp + ", Rook " + e.Member("rook").hp + " (" + string.Join(", ", Rules.Conditions(e.Member("rook"), e.data)) + ")");
        Pick(e, "Skin it");
        Assert(e.S.Count("wolf_pelt") == 1 && e.Check("ready:pack"), "the pelt, and the job ready");
        Keep(e); ToArea(e, "skelmed_north");
        Keep(e); UseThing(e, "ida");
        Pick(e, "The pack leader");
        Assert(e.Check("done:pack") && e.Member("rook") != null && e.Check("flag:rook_escort") && e.S.level > lv0, "the job's done, you've levelled, and Rook walks on with you to the town");
        Leave(e);

        Keep(e); UseThing(e, "garran");
        Pick(e, "I've got the ore");
        Assert(e.Check("flag:ore_in") && e.S.Count("ore") == 0 && e.S.Count("pick") == 0, "Garran takes the ore and his pick back");
        Pick(e, "I found this at the quarry");
        Pick(e, "An iron knife, with the gem");
        Assert(e.S.scene == "forge" && e.Current.title.Contains("knife"), "making gear should go to the anvil, was " + e.S.scene);
        Pick(e, "Work the forge");
        Assert(e.Travelling && e.Area.id == "skelmed" && e.S.result.Contains("Quality"), "after forging, back in the village with the quality, was " + e.S.scene);
        Assert(e.Check("done:quarry") && e.S.Count("gem") == 0, "Garran's job done, the gem set in the knife");
        var knife = e.S.InSlot("weapon");
        Assert(knife != null && knife.id == "knife", "should be holding the new knife");
        Assert(e.S.gearItems.Any(g => g.id == "gutting_knife" && !g.equipped), "Yenson's knife should go back in the pack");
        Keep(e); UseThing(e, "garran");
        var buy = e.Choices().First(c => c.def.label.Contains("3 coppers"));
        Assert(buy.enabled, "your coppers should buy a bar of iron");
        int coins = e.S.coins;
        e.S.coins = 1;
        Assert(!e.Choices().First(c => c.def.label.Contains("3 coppers")).enabled, "iron costs coin you don't have");
        e.S.coins = coins;
        Pick(e, "Another time");

        Assert(e.Journal().Any(j => j.Contains("guild's book")), "the journal should point to Mira's book");
        Keep(e); UseThing(e, "mira");
        Pick(e, "Put your name in the guild book");
        Assert(e.Check("flag:guild_member") && e.S.Count("guild_card") == 1, "Mira should put you in the book and give you a card");
        Leave(e);
        Keep(e); UseThing(e, "ida");
        Pick(e, "What now");
        Assert(e.S.result.Contains("town inland"), "Ida should point you inland");
        Pick(e, "Goodbye");
        Assert(!e.ActiveQuests.Any() && e.FinishedQuests.Count() == 7, "all seven jobs done");
        Assert(e.SkillLevel("mining") >= 1 && e.SkillLevel("smithing") >= 1 && e.SkillLevel("magic") >= 1 && e.S.level >= 3,
               "doing things should grow skills and level: " + string.Join(", ", e.S.skills.Select(k => k.id + " " + k.level)) + ", level " + e.S.level);
        Console.WriteLine("    Skelmed's three jobs took " + Engine.Duration(e.S.minutes - t1) + ", rep " + e.S.Rep("skelmed") + ", gear " + knife.name + " (" + knife.rank + ")");

        // Out through the gate and back down the road
        WalkUntil(e, (17, 26), () => e.Area.id == "coast_road");
        Assert(AtSpot(e, "skelmed"), "leaving should put you outside the gate");
        Assert(!e.SceneIds.Any(id => id == "village" || id == "bedroom" || id == "inn" || id == "guild"), "the old text pages should be gone");
        Console.WriteLine("    finished on day " + e.S.Day + " at " + Rules.Clock(e.S) + ", corruption " + e.S.corruption.ToString("0.00") +
                          "%, stats " + string.Join("/", e.S.stats) + ", journal: " + string.Join(" | ", e.Journal()));
    }

    /// <summary>Chooses the last option until you're back out walking.</summary>
    static void Leave(Engine e)
    {
        for (int i = 0; i < 10 && e.S.scene != "travel"; i++) e.Choose(e.Choices().Where(c => c.enabled).Last().def);
        Assert(e.S.scene == "travel", "should be back out, was " + e.S.scene);
    }

    static void Progression()
    {
        // EXP and levels
        var e = NewEngine();
        e.DevStart("village");
        Assert(e.S.level == 1 && e.S.exp == 0, "start at level 1");
        int hp1 = Rules.MaxHp(e.S), st1 = Rules.MaxStamina(e.S, e.data);
        e.GainExp(Rules.ExpToNext(1), "test");
        Assert(e.S.level == 2 && e.S.exp == 0 && Rules.MaxHp(e.S) > hp1 && Rules.MaxStamina(e.S, e.data) > st1 && e.S.hp == Rules.MaxHp(e.S), "a level up should raise and refill hp");

        // Skills grow with use and unlock moves
        Assert(e.WeaponSkill() == "dagger", "the gutting knife is a dagger");
        Assert(!e.UsableMoves().Any(m => m.skill == "dagger"), "no dagger moves at level 0");
        for (int i = 0; i < 40 && e.SkillLevel("dagger") < 2; i++) e.TrainSkill("dagger", 3, "test");
        Assert(e.SkillLevel("dagger") == 2 && e.UsableMoves().Any(m => m.id == "quick_stab"), "dagger 2 unlocks Quick Stab");
        for (int i = 0; i < 2000; i++) e.TrainSkill("dagger", 5, "test");
        Assert(e.SkillLevel("dagger") == Rules.MaxSkill, "skills cap out");

        // Attacking trains your weapon's skill, and a fight gives EXP
        var f = NewEngine();
        f.DevStart("dog");
        int exp0 = f.S.exp;
        f.Act(f.Choices().First(c => c.def.label == "Attack").def, Engine.TimingPerfect);
        Assert(f.SkillProgress("dagger") > 0 || f.SkillLevel("dagger") > 0, "attacking with a knife trains daggers");
        for (int i = 0; i < 100 && f.S.inCombat; i++) StepToward(f, (0, 0));
        Assert(f.S.level > 1 || f.S.exp > exp0, "winning a fight gives EXP");

        // Moves show up in a fight once you know them, and only for the weapon in hand
        var m = NewEngine();
        m.DevStart("dog");
        m.TrainSkill("dagger", 100, "test");
        m.TrainSkill("bow", 100, "test");
        var skills = m.Choices().Where(c => Engine.CommandOf(c.def) == "skill").Select(c => c.def.label).ToList();
        Assert(skills.Any(l => l.Contains(m.data.Move("quick_stab").name)) && !skills.Any(l => l.Contains(m.data.Move("aimed_shot").name)),
               "dagger moves with a knife, no bow moves: " + string.Join(", ", skills));
        float hpBefore = m.S.enemyHp;
        m.Act(m.Choices().First(c => c.def.label.Contains(m.data.Move("quick_stab").name)).def, Engine.TimingGood);
        Assert(m.S.enemyHp < hpBefore || !m.S.inCombat, "Quick Stab should hurt");

        // Life skills make the work easier
        var w = NewEngine();
        w.DevStart("village");
        var chop = new ChoiceDef { label = "work", stamina = 20, train = new List<string> { "woodcutting+1" } };
        int before = w.StaminaCost(chop);
        for (int i = 0; i < 2000; i++) w.TrainSkill("woodcutting", 5, "test");
        Assert(w.StaminaCost(chop) < before, "a skilled woodcutter tires less");

        // Followers walk behind you, fight beside you and go home
        var b = NewEngine();
        b.DevStart("boss");
        Assert(b.Member("rook") != null && b.FollowerUp, "Rook should be with you");
        int px = b.S.ax, py = b.S.ay;
        StepToward(b, (b.S.ax, b.S.ay - 1));
        Assert(b.S.party[0].x == px && b.S.party[0].y == py, "Rook steps into the tile you left");
        var save = Json.Write(b.S);
        var b2 = NewEngine();
        b2.LoadState(Json.Read<GameState>(save));
        Assert(b2.Member("rook") != null && b2.Member("rook").hp == b.Member("rook").hp && b2.Member("rook").hunger == b.Member("rook").hunger, "Rook survives a save");
        WalkUntil(b, (29, 5), () => b.S.scene == "den");
        Pick(b, "Fight it");
        float ehp = b.S.enemyHp;
        b.Act(b.Choices().First(c => c.def.label == "Defend").def, Engine.TimingGood);
        Assert(b.S.enemyHp < ehp || !b.S.inCombat, "Rook should hit the pack leader even when you defend");
    }

    static void Party()
    {
        var e = NewEngine();
        e.DevStart("village");
        // Two made-up companions to fill out a party
        e.data.file.followers.Add(new FollowerDef { id = "t_wren", name = "Wren", sprite = "npc_rook", hp = 50, atkMin = 3, atkMax = 5, quest = "pack" });
        e.data.file.followers.Add(new FollowerDef { id = "t_bram", name = "Bram", sprite = "npc_rook", hp = 80, atkMin = 5, atkMax = 9, appetite = 2 });
        e.data.file.followers.Add(new FollowerDef { id = "t_extra", name = "Extra", sprite = "npc_rook", hp = 10 });
        e.Choose(Ch("follow:rook")); e.Choose(Ch("follow:t_wren")); e.Choose(Ch("follow:t_bram"));
        Assert(e.S.party.Count == 3, "three in the party");
        e.Choose(Ch("follow:t_extra"));
        Assert(e.S.party.Count == 3 && e.Member("t_extra") == null, "a party holds three");

        // They walk in a line behind you
        e.S.result = "";
        for (int i = 0; i < 4; i++)
        {
            int px = e.S.ax, py = e.S.ay;
            var c = e.StepChoice(false);
            if (c == null) { e.Face(e.S.facing == "down" ? "left" : "down"); continue; }
            e.Choose(c);
            Assert(e.S.party[0].x == px && e.S.party[0].y == py, "the first one steps where you were");
            e.S.result = "";
        }

        // Rook starts hungry; feeding him fixes it and lifts him
        var rook = e.Member("rook");
        Rules.Advance(e.S, e.data, 2 * 60, false);
        Assert(Rules.Conditions(rook, e.data).Contains("hungry") || rook.hunger < 40, "Rook should be getting hungry, was " + rook.hunger);
        e.S.items.Add(new ItemStack { id = "fish", count = 1 });
        float m0 = rook.morale;
        Assert(e.Feed("rook", "fish") && rook.hunger > 40 && rook.morale > m0 && e.S.Count("fish") == 0, "feeding Rook should fill him and cheer him up");
        e.S.water = 1;
        float t0 = rook.thirst;
        Assert(e.GiveWater("rook") && rook.thirst > t0 && e.S.water == 0, "sharing the waterskin");

        // A hungry appetite empties the pack at camp
        var bram = e.Member("t_bram");
        bram.hunger = rook.hunger = 90;
        Rules.Advance(e.S, e.data, 6 * 60, false);
        Assert(bram.hunger < rook.hunger, "a big eater gets hungry faster");
        e.S.items.RemoveAll(i => e.data.Item(i.id)?.food == true);
        e.S.items.Add(new ItemStack { id = "bread", count = 1 });
        e.S.water = 0;
        float wrenMorale = e.Member("t_wren").morale;
        e.S.result = "";
        e.Choose(Ch("sleep:camp"));
        Assert(e.S.Count("bread") == 0, "someone ate the bread");
        Assert(e.S.result.Contains("goes to sleep hungry") || e.S.result.Contains("nothing to drink"), "with no supplies, someone goes without: " + e.S.result);

        // Neglect: spirits fall; most companions leave, a story companion stays
        foreach (var m in e.S.party) { m.hunger = 0; m.thirst = 0; m.morale = 5; m.hp = 1000; }
        e.S.hp = 1000;
        Rules.Advance(e.S, e.data, 4 * 60, false);
        Assert(e.Member("t_bram") == null && e.Member("t_wren") == null, "neglected companions walk out");
        Assert(e.Member("rook") != null, "Rook stays to the town, however he feels");

        // A companion's own quest lifts their spirits
        var q = NewEngine();
        q.DevStart("village");
        q.data.file.followers.Add(new FollowerDef { id = "t_wren", name = "Wren", sprite = "npc_rook", hp = 50, atkMin = 3, atkMax = 5, quest = "pack" });
        q.Choose(Ch("follow:t_wren"));
        float before = q.Member("t_wren").morale;
        q.Choose(Ch("complete:pack"));
        Assert(q.Member("t_wren").morale >= before + 25, "doing Wren's quest should lift her");

        // Hunger and tiredness weaken them in a fight
        var w = new PartyMember { id = "rook", hp = 60, hunger = 10, thirst = 10, energy = 10, morale = 60 };
        Assert(Rules.PartyPower(w, e.data) < 0.6f, "a starving, parched, exhausted companion fights badly");
    }

    /// <summary>A throwaway choice that applies some effects, for tests.</summary>
    static ChoiceDef Ch(params string[] effects) => new ChoiceDef { label = "test", effects = effects.ToList() };

    static void SaveLoad()
    {
        var oldSave = new GameState { stats = new[] { 4, 4, 4, 4 }, xp = new float[] { 1, 1, 1, 1 } };
        var up = NewEngine();
        up.LoadState(oldSave);
        Assert(up.S.stats.Length == 5 && up.S.stats[Stat.Cha] == 3 && up.S.xp.Length == 5, "old saves should gain Charisma");
        var onOldRoad = NewEngine();
        onOldRoad.LoadState(new GameState { scene = "road2", hud = true });
        Assert(onOldRoad.S.scene == "travel" && AtSpot(onOldRoad, "stream"), "saves on the old road scenes should land by the stream");
        var inOldScene = NewEngine();
        inOldScene.LoadState(new GameState { scene = "guild", hud = true, flags = { "arrived" } });
        Assert(inOldScene.S.scene == "travel" && AtSpot(inOldScene, "skelmed"), "saves in a removed scene should land in the world");
        var inShack = NewEngine();
        inShack.LoadState(new GameState { scene = "shack", hud = true });
        Assert(inShack.S.scene == "travel" && AtSpot(inShack, "hut_bed"), "saves in the old shack page should wake in the hut");
        var midRoad = NewEngine();
        midRoad.LoadState(new GameState { scene = "travel", hud = true, travelRoute = "coast_road", travelAt = 3 });
        Assert(midRoad.S.scene == "travel" && midRoad.Area != null && midRoad.S.travelRoute == "", "saves walking the old road should land in the world");

        var e = NewEngine();
        ToHut(e);
        string a = Json.Write(e.S);
        var back = Json.Read<GameState>(a);
        Assert(Json.Write(back) == a, "state should survive a save and load unchanged");
    }

    static void BasicDeath()
    {
        var e = NewEngine();
        ToRoad(e);
        LeaveHut(e, back: true);
        Pick(e, "Goodbye");
        e.Face("left");
        WalkUntil(e, (2, 3), () => e.S.ax == 2 && e.S.ay == 3);
        e.Face("left");
        e.Choose(e.UseChoice());
        Pick(e, "Sleep until morning");
        int savedTime = e.S.minutes;
        LeaveHut(e);
        e.S.stepsSinceFight = -1000;
        UseThing(e, "pools");
        Pick(e, "Search the tide pools");
        e.S.hp = 0;
        e.GoTo(e.S.scene);
        Assert(e.S.scene == "death", "hp 0 should kill you");
        Pick(e, "Wake at your last sleep");
        Assert(e.S.scene == "travel" && e.Area.id == "yenson_hut" && e.S.minutes == savedTime, "should wake in the hut at the time of the last sleep, was " + e.S.scene + " in " + e.S.area);
        Assert(!e.S.Has("pools"), "things after the last sleep should be undone");
        Assert(e.S.result.Contains("You wake with a gasp"), "should explain what happened");
    }

    static void PermaDeath()
    {
        var e = NewEngine();
        e.NewGame("perma");
        e.CreateCharacter("Rook", "male", "Lean", "Red", "desk", new[] { 1, 1, 1, 1, 1 });
        Walk(e, "Go", "Sit up");
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
        ToRoad(e);
        e.S.energy = 0.05f;
        e.S.coins = 6;
        e.S.stepsSinceFight = -1000;
        StepToward(e, Gate(e));
        Assert(e.S.scene == "collapse", "should collapse, was " + e.S.scene);
        Assert(e.S.coins == 3, "collapsing on the road should cost half your coins, had " + e.S.coins + " result " + e.S.result);
        Pick(e, "Get up");
        Assert(e.S.scene != "collapse" && e.S.energy > 0, "should get up where you fell, was " + e.S.scene);
    }

    static void CombatExhaustion()
    {
        var e = AtDog(1);
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
            var e = AtDog(seed);
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
        ToRoad(e, seed);
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
        ToHut(e);
        e.DevGear("gutting_knife", "F");
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
        ToRoad(e);
        var a = e.Area;
        Assert(a.id == "coast_road" && AtSpot(e, "shack"), "should be outside the shack");

        // Walking and running
        e.Face("down");
        var walk = e.StepChoice(false);
        var run = e.StepChoice(true);
        Assert(walk != null && run != null && run.time < walk.time && run.stamina > walk.stamina, "running should be quicker but tiring");

        // In and out of the door
        e.Face("up");
        e.Choose(e.StepChoice());
        Assert(e.Area.id == "yenson_hut" && AtSpot(e, "hut_door") && e.Travelling, "walking into the door should take you inside");
        Assert(!e.Choices().Any(), "no camping indoors");
        e.Face("down");
        e.Choose(e.StepChoice());
        Assert(e.Area.id == "coast_road" && AtSpot(e, "shack"), "walking out should put you back outside");

        // The sign by the road
        UseThing(e, "sign_shack");
        Assert(e.S.scene == "travel" && e.S.result.Contains("Skelmed"), "reading a sign should show what it says");
        e.S.result = "";

        // The coast road to Skelmed: a couple of hours, past the wolf
        int t0 = e.S.minutes;
        e.S.stepsSinceFight = -1000;
        WalkUntil(e, Gate(e), () => e.S.scene == "dog");
        Pick(e, "Throw it some bread");
        Pick(e, "Walk on");
        var wolf = a.objects.First(o => o.id == "wolf");
        Assert(e.ObjectAt(wolf.x, wolf.y) == null, "the wolf should be gone from the road");
        WalkUntil(e, Gate(e), () => e.Area.id == "skelmed");
        Assert(e.S.minutes - t0 >= 150, "the walk should take a good while, took " + (e.S.minutes - t0) + " min");
        Assert(!e.Choices().Any(), "no camping in the village");

        // The well
        e.Face("up");
        WalkUntil(e, (18, 14), () => e.S.ax == 18 && e.S.ay == 14);
        e.Face("up");
        e.Choose(e.UseChoice());
        Assert(e.S.scene == "well_drink", "you should be able to use the well");
        e.S.water = 0;
        Pick(e, "Drink");
        Assert(e.S.water == 4 && e.Travelling, "the waterskin should be full");

        // Back out, and down to the stream
        WalkUntil(e, (17, 26), () => e.Area.id == "coast_road");
        Assert(e.S.scene == "travel" && AtSpot(e, "skelmed"), "leaving should put you outside the gate");
        var st = a.spots.First(p => p.id == "stream");
        WalkUntil(e, (st.x, st.y), () => e.S.ax == st.x && e.S.ay == st.y);
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

        // Everything you can use, in every area, opens a scene that leads back out
        foreach (var area in e.data.file.areas)
            foreach (var o in area.objects.Where(o => !string.IsNullOrEmpty(o.scene) && o.id != "wolf" && o.id != "boar"))
            {
                var v = NewEngine();
                v.DevStart(area.id == "skelmed" ? "village" : area.id == "yenson_hut" ? "hut" : area.id == "skelmed_hills" ? "hills" : "road");
                if (area.id == "yenson_hut") Walk(v, "Sit up", "Goodbye");
                if (o.id.StartsWith("fire") || o.id == "orlen_fire") { v.S.flags.Add("fire_started"); v.S.spells.Add("draw_water"); }
                if (o.id == "brazier_lit") v.S.flags.Add("brazier_lit");
                if (o.id == "alpha") v.S.flags.Add("quest:pack");
                if (!v.All(o.when)) throw new Exception(o.id + " isn't there to test");
                v.S.coins = 20;
                v.S.flags.Add("dog_met");
                v.S.stepsSinceFight = -1000;
                if (o.id == "alpha") WalkUntil(v, (29, 5), () => v.S.scene == "den");   // the den's edge stops you first
                else UseThing(v, o.id);
                Assert(v.S.scene != "travel" || v.S.inCombat, "using " + o.id + " should open its scene");
                for (int i = 0; i < 30 && (v.S.scene != "travel" || v.S.inCombat) && v.S.scene != "death"; i++)
                {
                    if (v.S.inCombat) { v.DevWinFight(); continue; }
                    if (v.S.scene == "forge") { v.FinishForge(1, 1, 1); continue; }
                    v.Choose(v.Choices().Where(c => c.enabled).Last().def);
                }
                Assert(v.S.scene == "travel" && v.Area.id == area.id, "after " + o.id + " you should be back where you were, was " + v.S.scene);
            }

        // Fights come at random as you walk, never too close together; nothing else interrupts the walk
        var fights = new Dictionary<string, int>();
        var seen = new HashSet<string>();
        int closest = 999;
        for (int seed = 0; seed < 60; seed++)
        {
            var w = NewEngine();
            ToRoad(w, seed);
            int last = -1, steps = 0;
            var goal = Gate(w);
            for (int i = 0; i < 400 && w.Area.id == "coast_road" && w.S.scene != "death"; i++)
            {
                if (w.S.inCombat) { w.DevWinFight(); continue; }
                if (w.S.scene.StartsWith("ev_") && !w.S.scene.EndsWith("_won")) seen.Add(w.S.scene);
                if (w.S.scene == "dog") { Pick(w, "Throw it some bread"); continue; }
                bool walking = w.S.scene == "travel";
                if (walking) steps++;
                StepToward(w, goal);
                if (walking && w.S.inCombat)
                {
                    fights[w.S.enemy] = fights.TryGetValue(w.S.enemy, out var n) ? n + 1 : 1;
                    if (last >= 0) closest = Math.Min(closest, steps - last);
                    last = steps;
                }
            }
        }
        Console.WriteLine("    fights: " + string.Join(", ", fights.Select(f => f.Key + " " + f.Value)) + "; closest two fights " + closest + " steps apart");
        Assert(fights.Values.Sum() >= 20, "random fights should happen on the walk");
        Assert(closest > a.fightGap, "fights should never come close together");
        Assert(seen.Count == 0, "no text events should pop up on the road: " + string.Join(", ", seen));
    }

    static void Forging()
    {
        GearItem ForgeOnce(string effect, float h, float m, float q)
        {
            var e = NewEngine();
            ToHut(e);
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
            { "village", "travel" }, { "road", "travel" }, { "hut", "wake" }, { "forge_first", "smith" }, { "forge_iron", "smith" },
            { "hills", "travel" }, { "fire", "travel" }, { "guild", "travel" }, { "boss", "travel" },
            { "dog", "travel" }, { "crab", "travel" },
        };
        foreach (var (id, _) in Engine.DevStarts)
        {
            var e = NewEngine();
            e.DevStart(id);
            Assert(e.IsDev && e.S.scene == expect[id] && e.Area != null, id + " should land in " + expect[id] + ", was " + e.S.scene);
            if (e.S.scene != "travel" || e.S.inCombat) Assert(e.Choices().Any(c => c.enabled), id + " should have something to do");
            if (id == "dog" || id == "crab") Assert(e.S.inCombat && e.S.enemy == id, id + " should start the fight");
            if (id == "forge_first") { Pick(e, "An iron knife"); Assert(e.S.scene == "forge", "should be at the anvil"); }
            if (id == "forge_iron") { Pick(e, "An iron cap"); Assert(e.S.scene == "forge", "should be at the anvil"); e.FinishForge(1, 1, 1); Assert(e.Travelling, "back in the village"); }
            if (id == "fire") Assert(e.Objects().Count(o => o.id.StartsWith("fire")) == 3, "the net shed should be burning");
            if (id == "guild") { e.Choose(e.UseChoice()); Assert(e.S.scene == "mira" && e.Choices().Any(c => c.enabled && c.def.label.Contains("guild book")), "Mira's book should be open to you"); }
            if (id == "boss") { Assert(e.Member("rook") != null, "Rook should be with you"); WalkUntil(e, (29, 5), () => e.S.scene == "den"); Pick(e, "Fight it"); Assert(e.S.inCombat && e.S.enemy == "pack_leader", "should face the pack leader"); }
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
        var z = e.data.Area("coast_road").zones.First(z => z.to == "village_gate");
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

    /// <summary>Walks to the zone that leads to a spot in another area, and through it.</summary>
    static void ToArea(Engine e, string spot)
    {
        var z = e.Area.zones.First(z => z.to == spot);
        string target = e.data.Spot(spot).area.id;
        WalkUntil(e, (z.x0, z.y0), () => e.Area.id == target && e.Travelling);
    }

    /// <summary>Walks up to the nearest tile of a kind (the stream, the rock face), faces it and uses it.</summary>
    static void UseTile(Engine e, char t)
    {
        var a = e.Area;
        int best = int.MaxValue;
        (int x, int y, string f) pick = (0, 0, "");
        for (int y = 0; y < a.rows.Count; y++)
            for (int x = 0; x < a.rows[y].Length; x++)
            {
                if (a.rows[y][x] != t) continue;
                foreach (var f in Engine.Facings)
                {
                    var (dx, dy) = Engine.Dir(f);
                    int px = x - dx, py = y - dy;
                    if (!Engine.InBounds(a, px, py) || (e.Blocked(px, py) && !(e.S.ax == px && e.S.ay == py))) continue;
                    var path = e.PathTo(px, py);
                    if (path != null && path.Count < best) { best = path.Count; pick = (px, py, f); }
                }
            }
        if (best == int.MaxValue) throw new Exception("no way to a '" + t + "' tile");
        WalkUntil(e, (pick.x, pick.y), () => e.Travelling && e.S.ax == pick.x && e.S.ay == pick.y);
        e.Face(pick.f);
        e.Choose(e.UseChoice() ?? throw new Exception("nothing to use facing '" + t + "'"));
    }

    /// <summary>Tops up stamina, energy, hunger and thirst, for runs that are about the story, not survival.</summary>
    static void Keep(Engine e)
    {
        if (e.S.stamina < 40) e.CatchBreath();
        if (e.S.stamina < 40) e.CatchBreath();
        e.S.energy = Math.Max(e.S.energy, 60);
        e.S.hunger = Math.Max(e.S.hunger, 60);
        e.S.thirst = Math.Max(e.S.thirst, 60);
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
        int villages = 0;
        for (int seed = 0; seed < 300; seed++)
        {
            var e = NewEngine();
            var rng = new Random(seed);
            e.NewGame(seed % 2 == 0 ? "basic" : "perma");
            bool village = false;
            for (int step = 0; step < 600; step++)
            {
                reached.Add(e.S.scene);
                if (e.S.scene == "title") { e.NewGame("basic"); continue; }
                if (e.S.scene == "creation" && !e.S.Has("created"))
                    e.CreateCharacter("Test", "other", "Lean", "Red", "nights", new[] { 5, 0, 0, 0, 0 });
                if (e.Area?.id == "skelmed") village = true;
                if (e.S.scene == "forge") { e.FinishForge((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble()); continue; }

                var opts = e.Choices().Where(c => c.enabled).ToList();
                int r = rng.Next(10);
                if (r == 0 && e.FreeToAct) { e.CatchBreath(); continue; }
                if (r == 1 && e.FreeToAct)
                {
                    var food = e.S.items.FirstOrDefault(i => e.data.Item(i.id).food);
                    if (food != null) { e.Eat(food.id); continue; }
                }
                if (r == 2 && e.FreeToAct) { e.Drink(); e.DrawWater(); continue; }
                if (e.Travelling && !string.IsNullOrEmpty(e.S.result) && r < 5) { e.S.result = ""; continue; }
                if (e.Travelling && (r >= 3 || opts.Count == 0))
                {
                    // Walk: mostly on towards Skelmed, sometimes towards something to use, sometimes use what's in front
                    var a = e.Area;
                    (int x, int y) goal = a.id == "coast_road" ? Gate(e) : a.id == "yenson_hut" ? (5, 7) : (rng.Next(2) == 0 ? (17, 26) : (28, 23));
                    if (r == 8 && a.objects.Count > 0) { var o = a.objects[rng.Next(a.objects.Count)]; goal = (o.x, o.y + 1); }
                    if (r == 9) { e.Face(Engine.Facings[rng.Next(4)]); var u = e.UseChoice(); if (u != null) { e.Choose(u); continue; } }
                    var path = e.PathTo(goal.x, goal.y);
                    if (path != null && path.Count > 0)
                    {
                        e.Face(path[0]);
                        var walk = e.StepChoice(false);
                        if (walk != null) { e.Choose(walk); continue; }
                    }
                    if (opts.Count == 0) { e.Face(Engine.Facings[rng.Next(4)]); var w = e.StepChoice(false); if (w != null) e.Choose(w); else e.CatchBreath(); continue; }
                }
                if (opts.Count == 0)
                {
                    // Only allowed when side actions can fix it (out of stamina)
                    Assert(e.FreeToAct, "stuck with no choices in " + e.S.scene);
                    if (!e.CatchBreath()) throw new Exception("stuck in " + e.S.scene);
                    continue;
                }
                e.Choose(opts[rng.Next(opts.Count)].def);
                Rules.Clamp(e.S, e.data);
                Assert(!float.IsNaN(e.S.hp) && e.S.corruption >= 0 && e.S.corruption <= 100, "numbers out of range in " + e.S.scene);
            }
            if (village) villages++;
        }
        var never = NewEngine().SceneIds.Where(id => !reached.Contains(id)).ToList();
        Console.WriteLine("    random players reached Skelmed " + villages + " times out of 300; scenes never visited: " + (never.Count == 0 ? "none" : string.Join(", ", never)));
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>A new character, just woken in Yenson's hut and talking to him.</summary>
    static void ToHut(Engine e, int seed = 1)
    {
        e.NewGame("basic");
        e.S.seed = seed;
        e.CreateCharacter("Ash", "female", "Lean", "Red", "runner", new[] { 1, 1, 1, 1, 1 });
        Walk(e, "Go", "Sit up");
    }

    /// <summary>Yenson's jobs done (as if), supplied, and standing outside his door.</summary>
    static void ToRoad(Engine e, int seed = 1)
    {
        ToHut(e, seed);
        foreach (var f in new[] { "quest_done:water", "quest_done:firewood", "quest_done:boar", "boar_killed", "y_supplies", "yenson_road" }) e.S.flags.Add(f);
        e.S.AddItem("bread", 2);
        e.S.AddItem("flint", 1);
        e.S.water = 4;
        e.DevGear("gutting_knife", "F");
        Pick(e, "Goodbye");
        LeaveHut(e);
    }

    /// <summary>From anywhere on the coast road, back into the hut and talking to Yenson.</summary>
    static void BackToYenson(Engine e)
    {
        var door = e.Area.zones.First(z => z.to == "hut_door");
        WalkUntil(e, (door.x0, door.y0), () => e.Area.id == "yenson_hut");
        UseThing(e, "yenson");
    }

    /// <summary>Walks out of the hut's door (or, with back, from outside the door back in and up to Yenson).</summary>
    static void LeaveHut(Engine e, bool back = false)
    {
        if (back)
        {
            e.Face("up");
            e.Choose(e.StepChoice());
            UseThing(e, "yenson");
            return;
        }
        WalkUntil(e, (5, 7), () => e.Area.id == "coast_road");
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
