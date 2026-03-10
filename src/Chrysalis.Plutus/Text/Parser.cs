using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using Chrysalis.Crypto.Bls12381;
using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Text;

/// <summary>
/// Recursive descent parser for UPLC text syntax.
/// Produces a <c>Program&lt;Name&gt;</c> AST from UPLC text.
/// Ported from blaze-plutus parse.ts.
/// </summary>
internal sealed class Parser
{
    private readonly Lexer _lexer;
    private Token _current;
    private Token _previous;
    private readonly Dictionary<string, int> _interned = [];
    private int _nextUnique;
    private Types.Version _version;

    internal Parser(string source)
    {
        _lexer = new Lexer(source);
        _current = _lexer.NextToken();
        _previous = _current;
    }

    private void Advance()
    {
        _previous = _current;
        _current = _lexer.NextToken();
    }

    private bool Is(TokenType type)
    {
        return _current.Type == type;
    }

    private void Expect(TokenType type)
    {
        if (_current.Type != type)
        {
            throw new ParseException(
                $"expected {type}, got {_current.Type} at position {_current.Position}");
        }
        Advance();
    }

    private Name InternName(string text)
    {
        if (_interned.TryGetValue(text, out int existing))
        {
            return new Name(text, existing);
        }
        int unique = _nextUnique++;
        _interned[text] = unique;
        return new Name(text, unique);
    }

    private bool IsBeforeV1_1_0()
    {
        return _version.Major < 2 && _version.Minor < 1;
    }

    internal Program<Name> ParseProgram()
    {
        Expect(TokenType.LParen);
        Expect(TokenType.Program);

        int[] versionParts = new int[3];
        for (int i = 0; i < 3; i++)
        {
            if (!Is(TokenType.Number))
            {
                throw new ParseException(
                    $"expected version number, got {_current.Type} at position {_current.Position}");
            }
            int n = int.Parse(_current.Value, CultureInfo.InvariantCulture);
            if (n < 0)
            {
                throw new ParseException(
                    $"invalid version number {_current.Value} at position {_current.Position}");
            }
            versionParts[i] = n;
            Advance();
            if (i < 2)
            {
                Expect(TokenType.Dot);
            }
        }

        _version = new Types.Version(versionParts[0], versionParts[1], versionParts[2]);
        Term<Name> term = ParseTerm();
        Expect(TokenType.RParen);

        return !Is(TokenType.Eof)
            ? throw new ParseException(
                $"unexpected token {_current.Type} after program at position {_current.Position}")
            : new Program<Name>(_version, term);
    }

    private Term<Name> ParseTerm()
    {
        if (Is(TokenType.Identifier))
        {
            Name name = InternName(_current.Value);
            Advance();
            return new VarTerm<Name>(name);
        }

        if (Is(TokenType.LParen))
        {
            Advance();
            return ParseParenTerm();
        }

        return Is(TokenType.LBracket)
            ? ParseApply()
            : throw new ParseException(
            $"unexpected token {_current.Type} in term at position {_current.Position}");
    }

    private Term<Name> ParseParenTerm()
    {
        if (Is(TokenType.Lam))
        {
            return ParseLambda();
        }

        if (Is(TokenType.Delay))
        {
            return ParseDelay();
        }

        if (Is(TokenType.Force))
        {
            return ParseForce();
        }

        if (Is(TokenType.Builtin))
        {
            return ParseBuiltin();
        }

        if (Is(TokenType.Con))
        {
            return ParseConstantTerm();
        }

        if (Is(TokenType.Error))
        {
            Advance();
            Expect(TokenType.RParen);
            return new ErrorTerm<Name>();
        }
        return Is(TokenType.Constr)
            ? ParseConstr()
            : Is(TokenType.Case)
            ? ParseCase()
            : throw new ParseException(
            $"unexpected token {_current.Type} in term at position {_current.Position}");
    }

    private Term<Name> ParseLambda()
    {
        Expect(TokenType.Lam);

        if (!Is(TokenType.Identifier))
        {
            throw new ParseException(
                $"expected identifier, got {_current.Type} at position {_current.Position}");
        }
        Name name = InternName(_current.Value);
        Advance();

        Term<Name> body = ParseTerm();
        Expect(TokenType.RParen);

        return new LambdaTerm<Name>(name, body);
    }

