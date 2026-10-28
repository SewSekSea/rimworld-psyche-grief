using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grief
{
    [DefOf]
    public static class GR_DefOf
    {
        public static ThoughtDef GR_Grieving;
        public static ThoughtDef GR_SurvivorsGuilt;
        public static ThoughtDef GR_Anniversary;
        public static ThoughtDef GR_RememberedTogether;
        public static InteractionDef GR_Remember;
        public static ThoughtDef GR_AttendedWake;
        public static GatheringDef GR_Wake;

        static GR_DefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(GR_DefOf));
        }
    }

    /// <summary>All the tuning numbers in one place. The ones players can change come from the settings menu.</summary>
    public static class Tuning
    {
        private static GriefSettings S => GriefMod.Settings;

        /// <summary>Length of the whole arc for an average loss, before closeness stretches it.</summary>
        public static float BaseGriefDays => S.baseGriefDays;

        /// <summary>Overall multiplier on how hard grief hits.</summary>
        public static float Intensity => S.intensity;

        /// <summary>How long the room where someone died keeps its edge.</summary>
        public static float WhereItHappenedDays => S.whereItHappenedDays;

        /// <summary>Leave the base game's own loss memory in place for other mods to read.</summary>
        public static bool KeepVanillaMemory => S.keepVanillaMemory;

        public static bool WhereItHappened => S.whereItHappened;
        public static bool SurvivorsGuilt => S.survivorsGuilt;
        public static bool Anniversaries => S.anniversaries;
        public static bool SharedGrief => S.sharedGrief;
        public static float RememberWeight => S.rememberWeight;
        public static bool Wakes => S.wakes;

        /// <summary>What a full wake is worth, in days of the arc, to each person who attends it.</summary>
        public static float WakeReliefDays => S.wakeReliefDays;

        /// <summary>How far from a death a colonist has to be to carry survivor's guilt.</summary>
        public const float SurvivorRadius = 12f;

        /// <summary>One shared remembrance is worth this much of the arc, and they cannot skip more than the cap.</summary>
        public const float ReliefDaysPerTalk = 1.5f;
        public const float ReliefCapFraction = 0.4f;

        /// <summary>Time in the colony that counts as "one of us", in days, for the closeness bonus.</summary>
        public const float TenureDaysForFullBonus = 60f;
        public const float TenureBonus = 0.15f;

        /// <summary>A RimWorld year.</summary>
        public const int DaysPerYear = 60;
    }

    [StaticConstructorOnStartup]
    public static class GR
    {
        /// <summary>Squad's battle bond, if that mod is installed. Counts as a close relationship.</summary>
        public static readonly ThoughtDef BattleBonded;

        /// <summary>Squad PTSD's mark. Someone hardened by war does not feel a loss the same way.</summary>
        public static readonly HediffDef WarHardened;

        /// <summary>Machiavelli's rating, if installed. Losing a useful person stings practically as well.</summary>
        public static readonly ThoughtDef RatedUseful;

        static GR()
        {
            BattleBonded = DefDatabase<ThoughtDef>.GetNamedSilentFail("SQ_BattleBonded");
            WarHardened = DefDatabase<HediffDef>.GetNamedSilentFail("SQP_WarHardened");
            RatedUseful = DefDatabase<ThoughtDef>.GetNamedSilentFail("MAC_Useful");

            new Harmony("grief.mourning").PatchAll();
            Log.Message("[Grief] Loaded.");
        }

        /// <summary>
        /// The base game's personal loss memories, which this mod replaces with a staged one.
        /// Matched by shape rather than by a list of names, so modded relations are covered too.
        /// "Knew a colonist died" and "witnessed a death" are shock, not grief, and are left alone.
        /// </summary>
        public static bool IsPersonalLossThought(ThoughtDef def)
        {
            if (def == null)
            {
                return false;
            }
            string n = def.defName;
            if (n == "PawnWithGoodOpinionDied")
            {
                return true;
            }
            return n.Length > 6 && n.StartsWith("My") && n.EndsWith("Died");
        }

        /// <summary>
        /// How much this loss is worth to this person, from 0 (barely knew them) to about 1 (spouse, child).
        /// Relationship first, then how highly they rated them, then how long the dead person had been here.
        /// </summary>
        public static float Closeness(Pawn griever, Pawn lost)
        {
            if (griever == null || lost == null)
            {
                return 0.35f;
            }
            float best = 0f;
            foreach (PawnRelationDef rel in griever.GetRelations(lost))
            {
                float v = 0f;
                if (rel == PawnRelationDefOf.Spouse || rel == PawnRelationDefOf.Lover ||
                    rel == PawnRelationDefOf.Fiance || rel == PawnRelationDefOf.Child ||
                    rel == PawnRelationDefOf.Parent)
                {
                    v = 1f;
                }
                else if (rel == PawnRelationDefOf.Sibling || rel.familyByBloodRelation)
                {
                    v = 0.8f;
                }
                if (v > best)
                {
                    best = v;
                }
            }

            if (best <= 0f)
            {
                int opinion = griever.relations == null ? 0 : griever.relations.OpinionOf(lost);
                if (opinion >= 60) { best = 0.7f; }
                else if (opinion >= 20) { best = 0.5f; }
                else if (opinion >= 0) { best = 0.35f; }
                else { best = 0.15f; }

                // Squad: people who fought together and came through it are close, whatever their opinion says.
                if (BattleBonded != null && best < 0.7f && HasMemoryAbout(griever, BattleBonded, lost))
                {
                    best = 0.7f;
                }
            }

            // Someone who had been here for years is missed more than a recent arrival.
            float tenureDays = GameComponent_Grief.TenureDays(lost);
            float tenure = tenureDays >= Tuning.TenureDaysForFullBonus
                ? Tuning.TenureBonus
                : Tuning.TenureBonus * (tenureDays / Tuning.TenureDaysForFullBonus);

            // Machiavelli: losing someone they rated as carrying the colony is a practical blow too.
            if (RatedUseful != null && RatedUseful.Worker != null)
            {
                // Checked through the thought rather than the other mod's code, so this mod never needs it.
                try
                {
                    if (RatedUseful.Worker.CurrentSocialState(griever, lost).Active)
                    {
                        tenure += 0.1f;
                    }
                }
                catch
                {
                    // The other mod can refuse for its own reasons. Never let that break a death.
                }
            }

            float total = best + tenure;

            // Squad PTSD: someone already hardened by war does not take it the same way.
            if (WarHardened != null && griever.health?.hediffSet != null &&
                griever.health.hediffSet.HasHediff(WarHardened))
            {
                total *= 0.5f;
            }
            return Mathf01(total);
        }

        private static float Mathf01(float v)
        {
            if (v < 0.05f) { return 0.05f; }
            return v > 1.2f ? 1.2f : v;
        }

        public static bool HasMemoryAbout(Pawn pawn, ThoughtDef def, Pawn about)
        {
            if (def == null || about == null)
            {
                return false;
            }
            List<Thought_Memory> memories = pawn?.needs?.mood?.thoughts?.memories?.Memories;
            if (memories == null)
            {
                return false;
            }
            for (int i = 0; i < memories.Count; i++)
            {
                if (memories[i].def == def && memories[i].otherPawn == about)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Colonists of this colony, grown, alive, and able to feel anything.</summary>
        public static bool CanGrieve(Pawn p)
        {
            return p != null && !p.Dead && p.RaceProps != null && p.RaceProps.Humanlike &&
                   p.needs?.mood != null && !p.DevelopmentalStage.Juvenile();
        }
    }
}
