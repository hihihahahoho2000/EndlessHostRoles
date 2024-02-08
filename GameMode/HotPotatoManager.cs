﻿using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TOHE
{
    internal class HotPotatoManager
    {
        private static OptionItem Time;
        private static OptionItem HolderSpeed;
        private static OptionItem Chat;
        private static OptionItem Range;

        private static (byte HolderID, byte LastHolderID, int TimeLeft, int RoundNum) HotPotatoState;
        private static Dictionary<byte, int> SurvivalTimes;

        public static void SetupCustomOption()
        {
            Time = IntegerOptionItem.Create(68_213_001, "HotPotato_Time", new(1, 90, 1), 20, TabGroup.GameSettings, false)
                .SetGameMode(CustomGameMode.HotPotato)
                .SetValueFormat(OptionFormat.Seconds)
                .SetColor(new(232, 205, 70));
            HolderSpeed = FloatOptionItem.Create(68_213_002, "HotPotato_HolderSpeed", new(0.1f, 5f, 0.1f), 1.5f, TabGroup.GameSettings, false)
                .SetGameMode(CustomGameMode.HotPotato)
                .SetValueFormat(OptionFormat.Multiplier)
                .SetColor(new(232, 205, 70));
            Chat = BooleanOptionItem.Create(68_213_003, "FFA_ChatDuringGame", false, TabGroup.GameSettings, false)
                .SetGameMode(CustomGameMode.HotPotato)
                .SetColor(new(232, 205, 70));
            Range = FloatOptionItem.Create(68_213_004, "HotPotato_Range", new(0.25f, 5f, 0.25f), 1f, TabGroup.GameSettings, false)
                .SetGameMode(CustomGameMode.HotPotato)
                .SetValueFormat(OptionFormat.Multiplier)
                .SetColor(new(232, 205, 70));
        }

        public static void Init()
        {
            HotPotatoState = (byte.MaxValue, byte.MaxValue, Time.GetInt() + 15, 1);
            SurvivalTimes = [];
            foreach (var pc in Main.AllPlayerControls) SurvivalTimes[pc.PlayerId] = 0;

            if (Chat.GetBool()) _ = new LateTask(Utils.SetChatVisible, 7f, "Set Chat Visible for Everyone");
        }

        public static int GetSurvivalTime(byte id) => SurvivalTimes.TryGetValue(id, out var time) ? time : 0;
        public static string GetIndicator(byte id) => HotPotatoState.HolderID == id ? " ★ " : string.Empty;
        public static string GetSuffixText(byte id) => $"{(HotPotatoState.HolderID == id ? $"{Translator.GetString("HotPotato_HoldingNotify")}\n" : string.Empty)}{Translator.GetString("HotPotato_TimeLeftSuffix")}{HotPotatoState.TimeLeft}s";

        [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
        class FixedUpdatePatch
        {
            private static long LastFixedUpdate = 0;
            public static void Postfix(PlayerControl __instance)
            {
                if (Options.CurrentGameMode != CustomGameMode.HotPotato || !AmongUsClient.Instance.AmHost || !GameStates.IsInTask) return;

                PlayerControl Holder = Utils.GetPlayerById(HotPotatoState.HolderID);
                if (Holder == null || Holder.Data.Disconnected || !Holder.IsAlive())
                {
                    PassHotPotato();
                    return;
                }

                long now = Utils.GetTimeStamp();
                if (now > LastFixedUpdate)
                {
                    HotPotatoState.TimeLeft--;
                    LastFixedUpdate = now;
                }

                if (HotPotatoState.TimeLeft <= 0)
                {
                    Holder.Suicide();
                    PassHotPotato();
                    return;
                }

                if (HotPotatoState.HolderID != __instance.PlayerId || !Main.AllAlivePlayerControls.Any(x => Vector2.Distance(x.Pos(), Holder.Pos()) <= Range.GetFloat())) return;

                var Target = Main.AllAlivePlayerControls.Where(x => Vector2.Distance(x.Pos(), Holder.Pos()) <= Range.GetFloat()).OrderBy(x => Vector2.Distance(x.Pos(), Holder.Pos())).FirstOrDefault();
                if (Target == null || HotPotatoState.LastHolderID == Target.PlayerId) return;

                PassHotPotato(Target);
            }
            private static void PassHotPotato(PlayerControl target = null)
            {
                target ??= Main.AllAlivePlayerControls[IRandom.Instance.Next(0, Main.AllAlivePlayerControls.Length)];

                HotPotatoState.LastHolderID = HotPotatoState.HolderID;
                HotPotatoState.HolderID = target.PlayerId;

                Main.AllPlayerSpeed[target.PlayerId] = HolderSpeed.GetFloat();
                target.MarkDirtySettings();

                PlayerControl LastHolder = Utils.GetPlayerById(HotPotatoState.LastHolderID);
                if (LastHolder != null) Main.AllPlayerSpeed[HotPotatoState.LastHolderID] = Main.NormalOptions.PlayerSpeedMod;
                LastHolder.MarkDirtySettings();
            }
        }
    }
}
