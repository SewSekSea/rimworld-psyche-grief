using System.Collections.Generic;
using RimWorld;
using Verse;

namespace Grief
{
    /// <summary>One person's loss of one other person, and how far along it is.</summary>
    public class Loss : IExposable
    {
        public Pawn griever;
        public Pawn lost;
        public int diedTick;
        public float closeness = 0.35f;

        // Where it happened, so the room can keep its edge for a while.
        public int deathMapId = -1;
        public int deathRoomId = -1;

        // Latched the first time the body is found in a grave. Robbing the grave later does not
        // reopen the wound; they were laid to rest and the griever knows it.
        public bool everBuried;

        // Years already marked, so an anniversary fires once each time.
        public int anniversariesMarked;

        // Ticks of the arc skipped by talking it over with someone who lost the same person.
        public int reliefTicks;

        public Loss() { }

        public Loss(Pawn griever, Pawn lost, float closeness)
        {
            this.griever = griever;
            this.lost = lost;
            this.closeness = closeness;
            diedTick = Find.TickManager.TicksGame;
        }

        /// <summary>
        /// Still a loss at all. Someone who has been brought back is not dead, and grieving for a
        /// person standing in front of you is not a thing this mod should make colonists do.
        /// </summary>
        public bool Valid => griever != null && lost != null && !griever.Dead && lost.Dead;

        /// <summary>The whole arc in ticks. A closer loss takes longer.</summary>
        public int SpanTicks =>
            (int)(Tuning.BaseGriefDays * (0.4f + closeness) * GenDate.TicksPerDay);

        /// <summary>Time since the death, plus anything talking it over has moved along.</summary>
        public int Elapsed
        {
            get
            {
                int cap = (int)(SpanTicks * Tuning.ReliefCapFraction);
                int relief = reliefTicks > cap ? cap : reliefTicks;
                return Find.TickManager.TicksGame - diedTick + relief;
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref griever, "griever");
            Scribe_References.Look(ref lost, "lost", saveDestroyedThings: true);
            Scribe_Values.Look(ref diedTick, "diedTick", 0);
            Scribe_Values.Look(ref closeness, "closeness", 0.35f);
            Scribe_Values.Look(ref deathMapId, "deathMapId", -1);
            Scribe_Values.Look(ref deathRoomId, "deathRoomId", -1);
            Scribe_Values.Look(ref everBuried, "everBuried", false);
            Scribe_Values.Look(ref anniversariesMarked, "anniversariesMarked", 0);
            Scribe_Values.Look(ref reliefTicks, "reliefTicks", 0);
        }
    }

    /// <summary>
    /// The grief ledger. Holds every open loss, notes how long each colonist has been here,
    /// and keeps a periodically refreshed list of who has actually been buried.
    /// </summary>
    public class GameComponent_Grief : GameComponent
    {
        private const int TickInterval = 500;

        /// <summary>How long after a death before a missing body counts as having been dealt with.</summary>
        private const int NoBodyGraceTicks = 2500;

        private List<Loss> losses = new List<Loss>();

        // How long each colonist has been with the colony. Counted from when this mod is added.
        private Dictionary<int, int> arrived = new Dictionary<int, int>();
        private List<int> arrivedKeys;
        private List<int> arrivedValues;

        // Who has already been given a wake. One each; the second gathering is just a party.
        private HashSet<int> wakesHeld = new HashSet<int>();
        private List<int> wakesHeldList;

        // Checked occasionally rather than every interval, so a wake is something the colony gets
        // round to in the days after a burial instead of the moment the last shovelful lands.
        private const int WakeCheckInterval = 7500;
        private const float WakeChancePerCheck = 0.35f;

        private static readonly HashSet<int> buriedIds = new HashSet<int>();
        private static GameComponent_Grief instance;

        public GameComponent_Grief(Game game)
        {
            instance = this;
        }