    private Term<Name> ParseDelay()
    {
        Expect(TokenType.Delay);
        Term<Name> term = ParseTerm();
        Expect(TokenType.RParen);
        return new DelayTerm<Name>(term);
    }

    private Term<Name> ParseForce()
    {
        Expect(TokenType.Force);
        Term<Name> term = ParseTerm();
        Expect(TokenType.RParen);
        return new ForceTerm<Name>(term);
    }

    private Term<Name> ParseBuiltin()
    {
        Expect(TokenType.Builtin);

        if (!Is(TokenType.Identifier))
        {
            throw new ParseException(
                $"expected builtin name, got {_current.Type} at position {_current.Position}");
        }

        string name = _current.Value;
        if (!BuiltinNames.TryParse(name, out DefaultFunction func))
        {
            throw new ParseException(
                $"unknown builtin function {name} at position {_current.Position}");
        }

        Advance();
        Expect(TokenType.RParen);

        return new BuiltinTerm<Name>(func);
    }

    private Term<Name> ParseConstr()
    {
        if (IsBeforeV1_1_0())
        {
            throw new ParseException("constr can't be used before 1.1.0");
        }

        Expect(TokenType.Constr);

        if (!Is(TokenType.Number))
        {
            throw new ParseException(
                $"expected tag number, got {_current.Type} at position {_current.Position}");
        }

        ulong tag = ParseConstrTag(_current.Value, _current.Position);
        Advance();

        ImmutableArray<Term<Name>>.Builder fields = ImmutableArray.CreateBuilder<Term<Name>>();
        while (!Is(TokenType.RParen))
        {
            fields.Add(ParseTerm());
        }

        Expect(TokenType.RParen);
        return new ConstrTerm<Name>(tag, fields.ToImmutable());
    }

    private Term<Name> ParseCase()
    {
        if (IsBeforeV1_1_0())
        {
            throw new ParseException("case can't be used before 1.1.0");
        }

        Expect(TokenType.Case);
        Term<Name> scrutinee = ParseTerm();

        ImmutableArray<Term<Name>>.Builder branches = ImmutableArray.CreateBuilder<Term<Name>>();
        while (!Is(TokenType.RParen))
        {
            branches.Add(ParseTerm());
        }

        Expect(TokenType.RParen);
        return new CaseTerm<Name>(scrutinee, branches.ToImmutable());
    }

    private Term<Name> ParseApply()
    {
        Expect(TokenType.LBracket);

        List<Term<Name>> terms = [];
        while (!Is(TokenType.RBracket))
        {
            terms.Add(ParseTerm());
        }

        if (terms.Count < 2)
        {
            throw new ParseException(
                $"application requires at least two terms, got {terms.Count} at position {_current.Position}");
        }

        Expect(TokenType.RBracket);

        // Build left-nested Apply
        Term<Name> result = terms[0];
        for (int i = 1; i < terms.Count; i++)
        {
            result = new ApplyTerm<Name>(result, terms[i]);
        }

        return result;
    }

    private Term<Name> ParseConstantTerm()
    {
        Expect(TokenType.Con);

        ConstantType typeSpec = ParseTypeSpec();

        // Top-level data constants have extra parens: (con data (I 42))
        if (typeSpec is DataType)
        {
            Expect(TokenType.LParen);
            PlutusData data = ParsePlutusData();
            Expect(TokenType.RParen);
            Expect(TokenType.RParen);
            return new ConstTerm<Name>(new DataConstant(data));
        }

        Constant value = ParseConstantValue(typeSpec);
        Expect(TokenType.RParen);
        return new ConstTerm<Name>(value);
    }

    private ConstantType ParseTypeSpec()
    {
        // Check for bare list/pair/array (requires parens)
        if (Is(TokenType.List) || Is(TokenType.Pair) || Is(TokenType.Array))
        {
            throw new ParseException(
                $"expected left parenthesis for {_current.Type} type at position {_current.Position}");
        }

        if (Is(TokenType.LParen))
        {
            Advance();
            ConstantType ts = ParseInnerTypeSpec();
            if (!Is(TokenType.RParen))
            {
                throw new ParseException(
                    $"expected right parenthesis after type spec, got {_current.Type} at position {_current.Position}");
            }
            Advance();
            return ts;
        }

        return ParseInnerTypeSpec();
    }

