// Contains code adapted from Blaze (Apache-2.0); changed for Chrysalis. See THIRD-PARTY-NOTICES.md.
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Flat;

internal readonly record struct FlatRules(UInt128 Builtins, bool Sop, bool Limits);

/// <summary>
/// Decodes Flat-encoded bytes into a <see cref="Program{TBinder}"/> with DeBruijn indices.
/// Implements spec Appendix C.3.
/// </summary>
public static class FlatDecoder
{
    // PlutusLedgerApi/Common/Versions.hs:389-399
    private const int MaxTypeTags = 32;
    private const int MaxConstrFields = 1024;
    private static readonly UInt128 KnownBuiltins = BuildKnownBuiltins();

    // Keep the wrapper in the caller so the JIT can elide Program when only Term is used.
    /// <summary>
    /// Decode a Flat-encoded byte sequence into a UPLC program.
    /// </summary>
    /// <remarks>Omits the protocol version's tag and field limits, as upstream's plain decoder does; embedded data is still checked.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Program<DeBruijn> DecodeProgram(ReadOnlyMemory<byte> data)
    {
        Term<DeBruijn> term = DecodeProgramCore(data, KnownBuiltins, limits: false, out Types.Version version);
        return new(version, term);
    }

    internal static Term<DeBruijn> DecodeProgramCore(ReadOnlyMemory<byte> data, UInt128 builtins, bool limits, out Types.Version version)
    {
        BitReader reader = new(data);

        int major = DecodeNatural(reader);
        int minor = DecodeNatural(reader);
        int patch = DecodeNatural(reader);

        version = new(major, minor, patch);
        FlatRules rules = new(builtins, version.SupportsConstrAndCase, limits);
        Term<DeBruijn> term = DecodeTerm(reader, in rules);
        reader.SkipPadding();

        return term;
    }

    private enum Open : byte { Delay, Lambda, Force, Apply, Constr, Case }

    // An open term; its finished children sit on the term stack from Start.
    private readonly record struct Pending(Open Kind, int Start, ulong Tag);

    // Iterative and depth first: no term nesting depth can overflow the stack (the node has no depth limit).
    private static Term<DeBruijn> DecodeTerm(BitReader reader, in FlatRules rules)
    {
        PooledStack<Pending> open = new(16);
        PooledStack<Term<DeBruijn>> terms = new(16);
        try
        {
            while (true)
            {
                int tag = reader.PopBits(4);
                Term<DeBruijn>? leaf;
                switch (tag)
                {
                    case 0:
                        leaf = DecodeVar(reader);
                        break;
                    case 1:
                        open.Push(new(Open.Delay, terms.Count, 0));
                        continue;
                    case 2:
                        open.Push(new(Open.Lambda, terms.Count, 0));
                        continue;
                    case 3:
                        open.Push(new(Open.Apply, terms.Count, 0));
                        continue;
                    case 4:
                        leaf = DecodeConst(reader, in rules);
                        break;
                    case 5:
                        open.Push(new(Open.Force, terms.Count, 0));
                        continue;
                    case 6:
                        leaf = new ErrorTerm<DeBruijn>();
                        break;
                    case 7:
                        leaf = DecodeBuiltin(reader, in rules);
                        break;
                    case 8:
                        leaf = OpenConstr(reader, in rules, ref open, terms.Count);
                        if (leaf is null)
                        {
                            continue;
                        }

                        break;
                    case 9:
                        OpenCase(in rules, ref open, terms.Count);
                        continue;
                    default:
                        throw new InvalidOperationException($"Flat: unknown term tag {tag}.");
                }

                if (Close(reader, in rules, ref open, ref terms, leaf) is { } finished)
                {
                    return finished;
                }
            }
        }
        finally
        {
            terms.Dispose();
            open.Dispose();
        }
    }

    private static Term<DeBruijn>? OpenConstr(BitReader reader, in FlatRules rules, ref PooledStack<Pending> open, int start)
    {
        RequireSop(in rules, "constr");
        ulong tag = DecodeWord64(reader);
        if (reader.PopBit() == 0)
        {
            return new ConstrTerm<DeBruijn>(tag, []);
        }

        open.Push(new(Open.Constr, start, tag));
        return null;
    }

