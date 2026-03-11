using Chrysalis.Plutus.Cek;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class ControlBuiltins
{
    internal static CekValue IfThenElse(CekValue[] args) => UnwrapBool(args[0]) ? args[1] : args[2];

    internal static CekValue ChooseUnit(CekValue[] args)
    {
        UnwrapUnit(args[0]);
        return args[1];
    }

    internal static CekValue Trace(CekValue[] args) => args[1];
}
