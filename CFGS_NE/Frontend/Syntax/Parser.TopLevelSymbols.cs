using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Core
{
    public partial class Parser
    {
        /// <summary>
        /// Defines already seen top-level symbols across the current parse/import graph.
        /// </summary>
        private readonly Dictionary<string, (string Kind, string Origin)> _seenTopLevelSymbols = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines known top-level kinds while parsing the current script.
        /// </summary>
        private readonly Dictionary<string, string> _knownTopLevelKinds = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines namespace roots introduced by namespace declarations in the current script.
        /// </summary>
        private readonly HashSet<string> _knownNamespaceRoots = new(StringComparer.Ordinal);

        /// <summary>
        /// The IndexTopLevelSymbols
        /// </summary>
        /// <param name="stmts">The stmts<see cref="IEnumerable{Stmt}"/></param>
        private void IndexTopLevelSymbols(IEnumerable<Stmt> stmts)
        {
            foreach (Stmt s in stmts)
            {
                if (!TopLevelSymbolFacts.TryGetNamedTopLevelWithKind(s, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                string origin = TopLevelSymbolFacts.NormalizeOriginKey(s.OriginFile);
                if (!_seenTopLevelSymbols.ContainsKey(name))
                    _seenTopLevelSymbols[name] = (kind ?? "symbol", origin);
            }
        }

        /// <summary>
        /// The TrackKnownTopLevelSymbols
        /// </summary>
        /// <param name="stmts">The stmts<see cref="IEnumerable{Stmt}"/></param>
        private void TrackKnownTopLevelSymbols(IEnumerable<Stmt> stmts)
        {
            foreach (Stmt s in stmts)
            {
                if (!TopLevelSymbolFacts.TryGetNamedTopLevelWithKind(s, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                if (!_knownTopLevelKinds.ContainsKey(name))
                    _knownTopLevelKinds[name] = kind ?? "symbol";
            }
        }

        /// <summary>
        /// Resets current-script top-level tracking from already seen imported symbols.
        /// </summary>
        private void ResetKnownTopLevelStateFromSeenSymbols()
        {
            _knownTopLevelKinds.Clear();
            _knownNamespaceRoots.Clear();

            foreach (string name in _seenTopLevelSymbols.Keys)
            {
                _knownTopLevelKinds[name] = _seenTopLevelSymbols[name].Kind;
            }
        }

        /// <summary>
        /// The FilterDuplicateTopLevel
        /// </summary>
        /// <param name="stmts">The stmts<see cref="List{Stmt}"/></param>
        /// <param name="allowIdempotentSameOrigin">The allowIdempotentSameOrigin<see cref="bool"/></param>
        /// <returns>The <see cref="List{Stmt}"/></returns>
        private List<Stmt> FilterDuplicateTopLevel(List<Stmt> stmts, bool allowIdempotentSameOrigin = true)
        {
            List<Stmt> filtered = new(stmts.Count);
            Dictionary<string, (string Kind, string Origin)> localSeen = new(StringComparer.Ordinal);
            foreach (Stmt s in stmts)
            {
                if (!TryGetNamedTopLevelWithKind(s, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                {
                    filtered.Add(s);
                    continue;
                }

                string currKind = kind ?? "symbol";
                string currOrigin = TopLevelSymbolFacts.NormalizeOriginKey(s.OriginFile);

                if (localSeen.TryGetValue(name, out (string Kind, string Origin) localPrev))
                    throw new ParserException(TopLevelSymbolFacts.DuplicateTopLevelMessage(name, currKind, localPrev.Kind), s.Line, s.Col, s.OriginFile);

                localSeen[name] = (currKind, currOrigin);

                if (_seenTopLevelSymbols.TryGetValue(name, out (string Kind, string Origin) prev))
                {
                    if (allowIdempotentSameOrigin && string.Equals(prev.Origin, currOrigin, StringComparison.Ordinal))
                        continue;

                    throw new ParserException(TopLevelSymbolFacts.DuplicateTopLevelMessage(name, currKind, prev.Kind), s.Line, s.Col, s.OriginFile);
                }

                filtered.Add(s);
            }

            return filtered;
        }

        /// <summary>
        /// The TryGetNamedTopLevel
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <param name="name">The name<see cref="string?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetNamedTopLevel(Stmt stmt, out string? name)
            => TopLevelSymbolFacts.TryGetNamedTopLevel(stmt, out name);

        /// <summary>
        /// The TryGetNamedTopLevelWithKind
        /// </summary>
        /// <param name="stmt">The stmt<see cref="Stmt"/></param>
        /// <param name="name">The name<see cref="string?"/></param>
        /// <param name="kind">The kind<see cref="string?"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetNamedTopLevelWithKind(Stmt stmt, out string? name, out string? kind)
            => TopLevelSymbolFacts.TryGetNamedTopLevelWithKind(stmt, out name, out kind);

        /// <summary>
        /// The ValidateTopLevelSymbolUniqueness
        /// </summary>
        /// <param name="stmts">The stmts<see cref="IEnumerable{Stmt}"/></param>
        private static void ValidateTopLevelSymbolUniqueness(IEnumerable<Stmt> stmts)
            => TopLevelSymbolFacts.ValidateTopLevelSymbolUniqueness(stmts);
    }
}
