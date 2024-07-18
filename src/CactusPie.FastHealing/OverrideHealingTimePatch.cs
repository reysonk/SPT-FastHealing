using System;
using System.Linq;
using System.Reflection;
using SPT.Reflection.Patching;
using CactusPie.FastHealing.Enums;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using HarmonyLib;
using static GClass2438;

namespace CactusPie.FastHealing
{
    public class OverrideHealingTimePatch : ModulePatch
    {
        private static readonly Type LightBleedingType = AccessTools.FirstInner(typeof(ActiveHealthController), x => x.Name == "LightBleeding");
        private static readonly Type HeavyBleedingType = AccessTools.FirstInner(typeof(ActiveHealthController), x => x.Name == "HeavyBleeding");
        private static readonly Type FractureType = AccessTools.FirstInner(typeof(ActiveHealthController), x => x.Name == "Fracture");
        private static readonly Type MedEffectType = AccessTools.FirstInner(typeof(ActiveHealthController), x => x.Name == "MedEffect");

        private static readonly MethodInfo InitEffectMethod = AccessTools.Method(MedEffectType, "Init", new[] { typeof(Item), typeof(float) });
        private static readonly MethodInfo AddEffectMethod = AccessTools.Method(typeof(ActiveHealthController), "AddEffect").MakeGenericMethod(MedEffectType);
        private static readonly MethodInfo TryGetBodyPartToApplyMethod = AccessTools.Method(typeof(ActiveHealthController), "TryGetBodyPartToApply");

        protected override MethodBase GetTargetMethod()
        {
            Logger.LogInfo("GetTargetMethod called");
            return AccessTools.Method(typeof(ActiveHealthController), nameof(ActiveHealthController.DoMedEffect));
        }

        [PatchPrefix]
        public static bool PatchPrefix(
            ref IEffect __result,
            ActiveHealthController __instance,
            Item item,
            EBodyPart bodyPart,
            float? amount = null)
        {
            Logger.LogInfo("Entered PatchPrefix");
            if (!(item is MedsClass medsItem))
            {
                Logger.LogInfo("PatchPrefix: Item is not medsclass item, returning");
                return true;
            }

            float delay;

            switch (item)
            {
                
                case GClass2726 _:
                    Logger.LogInfo("PatchPrefix: Switch statement on item entered - GClass2726 - MedKitStartDelay");
                    delay = Singleton<BackendConfigSettingsClass>.Instance.Health.Effects.MedEffect.MedKitStartDelay;
                    break;
                case GClass2729 _:
                    Logger.LogInfo("PatchPrefix: Switch statement on item entered - GClass2729 - MedicalStartDelay");
                    delay = Singleton<BackendConfigSettingsClass>.Instance.Health.Effects.MedEffect.MedicalStartDelay;
                    break;
                default:
                    return true;
            }

            Player player = Singleton<GameWorld>.Instance.MainPlayer;

            var getBodyPartToApplyArguments = new object[]
            {
                item,
                bodyPart,
                null,
            };

            bool tryGetBodyPartToApplyResult = (bool)TryGetBodyPartToApplyMethod
                .Invoke(__instance, getBodyPartToApplyArguments);

            if (!tryGetBodyPartToApplyResult)
            {
                Logger.LogInfo("PatchPrefix: Can't get body part to apply result, returning.");
                return true;
            }

            var damagedBodyPart = (EBodyPart?)getBodyPartToApplyArguments[2];

            if (!damagedBodyPart.HasValue)
            {
                Logger.LogInfo("PatchPrefix: Body part does not have a value, returning.");
                return true;
            }

            EBodyPart damagedBodyPartValue = damagedBodyPart.Value;

            MedsItemType medsItemType = GetItemType(medsItem);

            delay = GetModifiedDelay(player, delay, medsItemType, damagedBodyPartValue);

            bool isBodyPartDestroyed = player.HealthController.IsBodyPartDestroyed(damagedBodyPartValue);

            if(damagedBodyPartValue.Equals(null) || medsItem.Equals(null) || delay.Equals(null) || isBodyPartDestroyed.Equals(null))
            {
                Logger.LogError("Null values detected, error");
            }

            float healingTime = medsItem.HealthEffectsComponent.UseTimeFor(damagedBodyPartValue);
            Logger.LogInfo("PatchPrefix: Starting ealing time is " + healingTime);
            if (isBodyPartDestroyed && medsItem.HealthEffectsComponent.AffectsAny(EDamageEffectType.DestroyedPart))
            {
                healingTime /= 1f + (float)player.Skills.SurgerySpeed;
                Logger.LogInfo("PatchPrefix: New healing time is " + healingTime);
            }

            healingTime = GetModifiedHealingTime(player, healingTime, medsItemType, damagedBodyPartValue);
            Logger.LogInfo("PatchPrefix: Modified healing time is " + healingTime);
            Action<object> addEffectCallback = medEffectObj =>
            {
                InitEffectMethod.Invoke(medEffectObj, new object[] { medsItem, 1f });
                Logger.LogInfo("PatchPrefix:  InitEffectMethod.Invoke called.");
            };

            __result = (IEffect)AddEffectMethod.Invoke(
                __instance,
                new object[]
                {
                    damagedBodyPart,
                    delay, //delay time
                    healingTime,
                    (float?)null,
                    (float?)null,
                    addEffectCallback,
                });
            Logger.LogInfo("PatchPrefix: Result for Effect Calculated, calling back.");
            return false;
        }

