using System.Text;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Text;

/// <summary>
/// Pretty-prints UPLC terms and programs to text format.
/// Ported from blaze-plutus pretty.ts.
/// </summary>
internal static class PrettyPrinter
{
    internal static string Print(Term<DeBruijn> term)
    {
        StringBuilder sb = new();
        PrintTerm(sb, term);
        return sb.ToString();
    }

    internal static string Print(Program<DeBruijn> program)
    {
        StringBuilder sb = new();
        _ = sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"(program {program.Version} ");
        PrintTerm(sb, program.Term);
        _ = sb.Append(')');
        return sb.ToString();
    }

    private static void PrintTerm(StringBuilder sb, Term<DeBruijn> term)
    {
        switch (term)
        {
            case VarTerm<DeBruijn> v:
                _ = sb.Append('i');
                _ = sb.Append(v.Name.Index);
                break;

            case LambdaTerm<DeBruijn> lam:
                _ = sb.Append("(lam i");
                _ = sb.Append(lam.Parameter.Index);
                _ = sb.Append(' ');
                PrintTerm(sb, lam.Body);
                _ = sb.Append(')');
                break;

            case ApplyTerm<DeBruijn> app:
                _ = sb.Append('[');
                PrintTerm(sb, app.Function);
                _ = sb.Append(' ');
                PrintTerm(sb, app.Argument);
                _ = sb.Append(']');
                break;

            case DelayTerm<DeBruijn> delay:
                _ = sb.Append("(delay ");
                PrintTerm(sb, delay.Body);
                _ = sb.Append(')');
                break;

            case ForceTerm<DeBruijn> force:
                _ = sb.Append("(force ");
                PrintTerm(sb, force.Body);
                _ = sb.Append(')');
                break;

            case ConstrTerm<DeBruijn> constr:
                _ = sb.Append("(constr ");
                _ = sb.Append(constr.Tag);
                foreach (Term<DeBruijn> field in constr.Fields)
                {
                    _ = sb.Append(' ');
                    PrintTerm(sb, field);
                }
                _ = sb.Append(')');
                break;

            case CaseTerm<DeBruijn> caseTerm:
                _ = sb.Append("(case ");
                PrintTerm(sb, caseTerm.Scrutinee);
                foreach (Term<DeBruijn> branch in caseTerm.Branches)
                {
                    _ = sb.Append(' ');
                    PrintTerm(sb, branch);
                }
                _ = sb.Append(')');
                break;

            case ConstTerm<DeBruijn> con:
                _ = sb.Append("(con ");
                PrintConstant(sb, con.Value);
                _ = sb.Append(')');
                break;

            case BuiltinTerm<DeBruijn> builtin:
                _ = sb.Append("(builtin ");
                _ = sb.Append(BuiltinNames.GetName(builtin.Function));
                _ = sb.Append(')');
                break;

            case ErrorTerm<DeBruijn>:
                _ = sb.Append("(error)");
                break;
            default:
                break;
        }
    }

    private static void PrintConstant(StringBuilder sb, Constant constant)
    {
        switch (constant)
        {
            case IntegerConstant c:
                _ = sb.Append("integer ");
                _ = sb.Append(c.Value);
                break;

            case ByteStringConstant c:
                _ = sb.Append("bytestring #");
                AppendHex(sb, c.Value.Span);
                break;

            case StringConstant c:
                _ = sb.Append("string \"");
                AppendEscaped(sb, c.Value);
                _ = sb.Append('"');
                break;

            case BoolConstant c:
                _ = sb.Append("bool ");
                _ = sb.Append(c.Value ? "True" : "False");
                break;

            case UnitConstant:
                _ = sb.Append("unit ()");
                break;

            case DataConstant c:
                _ = sb.Append("data (");
                PrintPlutusData(sb, c.Value);
                _ = sb.Append(')');
                break;

            case ListConstant c:
                _ = sb.Append('(');
                _ = sb.Append("list ");
                PrintType(sb, c.ItemType);
                _ = sb.Append(") [");
                for (int i = 0; i < c.Values.Length; i++)
                {
                    if (i > 0)
                    {
                        _ = sb.Append(", ");
                    }

                    PrintConstantInner(sb, c.Values[i]);
                }
                _ = sb.Append(']');
                break;

            case ArrayConstant c:
                _ = sb.Append('(');
                _ = sb.Append("array ");
                PrintType(sb, c.ItemType);
                _ = sb.Append(") [");
                for (int i = 0; i < c.Values.Length; i++)
                {
                    if (i > 0)
                    {
                        _ = sb.Append(',');
                    }

                    PrintConstantInner(sb, c.Values[i]);
                }
                _ = sb.Append(']');
                break;

            case PairConstant c:
                _ = sb.Append('(');
                _ = sb.Append("pair ");
                PrintType(sb, c.FstType);
                _ = sb.Append(' ');
                PrintType(sb, c.SndType);
                _ = sb.Append(") (");
                PrintConstantInner(sb, c.First);
                _ = sb.Append(", ");
                PrintConstantInner(sb, c.Second);
                _ = sb.Append(')');
                break;

            case Bls12381G1Constant c:
                _ = sb.Append("bls12_381_G1_element 0x");
                AppendHex(sb, c.Value.Span);
                break;

            case Bls12381G2Constant c:
                _ = sb.Append("bls12_381_G2_element 0x");
                AppendHex(sb, c.Value.Span);
                break;

            case Bls12381MlResultConstant:
                _ = sb.Append("bls12_381_mlresult ...");
                break;

            case ValueConstant c:
                _ = sb.Append("value [");
                for (int i = 0; i < c.Value.Entries.Length; i++)
                {
                    if (i > 0)
                    {
                        _ = sb.Append(", ");
                    }
                    CurrencyEntry entry = c.Value.Entries[i];
                    _ = sb.Append("(#");
                    AppendHex(sb, entry.Currency.Span);
                    _ = sb.Append(", [");
                    for (int j = 0; j < entry.Tokens.Length; j++)
                    {
                        if (j > 0)
                        {
                            _ = sb.Append(", ");
                        }
                        TokenEntry tok = entry.Tokens[j];
                        _ = sb.Append("(#");
                        AppendHex(sb, tok.Name.Span);
                        _ = sb.Append(", ");
                        _ = sb.Append(tok.Quantity);
                        _ = sb.Append(')');
                    }
                    _ = sb.Append("])");
                }
                _ = sb.Append(']');
                break;
            default:
                break;
        }
    }

    private static void PrintConstantInner(StringBuilder sb, Constant constant)
    {
        if (constant is DataConstant data)
        {
            PrintPlutusData(sb, data.Value);
        }
        else
        {
            PrintConstant(sb, constant);
        }
    }

    private static void PrintType(StringBuilder sb, ConstantType type)
    {
        switch (type)
        {
            case IntegerType: _ = sb.Append("integer"); break;
            case ByteStringType: _ = sb.Append("bytestring"); break;
            case StringType: _ = sb.Append("string"); break;
            case BoolType: _ = sb.Append("bool"); break;
            case UnitType: _ = sb.Append("unit"); break;
            case DataType: _ = sb.Append("data"); break;
            case Bls12381G1Type: _ = sb.Append("bls12_381_G1_element"); break;
            case Bls12381G2Type: _ = sb.Append("bls12_381_G2_element"); break;
            case Bls12381MlResultType: _ = sb.Append("bls12_381_mlresult"); break;
            case PlutusValueType: _ = sb.Append("value"); break;
            case ListType lt:
                _ = sb.Append("(list ");
                PrintType(sb, lt.Element);
                _ = sb.Append(')');
                break;
            case ArrayType at:
                _ = sb.Append("(array ");
                PrintType(sb, at.Element);
                _ = sb.Append(')');
                break;
            case PairType pt:
                _ = sb.Append("(pair ");
                PrintType(sb, pt.First);
                _ = sb.Append(' ');
                PrintType(sb, pt.Second);
                _ = sb.Append(')');
                break;
            default:
                break;
        }
    }

    private static void PrintPlutusData(StringBuilder sb, PlutusData data)
    {
        switch (data)
        {
            case PlutusDataInteger d:
                _ = sb.Append("I ");
                _ = sb.Append(d.Value);
                break;

            case PlutusDataByteString d:
                _ = sb.Append("B #");
                AppendHex(sb, d.Value.Span);
                break;

            case PlutusDataList d:
                _ = sb.Append("List [");
                for (int i = 0; i < d.Values.Length; i++)
                {
                    if (i > 0)
                    {
                        _ = sb.Append(", ");
                    }

                    PrintPlutusData(sb, d.Values[i]);
                }
                _ = sb.Append(']');
                break;

            case PlutusDataMap d:
                _ = sb.Append("Map [");
                for (int i = 0; i < d.Entries.Length; i++)
                {
                    if (i > 0)
                    {
                        _ = sb.Append(", ");
                    }

                    _ = sb.Append('(');
                    PrintPlutusData(sb, d.Entries[i].Key);
                    _ = sb.Append(", ");
                    PrintPlutusData(sb, d.Entries[i].Value);
                    _ = sb.Append(')');
                }
                _ = sb.Append(']');
                break;

            case PlutusDataConstr d:
                _ = sb.Append("Constr ");
                _ = sb.Append(d.Tag);
                _ = sb.Append(" [");
                for (int i = 0; i < d.Fields.Length; i++)
                {
                    if (i > 0)
                    {
                        _ = sb.Append(", ");
                    }

                    PrintPlutusData(sb, d.Fields[i]);
                }
                _ = sb.Append(']');
                break;
            default:
                break;
        }
    }

    private static void AppendHex(StringBuilder sb, ReadOnlySpan<byte> bytes)
    {
        foreach (byte b in bytes)
        {
            _ = sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static void AppendEscaped(StringBuilder sb, string s)
    {
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': _ = sb.Append("\\\\"); break;
                case '"': _ = sb.Append("\\\""); break;
                case '\n': _ = sb.Append("\\n"); break;
                case '\t': _ = sb.Append("\\t"); break;
                case '\r': _ = sb.Append("\\r"); break;
                case '\a': _ = sb.Append("\\a"); break;
                case '\b': _ = sb.Append("\\b"); break;
                case '\f': _ = sb.Append("\\f"); break;
                case '\v': _ = sb.Append("\\v"); break;
                case '\x7f': _ = sb.Append("\\DEL"); break;
                default:
                    if (c < 0x20)
                    {
                        _ = sb.Append("\\x");
                        _ = sb.Append(((int)c).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        _ = sb.Append(c);
                    }
                    break;
            }
        }
    }
}
