﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using AmongUs.GameOptions;
using EHR.Modules;
using EHR.Patches;
using Hazel;

namespace EHR;

// Most code is from https://github.com/EnhancedNetwork/TownofHost-Enhanced, as stated in the README credits

public static class AntiBlackout
{
    public static int ExilePlayerId = -1;
    public static bool SkipTasks;
    private static Dictionary<byte, (bool IsDead, bool Disconnected)> IsDeadCache = [];
    private static readonly LogHandler Logger = EHR.Logger.Handler("AntiBlackout");

    /*
        public static bool CheckBlackOut()
        {
            HashSet<byte> Impostors = [];
            HashSet<byte> Crewmates = [];
            HashSet<byte> NeutralKillers = [];

            var lastExiled = ExileControllerWrapUpPatch.AntiBlackoutLastExiled;
            foreach (var pc in Main.AllAlivePlayerControls)
            {
                // If a player is ejected, do not count them as alive
                if (lastExiled != null && pc.PlayerId == lastExiled.PlayerId) continue;

                if (pc.Is(Team.Impostor)) Impostors.Add(pc.PlayerId);
                else if (pc.IsNeutralKiller()) NeutralKillers.Add(pc.PlayerId);
                else Crewmates.Add(pc.PlayerId);
            }
            var numAliveImpostors = Impostors.Count;
            var numAliveCrewmates = Crewmates.Count;
            var numAliveNeutralKillers = NeutralKillers.Count;

            EHR.Logger.Info($" Impostors: {numAliveImpostors}, Crewmates: {numAliveCrewmates}, Neutral Killers: {numAliveNeutralKillers}", "AntiBlackout Num Alive");

            bool con1 = numAliveImpostors <= 0; // All real impostors are dead
            bool con2 = (numAliveNeutralKillers + numAliveCrewmates) <= numAliveImpostors; // Alive Impostors >= other teams sum
            bool con3 = numAliveNeutralKillers == 1 && numAliveImpostors == 1 && numAliveCrewmates <= 2; // One Impostor and one Neutral Killer is alive and living Crewmates are very few

            var blackOutIsActive = con1 || con2 || con3;

            EHR.Logger.Info($" {blackOutIsActive}", "BlackOut Is Active");
            return blackOutIsActive;
        }
    */

    private static bool IsCached { get; set; }

    public static void SetIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        SkipTasks = true;
        RevivePlayersAndSetDummyImp();
        Logger.Info($"SetIsDead is called from {callerMethodName}");
        if (IsCached) return;

        IsDeadCache.Clear();

        foreach (NetworkedPlayerInfo info in GameData.Instance.AllPlayers)
        {
            if (info == null) continue;

            IsDeadCache[info.PlayerId] = (info.IsDead, info.Disconnected);
            info.IsDead = false;
            info.Disconnected = false;
        }

