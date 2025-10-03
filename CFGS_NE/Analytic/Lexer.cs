using CFGS_VM.Analytic.TTypes;
using System.Globalization;
using System.Text;

public class Lexer
{
    public string FileName { get; set; }

    private Token MakeToken(TokenType type, string value)
    {
        return new Token(type, value, _line, _col, FileName);
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

    public Lexer(string name, string text)
    {
        FileName = name;
        _line = 1;
        _col = 1;
        _text = text;
    }

    private char Current => _pos < _text.Length ? _text[_pos] : '\0';

    private char Peek => _pos + 1 < _text.Length ? _text[_pos + 1] : '\0';

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

public sealed class LexerException(string message, int line, int column, string filename) : Exception($"{message}. ( Line : {line}, Column : {column} ) : [Source : '{filename}']");

public class SourceLocation
{
    public string FileName { get; }

    public int Line { get; }

    public int Column { get; }

    public SourceLocation(string file, int line, int column)
    {
        FileName = file;
        Line = line;
        Column = column;
    }
}