        public static GameComponent_Grief Instance => instance ?? Current.Game?.GetComponent<GameComponent_Grief>();

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref losses, "losses", LookMode.Deep);
            Scribe_Collections.Look(ref arrived, "arrived", LookMode.Value, LookMode.Value,
                ref arrivedKeys, ref arrivedValues);
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                wakesHeldList = new List<int>(wakesHeld);
            }
            Scribe_Collections.Look(ref wakesHeldList, "wakesHeld", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                wakesHeld = wakesHeldList == null ? new HashSet<int>() : new HashSet<int>(wakesHeldList);
            }
            if (losses == null) { losses = new List<Loss>(); }
            if (arrived == null) { arrived = new Dictionary<int, int>(); }
            if (wakesHeld == null) { wakesHeld = new HashSet<int>(); }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                losses.RemoveAll(l => l == null || l.griever == null || l.lost == null);
            }
        }

        public override void FinalizeInit()
        {
            instance = this;
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % TickInterval != 0)
            {
                return;
            }
            NoteArrivals();
            RefreshGraves();
            Prune();
            MarkAnniversaries();
            GatheringWorker_Wake.TickWakes(TickInterval);
            if (Find.TickManager.TicksGame % WakeCheckInterval == 0)
            {
                TryHoldWake();
            }
        }

        /// <summary>
        /// See whether the colony gets round to holding a wake. Deliberately a chance rather than a
        /// certainty: some deaths get a gathering and some quietly do not, which is truer to the
        /// thing and stops wakes reading as a button you press to clear grief.
        /// </summary>
        private void TryHoldWake()
        {
            if (!Tuning.Wakes || !Rand.Chance(WakeChancePerCheck))
            {
                return;
            }
            GatheringDef def = GR_DefOf.GR_Wake;
            GatheringWorker_Wake worker = def?.Worker as GatheringWorker_Wake;
            if (worker == null)
            {
                return;
            }
            List<Map> maps = Find.Maps;
            for (int i = 0; i < maps.Count; i++)
            {
                Map map = maps[i];
                if (map?.IsPlayerHome != true)
                {
                    continue;
                }
                if (worker.CanExecute(map, null))
                {
                    worker.TryExecute(map, null);
                    return;
                }
            }
        }

        // ---- tenure ------------------------------------------------------

        private void NoteArrivals()
        {
            int now = Find.TickManager.TicksGame;
            List<Pawn> colonists = PawnsFinder.AllMaps_FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p != null && !arrived.ContainsKey(p.thingIDNumber))
                {
                    arrived[p.thingIDNumber] = now;
                }
            }
        }

        /// <summary>Days this person had been with the colony, as far as this mod ever saw.</summary>
        public static float TenureDays(Pawn p)
        {
            GameComponent_Grief g = Instance;
            if (g == null || p == null || !g.arrived.TryGetValue(p.thingIDNumber, out int when))
            {
                return 0f;
            }
            return (Find.TickManager.TicksGame - when) / (float)GenDate.TicksPerDay;
        }

        // ---- burial ------------------------------------------------------

        private static void RefreshGraves()
        {
            buriedIds.Clear();
            List<Map> maps = Find.Maps;
            for (int m = 0; m < maps.Count; m++)
            {
                List<Building> graves = maps[m].listerBuildings.allBuildingsColonist;
                for (int i = 0; i < graves.Count; i++)
                {
                    if (graves[i] is Building_Grave grave && grave.HasCorpse)
                    {
                        Pawn inside = grave.Corpse?.InnerPawn;
                        if (inside != null)
                        {
                            buriedIds.Add(inside.thingIDNumber);
                        }
                    }
                }
            }
        }

        public static bool IsBuried(Pawn lost)
        {
            return lost != null && buriedIds.Contains(lost.thingIDNumber);
        }

        // ---- the ledger --------------------------------------------------

        public void Record(Pawn griever, Pawn lost, float closeness)
        {
            if (griever == null || lost == null || griever == lost)
            {
                return;
            }
            for (int i = 0; i < losses.Count; i++)
            {
                if (losses[i].griever == griever && losses[i].lost == lost)
                {
                    return;
                }
            }
            Loss loss = new Loss(griever, lost, closeness);
            Corpse corpse = lost.Corpse;
            IntVec3 where = corpse != null && corpse.Spawned ? corpse.Position : lost.PositionHeld;
            Map map = corpse != null && corpse.Spawned ? corpse.Map : lost.MapHeld;
            if (map != null && where.IsValid)
            {
                loss.deathMapId = map.uniqueID;
                Room room = where.GetRoom(map);
                loss.deathRoomId = room != null && !room.PsychologicallyOutdoors ? room.ID : -1;
            }
            losses.Add(loss);
        }

        private void Prune()
        {
            for (int i = losses.Count - 1; i >= 0; i--)
            {
                Loss l = losses[i];
                if (l == null || !l.Valid)
                {
                    losses.RemoveAt(i);
                    continue;
                }
                if (!l.everBuried && IsBuried(l.lost))
                {
                    l.everBuried = true;
                }
                // A body that no longer exists anywhere has been dealt with: cremated, or given
                // whatever send-off this colony goes in for. That is a deliberate act, and treating
                // it as worse than leaving someone in the freezer was plainly the wrong way round.
                // Only counted after a short grace period, so a corpse mid-creation is not mistaken
                // for one that was disposed of.
                else if (!l.everBuried && l.lost.Corpse == null &&
                         Find.TickManager.TicksGame - l.diedTick > NoBodyGraceTicks)
                {
                    l.everBuried = true;
                }
                // Once the arc is done and it is not one of the losses that stays with them, drop it.
                bool longTail = l.closeness >= 0.7f;
                int keepFor = longTail ? l.SpanTicks * 4 : l.SpanTicks;
                if (l.everBuried && l.Elapsed > keepFor)
                {
                    losses.RemoveAt(i);
                }
            }
        }

        private void MarkAnniversaries()
        {
            if (!Tuning.Anniversaries)
            {
                return;
            }
            int yearTicks = Tuning.DaysPerYear * GenDate.TicksPerDay;
            for (int i = 0; i < losses.Count; i++)
            {
                Loss l = losses[i];
                if (!l.Valid || l.griever.needs?.mood == null)
                {
                    continue;
                }
                int years = (Find.TickManager.TicksGame - l.diedTick) / yearTicks;
                if (years > l.anniversariesMarked)
                {
                    l.anniversariesMarked = years;
                    l.griever.needs.mood.thoughts.memories.TryGainMemory(GR_DefOf.GR_Anniversary, l.lost);
                }
            }
        }

        // ---- queries used by the thought workers -------------------------

        /// <summary>Every loss this person is still carrying.</summary>
        public List<Loss> OpenLosses(Pawn griever, List<Loss> into)
        {
            into.Clear();
            for (int i = 0; i < losses.Count; i++)
            {
                Loss l = losses[i];
                if (l != null && l.griever == griever && l.Valid)
                {
                    into.Add(l);
                }
            }
            return into;
        }

        /// <summary>The loss weighing heaviest on them right now, or null.</summary>
        public Loss Heaviest(Pawn griever)
        {
            Loss best = null;
            float bestWeight = 0f;
            for (int i = 0; i < losses.Count; i++)
            {
                Loss l = losses[i];
                if (l == null || l.griever != griever || !l.Valid)
                {
                    continue;
                }
                int stage = Stages.Of(l);
                if (stage < 0)
                {
                    continue;
                }
                float weight = Stages.Weight(stage) * l.closeness;
                if (weight > bestWeight)
                {
                    best = l;
                    bestWeight = weight;
                }
            }
            return best;
        }

        /// <summary>The loss these two people share, if they are both carrying the same one.</summary>
        public Loss Shared(Pawn a, Pawn b)
        {
            for (int i = 0; i < losses.Count; i++)
            {
                Loss l = losses[i];
                if (l == null || l.griever != a || !l.Valid || Stages.Of(l) < 0)
                {
                    continue;
                }
                for (int j = 0; j < losses.Count; j++)
                {
                    Loss o = losses[j];
                    if (o != null && o.griever == b && o.lost == l.lost && o.Valid && Stages.Of(o) >= 0)
                    {
                        return l;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Move one particular loss along, for one particular person.
        ///
        /// Targeted on purpose. Anything that eases grief has to name who it is easing it for --
        /// otherwise one act of closure quietly settles every other death that colonist is still
        /// carrying, and a single funeral clears a lifetime.
        /// </summary>
        public bool Relieve(Pawn griever, Pawn lost, int ticks)
        {
            if (griever == null || lost == null || ticks <= 0)
            {
                return false;
            }
            for (int i = 0; i < losses.Count; i++)
            {
                Loss l = losses[i];
                if (l != null && l.griever == griever && l.lost == lost && l.Valid)
                {
                    l.reliefTicks += ticks;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Talking it over moves both of them along -- for the person they both lost, and no one else.
        /// </summary>
        public void RememberTogether(Pawn a, Pawn b)
        {
            Loss shared = Shared(a, b);
            if (shared == null)
            {
                return;
            }
            int add = (int)(Tuning.ReliefDaysPerTalk * GenDate.TicksPerDay);
            Relieve(a, shared.lost, add);
            if (b != a)
            {
                Relieve(b, shared.lost, add);
            }
        }

        /// <summary>How many colonists are still openly grieving this person.</summary>
        public int MournerCount(Pawn lost)
        {
            if (lost == null)
            {
                return 0;
            }
            int n = 0;
            for (int i = 0; i < losses.Count; i++)
            {
                Loss l = losses[i];
                if (l != null && l.lost == lost && l.Valid && Stages.Of(l) >= 0 &&
                    l.griever.Spawned && l.griever.IsColonist)
                {
                    n++;
                }
            }
            return n;
        }

        /// <summary>A person gets one wake. The second gathering is just a party at a graveside.</summary>
        public bool WakeHeldFor(Pawn lost)
        {
            return lost != null && wakesHeld.Contains(lost.thingIDNumber);
        }

        public void NoteWakeHeld(Pawn lost)
        {
            if (lost != null)
            {
                wakesHeld.Add(lost.thingIDNumber);
            }
        }
    }
}