    private ConstantType ParseInnerTypeSpec()
    {
        if (Is(TokenType.Identifier))
        {
            string name = _current.Value;
            Advance();
            return name switch
            {
                "integer" => ConstantType.PlutusInteger,
                "bytestring" => ConstantType.PlutusByteString,
                "string" => ConstantType.PlutusText,
                "unit" => ConstantType.PlutusUnit,
                "bool" => ConstantType.PlutusBool,
                "data" => ConstantType.PlutusData,
                "bls12_381_G1_element" => ConstantType.Bls12381G1Element,
                "bls12_381_G2_element" => ConstantType.Bls12381G2Element,
                "bls12_381_mlresult" => ConstantType.Bls12381MlResult,
                "value" => ConstantType.PlutusValue,
                _ => throw new ParseException($"unknown type {name} at position {_previous.Position}"),
            };
        }

        if (Is(TokenType.List))
        {
            Advance();
            ConstantType elemType = ParseTypeSpec();
            return new ListType(elemType);
        }

        if (Is(TokenType.Array))
        {
            Advance();
            ConstantType elemType = ParseTypeSpec();
            return new ArrayType(elemType);
        }

        if (Is(TokenType.Pair))
        {
            Advance();
            ConstantType first = ParseTypeSpec();
            ConstantType second = ParseTypeSpec();
            return new PairType(first, second);
        }

        throw new ParseException(
            $"expected type identifier, got {_current.Type} at position {_current.Position}");
    }

