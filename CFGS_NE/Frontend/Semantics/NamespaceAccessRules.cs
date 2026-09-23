using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Analytic.Semantics
{
    /// <summary>
    /// Compile-time rules for script namespaces and namespace import aliases. The resolver binds every
    /// valid member reference to its global binding before compilation, so a member access chain that still
    /// starts at a namespace or an alias here is invalid: bracket access, an unknown member, an inaccessible
    /// member, or a namespace used as a value.
    /// </summary>
    internal sealed class NamespaceAccessRules
    {
        internal enum NamespaceSymbolKind
        {
            Namespace,
            Function,
            Class,
            Interface,
            Enum,
            Variable,
            Constant,
            AliasExport
        }

        private sealed record NamespaceMemberDescriptor(
            string Name,
            string QualifiedPath,
            NamespaceSymbolKind Kind,
            MemberVisibility Visibility,
            string DeclaringNamespacePath);

        private readonly Dictionary<string, Dictionary<string, NamespaceMemberDescriptor>> _namespaceMembersByPath = new(StringComparer.Ordinal);
        private readonly HashSet<string> _knownNamespacePaths = new(StringComparer.Ordinal);
        private readonly HashSet<string> _aliasNames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NamespaceMemberDescriptor> _membersByQualifiedPath = new(StringComparer.Ordinal);

        public void Build(IEnumerable<Stmt> statements)
        {
            _namespaceMembersByPath.Clear();
            _knownNamespacePaths.Clear();
            _aliasNames.Clear();
            _membersByQualifiedPath.Clear();

            foreach (Stmt raw in statements)
            {
                Stmt stmt = raw is ExportStmt exportStmt ? exportStmt.Inner : raw;
                if (stmt is NamespaceImportAliasStmt alias)
                {
                    RegisterAlias(alias);
                    continue;
                }

                if (stmt is not BlockStmt block || !Compiler.TryGetNamespaceScopePath(block, out string namespacePath))
                    continue;

                RegisterNamespaceScope(namespacePath, block);
            }
        }

        public void ValidateExplicitMemberAccess(
            IndexExpr indexExpr,
            ISet<string> currentLocals,
            string? currentNamespacePath)
        {
            if (indexExpr.Target is null ||
                !TryResolveKnownNamespacePathFromExpr(indexExpr.Target, currentLocals, out string namespacePath))
            {
                return;
            }

            if (!indexExpr.IsDotAccess)
            {
                throw new CompilerException(
                    "namespace members require dot access; bracket access is for indexable values",
                    indexExpr.Line,
                    indexExpr.Col,
                    indexExpr.OriginFile);
            }

            if (indexExpr.Index is not StringExpr memberExpr)
                return;

            if (!_namespaceMembersByPath.TryGetValue(namespacePath, out Dictionary<string, NamespaceMemberDescriptor>? members) ||
                !members.TryGetValue(memberExpr.Value, out NamespaceMemberDescriptor? descriptor))
            {
                throw CreateUnknownMemberException(namespacePath, memberExpr.Value, memberExpr);
            }

            if (IsNamespaceMemberAccessible(descriptor, currentNamespacePath))
                return;

            throw CreateInaccessibleMemberException(descriptor, indexExpr);
        }

        public void ValidateQualifiedTypeAccess(
            string? qualifiedTypeName,
            string? currentNamespacePath,
            Node node,
            params NamespaceSymbolKind[] allowedKinds)
        {
            if (string.IsNullOrWhiteSpace(qualifiedTypeName))
                return;

            string[] parts = qualifiedTypeName.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;

            string currentPath = parts[0];
            if (!_knownNamespacePaths.Contains(currentPath))
                return;

            for (int i = 1; i < parts.Length; i++)
            {
                if (!_namespaceMembersByPath.TryGetValue(currentPath, out Dictionary<string, NamespaceMemberDescriptor>? members) ||
                    !members.TryGetValue(parts[i], out NamespaceMemberDescriptor? descriptor))
                {
                    throw CreateUnknownMemberException(currentPath, parts[i], node);
                }

                if (descriptor.Kind == NamespaceSymbolKind.Namespace)
                {
                    currentPath = descriptor.QualifiedPath;
                    continue;
                }

                if (allowedKinds.Length > 0 && !allowedKinds.Contains(descriptor.Kind))
                    return;

                if (IsNamespaceMemberAccessible(descriptor, currentNamespacePath))
                    return;

                throw CreateInaccessibleMemberException(descriptor, node);
            }
        }

        /// <summary>
        /// Rejects an assignment to a namespace member that is not a variable. Functions, classes,
        /// interfaces, and enums are declarations, not storage; a constant keeps its runtime const check.
        /// </summary>
        public void ValidateMemberAssignment(VarExpr target)
        {
            if (target.NamespaceMember is null ||
                !_membersByQualifiedPath.TryGetValue(target.NamespaceMember, out NamespaceMemberDescriptor? descriptor))
            {
                return;
            }

            string? label = descriptor.Kind switch
            {
                NamespaceSymbolKind.Function => "function",
                NamespaceSymbolKind.Class => "class",
                NamespaceSymbolKind.Interface => "interface",
                NamespaceSymbolKind.Enum => "enum",
                _ => null
            };

            if (label is null)
                return;

            throw new CompilerException(
                $"invalid assignment to namespace {label} '{descriptor.QualifiedPath}'; only namespace variables can be assigned",
                target.Line,
                target.Col,
                target.OriginFile);
        }

        private void RegisterNamespaceScope(string namespacePath, BlockStmt block)
        {
            RegisterNamespacePath(namespacePath);

            foreach (Stmt stmt in block.Statements)
            {
                if (!TryGetNamespaceDeclaredMember(stmt, out string? name, out NamespaceSymbolKind kind, out MemberVisibility visibility))
                    continue;

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                AddNamespaceMember(namespacePath, name, $"{namespacePath}.{name}", kind, visibility, namespacePath);
            }
        }

        private void RegisterAlias(NamespaceImportAliasStmt alias)
        {
            _aliasNames.Add(alias.Alias);
            EnsureNamespacePath(alias.Alias);
            foreach (string exportName in alias.ImportedNames)
                AddNamespaceMember(alias.Alias, exportName, exportName, NamespaceSymbolKind.AliasExport, MemberVisibility.Public, alias.Alias);
        }

        private void RegisterNamespacePath(string namespacePath)
        {
            string[] parts = namespacePath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return;

            string currentPath = parts[0];
            EnsureNamespacePath(currentPath);

            for (int i = 1; i < parts.Length; i++)
            {
                string parentPath = currentPath;
                currentPath = $"{currentPath}.{parts[i]}";
                EnsureNamespacePath(currentPath);
                AddNamespaceMember(parentPath, parts[i], currentPath, NamespaceSymbolKind.Namespace, MemberVisibility.Public, parentPath);
            }
        }

        private void EnsureNamespacePath(string namespacePath)
        {
            _knownNamespacePaths.Add(namespacePath);
            if (!_namespaceMembersByPath.ContainsKey(namespacePath))
                _namespaceMembersByPath[namespacePath] = new Dictionary<string, NamespaceMemberDescriptor>(StringComparer.Ordinal);
        }

        private void AddNamespaceMember(
            string namespacePath,
            string name,
            string qualifiedPath,
            NamespaceSymbolKind kind,
            MemberVisibility visibility,
            string declaringNamespacePath)
        {
            EnsureNamespacePath(namespacePath);
            NamespaceMemberDescriptor descriptor = new(name, qualifiedPath, kind, visibility, declaringNamespacePath);
            if (_namespaceMembersByPath[namespacePath].TryAdd(name, descriptor) && kind != NamespaceSymbolKind.AliasExport)
                _membersByQualifiedPath.TryAdd(qualifiedPath, descriptor);
        }

        private static bool TryGetNamespaceDeclaredMember(
            Stmt stmt,
            out string? name,
            out NamespaceSymbolKind kind,
            out MemberVisibility visibility)
        {
            switch (stmt)
            {
                case FuncDeclStmt funcDecl:
                    name = funcDecl.Name;
                    kind = NamespaceSymbolKind.Function;
                    visibility = funcDecl.Visibility;
                    return true;

                case ClassDeclStmt classDecl:
                    name = classDecl.Name;
                    kind = NamespaceSymbolKind.Class;
                    visibility = classDecl.Visibility;
                    return true;

                case InterfaceDeclStmt interfaceDecl:
                    name = interfaceDecl.Name;
                    kind = NamespaceSymbolKind.Interface;
                    visibility = interfaceDecl.Visibility;
                    return true;

                case EnumDeclStmt enumDecl:
                    name = enumDecl.Name;
                    kind = NamespaceSymbolKind.Enum;
                    visibility = enumDecl.Visibility;
                    return true;

                case VarDecl varDecl:
                    name = varDecl.Name;
                    kind = NamespaceSymbolKind.Variable;
                    visibility = varDecl.Visibility;
                    return true;

                case ConstDecl constDecl:
                    name = constDecl.Name;
                    kind = NamespaceSymbolKind.Constant;
                    visibility = constDecl.Visibility;
                    return true;

                default:
                    name = null;
                    kind = default;
                    visibility = MemberVisibility.Public;
                    return false;
            }
        }

        /// <summary>
        /// Returns true when the expression resolves to a pure namespace path or a namespace import alias
        /// (no concrete member leaf), i.e. the expression *is* a namespace rather than a value. Used to
        /// enforce C#-like semantics where a namespace can only prefix a member access and is never itself
        /// a value.
        /// </summary>
        public bool IsNamespacePathExpr(Expr expr, ISet<string> currentLocals)
        {
            return TryResolveKnownNamespacePathFromExpr(expr, currentLocals, out _);
        }

        private bool TryResolveKnownNamespacePathFromExpr(Expr expr, ISet<string> currentLocals, out string namespacePath)
        {
            switch (expr)
            {
                case VarExpr { SourceExpr: null } variableExpr:
                    namespacePath = variableExpr.Name;
                    return !currentLocals.Contains(variableExpr.Name) && _knownNamespacePaths.Contains(namespacePath);

                case IndexExpr { Target: not null, Index: StringExpr segment } indexExpr:
                    if (!TryResolveKnownNamespacePathFromExpr(indexExpr.Target, currentLocals, out string prefix) ||
                        _aliasNames.Contains(prefix))
                    {
                        namespacePath = string.Empty;
                        return false;
                    }

                    namespacePath = $"{prefix}.{segment.Value}";
                    return _knownNamespacePaths.Contains(namespacePath);

                default:
                    namespacePath = string.Empty;
                    return false;
            }
        }

        private CompilerException CreateUnknownMemberException(string namespacePath, string memberName, Node node)
        {
            string container = _aliasNames.Contains(namespacePath)
                ? $"namespace import '{namespacePath}'"
                : $"namespace '{namespacePath}'";

            return new CompilerException(
                $"unknown member '{memberName}' in {container}",
                node.Line,
                node.Col,
                node.OriginFile,
                memberName.Length);
        }

        private static CompilerException CreateInaccessibleMemberException(NamespaceMemberDescriptor descriptor, Node node)
            => new(
                $"inaccessible namespace member '{descriptor.Name}' in namespace '{descriptor.DeclaringNamespacePath}': '{VisibilityLabel(descriptor.Visibility)}' access",
                node.Line,
                node.Col,
                node.OriginFile,
                descriptor.Name.Length);

        private static bool IsNamespaceMemberAccessible(NamespaceMemberDescriptor descriptor, string? currentNamespacePath)
        {
            return descriptor.Visibility switch
            {
                MemberVisibility.Public => true,
                MemberVisibility.Private => string.Equals(currentNamespacePath, descriptor.DeclaringNamespacePath, StringComparison.Ordinal),
                MemberVisibility.Protected => IsSameOrChildNamespace(currentNamespacePath, descriptor.DeclaringNamespacePath),
                _ => true
            };
        }

        private static bool IsSameOrChildNamespace(string? currentNamespacePath, string declaringNamespacePath)
        {
            if (string.IsNullOrWhiteSpace(currentNamespacePath) || string.IsNullOrWhiteSpace(declaringNamespacePath))
                return false;

            return string.Equals(currentNamespacePath, declaringNamespacePath, StringComparison.Ordinal) ||
                currentNamespacePath.StartsWith(declaringNamespacePath + ".", StringComparison.Ordinal);
        }

        private static string VisibilityLabel(MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Private => "private",
                MemberVisibility.Protected => "protected",
                _ => "public"
            };
        }
    }
}
