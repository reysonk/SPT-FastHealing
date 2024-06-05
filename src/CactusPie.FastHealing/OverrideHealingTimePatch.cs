using System;
using System.Linq;
using System.Reflection;
using Aki.Reflection.Patching;
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
            MethodInfo method = typeof(ActiveHealthController).GetMethod("DoMedEffect", BindingFlags.Public | BindingFlags.Instance);
            return method;
        }

        [PatchPrefix]
        public static bool PatchPrefix(
            ref IEffect __result,
            ActiveHealthController __instance,
            Item item,
            EBodyPart bodyPart,
            float? amount = null)
        {
            if (!(item is MedsClass medsItem))
            {
                return true;
            }

            float delay;

            switch (item)
            {
                case GClass2726 _:
                    delay = Singleton<BackendConfigSettingsClass>.Instance.Health.Effects.MedEffect.MedKitStartDelay;
                    break;
                case GClass2729 _:
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
                return true;
            }

            var damagedBodyPart = (EBodyPart?)getBodyPartToApplyArguments[2];

            if (!damagedBodyPart.HasValue)
            {
                return true;
            }

            EBodyPart damagedBodyPartValue = damagedBodyPart.Value;

            MedsItemType medsItemType = GetItemType(medsItem);

            delay = GetModifiedDelay(player, delay, medsItemType, damagedBodyPartValue);

            bool isBodyPartDestroyed = player.HealthController.IsBodyPartDestroyed(damagedBodyPartValue);

            float healingTime = medsItem.HealthEffectsComponent.UseTimeFor(damagedBodyPartValue);
            if (isBodyPartDestroyed && medsItem.HealthEffectsComponent.AffectsAny(EDamageEffectType.DestroyedPart))
            {
                healingTime /= 1f + (float)player.Skills.SurgerySpeed;
            }

            healingTime = GetModifiedHealingTime(player, healingTime, medsItemType, damagedBodyPartValue);

            Action<object> addEffectCallback = medEffectObj =>
            {
                InitEffectMethod.Invoke(medEffectObj, new object[] { medsItem, 1f });
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

            return false;
        }

        private static bool BodyPartHasNegativeEffect(IEffect effect)
        {
            Type effectType = effect.GetType();
            return effectType == LightBleedingType || effectType == HeavyBleedingType || effectType == FractureType;
        }

        private static float GetModifiedHealingTime(Player player, float healingTime, MedsItemType medsItemType, EBodyPart bodyPart)
        {
            if (medsItemType == MedsItemType.Other)
            {
                return healingTime;
            }

            if (medsItemType == MedsItemType.SurgeryKit)
            {
                if (FastHealingPlugin.SurgerySpeedMultiplierEnabled.Value)
                {
                    healingTime *= FastHealingPlugin.SurgerySpeedMultiplier.Value;
                }

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
                    }
                }

                if (FastHealingPlugin.HealingSpeedMultiplierEnabled.Value)
                {
                    healingTime *= FastHealingPlugin.HealingTimeMultiplier.Value;
                }
            }

            return healingTime;
        }

        private static float GetModifiedDelay(Player player, float delay, MedsItemType medsItemType, EBodyPart bodyPart)
        {
            if (player.HealthController.GetAllActiveEffects(bodyPart).Any(BodyPartHasNegativeEffect))
            {
                return delay;
            }

            if (medsItemType == MedsItemType.Other)
            {
                return delay;
            }

            if (medsItemType == MedsItemType.SurgeryKit)
            {
                if (FastHealingPlugin.SurgerySpeedMultiplierEnabled.Value)
                {
                    delay *= FastHealingPlugin.SurgerySpeedMultiplier.Value;
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
                    }
                }

                if (FastHealingPlugin.HealingSpeedMultiplierEnabled.Value)
                {
                    delay *= FastHealingPlugin.HealingTimeMultiplier.Value;
                }
            }

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
                return MedsItemType.SurgeryKit;
            }

            if (itemName == grizzly ||
                itemName == salewa ||
                itemName == carMedkit ||
                itemName == afak ||
                itemName == ifak ||
                itemName == yellowMedkit)
            {
                return MedsItemType.Medkit;
            }

            return MedsItemType.Other;
        }
    }
}