    private Constant ParseConstantValue(ConstantType typeSpec)
    {
        switch (typeSpec)
        {
            case IntegerType:
                {
                    if (!Is(TokenType.Number))
                    {
                        throw new ParseException(
                            $"expected integer value, got {_current.Type} at position {_current.Position}");
                    }
                    BigInteger value = BigInteger.Parse(_current.Value, CultureInfo.InvariantCulture);
                    Advance();
                    return new IntegerConstant(value);
                }

            case ByteStringType:
                {
                    if (!Is(TokenType.ByteString))
                    {
                        throw new ParseException(
                            $"expected bytestring value, got {_current.Type} at position {_current.Position}");
                    }
                    byte[] bytes = HexToBytes(_current.Value);
                    Advance();
                    return new ByteStringConstant(bytes);
                }

            case StringType:
                {
                    if (!Is(TokenType.String))
                    {
                        throw new ParseException(
                            $"expected string value, got {_current.Type} at position {_current.Position}");
                    }
                    string s = _current.Value;
                    Advance();
                    return new StringConstant(s);
                }

            case BoolType:
                {
                    if (Is(TokenType.True))
                    {
                        Advance();
                        return new BoolConstant(true);
                    }
                    if (Is(TokenType.False))
                    {
                        Advance();
                        return new BoolConstant(false);
                    }
                    throw new ParseException(
                        $"expected bool value, got {_current.Type} at position {_current.Position}");
                }

            case UnitType:
                {
                    if (!Is(TokenType.Unit))
                    {
                        throw new ParseException(
                            $"expected unit value, got {_current.Type} at position {_current.Position}");
                    }
                    Advance();
                    return UnitConstant.Instance;
                }

            case DataType:
                {
                    PlutusData data = ParsePlutusData();
                    return new DataConstant(data);
                }

            case ListType listType:
                {
                    Expect(TokenType.LBracket);
                    ImmutableArray<Constant>.Builder values = ImmutableArray.CreateBuilder<Constant>();
                    while (!Is(TokenType.RBracket))
                    {
                        values.Add(ParseConstantValue(listType.Element));
                        if (!Is(TokenType.RBracket))
                        {
                            Expect(TokenType.Comma);
                        }
                    }
                    Expect(TokenType.RBracket);
                    return new ListConstant(listType.Element, values.ToImmutable());
                }

            case ArrayType arrayType:
                {
                    Expect(TokenType.LBracket);
                    ImmutableArray<Constant>.Builder values = ImmutableArray.CreateBuilder<Constant>();
                    while (!Is(TokenType.RBracket))
                    {
                        values.Add(ParseConstantValue(arrayType.Element));
                        if (!Is(TokenType.RBracket))
                        {
                            Expect(TokenType.Comma);
                        }
                    }
                    Expect(TokenType.RBracket);
                    return new ArrayConstant(arrayType.Element, values.ToImmutable());
                }

            case PairType pairType:
                {
                    Expect(TokenType.LParen);
                    Constant first = ParseConstantValue(pairType.First);
                    Expect(TokenType.Comma);
                    Constant second = ParseConstantValue(pairType.Second);
                    Expect(TokenType.RParen);
                    return new PairConstant(pairType.First, pairType.Second, first, second);
                }

            case Bls12381G1Type:
                {
                    if (!Is(TokenType.Point))
                    {
                        throw new ParseException(
                            $"expected point value, got {_current.Type} at position {_current.Position}");
                    }
                    int pos = _current.Position;
                    try
                    {
                        byte[] bytes = HexToBytes(_current.Value);
                        Advance();
                        if (bytes.Length != 48)
                        {
                            throw new ParseException(
                                $"bls12_381_G1_element must be 48 bytes, got {bytes.Length}");
                        }

                        PointG1 point = PointG1.Uncompress(bytes);
                        return new Bls12381G1Constant(point.Compress());
                    }
                    catch (ParseException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new ParseException(
                            $"invalid bls12_381_G1_element at position {pos}: {ex.Message}");
                    }
                }

            case Bls12381G2Type:
                {
                    if (!Is(TokenType.Point))
                    {
                        throw new ParseException(
                            $"expected point value, got {_current.Type} at position {_current.Position}");
                    }
                    int pos = _current.Position;
                    try
                    {
                        byte[] bytes = HexToBytes(_current.Value);
                        Advance();
                        if (bytes.Length != 96)
                        {
                            throw new ParseException(
                                $"bls12_381_G2_element must be 96 bytes, got {bytes.Length}");
                        }

                        PointG2 point = PointG2.Uncompress(bytes);
                        return new Bls12381G2Constant(point.Compress());
                    }
                    catch (ParseException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new ParseException(
                            $"invalid bls12_381_G2_element at position {pos}: {ex.Message}");
                    }
                }

            case Bls12381MlResultType:
                {
                    if (!Is(TokenType.Point))
                    {
                        throw new ParseException(
                            $"expected point value, got {_current.Type} at position {_current.Position}");
                    }
                    byte[] bytes = HexToBytes(_current.Value);
                    Advance();
                    return new Bls12381MlResultConstant(bytes);
                }

            case PlutusValueType:
                return ParseValueConstant();

            default:
                throw new ParseException($"unsupported constant type {typeSpec}");
        }
    }

    private static readonly BigInteger ValueMax = (BigInteger.One << 127) - 1;
    private static readonly BigInteger ValueMin = -(BigInteger.One << 127);

