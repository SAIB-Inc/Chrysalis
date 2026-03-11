using System.Numerics;
using Chrysalis.Plutus.Cek;
using static Chrysalis.Plutus.Builtins.BuiltinHelpers;

namespace Chrysalis.Plutus.Builtins;

internal static class IntegerBuiltins
{
    internal static CekValue AddInteger(CekValue[] args)
    {
        return IntegerResult(UnwrapInteger(args[0]) + UnwrapInteger(args[1]));
    }

    internal static CekValue SubtractInteger(CekValue[] args)
    {
        return IntegerResult(UnwrapInteger(args[0]) - UnwrapInteger(args[1]));
    }

    internal static CekValue MultiplyInteger(CekValue[] args)
    {
        return IntegerResult(UnwrapInteger(args[0]) * UnwrapInteger(args[1]));
    }

    internal static CekValue QuotientInteger(CekValue[] args)
    {
        BigInteger a = UnwrapInteger(args[0]);
        BigInteger b = UnwrapInteger(args[1]);
        return b.IsZero ? throw new EvaluationException("division by zero") : IntegerResult(BigInteger.DivRem(a, b, out _));
    }

    internal static CekValue RemainderInteger(CekValue[] args)
    {
        BigInteger a = UnwrapInteger(args[0]);
        BigInteger b = UnwrapInteger(args[1]);
        if (b.IsZero)
        {
            throw new EvaluationException("division by zero");
        }

        _ = BigInteger.DivRem(a, b, out BigInteger rem);
        return IntegerResult(rem);
    }

    internal static CekValue DivideInteger(CekValue[] args)
    {
        BigInteger a = UnwrapInteger(args[0]);
        BigInteger b = UnwrapInteger(args[1]);
        if (b.IsZero)
        {
            throw new EvaluationException("division by zero");
        }

        BigInteger q = BigInteger.DivRem(a, b, out BigInteger r);
        // Floor division: adjust when remainder is nonzero and signs differ
        if (!r.IsZero && (a < 0) != (b < 0))
        {
            q -= 1;
        }

        return IntegerResult(q);
    }

    internal static CekValue ModInteger(CekValue[] args)
    {
        BigInteger a = UnwrapInteger(args[0]);
        BigInteger b = UnwrapInteger(args[1]);
        if (b.IsZero)
        {
            throw new EvaluationException("division by zero");
        }

        _ = BigInteger.DivRem(a, b, out BigInteger r);
        // Floor mod: adjust when remainder is nonzero and signs differ
        if (!r.IsZero && (a < 0) != (b < 0))
        {
            r += b;
        }

        return IntegerResult(r);
    }

    internal static CekValue EqualsInteger(CekValue[] args)
    {
        return BoolResult(UnwrapInteger(args[0]) == UnwrapInteger(args[1]));
    }

    internal static CekValue LessThanInteger(CekValue[] args)
    {
        return BoolResult(UnwrapInteger(args[0]) < UnwrapInteger(args[1]));
    }

    internal static CekValue LessThanEqualsInteger(CekValue[] args)
    {
        return BoolResult(UnwrapInteger(args[0]) <= UnwrapInteger(args[1]));
    }

    internal static CekValue ExpModInteger(CekValue[] args)
    {
        BigInteger @base = UnwrapInteger(args[0]);
        BigInteger exp = UnwrapInteger(args[1]);
        BigInteger mod = UnwrapInteger(args[2]);

        if (mod <= 0)
        {
            throw new EvaluationException("expModInteger: modulus must be positive");
        }

        if (mod == 1)
        {
            return IntegerResult(0);
        }

        if (exp.IsZero)
        {
            return IntegerResult(1);
        }

        if (exp > 0)
        {
            return IntegerResult(BigIntMod(BigInteger.ModPow(@base, exp, mod), mod));
        }

        // Negative exponent
        if (@base.IsZero)
        {
            throw new EvaluationException("expModInteger: zero base with negative exponent");
        }

        BigInteger reducedBase = BigIntMod(@base, mod);
        if (BigInteger.GreatestCommonDivisor(reducedBase, mod) != 1)
        {
            throw new EvaluationException("expModInteger: base and modulus are not coprime");
        }

        BigInteger inv = ModInverse(reducedBase, mod);
        return IntegerResult(BigIntMod(BigInteger.ModPow(inv, -exp, mod), mod));
    }

    private static BigInteger BigIntMod(BigInteger a, BigInteger m)
    {
        BigInteger r = a % m;
        return r < 0 ? r + m : r;
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        BigInteger oldR = a, r = m;
        BigInteger oldS = 1, s = 0;

        while (!r.IsZero)
        {
            BigInteger q = FloorDiv(oldR, r);

            BigInteger tempR = r;
            r = oldR - (q * r);
            oldR = tempR;

            BigInteger tempS = s;
            s = oldS - (q * s);
            oldS = tempS;
        }

        return BigIntMod(oldS, m);
    }

    private static BigInteger FloorDiv(BigInteger a, BigInteger b)
    {
        BigInteger q = BigInteger.DivRem(a, b, out BigInteger rem);
        // Adjust truncating division to floor division
        if (!rem.IsZero && (a < 0) != (b < 0))
        {
            q -= 1;
        }
        return q;
    }
}
