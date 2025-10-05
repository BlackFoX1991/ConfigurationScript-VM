using CFGS_VM.Analytic.TTypes;
using System.Globalization;

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
    /// The MakeToken
    /// </summary>
    /// <param name="type">The type<see cref="TokenType"/></param>
    /// <param name="value">The value<see cref="string"/></param>
    /// <returns>The <see cref="Token"/></returns>
    private Token MakeToken(TokenType type, string value)
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
                return new Token(TokenType.String, sb.ToString(), startLine, startCol, FileName);
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
                return new Token(TokenType.Char, ch.ToString(), startLine, startCol, FileName);
            }

            if (char.IsDigit(Current))
            {
                int startLine = _line, startCol = _col;

                System.Text.StringBuilder raw = new();
                bool isFloat = false;
                bool seenDot = false;
                bool seenExp = false;
                bool expectExpSign = false;

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
                        object nval;
                        try { nval = Convert.ToInt64(hex, 16); }
                        catch { nval = decimal.Parse(Convert.ToUInt64(hex, 16).ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture); }
                        return new Token(TokenType.Number, nval, startLine, startCol, FileName);
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
                        return new Token(TokenType.Number, Convert.ToInt64(bin, 2), startLine, startCol, FileName);
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
                        return new Token(TokenType.Number, Convert.ToInt64(oct, 8), startLine, startCol, FileName);
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
                CultureInfo ci = System.Globalization.CultureInfo.InvariantCulture;

                if (isFloat || num.Contains("e") || num.Contains("E") || num.Contains("."))
                {
                    if (double.TryParse(num, System.Globalization.NumberStyles.Float, ci, out double d)) val = d;
                    else if (decimal.TryParse(num, System.Globalization.NumberStyles.Float, ci, out decimal m)) val = m;
                    else throw new LexerException($"invalid number literal '{num}'", startLine, startCol, FileName);
                }
                else
                {
                    if (int.TryParse(num, System.Globalization.NumberStyles.Integer, ci, out int i)) val = i;
                    else if (long.TryParse(num, System.Globalization.NumberStyles.Integer, ci, out long l)) val = l;
                    else if (decimal.TryParse(num, System.Globalization.NumberStyles.Integer, ci, out decimal m)) val = m;
                    else throw new LexerException($"invalid number literal '{num}'", startLine, startCol, FileName);
                }

                return new Token(TokenType.Number, val, startLine, startCol, FileName);
            }

            if (char.IsLetter(c) || c == '_')
            {
                string id = "";
                while (char.IsLetterOrDigit(Current) || Current == '_') { id += Current; SyncPos(); }
                return id switch
                {
                    "var" => MakeToken(TokenType.Var, id),
                    "if" => MakeToken(TokenType.If, id),
                    "else" => MakeToken(TokenType.Else, id),
                    "delete" => MakeToken(TokenType.Delete, id),
                    "while" => MakeToken(TokenType.While, id),
                    "for" => MakeToken(TokenType.For, id),
                    "break" => MakeToken(TokenType.Break, id),
                    "continue" => MakeToken(TokenType.Continue, id),
                    "func" => MakeToken(TokenType.Func, id),
                    "return" => MakeToken(TokenType.Return, id),
                    "match" => MakeToken(TokenType.Match, id),
                    "case" => MakeToken(TokenType.Case, id),
                    "default" => MakeToken(TokenType.Default, id),
                    "try" => MakeToken(TokenType.Try, id),
                    "catch" => MakeToken(TokenType.Catch, id),
                    "finally" => MakeToken(TokenType.Finally, id),
                    "throw" => MakeToken(TokenType.Throw, id),
                    "class" => MakeToken(TokenType.Class, id),
                    "new" => MakeToken(TokenType.New, id),
                    "null" => MakeToken(TokenType.Null, id),
                    "true" => MakeToken(TokenType.True, id),
                    "false" => MakeToken(TokenType.False, id),
                    "import" => MakeToken(TokenType.Import, id),
                    "enum" => MakeToken(TokenType.Enum, id),
                    "from" => MakeToken(TokenType.From, id),
                    "emit" => MakeToken(TokenType.Emit, id),
                    "static" => MakeToken(TokenType.Static, id),
                    _ => MakeToken(TokenType.Ident, id)
                };
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
                _ => throw new LexerException($"unknown character in input: '{c}'", _line, _col, FileName)
            };
        }
        return MakeToken(TokenType.EOF, "");
    }
}

/// <summary>
/// Defines the <see cref="LexerException" />
/// </summary>
public sealed class LexerException(string message, int line, int column, string filename) : Exception($"{message}. ( Line : {line}, Column : {column} ) : [Source : '{filename}']");

/// <summary>
/// Defines the <see cref="SourceLocation" />
/// </summary>
public class SourceLocation
{
    /// <summary>
    /// Gets the FileName
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the Line
    /// </summary>
    public int Line { get; }

    /// <summary>
    /// Gets the Column
    /// </summary>
    public int Column { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceLocation"/> class.
    /// </summary>
    /// <param name="file">The file<see cref="string"/></param>
    /// <param name="line">The line<see cref="int"/></param>
    /// <param name="column">The column<see cref="int"/></param>
    public SourceLocation(string file, int line, int column)
    {
        FileName = file;
        Line = line;
        Column = column;
    }
}
