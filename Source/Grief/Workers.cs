using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grief
{
    /// <summary>Where in the arc a loss currently sits.</summary>
    public static class Stages
    {
        public const int Shock = 0;
        public const int Raw = 1;
        public const int Aching = 2;
        public const int Dull = 3;
        public const int LongTail = 4;
        public const int NoGrave = 5;

        /// <summary>The stage for this loss, or -1 if it has run its course.</summary>
        public static int Of(Loss l)
        {
            if (l == null || !l.Valid)
            {
                return -1;
            }
            float span = l.SpanTicks;
            float t = l.Elapsed / span;

            if (t < 0.02f) { return Shock; }
            if (t < 0.15f) { return Raw; }

            // Nobody ever put them in the ground. It does not move on by itself.
            if (!l.everBuried && !GameComponent_Grief.IsBuried(l.lost))
            {
                return NoGrave;
            }

            if (t < 0.45f) { return Aching; }
            if (t < 1f) { return Dull; }
            return l.closeness >= 0.7f ? LongTail : -1;
        }

        /// <summary>Rough severity, used only to decide which of several losses is uppermost.</summary>
        public static float Weight(int stage)
        {
            switch (stage)
            {
                case Shock: return 0.5f;
                case Raw: return 1f;
                case NoGrave: return 1.1f;
                case Aching: return 0.6f;
                case Dull: return 0.3f;
                case LongTail: return 0.1f;
                default: return 0f;
            }
        }
    }

    /// <summary>
    /// The grief thought itself. Scales with how close they were, which a plain
    /// situational thought cannot do on its own.
    /// </summary>
    public class Thought_Grief : Thought_Situational
    {
        public override float MoodOffset()
        {
            float baseOffset = base.MoodOffset();
            if (baseOffset == 0f)
            {
                return 0f;
            }
            Loss l = GameComponent_Grief.Instance?.Heaviest(pawn);
            float closeness = l == null ? 0.35f : l.closeness;
            return baseOffset * closeness * Tuning.Intensity;
        }
    }

    public class ThoughtWorker_Grieving : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!GR.CanGrieve(p))
            {
                return ThoughtState.Inactive;
            }
            Loss l = GameComponent_Grief.Instance?.Heaviest(p);
            if (l == null)
            {
                return ThoughtState.Inactive;
            }
            int stage = Stages.Of(l);
            return stage < 0 ? ThoughtState.Inactive : ThoughtState.ActiveAtStage(stage);
        }
    }

    /// <summary>More than one loss still open at once.</summary>
    public class ThoughtWorker_MoreLosses : ThoughtWorker
    {
        private static readonly List<Loss> buffer = new List<Loss>();

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            GameComponent_Grief ledger = GameComponent_Grief.Instance;
            if (ledger == null || !GR.CanGrieve(p))
            {
                return ThoughtState.Inactive;
            }
            ledger.OpenLosses(p, buffer);
            int active = 0;
            for (int i = 0; i < buffer.Count; i++)
            {
                int stage = Stages.Of(buffer[i]);
                if (stage >= 0 && stage != Stages.LongTail)
                {
                    active++;
                }
            }
            buffer.Clear();
            if (active >= 4) { return ThoughtState.ActiveAtStage(1); }
            return active >= 2 ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    /// <summary>They were laid to rest properly, and it helps.</summary>
    public class ThoughtWorker_AtRest : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!GR.CanGrieve(p))
            {
                return ThoughtState.Inactive;
            }
            Loss l = GameComponent_Grief.Instance?.Heaviest(p);
            if (l == null || Stages.Of(l) < 0)
            {
                return ThoughtState.Inactive;
            }
            bool buried = l.everBuried || GameComponent_Grief.IsBuried(l.lost);
            return buried ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    /// <summary>Standing in the room where it happened.</summary>
    public class ThoughtWorker_WhereItHappened : ThoughtWorker
    {
        private static readonly List<Loss> buffer = new List<Loss>();

        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            GameComponent_Grief ledger = GameComponent_Grief.Instance;
            if (ledger == null || !Tuning.WhereItHappened || !GR.CanGrieve(p) || !p.Spawned || p.Map == null)
            {
                return ThoughtState.Inactive;
            }
            Room room = p.GetRoom();
            if (room == null || room.PsychologicallyOutdoors)
            {
                return ThoughtState.Inactive;
            }
            int limit = (int)(Tuning.WhereItHappenedDays * GenDate.TicksPerDay);
            ledger.OpenLosses(p, buffer);
            bool hit = false;
            for (int i = 0; i < buffer.Count; i++)
            {
                Loss l = buffer[i];
                if (l.deathRoomId == room.ID && l.deathMapId == p.Map.uniqueID && l.Elapsed <= limit)
                {
                    hit = true;
                    break;
                }
            }
            buffer.Clear();
            return hit ? ThoughtState.ActiveAtStage(0) : ThoughtState.Inactive;
        }
    }

    /// <summary>
    /// Two people who lost the same person talk about them. Only possible while both are
    /// still carrying it, so it cannot come out of nowhere years later.
    /// </summary>
    public class InteractionWorker_Remember : InteractionWorker
    {
        public override float RandomSelectionWeight(Pawn initiator, Pawn recipient)
        {
            if (!Tuning.SharedGrief || !GR.CanGrieve(initiator) || !GR.CanGrieve(recipient))
            {
                return 0f;
            }
            GameComponent_Grief ledger = GameComponent_Grief.Instance;
            if (ledger == null || ledger.Shared(initiator, recipient) == null)
            {
                return 0f;
            }
            return Tuning.RememberWeight;
        }

        public override void Interacted(Pawn initiator, Pawn recipient, List<RulePackDef> extraSentencePacks,
            out string letterText, out string letterLabel, out LetterDef letterDef, out LookTargets lookTargets)
        {
            letterText = null;
            letterLabel = null;
            letterDef = null;
            lookTargets = null;

            GameComponent_Grief ledger = GameComponent_Grief.Instance;
            if (ledger == null)
            {
                return;
            }
            ledger.RememberTogether(initiator, recipient);
            // The recipient's side is handled by recipientThought on the def; this is the initiator's.
            initiator.needs?.mood?.thoughts?.memories?.TryGainMemory(GR_DefOf.GR_RememberedTogether, recipient);
        }
    }
}
