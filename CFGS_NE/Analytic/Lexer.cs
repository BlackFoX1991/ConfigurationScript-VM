using System.Globalization;
using System.Text;

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

            if (c == '"')
            {
                SyncPos();
                string s = "";
                while (Current != '"' && Current != '\0') { s += Current; SyncPos(); }
                if (Current != '"') throw new LexerException("unterminated string literal", _line, _col, FileName);
                SyncPos();
                return MakeToken(TokenType.String, s);
            }

            if (c == '\'')
            {
                SyncPos();
                string s = "";
                while (Current != '\'' && Current != '\0') { s += Current; SyncPos(); }
                if (Current != '\'') throw new LexerException("unterminated char literal", _line, _col, FileName);
                SyncPos();
                return MakeToken(TokenType.Char, s);
            }

            if (char.IsDigit(Current))
            {
                int startLine = _line, startCol = _col;
                var sb = new StringBuilder();
                bool hasDot = false;
                bool hasExp = false;

                while (char.IsDigit(Current))
                {
                    sb.Append(Current);
                    SyncPos();
                }

                if (Current == '.' && char.IsDigit(Peek))
                {
                    hasDot = true;
                    sb.Append(Current);
                    SyncPos();
                    while (char.IsDigit(Current))
                    {
                        sb.Append(Current);
                        SyncPos();
                    }
                }

                if (Current == 'e' || Current == 'E')
                {
                    hasExp = true;
                    sb.Append(Current);
                    SyncPos();

                    if (Current == '+' || Current == '-')
                    {
                        sb.Append(Current);
                        SyncPos();
                    }

                    if (!char.IsDigit(Current))
                        throw new LexerException("invalid float exponent (missing digits)", _line, _col, FileName);

                    while (char.IsDigit(Current))
                    {
                        sb.Append(Current);
                        SyncPos();
                    }
                }

                var num = sb.ToString();
                object value;

                if (hasDot || hasExp)
                {
                    if (double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        value = d;
                    else
                        throw new LexerException($"invalid float literal '{num}'", startLine, startCol, FileName);
                }
                else
                {
                    if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                        value = i;
                    else if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
                        value = l;
                    else if (decimal.TryParse(num, NumberStyles.Number, CultureInfo.InvariantCulture, out var m))
                        value = m;
                    else
                        throw new LexerException($"invalid number literal '{num}'", startLine, startCol, FileName);
                }

                return new Token(TokenType.Number, value, startLine, startCol, FileName);
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
                    _ => MakeToken(TokenType.Ident, id)
                };
            }

            if (c == '=' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Eq, "=="); }
            if (c == '!' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Neq, "!="); }
            if (c == '<' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Le, "<="); }
            if (c == '>' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.Ge, ">="); }
            if (c == '<' && Peek == '<') { SyncPos(2); return MakeToken(TokenType.bShiftL, "<<"); }
            if (c == '>' && Peek == '>') { SyncPos(2); return MakeToken(TokenType.BShiftR, ">>"); }
            if (c == '&' && Peek == '&') { SyncPos(2); return MakeToken(TokenType.AndAnd, "&&"); }
            if (c == '|' && Peek == '|') { SyncPos(2); return MakeToken(TokenType.OrOr, "||"); }

            if (c == '+' && Peek == '+') { SyncPos(2); return MakeToken(TokenType.PlusPlus, "++"); }
            if (c == '+' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.PlusAssign, "+="); }

            if (c == '-' && Peek == '-') { SyncPos(2); return MakeToken(TokenType.MinusMinus, "--"); }
            if (c == '-' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.MinusAssign, "-="); }

            if (c == '*' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.StarAssign, "*="); }
            if (c == '/' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.SlashAssign, "/="); }

            if (c == '%' && Peek == '=') { SyncPos(2); return MakeToken(TokenType.ModuloAssign, "%="); }
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