    /// <summary>
    /// Parses a value constant: [(#hex, [(#hex, qty), ...]), ...]
    /// with canonicalization (merge duplicates, sort, remove zeros).
    /// Ported from blaze-plutus parse.ts parseValueConstant.
    /// </summary>
    private ValueConstant ParseValueConstant()
    {
        Expect(TokenType.LBracket);

        // Parse raw entries
        List<(byte[] Currency, List<(byte[] Name, BigInteger Qty)> Tokens)> rawEntries = [];

        while (!Is(TokenType.RBracket))
        {
            Expect(TokenType.LParen);

            if (!Is(TokenType.ByteString))
            {
                throw new ParseException(
                    $"expected bytestring key for value, got {_current.Type} at position {_current.Position}");
            }
            byte[] currency = HexToBytes(_current.Value);
            if (currency.Length > 32)
            {
                throw new ParseException(
                    $"policy key too long ({currency.Length} bytes) at position {_current.Position}");
            }
            Advance();
            Expect(TokenType.Comma);

            Expect(TokenType.LBracket);
            List<(byte[] Name, BigInteger Qty)> tokens = [];

            while (!Is(TokenType.RBracket))
            {
                Expect(TokenType.LParen);

                if (!Is(TokenType.ByteString))
                {
                    throw new ParseException(
                        $"expected bytestring in inner pair, got {_current.Type} at position {_current.Position}");
                }
                byte[] tokenName = HexToBytes(_current.Value);
                if (tokenName.Length > 32)
                {
                    throw new ParseException(
                        $"token key too long ({tokenName.Length} bytes) at position {_current.Position}");
                }
                Advance();
                Expect(TokenType.Comma);

                if (!Is(TokenType.Number))
                {
                    throw new ParseException(
                        $"expected integer in inner pair, got {_current.Type} at position {_current.Position}");
                }
                BigInteger qty = BigInteger.Parse(_current.Value, CultureInfo.InvariantCulture);
                if (qty > ValueMax || qty < ValueMin)
                {
                    throw new ParseException(
                        $"integer in value token out of range {_current.Value} at position {_current.Position}");
                }
                Advance();
                Expect(TokenType.RParen);

                tokens.Add((tokenName, qty));

                if (!Is(TokenType.RBracket))
                {
                    Expect(TokenType.Comma);
                }
            }
            Expect(TokenType.RBracket);
            Expect(TokenType.RParen);

            rawEntries.Add((currency, tokens));

            if (!Is(TokenType.RBracket))
            {
                Expect(TokenType.Comma);
            }
        }
        Expect(TokenType.RBracket);

        // Canonicalize: merge duplicates, sum quantities, sort, remove zeros
        Dictionary<string, Dictionary<string, (byte[] Key, BigInteger Sum)>> merged = [];

        foreach ((byte[] currency, List<(byte[] Name, BigInteger Qty)> tokens) in rawEntries)
        {
            string polKey = Convert.ToHexString(currency);
            if (!merged.TryGetValue(polKey, out Dictionary<string, (byte[], BigInteger)>? tokenMap))
            {
                tokenMap = [];
                merged[polKey] = tokenMap;
            }
            foreach ((byte[] name, BigInteger qty) in tokens)
            {
                string tokKey = Convert.ToHexString(name);
                if (tokenMap.TryGetValue(tokKey, out (byte[] Key, BigInteger Sum) existing))
                {
                    tokenMap[tokKey] = (existing.Key, existing.Sum + qty);
                }
                else
                {
                    tokenMap[tokKey] = (name, qty);
                }
            }
        }

        // Validate summed amounts
        foreach (KeyValuePair<string, Dictionary<string, (byte[], BigInteger)>> polEntry in merged)
        {
            foreach (KeyValuePair<string, (byte[] Key, BigInteger Sum)> tokEntry in polEntry.Value)
            {
                if (tokEntry.Value.Sum > ValueMax || tokEntry.Value.Sum < ValueMin)
                {
                    throw new ParseException(
                        $"summed token amount out of range {tokEntry.Value.Sum}");
                }
            }
        }

        // Sort and build result, removing zero-quantity entries
        List<string> sortedPolicies = [.. merged.Keys];
        sortedPolicies.Sort(StringComparer.Ordinal);

        ImmutableArray<CurrencyEntry>.Builder entries =
            ImmutableArray.CreateBuilder<CurrencyEntry>();

        foreach (string polKey in sortedPolicies)
        {
            Dictionary<string, (byte[] Key, BigInteger Sum)> tokenMap = merged[polKey];
            List<string> sortedTokenKeys = [.. tokenMap.Keys];
            sortedTokenKeys.Sort(StringComparer.Ordinal);

            ImmutableArray<TokenEntry>.Builder nonZeroTokens =
                ImmutableArray.CreateBuilder<TokenEntry>();
            foreach (string tokKey in sortedTokenKeys)
            {
                (byte[] key, BigInteger sum) = tokenMap[tokKey];
                if (sum != 0)
                {
                    nonZeroTokens.Add(new TokenEntry(key, sum));
                }
            }
            if (nonZeroTokens.Count > 0)
            {
                entries.Add(new CurrencyEntry(
                    HexToBytes(polKey),
                    nonZeroTokens.ToImmutable()));
            }
        }

        return new ValueConstant(new LedgerValue(entries.ToImmutable()));
    }

