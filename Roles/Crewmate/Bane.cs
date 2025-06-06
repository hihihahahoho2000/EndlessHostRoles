﻿namespace EHR.Crewmate;

public class Bane : RoleBase
{
    public static bool On;
    public override bool IsEnable => On;

    public override void Init()
    {
        On = false;
    }

    public override void Add(byte playerId)
    {
        On = true;
    }

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(642600, TabGroup.CrewmateRoles, CustomRoles.Bane);
    }

    public static void OnKilled(PlayerControl killer)
    {
        if (killer == null || killer.Is(CustomRoles.Bloodlust)) return;

        CustomRoles erasedRole = killer.IsImpostor() ? CustomRoles.ImpostorEHR : killer.IsCrewmate() ? CustomRoles.CrewmateEHR : killer.Is(Team.Coven) ? CustomRoles.RegularCoven : CustomRoles.Amnesiac;
        killer.RpcSetCustomRole(erasedRole);
        killer.RpcChangeRoleBasis(erasedRole);
    }
}