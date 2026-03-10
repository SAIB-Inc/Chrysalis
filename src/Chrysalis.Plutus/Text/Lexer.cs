namespace Chrysalis.Plutus.Text;

/// <summary>
/// Hand-written lexer for UPLC text format.
/// Ported from blaze-plutus lexer.ts.
/// </summary>
internal sealed class Lexer
{
    private readonly string _source;
    private int _pos;

    internal Lexer(string source)
    {
        _source = source;
        _pos = 0;
    }

    private bool IsAtEnd => _pos >= _source.Length;

    private char Peek()
    {
        return IsAtEnd ? '\0' : _source[_pos];
    }

    private char PeekNext()
    {
        return _pos + 1 >= _source.Length ? '\0' : _source[_pos + 1];
    }

    private char Advance()
    {
        char c = _source[_pos];
        _pos++;
        return c;
    }

    private void SkipWhitespaceAndComments()
    {
        while (!IsAtEnd)
        {
            char c = Peek();
            if (IsWhitespace(c))
            {
                _ = Advance();
            }
            else if (c == '-' && PeekNext() == '-')
            {
                while (!IsAtEnd && Peek() != '\n')
                {
                    _ = Advance();
                }
            }
            else
            {
                break;
            }
        }
    }

    internal Token NextToken()
    {
        SkipWhitespaceAndComments();

        int position = _pos;

        if (IsAtEnd)
        {
            return new Token(TokenType.Eof, "", position);
        }

        char c = Advance();

        switch (c)
        {
            case '(':
                if (Peek() == ')')
                {
                    _ = Advance();
                    return new Token(TokenType.Unit, "()", position);
                }
                return new Token(TokenType.LParen, "(", position);
            case ')':
                return new Token(TokenType.RParen, ")", position);
            case '[':
                return new Token(TokenType.LBracket, "[", position);
            case ']':
                return new Token(TokenType.RBracket, "]", position);
            case '.':
                return new Token(TokenType.Dot, ".", position);
            case ',':
                return new Token(TokenType.Comma, ",", position);
            case '#':
                return ReadByteString(position);
            case '"':
                return ReadString(position);
            case '0':
                if (Peek() == 'x')
                {
                    _ = Advance();
                    return ReadHexLiteral(position);
                }
                return ReadNumber(position, c);
            default:
                if (c is '-' or '+')
                {
                    return !IsAtEnd && IsDigit(Peek()) ? ReadNumber(position, c) : ReadIdentifier(position, c);
                }
                if (IsDigit(c))
                {
                    return ReadNumber(position, c);
                }
                if (IsAlpha(c) || c == '\'')
                {
                    return ReadIdentifier(position, c);
                }
                throw new ParseException($"unexpected character '{c}' at position {position}");
        }
    }

    private Token ReadNumber(int position, char first)
    {
        Span<char> buf = stackalloc char[128];
        int len = 0;
        buf[len++] = first;
        while (!IsAtEnd && IsDigit(Peek()))
        {
            if (len < buf.Length)
            {
                buf[len++] = Advance();
            }
            else
            {
                // Fallback for very large numbers
                string prefix = new(buf[..len]);
                System.Text.StringBuilder sb = new(prefix);
                while (!IsAtEnd && IsDigit(Peek()))
                {
                    _ = sb.Append(Advance());
                }
                return new Token(TokenType.Number, sb.ToString(), position);
            }
        }
        return new Token(TokenType.Number, new string(buf[..len]), position);
    }

    private Token ReadIdentifier(int position, char first)
    {
        Span<char> buf = stackalloc char[128];
        int len = 0;
        buf[len++] = first;
        while (!IsAtEnd && len < buf.Length)
        {
            char p = Peek();
            if (IsAlphaNumeric(p) || p == '_' || p == '\'' || p == '-')
            {
                buf[len++] = Advance();
            }
            else
            {
                break;
            }
        }
        string literal = new(buf[..len]);
        TokenType type = Keywords.Lookup(literal);
        return new Token(type, literal, position);
    }

    private Token ReadByteString(int position)
    {
        int start = _pos;
        while (!IsAtEnd && !IsWhitespace(Peek()) && Peek() != ')' && Peek() != ']' && Peek() != ',')
        {
            char ch = Peek();
            if (!IsHexDigit(ch))
            {
                throw new ParseException($"invalid bytestring character '{ch}' at position {_pos}");
            }
            _ = Advance();
        }
        string hex = _source[start.._pos];
        return hex.Length % 2 != 0
            ? throw new ParseException($"bytestring #{hex} has odd length at position {position}")
            : new Token(TokenType.ByteString, hex, position);
    }

