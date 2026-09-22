using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        private void ParseImportHeaderTolerant(List<Stmt> stmts)
        {
            while (_current.Type == TokenType.Import)
            {
                int attemptIndex = _cursor.CurrentIndex;
                try
                {
                    ParseImportHeader(stmts);
                }
                catch (Exception ex) when (ex is ParserException or LexerException)
                {
                    AddErrorStatement(stmts, ex);
                    RecoverAfterStatementError(attemptIndex);
                }
            }
        }

        private void ParseUseHeaderTolerant(List<Stmt> stmts)
        {
            while (_current.Type == TokenType.Use)
            {
                int attemptIndex = _cursor.CurrentIndex;
                try
                {
                    ParseUseHeader(stmts);
                }
                catch (Exception ex) when (ex is ParserException or LexerException)
                {
                    AddErrorStatement(stmts, ex);
                    RecoverAfterStatementError(attemptIndex);
                }
            }
        }

        private void TryParseTopLevelStatement(List<Stmt> stmts, bool allowFileScopedNamespace)
        {
            // A top-level statement boundary is by definition outside any loop, function, class or nested
            // expression, so every nesting depth must be zero here. Restore that invariant first: it clears
            // any counter a preceding statement leaked before its guard threw (see ParserContext.
            // ResetNestingDepths) so recovered errors cannot cascade into false diagnostics on later,
            // well-formed statements.
            ResetNestingDepths();
            _pendingDeclarations.Clear();

            int attemptIndex = _cursor.CurrentIndex;
            try
            {
                if (_current.Type == TokenType.Import)
                    throw new ParserException(
                        "Invalid import statement. Imports are only allowed in the header of the script",
                        _current.Line,
                        _current.Column,
                        _current.Filename);

                if (_current.Type == TokenType.Use)
                    throw new ParserException(
                        "Invalid use statement. Use directives are only allowed in the header of the script",
                        _current.Line,
                        _current.Column,
                        _current.Filename);

                if (_current.Type == TokenType.Namespace)
                {
                    NamespaceDeclStmt nsStmt = ParseNamespaceDeclStatement(allowFileScopedNamespace);
                    stmts.Add(nsStmt);
                    TrackKnownTopLevelSymbols([nsStmt]);
                    return;
                }

                List<Stmt> parsed = ParseStatementList();
                stmts.AddRange(parsed);
                TrackKnownTopLevelSymbols(parsed);
            }
            catch (Exception ex) when (ex is ParserException or LexerException)
            {
                AddErrorStatement(stmts, ex);
                if (_current.Type is TokenType.Import or TokenType.Use)
                    Advance();
                RecoverAfterStatementError(attemptIndex);
            }
        }

        private void AddErrorStatement(List<Stmt> statements, Exception ex)
        {
            // Every tolerant catch site passes through here; declarators queued by a statement that then
            // failed must not be attached to the next one.
            _pendingDeclarations.Clear();

            ParserException diagnostic = NormalizeDiagnostic(ex);
            _diagnostics.Add(diagnostic);
            statements.Add(new ErrorStmt(diagnostic.RawMessage, diagnostic.Line, diagnostic.Column, diagnostic.Filename));
        }

        private void RecordDiagnostic(Exception ex)
        {
            _diagnostics.Add(NormalizeDiagnostic(ex));
        }

        private static ParserException NormalizeDiagnostic(Exception ex)
        {
            return ex switch
            {
                ParserException parserException => parserException,
                LexerException lexerException => new ParserException(
                    lexerException.RawMessage,
                    lexerException.Line,
                    lexerException.Column,
                    lexerException.Filename,
                    lexerException.SpanLength,
                    lexerException.DiagnosticCode),
                _ => new ParserException(ex.Message, 1, 1, string.Empty, diagnosticCode: "CFGS9000")
            };
        }

        private void FinalizeTopLevelStateTolerant(List<Stmt> stmts)
        {
            try
            {
                ValidateTopLevelSymbolUniqueness(stmts);
            }
            catch (Exception ex) when (ex is ParserException or LexerException)
            {
                RecordDiagnostic(ex);
            }

            try
            {
                IndexTopLevelSymbols(stmts);
            }
            catch (Exception ex) when (ex is ParserException or LexerException)
            {
                RecordDiagnostic(ex);
            }
        }

        /// <summary>
        /// Statement-level panic recovery with a progress guarantee for the tolerant parse loops.
        /// After resynchronizing, when the failed parse attempt consumed no token AND recovery stopped
        /// on that very token (a stray closer like <c>}</c>, or a boundary keyword such as a top-level
        /// <c>if</c>/<c>export</c> that is invalid at exactly this position), the enclosing loop would
        /// retry the identical parse forever, appending one error statement per spin. Skipping the stuck
        /// token trades one extra follow-up diagnostic for guaranteed termination.
        /// </summary>
        /// <param name="cursorIndexBeforeAttempt">The <c>_cursor.CurrentIndex</c> captured before the parse attempt.</param>
        /// <param name="closingToken">An optional closing token the caller's loop terminates on.</param>
        private void RecoverAfterStatementError(int cursorIndexBeforeAttempt, TokenType? closingToken = null)
        {
            RecoverAfterStatementError(closingToken);

            if (_current.Type != TokenType.EOF && _cursor.CurrentIndex == cursorIndexBeforeAttempt)
                _cursor.Advance();
        }

        private void RecoverAfterStatementError(TokenType? closingToken = null)
        {
            while (_current.Type != TokenType.EOF)
            {
                if (_current.Type == TokenType.Semi)
                {
                    _cursor.Advance();
                    return;
                }

                if (closingToken.HasValue && _current.Type == closingToken.Value)
                    return;

                if (_current.Type == TokenType.RBrace)
                    return;

                if (IsHardStatementBoundary(_current.Type))
                    return;

                // Raw cursor advance: the validating Advance() throws on reserved identifiers, and
                // recovery runs inside catch handlers where a second throw would escape the tolerant
                // parse entirely. Skipping garbage must never fail.
                _cursor.Advance();
            }
        }

        private static bool IsHardStatementBoundary(TokenType type)
        {
            return type switch
            {
                TokenType.Var or
                TokenType.Const or
                TokenType.Func or
                TokenType.Async or
                TokenType.Class or
                TokenType.Interface or
                TokenType.Enum or
                TokenType.ForEach or
                TokenType.Try or
                TokenType.Using or
                TokenType.Defer or
                TokenType.Throw or
                TokenType.Return or
                TokenType.Yield or
                TokenType.Delete or
                TokenType.Match or
                TokenType.If or
                TokenType.While or
                TokenType.Do or
                TokenType.Break or
                TokenType.Continue or
                TokenType.For or
                TokenType.Export or
                TokenType.Namespace or
                TokenType.Import or
                TokenType.Use => true,
                _ => false
            };
        }
    }
}