    private PlutusData ParsePlutusData()
    {
        if (Is(TokenType.DataI))
        {
            Advance();
            if (!Is(TokenType.Number))
            {
                throw new ParseException(
                    $"expected integer value for I, got {_current.Type} at position {_current.Position}");
            }
            BigInteger value = BigInteger.Parse(_current.Value, CultureInfo.InvariantCulture);
            Advance();
            return new PlutusDataInteger(value);
        }

        if (Is(TokenType.DataB))
        {
            Advance();
            if (!Is(TokenType.ByteString))
            {
                throw new ParseException(
                    $"expected bytestring value for B, got {_current.Type} at position {_current.Position}");
            }
            byte[] bytes = HexToBytes(_current.Value);
            Advance();
            return new PlutusDataByteString(bytes);
        }

        if (Is(TokenType.DataList))
        {
            Advance();
            Expect(TokenType.LBracket);
            ImmutableArray<PlutusData>.Builder items = ImmutableArray.CreateBuilder<PlutusData>();
            while (!Is(TokenType.RBracket))
            {
                items.Add(ParsePlutusData());
                if (!Is(TokenType.RBracket))
                {
                    Expect(TokenType.Comma);
                }
            }
            Expect(TokenType.RBracket);
            return new PlutusDataList(items.ToImmutable());
        }

        if (Is(TokenType.DataMap))
        {
            Advance();
            Expect(TokenType.LBracket);
            ImmutableArray<(PlutusData Key, PlutusData Value)>.Builder entries =
                ImmutableArray.CreateBuilder<(PlutusData, PlutusData)>();
            while (!Is(TokenType.RBracket))
            {
                Expect(TokenType.LParen);
                PlutusData key = ParsePlutusData();
                Expect(TokenType.Comma);
                PlutusData value = ParsePlutusData();
                Expect(TokenType.RParen);
                entries.Add((key, value));
                if (!Is(TokenType.RBracket))
                {
                    Expect(TokenType.Comma);
                }
            }
            Expect(TokenType.RBracket);
            return new PlutusDataMap(entries.ToImmutable());
        }

        if (Is(TokenType.DataConstr))
        {
            Advance();
            if (!Is(TokenType.Number))
            {
                throw new ParseException(
                    $"expected tag number for Constr, got {_current.Type} at position {_current.Position}");
            }
            BigInteger tag = BigInteger.Parse(_current.Value, CultureInfo.InvariantCulture);
            if (tag < 0)
            {
                throw new ParseException(
                    $"invalid Constr tag {_current.Value} at position {_current.Position}");
            }
            Advance();
            Expect(TokenType.LBracket);
            ImmutableArray<PlutusData>.Builder fields = ImmutableArray.CreateBuilder<PlutusData>();
            while (!Is(TokenType.RBracket))
            {
                fields.Add(ParsePlutusData());
                if (!Is(TokenType.RBracket))
                {
                    Expect(TokenType.Comma);
                }
            }
            Expect(TokenType.RBracket);
            return new PlutusDataConstr(tag, fields.ToImmutable());
        }

        throw new ParseException(
            $"expected PlutusData constructor (I, B, List, Map, Constr), got {_current.Type} at position {_current.Position}");
    }

