﻿using System;
using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using AmongUs.QuickChat;
using HarmonyLib;
using Hazel;
using InnerNet;
using static EHR.Translator;

namespace EHR;

internal static class EAC
{
    public static int DeNum;
    public static HashSet<string> InvalidReports = [];

    public static void WarnHost(int denum = 1)
    {
        DeNum += denum;

        if (ErrorText.Instance)
        {
            ErrorText.Instance.CheatDetected = DeNum > 3;
            ErrorText.Instance.SBDetected = DeNum > 10;

            if (ErrorText.Instance.CheatDetected)
                ErrorText.Instance.AddError(ErrorText.Instance.SBDetected ? ErrorCode.SBDetected : ErrorCode.CheatDetected);
            else
                ErrorText.Instance.Clear();
        }
    }

    public static bool ReceiveRpc(PlayerControl pc, byte callId, MessageReader reader)
    {
        if (!AmongUsClient.Instance.AmHost) return false;

        if (pc == null || reader == null) return false;

        try
        {
            MessageReader sr = MessageReader.Get(reader);
            var rpc = (RpcCalls)callId;

            switch (rpc)
            {
                case RpcCalls.CheckName:
                    if (!GameStates.IsLobby)
                    {
                        WarnHost();
                        Report(pc, "CheckName out of Lobby");
                        HandleCheat(pc, "CheckName out of Lobby");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] CheckName out of lobby, rejected", "EAC");
                        return true;
                    }

                    break;
                case RpcCalls.SendChat when !pc.IsHost():
                    string text = sr.ReadString();

                    if (text.Contains('░') ||
                        text.Contains('▄') ||
                        text.Contains('█') ||
                        text.Contains('▌') ||
                        text.Contains('▒') ||
                        text.Contains("习近平"))
                    {
                        Report(pc, "Illegal messages");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] sent an illegal message, which has been rejected", "EAC");
                        return true;
                    }

                    break;
                case RpcCalls.ReportDeadBody:
                    byte targetId = sr.ReadByte();

                    if (GameStates.IsMeeting && MeetingHud.Instance.state != MeetingHud.VoteStates.Animating && !pc.IsHost())
                    {
                        WarnHost();
                        Report(pc, "Report dead body in meeting");
                        HandleCheat(pc, "Report dead body in meeting");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] attempted to report a body in a meeting, rejected", "EAC");
                        return true;
                    }