    private Token ReadHexLiteral(int position)
    {
        int start = _pos;
        while (!IsAtEnd && !IsWhitespace(Peek()) && Peek() != ')' && Peek() != ']' && Peek() != ',')
        {
            char ch = Peek();
            if (!IsHexDigit(ch))
            {
                throw new ParseException($"invalid hex character '{ch}' at position {_pos}");
            }
            _ = Advance();
        }
        string hex = _source[start.._pos];
        return new Token(TokenType.Point, hex, position);
    }

    private Token ReadString(int position)
    {
        System.Text.StringBuilder result = new();
        while (!IsAtEnd && Peek() != '"')
        {
            if (Peek() == '\\')
            {
                _ = Advance();
                if (IsAtEnd)
                {
                    throw new ParseException($"unterminated string escape at position {_pos}");
                }
                char escChar = Advance();

                if (TrySimpleEscape(escChar, out char escaped))
                {
                    _ = result.Append(escaped);
                    continue;
                }

                if (escChar == 'u')
                {
                    string hexStr = ReadEscapeHexDigits(4);
                    if (hexStr.Length == 0)
                    {
                        throw new ParseException($"invalid unicode escape sequence at position {position}");
                    }
                    int codepoint = int.Parse(hexStr, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                    _ = result.Append(char.ConvertFromUtf32(codepoint));
                    continue;
                }

                if (escChar == 'x')
                {
                    string hexStr = ReadEscapeHexDigits(2);
                    if (hexStr.Length == 0)
                    {
                        throw new ParseException($"invalid hex escape sequence at position {position}");
                    }
                    int b = int.Parse(hexStr, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture);
                    _ = result.Append((char)b);
                    continue;
                }

                if (escChar == 'o')
                {
                    string octalStr = ReadEscapeOctalDigits(3);
                    if (octalStr.Length == 0)
                    {
                        throw new ParseException($"invalid octal escape sequence at position {position}");
                    }
                    int value = Convert.ToInt32(octalStr, 8);
                    _ = result.Append(char.ConvertFromUtf32(value));
                    continue;
                }

                if (IsLetter(escChar))
                {
                    string name = ReadNamedEscape(escChar);
                    if (NamedEscapes.TryGetValue(name, out int namedCode))
                    {
                        _ = result.Append((char)namedCode);
                        continue;
                    }
                    _ = result.Append('\\');
                    _ = result.Append(name);
                    continue;
                }

                if (IsDigit(escChar))
                {
                    string decStr = ReadDecimalEscape(escChar);
                    int codepoint = int.Parse(decStr, System.Globalization.CultureInfo.InvariantCulture);
                    _ = result.Append(char.ConvertFromUtf32(codepoint));
                    continue;
                }

                _ = result.Append('\\');
                _ = result.Append(escChar);
            }
            else
            {
                _ = result.Append(Advance());
            }
        }

        if (IsAtEnd)
        {
            throw new ParseException($"unterminated string at position {position}");
        }

        _ = Advance(); // closing quote
        return new Token(TokenType.String, result.ToString(), position);
    }

    private string ReadEscapeHexDigits(int maxCount)
    {
        int start = _pos;
        int count = 0;
        while (!IsAtEnd && count < maxCount && IsHexDigit(Peek()))
        {
            _ = Advance();
            count++;
        }
        return _source[start.._pos];
    }

    private string ReadEscapeOctalDigits(int maxCount)
    {
        int start = _pos;
        int count = 0;
        while (!IsAtEnd && count < maxCount && IsOctalDigit(Peek()))
        {
            _ = Advance();
            count++;
        }
        return _source[start.._pos];
    }

    private string ReadNamedEscape(char first)
    {
        Span<char> buf = stackalloc char[8];
        int len = 0;
        buf[len++] = first;
        while (!IsAtEnd && IsLetter(Peek()) && len < buf.Length)
        {
            buf[len++] = Advance();
        }
        return new string(buf[..len]);
    }

    private string ReadDecimalEscape(char first)
    {
        Span<char> buf = stackalloc char[16];
        int len = 0;
        buf[len++] = first;
        while (!IsAtEnd && IsDigit(Peek()) && len < buf.Length)
        {
            buf[len++] = Advance();
        }
        return new string(buf[..len]);
    }

    private static bool TrySimpleEscape(char c, out char result)
    {
        result = c switch
        {
            'a' => '\a',
            'b' => '\b',
            'f' => '\f',
            'n' => '\n',
            'r' => '\r',
            't' => '\t',
            'v' => '\v',
            '"' => '"',
            '\\' => '\\',
            _ => '\0',
        };
        return c is 'a' or 'b' or 'f' or 'n' or 'r' or 't' or 'v' or '"' or '\\';
    }

    private static bool IsDigit(char c)
    {
        return c is >= '0' and <= '9';
    }

    private static bool IsHexDigit(char c)
    {
        return c is (>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F');
    }

    private static bool IsAlpha(char c)
    {
        return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '_';
    }

    private static bool IsLetter(char c)
    {
        return c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
    }

    private static bool IsAlphaNumeric(char c)
    {
        return IsAlpha(c) || IsDigit(c);
    }

    private static bool IsWhitespace(char c)
    {
        return c is ' ' or '\t' or '\n' or '\r';
    }

    private static bool IsOctalDigit(char c)
    {
        return c is >= '0' and <= '7';
    }

    private static readonly Dictionary<string, int> NamedEscapes = new()
    {
        ["NUL"] = 0x00,
        ["SOH"] = 0x01,
        ["STX"] = 0x02,
        ["ETX"] = 0x03,
        ["EOT"] = 0x04,
        ["ENQ"] = 0x05,
        ["ACK"] = 0x06,
        ["BEL"] = 0x07,
        ["BS"] = 0x08,
        ["HT"] = 0x09,
        ["LF"] = 0x0a,
        ["VT"] = 0x0b,
        ["FF"] = 0x0c,
        ["CR"] = 0x0d,
        ["SO"] = 0x0e,
        ["SI"] = 0x0f,
        ["DLE"] = 0x10,
        ["DC1"] = 0x11,
        ["DC2"] = 0x12,
        ["DC3"] = 0x13,
        ["DC4"] = 0x14,
        ["NAK"] = 0x15,
        ["SYN"] = 0x16,
        ["ETB"] = 0x17,
        ["CAN"] = 0x18,
        ["EM"] = 0x19,
        ["SUB"] = 0x1a,
        ["ESC"] = 0x1b,
        ["FS"] = 0x1c,
        ["GS"] = 0x1d,
        ["RS"] = 0x1e,
        ["US"] = 0x1f,
        ["SP"] = 0x20,
        ["DEL"] = 0x7f,
    };
}

internal enum TokenType
{
    LParen,
    RParen,
    LBracket,
    RBracket,
    Dot,
    Comma,
    Number,
    String,
    ByteString,
    Point,
    Unit,
    True,
    False,
    Identifier,
    Lam,
    Delay,
    Force,
    Builtin,
    Con,
    Error,
    Program,
    Constr,
    Case,
    DataI,
    DataB,
    DataList,
    DataMap,
    DataConstr,
    List,
    Pair,
    Array,
    Eof,
}

internal readonly record struct Token(TokenType Type, string Value, int Position);

internal static class Keywords
{
    private static readonly Dictionary<string, TokenType> Map = new()
    {
        ["lam"] = TokenType.Lam,
        ["delay"] = TokenType.Delay,
        ["force"] = TokenType.Force,
        ["builtin"] = TokenType.Builtin,
        ["con"] = TokenType.Con,
        ["error"] = TokenType.Error,
        ["program"] = TokenType.Program,
        ["constr"] = TokenType.Constr,
        ["case"] = TokenType.Case,
        ["True"] = TokenType.True,
        ["False"] = TokenType.False,
        ["I"] = TokenType.DataI,
        ["B"] = TokenType.DataB,
        ["List"] = TokenType.DataList,
        ["Map"] = TokenType.DataMap,
        ["Constr"] = TokenType.DataConstr,
        ["list"] = TokenType.List,
        ["pair"] = TokenType.Pair,
        ["array"] = TokenType.Array,
    };

    internal static TokenType Lookup(string text)
    {
        return Map.TryGetValue(text, out TokenType type) ? type : TokenType.Identifier;
    }
}
