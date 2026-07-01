using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Modules
{
    /// <summary>
    /// Cacht die geparsten Syntax-Statements importierter Module ueber mehrere Analysen hinweg.
    /// Der Schluessel ist Pfad + Inhalts-Hash, daher ist der Cache selbst-invalidierend: aendert sich
    /// der Inhalt einer Datei, entsteht ein neuer Hash und es wird neu geparst; unveraenderte Importe
    /// ueberspringen den teuren Lex-/Parse-Schritt.
    ///
    /// Sicherheit: Ein reiner Parse haengt ausschliesslich vom Inhalt DIESER Datei ab (keine transitive
    /// Abhaengigkeit), und die nachgelagerten Passes (Import-Materialisierung, Lowering, Semantik,
    /// Codegen) sind funktional bzw. lesen den Syntaxbaum nur - sie mutieren die Knoten nicht. Daher
    /// duerfen dieselben AST-Instanzen ueber Analysen hinweg wiederverwendet werden.
    /// </summary>
    public sealed class ModuleSyntaxCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _bySource =
            new(StringComparer.OrdinalIgnoreCase);

        public List<Stmt> GetOrParse(string sourceName, string sourceText, Func<List<Stmt>> parse)
        {
            string contentHash = ComputeHash(sourceText);

            if (_bySource.TryGetValue(sourceName, out CacheEntry existing) &&
                string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal))
            {
                return existing.SyntaxStatements;
            }

            List<Stmt> parsed = parse();
            // Nur der jeweils aktuellste Stand pro Datei wird behalten -> begrenzt den Speicher auf die
            // Anzahl der Dateien und verwirft alte Inhalte automatisch.
            _bySource[sourceName] = new CacheEntry(contentHash, parsed);
            return parsed;
        }

        private static string ComputeHash(string sourceText)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sourceText));
            return Convert.ToHexString(hash);
        }

        private readonly record struct CacheEntry(string ContentHash, List<Stmt> SyntaxStatements);
    }
}
