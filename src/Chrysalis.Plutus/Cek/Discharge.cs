using System.Collections.Immutable;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Converts CEK runtime values back to UPLC terms (spec section 2.4.1, Figure 2.11).
/// </summary>
internal static class Discharge
{
    internal static Term<DeBruijn> DischargeValue(CekValue value)
    {
        return value switch
        {
            VConstant vc => new ConstTerm<DeBruijn>(vc.Value),
            VLambda vl => new LambdaTerm<DeBruijn>(vl.Parameter, WithEnv(1, vl.Env, vl.Body)),
            VDelay vd => new DelayTerm<DeBruijn>(WithEnv(0, vd.Env, vd.Body)),
            VBuiltin vb => DischargeBuiltin(vb),
            VConstr vcr => new ConstrTerm<DeBruijn>(vcr.Index, DischargeAll(vcr.Fields, vcr.FieldCount)),
            _ => throw new InvalidOperationException($"Unknown CekValue type: {value.GetType().Name}")
        };
    }

    private static Term<DeBruijn> DischargeBuiltin(VBuiltin b)
    {
        Term<DeBruijn> term = new BuiltinTerm<DeBruijn>(b.Function);

        for (int i = 0; i < b.Forces; i++)
        {
            term = new ForceTerm<DeBruijn>(term);
        }

        for (int i = 0; i < b.ArgCount; i++)
        {
            term = new ApplyTerm<DeBruijn>(term, DischargeValue(b.Args[i]));
        }

        return term;
    }

    private static ImmutableArray<Term<DeBruijn>> DischargeAll(CekValue[] fields, int count)
    {
        ImmutableArray<Term<DeBruijn>>.Builder builder =
            ImmutableArray.CreateBuilder<Term<DeBruijn>>(count);
        for (int i = 0; i < count; i++)
        {
            builder.Add(DischargeValue(fields[i]));
        }
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Substitute environment bindings into a term during discharge.
    /// lamCnt tracks how many lambdas we've descended through.
    /// For each var: if index &lt;= lamCnt, it's bound by an enclosing lambda — leave it.
    /// If index &gt; lamCnt, look up (index - lamCnt) in env and discharge that value.
    /// </summary>
    internal static Term<DeBruijn> WithEnv(int lamCnt, Environment? env, Term<DeBruijn> term)
    {
        return term switch
        {
            VarTerm<DeBruijn> v => WithEnvVar(lamCnt, env, v),
            LambdaTerm<DeBruijn> l => new LambdaTerm<DeBruijn>(l.Parameter, WithEnv(lamCnt + 1, env, l.Body)),
            ApplyTerm<DeBruijn> a => new ApplyTerm<DeBruijn>(
                WithEnv(lamCnt, env, a.Function),
                WithEnv(lamCnt, env, a.Argument)),
            DelayTerm<DeBruijn> d => new DelayTerm<DeBruijn>(WithEnv(lamCnt, env, d.Body)),
            ForceTerm<DeBruijn> f => new ForceTerm<DeBruijn>(WithEnv(lamCnt, env, f.Body)),
            ConstrTerm<DeBruijn> c => new ConstrTerm<DeBruijn>(c.Tag, WithEnvAll(lamCnt, env, c.Fields)),
            CaseTerm<DeBruijn> cs => new CaseTerm<DeBruijn>(
                WithEnv(lamCnt, env, cs.Scrutinee),
                WithEnvAll(lamCnt, env, cs.Branches)),
            ConstTerm<DeBruijn> or BuiltinTerm<DeBruijn> or ErrorTerm<DeBruijn> => term,
            _ => term
        };
    }

    private static Term<DeBruijn> WithEnvVar(int lamCnt, Environment? env, VarTerm<DeBruijn> v)
    {
        int idx = v.Name.Index;
        if (idx <= lamCnt)
        {
            return v;
        }

        int envIdx = idx - lamCnt;
        Environment? current = env;
        int i = envIdx;
        while (current is not null)
        {
            if (i == 1)
            {
                return DischargeValue(current.Value);
            }

            i--;
            current = current.Next;
        }

        return v;
    }

    private static ImmutableArray<Term<DeBruijn>> WithEnvAll(
        int lamCnt, Environment? env, ImmutableArray<Term<DeBruijn>> terms)
    {
        ImmutableArray<Term<DeBruijn>>.Builder builder =
            ImmutableArray.CreateBuilder<Term<DeBruijn>>(terms.Length);
        foreach (Term<DeBruijn> t in terms)
        {
            builder.Add(WithEnv(lamCnt, env, t));
        }
        return builder.MoveToImmutable();
    }
}
