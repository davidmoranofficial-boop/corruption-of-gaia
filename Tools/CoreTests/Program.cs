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
        if (Environment.GetEnvironmentVariable("PERKS") == "1") { Test("perk balance", PerkBalance); return failures; }
        Test("content has no broken links", ContentIsValid);
        Test("full playthrough: hut, the wreck, Ida and the wolf, Bess, the hunt with Rook, Ida sends you to Eastmere", FullPlaythrough);
        Test("save and load round trip", SaveLoad);
        Test("hair styles and beards: chosen, saved, drawn", Looks);
        Test("dying in basic mode wakes you at your last sleep", BasicDeath);
        Test("dying in permadeath ends the game", PermaDeath);
        Test("running out of energy makes you collapse", Collapse);
        Test("running out of energy mid-fight kills you", CombatExhaustion);
        if (Environment.GetEnvironmentVariable("QUICK") != "1") Test("fights: reading tells wins, mashing loses", DogFights);
        Test("timing: perfect hits beat mistimed ones, parries block", Timing);
        Test("breath, potions, and Rook's levels, trait and abilities", PartyAndPotions);
        Test("gear ranks, traits and slots", GearRanks);
        Test("equipment slots: clothes, rings, two hands", Slots);
        Test("walking the coast road", Travel);
        Test("the road to Eastmere: Wren, Tobin, the deserters, the toll, Thornby", RoadToThornby);
        Test("the road's hard choices: leaving Tobin, informing on Sorrel, the ford", RoadHardChoices);
        Test("forging: how well you play sets the rank", Forging);
        Test("dev mode quick starts and cheats", DevMode);
        Test("levels, skills, moves and followers", Progression);
        Test("a party eats, drinks, tires and can walk out on you", Party);
        Test("wounds: treating them, letting them go bad, broken bones", Wounds);
        Test("the opening wolf bites you, and Bess shows you how to patch it up", FirstWound);
        Test("backgrounds give perks: berserk, light feet, healer, iron stomach, hedge magic", Perks);
        Test("weather: rain, cold, hypothermia, a chill", Weather);
        Test("companions can die, for good", CompanionDeath);
        Test("mana: spells use it, time and sleep bring it back", Mana);
        Test("dialogue: greetings that notice, questions marked as asked, checks that can fail, tones and bonds", Dialogue);
        Test("creation: origins, quirks, and stats that matter out in the world", Creation);
        Test("time: the clock runs standing still, and slowly", () =>
        {
            var e = NewEngine();
            e.DevStart("village");
            e.S.result = "";
            Assert(e.Travelling && e.FreeToAct, "free in the world: " + e.S.scene);
            int t0 = e.S.minutes;
            for (int i = 0; i < 60 * 30; i++) e.Tick(1f / 30f);   // a real minute, standing still
            Assert(e.S.minutes - t0 == 20, "a real minute is twenty game minutes: " + (e.S.minutes - t0));
            e.S.result = "Something to read.";
            int t1 = e.S.minutes;
            for (int i = 0; i < 300; i++) e.Tick(0.1f);
            Assert(e.S.minutes == t1, "reading stops the clock");
            Assert(24 * 60 * 60 / Rules.ClockRate / 60f >= 60, "a day lasts an hour of play or more");
        });
        Test("upkeep: blades dull, armour wears, whetstones and mending kits fix them so far", () =>
        {
            var e = AtDog(3);
            var blade = e.MakeGear("hunting_dagger", null, null);
            var coat = e.MakeGear("leather_jerkin", null, null);
            Assert(blade.condition == 100 && coat.condition == 100, "new gear is in good shape");
            bool struck = false;
            for (int i = 0; i < 8 && e.S.inCombat && e.S.hp > 0; i++)
            {
                e.Act(e.Choices().First(c => c.def.label == "Attack").def, Engine.TimingMiss);
                struck |= e.Events.Any(x => x.target == "player" && x.from == "enemy" && x.amount > 0);
            }
            Assert(blade.condition < 100, "fighting dulls the blade: " + blade.condition);
            Assert(!struck || e.S.Equipped.Any(g => g.guard > 0 && g.condition < 100), "taking hits wears what you're wearing");
            var v = NewEngine();
            v.DevStart("village");
            v.S.result = "";
            var d = v.MakeGear("hunting_dagger", null, null);
            float sharp = v.AttackPower();
            d.condition = 10;
            Assert(v.AttackPower() < sharp, "a blunt blade hits softer");
            v.S.items.RemoveAll(x => x.id == "whetstone");
            Assert(v.UpkeepBlocked(d) != null && !v.Upkeep(d.uid), "no whetstone, no sharpening");
            v.S.AddItem("whetstone", 1);
            int t0 = v.S.minutes;
            Assert(v.Upkeep(d.uid) && d.condition == Rules.FieldSharpen && v.S.minutes > t0 && v.S.Count("whetstone") == 1, "sharpened, as far as a whetstone goes, and it's kept");
            Assert(v.UpkeepBlocked(d) != null, "past that needs a grindstone");
            var j = v.MakeGear("leather_jerkin", null, null);
            j.condition = 20;
            float guard = Rules.GearGuard(v.S, v.data);
            v.S.result = "";
            v.S.AddItem("mending_kit", 1);
            Assert(v.Upkeep(j.uid) && j.condition == Rules.FieldMend && v.S.Count("mending_kit") == 0, "a mending kit is used up");
            Assert(Rules.GearGuard(v.S, v.data) > guard, "mended armour blocks more");
            var back = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(v.S));
            Assert(back.gearItems.First(x => x.uid == j.uid).condition == Rules.FieldMend, "condition survives a save");
            var old = JsonConvert.DeserializeObject<GearItem>("{\"id\":\"knife\",\"damage\":2}");
            Assert(old.condition == 100, "old saves' gear starts in good shape");
        });
        Test("money: copper, silver, gold, platinum", () =>
        {
            Assert(Rules.Money(5) == "5 copper" && Rules.Money(100) == "1 silver" && Rules.Money(12345) == "1 gold 23 silver 45 copper", Rules.Money(12345));
            Assert(Rules.Money(Rules.Platinum + 1, true) == "1p 1c" && Rules.Money(0) == "no money", "short form and empty purse");
            var e = NewEngine();
            e.DevStart("village");
            var old = new GameState { coins = 7, hud = true, scene = e.S.scene, area = e.S.area, ax = e.S.ax, ay = e.S.ay };
            e.LoadState(old);
            Assert(e.S.coins == 140, "an old save's coins are scaled to the new prices");
        });
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

    static void Looks()
    {
        var e = NewEngine();
        e.NewGame("basic");
        Assert(e.data.file.hairStyles.Count >= 10 && e.data.file.beards.Contains("None"), "there should be hair styles and beards to choose");
        Assert(e.CreateCharacter("Ash", "male", "Lean", "Auburn", "farmer", new[] { 1, 1, 1, 1, 1 }, "Fair", "male", "Topknot", "Stubble") == null, "valid character refused");
        Assert(e.S.hairStyle == "Topknot" && e.S.beard == "Stubble" && e.S.hair == "Auburn", "the chosen look should be kept");
        var json = JsonConvert.SerializeObject(e.S);
        var back = JsonConvert.DeserializeObject<GameState>(json);
        Assert(back.hairStyle == "Topknot" && back.beard == "Stubble", "the look should survive a save");
        var old = new GameState { hair = "Shaved" };
        old.Upgrade();
        Assert(old.hair == "Dark brown" && old.hairStyle == "Cropped", "old saves with shaved hair should get cropped hair");

        // every choice has its art, for the doll and the walking sprite
        string spr = Path.Combine(Root, "Assets", "Game", "Resources", "Sprites");
        string K(string v) => v.Trim().ToLowerInvariant().Replace(' ', '_');
        foreach (var st in e.data.file.hairStyles)
            foreach (var hc in e.data.file.hair)
            {
                if (K(st) == "bald") continue;
                foreach (var cap in new[] { "", "_cap" })
                {
                    Assert(File.Exists(Path.Combine(spr, "Walk", "hair_" + K(st) + "_" + K(hc) + cap + ".png")), "no walking art for " + st + " " + hc + cap);
                    Assert(File.Exists(Path.Combine(spr, "Hair", "hair_" + K(st) + "_" + K(hc) + cap + ".png")), "no doll art for " + st + " " + hc + cap);
                }
            }
        foreach (var b in e.data.file.beards)
            foreach (var hc in e.data.file.hair)
                if (K(b) != "none")
                    Assert(File.Exists(Path.Combine(spr, "Walk", "beard_" + K(b) + "_" + K(hc) + ".png")) && File.Exists(Path.Combine(spr, "Hair", "beard_" + K(b) + "_" + K(hc) + ".png")),
                           "no art for a " + b + " in " + hc);
        foreach (var sk in e.data.file.skins)
            foreach (var fig in new[] { "male", "female" })
                Assert(File.Exists(Path.Combine(spr, "Walk", "body_" + fig + "_" + K(sk) + ".png")), "no walking body for " + fig + " " + sk);
        foreach (var g in e.data.file.gear)
            if (g.slot != "ring")
                Assert(File.Exists(Path.Combine(spr, "WalkGear", g.id + "_male.png")) && File.Exists(Path.Combine(spr, "WalkGear", g.id + "_female.png")),
                       g.id + " doesn't show on the walking sprite");
    }

    static void FullPlaythrough()
    {
        var e = NewEngine();
        e.NewGame("basic");
        Assert(e.S.scene == "creation", "a new game should start at character creation");
        Assert(e.CreateCharacter("", "female", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 }) != null, "empty name should be refused");
        Assert(e.CreateCharacter("Ash", "female", "Lean", "Red", "farmer", new[] { 2, 2, 1, 1, 1 }) != null, "too many points should be refused");
        Assert(e.CreateCharacter("Ash", "female", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 }) == null, "valid character refused");
        Assert(e.S.stats.All(v => v == 4) && e.S.background == "farmer", "backgrounds give a perk, not a stat point");
        Assert(e.CreateCharacter("Ash", "", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 }) != null, "sex must be chosen");
        Assert(e.S.stats.Length == 5 && e.S.stats[Stat.Cha] == 4, "charisma should exist and take points");
        Assert(e.Fill("{they} nods at {them}") == "she nods at her", "pronouns should follow the chosen sex");
        Pick(e, "Go");
        Assert(e.S.scene == "shipwreck" && e.Current.cutscene == "shipwreck" && e.Current.text.Count == 7, "the shipwreck cutscene should play after creation");
        Pick(e, "Wake");
        Assert(e.S.scene == "wake" && e.S.hud && e.Area?.id == "yenson_hut" && AtSpot(e, "hut_bed"), "should wake in Yenson's hut, by the bed");
        Assert(Math.Abs(e.S.corruption - 5) < 0.01, "corruption should start at 5%");
        Assert(e.S.lastSleep != "", "waking in the hut should count as a save");
        Pick(e, "Sit up");
        Assert(e.S.scene == "yenson" && e.Current.speaker == "Yenson", "Yenson should be talking");
        Pick(e, "Where am I");
        Assert(e.S.result.Contains("Loran"), "his answer should show");
        Pick(e, "Eat the stew");
        Pick(e, "What should I do now");
        Assert(e.Check("flag:y_road") && e.S.water == 4 && !e.ActiveQuests.Any(), "Yenson should send you to Skelmed, no jobs");
        Assert(e.Journal().Any(j => j.Contains("Skelmed") && j.Contains("beach")), "the journal should point to Skelmed and the wreck");
        Pick(e, "Goodbye");
        Assert(e.Travelling && e.Area.id == "yenson_hut", "should be free to walk round the hut");

        // Sleep in the bed
        e.Face("left");
        Assert(e.UseLabel() == "Look", "facing the bed you can use it");
        e.Choose(e.UseChoice());
        Pick(e, "Sleep until morning");
        Assert(e.S.Day == 2 && e.S.Hour == 6 && e.Travelling, "should wake on day 2 at 06:00, was day " + e.S.Day + " " + Rules.Clock(e.S));

        // The wreck of your boat, on the beach north of the shack
        LeaveHut(e);
        Assert(e.Area.id == "coast_road" && AtSpot(e, "shack"), "the door should put you outside the shack");
        e.S.stepsSinceFight = -100000;   // this run is about the story; random fights are tested on their own
        int t0 = e.S.minutes;
        UseThing(e, "wreck_crate");
        Assert(e.Travelling && string.IsNullOrEmpty(e.S.result) && e.S.Count("bread") == 2 && e.S.Toasts.Any(t => t.Contains("+2")), "picking things up is instant: just +2 Bread, no page of text, was " + e.S.scene + " [" + e.S.result + "] " + string.Join("/", e.S.Toasts));
        UseThing(e, "wreck_chest");
        UseThing(e, "wreck_barrel");
        Keep(e); UseTile(e, 'Q');
        Assert(e.S.Count("bread") == 2 && e.S.Count("flint") == 1 && e.S.Count("fish") == 2 && e.S.Count("spirits") == 1 && e.S.coins >= 30
               && e.S.gearItems.Any(g => g.id == "gutting_knife" && !g.equipped), "the wreck: bread, a flint, fish, spirits, coin and a knife");
        Assert(e.Objects().All(o => !o.id.StartsWith("wreck_")), "what you've searched is gone");
        Assert(e.Equip(e.S.gearItems.First(g => g.id == "gutting_knife").uid) && e.S.InSlot("weapon")?.id == "gutting_knife", "you can take up the knife");
        Console.WriteLine("    the wreck took " + Engine.Duration(e.S.minutes - t0));

        // The wolf in the road, and Ida
        WalkUntil(e, Gate(e), () => e.S.scene == "wolf_rescue");
        Assert(e.Current.script.Contains("lie:you") && e.Current.script.Any(x => x.StartsWith("say:Ida:")) && e.Check("untreated:bite") && e.Check("flag:ida_saved"),
               "the wolf should be acted out on the road, and leave a bite");
        Assert(e.S.body.afflictions.First(x => x.id == "bite").from.Contains("wolf"), "the wolf did it");
        Pick(e, "Get up");
        Assert(e.Travelling && e.Objects().All(o => o.id != "wolf"), "the wolf is gone from the road");
        Assert(e.Journal().Any(j => j.Contains("Ida") && j.Contains("bite")), "the journal should send you to get the bite seen to");
        WalkUntil(e, Gate(e), () => e.Area.id == "skelmed");
        Assert(e.Travelling && AtSpot(e, "village_gate"), "the gate should take you into Skelmed");

        // Bess sees to the bite
        e.S.energy = 80; e.S.hunger = 80; e.S.thirst = 80;
        WalkUntil(e, (17, 20), () => e.S.scene == "bess_patch");
        Pick(e, "see to the bite");
        Assert(e.Treat("", "bite", "clean") && e.Treat("", "bite", "bandage") && e.Check("flag:patch_done"), "clean it and bind it");
        e.S.result = "";
        Assert(e.Journal().Any(j => j.Contains("Ida") && j.Contains("help")), "the journal should send you to Ida");

        // Ida sends you out with Rook: deadwood and the boar
        foreach (var food in e.S.items.Where(i => e.data.Item(i.id).food).Select(i => i.id).ToList()) e.Eat(food);
        int t1 = e.S.minutes;
        Keep(e); UseThing(e, "ida");
        Assert(e.S.scene == "ida" && e.Current.speaker == "Ida", "Ida should talk");
        Pick(e, "Thank you");
        Pick(e, "How can I help");
        Assert(e.S.scene == "ida_send" && e.Current.script.Any(x => x.StartsWith("walk:rook")), "Ida calls Rook over (acted out)");
        Pick(e, "Go with Rook");
        Assert(e.Travelling && e.Check("quest:hunt") && e.Member("rook") != null && e.Objects().All(o => o.id != "rook"), "Rook walks with you now");
        Assert(!e.SceneIds.Any(id => id == "ida_forest" || id == "chop_tree1" || id == "workstump") && e.data.Quest("forest") == null, "Ida's forest lessons are gone");
        int lv0 = e.S.level;
        Keep(e); ToArea(e, "hills_gate");
        foreach (var w in new[] { "hwood1", "hwood2", "hwood3" }) { Keep(e); UseThing(e, w); Assert(e.Travelling, "deadwood is picked up straight away"); }
        Assert(e.QuestGoals(e.data.Quest("hunt"))[0].met && e.S.Count("firewood") >= 3, "three bundles of deadwood");
        Assert(e.Objects().Any(o => o.id == "boar"), "the boar's in its glade");
        Keep(e);
        var boar = e.Area.objects.First(o => o.id == "boar");
        WalkUntil(e, (boar.x + 1, boar.y), () => e.S.scene == "boar");
        if (e.S.stamina < 80) e.CatchBreath();
        Pick(e, "Fight it");
        Assert(e.S.inCombat && e.S.enemy == "boar", "the boar should fight");
        int rounds = 0;
        for (; rounds < 120 && e.S.inCombat; rounds++) StepToward(e, (0, 0));
        Assert(e.S.scene == "boar_won" && e.S.Count("raw_boar") == 2 && e.S.Count("boar_hide") == 1 && e.S.Count("sinew") == 1,
               "beat the boar with Rook and butcher it, was " + e.S.scene + " " + e.S.deathCause);
        Console.WriteLine("    the boar took " + rounds + " rounds, hp " + e.S.hp + ", Rook " + e.Member("rook").hp);
        Pick(e, "Carry it");
        Assert(e.Check("ready:hunt"), "wood and meat: ready to go home");
        Assert(new[] { "woodcutting", "hunting" }.All(k => e.SkillLevel(k) >= 1 || e.SkillProgress(k) > 0), "the hunt grows your skills");

        // Back at the lodge: Ida's thanks, and Eastmere
        Keep(e); ToArea(e, "skelmed_north");
        Keep(e); UseThing(e, "ida");
        Pick(e, "We're back");
        Assert(e.S.scene == "ida_home" && e.Check("done:hunt"), "Ida takes it all in (acted out)");
        Assert(e.Current.script.Any(x => x.Contains("Things are getting worse around here. If we don't get help soon, this village is done for.")), "Ida's words to Rook");
        Pick(e, "Go with Rook to Eastmere");
        Assert(new[] { "leather_jerkin", "leather_boots", "hunting_dagger" }.All(g => e.S.gearItems.Any(x => x.id == g)), "leather gear and a dagger from Ida");
        Assert(e.Check("flag:rook_escort") && e.Check("quest:eastmere") && e.Member("rook") != null && e.S.level > lv0, "off to Eastmere with Rook, a level up");
        Assert(e.Journal().Any(j => j.Contains("north gate")), "the journal says where Rook's waiting");
        Keep(e); UseThing(e, "ida");
        Assert(e.Paragraphs().Any(x => x.Contains("Look after him")), "Ida sees you off");
        Leave(e);
        Assert(!e.SceneIds.Any(id => id == "orlen" || id == "quarry_dig" || id == "ida_pay" || id == "chop" || id == "fire1"), "the old jobs are gone");
        Assert(e.data.file.areas.All(ar => ar.objects.All(o => o.id != "garran" && o.id != "orlen")), "no smith or mage in Skelmed");
        Console.WriteLine("    Skelmed took " + Engine.Duration(e.S.minutes - t1) + ", level " + e.S.level + ", rep " + e.S.Rep("skelmed"));

        // The north gate: up the lane into the hills, and east onto the road
        Keep(e);
        WalkUntil(e, (12, 1), () => e.S.scene == "north_gate");
        Assert(e.Check("flag:left_skelmed") && e.Current.script.Any(x => x.StartsWith("fade:out")), "Rook and you set off at the north gate");
        Assert(!e.Current.script.Any(x => x.Contains("ends for now")), "the opening doesn't end at the gate any more");
        Pick(e, "Set off up the north lane");
        Assert(e.Travelling && e.Area.id == "skelmed_hills" && AtSpot(e, "hills_gate"), "out into the hills");
        Assert(e.ActiveQuests.Count() == 1 && e.FinishedQuests.Count() == 1, "the hunt's done; Eastmere's ahead");
        Assert(e.Objects().All(o => o.id != "east_trunk"), "the old track east is open once you've left");
        Keep(e); ToArea(e, "fr_west");
        Assert(e.Area.id == "forest_road" && e.Member("rook") != null, "Rook and you on the forest road");

        // and it's all walkable back the other way
        Keep(e); ToArea(e, "hills_east");
        Keep(e); ToArea(e, "skelmed_north");
        Assert(e.Travelling && e.Area.id == "skelmed" && e.S.scene == "travel", "back through the north gate with no scene the second time");
        WalkUntil(e, (17, 27), () => e.Area.id == "coast_road");
        Assert(AtSpot(e, "skelmed"), "leaving should put you outside the gate");
        Assert(!e.SceneIds.Any(id => id == "village" || id == "bedroom" || id == "inn" || id == "guild" || id == "mira"), "the old text pages should be gone");
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
        Assert(e.SkillLevel("dagger") == 2 && !e.UsableMoves().Any(m => m.id == "quick_stab"), "dagger 2 makes Quick Stab learnable, not known");
        Assert(e.S.skillPoints == 1, "the level up gave a skill point");
        var stab = e.data.Move("quick_stab");
        Assert(e.CanLearn(stab, out _) && e.LearnMove("quick_stab") && e.S.skillPoints == 0, "spend it on Quick Stab");
        Assert(e.UsableMoves().Any(m => m.id == "quick_stab"), "now you can use it");
        Assert(!e.CanLearn(stab, out var why) && why == "Learned", "and can't buy it twice");

        // Old saves keep the moves their skills had unlocked
        var old = NewEngine();
        old.DevStart("village");
        old.TrainSkill("dagger", 200, "test");
        var snap = new GameState { movesLearned = false, skills = old.S.skills, hud = true, scene = old.S.scene, area = old.S.area, ax = old.S.ax, ay = old.S.ay };
        old.LoadState(snap);
        Assert(old.S.moves.Contains("quick_stab"), "an old save keeps Quick Stab");
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
        m.S.moves.Add("quick_stab");
        m.S.moves.Add("aimed_shot");
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
        Assert(b.S.enemyHp < ehp || b.S.pinUsed || !b.S.inCombat, "Rook should hit (or pin) the pack leader even when you defend");
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

    static void Wounds()
    {
        var e = NewEngine();
        e.DevStart("village");
        e.S.devWeather = "overcast";
        // A bite left alone goes bad
        Rules.Afflict(e.S, e.data, e.S.body, "bite", null);
        float hp0 = e.S.hp;
        Rules.Advance(e.S, e.data, 60, false);
        Assert(e.S.hp < hp0, "a bite bleeds");
        Rules.Advance(e.S, e.data, 12 * 60, false);
        Assert(!Rules.Has(e.S.body, "bite") && Rules.Has(e.S.body, "infection"), "an unclean bite turns into an infection");
        e.S.hp = Rules.MaxHp(e.S);
        e.DevMedicine();
        e.S.result = "";
        Assert(e.Treat("", "infection", "medicine"), "willow bark brings the fever down");
        e.Choose(Ch("sleep:safe"));
        Assert(e.S.body.afflictions.All(a => a.id != "infection" || a.done.Contains("rest")), "sleeping counts as rest");

        // A bite cleaned and bound heals
        var b = NewEngine();
        b.DevStart("village");
        b.S.devWeather = "overcast";
        Rules.Afflict(b.S, b.data, b.S.body, "bite", null);
        b.DevMedicine();
        int spirits = b.S.Count("spirits");
        Assert(b.Treat("", "bite", "clean") && b.S.Count("spirits") == spirits - 1, "spirits clean a bite");
        b.S.result = "";
        Assert(b.Treat("", "bite", "bandage"), "and a bandage binds it");
        float hp1 = b.S.hp;
        Rules.Advance(b.S, b.data, 60, false);
        Assert(b.S.hp >= hp1, "a bound wound stops bleeding");
        Rules.Advance(b.S, b.data, 40 * 60, false);
        Assert(!Rules.Has(b.S.body, "bite") && !Rules.Has(b.S.body, "infection"), "a treated bite heals in time");
        Assert(b.SkillLevel("first_aid") >= 0 && b.S.skills.Any(k => k.id == "first_aid"), "treating wounds trains First aid");

        // A broken leg: no running, slow walking, no getting away
        var l = NewEngine();
        l.DevStart("village");
        float walk = l.StepSeconds(false);
        Rules.Afflict(l.S, l.data, l.S.body, "broken_leg", null);
        var run = l.StepChoice(true);
        Assert(run == null || (run.label == "Walk" && l.StepSeconds(false) >= walk * 2), "a broken leg can't run and walks slowly");
        Assert(!l.TreatmentCost("splint").ok, "no splint, no bandage, no splinting");
        l.S.AddItem("split_wood", 1); l.S.AddItem("bandage", 1);
        Assert(l.TreatmentCost("splint").ok, "a split log and a bandage make a splint");
        l.S.result = "";
        Assert(l.Treat("", "broken_leg", "splint") && l.S.Count("split_wood") == 0 && l.S.Count("bandage") == 0, "splinting uses them up");
        Assert(Rules.NoRun(l.S.body, l.data), "still no running while it knits");

        // Stitching needs a sewing kit, which isn't used up
        var g = NewEngine();
        g.DevStart("village");
        Rules.Afflict(g.S, g.data, g.S.body, "gash", null);
        Assert(!g.TreatmentCost("stitch").ok, "no kit, no stitches");
        g.S.AddItem("sewing_kit", 1);
        g.S.result = "";
        Assert(g.Treat("", "gash", "stitch") && g.S.Count("sewing_kit") == 1, "stitching with the kit keeps the kit");

        // Fights leave wounds (counted mid-fight: the opening wolf always bites you by the end, see below)
        int wounded = 0;
        for (int seed = 1; seed <= 40; seed++)
        {
            var f = NewEngine();
            f.DevStart("dog");
            f.S.seed = seed;
            bool hurt = false;
            for (int i = 0; i < 100 && f.S.inCombat; i++) { StepToward(f, (0, 0)); hurt |= f.S.inCombat && f.S.body.afflictions.Any(); }
            if (hurt) wounded++;
        }
        Console.WriteLine("    wolf fights that left a wound: " + wounded + " of 40");
        Assert(wounded >= 4 && wounded <= 36, "wolf fights should sometimes leave a wound, not always");

        // A concussion makes blows go astray
        var c = NewEngine();
        c.DevStart("dog");
        Rules.Afflict(c.S, c.data, c.S.body, "concussion", null);
        int dizzy = 0;
        for (int i = 0; i < 30 && c.S.inCombat; i++)
        {
            c.S.enemyHp = 999;
            c.Act(c.Choices().First(x => x.def.label == "Attack").def, Engine.TimingGood);
            if (c.S.battleLog.Any(x => x.Contains("world swims"))) dizzy++;
            c.S.hp = Rules.MaxHp(c.S); c.S.stamina = 100; c.S.energy = 100;
        }
        Assert(dizzy > 0, "a concussion should make you miss sometimes");
    }

    static void Weather()
    {
        var e = NewEngine();
        e.DevStart("road");
        // The weather is the same every time for the same moment, and it changes over days
        var seen = new HashSet<string>();
        for (int m = 0; m < 20 * 1440; m += 240) seen.Add(Rules.WeatherAt(e.S, e.data, m).id);
        Assert(seen.SetEquals(new[] { "clear", "fog", "rain" }), "the coast has clear days, fog and rain, nothing else: " + string.Join(", ", seen));
        int changes = 0;
        string last = null;
        for (int m = 0; m < 20 * 1440; m += 60)
        {
            var w = Rules.WeatherAt(e.S, e.data, m).id;
            if (w == "fog") w = "clear";   // fog lifting in the morning isn't a change of weather
            if (last != null && w != last) changes++;
            last = w;
        }
        Console.WriteLine("    weather changes in 20 days: " + changes);
        Assert(changes <= 25, "the weather shouldn't change every few hours: " + changes + " changes in 20 days");
        foreach (var m in Enumerable.Range(0, 20 * 24).Select(h => h * 60))
            if (Rules.WeatherAt(e.S, e.data, m).id == "fog") Assert((m % 1440) / 60 >= 3 && (m % 1440) / 60 < 11, "fog only in the morning");
        int summerNoon = 12 * 60, winterNoon = (Rules.SeasonDays * 2 + 1) * 1440 + 12 * 60;
        Assert(Rules.Season(summerNoon) == "summer" && Rules.Season(winterNoon) == "winter", "the game starts in summer and winter comes");
        e.S.devWeather = "clear";
        Assert(Rules.AirTemp(e.S, e.data, summerNoon) > Rules.AirTemp(e.S, e.data, winterNoon) + 10, "summer is warmer than winter");
        Assert(Rules.AirTemp(e.S, e.data, summerNoon + 3 * 60) > Rules.AirTemp(e.S, e.data, summerNoon + 16 * 60), "afternoons are warmer than the small hours");
        e.S.devWeather = "";
        Assert(Rules.WeatherAt(e.S, e.data, 5000).id == Rules.WeatherAt(e.S, e.data, 5000).id, "weather is fixed by the seed");
        Assert(Rules.AirTemp(e.S, e.data, 1440 + 15 * 60) > Rules.AirTemp(e.S, e.data, 1440 + 4 * 60) || e.S.devWeather != "", "afternoons are warmer than before dawn");

        // A winter storm at night, in castaway clothes: wet, cold, then hypothermia
        e.S.devWeather = "storm";
        e.S.minutes = (Rules.SeasonDays * 2 + 1) * 1440 + 21 * 60;
        e.S.body = new Body();
        Rules.Advance(e.S, e.data, 60, false);
        Assert(e.S.body.wet >= Rules.WetAt, "a storm soaks you");
        for (int i = 0; i < 12 && !Rules.Has(e.S.body, "hypothermia"); i++) { Rules.Advance(e.S, e.data, 60, false); e.S.hp = 100; e.S.energy = 100; e.S.hunger = 100; e.S.thirst = 100; }
        Assert(Rules.Has(e.S.body, "hypothermia"), "hours in a night storm bring on hypothermia, cold " + e.S.body.cold);
        Assert(Rules.FightPower(e.S.body, e.data) < 0.6f, "hypothermia makes you fight badly");

        // A fire warms you and dries you; hypothermia goes
        e.S.AddItem("flint", 1);
        e.S.result = "";
        e.Choose(Ch("sleep:camp"));
        Assert(e.S.body.wet == 0 && !Rules.Has(e.S.body, "hypothermia"), "a night by a fire dries you and warms you, cold " + e.S.body.cold);

        // Sleeping out in the rain, no fire, catches you a chill
        var r = NewEngine();
        r.DevStart("road");
        r.S.devWeather = "rain";
        r.DevTime(21);
        r.S.items.RemoveAll(i => i.id == "flint");
        r.S.result = "";
        r.Choose(Ch("sleep:camp"));
        Assert(Rules.Has(r.S.body, "chill") || Rules.Has(r.S.body, "hypothermia"), "a wet night in the open makes you ill");

        // A bed puts it all right
        var bed = NewEngine();
        bed.DevStart("village");
        bed.S.body.cold = 90; bed.S.body.wet = 90;
        bed.S.result = "";
        bed.Choose(Ch("sleep:safe"));
        Assert(bed.S.body.cold == 0 && bed.S.body.wet == 0, "a bed: warm and dry");

        // Good clothes keep the cold out
        var warm = NewEngine();
        warm.DevStart("road");
        warm.S.devWeather = "rain";
        warm.S.minutes = (Rules.SeasonDays * 2 + 1) * 1440 + 21 * 60;
        warm.DevGear("leather_jerkin", "F"); warm.DevGear("leather_boots", "F"); warm.DevGear("gloves", "F"); warm.DevGear("iron_cap", "F");
        var bare = NewEngine();
        bare.DevStart("road");
        bare.S.devWeather = "rain";
        bare.S.minutes = (Rules.SeasonDays * 2 + 1) * 1440 + 21 * 60;
        Rules.Advance(warm.S, warm.data, 3 * 60, false);
        Rules.Advance(bare.S, bare.data, 3 * 60, false);
        Assert(warm.S.body.cold < bare.S.body.cold, "warm clothes keep you warmer: " + warm.S.body.cold + " vs " + bare.S.body.cold);
    }

    static void Mana()
    {
        var e = NewEngine();
        e.DevStart("village");
        e.DevSpells();
        e.S.result = "";
        int max = Rules.MaxMana(e.S);
        Assert(max >= 30 && e.S.mana == max, "you start with full mana (" + e.S.mana + "/" + max + ")");
        e.S.water = 0;
        Assert(e.DrawWater() && e.S.water == Rules.WaterskinSize, "Draw Water fills the skin");
        Assert(e.S.mana <= max - Rules.DrawWaterMana + 1, "and costs mana");
        e.S.result = "";
        e.S.mana = 2;
        Assert(!e.DrawWater(), "not without the mana for it");
        Rules.Advance(e.S, e.data, 60, false);
        Assert(e.S.mana > 2 && e.S.mana < max, "mana comes back slowly with time");
        Rules.Sleep(e.S, e.data, 8 * 60, 100);
        Assert(e.S.mana == Rules.MaxMana(e.S), "and fully with sleep");

        // An old save without mana starts full
        e.S.mana = -1;
        Rules.Clamp(e.S, e.data);
        Assert(e.S.mana == Rules.MaxMana(e.S), "old saves get full mana");
    }

    static void CompanionDeath()
    {
        // Down in a fight, and left untreated: dead, for good
        var e = NewEngine();
        e.DevStart("boss");
        var rook = e.Member("rook");
        rook.hp = 0;
        Rules.Afflict(e.S, e.data, rook.body, "grave", "Rook");
        Rules.Advance(e.S, e.data, 7 * 60, false);
        Assert(e.Member("rook") == null && e.S.fallen.Contains("rook") && e.Check("flag:dead_rook"), "an untreated grave wound kills");
        Assert(e.Check("!follower:rook") && e.Objects().All(o => o.id != "rook"), "Rook is gone, and not back at his snares");

        // Bandaged in time: back on their feet
        var s = NewEngine();
        s.DevStart("boss");
        var r2 = s.Member("rook");
        r2.hp = 0;
        Rules.Afflict(s.S, s.data, r2.body, "grave", "Rook");
        s.DevMedicine();
        s.S.result = "";
        Assert(s.Treat("rook", "grave", "bandage") && r2.hp > 0, "a bandage gets Rook back up");
        Rules.Advance(s.S, s.data, 8 * 60, false);
        Assert(s.Member("rook") != null, "and he lives");
        Assert(Rules.NoFight(r2.body, s.data), "but he can't fight until it heals");

        // Killed outright by a huge blow, or starved: dead too
        var k = NewEngine();
        k.DevStart("boss");
        var r3 = k.Member("rook");
        r3.hunger = 0; r3.thirst = 0; r3.hp = 3;
        Rules.Advance(k.S, k.data, 2 * 60, false);
        Assert(k.Member("rook") == null && k.S.fallen.Contains("rook"), "starving to death kills a companion");

        // Ida, if Rook died on the hunt
        var i = NewEngine();
        i.DevStart("village");
        foreach (var f in new[] { "quest:hunt", "hunt_wood1", "hunt_wood2", "hunt_wood3", "boar_killed", "dead_rook" }) i.S.flags.Add(f);
        Keep(i); UseThing(i, "ida");
        Assert(i.Choices().Any(c => c.enabled && c.def.label.Contains("Rook isn't")) && !i.Choices().Any(c => c.enabled && c.def.label.Contains("Wood, and a boar")), "Ida hears Rook's dead");
        Pick(i, "Rook isn't");
        Assert(i.Check("done:hunt") && i.Check("quest:eastmere") && i.S.gearItems.Any(g => g.id == "hunting_dagger"), "the job's still done, and Eastmere's still ahead");
        Leave(i);
        WalkUntil(i, (12, 1), () => i.S.scene == "north_gate_alone");
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
        e.CreateCharacter("Rook", "male", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 });
        Walk(e, "Go", "Wake", "Sit up");
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
        e.S.energy = 0.005f;
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

    /// <summary>One round as someone who reads the tells: dodge the big ones, block the feints and quick ones,
    /// hit it when it's open or howling, get your breath back while it holds off. Their presses are good, not perfect.</summary>
    static void SmartRound(Engine e, int salt)
    {
        var opts = e.Choices().Where(c => c.enabled).ToDictionary(c => c.def.label, c => c.def);
        Loran.Core.ChoiceDef Get(string l) => opts.TryGetValue(l, out var d) ? d : null;
        string kind = e.Intent.KindOf();
        bool winded = Rules.Winded(e.S, e.data);
        var pick =
            e.S.enemyOpen > 0 || kind == "howl" ? Get("Attack")
          : kind == "heavy" ? (winded ? null : Get("Dodge"))
          : kind == "feint" || kind == "guard" ? Get("Defend")
          : winded || e.S.stamina < 40 ? Get("Defend")
          : Get("Attack");
        pick ??= Get("Defend");
        int r = (int)((salt * 7919L + e.S.round * 104729L + (long)e.S.enemyHp * 31) % 100);
        if (pick.label == "Dodge" && Rules.HasPerk(e.S, e.data, "nimble")) r = r * 3 / 4;   // Nimble's wider dodge zone: fewer misses
        e.Act(pick, r < 30 ? Engine.TimingPerfect : r < 82 ? Engine.TimingGood : Engine.TimingMiss);
    }

    /// <summary>One round as a button-masher: attack whenever there's breath for it, otherwise block. Presses all over the place.</summary>
    static void MashRound(Engine e, int salt)
    {
        var opts = e.Choices().Where(c => c.enabled).ToList();
        var pick = opts.FirstOrDefault(c => c.def.label == "Attack") ?? opts.First(c => c.def.label == "Defend");
        int t = (salt + e.S.round) % 6;
        e.Act(pick.def, t == 0 ? Engine.TimingPerfect : t <= 2 ? Engine.TimingGood : Engine.TimingMiss);
    }

    static (int wins, int deaths, float lost) Simulate(Func<int, Engine> start, Action<Engine, int> play, string win, int n)
    {
        int wins = 0, deaths = 0;
        float lost = 0, rounds = 0;
        for (int seed = 0; seed < n; seed++)
        {
            var e = start(seed);
            float hp0 = e.S.hp;
            for (int turn = 0; turn < 80 && e.S.inCombat; turn++) play(e, seed);
            Assert(!e.S.inCombat, "a fight should end within 80 rounds");
            rounds += e.S.round;
            lost += hp0 - Math.Max(0, e.S.hp);
            if (e.S.scene == win) wins++;
            else if (e.S.scene == "death") deaths++;
        }
        Console.WriteLine("      " + wins + "/" + n + " won, " + deaths + " died, " + (rounds / n).ToString("0.0") + " rounds, " + (lost / n).ToString("0") + " health lost on average");
        return (wins, deaths, lost / n);
    }

    static void Perks()
    {
        Assert(NewEngine().data.file.backgrounds.All(b => b.perk != "" && b.perkName != "" && b.perkDesc != "" && b.stat == ""), "every background has a perk, and no stat bonus");

        // Berserk: badly hurt, harder blows
        var a = AtDog(3); a.S.background = "soldier";
        var b = AtDog(3); b.S.background = "soldier";
        b.S.hp = Rules.MaxHp(b.S) * 0.2f;
        a.S.enemyMove = b.S.enemyMove = "wary";
        float ea = a.S.enemyHp, eb = b.S.enemyHp;
        a.Act(a.Choices().First(c => c.def.label == "Attack").def, Engine.TimingGood);
        b.Act(b.Choices().First(c => c.def.label == "Attack").def, Engine.TimingGood);
        Assert(b.Berserk && !a.Berserk && (eb - b.S.enemyHp) >= (ea - a.S.enemyHp) * 1.2f, "berserk hits harder when you're badly hurt");

        // Nimble: dodging is free
        var l = AtDog(3); l.S.background = "thief";
        Assert(l.Choices().First(c => c.def.label == "Dodge").def.stamina == Rules.NimbleDodge, "a thief dodges for half the stamina");

        // Deadeye: more crits
        var k1 = AtDog(3); k1.S.background = "hunter";
        var k2 = AtDog(3);
        Assert(Rules.CritChance(k1.S, k1.data) == Rules.CritChance(k2.S, k2.data) + Rules.DeadeyeCrit, "a hunter crits more often");

        // Healer: treating takes half the time
        var h = NewEngine(); h.DevStart("village"); h.S.background = "herbalist";
        Rules.Afflict(h.S, h.data, h.S.body, "bite", null);
        h.S.AddItem("bandage", 1);
        int m0 = h.S.minutes;
        h.S.result = "";
        Assert(h.Treat("", "bite", "bandage") && h.S.minutes - m0 == 5, "bandaging takes 5 minutes, not 10");

        // Hardy: hunger goes down slower
        var f1 = NewEngine(); f1.DevStart("village"); f1.S.background = "farmer";
        var f2 = NewEngine(); f2.DevStart("village"); f2.S.background = "scholar";
        f1.S.hunger = f2.S.hunger = 90;
        Rules.Advance(f1.S, f1.data, 240, false); Rules.Advance(f2.S, f2.data, 240, false);
        Assert(f1.S.hunger > f2.S.hunger + 0.5f, "a farmer gets hungry slower");

        // Studious: learns faster
        var w1 = NewEngine(); w1.DevStart("village"); w1.S.background = "scholar";
        var w2 = NewEngine(); w2.DevStart("village"); w2.S.background = "farmer";
        float x1 = w1.S.xp[Stat.Str], x2 = w2.S.xp[Stat.Str];
        Rules.Train(w1.S, Stat.Str, 1f, ""); Rules.Train(w2.S, Stat.Str, 1f, "");
        Assert(Math.Abs((w1.S.xp[Stat.Str] - x1) - (w2.S.xp[Stat.Str] - x2) * Rules.StudiousRate) < 0.01f, "a scholar learns faster");
    }

    static void PartyAndPotions()
    {
        // Stamina comes back by itself out of a fight, but not while running
        var e = NewEngine(); e.DevStart("village");
        e.S.stamina = 10;
        Assert(e.Breathe(2f, false) && Math.Abs(e.S.stamina - (10 + 2 * Rules.BreathPerSecond)) < 0.01f || e.S.body.cold >= Rules.ColdAt, "your breath comes back by itself");
        float st = e.S.stamina;
        Assert(!e.Breathe(2f, true) && e.S.stamina == st, "not while you run");

        // A potion heals you, and gets a fallen companion back up
        e.S.hp = 20;
        e.S.AddItem("health_potion", 2);
        Assert(e.UsePotion("", "health_potion") && e.S.hp == 60 && e.S.Count("health_potion") == 1, "a potion gives back 40 health");
        Assert(e.Join("rook"), "Rook joins");
        var m = e.Member("rook");
        Assert(m.level == 2 && Math.Abs(m.hp - Rules.MemberMaxHp(m, e.data.Follower("rook"))) < 0.01f, "Rook joins at level 2 with full health");
        m.hp = 0;
        Rules.Afflict(e.S, e.data, m.body, "grave", "Rook");
        Assert(!e.UsePotion("rook", "health_potion") && m.hp == 0 && e.S.Count("health_potion") == 1, "a potion can't raise someone who's down");
        m.hp = 10;
        Assert(e.UsePotion("rook", "health_potion") && m.hp == 50, "but it heals someone who's standing");

        // Party members level up from the same experience as you
        int lv = m.level;
        e.GainExp(Rules.ExpToNext(m.level), "test");
        Assert(m.level == lv + 1 && Rules.HasAbility(m, e.data.Follower("rook"), "field_dressing"), "Rook levels up and opens Field dressing at 3");

        // Rook's trait: hill-born, he needs half the food and water
        m.hunger = 90; float h0 = m.hunger;
        Rules.Advance(e.S, e.data, 120, false);
        Assert(h0 - m.hunger > 0 && h0 - m.hunger < Rules.HungerPerHour * 2 * 0.6f, "hill-born Rook gets hungry at half the rate");
        // Pin down stops a heavy attack, once a fight
        int pins = 0;
        for (int seed = 0; seed < 30 && pins == 0; seed++)
        {
            var g = AtPackLeader(seed);
            for (int r = 0; r < 30 && g.S.inCombat; r++)
            {
                bool heavy = g.Intent.KindOf() == "heavy";
                g.Act(g.Choices().First(c => c.def.label == "Defend").def, Engine.TimingGood);
                if (heavy && g.S.battleLog.Any(l => l.Contains("pins"))) { pins++; break; }
            }
        }
        Assert(pins > 0, "Rook pins down a big attack");
    }

    static void FirstWound()
    {
        var e = NewEngine();
        ToRoad(e, 5);
        e.S.stepsSinceFight = -1000;
        WalkUntil(e, Gate(e), () => e.S.scene == "wolf_rescue");
        Assert(e.Check("wound:bite"), "the opening wolf always leaves a bite");
        Pick(e, "Get up");
        WalkUntil(e, Gate(e), () => e.Area.id == "skelmed");
        var bite = e.S.body.afflictions.First(x => x.id == "bite");
        Assert(e.data.Affliction("bite").parts.Contains(bite.part) && bite.from.Contains("wolf"), "the bite is on an arm or leg, and the wolf did it: " + bite.part + ", " + bite.from);
        Assert(e.Objects().Any(o => o.id == "bess_gate") && e.Objects().All(o => o.id != "bess"), "Bess is down at the gate when you walk in bitten");
        e.S.stepsSinceFight = -1000;
        e.S.energy = 80; e.S.hunger = 80; e.S.thirst = 80;
        WalkUntil(e, (17, 20), () => e.S.scene == "bess_patch");
        Pick(e, "see to the bite");
        Assert(e.MenuRequest == "survival" && e.S.Count("spirits") >= 1 && e.S.Count("bandage") >= 1, "Bess hands you spirits and linen and opens Survival");
        e.S.result = "";
        Assert(e.Treat("", "bite", "clean") && e.Treat("", "bite", "bandage"), "you clean and bind it yourself");
        Assert(e.S.Has("patch_done") && e.S.result.Contains("Bess"), "Bess approves");
        Assert(e.Objects().Any(o => o.id == "bess") && e.Objects().All(o => o.id != "bess_gate") && e.Walks.Contains(("bess_gate", "bess")), "then she walks home to the inn");
        Assert(e.NextStep("", "bite") == null, "nothing left to do but wait");
        Rules.Advance(e.S, e.data, 60 * 60, true);
        Assert(e.S.body.afflictions.All(x => x.id != "bite") && e.S.body.scars.Any(x => x.name == "Bite scar" && x.part == bite.part && x.from.Contains("wolf")), "a healed bite leaves a scar where it was");

        var f = AtDog(6);
        f.S.inCombat = false;   // AtDog picks "Fight it"; go back to the meeting itself
        f.GoTo("dog");
        f.S.AddItem("bread", 1);
        Pick(f, "Throw it some bread");
        Assert(!f.Check("wound"), "feeding the wolf gets you past it unhurt");
    }

    static void DogFights()
    {
        { var d0 = AtDog(0); Console.WriteLine("    at the wolf: hp " + d0.S.hp + "/" + Rules.MaxHp(d0.S) + ", stamina " + d0.S.stamina + "/" + Rules.MaxStamina(d0.S, d0.data) + ", str " + d0.S.stats[Stat.Str] + " agi " + d0.S.stats[Stat.Agi] + ", gear dmg " + Rules.GearDamage(d0.S) + " guard " + Rules.GearGuard(d0.S, d0.data) + ", crit " + Rules.CritChance(d0.S, d0.data) + "%, party " + d0.S.party.Count + ", speed " + Rules.Speed(d0.S, d0.data)); }
        { var d0 = AtPackLeader(0); Console.WriteLine("    at the pack leader: level " + d0.S.level + " hp " + d0.S.hp + "/" + Rules.MaxHp(d0.S) + ", stamina " + d0.S.stamina + ", str " + d0.S.stats[Stat.Str] + ", gear dmg " + Rules.GearDamage(d0.S) + " guard " + Rules.GearGuard(d0.S, d0.data) + ", party " + d0.S.party.Count); }
        if (Environment.GetEnvironmentVariable("BOSS") != "1")
        {
        Console.WriteLine("    the wolf, reading its tells:");
        var smart = Simulate(AtDog, SmartRound, "dog_won", 400);
        Console.WriteLine("    the wolf, mashing attack:");
        var mash = Simulate(AtDog, MashRound, "dog_won", 400);
        Assert(smart.wins >= 380, "reading the wolf should nearly always win");
        Assert(mash.wins < smart.wins - 40 && mash.lost > smart.lost * 1.5f, "mashing attack should go much worse");
        }

        Console.WriteLine("    the pack leader with Rook, reading its tells:");
        var bs = Simulate(AtPackLeader, SmartRound, "den_won", 200);
        Console.WriteLine("    the pack leader with Rook, mashing attack:");
        var bm = Simulate(AtPackLeader, MashRound, "den_won", 200);
        Assert(bs.wins >= 120, "the pack leader should be beatable by someone who reads it");
        Assert(bm.wins < bs.wins / 2, "but it should punish mashing");
    }

    /// <summary>PERKS=1: each background's fights, side by side. The fight perks should each help about the same.</summary>
    static void PerkBalance()
    {
        int n = int.TryParse(Environment.GetEnvironmentVariable("PERKN"), out var pn) ? pn : 200;
        foreach (var bg in (Environment.GetEnvironmentVariable("BGS") ?? "farmer,soldier,thief,hunter").Split(','))
        {
            Engine With(Engine e) { e.S.background = bg; return e; }
            Console.WriteLine("  " + bg + ":");
            Simulate(s => With(AtDog(s)), SmartRound, "dog_won", n);
            Simulate(s => With(AtDog(s)), MashRound, "dog_won", n);
            Simulate(s => With(AtPackLeader(s)), SmartRound, "den_won", n);
            Simulate(s => With(AtPackLeader(s)), MashRound, "den_won", n);
        }
    }

    static Engine AtPackLeader(int seed)
    {
        var e = NewEngine();
        e.DevStart("boss");
        e.S.seed = seed * 7 + 1;
        WalkUntil(e, (29, 5), () => e.S.scene == "den");
        Pick(e, "Fight it");
        return e;
    }

    static Engine AtDog(int seed)
    {
        var e = NewEngine();
        ToRoad(e, seed);
        e.S.stepsSinceFight = -1000;   // no random fights on the way
        e.S.flags.Add("dog_met");      // the road wolf is Ida's cutscene now; walk to where it stood and fight the old way
        WalkUntil(e, (15, 44), () => e.S.ax == 15 && e.S.ay == 44);
        e.GoTo("dog");
        Pick(e, "Fight it");
        return e;
    }

    static void Timing()
    {
        Assert(AtDog(3).Intent != null && AtDog(3).Paragraphs().Count == 2, "the dog should show what it's about to do");
        // Attacks no longer use the bar: crits (and the odd glancing blow) come by chance
        int crits = 0, glances = 0, hits = 0;
        float critDmg = 0, hitDmg = 0;
        for (int seed = 0; seed < 120; seed++)
        {
            var a = AtDog(seed);
            float before = a.S.enemyHp;
            a.Act(a.Choices().First(c => c.def.label == "Attack").def, Engine.TimingMiss);
            var ev = a.Events.FirstOrDefault(x => x.target == "enemy");
            Assert(ev != null, "a hit should be recorded for the screen");
            if (ev.kind == "crit") { crits++; critDmg += ev.amount; }
            else if (ev.kind == "glance") glances++;
            else { hits++; hitDmg += ev.amount; }
        }
        Assert(crits > 3 && crits < 40, "some attacks crit by chance (" + crits + " of 120)");
        Assert(glances < 20, "only a few glance (" + glances + ")");
        Assert(critDmg / crits > hitDmg / hits * 1.4f, "a crit hits much harder");

        int parried = 0, dodged = 0, smashed = 0;
        for (int seed = 0; seed < 60; seed++)
        {
            var e = AtDog(seed);
            e.S.stamina = Rules.MaxStamina(e.S, e.data);
            string kind = e.Intent.KindOf();
            if (kind == "guard") continue;
            float hp = e.S.hp;
            if (kind == "strike")
            {
                e.Act(e.Choices().First(c => c.def.label == "Defend").def, Engine.TimingPerfect);
                Assert(e.S.hp >= hp, "a perfect block parries a quick blow");
                parried++;
            }
            else if (seed % 2 == 0)
            {
                e.Act(e.Choices().First(c => c.def.label == "Defend").def, Engine.TimingPerfect);
                Assert(e.S.hp < hp, "a big blow smashes through a block without a shield");
                smashed++;
            }
            else
            {
                e.Act(e.Choices().First(c => c.def.label == "Dodge").def, Engine.TimingGood);
                Assert(e.S.hp >= hp && e.S.enemyOpen > 0, "dodging a big blow takes nothing and leaves it open");
                float ehp = e.S.enemyHp;
                e.S.enemyMove = "wary";
                e.Act(e.Choices().First(c => c.def.label == "Attack").def, Engine.TimingGood);
                Assert(e.Events.Any(x => x.target == "enemy" && x.kind == "crit") && e.S.enemyOpen == 0, "the next blow into an open enemy is a sure crit");
                dodged++;
            }
        }
        Assert(parried > 10 && dodged > 3 && smashed > 3, "should have tested parries, dodges and blocks (" + parried + "/" + dodged + "/" + smashed + ")");

        // A feint punishes a dodge; a howl left alone enrages
        var f = AtPackLeader(1);
        f.S.stamina = Rules.MaxStamina(f.S, f.data);
        f.S.enemyMove = "feint";
        float fh = f.S.hp;
        f.Act(f.Choices().First(c => c.def.label == "Dodge").def, Engine.TimingPerfect);
        Assert(f.S.enemyOpen == 0 && (f.S.hp < fh || f.Member("rook").hp < f.data.Follower("rook").hp), "dodging a feint gets you caught");
        f.S.enemyMove = "howl";
        f.Act(f.Choices().First(c => c.def.label == "Defend").def, Engine.TimingGood);
        Assert(f.S.enemyRage == 1, "letting it howl enrages it");
        f.S.enemyMove = "howl";
        f.Act(f.Choices().First(c => c.def.label == "Attack").def, Engine.TimingGood);
        Assert(f.S.enemyRage == 1, "hitting it cuts the howl short");
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

    // ---------------------------------------------------------------------------------------- the road to Eastmere

    static Engine OnTheRoad()
    {
        var e = NewEngine();
        e.DevStart("eastroad");
        e.S.stepsSinceFight = -100000;
        e.S.coins = 300;
        e.S.AddItem("bandage", 4);
        Assert(e.Travelling && e.Area.id == "forest_road" && e.Member("rook") != null && e.Check("quest:eastmere"), "on the forest road with Rook");
        return e;
    }

    /// <summary>Walks towards a tile, answering any scene the road springs on you on the way.</summary>
    static void Through(Engine e, (int x, int y) goal, Func<bool> done, params (string scene, string label)[] answers)
    {
        for (int i = 0; i < 800 && !done(); i++)
        {
            Keep(e);
            var a = answers.FirstOrDefault(x => x.scene == e.S.scene && !e.S.inCombat);
            if (a.scene != null) { Pick(e, a.label); continue; }
            if (e.S.inCombat) { e.DevWinFight(); continue; }
            StepToward(e, goal);
        }
        if (!done()) throw new Exception("never got there; stuck in " + e.S.scene + " at " + e.Area.id + " " + e.S.ax + "," + e.S.ay);
    }

    static void ThroughTo(Engine e, string spot, params (string scene, string label)[] answers)
    {
        string target = e.data.Spot(spot).area.id;
        Through(e, ZoneTile(e, spot), () => e.Area.id == target && e.Travelling, answers);
    }

    static (int x, int y) Beside(Engine e, string id)
    {
        var o = e.Area.objects.First(x => x.id == id);
        foreach (var f in Engine.Facings)
        {
            var (dx, dy) = Engine.Dir(f);
            int x = o.x - dx, y = o.y - dy;
            if (Engine.InBounds(e.Area, x, y) && (!e.Blocked(x, y) || (e.S.ax == x && e.S.ay == y)) && e.PathTo(x, y) != null) return (x, y);
        }
        throw new Exception("can't get next to " + id);
    }

    static void RoadToThornby()
    {
        var e = OnTheRoad();
        int rook0 = e.Bond("rook").value;

        // Deadwood and a talk by the fire
        UseThing(e, "fr_wood1"); UseThing(e, "fr_wood2");
        Assert(e.S.Count("firewood") >= 2 && e.Travelling, "deadwood by the road, picked up at once");
        UseThing(e, "fr_fire");
        Assert(e.S.scene == "campfire", "the fire");
        Pick(e, "Talk to Rook");
        Assert(e.S.scene == "talk_rook" && e.Current.speaker == "Rook", "talking to Rook face to face");
        Pick(e, "growing up in Skelmed");
        Assert(!e.Choices().Any(c => c.def.label.Contains("your father")), "one talk a night");
        Pick(e, "Just checking on you");
        Assert(e.Travelling, "back on the road");
        // and from the camp menu, anywhere you can camp
        Assert(e.Choices().Any(c => c.def.label == "Talk to Rook"), "the camp menu has a word with Rook");
        Assert(e.Bond("rook").value > rook0, "Rook warms to you");

        // The charcoal burner's son
        UseThing(e, "hesta");
        Pick(e, "What's wrong");
        Pick(e, "Goodbye");
        Assert(e.Check("quest:burners_son"), "Hesta's son is missing");

        // The burnt cart, and Wren
        var w = Beside(e, "wren");
        Through(e, w, () => e.S.ax == w.x && e.S.ay == w.y && e.Travelling, ("cart_seen", "Go and see"));
        Assert(e.Check("flag:cart_seen"), "you see the cart from the road");
        UseThing(e, "wren");
        Assert(e.S.scene == "wren_meet", "Wren by the cart");
        Assert(!e.Choices().Any(c => c.def.label.Contains("We'll help")), "she has to trust you first");
        Pick(e, "We're not with anyone");
        Pick(e, "What happened here");
        Pick(e, "We'll help you find him");
        Assert(e.Member("wren") != null && e.Check("quest:wren_home") && e.S.party.Count == 2, "Wren walks with you now");

        // Her father in the hollow
        UseThing(e, "fr_tracks");
        Pick(e, "Follow them");
        UseThing(e, "tobin");
        Assert(e.S.scene == "tobin" && e.Check("flag:tobin_found"), "Tobin, with a broken leg");
        int wren0 = e.Bond("wren").value;
        Pick(e, "Splint the leg");
        Assert(e.Check("flag:tobin_carried") && e.Bond("wren").value > wren0 && e.Objects().All(o => o.id != "tobin"), "you carry him");

        // The old track: Rook's fall, the deserters and the burner's boy
        ThroughTo(e, "ot_west");
        Through(e, (25, 12), () => e.S.scene == "deserters", ("rook_fall", "Stop and bind it properly"));
        Assert(e.Check("flag:rook_fell") && e.Check("flag:col_found"), "Rook turned his ankle on the way; the deserters have Col");
        Pick(e, "Let the boy go");
        Pick(e, "Fight them");
        Assert(e.S.inCombat && e.S.enemy == "deserter", "a fight with the deserters");
        e.DevWinFight();
        Assert(e.S.scene == "deserters_won" && e.Check("flag:col_freed"), "Col's free");
        Pick(e, "Move on");

        // Brannock bridge: the king's toll, for three
        ThroughTo(e, "br_west_south");
        int coins = e.S.coins;
        Through(e, (14, 17), () => e.S.scene == "toll");
        Pick(e, "Pay the toll for three");
        Assert(e.Travelling && AtSpot(e, "br_bridge_east") && e.S.coins < coins && e.Check("flag:crossed_river"), "over the bridge, poorer");

        // Thornby: Wren home
        ThroughTo(e, "th_west");
        Through(e, (17, 17), () => e.S.scene == "wren_arrives");
        Pick(e, "Go on");
        Assert(e.Member("wren") == null && e.Check("done:wren_home") && e.Check("flag:tobin_home") && e.Objects().Any(o => o.id == "tobin_home"), "Wren's home, and her father");
        UseThing(e, "tobin_home");
        Pick(e, "How's the leg");
        Assert(e.S.gearItems.Any(g => g.id == "knife"), "Tobin's knife");
        Pick(e, "Goodbye");

        // The bad well
        UseThing(e, "marda");
        Pick(e, "something wrong with the well");
        Pick(e, "Goodbye");
        e.S.AddItem("firewood", 2);
        UseThing(e, "carcass");
        Pick(e, "Haul it out and burn it");
        Pick(e, "Leave");
        UseThing(e, "marda");
        Pick(e, "We burned it");
        Assert(e.Check("done:thornby_well") && e.S.Rep("thornby") > 0, "the well's clean");
        Pick(e, "Goodbye");

        // The road ends east of Thornby, for now
        Through(e, (35, 15), () => e.S.scene == "eastmere_end");
        Pick(e, "Turn back");
        Assert(e.Travelling && e.Area.id == "thornby", "back into Thornby");
        Console.WriteLine("    reached Thornby on day " + e.S.Day + " at " + Rules.Clock(e.S) + ", coins " + e.S.coins + ", Rook bond " + e.Bond("rook").value + ", Wren " + e.Bond("wren").value);
    }

    static void RoadHardChoices()
    {
        var e = OnTheRoad();
        var w = Beside(e, "wren");
        Through(e, w, () => e.S.ax == w.x && e.S.ay == w.y && e.Travelling, ("cart_seen", "Go and see"));
        UseThing(e, "wren");
        Walk(e, "We're not with anyone", "What happened here", "We'll help you find him");
        UseThing(e, "fr_tracks");
        Pick(e, "Follow them");
        UseThing(e, "tobin");
        int rook0 = e.Bond("rook").value;
        Pick(e, "nothing we can do");
        Assert(e.Member("wren") == null && e.Check("flag:tobin_with_wren") && e.Bond("rook").value < rook0, "Wren stays with her father; Rook thinks less of you");
        Assert(e.Objects().Any(o => o.id == "wren_left"), "she's still there in the hollow");

        // Up to Greyfell, and the deserter at the shrine
        ThroughTo(e, "gf_south");
        UseThing(e, "sorrel");
        Pick(e, "You're a deserter");
        Pick(e, "Leave him");
        Assert(e.Check("flag:sorrel_met") && !e.Check("flag:ford_known"), "Sorrel, unhelped");
        ThroughTo(e, "br_west_north");

        // Informing on him gets you over the bridge
        int coins = e.S.coins;
        Through(e, (14, 17), () => e.S.scene == "toll");
        Pick(e, "wounded deserter sheltering");
        Assert(e.Check("flag:informer") && e.Check("flag:sorrel_gone") && e.S.coins == coins + 20 && AtSpot(e, "br_bridge_east"), "twenty coins and the bridge");

        // Thornby: telling Ness
        ThroughTo(e, "th_west");
        UseThing(e, "ness");
        Pick(e, "We met them on the road");
        Assert(e.Check("flag:tobin_rescued") && e.Check("flag:wren_home") && !e.Check("quest:wren_home"), "Thornby fetches them home");
        Pick(e, "Goodbye");
        Assert(e.Objects().Any(o => o.id == "wren_home") && e.Objects().Any(o => o.id == "tobin_home"), "both home");
        UseThing(e, "wren_home");
        Assert(e.Paragraphs().Any(p => p.Contains("doesn't say whether she agrees")), "Wren hasn't forgiven you");
        Pick(e, "Goodbye");

        // The ford: Sorrel's way over, or wading
        var f = OnTheRoad();
        f.S.flags.Add("ford_known");
        var sp = f.data.Spot("br_west_north").spot;
        f.S.area = "brannock"; f.S.ax = sp.x; f.S.ay = sp.y;
        Through(f, (18, 4), () => f.S.scene == "ford");
        Pick(f, "Cross on the flat stones");
        Assert(f.Travelling && AtSpot(f, "br_ford_east") && f.Check("flag:crossed_river"), "over by the ford");
        ThroughTo(f, "th_west");
        Assert(f.Check("flag:toll_done") == false, "the ford skips the toll");
    }

    static void UseEverything(AreaDef area, WorldObj o)
    {
        var v = NewEngine();
        bool east = RoadEast.Contains(area.id);
        v.DevStart(area.id == "skelmed" ? "village" : area.id == "yenson_hut" ? "hut" : area.id == "skelmed_hills" ? "hills" : east ? "eastroad" : "road");
        if (area.id == "yenson_hut") Walk(v, "Sit up", "Goodbye");
        if (east)
        {
            var sp = area.spots[0]; v.S.area = area.id; v.S.ax = sp.x; v.S.ay = sp.y;
            foreach (var f in new[] { "cart_seen", "rook_fell", "tower_seen", "toll_seen" }) v.S.flags.Add(f);
        }
        // whatever it needs to be there: its flags, quests and company (the first of any either/or)
        foreach (var w in o.when.Where(w => !w.StartsWith("!")).Select(w => w.Split('|')[0]))
        {
            if (w.StartsWith("flag:")) v.S.flags.Add(w.Substring(5));
            else if (w.StartsWith("quest:")) v.S.flags.Add(w);
            else if (w.StartsWith("follower:")) v.Join(w.Substring(9));
        }
        if (o.id == "bess_gate") { Rules.Afflict(v.S, v.data, v.S.body, "bite", null); v.S.flags.Add("patch_lesson"); v.S.flags.Remove("patch_done"); }
        if (!v.All(o.when)) throw new Exception(o.id + " isn't there to test");
        v.S.coins = 2000;
        v.S.flags.Add("dog_met");
        v.S.stepsSinceFight = -1000;
        if (o.id == "alpha") WalkUntil(v, (29, 5), () => v.S.scene == "den");   // the den's edge stops you first
        else UseThing(v, o.id);
        bool quick = v.Scene(o.scene ?? "")?.quick == true;
        Assert(v.S.scene != "travel" || v.S.inCombat || quick, "using " + o.id + " should open its scene");
        for (int i = 0; i < 30 && (v.S.scene != "travel" || v.S.inCombat) && v.S.scene != "death"; i++)
        {
            if (v.S.inCombat) { v.DevWinFight(); continue; }
            if (v.S.scene == "forge") { v.FinishForge(1, 1, 1); continue; }
            v.Choose(v.Choices().Where(c => c.enabled).Last().def);
        }
        Assert(v.S.scene == "travel" && v.Area.id == area.id, "after " + o.id + " you should be back where you were, was " + v.S.scene);
    }

    static readonly string[] RoadEast = { "forest_road", "old_track", "greyfell", "brannock", "thornby" };

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
        Assert(walk != null && run != null && e.StepSeconds(true) < e.StepSeconds(false) && run.stamina > walk.stamina, "running should be quicker but tiring");

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

        // The coast road to Skelmed: a quarter of an hour or so, past the wolf
        int t0 = e.S.minutes;
        e.S.stepsSinceFight = -1000;
        WalkUntil(e, Gate(e), () => e.S.scene == "wolf_rescue");
        Pick(e, "Get up");
        e.S.body = new Body();   // the bite and Bess have their own test
        var wolf = a.objects.First(o => o.id == "wolf");
        Assert(e.ObjectAt(wolf.x, wolf.y) == null, "the wolf should be gone from the road");
        WalkUntil(e, Gate(e), () => e.Area.id == "skelmed");
        Assert(e.S.minutes - t0 >= 4 && e.S.minutes - t0 <= 60, "the walk should take a little while, took " + (e.S.minutes - t0) + " min");
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
        WalkUntil(e, (17, 27), () => e.Area.id == "coast_road");
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
                try { UseEverything(area, o); }
                catch (Exception ex) { throw new Exception(area.id + " / " + o.id + ": " + ex.Message, ex); }
            }

        // Fights come at random as you walk (only crabs now, down on the sand), never too close together; nothing else interrupts the walk
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
                if (w.S.scene == "wolf_rescue") { Pick(w, "Get up"); continue; }
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
        Assert(!fights.ContainsKey("dog"), "no random wolves: only the one in the road (David, 19 Oct)");
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
            { "village", "travel" }, { "road", "travel" }, { "hut", "wake" }, { "forge_iron", "smith" },
            { "hills", "travel" }, { "hunt", "travel" }, { "home", "ida_home" }, { "wolf", "wolf_rescue" }, { "boss", "travel" }, { "eastroad", "travel" },
            { "dog", "travel" }, { "crab", "travel" },
        };
        foreach (var (id, _) in Engine.DevStarts)
        {
            var e = NewEngine();
            e.DevStart(id);
            Assert(e.IsDev && e.S.scene == expect[id] && e.Area != null, id + " should land in " + expect[id] + ", was " + e.S.scene);
            if (e.S.scene != "travel" || e.S.inCombat) Assert(e.Choices().Any(c => c.enabled), id + " should have something to do");
            if (id == "dog" || id == "crab") Assert(e.S.inCombat && e.S.enemy == id, id + " should start the fight");
            if (id == "forge_iron") { Pick(e, "An iron cap"); Assert(e.S.scene == "forge", "should be at the anvil"); e.FinishForge(1, 1, 1); Assert(e.Travelling, "back in the village"); }
            if (id == "hunt") Assert(e.Member("rook") != null && e.Objects().Any(o => o.id == "hwood1") && e.Objects().Any(o => o.id == "boar"), "out hunting with Rook");
            if (id == "home") { Pick(e, "Go with Rook"); Assert(e.Travelling && e.Check("flag:rook_escort") && e.S.gearItems.Any(g => g.id == "hunting_dagger"), "Ida's gifts, and off to Eastmere"); }
            if (id == "wolf") { Pick(e, "Get up"); Assert(e.Travelling && e.Check("untreated:bite"), "bitten, back on the road"); }
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
            SmartRound(e, e.S.round);
        }
        else if (e.S.scene == "travel")
        {
            var path = e.PathTo(goal.x, goal.y);
            if (path == null || path.Count == 0) throw new Exception("no way to " + goal + " from " + e.S.ax + "," + e.S.ay);
            e.Face(path[0]);
            var step = e.StepChoice(e.S.stamina > 50);
            e.Choose(step);
            RealTime(e, step.label == "Run" ? 0.11f : 0.2f, step.label == "Run");
        }
        else if (e.S.scene.StartsWith("ev_") || e.S.scene.StartsWith("dog_") || e.S.scene == "collapse")
            e.Choose(e.Choices().Where(c => c.enabled).Last().def);
        else throw new Exception("walking stopped in " + e.S.scene + ": " + e.S.result + " " + e.S.deathCause + " [" + string.Join(", ", Rules.BodyStates(e.S.body, e.data)) + "] day " + e.S.Day + " " + Rules.Clock(e.S));
    }

    /// <summary>What a step takes in real time on screen: the world clock runs, and your breath comes back.</summary>
    static void RealTime(Engine e, float seconds, bool running = false)
    {
        e.Tick(seconds);
        e.Breathe(seconds, running);
    }

    /// <summary>A tile you can walk onto in the zone that leads to a spot.</summary>
    static (int x, int y) ZoneTile(Engine e, string spot)
    {
        var z = e.Area.zones.First(z => z.to == spot);
        for (int y = z.y0; y <= z.y1; y++)
            for (int x = z.x0; x <= z.x1; x++)
                if (e.PathTo(x, y) != null) return (x, y);
        throw new Exception("no way into the zone to " + spot);
    }

    /// <summary>Walks to the zone that leads to a spot in another area, and through it.</summary>
    static void ToArea(Engine e, string spot)
    {
        string target = e.data.Spot(spot).area.id;
        WalkUntil(e, ZoneTile(e, spot), () => e.Area.id == target && e.Travelling);
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
        // the story runs aren't about wounds and weather (those have their own tests)
        e.S.body = new Body();
        foreach (var m in e.S.party) m.body = new Body();
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
            if ((e.S.ax == x && e.S.ay == y) || (Engine.InBounds(e.Area, x, y) && !e.Blocked(x, y) && e.PathTo(x, y) != null)) { best = (x, y); break; }
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
                    e.CreateCharacter("Test", "other", "Lean", "Red", "farmer", new[] { 5, 0, 0, 0, 0 });
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
                    (int x, int y) goal = a.id == "coast_road" ? Gate(e) : a.id == "yenson_hut" ? (5, 7) : (rng.Next(2) == 0 ? (17, 27) : (28, 23));
                    if (r == 8 && a.objects.Count > 0) { var o = a.objects[rng.Next(a.objects.Count)]; goal = (o.x, o.y + 1); }
                    if (r == 9) { e.Face(Engine.Facings[rng.Next(4)]); var u = e.UseChoice(); if (u != null) { e.Choose(u); continue; } }
                    var path = e.PathTo(goal.x, goal.y);
                    if (path != null && path.Count > 0)
                    {
                        e.Face(path[0]);
                        var walk = e.StepChoice(false);
                        if (walk != null) { e.Choose(walk); RealTime(e, 0.2f); continue; }
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
        e.CreateCharacter("Ash", "female", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 });
        Walk(e, "Go", "Wake", "Sit up");
    }

    /// <summary>Sent on by Yenson, supplied (as if from the wreck), and standing outside his door.</summary>
    static void ToRoad(Engine e, int seed = 1)
    {
        ToHut(e, seed);
        foreach (var f in new[] { "y_road", "wreck_hull" }) e.S.flags.Add(f);
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

    static void Dialogue()
    {
        var e = NewEngine();
        e.DevStart("hut");
        Pick(e, "Sit up");
        Assert(e.Paragraphs().Any(p => p.StartsWith("[[mood:worried]]") && p.Contains("Yenson")), "Yenson's first greeting, worried");
        Pick(e, "What happened to me");
        var parts = e.S.result.Split(new[] { "\n\n" }, StringSplitOptions.None);
        Assert(parts.Length == 2 && parts[1].StartsWith("[[voice:Intellect]]"), "the answer, then an inner voice in its own box");
        var asked = e.Choices().First(c => c.def.topic == "what");
        Assert(asked.read && asked.enabled, "the question stays, marked as asked");
        Pick(e, "What happened to me");
        Assert(e.S.result.Contains("tide line"), "asking again repeats the answer");
        Assert(e.Paragraphs().All(p => !p.Contains("The sea nearly kept you")), "no second greeting in the same talk");
        // a check you fail still says something, and can't be tried again
        e.S.stats[Stat.Int] = 2;
        var chk = e.Choices().First(c => c.def.check != null);
        Assert(chk.check == "Intellect 5" && !chk.checkOk, "the check shows on the button and that you'd fail it");
        e.Choose(chk.def);
        Assert(e.S.result.Contains("missed something") && e.Check("flag:y_past_tried") && !e.Check("flag:y_past"), "failing gets its own line");
        Assert(e.Choices().All(c => c.def.check == null), "and it's gone");
        // a tone, and the bond it builds
        Pick(e, "I owe you my life");
        Assert(e.Bond("Yenson").value == 5 && e.Bond("Yenson").name == "Stranger", "a kind word counts");
        Pick(e, "mend the net");
        Assert(e.Bond("Yenson").value == 10 && e.Bond("Yenson").name == "Friendly" && e.S.Toasts.Any(t => t.Contains("friendly")), "Yenson warms to you");
        Assert(e.Choices().Any(c => c.def.label.Contains("for the road")), "a friend's gift opens up");
        // a pass
        var p2 = NewEngine();
        p2.DevStart("village");
        p2.S.background = "hunter";
        p2.S.flags.Add("ida_met");
        Keep(p2); UseThing(p2, "ida");
        var hunter = p2.Choices().First(c => c.def.check == "background:hunter");
        Assert(hunter.checkOk && hunter.check == "Hunter", "a hunter can read the wolf");
        p2.Choose(hunter.def);
        Assert(p2.S.result.Contains("You've hunted") && p2.Bond("Ida").value == 5, "Ida takes note");
    }

    static void Creation()
    {
        var e = NewEngine();
        e.NewGame("basic");
        Assert(e.data.file.origins.Count >= 4 && e.data.file.quirks.Count >= 6, "origins and quirks to pick from");
        Assert(e.CreateCharacter("Ash", "male", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 }, quirks: new[] { "wiry", "bookish", "cautious" }) != null, "at most two quirks");
        Assert(e.CreateCharacter("Ash", "male", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 }, originId: "nowhere") != null, "unknown origin refused");
        // what the creation screen shows is what you get
        var pv = e.PreviewCharacter("farmer", new[] { 0, 0, 2, 3, 0 }, "abroad", new[] { "wiry", "bookish" });
        Assert(e.CreateCharacter("Ash", "male", "Lean", "Red", "farmer", new[] { 0, 0, 2, 3, 0 }, originId: "abroad", quirks: new[] { "wiry", "bookish" }) == null, "valid character refused");
        Assert(e.S.stats.SequenceEqual(pv.stats) && Rules.MaxHp(e.S) == Rules.MaxHp(pv), "the preview matches the character");
        Assert(e.S.stats[Stat.Int] == 8 && e.S.stats[Stat.Agi] == 6 && e.S.stats[Stat.Str] == 2, "abroad +1 Int, wiry +1 Agi, bookish +1 Int -1 Str");
        Assert(Rules.MaxHp(e.S) == Rules.MaxHp(new GameState { stats = e.S.stats }) - 10, "wiry costs 10 health");
        Assert(e.Bond("Yenson").value == -3, "a foreigner starts a little behind");
        Assert(e.Check("origin:abroad") && e.Check("quirk:wiry") && !e.Check("quirk:cautious"), "origin and quirk conditions");
        var back = JsonConvert.DeserializeObject<GameState>(JsonConvert.SerializeObject(e.S));
        Assert(back.origin == "abroad" && back.quirks.Count == 2 && back.hpBonus == -10, "origin and quirks survive a save");

        var coast = NewEngine();
        coast.NewGame("basic");
        coast.CreateCharacter("Ash", "male", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 }, originId: "coast");
        Assert(coast.Bond("Yenson").value == 10 && coast.Bond("Tam").value == 10, "coast folk start as friends");
        var woods = NewEngine();
        woods.NewGame("basic");
        woods.CreateCharacter("Ash", "male", "Lean", "Red", "farmer", new[] { 1, 1, 1, 1, 1 }, originId: "woods");
        Assert(woods.SkillLevel("hunting") >= 1 && woods.S.Toasts.Count == 0, "woodland-born start at Hunting 1, quietly");

        // each stat is felt in the world
        var lo = new GameState(); var hi = new GameState();
        lo.stats = new[] { 1, 1, 1, 1, 1 }; hi.stats = new[] { 8, 8, 8, 8, 8 };
        Assert(Rules.WorkRate(hi) < 1f && Rules.WorkRate(lo) > 1f, "Strength eases hard work");
        Assert(Rules.HungerRate(hi, e.data) < Rules.HungerRate(lo, e.data), "Endurance slows hunger");
        Assert(Rules.LearnRate(hi) > Rules.LearnRate(lo), "Intellect speeds learning");
        Assert(Rules.Price(hi, e.data, 100) < 100 && Rules.Price(lo, e.data, 100) > 100, "Charisma for prices");
        hi.origin = "zaron";
        Assert(Rules.Price(hi, e.data, 100) < Rules.Price(new GameState { stats = hi.stats }, e.data, 100), "city-bred haggle better");
        var big = new GameState { quirks = new List<string> { "big_appetite" } };
        Assert(Math.Abs(Rules.HungerRate(big, e.data) - 1.3f) < 0.01f, "big appetite eats more");

        // prices in a shop: the label, what you pay, and not being able to pay
        var v = NewEngine();
        v.DevStart("village");
        v.S.stats[Stat.Cha] = 8;
        int bread = Rules.Price(v.S, v.data, 6);
        v.GoTo("bess");
        var loaf = v.Choices().FirstOrDefault(c => c.def.label.Contains("loaf"));
        Assert(loaf != null, "bread at Bess's: " + v.S.scene + " / " + string.Join(" | ", v.Choices().Select(c => c.def.label)));
        Assert(v.Fill(loaf.def.label).Contains(Rules.Money(bread)), "the label shows your price: " + v.Fill(loaf.def.label));
        long before = v.S.coins;
        v.Choose(loaf.def);
        Assert(v.S.coins == before - bread, "you pay your price");
        v.GoTo("bess");
        v.S.coins = 0;
        Assert(!v.Choices().First(c => c.def.label.Contains("loaf")).enabled, "no money, no bread");

        // the world checks: a carving to read, a trunk to heave, a cairn to climb
        var w = NewEngine();
        w.DevStart("village");
        w.S.stats[Stat.Int] = 5; w.S.stats[Stat.Str] = 5; w.S.stats[Stat.Agi] = 2;
        w.GoTo("runestone");
        int exp = w.S.exp, lvl = w.S.level;
        Pick(w, "Make out the carving");
        Assert(w.S.exp > exp || w.S.level > lvl, "reading the stone teaches you something");
        w.GoTo("trunk");
        Pick(w, "Heave it aside");
        Assert(w.Check("flag:trunk_moved"), "strong enough to move the trunk");
        long c0 = w.S.coins;
        w.GoTo("cache");   // a quick scene: opened at once
        Assert(w.S.coins == c0 + 60, "under the trunk, a cache");
        w.GoTo("lookout_climb");
        float hp = w.S.hp;
        Pick(w, "sheer face");
        Assert(!w.Check("flag:cairn_top") && w.S.hp < hp, "too clumsy to climb the cairn, and it hurts");
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
        var only = Environment.GetEnvironmentVariable("ONLY");
        if (!string.IsNullOrEmpty(only) && name.IndexOf(only, StringComparison.OrdinalIgnoreCase) < 0) return;
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
