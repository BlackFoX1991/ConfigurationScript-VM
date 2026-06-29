using CFGS_VM.Analytic.Tokens;
using System.Globalization;
using System.Numerics;

public class Lexer
{
    /// <summary>
    /// Gets or sets the FileName
    /// </summary>
    public string FileName { get; set; }

    public static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.Ordinal)
        {
            { "var", TokenType.Var },
            { "const", TokenType.Const },
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
            { "using", TokenType.Using },
            { "defer", TokenType.Defer },
            { "throw", TokenType.Throw },
            { "class", TokenType.Class },
            { "interface", TokenType.Interface },
            { "new", TokenType.New },
            { "null", TokenType.Null },
            { "true", TokenType.True },
            { "false", TokenType.False },
            { "import", TokenType.Import },
            { "use", TokenType.Use },
            { "export", TokenType.Export },
            { "namespace", TokenType.Namespace },
            { "enum", TokenType.Enum },
            { "from", TokenType.From },
            { "as", TokenType.As },
            { "static", TokenType.Static },
            { "property", TokenType.Property },
            { "public", TokenType.Public },
            { "private", TokenType.Private },
            { "protected", TokenType.Protected },
            { "in", TokenType.In },
            { "is", TokenType.Is },
            { "foreach", TokenType.ForEach },
            {"super",TokenType.Ident },
            {"this",TokenType.Ident },
            {"outer", TokenType.Ident },
            {"type", TokenType.Ident },
            {"async", TokenType.Async},
            {"yield", TokenType.Yield},
            {"await", TokenType.Await},
            {"out", TokenType.Out},
            {"do", TokenType.Do }

        };

    private Token MakeToken(TokenType type, object value, int? lengthOverride = null)
    {
        value ??= string.Empty;
        int length = lengthOverride ?? (_line == _tokLine
            ? Math.Max(1, _col - _tokCol)
            : Math.Max(1, value.ToString()?.Length ?? 1));
        return new Token(type, value, _tokLine, _tokCol, FileName, length, _tokenIndex++, _tokPos);
    }

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

    public readonly string _text;

    private int _pos;

    private int _col = 1;

    private int _line = 1;

    private int _tokLine = 1;

    private int _tokCol = 1;

    private int _tokPos;

    private int _tokenIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="Lexer"/> class.
    /// </summary>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="text">The text<see cref="string"/></param>
    public Lexer(string name, string text, int initialLine = 1, int initialColumn = 1)
    {
        FileName = name;
        _line = initialLine;
        _col = initialColumn;
        _tokLine = initialLine;
        _tokCol = initialColumn;
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

    private char Peek2 => _pos + 2 < _text.Length ? _text[_pos + 2] : '\0';

    private bool TryLexStringToken(out Token token)
    {
        token = null!;

        bool isInterpolated = false;
        bool isVerbatim = false;

        if (Current == '$')
        {
            if (Peek == '\"')
            {
                isInterpolated = true;
                SyncPos();
            }
            else if (Peek == '@' && Peek2 == '\"')
            {
                isInterpolated = true;
                isVerbatim = true;
                SyncPos(2);
            }
            else
            {
                return false;
            }
        }
        else if (Current == '@')
        {
            if (Peek == '\"')
            {
                isVerbatim = true;
                SyncPos();
            }
            else if (Peek == '$' && Peek2 == '\"')
            {
                isInterpolated = true;
                isVerbatim = true;
                SyncPos(2);
            }
            else
            {
                return false;
            }
        }
        else if (Current != '\"')
        {
            return false;
        }

        int startLine = _tokLine;
        int startCol = _tokCol;

        if (Current != '\"')
            throw new LexerException("invalid string literal prefix", startLine, startCol, FileName);

        SyncPos();

        System.Text.StringBuilder text = new();
        List<InterpolatedStringTokenPart>? parts = isInterpolated ? new List<InterpolatedStringTokenPart>() : null;

        void FlushTextPart()
        {
            if (!isInterpolated || text.Length == 0)
                return;

            parts!.Add(new InterpolatedStringTextTokenPart(text.ToString()));
            text.Clear();
        }

        while (Current != '\0')
        {
            if (Current == '\"')
            {
                if (isVerbatim && Peek == '\"')
                {
                    text.Append('\"');
                    SyncPos(2);
                    continue;
                }

                SyncPos();
                if (!isInterpolated)
                {
                    token = MakeToken(TokenType.String, text.ToString(), _pos - _tokPos);
                    return true;
                }

                FlushTextPart();
                token = MakeToken(TokenType.String, new InterpolatedStringTokenValue(parts!), _pos - _tokPos);
                return true;
            }

            if (isInterpolated)
            {
                if (Current == '{')
                {
                    if (Peek == '{')
                    {
                        text.Append('{');
                        SyncPos(2);
                        continue;
                    }

                    FlushTextPart();
                    int braceLine = _line;
                    int braceCol = _col;
                    SyncPos();
                    parts!.Add(ReadInterpolatedExpressionPart(braceLine, braceCol));
                    continue;
                }

                if (Current == '}')
                {
                    if (Peek == '}')
                    {
                        text.Append('}');
                        SyncPos(2);
                        continue;
                    }

                    throw new LexerException("unexpected '}' in interpolated string literal; escape it as '}}'", _line, _col, FileName);
                }
            }

            if (isVerbatim)
            {
                text.Append(Current);
                SyncPos();
                continue;
            }

            if (Current == '\\')
            {
                SyncPos();
                char esc = Current;
                if (esc == 'n') { text.Append('\n'); }
                else if (esc == 't') { text.Append('\t'); }
                else if (esc == 'r') { text.Append('\r'); }
                else if (esc == '\\') { text.Append('\\'); }
                else if (esc == '\"') { text.Append('\"'); }
                else if (esc == 'u')
                {
                    string hex = "";
                    for (int i = 0; i < 4; i++)
                    {
                        SyncPos();
                        if (Current == '\0') throw new LexerException("unterminated \\u escape in string (expected 4 hex digits)", startLine, startCol, FileName);
                        if (!Uri.IsHexDigit(Current)) throw new LexerException("invalid \\u escape in string", startLine, startCol, FileName);
                        hex += Current;
                    }
                    text.Append((char)Convert.ToInt32(hex, 16));
                }
                else if (esc == 'x' || esc == 'X')
                {
                    string hex = "";
                    for (int i = 0; i < 2; i++)
                    {
                        SyncPos();
                        if (Current == '\0') throw new LexerException("unterminated \\x escape in string (expected 2 hex digits)", startLine, startCol, FileName);
                        if (!Uri.IsHexDigit(Current))
                            throw new LexerException("invalid \\x escape in string (expected 2 hex digits)", startLine, startCol, FileName);
                        hex += Current;
                    }
                    text.Append((char)Convert.ToInt32(hex, 16));
                }
                else
                {
                    throw new LexerException($"unknown escape '\\{esc}' in string", startLine, startCol, FileName);
                }

                SyncPos();
                continue;
            }

            text.Append(Current);
            SyncPos();
        }

        throw new LexerException("unterminated string literal", startLine, startCol, FileName);
    }

    private InterpolatedStringExpressionTokenPart ReadInterpolatedExpressionPart(int braceLine, int braceCol)
    {
        int exprLine = _line;
        int exprCol = _col;
        int exprStart = _pos;

        Lexer fragmentLexer = new(FileName, _text.Substring(_pos), _line, _col);
        int parenDepth = 0;
        int bracketDepth = 0;
        int braceDepth = 0;
        int? alignmentOffset = null;
        int? formatOffset = null;
        bool sawTopLevelQuestion = false;

        while (true)
        {
            Token token = fragmentLexer.GetNextToken();
            bool isTopLevel = parenDepth == 0 && bracketDepth == 0 && braceDepth == 0;

            switch (token.Type)
            {
                case TokenType.EOF:
                    throw new LexerException("unterminated interpolation expression (expected '}')", braceLine, braceCol, FileName);
                case TokenType.LParen:
                    parenDepth++;
                    break;
                case TokenType.RParen:
                    if (parenDepth > 0)
                        parenDepth--;
                    break;
                case TokenType.LBracket:
                    bracketDepth++;
                    break;
                case TokenType.RBracket:
                    if (bracketDepth > 0)
                        bracketDepth--;
                    break;
                case TokenType.Question:
                    if (isTopLevel && !formatOffset.HasValue)
                        sawTopLevelQuestion = true;
                    break;
                case TokenType.Comma:
                    if (isTopLevel && !alignmentOffset.HasValue && !formatOffset.HasValue)
                        alignmentOffset = token.StartOffset;
                    break;
                case TokenType.Colon:
                    if (isTopLevel && !formatOffset.HasValue)
                    {
                        if (sawTopLevelQuestion && !alignmentOffset.HasValue)
                        {
                            throw new LexerException(
                                "conditional expressions inside interpolated strings must be wrapped in parentheses",
                                exprLine,
                                exprCol,
                                FileName);
                        }

                        formatOffset = token.StartOffset;
                        int formatStartPos = exprStart + formatOffset.Value + 1;
                        int closingBracePos = formatStartPos;
                        while (closingBracePos < _text.Length && _text[closingBracePos] != '}')
                        {
                            if (_text[closingBracePos] == '{')
                            {
                                throw new LexerException(
                                    "interpolation format strings cannot contain '{'",
                                    token.Line,
                                    token.Column,
                                    FileName);
                            }

                            closingBracePos++;
                        }

                        if (closingBracePos >= _text.Length)
                        {
                            throw new LexerException(
                                "unterminated interpolation expression (expected '}')",
                                braceLine,
                                braceCol,
                                FileName);
                        }

                        int exprEndOffset = alignmentOffset ?? formatOffset.Value;
                        string exprText = _text.Substring(exprStart, exprEndOffset).Trim();
                        if (string.IsNullOrWhiteSpace(exprText))
                            throw new LexerException("empty interpolation expression", exprLine, exprCol, FileName);

                        int? alignment = null;
                        if (alignmentOffset.HasValue)
                        {
                            string alignmentText = _text
                                .Substring(exprStart + alignmentOffset.Value + 1, formatOffset.Value - alignmentOffset.Value - 1)
                                .Trim();
                            if (!int.TryParse(alignmentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAlignment))
                            {
                                throw new LexerException(
                                    "invalid interpolation alignment; expected an integer width",
                                    token.Line,
                                    token.Column,
                                    FileName);
                            }

                            alignment = parsedAlignment;
                        }

                        string format = _text.Substring(formatStartPos, closingBracePos - formatStartPos);
                        SyncPos(closingBracePos - exprStart + 1);
                        return new InterpolatedStringExpressionTokenPart(exprText, exprLine, exprCol, alignment, format);
                    }
                    break;
                case TokenType.LBrace:
                    braceDepth++;
                    break;
                case TokenType.RBrace:
                    if (isTopLevel)
                    {
                        int exprEndOffset = alignmentOffset ?? formatOffset ?? token.StartOffset;
                        string exprText = _text.Substring(exprStart, exprEndOffset).Trim();
                        if (string.IsNullOrWhiteSpace(exprText))
                            throw new LexerException("empty interpolation expression", exprLine, exprCol, FileName);

                        int? alignment = null;
                        if (alignmentOffset.HasValue)
                        {
                            int alignmentEndOffset = formatOffset ?? token.StartOffset;
                            string alignmentText = _text
                                .Substring(exprStart + alignmentOffset.Value + 1, alignmentEndOffset - alignmentOffset.Value - 1)
                                .Trim();
                            if (!int.TryParse(alignmentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedAlignment))
                            {
                                throw new LexerException(
                                    "invalid interpolation alignment; expected an integer width",
                                    token.Line,
                                    token.Column,
                                    FileName);
                            }

                            alignment = parsedAlignment;
                        }

                        string? format = null;
                        if (formatOffset.HasValue)
                        {
                            format = _text.Substring(
                                exprStart + formatOffset.Value + 1,
                                token.StartOffset - formatOffset.Value - 1);
                        }

                        SyncPos(token.EndOffset);
                        return new InterpolatedStringExpressionTokenPart(exprText, exprLine, exprCol, alignment, format);
                    }

                    if (braceDepth > 0)
                        braceDepth--;
                    break;
            }
        }
    }

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
                    int blockLine = _line, blockCol = _col;
                    SyncPos(2);
                    bool terminated = false;
                    while (_pos < _text.Length)
                    {
                        if (_text[_pos] == '+' && Peek == '#')
                        {
                            SyncPos(2);
                            terminated = true;
                            break;
                        }
                        SyncPos();
                    }
                    if (!terminated)
                        throw new LexerException("unterminated block comment (expected '+#')", blockLine, blockCol, FileName);
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

            _tokLine = _line;
            _tokCol = _col;
            _tokPos = _pos;

            if (TryLexStringToken(out Token stringToken))
                return stringToken;

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
                            if (Current == '\0') throw new LexerException("unterminated \\u escape in char literal (expected 4 hex digits)", startLine, startCol, FileName);
                            if (!Uri.IsHexDigit(Current)) throw new LexerException("invalid \\u escape in char literal", startLine, startCol, FileName);
                            hex += Current;
                        }
                        ch = (char)Convert.ToInt32(hex, 16);
                    }
                    else if (esc == 'x' || esc == 'X')
                    {
                        string hex = "";
                        for (int i = 0; i < 2; i++)
                        {
                            SyncPos();
                            if (Current == '\0') throw new LexerException("unterminated \\x escape in char literal (expected 2 hex digits)", startLine, startCol, FileName);
                            if (!Uri.IsHexDigit(Current))
                                throw new LexerException("invalid \\x escape in char literal (expected 2 hex digits)", startLine, startCol, FileName);
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
                        System.Text.StringBuilder hexBuilder = new();
                        while (Uri.IsHexDigit(Current) || Current == '_')
                        {
                            if (Current != '_') hexBuilder.Append(Current);
                            SyncPos();
                        }
                        string hex = hexBuilder.ToString();
                        if (hex.Length == 0) throw new LexerException("invalid hex literal", startLine, startCol, FileName);

                        try
                        {
                            BigInteger bi = ParseBaseBigInteger(hex, 16);
                            return MakeToken(TokenType.Number, NarrowIntOrBig(bi));
                        }
                        catch (FormatException)
                        {
                            throw new LexerException($"invalid hex literal '0x{hex}'", startLine, startCol, FileName);
                        }
                    }
                    else if (Current == 'b' || Current == 'B')
                    {
                        SyncPos();
                        System.Text.StringBuilder binBuilder = new();
                        while (Current == '0' || Current == '1' || Current == '_')
                        {
                            if (Current != '_') binBuilder.Append(Current);
                            SyncPos();
                        }
                        string bin = binBuilder.ToString();
                        if (bin.Length == 0) throw new LexerException("invalid binary literal", startLine, startCol, FileName);

                        try
                        {
                            // Parse as an unsigned magnitude (like the hex path). Convert.ToInt64(bin, 2)
                            // would interpret a 64-bit literal's high bit as a sign, so 0b<64×1> wrongly
                            // becomes -1 instead of promoting to BigInteger.
                            BigInteger bi = ParseBaseBigInteger(bin, 2);
                            return MakeToken(TokenType.Number, NarrowIntOrBig(bi));
                        }
                        catch (FormatException)
                        {
                            throw new LexerException($"invalid binary literal '0b{bin}'", startLine, startCol, FileName);
                        }
                    }
                    else if (Current == 'o' || Current == 'O')
                    {
                        SyncPos();
                        System.Text.StringBuilder octBuilder = new();
                        while ((Current >= '0' && Current <= '7') || Current == '_')
                        {
                            if (Current != '_') octBuilder.Append(Current);
                            SyncPos();
                        }
                        string oct = octBuilder.ToString();
                        if (oct.Length == 0) throw new LexerException("invalid octal literal", startLine, startCol, FileName);

                        try
                        {
                            // Parse as an unsigned magnitude (like the hex path) so a 64-bit octal literal
                            // promotes to BigInteger instead of being read as a negative two's-complement value.
                            BigInteger bi = ParseBaseBigInteger(oct, 8);
                            return MakeToken(TokenType.Number, NarrowIntOrBig(bi));
                        }
                        catch (FormatException)
                        {
                            throw new LexerException($"invalid octal literal '0o{oct}'", startLine, startCol, FileName);
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
                System.Text.StringBuilder idBuilder = new();
                while (char.IsLetterOrDigit(Current) || Current == '_') { idBuilder.Append(Current); SyncPos(); }
                string id = idBuilder.ToString();
                if (Keywords.TryGetValue(id, out TokenType keywordType))
                    return MakeToken(keywordType, id);
                else
                    return MakeToken(TokenType.Ident, id);

            }

            if (c == '=' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Eq, "=="); }
            if (c == '!' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Neq, "!="); }
            if (c == '<' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Le, "<="); }
            if (c == '>' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Ge, ">="); }
            // Longest-match: the 3-char merge operators must be tested before the 2-char shifts.
            if (c == '<' && Peek == '<' && _pos + 2 < _text.Length && _text[_pos + 2] == '<') { SyncPos(3); return MakeToken(TokenType.MergeLeft, "<<<"); }
            if (c == '>' && Peek == '>' && _pos + 2 < _text.Length && _text[_pos + 2] == '>') { SyncPos(3); return MakeToken(TokenType.MergeRight, ">>>"); }
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
                _ => throw new LexerException($"unknown character in input: '{c}'", _tokLine, _tokCol, FileName)
            };
        }
        _tokLine = _line;
        _tokCol = _col;
        _tokPos = _pos;
        return MakeToken(TokenType.EOF, "");
    }
}