        IsCached = true;
        if (doSend) SendGameData();
    }

    private static void RevivePlayersAndSetDummyImp()
    {
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default || PlayerControl.AllPlayerControls.Count < 2) return;

        PlayerControl dummyImp = PlayerControl.LocalPlayer;

        var hasValue = false;
        var sender = CustomRpcSender.Create("AntiBlackout.RevivePlayersAndSetDummyImp", SendOption.Reliable);

        foreach (var seer in Main.AllPlayerControls)
        {
            if (seer.IsModdedClient()) continue;

            var seerIsAliveAndHasKillButton = seer.HasKillButton() && seer.IsAlive() && Options.CurrentGameMode == CustomGameMode.Standard;

            if (Options.CurrentGameMode is not (CustomGameMode.MoveAndStop or CustomGameMode.HotPotato or CustomGameMode.Speedrun or CustomGameMode.HideAndSeek or CustomGameMode.NaturalDisasters or CustomGameMode.RoomRush))
            {
                foreach (var target in Main.AllPlayerControls)
                {
                    if (seerIsAliveAndHasKillButton)
                    {
                        if (target.PlayerId != seer.PlayerId)
                        {
                            sender.RpcSetRole(target, RoleTypes.Crewmate, seer.GetClientId());
                            hasValue = true;
                        }
                    }
                    else
                    {
                        if (target.PlayerId == dummyImp.PlayerId)
                        {
                            sender.RpcSetRole(target, RoleTypes.Impostor, seer.GetClientId());
                            hasValue = true;
                        }
                        else
                        {
                            sender.RpcSetRole(target, RoleTypes.Crewmate, seer.GetClientId());
                            hasValue = true;
                        }
                    }
                }
            }
            else
            {
                sender.RpcSetRole(seer, RoleTypes.Impostor, seer.OwnerId);
                Main.AllPlayerControls.DoIf(x => x.PlayerId != seer.PlayerId, x => sender.RpcSetRole(x, RoleTypes.Crewmate, seer.OwnerId));
                hasValue = true;
            }
        }

        sender.SendMessage(dispose: !hasValue);
    }

    public static void RestoreIsDead(bool doSend = true, [CallerMemberName] string callerMethodName = "")
    {
        Logger.Info($"RestoreIsDead is called from {callerMethodName}");

        foreach (NetworkedPlayerInfo info in GameData.Instance.AllPlayers)
        {
            if (info == null) continue;

            if (IsDeadCache.TryGetValue(info.PlayerId, out (bool IsDead, bool Disconnected) val))
            {
                info.IsDead = val.IsDead;
                info.Disconnected = val.Disconnected;
            }
        }

        IsDeadCache.Clear();
        IsCached = false;

        if (doSend)
        {
            SendGameData();
            LateTask.New(RestoreIsDeadByExile, 0.3f, "AntiBlackOut.RestoreIsDeadByExile");
        }
    }

    private static void RestoreIsDeadByExile()
    {
        var sender = CustomRpcSender.Create("AntiBlackout.RestoreIsDeadByExile", SendOption.Reliable);
        Main.AllPlayerControls.DoIf(x => x.Data.IsDead && !x.Data.Disconnected, x => sender.AutoStartRpc(x.NetId, (byte)RpcCalls.Exiled).EndRpc());
        sender.SendMessage();
    }

    public static void SendGameData([CallerMemberName] string callerMethodName = "")
    {
        Logger.Info($"SendGameData is called from {callerMethodName}");

        try
        {
            MessageWriter writer = MessageWriter.Get(SendOption.Reliable);
            writer.StartMessage(5);
            writer.Write(AmongUsClient.Instance.GameId);

            bool hasValue = false;

            foreach (var playerinfo in GameData.Instance.AllPlayers)
            {
                try
                {
                    writer.StartMessage(1);

                    {
                        writer.WritePacked(playerinfo.NetId);
                        playerinfo.Serialize(writer, false);
                    }

                    writer.EndMessage();
                    hasValue = true;

                    if (writer.Length > 800)
                    {
                        writer.EndMessage();
                        AmongUsClient.Instance.SendOrDisconnect(writer);
                        writer.Recycle();
                        writer = MessageWriter.Get(SendOption.Reliable);
                        hasValue = false;
                        writer.StartMessage(5);
                        writer.Write(AmongUsClient.Instance.GameId);
                    }
                }
                catch (Exception e) { Utils.ThrowException(e); }
            }

            writer.EndMessage();

            if (hasValue) AmongUsClient.Instance.SendOrDisconnect(writer);
            writer.Recycle();
        }
        catch (Exception e) { Utils.ThrowException(e); }
    }

    public static void OnDisconnect(NetworkedPlayerInfo player)
    {
        // Execution conditions: Client is the host, IsDead is overridden, player is already disconnected
        if (!AmongUsClient.Instance.AmHost || !IsCached || !player.Disconnected) return;

        IsDeadCache[player.PlayerId] = (true, true);
        RevivePlayersAndSetDummyImp();
        player.IsDead = player.Disconnected = false;
        SendGameData();
    }

    public static void SetRealPlayerRoles()
    {
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Default) return;

        var hasValue = false;
        var sender = CustomRpcSender.Create("AntiBlackout.SetRealPlayerRoles", SendOption.Reliable);
        List<PlayerControl> selfExiled = [];

        switch (Options.CurrentGameMode)
        {
            case CustomGameMode.Standard:
            {
                foreach (((byte seerId, byte targetId), (RoleTypes roletype, _)) in StartGameHostPatch.RpcSetRoleReplacer.RoleMap)
                {
                    // skip host
                    if (seerId == 0) continue;

                    var seer = seerId.GetPlayer();
                    var target = targetId.GetPlayer();

                    if (seer == null || target == null) continue;
                    if (seer.IsModdedClient()) continue;

                    var isSelf = seerId == targetId;
                    var isDead = target.Data.IsDead;
                    var changedRoleType = roletype;

                    switch (isDead)
                    {
                        case true when isSelf:
                        {
                            selfExiled.Add(seer);

                            if (target.HasGhostRole()) changedRoleType = RoleTypes.GuardianAngel;
                            else if (target.Is(Team.Impostor) || target.HasDesyncRole()) changedRoleType = RoleTypes.ImpostorGhost;
                            else changedRoleType = RoleTypes.CrewmateGhost;

                            break;
                        }
                        case true:
                        {
                            var seerIsKiller = seer.Is(Team.Impostor) || seer.HasDesyncRole();

                            if (!seerIsKiller && target.Is(Team.Impostor)) changedRoleType = RoleTypes.ImpostorGhost;
                            else changedRoleType = RoleTypes.CrewmateGhost;

                            break;
                        }
                        case false when isSelf && seer.HasKillButton():
                            continue;
                    }

                    sender.RpcSetRole(target, changedRoleType, seer.OwnerId);
                    hasValue = true;
                }

                foreach (var pc in selfExiled)
                {
                    sender.AutoStartRpc(pc.NetId, (byte)RpcCalls.Exiled);
                    sender.EndRpc();
                    hasValue = true;

                    if (pc.PlayerId == CheckForEndVotingPatch.TempExiledPlayer?.PlayerId)
                    {
                        sender.AutoStartRpc(pc.NetId, (byte)RpcCalls.MurderPlayer, pc.OwnerId);
                        sender.WriteNetObject(pc);
                        sender.Write((int)MurderResultFlags.Succeeded);
                        sender.EndRpc();

                        pc.ReactorFlash(0.2f);
                    }
                }

                break;
            }
            case CustomGameMode.SoloKombat or CustomGameMode.FFA or CustomGameMode.CaptureTheFlag or CustomGameMode.KingOfTheZones:
            {
                sender.RpcSetRole(PlayerControl.LocalPlayer, RoleTypes.Crewmate);

                PlayerControl[] apc = Main.AllPlayerControls;
                apc.DoIf(x => x.Is(CustomRoles.Killer), x => sender.RpcSetRole(x, RoleTypes.Impostor, x.OwnerId));

                foreach (var pc in apc)
                {
                    if (!pc.Is(CustomRoles.Killer))
                    {
                        if (pc.IsModdedClient())
                            continue;

                        sender.AutoStartRpc(pc.NetId, (byte)RpcCalls.MurderPlayer, pc.OwnerId);
                        sender.WriteNetObject(pc);
                        sender.Write((int)MurderResultFlags.Succeeded);
                        sender.EndRpc();

                        pc.ReactorFlash(0.2f);
                    }
                }

                hasValue = true;
                break;
            }
        }

        sender.SendMessage(dispose: !hasValue);
        ResetAllCooldowns();
    }

    private static void ResetAllCooldowns()
    {
        foreach (PlayerControl seer in Main.AllPlayerControls)
        {
            try
            {
                if (seer.IsAlive())
                {
                    seer.RpcResetAbilityCooldown();

                    if (Main.AllPlayerKillCooldown.TryGetValue(seer.PlayerId, out float kcd) && kcd >= 2f)
                        seer.SetKillCooldown(kcd - 2f);
                }
                else if (seer.HasGhostRole()) seer.RpcResetAbilityCooldown();
            }
            catch (Exception e) { Utils.ThrowException(e); }
        }

        CheckMurderPatch.TimeSinceLastKill.SetAllValues(0f);
    }

    public static void ResetAfterMeeting()
    {
        SkipTasks = false;
        ExilePlayerId = -1;
        ResetAllCooldowns();
    }

    public static void Reset()
    {
        Logger.Info("==Reset==");
        IsDeadCache ??= [];
        IsDeadCache.Clear();
        IsCached = false;
        ExilePlayerId = -1;
        SkipTasks = false;
    }
}