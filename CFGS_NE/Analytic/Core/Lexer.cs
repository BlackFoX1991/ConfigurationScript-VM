using CFGS_VM.Analytic.Tokens;
using System.Globalization;
using System.Numerics;

/// <summary>
/// Defines the <see cref="Lexer" />
/// </summary>
public class Lexer
{
    /// <summary>
    /// Gets or sets the FileName
    /// </summary>
    public string FileName { get; set; }

    /// <summary>
    /// Defines the Keywords
    /// </summary>
    public static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
        {
            { "var", TokenType.Var },
            { "if", TokenType.If },
            { "else", TokenType.Else },
            { "delete", TokenType.Delete },
            { "while", TokenType.While },
            { "for", TokenType.For },
            { "break", TokenType.Break },
            { "continue", TokenType.Continue },
            { "func", TokenType.Func },
            { "return", TokenType.Return },
            { "match", TokenType.Match },
            { "case", TokenType.Case },
            { "default", TokenType.Default },
            { "try", TokenType.Try },
            { "catch", TokenType.Catch },
            { "finally", TokenType.Finally },
            { "throw", TokenType.Throw },
            { "class", TokenType.Class },
            { "new", TokenType.New },
            { "null", TokenType.Null },
            { "true", TokenType.True },
            { "false", TokenType.False },
            { "import", TokenType.Import },
            { "enum", TokenType.Enum },
            { "from", TokenType.From },
            { "static", TokenType.Static },
            { "in", TokenType.In },
            { "foreach", TokenType.ForEach },
            {"super",TokenType.Ident },
            {"this",TokenType.Ident },
            {"outer", TokenType.Ident },
            {"type", TokenType.Ident },
            {"await", TokenType.Await},

        };

    /// <summary>
    /// The MakeToken
    /// </summary>
    /// <param name="type">The type<see cref="TokenType"/></param>
    /// <param name="value">The value<see cref="object"/></param>
    /// <returns>The <see cref="Token"/></returns>
    private Token MakeToken(TokenType type, object value)
    {
        return new Token(type, value, _line, _col, FileName);
    }

    /// <summary>
    /// The SyncPos
    /// </summary>
    /// <param name="offset">The offset<see cref="int"/></param>
    private void SyncPos(int offset = 1)
    {
        for (int i = 0; i < offset; i++)
        {
            if (_pos < _text.Length)
            {
                if (_text[_pos] == '\n')
                {
                    _line++;
                    _col = 1;
                }
                else
                {
                    _col++;
                }
            }
            _pos++;
        }
    }

    /// <summary>
    /// Defines the _text
    /// </summary>
    public readonly string _text;

    /// <summary>
    /// Defines the _pos
    /// </summary>
    private int _pos;

    /// <summary>
    /// Defines the _col
    /// </summary>
    private int _col = 1;

    /// <summary>
    /// Defines the _line
    /// </summary>
    private int _line = 1;

    /// <summary>
    /// Initializes a new instance of the <see cref="Lexer"/> class.
    /// </summary>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="text">The text<see cref="string"/></param>
    public Lexer(string name, string text)
    {
        FileName = name;
        _line = 1;
        _col = 1;
        _text = text;
    }

    /// <summary>
    /// Gets the Current
    /// </summary>
    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    /// <summary>
    /// Gets the Peek
    /// </summary>
    private char Peek => _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';