                    if (!GameStates.IsInGame)
                    {
                        WarnHost();
                        Report(pc, "Try to Report body out of game B");
                        HandleCheat(pc, "Try to Report body out of game B");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] attempted to report a body that may have been illegally killed, but was rejected", "EAC");
                        return true;
                    }

                    if (GameManager.Instance.TryCast<HideAndSeekManager>())
                    {
                        WarnHost();
                        Report(pc, "Try to Report body in Hide and Seek");
                        HandleCheat(pc, "Try to Report body in Hide and Seek");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] attempted to report a body in Hide and Seek, rejected", "EAC");
                        return true;
                    }

                    if (targetId != byte.MaxValue)
                    {
                        bool bodyExists = Object.FindObjectsOfType<DeadBody>().Any(deadBody => deadBody.ParentId == targetId);

                        if (!bodyExists && targetId != pc.PlayerId && (!MeetingHud.Instance || MeetingHud.Instance.state != MeetingHud.VoteStates.Animating))
                        {
                            WarnHost();
                            Report(pc, "Try to Report body that doesn't exist");
                            if (!InvalidReports.Add(pc.GetClient().GetHashedPuid())) HandleCheat(pc, "Try to Report body that doesn't exist");
                            Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] attempted to report a body that does't exist", "EAC");
                            return true;
                        }
                    }

                    break;
                case RpcCalls.SendQuickChat:
                    var quickChatPhraseType = (QuickChatPhraseType)sr.ReadByte();

                    switch (quickChatPhraseType)
                    {
                        case QuickChatPhraseType.Empty:
                            HandleCheat(pc, "Empty message in quick chat");
                            return true;
                        case QuickChatPhraseType.PlayerId:
                        {
                            byte playerID = sr.ReadByte();

                            if (playerID == 255)
                            {
                                HandleCheat(pc, "Sending invalid player in quick chat");
                                return true;
                            }

                            if (GameStates.InGame && GameData.Instance.GetPlayerById(playerID) == null)
                            {
                                HandleCheat(pc, "Sending non existing player in quick chat");
                                return true;
                            }

                            break;
                        }
                    }

                    if (quickChatPhraseType != QuickChatPhraseType.ComplexPhrase) break;
                    sr.ReadUInt16();
                    int num = sr.ReadByte();

                    switch (num)
                    {
                        case 0:
                            HandleCheat(pc, "Complex phrase without arguments");
                            return true;
                        case > 3:
                            HandleCheat(pc, "Trying to crash or lag other players");
                            return true;
                    }

                    break;
                case RpcCalls.CheckColor when !pc.IsHost():
                    if (!GameStates.IsLobby)
                    {
                        WarnHost();
                        Report(pc, "CheckColor out of Lobby");
                        HandleCheat(pc, "CheckColor out of Lobby");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] check color out of lobby, rejected", "EAC");
                        return true;
                    }

                    break;
                case RpcCalls.SetColor when !pc.IsModdedClient() && (!Options.PlayerCanSetColor.GetBool() || !GameStates.IsLobby):
                    Report(pc, "Directly SetColor");
                    HandleCheat(pc, "Directly SetColor");
                    Logger.Fatal($"Directly SetColor【{pc.GetClientId()}:{pc.GetRealName()}】has been rejected", "EAC");
                    return true;
                case RpcCalls.CheckMurder:
                    if (GameStates.IsLobby)
                    {
                        WarnHost();
                        Report(pc, "CheckMurder in Lobby");
                        HandleCheat(pc, "CheckMurder in Lobby");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] Illegal check kill, rejected", "EAC");
                        return true;
                    }

                    break;
                case RpcCalls.MurderPlayer:
                    sr.ReadNetObject<PlayerControl>();

                    if (GameStates.IsLobby)
                    {
                        Report(pc, "Directly Murder Player In Lobby");
                        HandleCheat(pc, "Directly Murder Player In Lobby");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] was killed directly in the lobby, rejected", "EAC");
                        return true;
                    }

                    Report(pc, "Directly Murder Player");
                    HandleCheat(pc, "Directly Murder Player");
                    Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] directly killed, rejected", "EAC");
                    return true;
                case RpcCalls.CheckShapeshift:
                    if (GameStates.IsLobby)
                    {
                        Report(pc, "Lobby Check Shapeshift");
                        HandleCheat(pc, "Lobby Check Shapeshift");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] directly transformed in lobby, rejected", "EAC");
                        return true;
                    }

                    break;
                case RpcCalls.Shapeshift when !pc.IsHost():
                {
                    Report(pc, "Directly Shapeshift");
                    MessageWriter swriter = AmongUsClient.Instance.StartRpcImmediately(pc.NetId, (byte)RpcCalls.Shapeshift, SendOption.Reliable);
                    swriter.WriteNetObject(pc);
                    swriter.Write(false);
                    AmongUsClient.Instance.FinishRpcImmediately(swriter);
                    HandleCheat(pc, "Directly Shapeshift");
                    Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] directly transformed, rejected", "EAC");
                    return true;
                }
                case RpcCalls.StartVanish:
                case RpcCalls.StartAppear:
                {
                    string sreason = "Direct Phantom RPCs " + rpc;
                    Report(pc, sreason);
                    MessageWriter swriter = AmongUsClient.Instance.StartRpcImmediately(pc.NetId, (byte)RpcCalls.StartAppear, SendOption.Reliable);
                    swriter.Write(false);
                    AmongUsClient.Instance.FinishRpcImmediately(swriter);
                    HandleCheat(pc, sreason);
                    Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()} {sreason}, rejected", "EAC");
                    return true;
                }
                case RpcCalls.CompleteTask when GameStates.IsMeeting && MeetingHud.Instance.state != MeetingHud.VoteStates.Animating && !pc.IsHost() && !(Main.CurrentMap == MapNames.Airship && ExileController.Instance):
                    Report(pc, "Complete Task in Meeting");
                    HandleCheat(pc, "Complete Task in Meeting");
                    Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] completed a task in a meeting, which has been rejected", "EAC");
                    return true;
            }

            switch (callId)
            {
                case 101: // Aum Chat
                    try
                    {
                        string firstString = sr.ReadString();
                        string secondString = sr.ReadString();
                        sr.ReadInt32();

                        bool flag = string.IsNullOrEmpty(firstString) && string.IsNullOrEmpty(secondString);

                        if (!flag)
                        {
                            Report(pc, "Aum Chat RPC");
                            HandleCheat(pc, "Aum Chat RPC");
                            Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] sent AUM chat, rejected", "EAC");
                            return true;
                        }
                    }
                    catch { }

                    break;
                case unchecked((byte)42069): // 85 AUM
                    try
                    {
                        byte aumid = sr.ReadByte();

                        if (aumid == pc.PlayerId)
                        {
                            Report(pc, "AUM RPC (Hack)");
                            HandleCheat(pc, "AUM RPC (Hack)");
                            Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] sent AUM RPC, rejected", "EAC");
                            return true;
                        }
                    }
                    catch { }

                    break;
                case unchecked((byte)420): // 164 Sicko
                    if (sr.BytesRemaining == 0)
                    {
                        Report(pc, "Sicko RPC (Hack against host-only mods, like EHR)");
                        HandleCheat(pc, "Sicko RPC (Hack against host-only mods, like EHR)");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] sent Sicko RPC, rejected", "EAC");
                        return true;
                    }

                    break;
                case 7 when !pc.IsHost():
                case 8 when !pc.IsHost():
                    if (!GameStates.IsLobby)
                    {
                        WarnHost();
                        Report(pc, "Illegal setting of color");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] illegally set the color, rejected", "EAC");
                        return true;
                    }

                    break;
                case 5 when !pc.IsHost():
                    sr.ReadString();

                    if (GameStates.IsInGame)
                    {
                        WarnHost();
                        Report(pc, "Illegal setting of game name");
                        Logger.Fatal($"Illegal modification of the game name of the player [{pc.GetClientId()}:{pc.GetRealName()}] has been rejected", "EAC");
                        return true;
                    }

                    break;
                case 47:
                    if (GameStates.IsLobby)
                    {
                        WarnHost();
                        Report(pc, "Illegal Killing");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] illegally killed, rejected", "EAC");
                        return true;
                    }

                    break;
                case 38 when !pc.IsHost():
                    if (GameStates.IsInGame)
                    {
                        WarnHost();
                        Report(pc, "Set level in game");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] changed the level in the game, which has been rejected", "EAC");
                        return true;
                    }

                    break;
                case 39 when !pc.IsHost():
                case 40 when !pc.IsHost():
                case 41 when !pc.IsHost():
                case 42 when !pc.IsHost():
                case 43 when !pc.IsHost():
                    if (GameStates.IsInGame)
                    {
                        WarnHost();
                        Report(pc, "Change skin in game");
                        Logger.Fatal($"Player [{pc.GetClientId()}:{pc.GetRealName()}] changed skin in the game, rejected", "EAC");
                        return true;
                    }

                    break;
            }
        }
        catch (Exception e) { Logger.Exception(e, "EAC"); }

        WarnHost(-1);
        return false;
    }

    public static bool PlayerPhysicsRpcCheck(PlayerPhysics __instance, byte callId, MessageReader reader) // Credit: NikoCat233
    {
        if (!AmongUsClient.Instance.AmHost) return false;

        var rpcType = (RpcCalls)callId;
        MessageReader subReader = MessageReader.Get(reader);

        PlayerControl player = __instance.myPlayer;

        if (!player)
        {
            Logger.Warn("Received Physics RPC without a player", "EAC_PlayerPhysics");
            return true;
        }

        if (GameStates.IsLobby && rpcType is not RpcCalls.Pet and not RpcCalls.CancelPet)
        {
            WarnHost();
            Report(player, $"Physics {rpcType} in lobby (can be spoofed by others)");
            HandleCheat(player, $"Physics {rpcType} in lobby (can be spoofed by others)");
            Logger.Fatal($"【{player.GetClientId()}:{player.GetRealName()}】 attempted to {rpcType} in lobby.", "EAC_physics");
            return true;
        }

        switch (rpcType)
        {
            case RpcCalls.EnterVent:
            case RpcCalls.ExitVent:
                int ventid = subReader.ReadPackedInt32();

                if (!HasVent(ventid))
                {
                    if (AmongUsClient.Instance.AmHost)
                    {
                        WarnHost();
                        Report(player, "Vent null vent (can be spoofed by others)");
                        HandleCheat(player, "Vent null vent (can be spoofed by others)");
                        Logger.Fatal($"【{player.GetClientId()}:{player.GetRealName()}】 attempted to enter an unexisting vent. {ventid}", "EAC_physics");
                    }
                    else
                    {
                        // Not sure whether host will send null vent to a player huh
                        Logger.Warn($"【{player.GetClientId()}:{player.GetRealName()}】 attempted to enter an unexisting vent. {ventid}", "EAC_physics");

                        if (rpcType is RpcCalls.ExitVent)
                        {
                            player.Visible = true;
                            player.inVent = false;
                            player.moveable = true;
                            player.NetTransform.SetPaused(false);
                        }
                    }

                    return true;
                }

                break;

            case RpcCalls.BootFromVent:
                // BootFromVent can only be sent by host
                WarnHost();
                Report(player, "Got boot from vent from clients, can be spoofed");
                HandleCheat(player, "Got boot from vent from clients, can be spoofed");
                Logger.Fatal($"【{player.GetClientId()}:{player.GetRealName()}】 sent boot from vent, can be spoofed.", "EAC_physics");
                break;

            case RpcCalls.ClimbLadder:
                int ladderId = subReader.ReadPackedInt32();

                if (!HasLadder(ladderId))
                {
                    if (AmongUsClient.Instance.AmHost)
                    {
                        WarnHost();
                        Report(player, "climb null ladder (can be spoofed by others)");
                        HandleCheat(player, "climb null ladder (can be spoofed by others)");
                        Logger.Fatal($"【{player.GetClientId()}:{player.GetRealName()}】 attempted to climb an unexisting ladder.", "EAC_physics");
                    }

                    return true;
                }

                if (player.AmOwner)
                {
                    Logger.Fatal("Got climb ladder for myself, this is impossible", "EAC_physics");
                    return true;
                }

                break;

            case RpcCalls.Pet:
                if (player.AmOwner)
                {
                    Logger.Fatal("Got pet pet for myself, this is impossible", "EAC_physics");
                    return true;
                }

                break;
        }

        return false;

        bool HasLadder(int ladderId) => ShipStatus.Instance.Ladders.Any(l => l.Id == ladderId);

        bool HasVent(int ventId) => ShipStatus.Instance.AllVents.Any(v => v.Id == ventId);
    }

    internal static void Report(PlayerControl pc, string reason)
    {
        var msg = $"{pc.GetClientId()}|{pc.FriendCode}|{pc.Data.PlayerName}|{pc.GetClient().GetHashedPuid()}|{reason}";
        //Cloud.SendData(msg);
        Logger.Fatal($"EAC report: {msg}", "EAC Cloud");
        if (Options.CheatResponses.GetInt() != 5) Logger.SendInGame(string.Format(GetString("Message.NoticeByEAC"), $"{pc.Data?.PlayerName} | {pc.GetClient().GetHashedPuid()}", reason));
    }

    public static bool ReceiveInvalidRpc(PlayerControl pc, byte callId)
    {
        switch (callId)
        {
            case unchecked((byte)42069):
                Report(pc, "AUM");
                HandleCheat(pc, GetString("EAC.CheatDetected.EAC"));
                return true;
        }

        return true;
    }

    private static void HandleCheat(PlayerControl pc, string text)
    {
        switch (Options.CheatResponses.GetInt())
        {
            case 0:
                AmongUsClient.Instance.KickPlayer(pc.GetClientId(), true);
                string msg0 = string.Format(GetString("Message.BannedByEAC"), pc?.Data?.PlayerName, text);
                Logger.Warn(msg0, "EAC");
                Logger.SendInGame(msg0);
                break;
            case 1:
                AmongUsClient.Instance.KickPlayer(pc.GetClientId(), false);
                string msg1 = string.Format(GetString("Message.KickedByEAC"), pc?.Data?.PlayerName, text);
                Logger.Warn(msg1, "EAC");
                Logger.SendInGame(msg1);
                break;
            case 2:
                Utils.SendMessage(string.Format(GetString("Message.NoticeByEAC"), pc?.Data?.PlayerName, text), PlayerControl.LocalPlayer.PlayerId, Utils.ColorString(Utils.GetRoleColor(CustomRoles.Impostor), GetString("MessageFromEAC")));
                break;
            case 3:
                (
                    from player in Main.AllPlayerControls
                    where player.PlayerId != pc?.Data?.PlayerId
                    let message = string.Format(GetString("Message.NoticeByEAC"), pc?.Data?.PlayerName, text)
                    let title = Utils.ColorString(Utils.GetRoleColor(CustomRoles.Impostor), GetString("MessageFromEAC"))
                    select new Message(message, player.PlayerId, title)
                ).SendMultipleMessages(SendOption.None);
                break;
            case 4:
                string hashedPuid = pc.GetClient().GetHashedPuid();
                if (!BanManager.TempBanWhiteList.Contains(hashedPuid)) BanManager.TempBanWhiteList.Add(hashedPuid);

                AmongUsClient.Instance.KickPlayer(pc.GetClientId(), true);
                string msg2 = string.Format(GetString("Message.TempBannedByEAC"), pc?.Data?.PlayerName, text);
                Logger.Warn(msg2, "EAC");
                Logger.SendInGame(msg2);
                break;
        }
    }

    internal static bool CheckInvalidSabotage(SystemTypes systemType, PlayerControl player, byte amount)
    {
        if (player.IsModdedClient() || !AmongUsClient.Instance.AmHost) return false;

        if (GameStates.IsMeeting && MeetingHud.Instance.state is MeetingHud.VoteStates.Voted or MeetingHud.VoteStates.NotVoted or MeetingHud.VoteStates.Discussion)
        {
            WarnHost();
            Report(player, "Bad Sabotage D : In Meeting");
            HandleCheat(player, "Bad Sabotage D : In Meeting");
            Logger.Fatal($"Player [{player.GetClientId()}:{player.GetRealName()}] Bad Sabotage D, rejected", "EAC");
            return true;
        }

        byte mapid = Main.NormalOptions.MapId;

        switch (systemType)
        {
            case SystemTypes.LifeSupp:
                if (mapid != 0 && mapid != 1 && mapid != 3) goto Cheat;
                if (amount != 64 && amount != 65) goto Cheat;
                break;
            case SystemTypes.Comms:
                switch (amount)
                {
                    case 0:
                        if (mapid is 1 or 5) goto Cheat;
                        break;
                    case 64:
                    case 65:
                    case 32:
                    case 33:
                    case 16:
                    case 17:
                        if (mapid is not (1 or 5)) goto Cheat;
                        break;
                    default:
                        goto Cheat;
                }

                break;
            case SystemTypes.Electrical:
                if (mapid == 5) goto Cheat;
                if (amount >= 5) goto Cheat;
                break;
            case SystemTypes.Laboratory:
                if (mapid != 2) goto Cheat;
                if (amount is not (64 or 65 or 32 or 33)) goto Cheat;
                break;
            case SystemTypes.Reactor:
                if (mapid is 2 or 4) goto Cheat;
                if (amount is not (64 or 65 or 32 or 33)) goto Cheat;
                break;
            case SystemTypes.HeliSabotage:
                if (mapid != 4) goto Cheat;
                if (amount is not (64 or 65 or 16 or 17 or 32 or 33)) goto Cheat;
                break;
            case SystemTypes.MushroomMixupSabotage:
                goto Cheat;
        }

        return false;

        Cheat:

        {
            WarnHost();
            Report(player, "Bad Sabotage C : Hack send RPC");
            HandleCheat(player, "Bad Sabotage C");
            Logger.Fatal($"Player [{player.GetClientId()}:{player.GetRealName()}] Bad Sabotage C, rejected", "EAC");
            return true;
        }
    }
}

