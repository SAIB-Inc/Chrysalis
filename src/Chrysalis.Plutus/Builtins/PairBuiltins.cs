using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Builtins;

internal static class PairBuiltins
{
    internal static CekValue FstPair(CekValue[] args)
    {
        return args[0] is VConstant { Value: PairConstant p }
            ? new VConstant(p.First)
            : throw new EvaluationException(
                $"fstPair: expected pair constant, got {args[0].GetType().Name}");
    }

    internal static CekValue SndPair(CekValue[] args)
    {
        return args[0] is VConstant { Value: PairConstant p }
            ? new VConstant(p.Second)
            : throw new EvaluationException(
                $"sndPair: expected pair constant, got {args[0].GetType().Name}");
    }
}
