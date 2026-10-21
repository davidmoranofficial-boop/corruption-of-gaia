using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Loran.Core
{
    /// <summary>
    /// The clock out in the world, and long roads between towns (David, 21 Oct): days should pass slowly, time runs
    /// whether you move or not, and a town two days away takes two days, with nights to camp through.
    /// </summary>
    public partial class Engine
    {
        public const string JourneyId = "journey";

        /// <summary>On a long road, in any of its scenes (the road itself, something met on it, the fire at night).</summary>
        public bool OnJourney => !string.IsNullOrEmpty(S.journey) && data.Journey(S.journey) != null;
        public JourneyDef Journey => data.Journey(S.journey);

        /// <summary>
        /// The world's clock: called every frame with real seconds while you're free in the world, walking or standing.
        /// Returns true when the clock moved on a minute (so the HUD wants redrawing). If time took you down
        /// (starving, or out on your feet) you're moved to the collapse or death scene.
        /// </summary>
        public bool Tick(float realSeconds)
        {
            if (realSeconds <= 0 || !FreeToAct || !Travelling || !string.IsNullOrEmpty(S.result)) return false;
            S.secondsCarry += Math.Min(realSeconds, 1f) * Rules.ClockRate;
            int mins = (int)(S.secondsCarry / 60);
            if (mins <= 0) return false;
            S.secondsCarry -= mins * 60;
            Rules.Advance(S, data, mins, false);
            if (S.hp <= 0 || S.energy <= 0) GoTo(S.scene);
            return true;
        }

        // ------------------------------------------------------------------ journeys

        /// <summary>How far along the road you are, as 0..1 from where it starts.</summary>
        public float JourneyProgress => Journey == null ? 0 : Math.Clamp(S.journeyMiles / Journey.miles, 0f, 1f);
        /// <summary>Miles still to walk, the way you're going.</summary>
        public float MilesLeft => Journey == null ? 0 : S.journeyBack ? S.journeyMiles : Journey.miles - S.journeyMiles;
        public string JourneyTarget => Journey == null ? "" : S.journeyBack ? Journey.from : Journey.to;
        /// <summary>Days on the road so far, counting from the day you set out.</summary>
        public int JourneyDay => S.journeyDay;
        public bool Dark => S.Hour >= Rules.DuskHour || S.Hour < Rules.DawnHour;

        /// <summary>The stretch of road you're on (its name and how it looks).</summary>
        public JourneyStop Leg
        {
            get
            {
                var j = Journey;
                if (j == null) return null;
                JourneyStop best = null;
                foreach (var l in j.legs.OrderBy(l => l.mile)) if (l.mile <= S.journeyMiles + 0.001f) best = l;
                return best ?? j.legs.FirstOrDefault();
            }
        }

        void StartJourney(string id, bool back)
        {
            var j = data.Journey(id) ?? throw new Exception("No journey called '" + id + "'");
            S.journey = j.id;
            S.journeyBack = back;
            S.journeyMiles = back ? j.miles : 0;
            S.journeyWalking = !Dark;
            S.journeyDark = false;
            S.journeyHour = 0;
            S.journeyDay = 1;
            S.journeyStartDay = S.Day;
            S.journeyPassed.Clear();
        }

        /// <summary>Stop walking (or start again), from the journey screen's key.</summary>
        public void Halt(bool walk)
        {
            if (!OnJourney || S.scene != JourneyId) return;
            if (walk && Dark && !S.journeyDark) return;   // in the dark you have to choose to push on
            S.journeyWalking = walk;
        }

        /// <summary>
        /// Walking the road: called every frame by the journey screen with real seconds. Returns true when something
        /// needs the screen redrawn: night fell, you arrived, something happened on the way, or just a new minute.
        /// </summary>
        public bool March(float realSeconds)
        {
            if (!OnJourney || S.scene != JourneyId || S.inCombat || !S.journeyWalking || !string.IsNullOrEmpty(S.result) || !FreeToAct) return false;
            S.secondsCarry += Math.Min(realSeconds, 0.5f) * Rules.JourneyMinutesPerSecond * 60f;
            int mins = (int)(S.secondsCarry / 60);
            if (mins <= 0) return false;
            S.secondsCarry -= mins * 60;
            Walk(mins);
            return true;
        }

        /// <summary>Some minutes on the road: time, the march's toll, the miles, and whatever's waiting along it.</summary>
        void Walk(int mins)
        {
            var j = Journey;
            for (int m = 0; m < mins && S.journeyWalking; m++)
            {
                // Dusk stops you, unless you've chosen to push on
                if (Dark && !S.journeyDark)
                {
                    S.journeyWalking = false;
                    S.result = "The light's going. The road ahead is already grey and shapeless, and something calls out in the trees. Time to make camp, or push on in the dark.";
                    return;
                }
                if (!Dark) S.journeyDark = false;
                Rules.Advance(S, data, 1, true);
                Rules.March(S, data, 1);
                S.journeyDay = S.Day - S.journeyStartDay + 1;
                float before = S.journeyMiles;
                float pace = Rules.WalkMph / 60f * (Dark ? Rules.DarkPace : 1f) / TravelSlow();
                S.journeyMiles = Math.Clamp(S.journeyMiles + (S.journeyBack ? -pace : pace), 0, j.miles);
                if (S.hp <= 0 || S.energy <= 0) { GoTo(JourneyId); return; }

                // There
                if (S.journeyBack ? S.journeyMiles <= 0 : S.journeyMiles >= j.miles) { Arrive(); return; }

                // A set place on the road, the first time you pass it
                float lo = Math.Min(before, S.journeyMiles), hi = Math.Max(before, S.journeyMiles);
                var stop = j.stops.FirstOrDefault(st => st.mile > lo && st.mile <= hi && !S.journeyPassed.Contains(st.scene)
                                                         && (st.every || !S.Has("seen:" + st.scene)) && All(st.when));
                if (stop != null)
                {
                    S.journeyPassed.Add(stop.scene);
                    if (!S.Has("seen:" + stop.scene)) S.flags.Add("seen:" + stop.scene);
                    GoTo(stop.scene);
                    return;
                }

                // Every hour, a chance of something: a fight (likelier in the dark), or someone or something on the way
                S.journeyHour += 1;
                if (S.journeyHour < 60) continue;
                S.journeyHour = 0;
                if (Roll(1, 100) <= j.fightChance * (Dark ? 2 : 1))
                {
                    var fight = Pick(j.events.Where(e => e.kind == "fight"), ' ');
                    if (fight != null)
                    {
                        StartCombat(fight.enemy, string.IsNullOrEmpty(fight.win) ? JourneyId : fight.win, JourneyId);
                        GoTo(JourneyId);
                        return;
                    }
                }
                if (Roll(1, 100) <= j.eventChance)
                {
                    var ev = Pick(j.events.Where(e => e.kind != "fight"), ' ');
                    if (ev != null)
                    {
                        if (!S.Has("seen:" + ev.scene)) S.flags.Add("seen:" + ev.scene);
                        GoTo(ev.scene);
                        return;
                    }
                }
            }
        }

        void Arrive()
        {
            var j = Journey;
            bool back = S.journeyBack;
            int days = S.journeyDay;
            string spot = back ? j.fromSpot : j.toSpot;
            string scene = back ? j.arriveBack : j.arrive;
            string place = back ? j.from : j.to;
            S.journeyWalking = false;
            if (!S.Has("been:" + place.ToLowerInvariant())) S.flags.Add("been:" + place.ToLowerInvariant());
            if (string.IsNullOrEmpty(spot))
            {
                // Somewhere not built yet: you stay at the end of the road, and the arrival scene says so
                S.flags.Add("journey_end:" + j.id);
                GoTo(!string.IsNullOrEmpty(scene) ? scene : JourneyId);
                return;
            }
            S.journey = "";
            S.journeyMiles = 0;
            MoveTo(spot);
            S.Toasts.Add(place + ", after " + days + " day" + (days == 1 ? "" : "s") + " on the road.");
            GoTo(!string.IsNullOrEmpty(scene) ? scene : TravelId);
        }

        /// <summary>The road itself: what you can do when you've stopped.</summary>
        SceneDef JourneyScene()
        {
            var j = Journey;
            var sc = new SceneDef { id = JourneyId, place = j?.name, title = Leg?.name ?? j?.name };
            if (j == null) return sc;
            if (Dark)
                sc.choices.Add(new ChoiceDef { label = "Push on in the dark", effects = { "journey:dark" } });
            else
                sc.choices.Add(new ChoiceDef { label = "Walk on", effects = { "journey:go" } });
            sc.choices.Add(new ChoiceDef { label = "Rest an hour by the road", time = 60, effects = { "journey:rest" } });
            if (Dark || S.Hour >= 17) sc.choices.Add(new ChoiceDef { label = "Make camp for the night", effects = { "journey:camp" } });
            sc.choices.Add(new ChoiceDef { label = "Turn back toward " + (S.journeyBack ? j.to : j.from), effects = { "journey:turn" } });
            return sc;
        }

        /// <summary>journey:&lt;id&gt;[:back] sets off; journey:go / dark / rest / camp / sleep / turn / halt on the road.</summary>
        void JourneyEffect(string[] parts, StringBuilder notes, ref string redirect)
        {
            switch (parts[0])
            {
                case "go":
                    if (Journey != null && (S.journeyBack ? S.journeyMiles <= 0 : S.journeyMiles >= Journey.miles)) { notes.Append("This is as far as the road goes."); redirect = JourneyId; return; }
                    S.journeyWalking = true;
                    redirect = JourneyId;
                    return;
                case "halt":
                    S.journeyWalking = false;
                    redirect = JourneyId;
                    return;
                case "dark":
                    S.journeyDark = true;
                    S.journeyWalking = true;
                    notes.Append("You push on. The road is a paler line in the dark, and every sound carries.");
                    redirect = JourneyId;
                    return;
                case "rest":
                    S.stamina = Rules.MaxStamina(S, data);
                    S.energy += 3;
                    Rules.Clamp(S, data);
                    notes.Append("You sit with your back to a tree and let your legs stop aching.");
                    redirect = JourneyId;
                    return;
                case "camp":
                {
                    // round the fire first, if there's talk to be had tonight
                    var talk = Journey?.camps.FirstOrDefault(c => !S.Has("seen:" + c.scene) && All(c.when));
                    if (talk != null) { S.flags.Add("seen:" + talk.scene); redirect = talk.scene; return; }
                    goto case "sleep";
                }
                case "sleep":
                    S.journeyWalking = false;
                    S.journeyDark = false;
                    redirect = JourneyId;
                    Sleep("camp", notes);
                    S.journeyWalking = true;   // you set off again at first light
                    return;
                case "turn":
                    S.journeyBack = !S.journeyBack;
                    S.journeyPassed.Clear();
                    S.journeyStartDay = S.Day;
                    notes.Append("You turn around. " + (S.journeyBack ? Journey.from : Journey.to) + " is " + Miles(MilesLeft) + " back the way you came.");
                    redirect = JourneyId;
                    return;
                default:
                    if (data.Journey(parts[0]) == null) throw new Exception("No journey called '" + parts[0] + "'");
                    StartJourney(parts[0], parts.Length > 1 && parts[1] == "back");
                    redirect = JourneyId;
                    return;
            }
        }

        public static string Miles(float m) => m < 1f ? "less than a mile" : Math.Round(m) + (Math.Round(m) == 1 ? " mile" : " miles");
    }
}
