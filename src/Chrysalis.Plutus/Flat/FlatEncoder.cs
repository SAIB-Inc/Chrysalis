using System.Collections.Immutable;
using System.Numerics;
using System.Text;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Flat;

/// <summary>
/// Encodes a <see cref="Program{TBinder}"/> with DeBruijn indices into Flat-encoded bytes.
/// Implements spec Appendix C.3.
/// </summary>
public static class FlatEncoder
{
    /// <summary>
    /// Encode a UPLC program into Flat-encoded bytes.
    /// </summary>
    public static byte[] EncodeProgram(Program<DeBruijn> program)
    {
        ArgumentNullException.ThrowIfNull(program);
        BitWriter writer = new();

        EncodeNatural(writer, program.Version.Major);
        EncodeNatural(writer, program.Version.Minor);
        EncodeNatural(writer, program.Version.Patch);

        EncodeTerm(writer, program.Term);
        writer.Pad();

        return writer.ToArray();
    }

    private static void EncodeTerm(BitWriter writer, Term<DeBruijn> term)
    {
        switch (term)
        {
            case VarTerm<DeBruijn> v:
                writer.PushBits(0, 4);
                EncodeNatural(writer, v.Name.Index);
                break;

            case DelayTerm<DeBruijn> d:
                writer.PushBits(1, 4);
                EncodeTerm(writer, d.Body);
                break;

            case LambdaTerm<DeBruijn> l:
                writer.PushBits(2, 4);
                EncodeTerm(writer, l.Body);
                break;

            case ApplyTerm<DeBruijn> a:
                writer.PushBits(3, 4);
                EncodeTerm(writer, a.Function);
                EncodeTerm(writer, a.Argument);
                break;

            case ConstTerm<DeBruijn> c:
                writer.PushBits(4, 4);
                EncodeConstantType(writer, c.Value.ConstantType);
                EncodeConstantValue(writer, c.Value);
                break;

            case ForceTerm<DeBruijn> f:
                writer.PushBits(5, 4);
                EncodeTerm(writer, f.Body);
                break;

            case ErrorTerm<DeBruijn>:
                writer.PushBits(6, 4);
                break;

            case BuiltinTerm<DeBruijn> b:
                writer.PushBits(7, 4);
                writer.PushBits((int)b.Function, 7);
                break;

            case ConstrTerm<DeBruijn> constr:
                writer.PushBits(8, 4);
                EncodeNaturalCore(writer, constr.Tag);
                EncodeTermList(writer, constr.Fields);
                break;

            case CaseTerm<DeBruijn> cs:
                writer.PushBits(9, 4);
                EncodeTerm(writer, cs.Scrutinee);
                EncodeTermList(writer, cs.Branches);
                break;

            default:
                throw new InvalidOperationException($"Flat: cannot encode term {term.GetType().Name}.");
        }
    }

    private static void EncodeTermList(BitWriter writer, ImmutableArray<Term<DeBruijn>> terms)
    {
        foreach (Term<DeBruijn> term in terms)
        {
            writer.PushBit(1);
            EncodeTerm(writer, term);
        }
        writer.PushBit(0);
    }

    // --- Type encoding (spec C.3.3) ---

    private static void EncodeConstantType(BitWriter writer, ConstantType type)
    {
        EncodeTypeTagList(writer, type);
        writer.PushBit(0);
    }

