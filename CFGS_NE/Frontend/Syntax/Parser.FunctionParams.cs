using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using System.Text;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        private sealed record ParsedFunctionParam(
            string Name,
            MatchPattern? DestructurePattern,
            Expr? DefaultValue,
            bool IsRest,
            int Line,
            int Col,
            string File);

        /// <summary>
        /// The IsReservedBindingName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedBindingName(string name)
        {
            if (name.StartsWith("__", StringComparison.Ordinal))
                return true;

            return name == "this" || name == "type" || name == "super" || name == "outer";
        }

        /// <summary>
        /// The ThrowIfInvalidParameterName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
        private static void ThrowIfInvalidParameterName(string name, int line, int col, string file)
        {
            if (Lexer.Keywords.ContainsKey(name) || IsReservedBindingName(name))
                throw new ParserException($"invalid parameter name '{name}'", line, col, file);
        }

        /// <summary>
        /// The ParseFunctionParamsWithDefaults
        /// </summary>
        /// <param name="requireParens">The requireParens<see cref="bool"/></param>
        /// <param name="allowTrailingComma">The allowTrailingComma<see cref="bool"/></param>
        /// <returns>The <see cref="(List{string}, int, string?, List{FunctionParameterSpec})"/></returns>
        private (List<string> Parameters, int MinArgs, string? RestParameter, List<FunctionParameterSpec> ParameterSpecs) ParseFunctionParamsWithDefaults(
            bool requireParens = true,
            bool allowTrailingComma = false)
        {
            if (_current.Type != TokenType.LParen)
                return (new List<string>(), 0, null, new List<FunctionParameterSpec>());

            if (requireParens || _current.Type == TokenType.LParen)
                Eat(TokenType.LParen);
            else
                return (new List<string>(), 0, null, new List<FunctionParameterSpec>());

            List<ParsedFunctionParam> parsed = new();
            HashSet<string> seen = new(StringComparer.Ordinal);
            HashSet<string> seenBindings = new(StringComparer.Ordinal);

            if (_current.Type == TokenType.RParen)
            {
                Eat(TokenType.RParen);
                return (new List<string>(), 0, null, new List<FunctionParameterSpec>());
            }

            bool sawDefault = false;
            bool sawRest = false;
            string? restParameter = null;

            while (true)
            {
                if (_current.Type == TokenType.Star)
                {
                    if (sawRest)
                        throw new ParserException("duplicate rest parameter", _current.Line, _current.Column, _current.Filename);

                    int restLine = _current.Line;
                    int restCol = _current.Column;
                    string restFile = _current.Filename;
                    Eat(TokenType.Star);

                    if (_current.Type != TokenType.Ident)
                        throw new ParserException("expected identifier after '*' in rest parameter", _current.Line, _current.Column, _current.Filename);

                    string restName = _current.Value?.ToString()
                        ?? throw new ParserException("invalid rest parameter name", _current.Line, _current.Column, _current.Filename);

                    ThrowIfInvalidParameterName(restName, _current.Line, _current.Column, _current.Filename);

                    if (!seen.Add(restName))
                        throw new ParserException($"duplicate parameter name '{restName}'", _current.Line, _current.Column, _current.Filename);
                    if (!seenBindings.Add(restName))
                        throw new ParserException($"duplicate parameter name '{restName}'", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Ident);

                    if (_current.Type == TokenType.Assign)
                        throw new ParserException("rest parameter cannot have a default value", _current.Line, _current.Column, _current.Filename);

                    parsed.Add(new ParsedFunctionParam(restName, null, null, true, restLine, restCol, restFile));
                    restParameter = restName;
                    sawRest = true;

                    if (_current.Type == TokenType.Comma)
                        throw new ParserException("rest parameter must be the last parameter", _current.Line, _current.Column, _current.Filename);

                    if (_current.Type != TokenType.RParen)
                        throw new ParserException($"invalid token {_current.Type} in parameters", _current.Line, _current.Column, _current.Filename);

                    break;
                }

                if (_current.Type != TokenType.Ident && _current.Type != TokenType.LBracket && _current.Type != TokenType.LBrace)
                    throw new ParserException($"invalid token {_current.Type} in parameters", _current.Line, _current.Column, _current.Filename);

                int pLine = _current.Line;
                int pCol = _current.Column;
                string pFile = _current.Filename;

                string paramName;
                MatchPattern? destructPattern = null;
                List<string> destructBindings = new();

                if (_current.Type == TokenType.Ident)
                {
                    string name = _current.Value?.ToString()
                                  ?? throw new ParserException("invalid parameter name", _current.Line, _current.Column, _current.Filename);

                    ThrowIfInvalidParameterName(name, _current.Line, _current.Column, _current.Filename);

                    if (!seen.Add(name))
                        throw new ParserException($"duplicate parameter name '{name}'", _current.Line, _current.Column, _current.Filename);
                    if (!seenBindings.Add(name))
                        throw new ParserException($"duplicate parameter name '{name}'", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Ident);
                    paramName = name;
                }
                else
                {
                    destructPattern = ParseDestructurePattern();
                    ValidateUniqueDestructureBindings(destructPattern);
                    destructBindings = CollectDestructureBindingNames(destructPattern);
                    foreach (string b in destructBindings)
                    {
                        ThrowIfInvalidParameterName(b, pLine, pCol, pFile);
                        if (!seenBindings.Add(b))
                            throw new ParserException($"duplicate parameter name '{b}'", pLine, pCol, pFile);
                    }

                    paramName = _context.AllocateDestructureParameterName();
                    while (!seen.Add(paramName))
                        paramName = _context.AllocateDestructureParameterName();
                }

                Expr? defaultValue = null;
                if (_current.Type == TokenType.Assign)
                {
                    Eat(TokenType.Assign);
                    defaultValue = Expr();
                    sawDefault = true;
                }
                else if (sawDefault)
                {
                    throw new ParserException("non-default parameter cannot follow a default parameter", _current.Line, _current.Column, _current.Filename);
                }

                parsed.Add(new ParsedFunctionParam(paramName, destructPattern, defaultValue, false, pLine, pCol, pFile));

                if (_current.Type == TokenType.Comma)
                {
                    Eat(TokenType.Comma);
                    if (allowTrailingComma && _current.Type == TokenType.RParen)
                        break;
                    continue;
                }

                if (_current.Type == TokenType.RParen)
                    break;

                throw new ParserException($"invalid token {_current.Type} in parameters", _current.Line, _current.Column, _current.Filename);
            }

            Eat(TokenType.RParen);

            List<string> parameterNames = parsed.Select(p => p.Name).ToList();
            int minArgs = parsed.Count(p => !p.IsRest && p.DefaultValue == null);
            List<FunctionParameterSpec> parameterSpecs = parsed
                .Select(p => new FunctionParameterSpec(p.Name, p.DestructurePattern, p.DefaultValue, p.IsRest, p.Line, p.Col, p.File))
                .ToList();
            return (parameterNames, minArgs, restParameter, parameterSpecs);
        }

        /// <summary>
        /// The ParseParams
        /// </summary>
        /// <param name="requireParens">The requireParens<see cref="bool"/></param>
        /// <param name="allowTrailingComma">The allowTrailingComma<see cref="bool"/></param>
        /// <returns>The <see cref="List{string}"/></returns>
        private List<string> ParseParams(bool requireParens = true, bool allowTrailingComma = false)
        {
            List<string> parameters = new();
            if (_current.Type != TokenType.LParen) return parameters;
            HashSet<string> seen = new(StringComparer.Ordinal);

            if (requireParens || _current.Type == TokenType.LParen)
                Eat(TokenType.LParen);
            else
                return parameters;

            if (_current.Type == TokenType.RParen)
            {
                Eat(TokenType.RParen);
                return parameters;
            }

            bool expectParam = true;

            while (true)
            {
                if (_current.Type == TokenType.Ident)
                {
                    if (!expectParam)
                        throw new ParserException("Erwarte ',' oder ')'", _current.Line, _current.Column, _current.Filename);

                    string name = _current.Value?.ToString()
                                  ?? throw new ParserException("invalid parameter name", _current.Line, _current.Column, _current.Filename);

                    ThrowIfInvalidParameterName(name, _current.Line, _current.Column, _current.Filename);

                    if (!seen.Add(name))
                        throw new ParserException($"duplicate parameter name '{name}'", _current.Line, _current.Column, _current.Filename);

                    parameters.Add(name);
                    Eat(TokenType.Ident);
                    expectParam = false;
                }
                else if (_current.Type == TokenType.Comma)
                {
                    if (expectParam)
                        throw new ParserException("Expected parameter before ','", _current.Line, _current.Column, _current.Filename);

                    Eat(TokenType.Comma);

                    if (allowTrailingComma && _current.Type == TokenType.RParen)
                    {
                        expectParam = false;
                        break;
                    }

                    expectParam = true;
                }
                else if (_current.Type == TokenType.RParen)
                {
                    if (expectParam && parameters.Count > 0)
                        throw new ParserException("Expected parameter after ','", _current.Line, _current.Column, _current.Filename);

                    break;
                }
                else
                {
                    throw new ParserException($"invalid token {_current.Type} in parameters",
                                               _current.Line, _current.Column, _current.Filename);
                }
            }

            Eat(TokenType.RParen);
            return parameters;
        }
    }
}
