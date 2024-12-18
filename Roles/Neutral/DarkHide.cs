﻿using System.Collections.Generic;
using AmongUs.GameOptions;
using EHR.Modules;
using Hazel;
using InnerNet;

namespace EHR.Neutral;

// Credit: https://github.com/Yumenopai/TownOfHost_Y
public class DarkHide : RoleBase
{
    private const int Id = 12900;
    private static List<byte> PlayerIdList = [];

    private static OptionItem KillCooldown;
    private static OptionItem HasImpostorVision;
    private static OptionItem CanCountNeutralKiller;
    private static OptionItem CanVent;
    public static OptionItem SnatchesWin;

    private float CurrentKillCooldown = Options.DefaultKillCooldown;
    public bool IsWinKill;

    public override bool IsEnable => PlayerIdList.Count > 0;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.NeutralRoles, CustomRoles.DarkHide);

        KillCooldown = new FloatOptionItem(Id + 10, "KillCooldown", new(0f, 180f, 0.5f), 30f, TabGroup.NeutralRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide])
            .SetValueFormat(OptionFormat.Seconds);

        CanVent = new BooleanOptionItem(Id + 14, "CanVent", true, TabGroup.NeutralRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);

        HasImpostorVision = new BooleanOptionItem(Id + 11, "ImpostorVision", true, TabGroup.NeutralRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);

        CanCountNeutralKiller = new BooleanOptionItem(Id + 12, "CanCountNeutralKiller", true, TabGroup.NeutralRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);

        SnatchesWin = new BooleanOptionItem(Id + 13, "SnatchesWin", false, TabGroup.NeutralRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.DarkHide]);
    }

    public override void Init()
    {
        PlayerIdList = [];
        CurrentKillCooldown = KillCooldown.GetFloat();
        IsWinKill = false;
    }

    public override void Add(byte playerId)
    {
        PlayerIdList.Add(playerId);
        CurrentKillCooldown = KillCooldown.GetFloat();
        IsWinKill = false;

        DRpcSetKillCount(Utils.GetPlayerById(playerId));
    }

    public override void Remove(byte playerId)
    {
        PlayerIdList.Remove(playerId);
    }

    public static void ReceiveRPC(MessageReader msg)
    {
        byte DarkHiderId = msg.ReadByte();
        if (Main.PlayerStates[DarkHiderId].Role is not DarkHide { IsEnable: true } dh) return;

        bool IsKillerKill = msg.ReadBoolean();
        dh.IsWinKill = IsKillerKill;
    }

    private void DRpcSetKillCount(PlayerControl player)
    {
        if (!IsEnable || !Utils.DoRPC || !AmongUsClient.Instance.AmHost) return;

        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SetDarkHiderKillCount, SendOption.Reliable);
        writer.Write(player.PlayerId);
        writer.Write(IsWinKill);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public override void SetKillCooldown(byte id)
    {
        Main.AllPlayerKillCooldown[id] = CurrentKillCooldown;
    }

    public override bool CanUseKillButton(PlayerControl player)
    {
        return !player.Data.IsDead;
    }

    public override bool CanUseImpostorVentButton(PlayerControl pc)
    {
        return CanVent.GetBool();
    }

    public override void ApplyGameOptions(IGameOptions opt, byte id)
    {
        opt.SetVision(HasImpostorVision.GetBool());
    }

    public override bool OnCheckMurder(PlayerControl killer, PlayerControl Ktarget)
    {
        CustomRoles targetRole = Ktarget.GetCustomRole();
        bool succeeded = targetRole.IsImpostor();
        if (CanCountNeutralKiller.GetBool() && !Ktarget.Is(CustomRoles.Arsonist) && !Ktarget.Is(CustomRoles.Revolutionist)) succeeded = succeeded || Ktarget.IsNeutralKiller();

        if (succeeded && SnatchesWin.GetBool()) IsWinKill = true;

        DRpcSetKillCount(killer);
        MessageWriter SabotageFixWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, killer.GetClientId());
        SabotageFixWriter.Write((byte)SystemTypes.Electrical);
        SabotageFixWriter.WriteNetObject(killer);
        AmongUsClient.Instance.FinishRpcImmediately(SabotageFixWriter);

        foreach (PlayerControl target in Main.AllPlayerControls)
        {
            if (target.PlayerId == killer.PlayerId || target.Data.Disconnected) continue;

            SabotageFixWriter = AmongUsClient.Instance.StartRpcImmediately(ShipStatus.Instance.NetId, (byte)RpcCalls.UpdateSystem, SendOption.Reliable, target.GetClientId());
            SabotageFixWriter.Write((byte)SystemTypes.Electrical);
            SabotageFixWriter.WriteNetObject(target);
            AmongUsClient.Instance.FinishRpcImmediately(SabotageFixWriter);
        }

        return true;
    }
}