using System.Collections.Generic;
using SR2E.Enums;
using SR2E.Managers;
using SR2E.Patches.Options;

namespace SR2E.Commands;

internal class MaxFpsCommand : SR2ECommand
{
    public override string ID => "maxfps";
    public override string Usage => "maxfps <target>";
    public override CommandType type => CommandType.Common;

    public override List<string> GetAutoComplete(int argIndex, string[] args)
    {
        if (argIndex == 0)
        {
            return new List<string>
            {
                "-1", "20", "30", "60", "120", "240", "480", "500", "600", "700",
                "800", "900", "1000", "2500", "5000", "10000"
            };
        }
        return null;
    }

    public override bool Execute(string[] args)
    {
        if (!args.IsBetween(1u, 1))
            return SendNoArguments();

        int value = -1;
        if (args != null && !TryParseInt(args[0], out value, 0, inclusive: false))
            return false;

        OptionsUIRootApplyPatch.customMaxFPS = value;
        SendMessage(SR2ELanguageManger.translation("cmd.maxfps.success", value));
        OptionsUIRootApplyPatch.Apply();
        return true;
    }
}
