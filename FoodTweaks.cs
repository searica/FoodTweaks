// Ignore Spelling: ToolTweaks Jotunn

using BepInEx;
using BepInEx.Configuration;
using FoodTweaks.Extensions;
using HarmonyLib;
using Jotunn.Extensions;
using Jotunn.Managers;
using Jotunn.Utils;
using Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Reflection.Emit;
using static ItemDrop;

namespace FoodTweaks
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, Jotunn.Main.Version)]
    [NetworkCompatibility(CompatibilityLevel.VersionCheckOnly, VersionStrictness.Patch)]
    [SynchronizationMode(AdminOnlyStrictness.IfOnServer)]
    internal sealed class FoodTweaks : BaseUnityPlugin
    {
        internal const string Author = "Searica";
        public const string PluginName = "FoodTweaks";
        public const string PluginGUID = $"{Author}.Valheim.{PluginName}";
        public const string PluginVersion = "0.1.1";

        // Use this class to add your own localization to the game
        // https://valheim-modding.github.io/Jotunn/tutorials/localization.html
        //public static CustomLocalization Localization = LocalizationManager.Instance.GetLocalization();

        private const string MainSection = "Global";

        internal static FoodTweaks Instance;
        private static ConfigFileWatcher ConfigWatcher;

        internal ConfigEntry<bool> EnableExtraStaminaRegen;
        internal ConfigEntry<bool> EnableExtraEitrRegen;
        internal ConfigEntry<float> ExtraStaminaRegenFromFood;
        internal ConfigEntry<float> ExtraEitrRegenFromFood;

        private static bool ShouldUpdatePlugin = false;

        public void Awake()
        {
            Instance = this;
            Log.Init(Logger);
            SetUpConfigEntries();

            Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), harmonyInstanceId: PluginGUID);
            Game.isModded = true;

            ConfigWatcher = new ConfigFileWatcher(Config);
            ConfigWatcher.OnConfigFileReloaded += () => { UpdatePlugin(shouldSave: false); };
            SynchronizationManager.OnConfigurationWindowClosed += () => { UpdatePlugin(shouldSave: true); };
            SynchronizationManager.OnConfigurationSynchronized += (obj, attr) => { UpdatePlugin(shouldSave: true); };
        }

        public void OnDestroy()
        {
            Config.Save();
        }

        internal void SetUpConfigEntries()
        {
            Config.SaveOnConfigSet = false;

            Log.Verbosity = Config.BindConfigInOrder(
                MainSection,
                "Verbosity",
                Log.InfoLevel.Low,
                "Low will log basic information about the mod. Medium will log information that " +
                "is useful for troubleshooting. High will log a lot of information, do not set " +
                "it to this without good reason as it will slow Down your game.",
                synced: false,
                sectionOrder: false
            );

            EnableExtraStaminaRegen = Config.BindConfigInOrder(
                MainSection,
                nameof(EnableExtraStaminaRegen),
                true,
                "Whether to enable gaining extra stamina regen from food.",
                sectionOrder: false
            );

            ExtraStaminaRegenFromFood = Config.BindConfigInOrder(
                MainSection,
                nameof(ExtraStaminaRegenFromFood),
                1f,
                "The percent increase in stamina regen per 10 points of stamina gained from food.",
                acceptableValues: new AcceptableValueRange<float>(0.0f, 50f),
                sectionOrder: false
            );


            EnableExtraEitrRegen = Config.BindConfigInOrder(
                MainSection,
                nameof(EnableExtraEitrRegen),
                true,
                "Whether to enable gaining extra eitr regen from food.",
                sectionOrder: false
            );

            ExtraEitrRegenFromFood = Config.BindConfigInOrder(
                MainSection,
                nameof(ExtraEitrRegenFromFood),
                1f,
                "The percent increase in eitr per 10 points of eitr gained from food.",
                acceptableValues: new AcceptableValueRange<float>(0.0f, 50f),
                sectionOrder: false
            );


            EnableExtraStaminaRegen.SettingChanged += SetUpdatePlugin;
            ExtraStaminaRegenFromFood.SettingChanged += SetUpdatePlugin;
            EnableExtraEitrRegen.SettingChanged += SetUpdatePlugin;
            ExtraEitrRegenFromFood.SettingChanged += SetUpdatePlugin;
            Config.Save();
        }

        private static void SetUpdatePlugin(object obj, EventArgs e)
        {
            ShouldUpdatePlugin = !ShouldUpdatePlugin | ShouldUpdatePlugin;
        }

        private static void UpdatePlugin(bool shouldSave)
        {
            if (ShouldUpdatePlugin)
            {

                ShouldUpdatePlugin = false;
                if (shouldSave)
                {
                    Instance.Config.Save();
                }
            }
        }
    }

    [HarmonyPatch]
    internal static class FoodPatches
    {
        private static MethodInfo ModifyStaminaRegenInfo = AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen));
        private static MethodInfo ModifyEitrRegenInfo = AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifyEitrRegen));
        private static FieldInfo SEManInfo = AccessTools.Field(typeof(Character), nameof(Character.m_seman));
        
        private static float BaseStamina = float.NaN;
        private static float BaseEitr = 0;
        private static float StaminaFromFood = 0;
        private static float EitrFromFood = 0;

        private static string[] tooltipTokens = new string[] { "$se_staminaregen", "$item_food_regen", "$item_food_duration", "$item_food_eitr", "$item_food_stamina" };


        /// <summary>
        ///     Cache the amount of eitr gained from food before other mods can modify it. Won't
        ///     catch any mods that use a transpiler but there isn't much I can do about that.
        /// </summary>
        /// <param name="__instance"></param>
        /// <param name="maxEitr"></param>
        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("shudnal.StaminaExtended", "shudnal.EitrMagicExtended", "MidnightsFX.ImpactfulSkills")]
        [HarmonyPatch(typeof(Player), nameof(Player.GetTotalFoodValue))]
        private static void GetStatsFromFood(Player __instance, float stamina, float eitr)
        {
            if (!__instance || __instance != Player.m_localPlayer)
            {
                return;
            } 
            StaminaFromFood = stamina - __instance.m_baseStamina;
            EitrFromFood = eitr;
        }


        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("shudnal.MyLittleUI", "shudnal.StaminaExtended", "shudnal.EitrMagicExtended")]
        [HarmonyPatch(typeof(ItemData), nameof(ItemData.GetTooltip), typeof(ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
        private static void ItemDrop_ItemData_GetTooltip_StaminaRegenTooltipForFoodRegen(ItemData item, ref string __result)
        {
            if (!FoodTweaks.Instance.EnableExtraStaminaRegen.Value && !FoodTweaks.Instance.EnableExtraEitrRegen.Value)
            {
                return;
            }

            if (FoodTweaks.Instance.EnableExtraStaminaRegen.Value && IsFoodItemForExtraStaminaRegeneration(item, out float foodStamina))
            {
                // update stamina tool tip
                int index = -1;
                foreach (string tailString in tooltipTokens)
                {
                    index = __result.IndexOf(tailString, StringComparison.InvariantCulture);
                    if (index != -1)
                    {
                        break;
                    }
                }
                if (index == -1)
                {
                    return;
                }

                string tooltip = string.Format(
                    "\n$se_staminaregen: <color=#ffff80ff>{0:P1}</color> ($item_current:<color=yellow>{1:P1}</color>)",
                    GetStaminaRegenerationValueFromStaminaPoints(foodStamina),
                    GetStaminaMultiplier()
                );

                int i = __result.IndexOf("\n", index, StringComparison.InvariantCulture);
                if (i != -1)
                {
                    __result = __result.Insert(i, tooltip);
                }
                else
                {
                    __result += tooltip;
                }
            }

            if (FoodTweaks.Instance.EnableExtraEitrRegen.Value && IsFoodItemForExtraEitrRegeneration(item, out float foodEitr))
            {
                // update eitr tool tip info
                int index = -1;
                foreach (string tailString in tooltipTokens)
                {
                    index = __result.IndexOf(tailString, StringComparison.InvariantCulture);
                    if (index != -1)
                    {
                        break;
                    }
                }
                if (index == -1)
                {
                    return;
                }
                    
                string tooltip = string.Format(
                    "\n$se_eitrregen: <color=#9090ffff>{0:P1}</color> ($item_current:<color=yellow>{1:P1}</color>)",
                    GetEitrRegenerationValueFromEitrPoints(foodEitr),
                    GetEitrMultiplier()
                );

                int i = __result.IndexOf("\n", index, StringComparison.InvariantCulture);
                if (i != -1)
                {
                    __result = __result.Insert(i, tooltip);
                }
                else
                {
                    __result += tooltip;
                }
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.AddActiveEffects))]
        private static void TextsDialog_AddActiveEffects_SeasonTooltipWhenBuffDisabled(TextsDialog __instance)
        {
            if (!Player.m_localPlayer)
            {
                return;
            }

            float staminaMultiplier = GetStaminaMultiplier();
            if (staminaMultiplier >= 0.01f)
            {
                __instance.m_texts[0].m_text += Localization.instance.Localize($"\n$se_staminaregen ($item_food): <color=orange>{staminaMultiplier:P1}</color>");
            }

            float eitrMultiplier = GetEitrMultiplier();
            if (eitrMultiplier >= 0.01f)
            {
                __instance.m_texts[0].m_text += Localization.instance.Localize($"\n$se_eitrregen ($item_food): <color=orange>{eitrMultiplier:P1}</color>");
            } 
        }

        public static float GetStaminaMultiplier()
        {
            return GetStaminaRegenerationValueFromStaminaPoints(StaminaFromFood);
        }

        private static float GetStaminaRegenerationValueFromStaminaPoints(float points)
        {
            return Mathf.Max(0f, (FoodTweaks.Instance.ExtraStaminaRegenFromFood.Value / 100f) * (points / 10f));
        }

        private static bool IsFoodItemForExtraStaminaRegeneration(ItemData item, out float foodStamina)
        {
            foodStamina = 0f;
            if (!Player.m_localPlayerExists)
            {
                return false;
            }
            if (item.m_shared.m_itemType == ItemData.ItemType.Consumable)
            {
                foodStamina = item.m_shared.m_foodStamina;
            }
            return foodStamina > 0 || item.m_shared.m_appendToolTip != null && IsFoodItemForExtraStaminaRegeneration(item.m_shared.m_appendToolTip.m_itemData, out foodStamina);
        }

        public static float GetEitrMultiplier()
        {
            return GetEitrRegenerationValueFromEitrPoints(EitrFromFood);
        }

        private static float GetEitrRegenerationValueFromEitrPoints(float points)
        {
            return Mathf.Max(0f, FoodTweaks.Instance.ExtraEitrRegenFromFood.Value / 100f) * (points / 10f);
        }

        private static bool IsFoodItemForExtraEitrRegeneration(ItemData item, out float foodEitr)
        {
            foodEitr = 0f;
            if (!Player.m_localPlayerExists)
            {
                return false;
            }
            if (item.m_shared.m_itemType == ItemData.ItemType.Consumable)
            {
                foodEitr = item.m_shared.m_foodEitr;
            }
            return foodEitr > 0 || item.m_shared.m_appendToolTip != null && IsFoodItemForExtraEitrRegeneration(item.m_shared.m_appendToolTip.m_itemData, out foodEitr);
        }

        /// <summary>
        ///     Transpiler to add the bonus regen from food to the multiplier before it is altered by any active status effects
        /// </summary>
        /// <param name="instructions"></param>
        /// <returns></returns>
        [HarmonyTranspiler]
        //[HarmonyEmitIL(".dumps")]
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateStats), typeof(float))]
        private static IEnumerable<CodeInstruction> UpdateRegenMultipliers(IEnumerable<CodeInstruction> instructions)
        {
            CodeMatcher matcher = new(instructions);
            CodeMatch[] matchStaminaRegen = [
                new CodeMatch(OpCodes.Callvirt, ModifyStaminaRegenInfo),
            ];

            CodeMatch[] matchEitrRegen = [
                new CodeMatch(OpCodes.Callvirt, ModifyEitrRegenInfo),
            ];

            if (matcher.TryMatchStartForward("Patching stamina regen", matchStaminaRegen))
            {
                // Get the local variable that was loaded to call SEMan.ModifyStaminaRegen with
                CodeInstruction loadMultiplier = matcher.Advance(-1).Instruction;

                // Advance to after the call to SEMan.ModifyStaminaRegen
                matcher.Advance(2).InsertAndAdvance(  
                        new CodeInstruction(OpCodes.Ldarg_0), // load player as first arg
                        loadMultiplier // load the local variable we want to modify
                    ).InsertAndAdvance(Transpilers.EmitDelegate(UpdateStaminaMultipler_Delegate)); // modifies local variable by reference
            }

            if (matcher.TryMatchStartForward("Patching eitr regen", matchEitrRegen))
            {
                // Get the local variable that was loaded to call SEMan.ModifyEitrRegen with
                CodeInstruction loadMultiplier = matcher.Advance(-1).Instruction;

                // Advance to after the call to SEMan.ModifyEitrRegen
                matcher.Advance(2).InsertAndAdvance(
                        new CodeInstruction(OpCodes.Ldarg_0), // load player as first arg
                        loadMultiplier // load the local variable we want to modify
                    ).InsertAndAdvance(Transpilers.EmitDelegate(UpdateEitrMultipler_Delegate)); // modifies local variable by reference
            }
            return matcher.InstructionEnumeration();
        }

        /// <summary>
        ///     Takes the float on the stack and modifies it with food regen multiplier if the player
        ///     is the local player, and then pushes the result back onto the stack.
        /// </summary>
        /// <param name="multiplier"></param>
        /// <returns></returns>
        private static void UpdateStaminaMultipler_Delegate(Player player, ref float multiplier)
        {
            if (player != Player.m_localPlayer)
            {
                return;
            }
            multiplier += GetStaminaMultiplier();
        }

        /// <summary>
        ///     Takes the float on the stack and modifies it with food regen multiplier if the player
        ///     is the local player, and then pushes the result back onto the stack.
        /// </summary>
        /// <param name="multiplier"></param>
        /// <returns></returns>
        private static void UpdateEitrMultipler_Delegate(Player player, ref float multiplier)
        {
            if (player != Player.m_localPlayer)
            {
                return ;
            }
            multiplier += GetEitrMultiplier();
        }


        //public override void ModifyStaminaRegen(ref float staminaRegen)
        //{
        //    if (m_staminaRegenMultiplier > 1f)
        //    {
        //        staminaRegen += m_staminaRegenMultiplier - 1f;
        //    }
        //    else
        //    {
        //        staminaRegen *= m_staminaRegenMultiplier;
        //    }
        //}

        //public override void ModifyEitrRegen(ref float staminaRegen)
        //{
        //    if (m_eitrRegenMultiplier > 1f)
        //    {
        //        staminaRegen += m_eitrRegenMultiplier - 1f;
        //    }
        //    else
        //    {
        //        staminaRegen *= m_eitrRegenMultiplier;
        //    }
        //}
    }
}