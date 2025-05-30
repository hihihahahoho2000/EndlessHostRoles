﻿using AmongUs.GameOptions;
using EHR.Modules;

namespace EHR.Impostor;

internal class Escapee : RoleBase
{
    public static bool On;

    public static OptionItem EscapeeSSCD;
    public static OptionItem CanVent;

    public Vector2? EscapeeLocation;
    public override bool IsEnable => On;

    public override void SetupCustomOption()
    {
        Options.SetupRoleOptions(3600, TabGroup.ImpostorRoles, CustomRoles.Escapee);

        EscapeeSSCD = new FloatOptionItem(3611, "ShapeshiftCooldown", new(1f, 180f, 1f), 5f, TabGroup.ImpostorRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Escapee])
            .SetValueFormat(OptionFormat.Seconds);

        CanVent = new BooleanOptionItem(3612, "CanVent", false, TabGroup.ImpostorRoles)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Escapee]);
    }

    public override void Add(byte playerId)
    {
        On = true;
        EscapeeLocation = null;
    }

    public override void Init()
    {
        On = false;
        EscapeeLocation = null;
    }

    public override bool CanUseImpostorVentButton(PlayerControl pc)
    {
        return CanVent.GetBool();
    }

    public override void SetButtonTexts(HudManager hud, byte id)
    {
        if (Options.UsePets.GetBool())
            hud.PetButton?.OverrideText(Translator.GetString("EscapeeAbilityButtonText"));
        else
            hud.AbilityButton?.OverrideText(Translator.GetString("EscapeeAbilityButtonText"));
    }

    public override void ApplyGameOptions(IGameOptions opt, byte id)
    {
        if (Options.UsePhantomBasis.GetBool())
            AURoleOptions.PhantomCooldown = EscapeeSSCD.GetFloat();
        else
        {
            if (Options.UsePets.GetBool()) return;

            AURoleOptions.ShapeshifterCooldown = EscapeeSSCD.GetFloat();
            AURoleOptions.ShapeshifterDuration = 1f;
        }
    }

    public override void OnPet(PlayerControl pc)
    {
        TeleportOrMark(pc);
    }

    public override bool OnVanish(PlayerControl pc)
    {
        TeleportOrMark(pc);
        return false;
    }

    private void TeleportOrMark(PlayerControl pc)
    {
        if (EscapeeLocation != null)
        {
            var position = (Vector2)EscapeeLocation;
            EscapeeLocation = null;
            pc.TP(position);
            pc.RPCPlayCustomSound("Teleport");
        }
        else
            EscapeeLocation = pc.Pos();
    }

    public override bool OnShapeshift(PlayerControl shapeshifter, PlayerControl target, bool shapeshifting)
    {
        if (shapeshifting || Options.UseUnshiftTrigger.GetBool()) TeleportOrMark(shapeshifter);

        return false;
    }
}