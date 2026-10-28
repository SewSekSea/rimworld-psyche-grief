using UnityEngine;
using Verse;

namespace Grief
{
    public class GriefSettings : ModSettings
    {
        public float baseGriefDays = 45f;
        public float intensity = 1f;
        public float whereItHappenedDays = 15f;
        public float rememberWeight = 0.3f;

        public bool keepVanillaMemory = false;
        public bool whereItHappened = true;
        public bool survivorsGuilt = true;
        public bool anniversaries = true;
        public bool sharedGrief = true;
        public bool wakes = true;
        public float wakeReliefDays = 4f;

        public override void ExposeData()
        {
            GriefSettings d = new GriefSettings();
            Scribe_Values.Look(ref baseGriefDays, "baseGriefDays", d.baseGriefDays);
            Scribe_Values.Look(ref intensity, "intensity", d.intensity);
            Scribe_Values.Look(ref whereItHappenedDays, "whereItHappenedDays", d.whereItHappenedDays);
            Scribe_Values.Look(ref rememberWeight, "rememberWeight", d.rememberWeight);
            Scribe_Values.Look(ref keepVanillaMemory, "keepVanillaMemory", false);
            Scribe_Values.Look(ref whereItHappened, "whereItHappened", true);
            Scribe_Values.Look(ref survivorsGuilt, "survivorsGuilt", true);
            Scribe_Values.Look(ref anniversaries, "anniversaries", true);
            Scribe_Values.Look(ref sharedGrief, "sharedGrief", true);
            Scribe_Values.Look(ref wakes, "wakes", true);
            Scribe_Values.Look(ref wakeReliefDays, "wakeReliefDays", d.wakeReliefDays);
        }

        public void Reset()
        {
            GriefSettings d = new GriefSettings();
            baseGriefDays = d.baseGriefDays;
            intensity = d.intensity;
            whereItHappenedDays = d.whereItHappenedDays;
            rememberWeight = d.rememberWeight;
            keepVanillaMemory = false;
            whereItHappened = survivorsGuilt = anniversaries = sharedGrief = true;
        }
    }

    public class GriefMod : Mod
    {
        public static GriefSettings Settings = new GriefSettings();

        public GriefMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<GriefSettings>();
        }

        public override string SettingsCategory() => "Grief";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            GriefSettings s = Settings;
            Listing_Standard list = new Listing_Standard { ColumnWidth = (inRect.width - 34f) / 2f };
            list.Begin(inRect);

            list.Label("How grief runs");
            s.baseGriefDays = list.SliderLabeled($"An average loss lasts about {s.baseGriefDays:0} days",
                s.baseGriefDays, 10f, 120f, 0.6f,
                "The length of the whole arc for an ordinary loss. A spouse or a child stretches it further; someone they barely knew is shorter. The base game's flat penalty was 30 days.");
            s.intensity = list.SliderLabeled($"Felt at {s.intensity:P0}",
                s.intensity, 0.25f, 2f, 0.6f,
                "Overall strength of the grief penalty. Everything scales from here.");
            s.whereItHappenedDays = list.SliderLabeled($"The room stays hard for {s.whereItHappenedDays:0} days",
                s.whereItHappenedDays, 1f, 60f, 0.6f);
            s.rememberWeight = list.SliderLabeled($"Remembering together, frequency {s.rememberWeight:0.00}",
                s.rememberWeight, 0f, 1f, 0.6f,
                "How often two colonists who lost the same person talk about them. Vanilla chit-chat is 1.00.");

            list.NewColumn();
            list.Label("Optional behaviours");
            list.CheckboxLabeled("Where it happened", ref s.whereItHappened,
                "Being in the room where someone died is hard for a while. Mood only; it never stops anyone working there.");
            list.CheckboxLabeled("Survivor's guilt", ref s.survivorsGuilt,
                "Colonists standing next to someone who died, who come through it unhurt, carry it.");
            list.CheckboxLabeled("Anniversaries", ref s.anniversaries,
                "A year to the day, it comes back briefly.");
            list.CheckboxLabeled("Shared grief", ref s.sharedGrief,
                "Two colonists who lost the same person can talk about them, which helps them both along.");
            list.CheckboxLabeled("Wakes", ref s.wakes,
                "After a burial the colony may gather at the graveside. Attending eases that one loss, for the people who stood there, and nothing else.");
            if (s.wakes)
            {
                s.wakeReliefDays = list.SliderLabeled($"A wake is worth {s.wakeReliefDays:0.0} days of the arc",
                    s.wakeReliefDays, 0.5f, 12f);
            }

            list.Gap(12f);
            list.Label("Compatibility");
            list.CheckboxLabeled("Keep the base game's memory", ref s.keepVanillaMemory,
                "Normally this mod replaces the base game's flat death penalty outright. Switch this on and the original memory is left in place with its mood set to zero, so other mods that read those memories still find them. Nothing is felt twice either way. Worth turning on in a heavily modded game if deaths seem to upset something else.");

            list.Gap(12f);
            list.Label("Burial is not optional. Someone never laid to rest stays an open wound until they are.");

            list.Gap(24f);
            if (list.ButtonText("Reset to defaults"))
            {
                s.Reset();
            }
            list.End();
        }
    }
}
