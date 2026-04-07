using CFGS_VM.Analytic.Tree;
using CFGS_VM.Analytic.Tokens;

namespace CFGS_VM.Analytic.Lowering
{
    internal sealed class NamespaceLowerer
    {
        private int _namespaceScopeCounter;

        public List<Stmt> Lower(List<Stmt> statements)
        {
            List<Stmt> lowered = new();
            HashSet<string> knownTopLevelNames = new(StringComparer.Ordinal);

            foreach (Stmt stmt in statements)
            {
                List<Stmt> loweredChunk = LowerTopLevelStatement(stmt, knownTopLevelNames);
                lowered.AddRange(loweredChunk);
                TrackTopLevelNames(loweredChunk, knownTopLevelNames);
            }

            return lowered;
        }

        private List<Stmt> LowerTopLevelStatement(Stmt stmt, HashSet<string> knownTopLevelNames)
        {
            return stmt switch
            {
                NamespaceDeclStmt ns => LowerNamespaceDecl(ns, knownTopLevelNames),
                NamespaceImportAliasStmt nsAlias => BuildNamespaceAliasStmts(nsAlias.Alias, nsAlias.ImportedNames, nsAlias.Line, nsAlias.Col, nsAlias.OriginFile),
                ImportAliasDeclStmt aliasDecl => new List<Stmt>
                {
                    new VarDecl(aliasDecl.LocalName, new VarExpr(aliasDecl.ImportName, aliasDecl.Line, aliasDecl.Col, aliasDecl.OriginFile), aliasDecl.Line, aliasDecl.Col, aliasDecl.OriginFile)
                },
                _ => new List<Stmt> { stmt }
            };
        }

        private List<Stmt> LowerNamespaceDecl(NamespaceDeclStmt stmt, HashSet<string> knownTopLevelNames)
        {
            List<string> parts = stmt.Parts;
            bool declareRoot = !knownTopLevelNames.Contains(parts[0]);
            List<Stmt> namespaceStmts = BuildNamespaceEnsurePathStmts(parts, declareRoot, stmt.Line, stmt.Col, stmt.OriginFile);

            Expr namespaceExpr = BuildQualifiedAccessExpr(parts, stmt.Line, stmt.Col, stmt.OriginFile);
            string namespaceTemp = $"__ns_scope_{_namespaceScopeCounter++}";
            List<Stmt> scopedBody = new()
            {
                new VarDecl(namespaceTemp, namespaceExpr, stmt.Line, stmt.Col, stmt.OriginFile)
            };

            scopedBody.AddRange(stmt.BodyStatements);
            foreach (Stmt bodyStmt in stmt.BodyStatements)
            {
                if (!TryGetNamedTopLevel(bodyStmt, out string? name) || string.IsNullOrWhiteSpace(name))
                    continue;

                IndexExpr target = new(
                    new VarExpr(namespaceTemp, bodyStmt.Line, bodyStmt.Col, bodyStmt.OriginFile),
                    new StringExpr(name, bodyStmt.Line, bodyStmt.Col, bodyStmt.OriginFile),
                    bodyStmt.Line,
                    bodyStmt.Col,
                    bodyStmt.OriginFile);

                scopedBody.Add(new AssignExprStmt(
                    target,
                    new VarExpr(name, bodyStmt.Line, bodyStmt.Col, bodyStmt.OriginFile),
                    bodyStmt.Line,
                    bodyStmt.Col,
                    bodyStmt.OriginFile));
            }

            BlockStmt scopeBlock = new(scopedBody, stmt.Line, stmt.Col, stmt.OriginFile)
            {
                IsNamespaceScope = true
            };

            namespaceStmts.Add(scopeBlock);
            return namespaceStmts;
        }

        private static void TrackTopLevelNames(IEnumerable<Stmt> statements, HashSet<string> knownTopLevelNames)
        {
            foreach (Stmt stmt in statements)
            {
                if (TryGetNamedTopLevel(stmt, out string? name) && !string.IsNullOrWhiteSpace(name))
                    knownTopLevelNames.Add(name);
            }
        }

        private static bool TryGetNamedTopLevel(Stmt stmt, out string? name)
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

        private static List<Stmt> BuildNamespaceAliasStmts(string alias, IEnumerable<string> names, int line, int col, string file)
        {
            List<Stmt> statements = new()
            {
                new VarDecl(alias, new DictExpr(new List<(Expr Key, Expr Value)>(), line, col, file), line, col, file)
            };

            foreach (string name in names.OrderBy(x => x, StringComparer.Ordinal))
            {
                IndexExpr target = new(
                    new VarExpr(alias, line, col, file),
                    new StringExpr(name, line, col, file),
                    line,
                    col,
                    file);

                statements.Add(new AssignExprStmt(target, new VarExpr(name, line, col, file), line, col, file));
            }

            return statements;
        }

        private static Expr BuildQualifiedAccessExpr(IReadOnlyList<string> parts, int line, int col, string file)
        {
            Expr expr = new VarExpr(parts[0], line, col, file);
            for (int i = 1; i < parts.Count; i++)
            {
                expr = new IndexExpr(expr, new StringExpr(parts[i], line, col, file), line, col, file);
            }

            return expr;
        }

        private static List<Stmt> BuildNamespaceEnsurePathStmts(
            IReadOnlyList<string> parts,
            bool declareRoot,
            int line,
            int col,
            string file)
        {
            List<Stmt> statements = new();

            if (declareRoot)
            {
                statements.Add(new VarDecl(
                    parts[0],
                    new DictExpr(new List<(Expr Key, Expr Value)>(), line, col, file),
                    line,
                    col,
                    file));
            }

            Expr current = new VarExpr(parts[0], line, col, file);
            for (int i = 1; i < parts.Count; i++)
            {
                Expr next = new IndexExpr(current, new StringExpr(parts[i], line, col, file), line, col, file);
                Expr ensureExpr = new BinaryExpr(
                    next,
                    TokenType.QQNull,
                    new DictExpr(new List<(Expr Key, Expr Value)>(), line, col, file),
                    line,
                    col,
                    file);

                statements.Add(new AssignExprStmt(next, ensureExpr, line, col, file));
                current = next;
            }

            return statements;
        }
    }
}