    private static ulong ParseConstrTag(string value, int position)
    {
        return !BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger n)
            ? throw new ParseException($"invalid constr tag {value} at position {position}")
            : n < 0 || n > ulong.MaxValue ? throw new ParseException($"invalid constr tag {value} at position {position}") : (ulong)n;
    }

    private static byte[] HexToBytes(string hex)
    {
        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
        {
            bytes[i / 2] = byte.Parse(hex.AsSpan(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
        return bytes;
    }
}

/// <summary>
/// Maps builtin function text names to <see cref="DefaultFunction"/> enum values.
/// </summary>
internal static class BuiltinNames
{
    private static readonly Dictionary<string, DefaultFunction> NameToFunction = new()
    {
        ["addInteger"] = DefaultFunction.AddInteger,
        ["subtractInteger"] = DefaultFunction.SubtractInteger,
        ["multiplyInteger"] = DefaultFunction.MultiplyInteger,
        ["divideInteger"] = DefaultFunction.DivideInteger,
        ["quotientInteger"] = DefaultFunction.QuotientInteger,
        ["remainderInteger"] = DefaultFunction.RemainderInteger,
        ["modInteger"] = DefaultFunction.ModInteger,
        ["equalsInteger"] = DefaultFunction.EqualsInteger,
        ["lessThanInteger"] = DefaultFunction.LessThanInteger,
        ["lessThanEqualsInteger"] = DefaultFunction.LessThanEqualsInteger,
        ["appendByteString"] = DefaultFunction.AppendByteString,
        ["consByteString"] = DefaultFunction.ConsByteString,
        ["sliceByteString"] = DefaultFunction.SliceByteString,
        ["lengthOfByteString"] = DefaultFunction.LengthOfByteString,
        ["indexByteString"] = DefaultFunction.IndexByteString,
        ["equalsByteString"] = DefaultFunction.EqualsByteString,
        ["lessThanByteString"] = DefaultFunction.LessThanByteString,
        ["lessThanEqualsByteString"] = DefaultFunction.LessThanEqualsByteString,
        ["sha2_256"] = DefaultFunction.Sha2_256,
        ["sha3_256"] = DefaultFunction.Sha3_256,
        ["blake2b_256"] = DefaultFunction.Blake2b_256,
        ["keccak_256"] = DefaultFunction.Keccak_256,
        ["blake2b_224"] = DefaultFunction.Blake2b_224,
        ["ripemd_160"] = DefaultFunction.Ripemd_160,
        ["verifyEd25519Signature"] = DefaultFunction.VerifyEd25519Signature,
        ["verifyEcdsaSecp256k1Signature"] = DefaultFunction.VerifyEcdsaSecp256k1Signature,
        ["verifySchnorrSecp256k1Signature"] = DefaultFunction.VerifySchnorrSecp256k1Signature,
        ["appendString"] = DefaultFunction.AppendString,
        ["equalsString"] = DefaultFunction.EqualsString,
        ["encodeUtf8"] = DefaultFunction.EncodeUtf8,
        ["decodeUtf8"] = DefaultFunction.DecodeUtf8,
        ["ifThenElse"] = DefaultFunction.IfThenElse,
        ["chooseUnit"] = DefaultFunction.ChooseUnit,
        ["trace"] = DefaultFunction.Trace,
        ["fstPair"] = DefaultFunction.FstPair,
        ["sndPair"] = DefaultFunction.SndPair,
        ["chooseList"] = DefaultFunction.ChooseList,
        ["mkCons"] = DefaultFunction.MkCons,
        ["headList"] = DefaultFunction.HeadList,
        ["tailList"] = DefaultFunction.TailList,
        ["nullList"] = DefaultFunction.NullList,
        ["chooseData"] = DefaultFunction.ChooseData,
        ["constrData"] = DefaultFunction.ConstrData,
        ["mapData"] = DefaultFunction.MapData,
        ["listData"] = DefaultFunction.ListData,
        ["iData"] = DefaultFunction.IData,
        ["bData"] = DefaultFunction.BData,
        ["unConstrData"] = DefaultFunction.UnConstrData,
        ["unMapData"] = DefaultFunction.UnMapData,
        ["unListData"] = DefaultFunction.UnListData,
        ["unIData"] = DefaultFunction.UnIData,
        ["unBData"] = DefaultFunction.UnBData,
        ["equalsData"] = DefaultFunction.EqualsData,
        ["mkPairData"] = DefaultFunction.MkPairData,
        ["mkNilData"] = DefaultFunction.MkNilData,
        ["mkNilPairData"] = DefaultFunction.MkNilPairData,
        ["serialiseData"] = DefaultFunction.SerialiseData,
        ["bls12_381_G1_add"] = DefaultFunction.Bls12_381_G1_Add,
        ["bls12_381_G1_neg"] = DefaultFunction.Bls12_381_G1_Neg,
        ["bls12_381_G1_scalarMul"] = DefaultFunction.Bls12_381_G1_ScalarMul,
        ["bls12_381_G1_equal"] = DefaultFunction.Bls12_381_G1_Equal,
        ["bls12_381_G1_hashToGroup"] = DefaultFunction.Bls12_381_G1_HashToGroup,
        ["bls12_381_G1_compress"] = DefaultFunction.Bls12_381_G1_Compress,
        ["bls12_381_G1_uncompress"] = DefaultFunction.Bls12_381_G1_Uncompress,
        ["bls12_381_G2_add"] = DefaultFunction.Bls12_381_G2_Add,
        ["bls12_381_G2_neg"] = DefaultFunction.Bls12_381_G2_Neg,
        ["bls12_381_G2_scalarMul"] = DefaultFunction.Bls12_381_G2_ScalarMul,
        ["bls12_381_G2_equal"] = DefaultFunction.Bls12_381_G2_Equal,
        ["bls12_381_G2_hashToGroup"] = DefaultFunction.Bls12_381_G2_HashToGroup,
        ["bls12_381_G2_compress"] = DefaultFunction.Bls12_381_G2_Compress,
        ["bls12_381_G2_uncompress"] = DefaultFunction.Bls12_381_G2_Uncompress,
        ["bls12_381_millerLoop"] = DefaultFunction.Bls12_381_MillerLoop,
        ["bls12_381_mulMlResult"] = DefaultFunction.Bls12_381_MulMlResult,
        ["bls12_381_finalVerify"] = DefaultFunction.Bls12_381_FinalVerify,
        ["integerToByteString"] = DefaultFunction.IntegerToByteString,
        ["byteStringToInteger"] = DefaultFunction.ByteStringToInteger,
        ["andByteString"] = DefaultFunction.AndByteString,
        ["orByteString"] = DefaultFunction.OrByteString,
        ["xorByteString"] = DefaultFunction.XorByteString,
        ["complementByteString"] = DefaultFunction.ComplementByteString,
        ["readBit"] = DefaultFunction.ReadBit,
        ["writeBits"] = DefaultFunction.WriteBits,
        ["replicateByte"] = DefaultFunction.ReplicateByte,
        ["shiftByteString"] = DefaultFunction.ShiftByteString,
        ["rotateByteString"] = DefaultFunction.RotateByteString,
        ["countSetBits"] = DefaultFunction.CountSetBits,
        ["findFirstSetBit"] = DefaultFunction.FindFirstSetBit,
        ["expModInteger"] = DefaultFunction.ExpModInteger,
        ["dropList"] = DefaultFunction.DropList,
        ["lengthOfArray"] = DefaultFunction.LengthOfArray,
        ["listToArray"] = DefaultFunction.ListToArray,
        ["indexArray"] = DefaultFunction.IndexArray,
        ["bls12_381_G1_multiScalarMul"] = DefaultFunction.Bls12_381_G1_MultiScalarMul,
        ["bls12_381_G2_multiScalarMul"] = DefaultFunction.Bls12_381_G2_MultiScalarMul,
        ["insertCoin"] = DefaultFunction.InsertCoin,
        ["lookupCoin"] = DefaultFunction.LookupCoin,
        ["unionValue"] = DefaultFunction.UnionValue,
        ["valueContains"] = DefaultFunction.ValueContains,
        ["valueData"] = DefaultFunction.ValueData,
        ["unValueData"] = DefaultFunction.UnValueData,
        ["scaleValue"] = DefaultFunction.ScaleValue,
    };

    private static readonly Dictionary<DefaultFunction, string> FunctionToName = BuildReverse();

    private static Dictionary<DefaultFunction, string> BuildReverse()
    {
        Dictionary<DefaultFunction, string> result = [];
        foreach (KeyValuePair<string, DefaultFunction> kv in NameToFunction)
        {
            result[kv.Value] = kv.Key;
        }
        return result;
    }

    internal static bool TryParse(string name, out DefaultFunction func)
    {
        return NameToFunction.TryGetValue(name, out func);
    }

    internal static string GetName(DefaultFunction func)
    {
        return FunctionToName.TryGetValue(func, out string? name) ? name : func.ToString();
    }
}
