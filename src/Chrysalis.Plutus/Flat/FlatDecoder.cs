using System.Collections.Immutable;
using System.Numerics;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Flat;

/// <summary>
/// Decodes Flat-encoded bytes into a <see cref="Program{TBinder}"/> with DeBruijn indices.
/// Implements spec Appendix C.3.
/// </summary>
public static class FlatDecoder
{
    /// <summary>
    /// Decode a Flat-encoded byte sequence into a UPLC program.
    /// </summary>
    public static Program<DeBruijn> DecodeProgram(ReadOnlyMemory<byte> data)
    {
        BitReader reader = new(data);

        int major = DecodeNatural(reader);
        int minor = DecodeNatural(reader);
        int patch = DecodeNatural(reader);

        Term<DeBruijn> term = DecodeTerm(reader);
        reader.SkipPadding();

        return new Program<DeBruijn>(new Types.Version(major, minor, patch), term);
    }

    private static Term<DeBruijn> DecodeTerm(BitReader reader)
    {
        int tag = reader.PopBits(4);

        return tag switch
        {
            0 => DecodeVar(reader),
            1 => DecodeDelay(reader),
            2 => DecodeLambda(reader),
            3 => DecodeApply(reader),
            4 => DecodeConst(reader),
            5 => DecodeForce(reader),
            6 => new ErrorTerm<DeBruijn>(),
            7 => DecodeBuiltin(reader),
            8 => DecodeConstr(reader),
            9 => DecodeCase(reader),
            _ => throw new InvalidOperationException($"Flat: unknown term tag {tag}.")
        };
    }

    private static VarTerm<DeBruijn> DecodeVar(BitReader reader)
    {
        int index = DecodeNatural(reader);
        return new VarTerm<DeBruijn>(new DeBruijn(index));
    }

    private static DelayTerm<DeBruijn> DecodeDelay(BitReader reader) => new(DecodeTerm(reader));

    private static LambdaTerm<DeBruijn> DecodeLambda(BitReader reader) => new(new DeBruijn(0), DecodeTerm(reader));

    private static ApplyTerm<DeBruijn> DecodeApply(BitReader reader)
    {
        Term<DeBruijn> function = DecodeTerm(reader);
        Term<DeBruijn> argument = DecodeTerm(reader);
        return new ApplyTerm<DeBruijn>(function, argument);
    }

    private static ConstTerm<DeBruijn> DecodeConst(BitReader reader)
    {
        ConstantType type = DecodeConstantType(reader);
        Constant value = DecodeConstantValue(reader, type);
        return new ConstTerm<DeBruijn>(value);
    }

    private static ForceTerm<DeBruijn> DecodeForce(BitReader reader) => new(DecodeTerm(reader));

    private static BuiltinTerm<DeBruijn> DecodeBuiltin(BitReader reader) => new((DefaultFunction)reader.PopBits(7));

    private static ConstrTerm<DeBruijn> DecodeConstr(BitReader reader)
    {
        ulong tag = DecodeWord(reader);
        ImmutableArray<Term<DeBruijn>> fields = DecodeTermList(reader);
        return new ConstrTerm<DeBruijn>(tag, fields);
    }

    private static CaseTerm<DeBruijn> DecodeCase(BitReader reader)
    {
        Term<DeBruijn> scrutinee = DecodeTerm(reader);
        ImmutableArray<Term<DeBruijn>> branches = DecodeTermList(reader);
        return new CaseTerm<DeBruijn>(scrutinee, branches);
    }

    private static ImmutableArray<Term<DeBruijn>> DecodeTermList(BitReader reader)
    {
        // Most term lists are small (1-5 items). Use a list and convert.
        List<Term<DeBruijn>>? list = null;
        while (reader.PopBit() == 1)
        {
            list ??= [];
            list.Add(DecodeTerm(reader));
        }
        return list is null ? [] : [.. list];
    }

    // --- Type decoding (spec C.3.3) ---