    private static void OpenCase(in FlatRules rules, ref PooledStack<Pending> open, int start)
    {
        RequireSop(in rules, "case");
        open.Push(new(Open.Case, start, 0));
    }

    private static Term<DeBruijn>? Close(
        BitReader reader,
        in FlatRules rules,
        ref PooledStack<Pending> open,
        ref PooledStack<Term<DeBruijn>> terms,
        Term<DeBruijn> term)
    {
        while (open.Count > 0)
        {
            Pending frame = open.Top;
            switch (frame.Kind)
            {
                case Open.Delay:
                    _ = open.Pop();
                    term = new DelayTerm<DeBruijn>(term);
                    break;
                case Open.Lambda:
                    _ = open.Pop();
                    term = new LambdaTerm<DeBruijn>(new DeBruijn(0), term);
                    break;
                case Open.Force:
                    _ = open.Pop();
                    term = new ForceTerm<DeBruijn>(term);
                    break;
                case Open.Apply:
                    if (terms.Count == frame.Start)
                    {
                        terms.Push(term);
                        return null;
                    }

                    _ = open.Pop();
                    term = new ApplyTerm<DeBruijn>(terms.Pop(), term);
                    break;
                case Open.Constr:
                    terms.Push(term);
                    if (reader.PopBit() == 1)
                    {
                        return null;
                    }

                    _ = open.Pop();
                    ImmutableArray<Term<DeBruijn>> fields = ImmutableArray.Create(terms.From(frame.Start));
                    terms.Truncate(frame.Start);
                    if (rules.Limits && fields.Length > MaxConstrFields)
                    {
                        throw new InvalidOperationException($"Flat: constr has {fields.Length} fields (maximum is {MaxConstrFields}).");
                    }

                    term = new ConstrTerm<DeBruijn>(frame.Tag, fields);
                    break;
                case Open.Case:
                    terms.Push(term);
                    if (reader.PopBit() == 1)
                    {
                        return null;
                    }

                    _ = open.Pop();
                    ReadOnlySpan<Term<DeBruijn>> items = terms.From(frame.Start);
                    Term<DeBruijn> scrutinee = items[0];
                    ImmutableArray<Term<DeBruijn>> branches = items.Length == 1 ? [] : ImmutableArray.Create(items[1..]);
                    terms.Truncate(frame.Start);
                    term = new CaseTerm<DeBruijn>(scrutinee, branches);
                    break;
                default:
                    throw new InvalidOperationException($"Flat: unknown open frame {frame.Kind}.");
            }
        }

        return term;
    }

    private static VarTerm<DeBruijn> DecodeVar(BitReader reader)
    {
        ulong index = DecodeWord64(reader);
        return new VarTerm<DeBruijn>(new DeBruijn(index > int.MaxValue ? int.MaxValue : (int)index));
    }

    private static ConstTerm<DeBruijn> DecodeConst(BitReader reader, in FlatRules rules)
    {
        ConstantType type = DecodeConstantType(reader, rules.Limits);
        Constant value = DecodeConstantValue(reader, type);
        return new ConstTerm<DeBruijn>(value);
    }

    private static BuiltinTerm<DeBruijn> DecodeBuiltin(BitReader reader, in FlatRules rules)
    {
        int tag = reader.PopBits(7);
        UInt128 bit = UInt128.One << tag;
        if ((rules.Builtins & bit) == 0)
        {
            throw new InvalidOperationException((KnownBuiltins & bit) == 0
                ? $"Flat: unknown builtin tag {tag}."
                : $"Flat: builtin {(DefaultFunction)tag} is not available for this language and protocol version.");
        }
        return new((DefaultFunction)tag);
    }

    private static void RequireSop(in FlatRules rules, string term)
    {
        if (!rules.Sop)
        {
            throw new InvalidOperationException($"'{term}' is not allowed before version 1.1.0.");
        }
    }

    private static UInt128 BuildKnownBuiltins()
    {
        UInt128 mask = 0;
        foreach (DefaultFunction function in Enum.GetValues<DefaultFunction>())
        {
            mask |= UInt128.One << (int)function;
        }
        return mask;
    }

    // --- Type decoding (spec C.3.3) ---

