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
#if DEBUG
            Logger.LogInfo("GetTargetMethod called");
#endif
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
#if DEBUG
            Logger.LogInfo("Entered PatchPrefix"); 
#endif
            if (!(item is MedsClass medsItem))
            {
#if DEBUG
                Logger.LogInfo("PatchPrefix: Item is not medsclass item, returning");
#endif                
                return true;
            }

            float delay;

            switch (item)
            {
                
                case GClass2741 _: //Look for TryGetBodyPartToApply to update in the future
#if DEBUG
                    Logger.LogInfo("PatchPrefix: Switch statement on item entered - GClass2726 - MedKitStartDelay");
#endif                    
                    delay = Singleton<BackendConfigSettingsClass>.Instance.Health.Effects.MedEffect.MedKitStartDelay;
                    break;
                case GClass2744 _: //Look for TryGetBodyPartToApply to update in the future
#if DEBUG 
                    Logger.LogInfo("PatchPrefix: Switch statement on item entered - GClass2729 - MedicalStartDelay");
#endif
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
#if DEBUG
                Logger.LogInfo("PatchPrefix: Can't get body part to apply result, returning."); 
#endif
                return true;
            }

            var damagedBodyPart = (EBodyPart?)getBodyPartToApplyArguments[2];

            if (!damagedBodyPart.HasValue)
            {
#if DEBUG
                Logger.LogInfo("PatchPrefix: Body part does not have a value, returning.");
#endif                
                return true;
            }

            EBodyPart damagedBodyPartValue = damagedBodyPart.Value;

            MedsItemType medsItemType = GetItemType(medsItem);

            delay = GetModifiedDelay(player, delay, medsItemType, damagedBodyPartValue);

            bool isBodyPartDestroyed = player.HealthController.IsBodyPartDestroyed(damagedBodyPartValue);

            if(damagedBodyPartValue.Equals(null) || medsItem.Equals(null) || delay.Equals(null) || isBodyPartDestroyed.Equals(null))
            {
#if DEBUG
                Logger.LogError("Null values detected, error");  
#endif
            }

            float healingTime = medsItem.HealthEffectsComponent.UseTimeFor(damagedBodyPartValue);
#if DEBUG
            Logger.LogInfo("PatchPrefix: Starting ealing time is " + healingTime);  
#endif
            if (isBodyPartDestroyed && medsItem.HealthEffectsComponent.AffectsAny(EDamageEffectType.DestroyedPart))
            {
                healingTime /= 1f + (float)player.Skills.SurgerySpeed;
#if DEBUG
                Logger.LogInfo("PatchPrefix: New healing time is " + healingTime);  
#endif
            }

            healingTime = GetModifiedHealingTime(player, healingTime, medsItemType, damagedBodyPartValue);
#if DEBUG
            Logger.LogInfo("PatchPrefix: Modified healing time is " + healingTime);  
#endif
            Action<object> addEffectCallback = medEffectObj =>
            {
                InitEffectMethod.Invoke(medEffectObj, new object[] { medsItem, 1f });
#if DEBUG
                Logger.LogInfo("PatchPrefix:  InitEffectMethod.Invoke called.");  
#endif
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
#if DEBUG
            Logger.LogInfo("PatchPrefix: Result for Effect Calculated, calling back.");  
#endif
            return false;
        }

        private static bool BodyPartHasNegativeEffect(IEffect effect)
        {
            Type effectType = effect.GetType();
#if DEBUG
            Logger.LogInfo("BodyPartHasNegativeEffect: returning effect type of " + effectType);  
#endif
            return effectType == LightBleedingType || effectType == HeavyBleedingType || effectType == FractureType;
        }

        private static float GetModifiedHealingTime(Player player, float healingTime, MedsItemType medsItemType, EBodyPart bodyPart)
        {
            if (medsItemType == MedsItemType.Other)
            {
#if DEBUG
                Logger.LogInfo("GetModifiedHealingTime: Other item detected, healing time of " + healingTime);
#endif                
                return healingTime;
            }

            if (medsItemType == MedsItemType.SurgeryKit)
            {
                if (FastHealingPlugin.SurgerySpeedMultiplierEnabled.Value)
                {
                    healingTime *= FastHealingPlugin.SurgerySpeedMultiplier.Value;
                }
#if DEBUG
                Logger.LogInfo("GetModifiedHealingTime: SurgeryKit detected, healing time of " + healingTime);  
#endif
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
#if DEBUG
                        Logger.LogInfo("GetModifiedHealingTime: Medkit detected and dynamic heal time enabled, healing time of " + healingTime );  
#endif
                    }
                }

                if (FastHealingPlugin.HealingSpeedMultiplierEnabled.Value)
                {
                    healingTime *= FastHealingPlugin.HealingTimeMultiplier.Value;
#if DEBUG
                    Logger.LogInfo("GetModifiedHealingTime: Medkit detected, healing time of " + healingTime);  
#endif
                }
            }

            return healingTime;
        }

        private static float GetModifiedDelay(Player player, float delay, MedsItemType medsItemType, EBodyPart bodyPart)
        {
            if (player.HealthController.GetAllActiveEffects(bodyPart).Any(BodyPartHasNegativeEffect))
            {
#if DEBUG
                Logger.LogInfo("GetModifiedDelay: Any body part has negative effect, delay is " + delay);  
#endif
                return delay;
            }

            if (medsItemType == MedsItemType.Other)
            {
#if DEBUG
                Logger.LogInfo("GetModifiedDelay: Other med item type detected, delay is " + delay);  
#endif
                return delay;
            }

            if (medsItemType == MedsItemType.SurgeryKit)
            {
                if (FastHealingPlugin.SurgerySpeedMultiplierEnabled.Value)
                {
                    delay *= FastHealingPlugin.SurgerySpeedMultiplier.Value;
#if DEBUG
                    Logger.LogInfo("GetModifiedDelay: SurgeryKit med item type detected, delay is " + delay);  
#endif
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
#if DEBUG
                        Logger.LogInfo("GetModifiedDelay: Medkit med item type detected and dynamic heal time mult enabled, delay is " + delay);  
#endif
                    }
                }

                if (FastHealingPlugin.HealingSpeedMultiplierEnabled.Value)
                {
                    delay *= FastHealingPlugin.HealingTimeMultiplier.Value;
#if DEBUG
                    Logger.LogInfo("GetModifiedDelay: Medkit med item type detected, delay is " + delay);  
#endif
                }
            }
#if DEBUG
            Logger.LogInfo("GetModifiedDelay:Final delay is " + delay);  
#endif
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
#if DEBUG
                Logger.LogInfo("GetItemType: Found surgery kit item");  
#endif
                return MedsItemType.SurgeryKit;
            }

            if (itemName == grizzly ||
                itemName == salewa ||
                itemName == carMedkit ||
                itemName == afak ||
                itemName == ifak ||
                itemName == yellowMedkit)
            {
#if DEBUG
                Logger.LogInfo("GetItemType: Found MedKit item");  
#endif
                return MedsItemType.Medkit;
            }
#if DEBUG
            Logger.LogInfo("GetItemType: Found Other item");  
#endif
            return MedsItemType.Other;
        }
    }
}
