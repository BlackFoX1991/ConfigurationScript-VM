using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class MemberAccessRules
    {
        private readonly record struct ConstructorSignature(List<string> Parameters, int MinArgs, string? RestParameter);
        internal enum MemberAccessKind
        {
            Read,
            Write,
            Init
        }

        public bool TryResolveKnownClassDeclFromPath(Compiler compiler, string classPath, out ClassDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(classPath))
                return false;

            if (compiler.Context.QualifiedClassDecls.TryGetValue(classPath, out decl!))
                return true;

            string[] parts = classPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            if (!compiler.Context.TopLevelClassDecls.TryGetValue(parts[0], out ClassDeclStmt? current))
                return false;

            for (int i = 1; i < parts.Length; i++)
            {
                ClassDeclStmt? nested = current.NestedClasses.FirstOrDefault(c => string.Equals(c.Name, parts[i], StringComparison.Ordinal));
                if (nested == null)
                    return false;

                current = nested;
            }

            decl = current;
            return true;
        }

        public bool TryResolveKnownClassDeclFromExpr(Compiler compiler, Expr expr, ISet<string> currentLocals, out ClassDeclStmt decl)
        {
            decl = null!;

            if (TryExtractQualifiedPath(expr, out string qualifiedPath))
            {
                int rootSep = qualifiedPath.IndexOf('.');
                string root = rootSep >= 0 ? qualifiedPath[..rootSep] : qualifiedPath;
                if (!currentLocals.Contains(root) && compiler.Context.QualifiedClassDecls.TryGetValue(qualifiedPath, out decl!))
                    return true;
            }

            if (expr is VarExpr variableExpr)
            {
                if (currentLocals.Contains(variableExpr.Name))
                    return false;

                return compiler.Context.TopLevelClassDecls.TryGetValue(variableExpr.Name, out decl!);
            }

            if (expr is not IndexExpr { IsDotAccess: true } indexExpr || indexExpr.Index is not StringExpr segment)
                return false;

            if (indexExpr.Target == null)
                return false;

            if (!TryResolveKnownClassDeclFromExpr(compiler, indexExpr.Target, currentLocals, out ClassDeclStmt parent))
                return false;

            ClassDeclStmt? nestedClass = parent.NestedClasses.FirstOrDefault(c => string.Equals(c.Name, segment.Value, StringComparison.Ordinal));
            if (nestedClass == null)
                return false;

            decl = nestedClass;
            return true;
        }

        public (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) GetOrBuildClassMemberSets(Compiler compiler, ClassDeclStmt decl)
        {
            if (compiler.Context.ClassMemberSetCache.TryGetValue(decl, out (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) cached))
                return cached;

            HashSet<string> instanceMembers = new(StringComparer.Ordinal);
            HashSet<string> staticMembers = new(StringComparer.Ordinal);
            HashSet<ClassDeclStmt> visited = new();

            void Collect(ClassDeclStmt current)
            {
                if (!visited.Add(current))
                    return;

                foreach (string name in current.Fields.Keys)
                    instanceMembers.Add(name);
                foreach (FuncDeclStmt method in current.Methods)
                    instanceMembers.Add(method.Name);
                foreach (PropertyDeclStmt property in current.Properties)
                    instanceMembers.Add(property.Name);

                ConstructorSignature constructor = GetConstructorSignature(current);
                foreach (string parameter in constructor.Parameters)
                {
                    if (!string.Equals(parameter, "__outer", StringComparison.Ordinal))
                        instanceMembers.Add(parameter);
                }

                foreach (string name in current.StaticFields.Keys)
                    staticMembers.Add(name);
                foreach (FuncDeclStmt method in current.StaticMethods)
                    staticMembers.Add(method.Name);
                foreach (PropertyDeclStmt property in current.StaticProperties)
                    staticMembers.Add(property.Name);
                foreach (EnumDeclStmt enumDecl in current.Enums)
                    staticMembers.Add(enumDecl.Name);
                foreach (ClassDeclStmt nested in current.NestedClasses)
                    staticMembers.Add(nested.Name);

                if (!string.IsNullOrWhiteSpace(current.BaseName) && compiler.TryResolveBaseClassDecl(current, out ClassDeclStmt baseDecl))
                    Collect(baseDecl);
            }

            Collect(decl);

            (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) built = (instanceMembers, staticMembers);
            compiler.Context.ClassMemberSetCache[decl] = built;
            return built;
        }

        public void ValidateMemberVisibilityAgainstKnownClass(
            Compiler compiler,
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            ClassDeclStmt? currentClassDecl,
            Node node,
            MemberAccessKind accessKind = MemberAccessKind.Read)
        {
            if (TryFindPropertyInHierarchy(compiler, decl, memberName, expectInstance, out ClassDeclStmt propertyOwnerDecl, out PropertyDeclStmt propertyDecl))
            {
                if (!TryGetPropertyAccessorVisibility(propertyDecl, accessKind, out MemberVisibility propertyVisibility, out string accessorLabel))
                {
                    throw new CompilerException(
                        DescribeMissingPropertyAccessor(propertyOwnerDecl, propertyDecl, accessKind),
                        node.Line,
                        node.Col,
                        node.OriginFile,
                        memberName.Length);
                }

                if (IsMemberAccessAllowed(compiler, currentClassDecl, propertyOwnerDecl, propertyVisibility))
                    return;

                throw new CompilerException(
                    $"inaccessible {accessorLabel} for property '{memberName}' in class '{propertyOwnerDecl.Name}': '{VisibilityLabel(propertyVisibility)}' access",
                    node.Line,
                    node.Col,
                    node.OriginFile,
                    memberName.Length);
            }

            if (!TryFindMemberVisibilityInHierarchy(compiler, decl, memberName, expectInstance, out ClassDeclStmt ownerDecl, out MemberVisibility visibility))
                return;

            if (IsMemberAccessAllowed(compiler, currentClassDecl, ownerDecl, visibility))
                return;

            throw new CompilerException(
                $"inaccessible member '{memberName}' in class '{ownerDecl.Name}': '{VisibilityLabel(visibility)}' access",
                node.Line,
                node.Col,
                node.OriginFile,
                memberName.Length);
        }

        public void ValidateMemberAccessAgainstCurrentClass(
            Compiler compiler,
            ClassInfo? currentClass,
            ClassDeclStmt? currentClassDecl,
            string memberName,
            bool expectInstance,
            Node node,
            MemberAccessKind accessKind = MemberAccessKind.Read)
        {
            if (currentClass == null || currentClassDecl == null)
                return;

            bool hasInstance = currentClass.IsInstanceMember(memberName);
            bool hasStatic = currentClass.IsStaticMember(memberName);

            if (expectInstance && !hasInstance)
            {
                if (hasStatic)
                {
                    throw new CompilerException(
                        $"invalid instance member access '{memberName}' in class '{currentClass.Name}': member is static",
                        node.Line,
                        node.Col,
                        node.OriginFile,
                        memberName.Length);
                }

                throw new CompilerException(
                    WithDidYouMean(
                        $"unknown instance member '{memberName}' in class '{currentClass.Name}'",
                        memberName,
                        currentClass.InstanceMembers),
                    node.Line,
                    node.Col,
                    node.OriginFile,
                    memberName.Length);
            }

            if (!expectInstance && !hasStatic)
            {
                if (hasInstance)
                {
                    throw new CompilerException(
                        $"invalid static member access '{memberName}' in class '{currentClass.Name}': member is instance",
                        node.Line,
                        node.Col,
                        node.OriginFile,
                        memberName.Length);
                }

                throw new CompilerException(
                    WithDidYouMean(
                        $"unknown static member '{memberName}' in class '{currentClass.Name}'",
                        memberName,
                        currentClass.StaticMembers),
                    node.Line,
                    node.Col,
                    node.OriginFile,
                    memberName.Length);
            }

            ValidateMemberVisibilityAgainstKnownClass(compiler, currentClassDecl, memberName, expectInstance, currentClassDecl, node, accessKind);
        }

        public void ValidateMemberAccessAgainstKnownClass(
            Compiler compiler,
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            ClassDeclStmt? currentClassDecl,
            Node node,
            MemberAccessKind accessKind = MemberAccessKind.Read)
        {
            (HashSet<string> instanceMembers, HashSet<string> staticMembers) = GetOrBuildClassMemberSets(compiler, decl);
            bool hasInstance = instanceMembers.Contains(memberName);
            bool hasStatic = staticMembers.Contains(memberName);

            if (expectInstance && !hasInstance)
            {
                if (hasStatic)
                {
                    throw new CompilerException(
                        $"invalid instance member access '{memberName}' in class '{decl.Name}': member is static",
                        node.Line,
                        node.Col,
                        node.OriginFile,
                        memberName.Length);
                }

                throw new CompilerException(
                    WithDidYouMean(
                        $"unknown instance member '{memberName}' in class '{decl.Name}'",
                        memberName,
                        instanceMembers),
                    node.Line,
                    node.Col,
                    node.OriginFile,
                    memberName.Length);
            }

            if (!expectInstance && !hasStatic)
            {
                if (hasInstance)
                {
                    throw new CompilerException(
                        $"invalid static member access '{memberName}' in class '{decl.Name}': member is instance",
                        node.Line,
                        node.Col,
                        node.OriginFile,
                        memberName.Length);
                }

                throw new CompilerException(
                    WithDidYouMean(
                        $"unknown static member '{memberName}' in class '{decl.Name}'",
                        memberName,
                        staticMembers),
                    node.Line,
                    node.Col,
                    node.OriginFile,
                    memberName.Length);
            }

            ValidateMemberVisibilityAgainstKnownClass(compiler, decl, memberName, expectInstance, currentClassDecl, node, accessKind);
        }

        public void ValidateNewObjectInitializers(
            Compiler compiler,
            NewExpr newExpr,
            ClassDeclStmt? currentClassDecl)
        {
            bool hasKnownDecl = TryResolveKnownClassDeclFromPath(compiler, newExpr.ClassName, out ClassDeclStmt decl);
            if (hasKnownDecl)
                ValidateMemberVisibilityAgainstKnownClass(compiler, decl, "new", expectInstance: false, currentClassDecl, newExpr, MemberAccessKind.Read);

            if (newExpr.Initializers == null || newExpr.Initializers.Count == 0)
                return;

            foreach ((string name, Expr valueExpr) in newExpr.Initializers)
            {
                if (Compiler.IsReservedRuntimeMemberName(name))
                {
                    throw new CompilerException(
                        $"invalid initializer member '{name}': reserved member name",
                        valueExpr.Line,
                        valueExpr.Col,
                        valueExpr.OriginFile,
                        name.Length);
                }
            }

            if (!hasKnownDecl)
                return;

            (HashSet<string> instanceMembers, HashSet<string> _) = GetOrBuildClassMemberSets(compiler, decl);
            foreach ((string name, Expr valueExpr) in newExpr.Initializers)
            {
                if (!instanceMembers.Contains(name))
                {
                    throw new CompilerException(
                        WithDidYouMean(
                            $"unknown initializer member '{name}' for class '{decl.Name}'",
                            name,
                            instanceMembers),
                        valueExpr.Line,
                        valueExpr.Col,
                        valueExpr.OriginFile,
                        name.Length);
                }

                ValidateMemberVisibilityAgainstKnownClass(compiler, decl, name, expectInstance: true, currentClassDecl, valueExpr, MemberAccessKind.Init);
            }
        }

        private static bool TryExtractQualifiedPath(Expr expr, out string path)
        {
            switch (expr)
            {
                case VarExpr variableExpr:
                    path = variableExpr.Name;
                    return !string.IsNullOrWhiteSpace(path);

                case IndexExpr indexExpr when indexExpr.Target != null && indexExpr.Index is StringExpr segment:
                    if (!TryExtractQualifiedPath(indexExpr.Target, out string prefix))
                    {
                        path = string.Empty;
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(segment.Value))
                    {
                        path = string.Empty;
                        return false;
                    }

                    path = $"{prefix}.{segment.Value}";
                    return true;

                default:
                    path = string.Empty;
                    return false;
            }
        }

        private static ConstructorSignature GetConstructorSignature(ClassDeclStmt cls)
        {
            FuncDeclStmt? initMethod = cls.Methods.FirstOrDefault(m => string.Equals(m.Name, "init", StringComparison.Ordinal));
            List<string> parameters = initMethod != null
                ? new List<string>(initMethod.Parameters)
                : new List<string>(cls.Parameters);

            int minArgs = initMethod != null ? initMethod.MinArgs : parameters.Count;
            string? restParameter = initMethod?.RestParameter;

            if (cls.IsNested && (parameters.Count == 0 || !string.Equals(parameters[0], "__outer", StringComparison.Ordinal)))
            {
                parameters.Insert(0, "__outer");
                minArgs++;
            }

            return new ConstructorSignature(parameters, minArgs, restParameter);
        }

        private static MemberVisibility GetConstructorVisibility(ClassDeclStmt cls)
        {
            if (cls.Methods.Any(m => string.Equals(m.Name, "init", StringComparison.Ordinal)))
                return GetOrDefaultVisibility(cls.MethodVisibility, "init");

            return MemberVisibility.Public;
        }

        private static MemberVisibility GetOrDefaultVisibility(Dictionary<string, MemberVisibility> map, string name)
            => map.TryGetValue(name, out MemberVisibility visibility) ? visibility : MemberVisibility.Public;

        private static string VisibilityLabel(MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Public => "public",
                MemberVisibility.Private => "private",
                MemberVisibility.Protected => "protected",
                _ => "public"
            };
        }

        private static string WithDidYouMean(string message, string name, IEnumerable<string> candidates)
        {
            if (TryFindClosestName(name, candidates, out string? closest))
                return $"{message}; did you mean '{closest}'?";

            return message;
        }

        private static bool TryFindClosestName(string name, IEnumerable<string> candidates, out string? closest)
        {
            closest = null;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            int bestDistance = int.MaxValue;
            foreach (string candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                int distance = EditDistance(name, candidate);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    closest = candidate;
                }
            }

            int threshold = Math.Max(2, name.Length / 3);
            return closest is not null && bestDistance <= threshold;
        }

        private static int EditDistance(string left, string right)
        {
            if (left.Length == 0)
                return right.Length;
            if (right.Length == 0)
                return left.Length;

            int[] previous = new int[right.Length + 1];
            int[] current = new int[right.Length + 1];

            for (int j = 0; j <= right.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= left.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= right.Length; j++)
                {
                    int cost = left[i - 1] == right[j - 1] ? 0 : 1;
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);
                }

                (previous, current) = (current, previous);
            }

            return previous[right.Length];
        }

        private static string DescribeMissingPropertyAccessor(ClassDeclStmt ownerDecl, PropertyDeclStmt propertyDecl, MemberAccessKind accessKind)
        {
            return accessKind switch
            {
                MemberAccessKind.Read => $"property '{propertyDecl.Name}' in class '{ownerDecl.Name}' has no getter",
                MemberAccessKind.Write => $"property '{propertyDecl.Name}' in class '{ownerDecl.Name}' has no setter",
                MemberAccessKind.Init => $"property '{propertyDecl.Name}' in class '{ownerDecl.Name}' has no init or set accessor",
                _ => $"property '{propertyDecl.Name}' in class '{ownerDecl.Name}' cannot be accessed"
            };
        }

        private static bool TryGetPropertyAccessorVisibility(
            PropertyDeclStmt propertyDecl,
            MemberAccessKind accessKind,
            out MemberVisibility visibility,
            out string accessorLabel)
        {
            PropertyAccessorDecl? accessor = accessKind switch
            {
                MemberAccessKind.Read => propertyDecl.Accessors.FirstOrDefault(a => a.Kind == PropertyAccessorKind.Get),
                MemberAccessKind.Write => propertyDecl.Accessors.FirstOrDefault(a => a.Kind == PropertyAccessorKind.Set),
                MemberAccessKind.Init => propertyDecl.Accessors.FirstOrDefault(a => a.Kind == PropertyAccessorKind.Init)
                    ?? propertyDecl.Accessors.FirstOrDefault(a => a.Kind == PropertyAccessorKind.Set),
                _ => null
            };

            if (accessor == null)
            {
                visibility = MemberVisibility.Public;
                accessorLabel = accessKind switch
                {
                    MemberAccessKind.Read => "getter",
                    MemberAccessKind.Write => "setter",
                    MemberAccessKind.Init => "init accessor",
                    _ => "accessor"
                };
                return false;
            }

            visibility = accessor.HasExplicitVisibility ? accessor.Visibility : propertyDecl.Visibility;
            accessorLabel = accessor.Kind switch
            {
                PropertyAccessorKind.Get => "getter",
                PropertyAccessorKind.Set => "setter",
                PropertyAccessorKind.Init => "init accessor",
                _ => "accessor"
            };
            return true;
        }

        private static bool TryFindPropertyInHierarchy(
            Compiler compiler,
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            out ClassDeclStmt ownerDecl,
            out PropertyDeclStmt propertyDecl)
        {
            ClassDeclStmt current = decl;
            while (true)
            {
                PropertyDeclStmt? declared = expectInstance
                    ? current.Properties.FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.Ordinal))
                    : current.StaticProperties.FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.Ordinal));

                if (declared != null)
                {
                    ownerDecl = current;
                    propertyDecl = declared;
                    return true;
                }

                if (!compiler.TryResolveBaseClassDecl(current, out ClassDeclStmt baseDecl))
                    break;

                current = baseDecl;
            }

            ownerDecl = null!;
            propertyDecl = null!;
            return false;
        }

        private static bool TryGetDeclaredMemberVisibility(
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            out MemberVisibility visibility)
        {
            visibility = MemberVisibility.Public;

            if (expectInstance)
            {
                if (decl.Fields.ContainsKey(memberName))
                {
                    visibility = GetOrDefaultVisibility(decl.FieldVisibility, memberName);
                    return true;
                }

                if (decl.Methods.Any(m => string.Equals(m.Name, memberName, StringComparison.Ordinal)))
                {
                    visibility = GetOrDefaultVisibility(decl.MethodVisibility, memberName);
                    return true;
                }

                ConstructorSignature ctor = GetConstructorSignature(decl);
                if (ctor.Parameters.Any(p => string.Equals(p, memberName, StringComparison.Ordinal) && !string.Equals(p, "__outer", StringComparison.Ordinal)))
                {
                    visibility = MemberVisibility.Public;
                    return true;
                }

                return false;
            }

            if (string.Equals(memberName, "new", StringComparison.Ordinal))
            {
                visibility = GetConstructorVisibility(decl);
                return true;
            }

            if (decl.StaticFields.ContainsKey(memberName))
            {
                visibility = GetOrDefaultVisibility(decl.StaticFieldVisibility, memberName);
                return true;
            }

            if (decl.StaticMethods.Any(m => string.Equals(m.Name, memberName, StringComparison.Ordinal)))
            {
                visibility = GetOrDefaultVisibility(decl.StaticMethodVisibility, memberName);
                return true;
            }

            if (decl.Enums.Any(e => string.Equals(e.Name, memberName, StringComparison.Ordinal)))
            {
                visibility = GetOrDefaultVisibility(decl.EnumVisibility, memberName);
                return true;
            }

            if (decl.NestedClasses.Any(c => string.Equals(c.Name, memberName, StringComparison.Ordinal)))
            {
                visibility = GetOrDefaultVisibility(decl.NestedClassVisibility, memberName);
                return true;
            }

            return false;
        }

        private static bool TryFindMemberVisibilityInHierarchy(
            Compiler compiler,
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            out ClassDeclStmt ownerDecl,
            out MemberVisibility visibility)
        {
            ClassDeclStmt current = decl;
            while (true)
            {
                if (TryGetDeclaredMemberVisibility(current, memberName, expectInstance, out visibility))
                {
                    ownerDecl = current;
                    return true;
                }

                if (!compiler.TryResolveBaseClassDecl(current, out ClassDeclStmt baseDecl))
                    break;

                current = baseDecl;
            }

            ownerDecl = null!;
            visibility = MemberVisibility.Public;
            return false;
        }

        private static bool IsSameOrDerivedFrom(Compiler compiler, ClassDeclStmt candidate, ClassDeclStmt baseDecl)
        {
            if (ReferenceEquals(candidate, baseDecl))
                return true;

            ClassDeclStmt current = candidate;
            while (compiler.TryResolveBaseClassDecl(current, out ClassDeclStmt parent))
            {
                if (ReferenceEquals(parent, baseDecl))
                    return true;

                current = parent;
            }

            return false;
        }

        private static bool IsMemberAccessAllowed(
            Compiler compiler,
            ClassDeclStmt? currentClassDecl,
            ClassDeclStmt ownerDecl,
            MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Public => true,
                MemberVisibility.Private => currentClassDecl != null && ReferenceEquals(currentClassDecl, ownerDecl),
                MemberVisibility.Protected => currentClassDecl != null && IsSameOrDerivedFrom(compiler, currentClassDecl, ownerDecl),
                _ => true
            };
        }
    }
}
