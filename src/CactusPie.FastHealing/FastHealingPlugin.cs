using BepInEx;
using BepInEx.Configuration;
using JetBrains.Annotations;

namespace CactusPie.FastHealing
{
    [BepInPlugin("com.cactuspie.fasthealing", "CactusPie.FastHealing", "1.0.0")]
    public class FastHealingPlugin : BaseUnityPlugin
    {
        internal static ConfigEntry<float> SurgerySpeedMultiplier { get; set; }
        internal static ConfigEntry<bool> SurgerySpeedMultiplierEnabled { get; set; }
        internal static ConfigEntry<float> HealingTimeMultiplier { get; set; }
        internal static ConfigEntry<bool> HealingSpeedMultiplierEnabled { get; set; }
        internal static ConfigEntry<bool> DynamicHealTimeEnabled { get; set; }
        internal static ConfigEntry<float> DynamicHealTimeThreshold { get; set; }
        internal static ConfigEntry<float> DynamicHealTimeMultiplier { get; set; }

        [UsedImplicitly]
        internal void Start()
        {
            // Surgery time section
            const string surgeryTimeSection = "Surgery time settings";

            SurgerySpeedMultiplierEnabled = Config.Bind
            (
                surgeryTimeSection,
                "Enable surgery time multiplier",
                true,
                new ConfigDescription
                (
                    "Whether or not the surgery time multiplier is enabled"
                )
            );

            SurgerySpeedMultiplier = Config.Bind
            (
                surgeryTimeSection,
                "Surgery time multiplier",
                0.5f,
                new ConfigDescription
                (
                    "Values below 1.0 will make the surgery faster, higher values will make it slower",
                    new AcceptableValueRange<float>(0.01f, 2.0f)
                )
            );

            // Healing time section
            const string healingTimeSection = "Healing time settings";

            HealingSpeedMultiplierEnabled = Config.Bind
            (
                healingTimeSection,
                "Enable healing time multiplier",
                true,
                new ConfigDescription
                (
                    "Whether or not the healing time multiplier is enabled"
                )
            );

            HealingTimeMultiplier = Config.Bind
            (
                healingTimeSection,
                "Healing time multiplier",
                0.8f,
                new ConfigDescription
                (
                    "Values below 1.0 will make the healing faster, higher values will make it slower",
                    new AcceptableValueRange<float>(0.01f, 2.0f)
                )
            );

            // Dynamic healing time section
            const string dynamicHealingTimeSection = "Dynamic healing time settings";

            DynamicHealTimeEnabled = Config.Bind
            (
                dynamicHealingTimeSection,
                "Dynamic heal time enabled",
                true,
                new ConfigDescription
                (
                    "Whether or not the healing time should be quicker for less damaged body parts"
                )
            );

            DynamicHealTimeThreshold = Config.Bind
            (
                dynamicHealingTimeSection,
                "Dynamic surgery time health threshold",
                50f,
                new ConfigDescription
                (
                    "Below this health percentage the game will not apply the dynamic healing time",
                    new AcceptableValueRange<float>(1f, 100.0f)
                )
            );

            DynamicHealTimeMultiplier = Config.Bind
            (
                dynamicHealingTimeSection,
                "Dynamic heal time multiplier",
                1f,
                new ConfigDescription
                (
                    "Decreasing this value below 1 will further speed up healing time for less damaged limbs",
                    new AcceptableValueRange<float>(0.01f, 5.0f)
                )
            );

            new OverrideHealingTimePatch().Enable();
        }

        [UsedImplicitly]
        public void OnDestroy()
        {
            Destroy(this);
        }
    }
}
