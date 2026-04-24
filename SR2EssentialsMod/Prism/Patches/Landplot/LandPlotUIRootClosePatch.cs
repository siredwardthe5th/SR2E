using HarmonyLib;
using Il2CppMonomiPark.SlimeRancher.Economy;
using Il2CppMonomiPark.SlimeRancher.UI;
using Il2CppMonomiPark.SlimeRancher.UI.Plot;
using SR2E.Storage;
using SR2E.Utils;
using UnityEngine;

namespace SR2E.Prism.Patches.Landplot;

[PrismPatch]
[HarmonyPatch(typeof(LandPlotUIRoot), "BuyPlot")]
internal static class LandPlotUIRootClosePatch
{
    public static void Postfix(LandPlotUIRoot __instance, PurchaseCost cost, GameObject plotPrefab)
    {
        if (((Object)plotPrefab).name != "patchEmpty")
            return;

        ActionsEUtil.ExecuteInTicks(() =>
        {
            foreach (Canvas item in UnityEUtil.GetAllInScene<Canvas>("DimBackground(Clone)"))
            {
                Object.Destroy((Object)(object)((Component)item).gameObject);
            }
            try
            {
                ((BaseUI)__instance).Close();
            }
            catch { }
        }, 2);
    }
}
