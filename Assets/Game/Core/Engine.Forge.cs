using System;
using System.Text;

namespace Loran.Core
{
    /// <summary>
    /// Smithing. A forge effect puts a piece on the anvil; the screen then runs three steps (heat, hammer, quench),
    /// each scored 0 to 1, and hands the scores to FinishForge. The quality they add up to sets the rank.
    /// </summary>
    public partial class Engine
    {
        public const string ForgeId = "forge";

        /// <summary>Quality at or above this lifts the piece a rank. Below CrudeBelow it comes out crude.</summary>
        public const int FineQuality = 75, CrudeBelow = 30;

        public GearDef OnAnvil => data.Gear(S.forging);
        public MaterialDef ForgeMaterial => data.Material(S.forgeMaterial);

        SceneDef ForgeScene()
        {
            var g = OnAnvil;
            var sc = new SceneDef { id = ForgeId, place = Scene(S.forgeNext)?.place ?? "", title = "At the anvil: " + g.name };
            sc.text.Add("Heat the metal, hammer it into shape, then quench it.");
            // Without a screen to play the forge on (the tests), the smith's own skill decides.
            sc.choices.Add(new ChoiceDef { label = "Work the forge", effects = { "forge:auto" } });
            return sc;
        }

        void Forge(string[] parts, StringBuilder notes, ref string redirect)
        {
            if (parts[0] == "auto")
            {
                float skill = Math.Clamp(0.45f + S.stats[Stat.Str] * 0.03f, 0f, 0.9f);
                redirect = CompleteForge(skill, skill, skill, notes);
                return;
            }
            // forge:base:maxRank[:material]
            var g = data.Gear(parts[0]) ?? throw new Exception("Unknown gear '" + parts[0] + "'");
            S.forging = g.id;
            S.forgeMax = parts[1];
            S.forgeMaterial = parts.Length > 2 ? parts[2] : "";
            S.forgeNext = string.IsNullOrEmpty(choosingGo) ? S.scene : choosingGo;   // after forging, go where the choice was going
            redirect = ForgeId;
        }

        /// <summary>The forge screen calls this with how well each step went (0 to 1).</summary>
        public void FinishForge(float heat, float hammer, float quench)
        {
            if (S.forging == "") return;
            var notes = new StringBuilder();
            string next = CompleteForge(heat, hammer, quench, notes);
            S.result = notes.ToString();
            GoTo(next);
        }

        /// <summary>The quality (0-100) that three step scores add up to, with a little help from Strength.</summary>
        public int ForgeQuality(float heat, float hammer, float quench)
        {
            float q = 100f * (0.35f * Math.Clamp(heat, 0, 1) + 0.45f * Math.Clamp(hammer, 0, 1) + 0.2f * Math.Clamp(quench, 0, 1));
            q += (S.stats[Stat.Str] - 3) * 2f;
            q += SkillLevel("smithing") * 3f;     // practice at the anvil shows
            return (int)Math.Round(Math.Clamp(q, 0f, 100f));
        }

        string CompleteForge(float heat, float hammer, float quench, StringBuilder notes)
        {
            int q = ForgeQuality(heat, hammer, quench);
            var mat = ForgeMaterial;
            int rank = mat != null ? 1 : 0;
            if (q >= FineQuality) rank++;
            rank = Math.Min(rank, Rules.RankIndex(S.forgeMax));
            bool crude = q < CrudeBelow;
            if (crude) rank = 0;

            var item = MakeGear(S.forging, Rules.Ranks[rank].ToString(), mat?.id, crude ? 0.8f : 1f, crude ? "Crude" : null);
            Rules.Train(S, Stat.Str, 1f + hammer, "working the forge");
            Rules.Train(S, Stat.Int, 0.5f + quench * 0.5f, "working the forge");
            TrainSkill("smithing", 6 + q / 20f, "working the forge");

            notes.Append("Quality " + q + " out of 100. ");
            notes.Append(q >= 90 ? "Garran takes it from you and turns it in the light for a long time. \"Where did you learn that?\""
                       : q >= FineQuality ? "Garran nods slowly. \"That's good work. Better than good.\""
                       : q >= 50 ? "Garran grunts. \"It'll do the job.\""
                       : q >= CrudeBelow ? "Garran winces. \"It'll hold. Probably.\""
                       : "Garran sighs. \"Well. It's a start.\"");
            S.Toasts.Add("Forged: " + item.name + " (" + item.rank + " rank).");

            string next = string.IsNullOrEmpty(S.forgeNext) ? TravelId : S.forgeNext;
            S.forging = S.forgeMaterial = S.forgeMax = S.forgeNext = S.forgeIntro = "";
            return next;
        }
    }
}
