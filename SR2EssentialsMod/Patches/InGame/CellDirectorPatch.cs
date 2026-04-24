using HarmonyLib;
using Il2Cpp;
using UnityEngine;

namespace SR2E.Patches.InGame;

[HarmonyPatch(typeof(CellDirector), "Start")]
internal class CellDirectorPatch
{
    internal static void Postfix(CellDirector __instance)
    {
        if (((Object)__instance).name == "cellConservatory")
        {
            Transform val = ((Component)__instance).transform.Find("Sector/cellLabCave/Sector/FX/PortalCard - Cave (2)");
            val.position = new Vector3(val.position.x, 7f, val.position.z);
        }
    }
}