    private static ConstantType DecodeConstantType(BitReader reader, bool limits)
    {
        // Stack-allocate tag buffer for common cases (most types have 1-4 tags)
        Span<int> tagBuf = stackalloc int[8];
        int tagCount = 0;

        while (reader.PopBit() == 1)
        {
            if (limits && tagCount >= MaxTypeTags)
            {
                throw new InvalidOperationException($"Flat: constant type exceeds {MaxTypeTags} tags.");
            }
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
        ConstantType type = ParseType(tagBuf[..tagCount], ref index);
        if (index != tagCount)
        {
            throw new InvalidOperationException("Flat: trailing tags in constant type.");
        }
        return type;
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
            7 => ParseTypeApplication(tags, ref index),
            8 => ConstantType.PlutusData,
            9 => ConstantType.Bls12381G1Element,
            10 => ConstantType.Bls12381G2Element,
            11 => ConstantType.Bls12381MlResult,
            13 => ConstantType.PlutusValue,
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
            12 => new ArrayType(ParseType(tags, ref index)),
            7 => ParsePairType(tags, ref index),
            _ => throw new InvalidOperationException($"Flat: unsupported type application operator {operatorTag}.")
        };
    }

    private static PairType ParsePairType(ReadOnlySpan<int> tags, ref int index)
    {
        if (index >= tags.Length || tags[index++] != 6)
        {
            throw new InvalidOperationException("Flat: unsupported curried type application.");
        }

        ConstantType first = ParseType(tags, ref index);
        ConstantType second = ParseType(tags, ref index);
        return new PairType(first, second);
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
        PlutusValueType => DecodeValue(reader),
        // plutus decodes G1, G2 and Miller-loop values with `fail` (Crypto/BLS12_381/G1.hs:86-97, G2.hs:72-83, Pairing.hs:56-63).
        Bls12381G1Type or Bls12381G2Type or Bls12381MlResultType =>
            throw new InvalidOperationException($"Flat: {type.GetType().Name} constants cannot be decoded."),
        _ => throw new InvalidOperationException($"Flat: unknown constant type {type}.")
    };

    // Value.hs:229-240: a list of (currency, list of (token, quantity)) with plutus-flat list bits, checked as plutus's buildValueWith checks it.
    private static ValueConstant DecodeValue(BitReader reader)
    {
        ImmutableArray<CurrencyEntry>.Builder entries = ImmutableArray.CreateBuilder<CurrencyEntry>();
        while (reader.PopBit() == 1)
        {
            ReadOnlyMemory<byte> currency = DecodeByteString(reader);
            ImmutableArray<TokenEntry>.Builder tokens = ImmutableArray.CreateBuilder<TokenEntry>();
            while (reader.PopBit() == 1)
            {
                ReadOnlyMemory<byte> name = DecodeByteString(reader);
                tokens.Add(new TokenEntry(name, DecodeInteger(reader)));
            }
            entries.Add(new CurrencyEntry(currency, tokens.ToImmutable()));
        }

        LedgerValue value = new(entries.ToImmutable());
        string? violation = value.Violation();
        return violation is null
            ? new ValueConstant(value)
            : throw new InvalidOperationException($"Flat: invalid value constant: {violation}.");
    }

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
            int payload = byte8 & 0x7F;
            result |= payload > (int.MaxValue >> shift) ? int.MaxValue : payload << shift;
            shift = Math.Min(shift + 7, 31);
        } while ((byte8 >> 7) != 0);

        return result;
    }

    private static ulong DecodeWord64(BitReader reader)
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
            if (shift == 63 && byte8 > 1)
            {
                throw new InvalidOperationException("Flat: unexpected extra byte in unsigned integer.");
            }
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
                Array.Resize(ref buffer, Math.Max(buffer.Length * 2, pos + nextLen));
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
        // dUTF8 fails on invalid UTF-8 (flat Decoder/Strict.hs:293-299).
        return System.Text.Unicode.Utf8.IsValid(bytes.Span)
            ? System.Text.Encoding.UTF8.GetString(bytes.Span)
            : throw new InvalidOperationException("Flat: string constant is not valid UTF-8.");
    }

    private static PlutusData DecodeData(BitReader reader)
    {
        ReadOnlyMemory<byte> cborBytes = DecodeByteString(reader);
        return Cbor.CborReader.DecodePlutusData(cborBytes);
    }
}