    private static ConstantType DecodeConstantType(BitReader reader)
    {
        // Stack-allocate tag buffer for common cases (most types have 1-4 tags)
        Span<int> tagBuf = stackalloc int[8];
        int tagCount = 0;

        while (reader.PopBit() == 1)
        {
            if (tagCount >= tagBuf.Length)
            {
                // Rare: more than 8 tags, fall back to heap
                int[] heapBuf = new int[tagCount * 2];
                tagBuf[..tagCount].CopyTo(heapBuf);
                tagBuf = heapBuf;
            }
            tagBuf[tagCount++] = reader.PopBits(4);
        }

        int index = 0;
        return ParseType(tagBuf[..tagCount], ref index);
    }

    private static ConstantType ParseType(ReadOnlySpan<int> tags, ref int index)
    {
        if (index >= tags.Length)
        {
            throw new InvalidOperationException("Flat: unexpected end of type tag list.");
        }

        int tag = tags[index++];
        return tag switch
        {
            0 => ConstantType.PlutusInteger,
            1 => ConstantType.PlutusByteString,
            2 => ConstantType.PlutusText,
            3 => ConstantType.PlutusUnit,
            4 => ConstantType.PlutusBool,
            5 => new ListType(ParseType(tags, ref index)),
            6 => new PairType(ParseType(tags, ref index), ParseType(tags, ref index)),
            7 => ParseTypeApplication(tags, ref index),
            8 => ConstantType.PlutusData,
            9 => ConstantType.Bls12381G1Element,
            10 => ConstantType.Bls12381G2Element,
            11 => ConstantType.Bls12381MlResult,
            12 => new ArrayType(ParseType(tags, ref index)),
            _ => throw new InvalidOperationException($"Flat: unknown type tag {tag}.")
        };
    }

    private static ConstantType ParseTypeApplication(ReadOnlySpan<int> tags, ref int index)
    {
        if (index >= tags.Length)
        {
            throw new InvalidOperationException("Flat: unexpected end in type application.");
        }

        int operatorTag = tags[index++];
        return operatorTag switch
        {
            5 => new ListType(ParseType(tags, ref index)),
            6 => new PairType(ParseType(tags, ref index), ParseType(tags, ref index)),
            7 => ParseTypeApplication(tags, ref index), // curried application (e.g. Pair encoded as Apply(Apply(Pair, A), B))
            12 => new ArrayType(ParseType(tags, ref index)),
            _ => throw new InvalidOperationException($"Flat: unsupported type application operator {operatorTag}.")
        };
    }

    // --- Constant value decoding (spec C.3.4) ---

    private static Constant DecodeConstantValue(BitReader reader, ConstantType type) => type switch
    {
        IntegerType => new IntegerConstant(DecodeInteger(reader)),
        ByteStringType => new ByteStringConstant(DecodeByteString(reader)),
        StringType => new StringConstant(DecodeString(reader)),
        BoolType => reader.PopBit() == 1 ? BoolConstant.True : BoolConstant.False,
        UnitType => UnitConstant.Instance,
        DataType => new DataConstant(DecodeData(reader)),
        ListType list => DecodeList(reader, list.Element),
        PairType pair => DecodePair(reader, pair),
        ArrayType arr => DecodeArray(reader, arr.Element),
        Bls12381G1Type => new Bls12381G1Constant(DecodeByteString(reader)),
        Bls12381G2Type => new Bls12381G2Constant(DecodeByteString(reader)),
        Bls12381MlResultType => new Bls12381MlResultConstant(DecodeByteString(reader)),
        _ => throw new InvalidOperationException($"Flat: unknown constant type {type}.")
    };

    private static ListConstant DecodeList(BitReader reader, ConstantType elementType)
    {
        ImmutableArray<Constant>.Builder values = ImmutableArray.CreateBuilder<Constant>();
        while (reader.PopBit() == 1)
        {
            values.Add(DecodeConstantValue(reader, elementType));
        }
        return new ListConstant(elementType, values.ToImmutable());
    }

