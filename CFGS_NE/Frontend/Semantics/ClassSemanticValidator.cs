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
            InstanceProperty,
            StaticField,
            StaticMethod,
            StaticProperty,
            StaticEnum,
            StaticClass
        }

        private readonly record struct InheritedMemberInfo(InheritedMemberKind Kind, ClassDeclStmt OwnerDecl, FuncDeclStmt? MethodDecl = null);

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
                    if (!TryFindInheritedMember(compiler, cls, method.RuntimeMemberName, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, method.RuntimeMemberName, InheritedMemberKind.InstanceMethod, inherited, method.Line, method.Col, method.OriginFile);

                    // `init` is the construction hook of its own class: the class header already runs the
                    // base init with the header's base arguments, and `super.init(...)` is an explicit call.
                    // Holding a derived init to the base signature made it impossible for a subclass to take
                    // more constructor parameters and still run construction logic.
                    if (IsConstructorHook(method))
                        continue;

                    ValidateMemberVisibilityCompatibility(cls, method.RuntimeMemberName, InheritedMemberKind.InstanceMethod, inherited, method.Line, method.Col, method.OriginFile);
                    ValidateMethodOverrideShape(cls, method, inherited);
                }

                foreach (PropertyDeclStmt property in cls.Properties)
                {
                    if (!TryFindInheritedMember(compiler, cls, property.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, property.Name, InheritedMemberKind.InstanceProperty, inherited, property.Line, property.Col, property.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, property.Name, InheritedMemberKind.InstanceProperty, inherited, property.Line, property.Col, property.OriginFile);
                    ValidatePropertyOverrideShape(cls, property, inherited);
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
                    if (!TryFindInheritedMember(compiler, cls, staticMethod.RuntimeMemberName, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, staticMethod.RuntimeMemberName, InheritedMemberKind.StaticMethod, inherited, staticMethod.Line, staticMethod.Col, staticMethod.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, staticMethod.RuntimeMemberName, InheritedMemberKind.StaticMethod, inherited, staticMethod.Line, staticMethod.Col, staticMethod.OriginFile);
                    ValidateMethodOverrideShape(cls, staticMethod, inherited);
                }

                foreach (PropertyDeclStmt property in cls.StaticProperties)
                {
                    if (!TryFindInheritedMember(compiler, cls, property.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, property.Name, InheritedMemberKind.StaticProperty, inherited, property.Line, property.Col, property.OriginFile);
                    ValidateMemberVisibilityCompatibility(cls, property.Name, InheritedMemberKind.StaticProperty, inherited, property.Line, property.Col, property.OriginFile);
                    ValidatePropertyOverrideShape(cls, property, inherited);
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
                ValidateConstructorSurfaceCompatibility(cls);

                if (string.IsNullOrWhiteSpace(cls.BaseName))
                    continue;

                if (!compiler.TryResolveBaseClassDecl(cls, out ClassDeclStmt baseClass))
                    continue;

                ClassConstructorSignature baseCtor = GetConstructorSignature(baseClass);
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
                        bool found = kv.Value.IsStatic
                            ? TryFindStaticMethodInHierarchy(compiler, cls, kv.Key, out ClassDeclStmt ownerDecl, out FuncDeclStmt methodDecl, out MemberVisibility visibility)
                            : TryFindInstanceMethodInHierarchy(compiler, cls, kv.Key, out ownerDecl, out methodDecl, out visibility);

                        if (!found)
                        {
                            found = kv.Value.IsStatic
                                ? TryFindExplicitStaticInterfaceMethodInHierarchy(compiler, cls, iface, kv.Key, out ownerDecl, out methodDecl, out visibility)
                                : TryFindExplicitInstanceInterfaceMethodInHierarchy(compiler, cls, iface, kv.Key, out ownerDecl, out methodDecl, out visibility);
                        }

                        if (!found)
                        {
                            if (kv.Value.HasDefaultBody)
                            {
                                AddDefaultInterfaceMethod(cls, kv.Value);
                                continue;
                            }

                            throw new CompilerException(
                                $"class '{cls.Name}' does not implement {(kv.Value.IsStatic ? "static " : string.Empty)}interface method '{kv.Key}' from interface '{iface.Name}'",
                                cls.Line,
                                cls.Col,
                                cls.OriginFile);
                        }

                        if (visibility != MemberVisibility.Public)
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' cannot implement {(kv.Value.IsStatic ? "static " : string.Empty)}interface method '{kv.Key}' from interface '{iface.Name}' with non-public visibility",
                                methodDecl.Line,
                                methodDecl.Col,
                                methodDecl.OriginFile);
                        }

                        if (!MethodShapeRules.HaveCompatibleShapes(kv.Value, methodDecl))
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' implements {(kv.Value.IsStatic ? "static " : string.Empty)}interface method '{kv.Key}' from interface '{iface.Name}' with incompatible arity: expected {MethodShapeRules.DescribeArity(kv.Value)}, got {MethodShapeRules.DescribeArity(methodDecl)}",
                                methodDecl.Line,
                                methodDecl.Col,
                                methodDecl.OriginFile);
                        }
                    }

                    Dictionary<string, InterfacePropertyDecl> propertyContract = compiler.GetOrBuildInterfacePropertyContract(iface);
                    foreach (KeyValuePair<string, InterfacePropertyDecl> kv in propertyContract)
                    {
                        bool found = kv.Value.IsStatic
                            ? TryFindStaticPropertyInHierarchy(compiler, cls, kv.Key, out ClassDeclStmt ownerDecl, out PropertyDeclStmt propertyDecl)
                            : TryFindInstancePropertyInHierarchy(compiler, cls, kv.Key, out ownerDecl, out propertyDecl);

                        if (!found)
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' does not implement {(kv.Value.IsStatic ? "static " : string.Empty)}interface property '{kv.Key}' from interface '{iface.Name}'",
                                cls.Line,
                                cls.Col,
                                cls.OriginFile);
                        }

                        ValidateInterfacePropertyImplementation(cls, iface, kv.Value, ownerDecl, propertyDecl);
                    }

                    Dictionary<string, InterfaceIndexerDecl> indexerContract = compiler.GetOrBuildInterfaceIndexerContract(iface);
                    foreach (KeyValuePair<string, InterfaceIndexerDecl> kv in indexerContract)
                    {
                        if (!TryFindInstanceMethodInHierarchy(compiler, cls, kv.Value.ContractMethodName, out ClassDeclStmt ownerDecl, out FuncDeclStmt methodDecl, out MemberVisibility visibility))
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' does not implement interface index {(kv.Value.IsSetter ? "set" : "get")} contract from interface '{iface.Name}' as method '{kv.Value.ContractMethodName}'",
                                cls.Line,
                                cls.Col,
                                cls.OriginFile);
                        }

                        if (visibility != MemberVisibility.Public)
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' cannot implement interface index {(kv.Value.IsSetter ? "set" : "get")} contract from interface '{iface.Name}' with non-public visibility",
                                methodDecl.Line,
                                methodDecl.Col,
                                methodDecl.OriginFile);
                        }

                        if (!HaveCompatibleIndexerImplementation(kv.Value, methodDecl))
                        {
                            throw new CompilerException(
                                $"class '{cls.Name}' implements interface index {(kv.Value.IsSetter ? "set" : "get")} contract from interface '{iface.Name}' with incompatible arity",
                                methodDecl.Line,
                                methodDecl.Col,
                                methodDecl.OriginFile);
                        }
                    }
                }
            }
        }

        private static void AddDefaultInterfaceMethod(ClassDeclStmt cls, InterfaceMethodDecl method)
        {
            if (method.Body == null)
                return;

            FuncDeclStmt synthesized = new(
                method.Name,
                new List<string>(method.Parameters),
                method.Body,
                method.MinArgs,
                method.RestParameter,
                method.Line,
                method.Col,
                method.OriginFile,
                method.IsAsync,
                new List<FunctionParameterSpec>(method.ParameterSpecs));

            if (method.IsStatic)
            {
                cls.StaticMethods.Add(synthesized);
                cls.StaticMethodVisibility[method.Name] = MemberVisibility.Public;
            }
            else
            {
                cls.Methods.Add(synthesized);
                cls.MethodVisibility[method.Name] = MemberVisibility.Public;
            }
        }

        private static string InheritedMemberKindLabel(InheritedMemberKind kind)
        {
            return kind switch
            {
                InheritedMemberKind.InstanceField => "instance field",
                InheritedMemberKind.InstanceMethod => "instance method",
                InheritedMemberKind.InstanceProperty => "instance property",
                InheritedMemberKind.StaticField => "static field",
                InheritedMemberKind.StaticMethod => "static method",
                InheritedMemberKind.StaticProperty => "static property",
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

            FuncDeclStmt? instanceMethod = cls.Methods.FirstOrDefault(m => string.Equals(m.RuntimeMemberName, name, StringComparison.Ordinal));
            if (instanceMethod != null)
            {
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceMethod, cls, instanceMethod);
                return true;
            }

            if (cls.Properties.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceProperty, cls);
                return true;
            }

            if (cls.StaticFields.ContainsKey(name))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticField, cls);
                return true;
            }

            FuncDeclStmt? staticMethod = cls.StaticMethods.FirstOrDefault(m => string.Equals(m.RuntimeMemberName, name, StringComparison.Ordinal));
            if (staticMethod != null)
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticMethod, cls, staticMethod);
                return true;
            }

            if (cls.StaticProperties.Any(p => string.Equals(p.Name, name, StringComparison.Ordinal)))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticProperty, cls);
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

        private static bool IsConstructorHook(FuncDeclStmt method)
            => string.Equals(method.RuntimeMemberName, "init", StringComparison.Ordinal);

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

        private static void ValidatePropertyOverrideShape(
            ClassDeclStmt derivedClass,
            PropertyDeclStmt derivedProperty,
            InheritedMemberInfo baseMember)
        {
            PropertyDeclStmt? baseProperty = GetPropertyByKind(baseMember.OwnerDecl, derivedProperty.Name, baseMember.Kind);
            if (baseProperty == null)
            {
                throw new CompilerException(
                    $"internal compiler error: missing base property metadata for '{derivedProperty.Name}'",
                    derivedProperty.Line,
                    derivedProperty.Col,
                    derivedProperty.OriginFile);
            }

            foreach (PropertyAccessorKind kind in Enum.GetValues<PropertyAccessorKind>())
            {
                PropertyAccessorDecl? baseAccessor = GetPropertyAccessor(baseProperty, kind);
                PropertyAccessorDecl? derivedAccessor = GetPropertyAccessor(derivedProperty, kind);

                if ((baseAccessor == null) != (derivedAccessor == null))
                {
                    throw new CompilerException(
                        $"incompatible override for property '{derivedProperty.Name}' in class '{derivedClass.Name}': accessor set differs from base class '{baseMember.OwnerDecl.Name}'",
                        derivedProperty.Line,
                        derivedProperty.Col,
                        derivedProperty.OriginFile);
                }

                if (baseAccessor == null || derivedAccessor == null)
                    continue;

                MemberVisibility baseVisibility = GetEffectiveAccessorVisibility(baseProperty, baseAccessor);
                MemberVisibility derivedVisibility = GetEffectiveAccessorVisibility(derivedProperty, derivedAccessor);
                if (VisibilityRank(derivedVisibility) < VisibilityRank(baseVisibility))
                {
                    throw new CompilerException(
                        $"incompatible visibility override for property '{derivedProperty.Name}' accessor '{kind.ToString().ToLowerInvariant()}' in class '{derivedClass.Name}': inherited accessor in base class '{baseMember.OwnerDecl.Name}' is '{VisibilityLabel(baseVisibility)}', override is '{VisibilityLabel(derivedVisibility)}'",
                        derivedProperty.Line,
                        derivedProperty.Col,
                        derivedProperty.OriginFile);
                }
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

        private static PropertyDeclStmt? GetPropertyByKind(ClassDeclStmt decl, string memberName, InheritedMemberKind kind)
        {
            return kind switch
            {
                InheritedMemberKind.InstanceProperty => decl.Properties.FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.Ordinal)),
                InheritedMemberKind.StaticProperty => decl.StaticProperties.FirstOrDefault(p => string.Equals(p.Name, memberName, StringComparison.Ordinal)),
                _ => null
            };
        }

        private static PropertyAccessorDecl? GetPropertyAccessor(PropertyDeclStmt property, PropertyAccessorKind kind)
            => property.Accessors.FirstOrDefault(a => a.Kind == kind);

        private static MemberVisibility GetEffectiveAccessorVisibility(PropertyDeclStmt property, PropertyAccessorDecl accessor)
            => accessor.HasExplicitVisibility ? accessor.Visibility : property.Visibility;

        private static MemberVisibility GetPropertyOverallVisibility(PropertyDeclStmt property)
        {
            MemberVisibility best = MemberVisibility.Private;
            foreach (PropertyAccessorDecl accessor in property.Accessors)
            {
                MemberVisibility effective = GetEffectiveAccessorVisibility(property, accessor);
                if (VisibilityRank(effective) > VisibilityRank(best))
                    best = effective;
            }

            return best;
        }

        private static MemberVisibility GetDeclaredMemberVisibilityByKind(
            ClassDeclStmt decl,
            string memberName,
            InheritedMemberKind kind)
        {
            return kind switch
            {
                InheritedMemberKind.InstanceField => GetOrDefaultVisibility(decl.FieldVisibility, memberName),
                InheritedMemberKind.InstanceMethod => GetOrDefaultVisibility(decl.MethodVisibility, memberName),
                InheritedMemberKind.InstanceProperty => GetPropertyOverallVisibility(
                    decl.Properties.First(p => string.Equals(p.Name, memberName, StringComparison.Ordinal))),
                InheritedMemberKind.StaticField => GetOrDefaultVisibility(decl.StaticFieldVisibility, memberName),
                InheritedMemberKind.StaticMethod => GetOrDefaultVisibility(decl.StaticMethodVisibility, memberName),
                InheritedMemberKind.StaticProperty => GetPropertyOverallVisibility(
                    decl.StaticProperties.First(p => string.Equals(p.Name, memberName, StringComparison.Ordinal))),
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

        private static void ValidateInterfacePropertyImplementation(
            ClassDeclStmt cls,
            InterfaceDeclStmt iface,
            InterfacePropertyDecl contractProperty,
            ClassDeclStmt ownerDecl,
            PropertyDeclStmt propertyDecl)
        {
            ValidateRequiredInterfaceAccessor(cls, iface, contractProperty, ownerDecl, propertyDecl, PropertyAccessorKind.Get, contractProperty.HasGetter);
            ValidateRequiredInterfaceAccessor(cls, iface, contractProperty, ownerDecl, propertyDecl, PropertyAccessorKind.Set, contractProperty.HasSetter);
            ValidateRequiredInterfaceAccessor(cls, iface, contractProperty, ownerDecl, propertyDecl, PropertyAccessorKind.Init, contractProperty.HasInit);
        }

        private static void ValidateRequiredInterfaceAccessor(
            ClassDeclStmt cls,
            InterfaceDeclStmt iface,
            InterfacePropertyDecl contractProperty,
            ClassDeclStmt ownerDecl,
            PropertyDeclStmt propertyDecl,
            PropertyAccessorKind kind,
            bool required)
        {
            if (!required)
                return;

            PropertyAccessorDecl? accessor = GetPropertyAccessor(propertyDecl, kind);
            if (accessor == null)
            {
                throw new CompilerException(
                    $"class '{cls.Name}' does not implement interface property '{contractProperty.Name}' accessor '{kind.ToString().ToLowerInvariant()}' from interface '{iface.Name}'",
                    propertyDecl.Line,
                    propertyDecl.Col,
                    propertyDecl.OriginFile);
            }

            MemberVisibility visibility = GetEffectiveAccessorVisibility(propertyDecl, accessor);
            if (visibility != MemberVisibility.Public)
            {
                throw new CompilerException(
                    $"class '{cls.Name}' cannot implement interface property '{contractProperty.Name}' accessor '{kind.ToString().ToLowerInvariant()}' from interface '{iface.Name}' with non-public visibility",
                    accessor.Line,
                    accessor.Col,
                    accessor.OriginFile);
            }
        }

        private static ClassConstructorSignature GetConstructorSignature(ClassDeclStmt cls)
            => cls.GetEffectiveConstructorSignature(includeImplicitOuter: cls.IsNested);

        private static void ValidateConstructorSurfaceCompatibility(ClassDeclStmt cls)
        {
            if (!cls.HasPrimaryConstructorHeader)
                return;

            FuncDeclStmt? initMethod = cls.GetInitMethod();
            if (initMethod == null)
                return;

            ClassConstructorSignature header = cls.GetPrimaryConstructorSignature();
            if (header.MinArgs == initMethod.MinArgs &&
                header.Parameters.Count == initMethod.Parameters.Count &&
                !string.IsNullOrWhiteSpace(header.RestParameter) == !string.IsNullOrWhiteSpace(initMethod.RestParameter))
            {
                return;
            }

            throw new CompilerException(
                $"class '{cls.Name}' declares a non-empty constructor header with arity {MethodShapeRules.DescribeArity(header.Parameters.Count, header.MinArgs, !string.IsNullOrWhiteSpace(header.RestParameter))}, but init has arity {MethodShapeRules.DescribeArity(initMethod)}",
                initMethod.Line,
                initMethod.Col,
                initMethod.OriginFile);
        }

        private static void ValidateCallArgumentsAgainstSignature(
            IReadOnlyList<Expr> args,
            ClassConstructorSignature signature,
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
                FuncDeclStmt? method = current.Methods.FirstOrDefault(m => string.Equals(m.RuntimeMemberName, methodName, StringComparison.Ordinal));
                if (method != null)
                {
                    ownerDecl = current;
                    methodDecl = method;
                    visibility = GetOrDefaultVisibility(current.MethodVisibility, method.RuntimeMemberName);
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

        private static bool TryFindInstancePropertyInHierarchy(
            Compiler compiler,
            ClassDeclStmt decl,
            string propertyName,
            out ClassDeclStmt ownerDecl,
            out PropertyDeclStmt propertyDecl)
        {
            ClassDeclStmt current = decl;
            while (true)
            {
                PropertyDeclStmt? property = current.Properties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal));
                if (property != null)
                {
                    ownerDecl = current;
                    propertyDecl = property;
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

        private static bool TryFindStaticMethodInHierarchy(
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
                FuncDeclStmt? method = current.StaticMethods.FirstOrDefault(m => string.Equals(m.RuntimeMemberName, methodName, StringComparison.Ordinal));
                if (method != null)
                {
                    ownerDecl = current;
                    methodDecl = method;
                    visibility = GetOrDefaultVisibility(current.StaticMethodVisibility, method.RuntimeMemberName);
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

        private static bool TryFindStaticPropertyInHierarchy(
            Compiler compiler,
            ClassDeclStmt decl,
            string propertyName,
            out ClassDeclStmt ownerDecl,
            out PropertyDeclStmt propertyDecl)
        {
            ClassDeclStmt current = decl;
            while (true)
            {
                PropertyDeclStmt? property = current.StaticProperties.FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.Ordinal));
                if (property != null)
                {
                    ownerDecl = current;
                    propertyDecl = property;
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

        private static bool TryFindExplicitInstanceInterfaceMethodInHierarchy(
            Compiler compiler,
            ClassDeclStmt decl,
            InterfaceDeclStmt iface,
            string methodName,
            out ClassDeclStmt ownerDecl,
            out FuncDeclStmt methodDecl,
            out MemberVisibility visibility)
        {
            ClassDeclStmt current = decl;
            while (true)
            {
                FuncDeclStmt? method = current.Methods.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(m.ExplicitInterfaceName) &&
                    ExplicitInterfaceMatchesContract(compiler, iface, m.ExplicitInterfaceName!) &&
                    string.Equals(m.Name, methodName, StringComparison.Ordinal));
                if (method != null)
                {
                    ownerDecl = current;
                    methodDecl = method;
                    visibility = GetOrDefaultVisibility(current.MethodVisibility, method.RuntimeMemberName);
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

        private static bool TryFindExplicitStaticInterfaceMethodInHierarchy(
            Compiler compiler,
            ClassDeclStmt decl,
            InterfaceDeclStmt iface,
            string methodName,
            out ClassDeclStmt ownerDecl,
            out FuncDeclStmt methodDecl,
            out MemberVisibility visibility)
        {
            ClassDeclStmt current = decl;
            while (true)
            {
                FuncDeclStmt? method = current.StaticMethods.FirstOrDefault(m =>
                    !string.IsNullOrWhiteSpace(m.ExplicitInterfaceName) &&
                    ExplicitInterfaceMatchesContract(compiler, iface, m.ExplicitInterfaceName!) &&
                    string.Equals(m.Name, methodName, StringComparison.Ordinal));
                if (method != null)
                {
                    ownerDecl = current;
                    methodDecl = method;
                    visibility = GetOrDefaultVisibility(current.StaticMethodVisibility, method.RuntimeMemberName);
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

        private static bool ExplicitInterfaceMatchesContract(Compiler compiler, InterfaceDeclStmt iface, string explicitInterfaceName)
            => ExplicitInterfaceMatchesContractCore(compiler, iface, explicitInterfaceName, new HashSet<InterfaceDeclStmt>());

        private static bool ExplicitInterfaceMatchesContractCore(
            Compiler compiler,
            InterfaceDeclStmt iface,
            string explicitInterfaceName,
            HashSet<InterfaceDeclStmt> visited)
        {
            if (!visited.Add(iface))
                return false;

            if (string.Equals(iface.Name, explicitInterfaceName, StringComparison.Ordinal))
                return true;

            if (compiler.Context.InterfaceQualifiedPaths.TryGetValue(iface, out string? qualifiedName) &&
                string.Equals(qualifiedName, explicitInterfaceName, StringComparison.Ordinal))
            {
                return true;
            }

            foreach (string baseName in iface.BaseInterfaces)
            {
                if (compiler.TryResolveBaseInterfaceDecl(iface, baseName, out InterfaceDeclStmt baseIface) &&
                    ExplicitInterfaceMatchesContractCore(compiler, baseIface, explicitInterfaceName, visited))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HaveCompatibleIndexerImplementation(InterfaceIndexerDecl expected, FuncDeclStmt actual)
            => expected.MinArgs == actual.MinArgs &&
               expected.Parameters.Count == actual.Parameters.Count &&
               !string.IsNullOrWhiteSpace(expected.RestParameter) == !string.IsNullOrWhiteSpace(actual.RestParameter);
    }
}
