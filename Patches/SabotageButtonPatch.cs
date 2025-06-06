﻿using HarmonyLib;

namespace EHR.Patches;
// https://github.com/tukasa0001/TownOfHost/blob/main/Patches/ActionButtonPatch.cs

[HarmonyPatch(typeof(SabotageButton), nameof(SabotageButton.DoClick))]
public static class SabotageButtonDoClickPatch
{
    public static bool Prefix()
    {
        if (!PlayerControl.LocalPlayer.inVent && GameManager.Instance.SabotagesEnabled())
        {
            FastDestroyableSingleton<HudManager>.Instance.ToggleMapVisible(new()
            {
                Mode = MapOptions.Modes.Sabotage
            });
        }

        return false;
    }
}