    private static PairConstant DecodePair(BitReader reader, PairType pairType)
    {
        Constant first = DecodeConstantValue(reader, pairType.First);
        Constant second = DecodeConstantValue(reader, pairType.Second);
        return new PairConstant(pairType.First, pairType.Second, first, second);
    }

    private static ArrayConstant DecodeArray(BitReader reader, ConstantType elementType)
    {
        ImmutableArray<Constant>.Builder values = ImmutableArray.CreateBuilder<Constant>();
        while (reader.PopBit() == 1)
        {
            values.Add(DecodeConstantValue(reader, elementType));
        }
        return new ArrayConstant(elementType, values.ToImmutable());
    }

    // --- Primitive decoders ---

    private static int DecodeNatural(BitReader reader)
    {
        // Read continuation bit + 7 data bits as single 8-bit read
        int byte8 = reader.PopBits(8);
        if ((byte8 >> 7) == 0)
        {
            return byte8;
        }

        int result = byte8 & 0x7F;
        int shift = 7;
        do
        {
            byte8 = reader.PopBits(8);
            result |= (byte8 & 0x7F) << shift;
            shift += 7;
        } while ((byte8 >> 7) != 0);

        return result;
    }

    private static ulong DecodeWord(BitReader reader)
    {
        int byte8 = reader.PopBits(8);
        if ((byte8 >> 7) == 0)
        {
            return (ulong)byte8;
        }

        ulong result = (ulong)(byte8 & 0x7F);
        int shift = 7;
        do
        {
            byte8 = reader.PopBits(8);
            result |= (ulong)(byte8 & 0x7F) << shift;
            shift += 7;
        } while ((byte8 >> 7) != 0);

        return result;
    }

    private static BigInteger DecodeInteger(BitReader reader)
    {
        // Fast path: decode natural + zigzag as long (most integers fit in 56 bits)
        long small = 0;
        int shift = 0;

        while (shift < 56)
        {
            int byte8 = reader.PopBits(8);
            small |= (long)(byte8 & 0x7F) << shift;
            shift += 7;
            if ((byte8 >> 7) == 0)
            {
                // Zigzag decode on long — avoids BigInteger division
                return (small & 1) == 0 ? new BigInteger(small >> 1) : new BigInteger(-(small >> 1) - 1);
            }
        }

        // Slow path: overflow to BigInteger
        BigInteger result = new(small);
        bool cont;
        do
        {
            int byte8 = reader.PopBits(8);
            result |= (BigInteger)(byte8 & 0x7F) << shift;
            shift += 7;
            cont = (byte8 >> 7) != 0;
        } while (cont);

        return result % 2 == 0 ? result / 2 : -(result + 1) / 2;
    }

    private static ReadOnlyMemory<byte> DecodeByteString(BitReader reader)
    {
        reader.SkipPadding();

        // Fast path: single chunk (common case for small bytestrings)
        byte firstLen = reader.PopByte();
        if (firstLen == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        ReadOnlyMemory<byte> firstChunk = reader.TakeBytes(firstLen);
        byte nextLen = reader.PopByte();
        if (nextLen == 0)
        {
            return firstChunk;
        }

        // Multi-chunk: concatenate into byte array
        int totalLen = firstLen + nextLen;
        byte[] buffer = new byte[totalLen];
        firstChunk.Span.CopyTo(buffer);
        int pos = firstLen;

        do
        {
            if (pos + nextLen > buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
            reader.TakeBytes(nextLen).Span.CopyTo(buffer.AsSpan(pos));
            pos += nextLen;
            nextLen = reader.PopByte();
        } while (nextLen != 0);

        return new ReadOnlyMemory<byte>(buffer, 0, pos);
    }

    private static string DecodeString(BitReader reader)
    {
        ReadOnlyMemory<byte> bytes = DecodeByteString(reader);
        return System.Text.Encoding.UTF8.GetString(bytes.Span);
    }

    private static PlutusData DecodeData(BitReader reader)
    {
        ReadOnlyMemory<byte> cborBytes = DecodeByteString(reader);
        return Cbor.CborReader.DecodePlutusData(cborBytes);
    }
}
