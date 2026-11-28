using HarmonyLib;
using KSP.Localization;
using RP0.ConfigurableStart;
using UnityEngine;
using UnityEngine.UI;

namespace RP0.Harmony
{
    [HarmonyPatch(typeof(MainMenu))]
    internal class PatchMainMenu
    {
        [HarmonyPostfix]
        [HarmonyPatch("CreateNewGameDialog")]
        internal static void Postfix_CreateNewGameDialog(ref PopupDialog __result)
        {
            ScenarioHandler.Instance.ClobberNewGameUI(__result);
        }
    }
}