    private static void EncodeTypeTagList(BitWriter writer, ConstantType type)
    {
        switch (type)
        {
            case IntegerType:
                writer.PushBit(1); writer.PushBits(0, 4);
                break;
            case ByteStringType:
                writer.PushBit(1); writer.PushBits(1, 4);
                break;
            case StringType:
                writer.PushBit(1); writer.PushBits(2, 4);
                break;
            case UnitType:
                writer.PushBit(1); writer.PushBits(3, 4);
                break;
            case BoolType:
                writer.PushBit(1); writer.PushBits(4, 4);
                break;
            case ListType list:
                writer.PushBit(1); writer.PushBits(7, 4);
                writer.PushBit(1); writer.PushBits(5, 4);
                EncodeTypeTagList(writer, list.Element);
                break;
            case PairType pair:
                writer.PushBit(1); writer.PushBits(7, 4);
                writer.PushBit(1); writer.PushBits(7, 4);
                writer.PushBit(1); writer.PushBits(6, 4);
                EncodeTypeTagList(writer, pair.First);
                EncodeTypeTagList(writer, pair.Second);
                break;
            case DataType:
                writer.PushBit(1); writer.PushBits(8, 4);
                break;
            case Bls12381G1Type:
                writer.PushBit(1); writer.PushBits(9, 4);
                break;
            case Bls12381G2Type:
                writer.PushBit(1); writer.PushBits(10, 4);
                break;
            case Bls12381MlResultType:
                writer.PushBit(1); writer.PushBits(11, 4);
                break;
            case ArrayType arr:
                writer.PushBit(1); writer.PushBits(7, 4);
                writer.PushBit(1); writer.PushBits(12, 4);
                EncodeTypeTagList(writer, arr.Element);
                break;
            default:
                throw new InvalidOperationException($"Flat: cannot encode type {type.GetType().Name}.");
        }
    }

    // --- Constant value encoding (spec C.3.4) ---

    private static void EncodeConstantValue(BitWriter writer, Constant value)
    {
        switch (value)
        {
            case IntegerConstant i:
                EncodeInteger(writer, i.Value);
                break;
            case ByteStringConstant bs:
                EncodeByteString(writer, bs.Value.Span);
                break;
            case StringConstant s:
                EncodeByteString(writer, Encoding.UTF8.GetBytes(s.Value));
                break;
            case BoolConstant b:
                writer.PushBit(b.Value ? 1 : 0);
                break;
            case UnitConstant:
                break;
            case DataConstant d:
                byte[] cborBytes = Cbor.CborWriter.EncodePlutusData(d.Value);
                EncodeByteString(writer, cborBytes);
                break;
            case ListConstant list:
                for (int li = 0; li < list.Count; li++)
                {
                    writer.PushBit(1);
                    EncodeConstantValue(writer, list.ElementAt(li));
                }
                writer.PushBit(0);
                break;
            case PairConstant pair:
                EncodeConstantValue(writer, pair.First);
                EncodeConstantValue(writer, pair.Second);
                break;
            case ArrayConstant arr:
                foreach (Constant item in arr.Values)
                {
                    writer.PushBit(1);
                    EncodeConstantValue(writer, item);
                }
                writer.PushBit(0);
                break;
            case Bls12381G1Constant g1:
                EncodeByteString(writer, g1.Value.Span);
                break;
            case Bls12381G2Constant g2:
                EncodeByteString(writer, g2.Value.Span);
                break;
            case Bls12381MlResultConstant ml:
                EncodeByteString(writer, ml.Value.Span);
                break;
            default:
                throw new InvalidOperationException($"Flat: cannot encode constant {value.GetType().Name}.");
        }
    }

    // --- Primitive encoders ---

    private static void EncodeNatural(BitWriter writer, int value) => EncodeNaturalCore(writer, (ulong)value);

    private static void EncodeNaturalCore(BitWriter writer, BigInteger value)
    {
        if (value < 128)
        {
            writer.PushBit(0);
            writer.PushBits((int)value, 7);
            return;
        }

        writer.PushBit(1);
        writer.PushBits((int)(value & 0x7F), 7);
        EncodeNaturalCore(writer, value >> 7);
    }

    private static void EncodeInteger(BitWriter writer, BigInteger value)
    {
        BigInteger natural = value >= 0 ? value * 2 : (-value * 2) - 1;
        EncodeNaturalCore(writer, natural);
    }

    private static void EncodeByteString(BitWriter writer, ReadOnlySpan<byte> bytes)
    {
        writer.Pad();

        int offset = 0;
        while (offset < bytes.Length)
        {
            int chunkSize = Math.Min(255, bytes.Length - offset);
            writer.PushByte((byte)chunkSize);
            writer.PushBytes(bytes.Slice(offset, chunkSize));
            offset += chunkSize;
        }
        writer.PushByte(0);
    }
}