        private static bool BodyPartHasNegativeEffect(IEffect effect)
        {
            Type effectType = effect.GetType();
            Logger.LogInfo("BodyPartHasNegativeEffect: returning effect type of " + effectType);
            return effectType == LightBleedingType || effectType == HeavyBleedingType || effectType == FractureType;
        }

        private static float GetModifiedHealingTime(Player player, float healingTime, MedsItemType medsItemType, EBodyPart bodyPart)
        {
            if (medsItemType == MedsItemType.Other)
            {
                Logger.LogInfo("GetModifiedHealingTime: Other item detected, healing time of " + healingTime);
                return healingTime;
            }

            if (medsItemType == MedsItemType.SurgeryKit)
            {
                if (FastHealingPlugin.SurgerySpeedMultiplierEnabled.Value)
                {
                    healingTime *= FastHealingPlugin.SurgerySpeedMultiplier.Value;
                }
                Logger.LogInfo("GetModifiedHealingTime: SurgeryKit detected, healing time of " + healingTime);
                return healingTime;
            }

            if (medsItemType == MedsItemType.Medkit)
            {
                if (FastHealingPlugin.DynamicHealTimeEnabled.Value)
                {
                    ValueStruct health = player.HealthController.GetBodyPartHealth(bodyPart);
                    float healthPercentage = health.Current / health.Maximum;

                    if (healthPercentage > (FastHealingPlugin.DynamicHealTimeThreshold.Value / 100f))
                    {
                        healingTime *= (1f - healthPercentage) * FastHealingPlugin.DynamicHealTimeMultiplier.Value;
                        Logger.LogInfo("GetModifiedHealingTime: Medkit detected and dynamic heal time enabled, healing time of " + healingTime );
                    }
                }

                if (FastHealingPlugin.HealingSpeedMultiplierEnabled.Value)
                {
                    healingTime *= FastHealingPlugin.HealingTimeMultiplier.Value;
                    Logger.LogInfo("GetModifiedHealingTime: Medkit detected, healing time of " + healingTime);
                }
            }

            return healingTime;
        }

        private static float GetModifiedDelay(Player player, float delay, MedsItemType medsItemType, EBodyPart bodyPart)
        {
            if (player.HealthController.GetAllActiveEffects(bodyPart).Any(BodyPartHasNegativeEffect))
            {
                Logger.LogInfo("GetModifiedDelay: Any body part has negative effect, delay is " + delay);
                return delay;
            }

            if (medsItemType == MedsItemType.Other)
            {
                Logger.LogInfo("GetModifiedDelay: Other med item type detected, delay is " + delay);
                return delay;
            }

            if (medsItemType == MedsItemType.SurgeryKit)
            {
                if (FastHealingPlugin.SurgerySpeedMultiplierEnabled.Value)
                {
                    delay *= FastHealingPlugin.SurgerySpeedMultiplier.Value;
                    Logger.LogInfo("GetModifiedDelay: SurgeryKit med item type detected, delay is " + delay);
                }

                return delay;
            }

            if (medsItemType == MedsItemType.Medkit)
            {
                if (FastHealingPlugin.DynamicHealTimeEnabled.Value)
                {
                    ValueStruct health = player.HealthController.GetBodyPartHealth(bodyPart);
                    float healthPercentage = health.Current / health.Maximum;

                    if (healthPercentage > (FastHealingPlugin.DynamicHealTimeThreshold.Value / 100f))
                    {
                        delay *= (1f - healthPercentage) * FastHealingPlugin.DynamicHealTimeMultiplier.Value;
                        Logger.LogInfo("GetModifiedDelay: Medkit med item type detected and dynamic heal time mult enabled, delay is " + delay);
                    }
                }

                if (FastHealingPlugin.HealingSpeedMultiplierEnabled.Value)
                {
                    delay *= FastHealingPlugin.HealingTimeMultiplier.Value;
                    Logger.LogInfo("GetModifiedDelay: Medkit med item type detected, delay is " + delay);
                }
            }
            Logger.LogInfo("GetModifiedDelay:Final delay is " + delay);
            return delay;
        }

        public static MedsItemType GetItemType(MedsClass medsItem)
        {
            const string surv12Id = "survival_first_aid_rollup_kit";
            const string cmsId = "core_medical_surgical_kit";
            const string grizzly = "AMK_Grizzly";
            const string salewa = "salewa";
            const string carMedkit = "item_meds_automedkit";
            const string ifak = "item_meds_medkit_ifak";
            const string afak = "item_meds_afak";
            const string yellowMedkit = "medkit";

            string itemName = medsItem.Template._name;
            string itemId = medsItem.Template.Name;

            if (itemId == cmsId || itemId == surv12Id)
            {
                Logger.LogInfo("GetItemType: Found surgery kit item");
                return MedsItemType.SurgeryKit;
            }

            if (itemName == grizzly ||
                itemName == salewa ||
                itemName == carMedkit ||
                itemName == afak ||
                itemName == ifak ||
                itemName == yellowMedkit)
            {
                Logger.LogInfo("GetItemType: Found MedKit item");
                return MedsItemType.Medkit;
            }
            Logger.LogInfo("GetItemType: Found Other item");
            return MedsItemType.Other;
        }
    }
}