// https://github.com/0xDrMoe/TownofHost-Enhanced/blob/main/Patches/InnerNetClientPatch.cs
internal enum GameDataTag : byte
{
    DataFlag = 1,
    RpcFlag = 2,
    SpawnFlag = 4,
    DespawnFlag = 5,
    SceneChangeFlag = 6,
    ReadyFlag = 7,
    ChangeSettingsFlag = 8,
    ConsoleDeclareClientPlatformFlag = 205,
    PS4RoomRequest = 206,
    XboxDeclareXuid = 207
}

[HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.HandleGameDataInner))]
internal class GameDataHandlerPatch
{
    public static bool Prefix(InnerNetClient __instance, MessageReader reader, int msgNum)
    {
        var tag = (GameDataTag)reader.Tag;

        switch (tag)
        {
            case GameDataTag.DataFlag:
            {
                uint netId = reader.ReadPackedUInt32();

                if (__instance.allObjectsFast.TryGetValue(netId, out InnerNetObject obj))
                {
                    if (obj.AmOwner)
                    {
                        Logger.Warn($"Received DataFlag for object {netId.ToString()} {obj.name} that we own.", "GameDataHandlerPatch");
                        EAC.WarnHost();
                        return false;
                    }

                    if (AmongUsClient.Instance.AmHost)
                    {
                        if (obj == MeetingHud.Instance)
                        {
                            Logger.Warn($"Received DataFlag for MeetingHud {netId.ToString()} that we own.", "GameDataHandlerPatch");
                            EAC.WarnHost();
                            return false;
                        }

                        if (obj == VoteBanSystem.Instance)
                        {
                            Logger.Warn($"Received DataFlag for VoteBanSystem {netId.ToString()} that we own.", "GameDataHandlerPatch");
                            EAC.WarnHost();
                            return false;
                        }

                        if (obj is NetworkedPlayerInfo)
                        {
                            Logger.Warn($"Received DataFlag for NetworkedPlayerInfo {netId.ToString()} that we own.", "GameDataHandlerPatch");
                            EAC.WarnHost();
                            return false;
                        }
                    }
                }

                break;
            }

            case GameDataTag.RpcFlag:
            case GameDataTag.SpawnFlag:
            case GameDataTag.DespawnFlag:
                break;

            case GameDataTag.SceneChangeFlag:
            {
                // Sender is only allowed to change his own scene.
                int clientId = reader.ReadPackedInt32();
                string scene = reader.ReadString();

                ClientData client = Utils.GetClientById(clientId);

                if (client == null)
                {
                    Logger.Warn($"Received SceneChangeFlag for unknown client {clientId}.", "GameDataHandlerPatch");
                    return false;
                }

                if (string.IsNullOrEmpty(scene))
                {
                    Logger.Warn($"Client {client.PlayerName} ({client.Id}) tried to send SceneChangeFlag with null scene.", "GameDataHandlerPatch");
                    EAC.WarnHost();
                    return false;
                }

                if (scene.ToLower() == "tutorial")
                {
                    Logger.Warn($"Client {client.PlayerName} ({client.Id}) tried to send SceneChangeFlag to Tutorial.", "GameDataHandlerPatch");
                    EAC.WarnHost(100);

                    if (GameStates.IsOnlineGame && AmongUsClient.Instance.AmHost) Utils.ErrorEnd("SceneChange Tutorial Hack");

                    return false;
                }

                if (GameStates.IsInGame)
                {
                    Logger.Warn($"Client {client.PlayerName} ({client.Id}) tried to send SceneChangeFlag during mid of game.", "GameDataHandlerPatch");
                    return false;
                }

                break;
            }

            case GameDataTag.ReadyFlag:
            {
                int clientId = reader.ReadPackedInt32();
                ClientData client = Utils.GetClientById(clientId);

                if (client == null)
                {
                    Logger.Warn($"Received ReadyFlag for unknown client {clientId}.", "GameDataHandlerPatch");
                    EAC.WarnHost();
                    return false;
                }

                if (AmongUsClient.Instance.AmHost)
                {
                    if (!StartGameHostPatchEAC.IsStartingAsHost)
                    {
                        Logger.Warn($"Received ReadyFlag while game is started from {clientId}.", "GameDataHandlerPatch");
                        EAC.WarnHost();
                        return false;
                    }
                }

                break;
            }

            case GameDataTag.ConsoleDeclareClientPlatformFlag:
                break;
        }

        return true;
    }
}

[HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGameHost))]
internal static class StartGameHostPatchEAC
{
    public static bool IsStartingAsHost;

    public static void Prefix()
    {
        if (LobbyBehaviour.Instance != null) IsStartingAsHost = true;
    }

    public static void Postfix()
    {
        if (ShipStatus.Instance != null) IsStartingAsHost = false;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
internal static class CheckInvalidMovementPatch
{
    private static readonly Dictionary<byte, long> LastCheck = [];
    public static readonly Dictionary<byte, Vector2> LastPosition = [];
    public static readonly HashSet<byte> ExemptedPlayers = [];

    public static void Postfix(PlayerControl __instance)
    {
        if (!GameStates.IsInTask || ExileController.Instance || !Options.EnableMovementChecking.GetBool() || Main.HasJustStarted || MeetingStates.FirstMeeting || Main.RealOptionsData.GetFloat(FloatOptionNames.PlayerSpeedMod) >= 1.9f || AmongUsClient.Instance.Ping >= 300 || CustomGameMode.NaturalDisasters.IsActiveOrIntegrated() || Utils.GetRegionName() is not ("EU" or "NA" or "AS") || __instance == null || __instance.PlayerId >= 254 || !__instance.IsAlive() || __instance.inVent) return;

        Vector2 pos = __instance.Pos();
        long now = Utils.TimeStamp;

        if (!LastPosition.TryGetValue(__instance.PlayerId, out Vector2 lastPosition))
        {
            SetCurrentData();
            return;
        }

        if (LastCheck.TryGetValue(__instance.PlayerId, out long lastCheck) && lastCheck == now) return;

        SetCurrentData();

        if (Vector2.Distance(lastPosition, pos) > 10f && PhysicsHelpers.AnythingBetween(__instance.Collider, lastPosition, pos, Constants.ShipOnlyMask, false))
        {
            if (ExemptedPlayers.Remove(__instance.PlayerId)) return;

            EAC.WarnHost();
            EAC.Report(__instance, "This player is moving too fast, possibly using a speed hack.");
        }

        return;

        void SetCurrentData()
        {
            LastPosition[__instance.PlayerId] = pos;
            LastCheck[__instance.PlayerId] = now;
        }
    }
}