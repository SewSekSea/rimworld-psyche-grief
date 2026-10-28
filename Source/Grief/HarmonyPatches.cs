using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Grief
{
    /// <summary>
    /// Memory intake. The base game's personal loss memories are intercepted: instead of a flat
    /// penalty that runs out, the loss is written into the ledger and felt in stages.
    ///
    /// Marked to run after the Sociopath mod, which decides for itself whether a colonist grieves
    /// at all. If it refuses the memory, this never runs, and a Sociopath goes on not caring.
    /// </summary>
    [HarmonyPatch(typeof(MemoryThoughtHandler), nameof(MemoryThoughtHandler.TryGainMemory),
        new[] { typeof(Thought_Memory), typeof(Pawn) })]
    [HarmonyAfter("sociopath.trait")]
    public static class Patch_TryGainMemory
    {
        public static bool Prefix(MemoryThoughtHandler __instance, Thought_Memory newThought, Pawn otherPawn)
        {
            if (newThought?.def == null || !GR.IsPersonalLossThought(newThought.def))
            {
                return true;
            }
            Pawn griever = __instance.pawn;
            Pawn lost = otherPawn ?? newThought.otherPawn;
            if (!GR.CanGrieve(griever) || lost == null || lost == griever)
            {
                return true;
            }
            GameComponent_Grief ledger = GameComponent_Grief.Instance;
            if (ledger == null)
            {
                return true;
            }
            ledger.Record(griever, lost, GR.Closeness(griever, lost));

            // Normally the original is swallowed and the staged thought carries it from here.
            //
            // In a heavily modded game something else may be reading these memories. With
            // "keep the base game's memory" switched on, the memory is left in place and its
            // mood contribution is zeroed instead, so other mods still find what they expect
            // and nothing is charged for the same death twice.
            return Tuning.KeepVanillaMemory;
        }
    }

    /// <summary>
    /// The other half of "keep the base game's memory". The memory stays so other mods can read
    /// it; this makes sure it contributes nothing to mood, because the staged grief already does.
    /// </summary>
    [HarmonyPatch(typeof(ThoughtHandler), nameof(ThoughtHandler.MoodOffsetOfGroup))]
    public static class Patch_MoodOffsetOfGroup
    {
        public static void Postfix(Thought group, ref float __result)
        {
            if (__result == 0f || !Tuning.KeepVanillaMemory)
            {
                return;
            }
            if (group is Thought_Memory && GR.IsPersonalLossThought(group.def))
            {
                __result = 0f;
            }
        }
    }

    /// <summary>
    /// Survivor's guilt: being close enough to watch it happen and walking away unhurt.
    /// </summary>
    [HarmonyPatch(typeof(PawnDiedOrDownedThoughtsUtility), nameof(PawnDiedOrDownedThoughtsUtility.TryGiveThoughts),
        new[] { typeof(Pawn), typeof(DamageInfo?), typeof(PawnDiedOrDownedThoughtsKind) })]
    public static class Patch_TryGiveThoughts
    {
        public static void Postfix(Pawn victim, DamageInfo? dinfo, PawnDiedOrDownedThoughtsKind thoughtsKind)
        {
            if (!Tuning.SurvivorsGuilt || thoughtsKind != PawnDiedOrDownedThoughtsKind.Died)
            {
                return;
            }
            if (victim?.RaceProps == null || !victim.RaceProps.Humanlike || !victim.IsColonist)
            {
                return;
            }
            Map map = victim.MapHeld;
            if (map == null || !victim.PositionHeld.IsValid)
            {
                return;
            }
            Pawn killer = dinfo?.Instigator as Pawn;
            float radiusSq = Tuning.SurvivorRadius * Tuning.SurvivorRadius;
            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn witness = colonists[i];
                if (witness == victim || witness == killer || !GR.CanGrieve(witness) || !witness.Awake())
                {
                    continue;
                }
                // Only people who came through it untouched carry this.
                if (witness.Downed || witness.health?.hediffSet == null ||
                    witness.health.hediffSet.BleedRateTotal > 0.01f)
                {
                    continue;
                }
                if (witness.PositionHeld.DistanceToSquared(victim.PositionHeld) > radiusSq)
                {
                    continue;
                }
                witness.needs?.mood?.thoughts?.memories?.TryGainMemory(GR_DefOf.GR_SurvivorsGuilt, victim);
            }
        }
    }
}
