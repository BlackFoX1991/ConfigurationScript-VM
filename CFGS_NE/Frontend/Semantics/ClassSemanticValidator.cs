using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions;

namespace CFGS_VM.Analytic.Semantics
{
    internal sealed class ClassSemanticValidator
    {
        private enum InheritedMemberKind
        {
            InstanceField,
            InstanceMethod,
            StaticField,
            StaticMethod,
            StaticEnum,
            StaticClass
        }

        private readonly record struct InheritedMemberInfo(InheritedMemberKind Kind, ClassDeclStmt OwnerDecl, FuncDeclStmt? MethodDecl = null);

        private readonly record struct ConstructorSignature(List<string> Parameters, int MinArgs, string? RestParameter);

        public void ValidateAll(Compiler compiler, List<ClassDeclStmt> sortedClasses)
        {
            ValidateInheritanceOverrides(compiler, sortedClasses);
            ValidateBaseConstructorCalls(compiler, sortedClasses);
            ValidateInterfaceImplementations(compiler, sortedClasses);
        }

        public void ValidateInheritanceOverrides(Compiler compiler, List<ClassDeclStmt> sortedClasses)
        {
            foreach (ClassDeclStmt cls in sortedClasses)
            {
                if (string.IsNullOrWhiteSpace(cls.BaseName))
                    continue;

                foreach (KeyValuePair<string, Expr?> field in cls.Fields)
                {
                    if (!TryFindInheritedMember(compiler, cls, field.Key, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, field.Key, InheritedMemberKind.InstanceField, inherited, cls.Line, cls.Col, cls.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, field.Key, InheritedMemberKind.InstanceField, inherited, cls.Line, cls.Col, cls.OriginFile);
                }

                foreach (FuncDeclStmt method in cls.Methods)
                {
                    if (!TryFindInheritedMember(compiler, cls, method.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, method.Name, InheritedMemberKind.InstanceMethod, inherited, method.Line, method.Col, method.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, method.Name, InheritedMemberKind.InstanceMethod, inherited, method.Line, method.Col, method.OriginFile);
                    ValidateMethodOverrideShape(cls, method, inherited);
                }

                foreach (KeyValuePair<string, Expr?> staticField in cls.StaticFields)
                {
                    if (!TryFindInheritedMember(compiler, cls, staticField.Key, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, staticField.Key, InheritedMemberKind.StaticField, inherited, cls.Line, cls.Col, cls.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, staticField.Key, InheritedMemberKind.StaticField, inherited, cls.Line, cls.Col, cls.OriginFile);
                }

                foreach (FuncDeclStmt staticMethod in cls.StaticMethods)
                {
                    if (!TryFindInheritedMember(compiler, cls, staticMethod.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, staticMethod.Name, InheritedMemberKind.StaticMethod, inherited, staticMethod.Line, staticMethod.Col, staticMethod.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, staticMethod.Name, InheritedMemberKind.StaticMethod, inherited, staticMethod.Line, staticMethod.Col, staticMethod.OriginFile);
                    ValidateMethodOverrideShape(cls, staticMethod, inherited);
                }

                foreach (EnumDeclStmt en in cls.Enums)
                {
                    if (!TryFindInheritedMember(compiler, cls, en.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, en.Name, InheritedMemberKind.StaticEnum, inherited, en.Line, en.Col, en.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, en.Name, InheritedMemberKind.StaticEnum, inherited, en.Line, en.Col, en.OriginFile);
                }

                foreach (ClassDeclStmt nested in cls.NestedClasses)
                {
                    if (!TryFindInheritedMember(compiler, cls, nested.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, nested.Name, InheritedMemberKind.StaticClass, inherited, nested.Line, nested.Col, nested.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, nested.Name, InheritedMemberKind.StaticClass, inherited, nested.Line, nested.Col, nested.OriginFile);
                }
            }
        }

        public void ValidateBaseConstructorCalls(Compiler compiler, List<ClassDeclStmt> sortedClasses)
        {
            foreach (ClassDeclStmt cls in sortedClasses)
            {
                if (string.IsNullOrWhiteSpace(cls.BaseName))
                    continue;

                if (!compiler.TryResolveBaseClassDecl(cls, out ClassDeclStmt baseClass))
                    continue;

                ConstructorSignature baseCtor = GetConstructorSignature(baseClass);
                int implicitOuterArgs = 0;
                if (baseClass.IsNested)
                {
                    if (!cls.IsNested)
                    {
                        throw new CompilerException(
                            $"invalid base constructor call in class '{cls.Name}': base class '{baseClass.Name}' is nested and requires an outer instance argument '__outer'",
                            cls.Line,
                            cls.Col,
                            cls.OriginFile);
                    }

                    implicitOuterArgs = 1;
                }

                ValidateCallArgumentsAgainstSignature(
                    cls.BaseCtorArgs,
                    baseCtor,
                    implicitOuterArgs,
                    $"invalid base constructor call in class '{cls.Name}'",
                    cls.Line,
                    cls.Col,
                    cls.OriginFile);
            }
        }

        public void ValidateInterfaceImplementations(Compiler compiler, IEnumerable<ClassDeclStmt> classDecls)
        {
            foreach (ClassDeclStmt cls in classDecls)
            {
                foreach (string ifaceName in cls.ImplementedInterfaces)
                {
                    if (!compiler.TryResolveInterfaceDecl(cls, ifaceName, out InterfaceDeclStmt iface))
                        continue;

                    Dictionary<string, InterfaceMethodDecl> contract = compiler.GetOrBuildInterfaceContract(iface);
                    foreach (KeyValuePair<string, InterfaceMethodDecl> kv in contract)
                    {
                        if (!TryFindInstanceMethodInHierarchy(compiler, cls, kv.Key, out ClassDeclStmt ownerDecl, out FuncDeclStmt methodDecl, out MemberVisibility visibility))
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' does not implement interface method '{kv.Key}' from interface '{iface.Name}'",
                                cls.Line,
                                cls.Col,
                                cls.OriginFile);
                        }

                        if (visibility != MemberVisibility.Public)
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' cannot implement interface method '{kv.Key}' from interface '{iface.Name}' with non-public visibility",
                                methodDecl.Line,
                                methodDecl.Col,
                                methodDecl.OriginFile);
                        }

                        if (!MethodShapeRules.HaveCompatibleShapes(kv.Value, methodDecl))
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' implements interface method '{kv.Key}' from interface '{iface.Name}' with incompatible arity: expected {MethodShapeRules.DescribeArity(kv.Value)}, got {MethodShapeRules.DescribeArity(methodDecl)}",
                                methodDecl.Line,
                                methodDecl.Col,
                                methodDecl.OriginFile);
                        }

                        if (!ReferenceEquals(ownerDecl, cls) && visibility != MemberVisibility.Public)
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' inherits interface method '{kv.Key}' from class '{ownerDecl.Name}' with non-public visibility",
                                methodDecl.Line,
                                methodDecl.Col,
                                methodDecl.OriginFile);
                        }
                    }
                }
            }
        }

        private static string InheritedMemberKindLabel(InheritedMemberKind kind)
        {
            return kind switch
            {
                InheritedMemberKind.InstanceField => "instance field",
                InheritedMemberKind.InstanceMethod => "instance method",
                InheritedMemberKind.StaticField => "static field",
                InheritedMemberKind.StaticMethod => "static method",
                InheritedMemberKind.StaticEnum => "static enum",
                InheritedMemberKind.StaticClass => "static nested class",
                _ => "member"
            };
        }

        private static bool TryFindOwnMember(ClassDeclStmt cls, string name, out InheritedMemberInfo member)
        {
            if (cls.Fields.ContainsKey(name))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceField, cls);
                return true;
            }

            FuncDeclStmt? instanceMethod = cls.Methods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
            if (instanceMethod != null)
            {
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceMethod, cls, instanceMethod);
                return true;
            }

            if (cls.StaticFields.ContainsKey(name))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticField, cls);
                return true;
            }

            FuncDeclStmt? staticMethod = cls.StaticMethods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
            if (staticMethod != null)
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticMethod, cls, staticMethod);
                return true;
            }

            if (cls.Enums.Any(e => string.Equals(e.Name, name, StringComparison.Ordinal)))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticEnum, cls);
                return true;
            }

            if (cls.NestedClasses.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticClass, cls);
                return true;
            }

            member = default;
            return false;
        }

        private static bool TryFindInheritedMember(Compiler compiler, ClassDeclStmt cls, string name, out InheritedMemberInfo member)
        {
            ClassDeclStmt current = cls;
            while (compiler.TryResolveBaseClassDecl(current, out ClassDeclStmt baseClass))
            {
                if (TryFindOwnMember(baseClass, name, out member))
                    return true;

                current = baseClass;
            }

            member = default;
            return false;
        }

        private static void ValidateMethodOverrideShape(
            ClassDeclStmt derivedClass,
            FuncDeclStmt derivedMethod,
            InheritedMemberInfo baseMember)
        {
            FuncDeclStmt baseMethod = baseMember.MethodDecl
                ?? throw new CompilerException(
                    $"internal compiler error: missing base method metadata for '{derivedMethod.Name}'",
                    derivedMethod.Line,
                    derivedMethod.Col,
                    derivedMethod.OriginFile);

            bool baseHasRest = !string.IsNullOrWhiteSpace(baseMethod.RestParameter);
            bool derivedHasRest = !string.IsNullOrWhiteSpace(derivedMethod.RestParameter);

            if (baseHasRest != derivedHasRest ||
                baseMethod.MinArgs != derivedMethod.MinArgs ||
                baseMethod.Parameters.Count != derivedMethod.Parameters.Count)
            {
                throw new CompilerException(
                    $"incompatible override for method '{derivedMethod.Name}' in class '{derivedClass.Name}': expected arity {MethodShapeRules.DescribeArity(baseMethod)} from base class '{baseMember.OwnerDecl.Name}', got {MethodShapeRules.DescribeArity(derivedMethod)}",
                    derivedMethod.Line,
                    derivedMethod.Col,
                    derivedMethod.OriginFile);
            }
        }

        private static int VisibilityRank(MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Private => 0,
                MemberVisibility.Protected => 1,
                _ => 2
            };
        }

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

        private static MemberVisibility GetOrDefaultVisibility(Dictionary<string, MemberVisibility> map, string name)
            => map.TryGetValue(name, out MemberVisibility visibility) ? visibility : MemberVisibility.Public;

        private static MemberVisibility GetDeclaredMemberVisibilityByKind(
            ClassDeclStmt decl,
            string memberName,
            InheritedMemberKind kind)
        {
            return kind switch
            {
                InheritedMemberKind.InstanceField => GetOrDefaultVisibility(decl.FieldVisibility, memberName),
                InheritedMemberKind.InstanceMethod => GetOrDefaultVisibility(decl.MethodVisibility, memberName),
                InheritedMemberKind.StaticField => GetOrDefaultVisibility(decl.StaticFieldVisibility, memberName),
                InheritedMemberKind.StaticMethod => GetOrDefaultVisibility(decl.StaticMethodVisibility, memberName),
                InheritedMemberKind.StaticEnum => GetOrDefaultVisibility(decl.EnumVisibility, memberName),
                InheritedMemberKind.StaticClass => GetOrDefaultVisibility(decl.NestedClassVisibility, memberName),
                _ => MemberVisibility.Public
            };
        }

        private static void ValidateMemberVisibilityCompatibility(
            ClassDeclStmt derivedClass,
            string memberName,
            InheritedMemberKind derivedKind,
            InheritedMemberInfo baseMember,
            int line,
            int col,
            string file)
        {
            MemberVisibility baseVisibility = GetDeclaredMemberVisibilityByKind(baseMember.OwnerDecl, memberName, baseMember.Kind);
            MemberVisibility derivedVisibility = GetDeclaredMemberVisibilityByKind(derivedClass, memberName, derivedKind);

            if (VisibilityRank(derivedVisibility) < VisibilityRank(baseVisibility))
            {
                throw new CompilerException(
                    $"incompatible visibility override for member '{memberName}' in class '{derivedClass.Name}': inherited member in base class '{baseMember.OwnerDecl.Name}' is '{VisibilityLabel(baseVisibility)}', override is '{VisibilityLabel(derivedVisibility)}'",
                    line,
                    col,
                    file);
            }
        }

        private static void ValidateMemberKindCompatibility(
            ClassDeclStmt derivedClass,
            string memberName,
            InheritedMemberKind derivedKind,
            InheritedMemberInfo baseMember,
            int line,
            int col,
            string file)
        {
            if (derivedKind == baseMember.Kind)
                return;

            throw new CompilerException(
                $"invalid override for member '{memberName}' in class '{derivedClass.Name}': declared as {InheritedMemberKindLabel(derivedKind)} but inherited member in base class '{baseMember.OwnerDecl.Name}' is {InheritedMemberKindLabel(baseMember.Kind)}",
                line,
                col,
                file);
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

        private static void ValidateCallArgumentsAgainstSignature(
            IReadOnlyList<Expr> args,
            ConstructorSignature signature,
            int implicitLeadingArgs,
            string contextPrefix,
            int line,
            int col,
            string file)
        {
            if (implicitLeadingArgs < 0 || implicitLeadingArgs > signature.Parameters.Count)
            {
                throw new CompilerException(
                    "internal compiler error: invalid implicit constructor argument count",
                    line,
                    col,
                    file);
            }

            List<string> parameters = signature.Parameters.Skip(implicitLeadingArgs).ToList();
            int minArgs = Math.Max(0, signature.MinArgs - implicitLeadingArgs);

            int positionalCount = 0;
            bool sawNamed = false;
            bool hasSpread = false;
            HashSet<string> namedArgs = new(StringComparer.Ordinal);

            foreach (Expr arg in args)
            {
                if (arg is NamedArgExpr named)
                {
                    sawNamed = true;
                    if (!namedArgs.Add(named.Name))
                    {
                        throw new CompilerException(
                            $"{contextPrefix}: duplicate named argument '{named.Name}'",
                            named.Line,
                            named.Col,
                            named.OriginFile);
                    }

                    continue;
                }

                if (sawNamed)
                {
                    throw new CompilerException(
                        $"{contextPrefix}: positional argument cannot follow named arguments",
                        arg.Line,
                        arg.Col,
                        arg.OriginFile);
                }

                positionalCount++;
                if (arg is SpreadArgExpr)
                    hasSpread = true;
            }

            int restIndex = -1;
            string? restParameter = signature.RestParameter;
            if (!string.IsNullOrWhiteSpace(restParameter))
            {
                restIndex = parameters.FindIndex(p => string.Equals(p, restParameter, StringComparison.Ordinal));
                if (restIndex < 0)
                {
                    throw new CompilerException(
                        "internal compiler error: invalid constructor rest-parameter metadata",
                        line,
                        col,
                        file);
                }
            }

            int fixedCount = restIndex >= 0 ? restIndex : parameters.Count;
            Dictionary<string, int> paramIndex = new(StringComparer.Ordinal);
            for (int i = 0; i < fixedCount; i++)
                paramIndex[parameters[i]] = i;

            foreach (string namedArg in namedArgs)
            {
                if (!string.IsNullOrWhiteSpace(restParameter) && string.Equals(namedArg, restParameter, StringComparison.Ordinal))
                {
                    throw new CompilerException(
                        $"{contextPrefix}: rest parameter '{namedArg}' cannot be passed as named argument",
                        line,
                        col,
                        file);
                }

                if (!paramIndex.ContainsKey(namedArg))
                {
                    throw new CompilerException(
                        $"{contextPrefix}: unknown named argument '{namedArg}'",
                        line,
                        col,
                        file);
                }
            }

            if (restIndex < 0 && positionalCount > fixedCount)
            {
                throw new CompilerException(
                    $"{contextPrefix}: too many args for call (expected {fixedCount}, got {positionalCount})",
                    line,
                    col,
                    file);
            }

            if (hasSpread)
                return;

            int requiredCount = Math.Min(minArgs, fixedCount);
            for (int i = 0; i < requiredCount; i++)
            {
                string paramName = parameters[i];
                bool providedByPosition = i < positionalCount;
                bool providedByName = namedArgs.Contains(paramName);
                if (!providedByPosition && !providedByName)
                {
                    throw new CompilerException(
                        $"{contextPrefix}: insufficient args for call (expected at least {minArgs})",
                        line,
                        col,
                        file);
                }
            }
        }

        private static bool TryFindInstanceMethodInHierarchy(
            Compiler compiler,
            ClassDeclStmt decl,
            string methodName,
            out ClassDeclStmt ownerDecl,
            out FuncDeclStmt methodDecl,
            out MemberVisibility visibility)
        {
            ClassDeclStmt current = decl;
            while (true)
            {
                FuncDeclStmt? method = current.Methods.FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.Ordinal));
                if (method != null)
                {
                    ownerDecl = current;
                    methodDecl = method;
                    visibility = GetOrDefaultVisibility(current.MethodVisibility, methodName);
                    return true;
                }

                if (!compiler.TryResolveBaseClassDecl(current, out ClassDeclStmt baseDecl))
                    break;

                current = baseDecl;
            }

            ownerDecl = null!;
            methodDecl = null!;
            visibility = MemberVisibility.Public;
            return false;
        }
    }
}
