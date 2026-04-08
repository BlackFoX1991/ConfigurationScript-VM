using CFGS_VM.Analytic.Ex;
using CFGS_VM.Analytic.Tree;

namespace CFGS_VM.Analytic.Semantics
{
    internal static class TopLevelSymbolFacts
    {
        public static bool TryGetNamedTopLevel(Stmt stmt, out string? name)
        {
            if (stmt is ExportStmt ex)
                return TryGetNamedTopLevel(ex.Inner, out name);

            switch (stmt)
            {
                case FuncDeclStmt f:
                    name = f.Name;
                    return true;
                case ClassDeclStmt c:
                    name = c.Name;
                    return true;
                case InterfaceDeclStmt i:
                    name = i.Name;
                    return true;
                case EnumDeclStmt e:
                    name = e.Name;
                    return true;
                case NamespaceDeclStmt ns:
                    name = ns.Parts.Count > 0 ? ns.Parts[0] : null;
                    return !string.IsNullOrWhiteSpace(name);
                case NamespaceImportAliasStmt nsAlias:
                    name = nsAlias.Alias;
                    return true;
                case ImportAliasDeclStmt importAlias:
                    name = importAlias.LocalName;
                    return true;
                case VarDecl v:
                    name = v.Name;
                    return true;
                case ConstDecl cst:
                    name = cst.Name;
                    return true;
                default:
                    name = null;
                    return false;
            }
        }

        public static bool TryGetNamedTopLevelWithKind(Stmt stmt, out string? name, out string? kind)
        {
            if (stmt is ExportStmt ex)
                return TryGetNamedTopLevelWithKind(ex.Inner, out name, out kind);

            switch (stmt)
            {
                case FuncDeclStmt f:
                    name = f.Name;
                    kind = "function";
                    return true;
                case ClassDeclStmt c:
                    name = c.Name;
                    kind = "class";
                    return true;
                case InterfaceDeclStmt i:
                    name = i.Name;
                    kind = "interface";
                    return true;
                case EnumDeclStmt e:
                    name = e.Name;
                    kind = "enum";
                    return true;
                case NamespaceDeclStmt ns:
                    name = ns.Parts.Count > 0 ? ns.Parts[0] : null;
                    kind = "namespace";
                    return !string.IsNullOrWhiteSpace(name);
                case NamespaceImportAliasStmt nsAlias:
                    name = nsAlias.Alias;
                    kind = "variable";
                    return true;
                case ImportAliasDeclStmt importAlias:
                    name = importAlias.LocalName;
                    kind = "variable";
                    return true;
                case VarDecl v:
                    name = v.Name;
                    kind = "variable";
                    return true;
                case ConstDecl cst:
                    name = cst.Name;
                    kind = "constant";
                    return true;
                default:
                    name = null;
                    kind = null;
                    return false;
            }
        }

        public static string NamespaceRootConflictLabel(string kind)
        {
            return kind switch
            {
                "namespace" => "namespace",
                "function" => "function",
                "class" => "class",
                "interface" => "interface",
                "enum" => "enum",
                "constant" => "constant",
                "variable" => "variable",
                _ => "symbol",
            };
        }

        public static string NormalizeOriginKey(string origin)
        {
            if (string.IsNullOrWhiteSpace(origin))
                return string.Empty;

            try
            {
                if (Path.IsPathRooted(origin))
                {
                    string full = Path.GetFullPath(origin);
                    return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
                }
            }
            catch (ArgumentException) { }
            catch (IOException) { }

            return origin;
        }

        public static string DuplicateTopLevelMessage(string name, string currentKind, string existingKind)
        {
            if (currentKind == "namespace" && existingKind == "namespace")
                return $"duplicate namespace root '{name}'";

            if (!string.Equals(currentKind, existingKind, StringComparison.Ordinal))
                return $"duplicate symbol '{name}'";

            return currentKind switch
            {
                "function" => $"duplicate function '{name}'",
                "class" => $"duplicate class '{name}'",
                "interface" => $"duplicate interface '{name}'",
                "enum" => $"duplicate enum '{name}'",
                "namespace" => $"duplicate namespace root '{name}'",
                "constant" => $"duplicate constant '{name}'",
                "variable" => $"duplicate variable '{name}'",
                _ => $"duplicate symbol '{name}'",
            };
        }

        public static void ValidateTopLevelSymbolUniqueness(IEnumerable<Stmt> stmts)
        {
            Dictionary<string, string> seenKinds = new(StringComparer.Ordinal);
            foreach (Stmt stmt in stmts)
            {
                if (!TryGetNamedTopLevelWithKind(stmt, out string? name, out string? kind) || string.IsNullOrWhiteSpace(name))
                    continue;

                string currentKind = kind ?? "symbol";
                if (seenKinds.TryGetValue(name, out string? previousKind))
                {
                    if (currentKind == "namespace" && previousKind == "namespace")
                        continue;

                    throw new ParserException(DuplicateTopLevelMessage(name, currentKind, previousKind), stmt.Line, stmt.Col, stmt.OriginFile);
                }

                seenKinds[name] = currentKind;
            }
        }
    }
}
