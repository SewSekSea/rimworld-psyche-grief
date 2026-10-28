using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace Grief
{
    /// <summary>
    /// The gathering itself. Subclassed rather than configured so that a wake can be recognised
    /// by its type: nothing else in the game is a LordJob_Wake, so there is no guessing at what
    /// a given party is actually for.
    /// </summary>
    public class LordJob_Wake : LordJob_Joinable_Party
    {
        public LordJob_Wake()
        {
        }

        public LordJob_Wake(IntVec3 spot, Pawn organizer, GatheringDef def)
            : base(spot, organizer, def)
        {
        }

        protected override ThoughtDef AttendeeThought => GR_DefOf.GR_AttendedWake;
        protected override ThoughtDef OrganizerThought => GR_DefOf.GR_AttendedWake;
        protected override TaleDef AttendeeTale => null;
        protected override TaleDef OrganizerTale => null;
    }

    /// <summary>
    /// A wake: the colony gathers at a grave and sits with one particular death.
    ///
    /// Burial is the floor. It stops a loss festering and does nothing more than that. The wake is
    /// the part that actually helps -- and it helps for exactly one person, the one in the grave.
    /// Someone carrying three losses who attends one wake walks away still carrying two.
    ///
    /// Wakes are not left to the storyteller's random gathering roll. A wake follows a burial; it
    /// is not something that happens to a colony on a slow afternoon.
    /// </summary>
    public class GatheringWorker_Wake : GatheringWorker
    {
        /// <summary>Roughly two hours at the graveside earns the full benefit. Leaving early earns a share.</summary>
        public const int FullAttendanceTicks = 5000;

        /// <summary>How close to the grave a mourner has to be for it to count as standing there.</summary>
        private const float AtGraveside = 12f;

        public override bool CanExecute(Map map, Pawn organizer)
        {
            return Tuning.Wakes && base.CanExecute(map, organizer) && SubjectGrave(map) != null;
        }

        /// <summary>
        /// The grave worth gathering at: an occupied one whose occupant at least two colonists are
        /// still openly grieving, and who has not been given a wake already. One person at a
        /// graveside is a visit, not a wake.
        /// </summary>
        public static Building_Grave SubjectGrave(Map map)
        {
            GameComponent_Grief ledger = GameComponent_Grief.Instance;
            if (ledger == null || map == null)
            {
                return null;
            }
            Building_Grave best = null;
            int bestMourners = 1;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_Grave grave = buildings[i] as Building_Grave;
                if (grave == null || !grave.HasCorpse)
                {
                    continue;
                }
                Pawn inside = grave.Corpse?.InnerPawn;
                if (inside == null || ledger.WakeHeldFor(inside))
                {
                    continue;
                }
                int mourners = ledger.MournerCount(inside);
                if (mourners > bestMourners)
                {
                    best = grave;
                    bestMourners = mourners;
                }
            }
            return best;
        }

        protected override bool TryFindGatherSpot(Pawn organizer, out IntVec3 spot)
        {
            spot = IntVec3.Invalid;
            Map map = organizer?.Map;
            Building_Grave grave = map == null ? null : SubjectGrave(map);
            if (grave == null)
            {
                return false;
            }
            return CellFinder.TryFindRandomCellNear(grave.Position, map, 5,
                c => c.Standable(map) && !c.IsForbidden(organizer) &&
                     map.reachability.CanReach(organizer.Position, c, PathEndMode.OnCell,
                         TraverseParms.For(organizer)),
                out spot);
        }

        protected override LordJob CreateLordJob(IntVec3 spot, Pawn organizer)
        {
            return new LordJob_Wake(spot, organizer, def);
        }

        /// <summary>
        /// Pay out the grief relief while people are standing there, rather than at the end.
        ///
        /// A wake broken up by a raid was still worth attending, and a colonist dragged off to
        /// fight should keep what the first hour of it gave them.
        /// </summary>
        public static void TickWakes(int interval)
        {
            if (!Tuning.Wakes)
            {
                return;
            }
            GameComponent_Grief ledger = GameComponent_Grief.Instance;
            if (ledger == null)
            {
                return;
            }
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                Map map = maps[m];
                List<Lord> lords = map.lordManager?.lords;
                if (lords == null)
                {
                    continue;
                }
                for (int i = 0; i < lords.Count; i++)
                {
                    Lord lord = lords[i];
                    LordJob_Wake wake = lord?.LordJob as LordJob_Wake;
                    if (wake == null)
                    {
                        continue;
                    }
                    Pawn mourned = BuriedNear(wake.Spot, map);
                    if (mourned == null)
                    {
                        continue;
                    }
                    ledger.NoteWakeHeld(mourned);

                    // A share of the full benefit, proportional to how long they have been here.
                    int slice = (int)(Tuning.WakeReliefDays * GenDate.TicksPerDay
                                      * ((float)interval / FullAttendanceTicks));
                    if (slice <= 0)
                    {
                        continue;
                    }
                    List<Pawn> present = lord.ownedPawns;
                    for (int p = 0; p < present.Count; p++)
                    {
                        Pawn attendee = present[p];
                        if (attendee == null || attendee.Dead || !attendee.Spawned)
                        {
                            continue;
                        }
                        if (!attendee.Position.InHorDistOf(wake.Spot, AtGraveside))
                        {
                            continue;
                        }
                        ledger.Relieve(attendee, mourned, slice);
                    }
                }
            }
        }

        /// <summary>Who is buried at this spot. The grave is the thing that says whose wake it is.</summary>
        private static Pawn BuriedNear(IntVec3 spot, Map map)
        {
            if (!spot.IsValid || map == null)
            {
                return null;
            }
            Building_Grave closest = null;
            float bestDist = float.MaxValue;
            List<Building> buildings = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building_Grave grave = buildings[i] as Building_Grave;
                if (grave == null || !grave.HasCorpse)
                {
                    continue;
                }
                float d = grave.Position.DistanceTo(spot);
                if (d < bestDist && d <= AtGraveside)
                {
                    bestDist = d;
                    closest = grave;
                }
            }
            return closest?.Corpse?.InnerPawn;
        }
    }
}