    /// <summary>
    /// The GetNextToken
    /// </summary>
    /// <returns>The <see cref="Token"/></returns>
    public Token GetNextToken()
    {
        while (_pos < _text.Length)
        {
            char c = Current;

            if (char.IsWhiteSpace(c))
            {
                SyncPos();
                continue;
            }

            if (c == '#')
            {
                if (Peek == '+')
                {
                    SyncPos(2);
                    while (_pos < _text.Length)
                    {
                        if (_text[_pos] == '+' && Peek == '#')
                        {
                            SyncPos(2);
                            break;
                        }
                        SyncPos();
                    }
                    continue;
                }
                else
                {
                    while (_pos < _text.Length && _text[_pos] != '\n')
                        SyncPos();

                    if (_pos < _text.Length && _text[_pos] == '\n')
                        SyncPos();

                    continue;
                }
            }

            if (Current == '\"')
            {
                int startLine = _line, startCol = _col;
                SyncPos();
                System.Text.StringBuilder sb = new();

                while (Current != '\"' && Current != '\0')
                {
                    if (Current == '\\')
                    {
                        SyncPos();
                        char esc = Current;
                        if (esc == 'n') { sb.Append('\n'); }
                        else if (esc == 't') { sb.Append('\t'); }
                        else if (esc == 'r') { sb.Append('\r'); }
                        else if (esc == '\\') { sb.Append('\\'); }
                        else if (esc == '\"') { sb.Append('\"'); }
                        else if (esc == 'u')
                        {
                            string hex = "";
                            for (int i = 0; i < 4; i++)
                            {
                                SyncPos();
                                if (!Uri.IsHexDigit(Current)) throw new LexerException("invalid \\u escape in string", startLine, startCol, FileName);
                                hex += Current;
                            }
                            sb.Append((char)Convert.ToInt32(hex, 16));
                        }
                        else
                        {
                            throw new LexerException($"unknown escape '\\{esc}' in string", startLine, startCol, FileName);
                        }
                        SyncPos();
                    }
                    else
                    {
                        sb.Append(Current);
                        SyncPos();
                    }
                }

                if (Current != '\"')
                    throw new LexerException("unterminated string literal", startLine, startCol, FileName);
                SyncPos();

                return MakeToken(TokenType.String, sb.ToString());
            }

            if (Current == '\'')
            {
                int startLine = _line, startCol = _col;
                SyncPos();

                if (Current == '\0')
                    throw new LexerException("unterminated char literal", startLine, startCol, FileName);

                char ch;
                if (Current == '\\')
                {
                    SyncPos();
                    char esc = Current;
                    if (esc == 'n') ch = '\n';
                    else if (esc == 't') ch = '\t';
                    else if (esc == 'r') ch = '\r';
                    else if (esc == '\\') ch = '\\';
                    else if (esc == '\'') ch = '\'';
                    else if (esc == '\"') ch = '\"';
                    else if (esc == 'u')
                    {
                        string hex = "";
                        for (int i = 0; i < 4; i++)
                        {
                            SyncPos();
                            if (!Uri.IsHexDigit(Current)) throw new LexerException("invalid \\u escape in char literal", startLine, startCol, FileName);
                            hex += Current;
                        }
                        ch = (char)Convert.ToInt32(hex, 16);
                    }
                    else
                        throw new LexerException($"unknown escape '\\{esc}' in char literal", startLine, startCol, FileName);
                    SyncPos();
                }
                else
                {
                    ch = Current;
                    SyncPos();
                }

                if (Current != '\'')
                    throw new LexerException("char literal must contain exactly one character", startLine, startCol, FileName);

                SyncPos();
                return MakeToken(TokenType.Char, ch.ToString());
            }

            if (char.IsDigit(Current))
            {
                int startLine = _line, startCol = _col;

                System.Text.StringBuilder raw = new();
                bool isFloat = false;
                bool seenDot = false;
                bool seenExp = false;
                bool expectExpSign = false;

                static BigInteger ParseBaseBigInteger(string digits, int radix)
                {
                    BigInteger acc = BigInteger.Zero;
                    foreach (char ch in digits)
                    {
                        int v = ch switch
                        {
                            >= '0' and <= '9' => ch - '0',
                            >= 'A' and <= 'Z' => ch - 'A' + 10,
                            >= 'a' and <= 'z' => ch - 'a' + 10,
                            _ => -1
                        };
                        if (v < 0 || v >= radix) throw new FormatException($"invalid digit '{ch}' for base {radix}");
                        acc = acc * radix + v;
                    }
                    return acc;
                }

                static object NarrowIntOrBig(BigInteger bi)
                {
                    if (bi <= int.MaxValue && bi >= int.MinValue) return (int)bi;
                    if (bi <= long.MaxValue && bi >= long.MinValue) return (long)bi;
                    return bi;
                }

                if (Current == '0')
                {
                    raw.Append(Current); SyncPos();
                    if (Current == 'x' || Current == 'X')
                    {
                        SyncPos();
                        string hex = "";
                        while (Uri.IsHexDigit(Current) || Current == '_')
                        {
                            if (Current != '_') hex += Current;
                            SyncPos();
                        }
                        if (hex.Length == 0) throw new LexerException("invalid hex literal", startLine, startCol, FileName);

                        try
                        {
                            long l = Convert.ToInt64(hex, 16);
                            return MakeToken(TokenType.Number, l <= int.MaxValue ? (object)(int)l : l);
                        }
                        catch
                        {
                            BigInteger bi = BigInteger.Parse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                            return MakeToken(TokenType.Number, NarrowIntOrBig(bi));
                        }
                    }
                    else if (Current == 'b' || Current == 'B')
                    {
                        SyncPos();
                        string bin = "";
                        while (Current == '0' || Current == '1' || Current == '_')
                        {
                            if (Current != '_') bin += Current;
                            SyncPos();
                        }
                        if (bin.Length == 0) throw new LexerException("invalid binary literal", startLine, startCol, FileName);

                        try
                        {
                            long l = Convert.ToInt64(bin, 2);
                            return MakeToken(TokenType.Number, l <= int.MaxValue ? (object)(int)l : l);
                        }
                        catch
                        {
                            BigInteger bi = ParseBaseBigInteger(bin, 2);
                            return MakeToken(TokenType.Number, NarrowIntOrBig(bi));
                        }
                    }
                    else if (Current == 'o' || Current == 'O')
                    {
                        SyncPos();
                        string oct = "";
                        while ((Current >= '0' && Current <= '7') || Current == '_')
                        {
                            if (Current != '_') oct += Current;
                            SyncPos();
                        }
                        if (oct.Length == 0) throw new LexerException("invalid octal literal", startLine, startCol, FileName);

                        try
                        {
                            long l = Convert.ToInt64(oct, 8);
                            return MakeToken(TokenType.Number, l <= int.MaxValue ? (object)(int)l : l);
                        }
                        catch
                        {
                            BigInteger bi = ParseBaseBigInteger(oct, 8);
                            return MakeToken(TokenType.Number, NarrowIntOrBig(bi));
                        }
                    }
                    else
                    {
                        raw.Clear();
                        raw.Append('0');
                    }
                }

                while (true)
                {
                    char ch = Current;
                    if (char.IsDigit(ch))
                    {
                        raw.Append(ch);
                        SyncPos();
                        expectExpSign = false;
                    }
                    else if (ch == '_')
                    {
                        SyncPos();
                    }
                    else if (ch == '.' && !seenDot && !seenExp)
                    {
                        seenDot = true;
                        isFloat = true;
                        raw.Append(ch);
                        SyncPos();
                    }
                    else if ((ch == 'e' || ch == 'E') && !seenExp)
                    {
                        seenExp = true;
                        isFloat = true;
                        raw.Append(ch);
                        SyncPos();
                        expectExpSign = true;
                    }
                    else if ((ch == '+' || ch == '-') && expectExpSign)
                    {
                        raw.Append(ch);
                        SyncPos();
                        expectExpSign = false;
                    }
                    else
                    {
                        break;
                    }
                }

                string num = raw.ToString().Replace("_", "");
                object val;
                CultureInfo ci = CultureInfo.InvariantCulture;

                if (isFloat || num.Contains("e") || num.Contains("E") || num.Contains("."))
                {
                    if (double.TryParse(num, NumberStyles.Float, ci, out double d)) val = d;
                    else if (decimal.TryParse(num, NumberStyles.Float, ci, out decimal m)) val = m;
                    else throw new LexerException($"invalid number literal '{num}'", startLine, startCol, FileName);
                }
                else
                {
                    if (int.TryParse(num, NumberStyles.Integer, ci, out int i)) val = i;
                    else if (long.TryParse(num, NumberStyles.Integer, ci, out long l)) val = l;
                    else
                    {
                        if (!BigInteger.TryParse(num, NumberStyles.Integer, ci, out BigInteger bi))
                            throw new LexerException($"invalid number literal '{num}'", startLine, startCol, FileName);
                        val = NarrowIntOrBig(bi);
                    }
                }
                return MakeToken(TokenType.Number, val);
            }

            if (char.IsLetter(c) || c == '_')
            {
                string id = "";
                while (char.IsLetterOrDigit(Current) || Current == '_') { id += Current; SyncPos(); }
                if (Keywords.ContainsKey(id))
                    return MakeToken(Keywords[id], id);
                else
                    return MakeToken(TokenType.Ident, id);

            }

            if (c == '=' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Eq, "=="); }
            if (c == '!' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Neq, "!="); }
            if (c == '<' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Le, "<="); }
            if (c == '>' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Ge, ">="); }
            if (c == '<' && Peek == '<') { SyncPos(2); return MakeToken(TokenType.bShiftL, "<<"); }
            if (c == '>' && Peek == '>') { SyncPos(2); return MakeToken(TokenType.bShiftR, ">>"); }
            if (c == '&' && Peek == '&') { SyncPos(2); return MakeToken(TokenType.AndAnd, "&&"); }
            if (c == '|' && Peek == '|') { SyncPos(2); return MakeToken(TokenType.OrOr, "||"); }

            if (c == '+' && Peek == '+') { SyncPos(2); return MakeToken(TokenType.PlusPlus, "++"); }
            if (c == '+' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.PlusAssign, "+="); }

            if (c == '-' && Peek == '-') { SyncPos(2); return MakeToken(TokenType.MinusMinus, "--"); }
            if (c == '-' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.MinusAssign, "-="); }

            if (c == '*' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.StarAssign, "*="); }
            if (c == '/' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.SlashAssign, "/="); }

            if (c == '%' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.ModAssign, "%="); }
            if (c == '*' && Peek == '*') { SyncPos(2); return MakeToken(TokenType.Expo, "**"); }
            if (c == '?' && Peek == '?') { SyncPos(2); return MakeToken(TokenType.QQNull, "??"); }

            SyncPos();

            return c switch
            {
                '+' => MakeToken(TokenType.Plus, "+"),
                '-' => MakeToken(TokenType.Minus, "-"),
                '*' => MakeToken(TokenType.Star, "*"),
                '/' => MakeToken(TokenType.Slash, "/"),
                '%' => MakeToken(TokenType.Modulo, "%"),
                '(' => MakeToken(TokenType.LParen, "("),
                ')' => MakeToken(TokenType.RParen, ")"),
                '{' => MakeToken(TokenType.LBrace, "{"),
                '}' => MakeToken(TokenType.RBrace, "}"),
                '[' => MakeToken(TokenType.LBracket, "["),
                ']' => MakeToken(TokenType.RBracket, "]"),
                ',' => MakeToken(TokenType.Comma, ","),
                ';' => MakeToken(TokenType.Semi, ";"),
                ':' => MakeToken(TokenType.Colon, ":"),
                '=' => MakeToken(TokenType.Assign, "="),
                '<' => MakeToken(TokenType.Lt, "<"),
                '>' => MakeToken(TokenType.Gt, ">"),
                '|' => MakeToken(TokenType.bOr, "|"),
                '&' => MakeToken(TokenType.bAnd, "&"),
                '^' => MakeToken(TokenType.bXor, "^"),
                '!' => MakeToken(TokenType.Not, "!"),
                '.' => MakeToken(TokenType.Dot, "."),
                '~' => MakeToken(TokenType.Range, "~"),
                '?' => MakeToken(TokenType.Question, "?"),
                _ => throw new LexerException($"unknown character in input: '{c}'", _line, _col, FileName)
            };
        }
        return MakeToken(TokenType.EOF, "");
    }
}
