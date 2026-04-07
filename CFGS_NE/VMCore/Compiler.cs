using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using System.Numerics;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Defines the <see cref="Compiler" />
    /// </summary>
    public partial class Compiler(string fname)
    {
        /// <summary>
        /// Defines the emission context
        /// </summary>
        private readonly BytecodeEmissionContext _emission = new();

        /// <summary>
        /// Defines the compilation context
        /// </summary>
        private readonly CompilationContext _context = new(fname);

        /// <summary>
        /// Gets or sets the FileName
        /// </summary>
        public string FileName
        {
            get => _context.FileName;
            set => _context.FileName = value;
        }

        /// <summary>
        /// Defines the _insns
        /// </summary>
        private List<Instruction> _insns => _context.Instructions;

        /// <summary>
        /// Defines the ImplicitMemberResolutionKind
        /// </summary>
        private enum ImplicitMemberResolutionKind
        {
            None,
            Instance,
            Static,
            Ambiguous
        }

        /// <summary>
        /// Defines the InheritedMemberKind
        /// </summary>
        private enum InheritedMemberKind
        {
            InstanceField,
            InstanceMethod,
            StaticField,
            StaticMethod,
            StaticEnum,
            StaticClass
        }

        /// <summary>
        /// Defines the InheritedMemberInfo
        /// </summary>
        private readonly record struct InheritedMemberInfo(InheritedMemberKind Kind, ClassDeclStmt OwnerDecl, FuncDeclStmt? MethodDecl = null);

        /// <summary>
        /// Defines the ConstructorSignature
        /// </summary>
        private readonly record struct ConstructorSignature(List<string> Parameters, int MinArgs, string? RestParameter);

        /// <summary>
        /// Defines reserved runtime member names.
        /// </summary>
        private static readonly HashSet<string> ReservedRuntimeMemberNames = new(StringComparer.Ordinal)
        {
            "__type",
            "__base",
            "__interfaces",
            "__is_interface",
            "__outer",
            "new"
        };

        /// <summary>
        /// Defines the _breakLists
        /// </summary>
        private Stack<List<LoopLeavePatch>> _breakLists => _emission.BreakLists;

        /// <summary>
        /// Defines the _continueLists
        /// </summary>
        private Stack<List<LoopLeavePatch>> _continueLists => _emission.ContinueLists;

        /// <summary>
        /// Gets the Functions
        /// </summary>
        public Dictionary<string, FunctionInfo> Functions => _context.Functions;

        /// <summary>
        /// Defines the _classInfos
        /// </summary>
        private Dictionary<ClassDeclStmt, ClassInfo> _classInfos => _context.ClassInfos;

        /// <summary>
        /// Defines the _topLevelClassDecls
        /// </summary>
        private Dictionary<string, ClassDeclStmt> _topLevelClassDecls => _context.TopLevelClassDecls;

        /// <summary>
        /// Defines the _topLevelInterfaceDecls
        /// </summary>
        private Dictionary<string, InterfaceDeclStmt> _topLevelInterfaceDecls => _context.TopLevelInterfaceDecls;

        /// <summary>
        /// Defines all known class declarations indexed by qualified path.
        /// </summary>
        private Dictionary<string, ClassDeclStmt> _qualifiedClassDecls => _context.QualifiedClassDecls;

        /// <summary>
        /// Defines all known interface declarations indexed by qualified path.
        /// </summary>
        private Dictionary<string, InterfaceDeclStmt> _qualifiedInterfaceDecls => _context.QualifiedInterfaceDecls;

        /// <summary>
        /// Defines the qualified path for each known class declaration.
        /// </summary>
        private Dictionary<ClassDeclStmt, string> _classQualifiedPaths => _context.ClassQualifiedPaths;

        /// <summary>
        /// Defines the qualified path for each known interface declaration.
        /// </summary>
        private Dictionary<InterfaceDeclStmt, string> _interfaceQualifiedPaths => _context.InterfaceQualifiedPaths;

        /// <summary>
        /// Defines the _interfaceContractCache
        /// </summary>
        private Dictionary<InterfaceDeclStmt, Dictionary<string, InterfaceMethodDecl>> _interfaceContractCache => _context.InterfaceContractCache;

        /// <summary>
        /// Defines the _classMemberSetCache
        /// </summary>
        private Dictionary<ClassDeclStmt, (HashSet<string> InstanceMembers, HashSet<string> StaticMembers)> _classMemberSetCache => _context.ClassMemberSetCache;

        /// <summary>
        /// Defines the _currentClass
        /// </summary>
        private ClassInfo? _currentClass
        {
            get => _emission.CurrentClass;
            set => _emission.CurrentClass = value;
        }

        /// <summary>
        /// Defines the current class declaration being compiled.
        /// </summary>
        private ClassDeclStmt? _currentClassDecl
        {
            get => _emission.CurrentClassDecl;
            set => _emission.CurrentClassDecl = value;
        }

        /// <summary>
        /// Defines the _currentMethodIsStatic
        /// </summary>
        private bool _currentMethodIsStatic
        {
            get => _emission.CurrentMethodIsStatic;
            set => _emission.CurrentMethodIsStatic = value;
        }

        /// <summary>
        /// Defines the _receiverContext
        /// </summary>
        private ReceiverContextKind _receiverContext
        {
            get => _emission.ReceiverContext;
            set => _emission.ReceiverContext = value;
        }

        /// <summary>
        /// Defines the _localVarsStack
        /// </summary>
        private Stack<HashSet<string>> _localVarsStack => _emission.LocalVarsStack;

        /// <summary>
        /// Defines the _scopeDepth
        /// </summary>
        private int _scopeDepth
        {
            get => _emission.ScopeDepth;
            set => _emission.ScopeDepth = value;
        }

        /// <summary>
        /// Defines the _asyncFunctionDepth
        /// </summary>
        private int _asyncFunctionDepth
        {
            get => _emission.AsyncFunctionDepth;
            set => _emission.AsyncFunctionDepth = value;
        }

        /// <summary>
        /// Defines the _anonCounter
        /// </summary>
        private int _anonCounter
        {
            get => _emission.AnonymousCounter;
            set => _emission.AnonymousCounter = value;
        }

        /// <summary>
        /// Defines the EmptyLocals
        /// </summary>
        private static readonly HashSet<string> EmptyLocals = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the CurrentLocals
        /// </summary>
        private HashSet<string> CurrentLocals => _localVarsStack.Count > 0 ? _localVarsStack.Peek() : EmptyLocals;

        /// <summary>
        /// The Compile
        /// </summary>
        /// <param name="program">The program<see cref="List{Stmt}"/></param>
        /// <returns>The <see cref="List{Instruction}"/></returns>
        public List<Instruction> Compile(List<Stmt> program)
        {
            try
            {
                return new CompilationPipeline().Compile(this, program);
            }
            catch (CompilerException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CompilerException(
                    $"internal compiler error: {ex.Message}",
                    0, 0, "<compiler>");
            }
        }

        /// <summary>
        /// The TryGetNamespaceScopePath
        /// </summary>
        /// <param name="block">The block<see cref="BlockStmt"/></param>
        /// <param name="namespacePath">The namespacePath<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        internal static bool TryGetNamespaceScopePath(BlockStmt block, out string namespacePath)
        {
            namespacePath = string.Empty;

            if (block.Statements.Count == 0)
                return false;

            if (block.Statements[0] is not VarDecl nsVar)
                return false;

            if (!nsVar.Name.StartsWith("__ns_scope_", StringComparison.Ordinal))
                return false;

            if (nsVar.Value == null)
                return false;

            return TryExtractQualifiedPath(nsVar.Value, out namespacePath);
        }

        /// <summary>
        /// The TryExtractQualifiedPath
        /// </summary>
        /// <param name="expr">The expr<see cref="Expr"/></param>
        /// <param name="path">The path<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryExtractQualifiedPath(Expr expr, out string path)
        {
            switch (expr)
            {
                case VarExpr ve:
                    path = ve.Name;
                    return !string.IsNullOrWhiteSpace(path);

                case IndexExpr idx when idx.Target != null && idx.Index is StringExpr seg:
                    {
                        if (!TryExtractQualifiedPath(idx.Target, out string prefix))
                        {
                            path = string.Empty;
                            return false;
                        }

                        if (string.IsNullOrWhiteSpace(seg.Value))
                        {
                            path = string.Empty;
                            return false;
                        }

                        path = $"{prefix}.{seg.Value}";
                        return true;
                    }

                default:
                    path = string.Empty;
                    return false;
            }
        }

        /// <summary>
        /// The MethodArityShape
        /// </summary>
        /// <param name="parameterCount">The parameterCount<see cref="int"/></param>
        /// <param name="minArgs">The minArgs<see cref="int"/></param>
        /// <param name="hasRest">The hasRest<see cref="bool"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string MethodArityShape(int parameterCount, int minArgs, bool hasRest)
            => hasRest
                ? $"{minArgs}..*"
                : $"{minArgs}..{parameterCount}";

        /// <summary>
        /// The OrderClassesByInheritance
        /// </summary>
        /// <param name="classDecls">The classDecls<see cref="List{ClassDeclStmt}"/></param>
        /// <returns>The <see cref="List{ClassDeclStmt}"/></returns>
        private List<ClassDeclStmt> OrderClassesByInheritance(List<ClassDeclStmt> classDecls)
            => new ClassCatalog().OrderByInheritance(this, classDecls);

        /// <summary>
        /// The OrderInterfacesByInheritance
        /// </summary>
        /// <param name="interfaceDecls">The interfaceDecls<see cref="List{InterfaceDeclStmt}"/></param>
        /// <returns>The <see cref="List{InterfaceDeclStmt}"/></returns>
        private List<InterfaceDeclStmt> OrderInterfacesByInheritance(List<InterfaceDeclStmt> interfaceDecls)
            => new InterfaceCatalog().OrderByInheritance(this, interfaceDecls);

        /// <summary>
        /// The MethodArityShape
        /// </summary>
        /// <param name="m">The m<see cref="FuncDeclStmt"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string MethodArityShape(FuncDeclStmt m)
            => MethodArityShape(m.Parameters.Count, m.MinArgs, !string.IsNullOrWhiteSpace(m.RestParameter));

        /// <summary>
        /// The MethodArityShape
        /// </summary>
        /// <param name="m">The m<see cref="InterfaceMethodDecl"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string MethodArityShape(InterfaceMethodDecl m)
            => MethodArityShape(m.Parameters.Count, m.MinArgs, !string.IsNullOrWhiteSpace(m.RestParameter));

        /// <summary>
        /// The HaveCompatibleMethodShapes
        /// </summary>
        private static bool HaveCompatibleMethodShapes(
            int expectedParamCount,
            int expectedMinArgs,
            string? expectedRestParameter,
            bool expectedIsAsync,
            int actualParamCount,
            int actualMinArgs,
            string? actualRestParameter,
            bool actualIsAsync)
        {
            return expectedMinArgs == actualMinArgs &&
                   expectedParamCount == actualParamCount &&
                   !string.IsNullOrWhiteSpace(expectedRestParameter) == !string.IsNullOrWhiteSpace(actualRestParameter) &&
                   expectedIsAsync == actualIsAsync;
        }

        /// <summary>
        /// The HaveCompatibleMethodShapes
        /// </summary>
        private static bool HaveCompatibleMethodShapes(InterfaceMethodDecl expected, InterfaceMethodDecl actual)
            => HaveCompatibleMethodShapes(
                expected.Parameters.Count,
                expected.MinArgs,
                expected.RestParameter,
                expected.IsAsync,
                actual.Parameters.Count,
                actual.MinArgs,
                actual.RestParameter,
                actual.IsAsync);

        /// <summary>
        /// The HaveCompatibleMethodShapes
        /// </summary>
        private static bool HaveCompatibleMethodShapes(InterfaceMethodDecl expected, FuncDeclStmt actual)
            => HaveCompatibleMethodShapes(
                expected.Parameters.Count,
                expected.MinArgs,
                expected.RestParameter,
                expected.IsAsync,
                actual.Parameters.Count,
                actual.MinArgs,
                actual.RestParameter,
                actual.IsAsync);

        /// <summary>
        /// The InheritedMemberKindLabel
        /// </summary>
        /// <param name="kind">The kind<see cref="InheritedMemberKind"/></param>
        /// <returns>The <see cref="string"/></returns>
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

        /// <summary>
        /// The DerivedMemberKindLabel
        /// </summary>
        /// <param name="kind">The kind<see cref="InheritedMemberKind"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string DerivedMemberKindLabel(InheritedMemberKind kind)
            => InheritedMemberKindLabel(kind);

        /// <summary>
        /// The TryFindOwnMember
        /// </summary>
        /// <param name="cls">The cls<see cref="ClassDeclStmt"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="member">The member<see cref="InheritedMemberInfo"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryFindOwnMember(ClassDeclStmt cls, string name, out InheritedMemberInfo member)
        {
            if (cls.Fields.ContainsKey(name))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceField, cls);
                return true;
            }

            FuncDeclStmt? instMethod = cls.Methods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
            if (instMethod != null)
            {
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceMethod, cls, instMethod);
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

        /// <summary>
        /// The TryFindInheritedMember
        /// </summary>
        /// <param name="byName">The byName<see cref="Dictionary{string, ClassDeclStmt}"/></param>
        /// <param name="cls">The cls<see cref="ClassDeclStmt"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="member">The member<see cref="InheritedMemberInfo"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryFindInheritedMember(
            ClassDeclStmt cls,
            string name,
            out InheritedMemberInfo member)
        {
            ClassDeclStmt current = cls;
            while (TryResolveBaseClassDecl(current, out ClassDeclStmt baseCls))
            {
                if (TryFindOwnMember(baseCls, name, out member))
                    return true;

                current = baseCls;
            }

            member = default;
            return false;
        }

        /// <summary>
        /// The ValidateMethodOverrideShape
        /// </summary>
        /// <param name="derivedClass">The derivedClass<see cref="ClassDeclStmt"/></param>
        /// <param name="derivedMethod">The derivedMethod<see cref="FuncDeclStmt"/></param>
        /// <param name="baseMember">The baseMember<see cref="InheritedMemberInfo"/></param>
        private static void ValidateMethodOverrideShape(
            ClassDeclStmt derivedClass,
            FuncDeclStmt derivedMethod,
            InheritedMemberInfo baseMember)
        {
            FuncDeclStmt baseMethod = baseMember.MethodDecl
                ?? throw new CompilerException(
                    $"internal compiler error: missing base method metadata for '{derivedMethod.Name}'",
                    derivedMethod.Line, derivedMethod.Col, derivedMethod.OriginFile);

            bool baseHasRest = !string.IsNullOrWhiteSpace(baseMethod.RestParameter);
            bool derivedHasRest = !string.IsNullOrWhiteSpace(derivedMethod.RestParameter);

            if (baseHasRest != derivedHasRest ||
                baseMethod.MinArgs != derivedMethod.MinArgs ||
                baseMethod.Parameters.Count != derivedMethod.Parameters.Count)
            {
                throw new CompilerException(
                    $"incompatible override for method '{derivedMethod.Name}' in class '{derivedClass.Name}': expected arity {MethodArityShape(baseMethod)} from base class '{baseMember.OwnerDecl.Name}', got {MethodArityShape(derivedMethod)}",
                    derivedMethod.Line, derivedMethod.Col, derivedMethod.OriginFile);
            }
        }

        /// <summary>
        /// The VisibilityRank
        /// </summary>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int VisibilityRank(MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Private => 0,
                MemberVisibility.Protected => 1,
                _ => 2
            };
        }

        /// <summary>
        /// The GetDeclaredMemberVisibilityByKind
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="kind">The kind<see cref="InheritedMemberKind"/></param>
        /// <returns>The <see cref="MemberVisibility"/></returns>
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

        /// <summary>
        /// The ValidateMemberVisibilityCompatibility
        /// </summary>
        /// <param name="byName">The byName<see cref="Dictionary{string, ClassDeclStmt}"/></param>
        /// <param name="derivedClass">The derivedClass<see cref="ClassDeclStmt"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="derivedKind">The derivedKind<see cref="InheritedMemberKind"/></param>
        /// <param name="baseMember">The baseMember<see cref="InheritedMemberInfo"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
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
                    line, col, file);
            }
        }

        /// <summary>
        /// The ValidateMemberKindCompatibility
        /// </summary>
        /// <param name="derivedClass">The derivedClass<see cref="ClassDeclStmt"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="derivedKind">The derivedKind<see cref="InheritedMemberKind"/></param>
        /// <param name="baseMember">The baseMember<see cref="InheritedMemberInfo"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
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
                $"invalid override for member '{memberName}' in class '{derivedClass.Name}': declared as {DerivedMemberKindLabel(derivedKind)} but inherited member in base class '{baseMember.OwnerDecl.Name}' is {InheritedMemberKindLabel(baseMember.Kind)}",
                line, col, file);
        }

        /// <summary>
        /// The ValidateInheritanceOverrides
        /// </summary>
        /// <param name="sortedClasses">The sortedClasses<see cref="List{ClassDeclStmt}"/></param>
        private void ValidateInheritanceOverrides(List<ClassDeclStmt> sortedClasses)
            => new ClassSemanticValidator().ValidateInheritanceOverrides(this, sortedClasses);

        /// <summary>
        /// The GetConstructorSignature
        /// </summary>
        /// <param name="cls">The cls<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="ConstructorSignature"/></returns>
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

        /// <summary>
        /// The GetConstructorVisibility
        /// </summary>
        /// <param name="cls">The cls<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="MemberVisibility"/></returns>
        private static MemberVisibility GetConstructorVisibility(ClassDeclStmt cls)
        {
            if (cls.Methods.Any(m => string.Equals(m.Name, "init", StringComparison.Ordinal)))
                return GetOrDefaultVisibility(cls.MethodVisibility, "init");

            return MemberVisibility.Public;
        }

        /// <summary>
        /// The ValidateCallArgumentsAgainstSignature
        /// </summary>
        /// <param name="args">The args<see cref="IReadOnlyList{Expr}"/></param>
        /// <param name="signature">The signature<see cref="ConstructorSignature"/></param>
        /// <param name="implicitLeadingArgs">The implicitLeadingArgs<see cref="int"/></param>
        /// <param name="contextPrefix">The contextPrefix<see cref="string"/></param>
        /// <param name="line">The line<see cref="int"/></param>
        /// <param name="col">The col<see cref="int"/></param>
        /// <param name="file">The file<see cref="string"/></param>
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
                    line, col, file);
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
                            named.Line, named.Col, named.OriginFile);
                    }
                    continue;
                }

                if (sawNamed)
                {
                    throw new CompilerException(
                        $"{contextPrefix}: positional argument cannot follow named arguments",
                        arg.Line, arg.Col, arg.OriginFile);
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
                        line, col, file);
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
                        line, col, file);
                }

                if (!paramIndex.ContainsKey(namedArg))
                {
                    throw new CompilerException(
                        $"{contextPrefix}: unknown named argument '{namedArg}'",
                        line, col, file);
                }
            }

            if (restIndex < 0 && positionalCount > fixedCount)
            {
                throw new CompilerException(
                    $"{contextPrefix}: too many args for call (expected {fixedCount}, got {positionalCount})",
                    line, col, file);
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
                        line, col, file);
                }
            }
        }

        /// <summary>
        /// The ValidateBaseConstructorCalls
        /// </summary>
        /// <param name="sortedClasses">The sortedClasses<see cref="List{ClassDeclStmt}"/></param>
        private void ValidateBaseConstructorCalls(List<ClassDeclStmt> sortedClasses)
            => new ClassSemanticValidator().ValidateBaseConstructorCalls(this, sortedClasses);

        /// <summary>
        /// The IsReservedRuntimeMemberName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        internal static bool IsReservedRuntimeMemberName(string name)
            => ReservedRuntimeMemberNames.Contains(name);

        /// <summary>
        /// The IsReservedInternalMemberName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        internal static bool IsReservedInternalMemberName(string name)
            => name.StartsWith("__", StringComparison.Ordinal);

        /// <summary>
        /// The TryResolveBaseClassDecl
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="baseDecl">The baseDecl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        internal bool TryResolveBaseClassDecl(ClassDeclStmt decl, out ClassDeclStmt baseDecl)
        {
            baseDecl = null!;

            if (string.IsNullOrWhiteSpace(decl.BaseName))
                return false;

            return TryResolveClassDecl(decl, decl.BaseName, out baseDecl);
        }

        /// <summary>
        /// Enumerates containing scope prefixes from inner-most to outer-most.
        /// </summary>
        private static IEnumerable<string> EnumerateContainingScopes(string qualifiedPath)
        {
            string current = qualifiedPath;
            while (true)
            {
                int lastDot = current.LastIndexOf('.');
                if (lastDot <= 0)
                    yield break;

                current = current[..lastDot];
                yield return current;
            }
        }

        /// <summary>
        /// The TryResolveClassDecl
        /// </summary>
        internal bool TryResolveClassDecl(ClassDeclStmt context, string className, out ClassDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(className))
                return false;

            if (className.Contains('.'))
                return _qualifiedClassDecls.TryGetValue(className, out decl!);

            if (_classQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{className}";
                    if (_qualifiedClassDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelClassDecls.TryGetValue(className, out decl!);
        }

        /// <summary>
        /// The TryResolveBaseInterfaceDecl
        /// </summary>
        internal bool TryResolveBaseInterfaceDecl(InterfaceDeclStmt context, string interfaceName, out InterfaceDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(interfaceName))
                return false;

            if (interfaceName.Contains('.'))
                return _qualifiedInterfaceDecls.TryGetValue(interfaceName, out decl!);

            if (_interfaceQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{interfaceName}";
                    if (_qualifiedInterfaceDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelInterfaceDecls.TryGetValue(interfaceName, out decl!);
        }

        /// <summary>
        /// The TryResolveInterfaceDecl
        /// </summary>
        internal bool TryResolveInterfaceDecl(ClassDeclStmt context, string interfaceName, out InterfaceDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(interfaceName))
                return false;

            if (interfaceName.Contains('.'))
                return _qualifiedInterfaceDecls.TryGetValue(interfaceName, out decl!);

            if (_classQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{interfaceName}";
                    if (_qualifiedInterfaceDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelInterfaceDecls.TryGetValue(interfaceName, out decl!);
        }

        /// <summary>
        /// Normalizes class inheritance declarations so interfaces are separated from the single base-class slot.
        /// </summary>
        private void NormalizeClassInheritanceDeclarations(IEnumerable<ClassDeclStmt> classDecls)
            => new ClassCatalog().NormalizeInheritanceDeclarations(this, classDecls);

        /// <summary>
        /// Validates all known interface declarations and warms the transitive contract cache.
        /// </summary>
        private void ValidateAllKnownInterfaces()
            => new InterfaceCatalog().ValidateAllKnownInterfaces(this);

        /// <summary>
        /// The TryResolveClassDeclFromInterfaceBaseName
        /// </summary>
        internal bool TryResolveClassDeclFromInterfaceBaseName(InterfaceDeclStmt context, string className, out ClassDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(className))
                return false;

            if (className.Contains('.'))
                return _qualifiedClassDecls.TryGetValue(className, out decl!);

            if (_interfaceQualifiedPaths.TryGetValue(context, out string? currentPath))
            {
                foreach (string scope in EnumerateContainingScopes(currentPath))
                {
                    string scopedPath = $"{scope}.{className}";
                    if (_qualifiedClassDecls.TryGetValue(scopedPath, out decl!))
                        return true;
                }
            }

            return _topLevelClassDecls.TryGetValue(className, out decl!);
        }

        /// <summary>
        /// The GetOrBuildInterfaceContract
        /// </summary>
        internal Dictionary<string, InterfaceMethodDecl> GetOrBuildInterfaceContract(InterfaceDeclStmt iface)
        {
            if (_interfaceContractCache.TryGetValue(iface, out Dictionary<string, InterfaceMethodDecl>? cached))
                return cached;

            Dictionary<string, InterfaceMethodDecl> contract = new(StringComparer.Ordinal);

            foreach (string baseName in iface.BaseInterfaces)
            {
                if (!TryResolveBaseInterfaceDecl(iface, baseName, out InterfaceDeclStmt baseIface))
                    continue;

                Dictionary<string, InterfaceMethodDecl> baseContract = GetOrBuildInterfaceContract(baseIface);
                foreach (KeyValuePair<string, InterfaceMethodDecl> kv in baseContract)
                {
                    if (contract.TryGetValue(kv.Key, out InterfaceMethodDecl? existing) &&
                        !HaveCompatibleMethodShapes(existing, kv.Value))
                    {
                        throw new CompilerException(
                            $"incompatible inherited method '{kv.Key}' in interface '{iface.Name}': expected arity {MethodArityShape(existing)}, got {MethodArityShape(kv.Value)}",
                            kv.Value.Line, kv.Value.Col, kv.Value.OriginFile);
                    }

                    contract[kv.Key] = kv.Value;
                }
            }

            foreach (InterfaceMethodDecl method in iface.Methods)
            {
                if (contract.TryGetValue(method.Name, out InterfaceMethodDecl? inherited) &&
                    !HaveCompatibleMethodShapes(inherited, method))
                {
                    throw new CompilerException(
                        $"incompatible method '{method.Name}' in interface '{iface.Name}': expected arity {MethodArityShape(inherited)}, got {MethodArityShape(method)}",
                        method.Line, method.Col, method.OriginFile);
                }

                contract[method.Name] = method;
            }

            _interfaceContractCache[iface] = contract;
            return contract;
        }

        /// <summary>
        /// The TryFindInstanceMethodInHierarchy
        /// </summary>
        private bool TryFindInstanceMethodInHierarchy(
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

                if (!TryResolveBaseClassDecl(current, out ClassDeclStmt baseDecl))
                    break;

                current = baseDecl;
            }

            ownerDecl = null!;
            methodDecl = null!;
            visibility = MemberVisibility.Public;
            return false;
        }

        /// <summary>
        /// The ValidateInterfaceImplementations
        /// </summary>
        private void ValidateInterfaceImplementations(IEnumerable<ClassDeclStmt> classDecls)
            => new ClassSemanticValidator().ValidateInterfaceImplementations(this, classDecls);

        /// <summary>
        /// The TryResolveKnownClassDeclFromPath
        /// </summary>
        /// <param name="classPath">The classPath<see cref="string"/></param>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryResolveKnownClassDeclFromPath(string classPath, out ClassDeclStmt decl)
            => new MemberAccessRules().TryResolveKnownClassDeclFromPath(this, classPath, out decl);

        /// <summary>
        /// The TryResolveKnownClassDeclFromExpr
        /// </summary>
        /// <param name="expr">The expr<see cref="Expr"/></param>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryResolveKnownClassDeclFromExpr(Expr expr, out ClassDeclStmt decl)
            => new MemberAccessRules().TryResolveKnownClassDeclFromExpr(this, expr, CurrentLocals, out decl);

        /// <summary>
        /// The GetOrBuildClassMemberSets
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="(HashSet{string} InstanceMembers, HashSet{string} StaticMembers)"/></returns>
        private (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) GetOrBuildClassMemberSets(ClassDeclStmt decl)
            => new MemberAccessRules().GetOrBuildClassMemberSets(this, decl);

        /// <summary>
        /// The VisibilityLabel
        /// </summary>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="string"/></returns>
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

        /// <summary>
        /// The TryGetDeclaredMemberVisibility
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="bool"/></returns>
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

        /// <summary>
        /// The TryFindMemberVisibilityInHierarchy
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="ownerDecl">The ownerDecl<see cref="ClassDeclStmt"/></param>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryFindMemberVisibilityInHierarchy(
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

                if (!TryResolveBaseClassDecl(current, out ClassDeclStmt baseDecl))
                    break;

                current = baseDecl;
            }

            ownerDecl = null!;
            visibility = MemberVisibility.Public;
            return false;
        }

        /// <summary>
        /// The IsSameOrDerivedFrom
        /// </summary>
        /// <param name="candidate">The candidate<see cref="ClassDeclStmt"/></param>
        /// <param name="baseDecl">The baseDecl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool IsSameOrDerivedFrom(ClassDeclStmt candidate, ClassDeclStmt baseDecl)
        {
            if (ReferenceEquals(candidate, baseDecl))
                return true;

            ClassDeclStmt current = candidate;
            while (TryResolveBaseClassDecl(current, out ClassDeclStmt parent))
            {
                if (ReferenceEquals(parent, baseDecl))
                    return true;
                current = parent;
            }

            return false;
        }

        /// <summary>
        /// The IsMemberAccessAllowed
        /// </summary>
        /// <param name="ownerDecl">The ownerDecl<see cref="ClassDeclStmt"/></param>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool IsMemberAccessAllowed(ClassDeclStmt ownerDecl, MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Public => true,
                MemberVisibility.Private => _currentClassDecl != null && ReferenceEquals(_currentClassDecl, ownerDecl),
                MemberVisibility.Protected => _currentClassDecl != null && IsSameOrDerivedFrom(_currentClassDecl, ownerDecl),
                _ => true
            };
        }

        /// <summary>
        /// The ValidateMemberVisibilityAgainstKnownClass
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void ValidateMemberVisibilityAgainstKnownClass(
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            Node node)
            => new MemberAccessRules().ValidateMemberVisibilityAgainstKnownClass(this, decl, memberName, expectInstance, _currentClassDecl, node);

        /// <summary>
        /// The ValidateMemberAccessAgainstCurrentClass
        /// </summary>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void ValidateMemberAccessAgainstCurrentClass(string memberName, bool expectInstance, Node node)
            => new MemberAccessRules().ValidateMemberAccessAgainstCurrentClass(this, _currentClass, _currentClassDecl, memberName, expectInstance, node);

        /// <summary>
        /// The ValidateMemberAccessAgainstKnownClass
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void ValidateMemberAccessAgainstKnownClass(
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            Node node)
            => new MemberAccessRules().ValidateMemberAccessAgainstKnownClass(this, decl, memberName, expectInstance, _currentClassDecl, node);

        /// <summary>
        /// The ValidateExplicitMemberAccess
        /// </summary>
        /// <param name="idx">The idx<see cref="IndexExpr"/></param>
        /// <param name="isStore">The isStore<see cref="bool"/></param>
        private void ValidateExplicitMemberAccess(IndexExpr idx, bool isStore)
        {
            if (idx.Index is not StringExpr memberExpr)
                return;

            string memberName = memberExpr.Value;
            if (string.IsNullOrWhiteSpace(memberName))
                return;

            if (idx.Target is VarExpr receiverVar && IsReceiverIdentifier(receiverVar.Name))
            {
                ValidateReceiverUsage(receiverVar.Name, receiverVar);
                if (isStore && IsReservedRuntimeMemberName(memberName))
                {
                    throw new CompilerException(
                        $"invalid member assignment '{memberName}': reserved member name",
                        idx.Line, idx.Col, idx.OriginFile);
                }

                if (_currentClass == null)
                    return;

                switch (receiverVar.Name)
                {
                    case "this":
                        ValidateMemberAccessAgainstCurrentClass(memberName, expectInstance: true, idx);
                        return;
                    case "type":
                        ValidateMemberAccessAgainstCurrentClass(memberName, expectInstance: false, idx);
                        return;
                    case "super":
                        if (string.IsNullOrWhiteSpace(_currentClass.BaseName))
                            return;
                        if (_currentClassDecl != null && TryResolveBaseClassDecl(_currentClassDecl, out ClassDeclStmt baseDecl))
                        {
                            bool expectInstance = _receiverContext == ReceiverContextKind.InstanceMethod;
                            ValidateMemberAccessAgainstKnownClass(baseDecl, memberName, expectInstance, idx);
                        }
                        return;
                    default:
                        return;
                }
            }

            if (idx.Target != null && TryResolveKnownClassDeclFromExpr(idx.Target, out ClassDeclStmt decl))
            {
                if (isStore && IsReservedRuntimeMemberName(memberName))
                {
                    throw new CompilerException(
                        $"invalid member assignment '{memberName}': reserved member name",
                        idx.Line, idx.Col, idx.OriginFile);
                }

                ValidateMemberAccessAgainstKnownClass(decl, memberName, expectInstance: false, idx);
            }
        }

        /// <summary>
        /// The ValidateNewObjectInitializers
        /// </summary>
        /// <param name="ne">The ne<see cref="NewExpr"/></param>
        private void ValidateNewObjectInitializers(NewExpr ne)
            => new MemberAccessRules().ValidateNewObjectInitializers(this, ne, _currentClassDecl);

        /// <summary>
        /// Emits instructions that load a runtime value by qualified symbol path.
        /// </summary>
        private void EmitLoadQualifiedRuntimeValue(string qualifiedPath, Node node)
        {
            if (string.IsNullOrWhiteSpace(qualifiedPath))
                throw new CompilerException("internal compiler error: empty qualified runtime path", node.Line, node.Col, node.OriginFile);

            string[] parts = qualifiedPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                throw new CompilerException("internal compiler error: invalid qualified runtime path", node.Line, node.Col, node.OriginFile);

            _insns.Add(new Instruction(OpCode.LOAD_VAR, parts[0], node.Line, node.Col, node.OriginFile));
            for (int i = 1; i < parts.Length; i++)
            {
                _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, null, node.Line, node.Col, node.OriginFile));
            }
        }



        /// <summary>
        /// The OpFromToken
        /// </summary>
        /// <param name="t">The t<see cref="TokenType"/></param>
        /// <param name="tp">The tp<see cref="Node"/></param>
        /// <param name="outOfFile">The outOfFile<see cref="string"/></param>
        /// <returns>The <see cref="OpCode"/></returns>
        private static OpCode OpFromToken(TokenType t, Node tp, string outOfFile) => t switch
        {
            TokenType.Plus => OpCode.ADD,
            TokenType.Minus => OpCode.SUB,
            TokenType.Star => OpCode.MUL,
            TokenType.Slash => OpCode.DIV,
            TokenType.Modulo => OpCode.MOD,
            TokenType.bShiftR => OpCode.SHR,
            TokenType.bShiftL => OpCode.SHL,
            TokenType.bOr => OpCode.BIT_OR,
            TokenType.bXor => OpCode.BIT_XOR,
            TokenType.bAnd => OpCode.BIT_AND,
            TokenType.Expo => OpCode.EXPO,
            TokenType.Eq => OpCode.EQ,
            TokenType.Neq => OpCode.NEQ,
            TokenType.Lt => OpCode.LT,
            TokenType.Gt => OpCode.GT,
            TokenType.Le => OpCode.LE,
            TokenType.Ge => OpCode.GE,
            TokenType.Is => OpCode.IS_TYPE,
            TokenType.AndAnd => OpCode.AND,
            TokenType.OrOr => OpCode.OR,
            TokenType.PlusAssign => OpCode.ADD,
            TokenType.MinusAssign => OpCode.SUB,
            TokenType.StarAssign => OpCode.MUL,
            TokenType.SlashAssign => OpCode.DIV,
            TokenType.ModAssign => OpCode.MOD,

            _ => throw new CompilerException($"unsupported operator token for bytecode: {t}", tp.Line, tp.Col, outOfFile)
        };

        /// <summary>
        /// The CompileLValue
        /// </summary>
        /// <param name="target">The target<see cref="Expr?"/></param>
        /// <param name="load">The load<see cref="bool"/></param>
        private void CompileLValue(Expr? target, bool load)
        {
            if (target is VarExpr v)
            {
                if (load)
                {
                    ValidateReceiverUsage(v.Name, v);
                    if (!TryEmitImplicitMemberLoad(v.Name, v))
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, v.Name, v.Line, v.Col, v.OriginFile));
                }
            }
            else if (target is IndexExpr ie)
            {
                if (ie.Index == null)
                    throw new CompilerException($"empty index '[]' cannot be used for reading", ie.Line, ie.Col, FileName);

                ValidateExplicitMemberAccess(ie, isStore: false);
                CompileExpr(ie.Target);
                CompileExpr(ie.Index);
                if (load) _insns.Add(new Instruction(OpCode.INDEX_GET, null, ie.Line, ie.Col, ie.OriginFile));
            }
            else
            {
                throw new CompilerException("invalid lvalue expression", target?.Line ?? -1, target?.Col ?? -1, FileName);
            }
        }

        /// <summary>
        /// The CompileLValueStore
        /// </summary>
        /// <param name="target">The target<see cref="Expr?"/></param>
        private void CompileLValueStore(Expr? target)
        {
            if (target is VarExpr v)
            {
                ValidateReceiverAssignment(v.Name, v);
                if (!TryEmitImplicitMemberStore(v.Name, v))
                    _insns.Add(new Instruction(OpCode.STORE_VAR, v.Name, v.Line, v.Col, v.OriginFile));
            }
            else if (target is IndexExpr ie)
            {
                if (ie.Index == null)
                {
                    CompileExpr(ie.Target);
                    _insns.Add(new Instruction(OpCode.ARRAY_PUSH, null, ie.Line, ie.Col, ie.OriginFile));
                }
                else
                {
                    ValidateExplicitMemberAccess(ie, isStore: true);
                    CompileExpr(ie.Target);
                    CompileExpr(ie.Index);
                    _insns.Add(new Instruction(OpCode.ROT, null, ie.Line, ie.Col, ie.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET, null, ie.Line, ie.Col, ie.OriginFile));
                }
            }
            else
            {
                throw new CompilerException("Invalid lvalue for store.", target?.Line ?? -1, target?.Col ?? -1, FileName);
            }
        }

        /// <summary>
        /// The GetOrDefaultVisibility
        /// </summary>
        /// <param name="map">The map<see cref="Dictionary{string, MemberVisibility}"/></param>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="MemberVisibility"/></returns>
        private static MemberVisibility GetOrDefaultVisibility(Dictionary<string, MemberVisibility> map, string name)
            => map.TryGetValue(name, out MemberVisibility v) ? v : MemberVisibility.Public;

        /// <summary>
        /// The ToVisibilityCode
        /// </summary>
        /// <param name="visibility">The visibility<see cref="MemberVisibility"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int ToVisibilityCode(MemberVisibility visibility)
        {
            return visibility switch
            {
                MemberVisibility.Public => 0,
                MemberVisibility.Private => 1,
                MemberVisibility.Protected => 2,
                _ => 0
            };
        }

        /// <summary>
        /// The EnumerateDeclaredInstanceVisibilityEntries
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="List{(string Name, int Code)}"/></returns>
        private static List<(string Name, int Code)> EnumerateDeclaredInstanceVisibilityEntries(ClassDeclStmt decl)
        {
            List<(string Name, int Code)> entries = new();

            foreach (string name in decl.Fields.Keys)
                entries.Add((name, ToVisibilityCode(GetOrDefaultVisibility(decl.FieldVisibility, name))));

            foreach (FuncDeclStmt method in decl.Methods)
                entries.Add((method.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.MethodVisibility, method.Name))));

            return entries;
        }

        /// <summary>
        /// The EnumerateDeclaredStaticVisibilityEntries
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="List{(string Name, int Code)}"/></returns>
        private static List<(string Name, int Code)> EnumerateDeclaredStaticVisibilityEntries(ClassDeclStmt decl)
        {
            List<(string Name, int Code)> entries = new();

            entries.Add(("new", ToVisibilityCode(GetConstructorVisibility(decl))));

            foreach (string name in decl.StaticFields.Keys)
                entries.Add((name, ToVisibilityCode(GetOrDefaultVisibility(decl.StaticFieldVisibility, name))));

            foreach (FuncDeclStmt method in decl.StaticMethods)
                entries.Add((method.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.StaticMethodVisibility, method.Name))));

            foreach (EnumDeclStmt en in decl.Enums)
                entries.Add((en.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.EnumVisibility, en.Name))));

            foreach (ClassDeclStmt nested in decl.NestedClasses)
                entries.Add((nested.Name, ToVisibilityCode(GetOrDefaultVisibility(decl.NestedClassVisibility, nested.Name))));

            return entries;
        }

        /// <summary>
        /// The AddDeclaredMembersToClassInfo
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="ci">The ci<see cref="ClassInfo"/></param>
        private static void AddDeclaredMembersToClassInfo(ClassDeclStmt decl, ClassInfo ci)
        {
            foreach (KeyValuePair<string, Expr?> kv in decl.Fields)
            {
                ci.InstanceMembers.Add(kv.Key);
                ci.InstanceVisibility[kv.Key] = GetOrDefaultVisibility(decl.FieldVisibility, kv.Key);
            }

            foreach (FuncDeclStmt m in decl.Methods)
            {
                ci.InstanceMembers.Add(m.Name);
                ci.InstanceVisibility[m.Name] = GetOrDefaultVisibility(decl.MethodVisibility, m.Name);
            }

            foreach (KeyValuePair<string, Expr?> kv in decl.StaticFields)
            {
                ci.StaticMembers.Add(kv.Key);
                ci.StaticVisibility[kv.Key] = GetOrDefaultVisibility(decl.StaticFieldVisibility, kv.Key);
            }

            foreach (FuncDeclStmt m in decl.StaticMethods)
            {
                ci.StaticMembers.Add(m.Name);
                ci.StaticVisibility[m.Name] = GetOrDefaultVisibility(decl.StaticMethodVisibility, m.Name);
            }

            ci.StaticMembers.Add("new");
            ci.StaticVisibility["new"] = GetConstructorVisibility(decl);

            foreach (EnumDeclStmt en in decl.Enums)
            {
                ci.StaticMembers.Add(en.Name);
                ci.StaticVisibility[en.Name] = GetOrDefaultVisibility(decl.EnumVisibility, en.Name);
            }

            foreach (ClassDeclStmt inner in decl.NestedClasses)
            {
                ci.StaticMembers.Add(inner.Name);
                ci.StaticVisibility[inner.Name] = GetOrDefaultVisibility(decl.NestedClassVisibility, inner.Name);
            }
        }

        /// <summary>
        /// The MergeInheritedVisibleMembers
        /// </summary>
        /// <param name="target">The target<see cref="ClassInfo"/></param>
        /// <param name="baseInfo">The baseInfo<see cref="ClassInfo"/></param>
        private static void MergeInheritedVisibleMembers(ClassInfo target, ClassInfo baseInfo)
        {
            foreach (KeyValuePair<string, MemberVisibility> kv in baseInfo.InstanceVisibility)
            {
                if (kv.Value == MemberVisibility.Private)
                    continue;

                if (target.InstanceMembers.Contains(kv.Key))
                    continue;

                target.InstanceMembers.Add(kv.Key);
                target.InstanceVisibility[kv.Key] = kv.Value;
            }

            foreach (KeyValuePair<string, MemberVisibility> kv in baseInfo.StaticVisibility)
            {
                if (kv.Value == MemberVisibility.Private)
                    continue;

                if (target.StaticMembers.Contains(kv.Key))
                    continue;

                target.StaticMembers.Add(kv.Key);
                target.StaticVisibility[kv.Key] = kv.Value;
            }
        }

        /// <summary>
        /// The BuildClassInfos
        /// </summary>
        /// <param name="sortedClasses">The sortedClasses<see cref="List{ClassDeclStmt}"/></param>
        internal void BuildClassInfos(List<ClassDeclStmt> sortedClasses)
        {
            _classInfos.Clear();

            foreach (ClassDeclStmt cds in sortedClasses)
            {
                string? baseName = string.IsNullOrEmpty(cds.BaseName) ? null : cds.BaseName;
                ClassInfo ci = new(cds.Name, baseName, cds.IsNested);
                AddDeclaredMembersToClassInfo(cds, ci);

                if (TryResolveBaseClassDecl(cds, out ClassDeclStmt baseDecl) && _classInfos.TryGetValue(baseDecl, out ClassInfo? baseCi))
                    MergeInheritedVisibleMembers(ci, baseCi);

                _classInfos[cds] = ci;
            }
        }

        /// <summary>
        /// The BuildAdHocClassInfo
        /// </summary>
        /// <param name="cds">The cds<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="ClassInfo"/></returns>
        private ClassInfo BuildAdHocClassInfo(ClassDeclStmt cds)
        {
            string? baseName = string.IsNullOrEmpty(cds.BaseName) ? null : cds.BaseName;
            ClassInfo ci = new(cds.Name, baseName, cds.IsNested);

            AddDeclaredMembersToClassInfo(cds, ci);

            if (TryResolveBaseClassDecl(cds, out ClassDeclStmt baseDecl))
            {
                if (!_classInfos.TryGetValue(baseDecl, out ClassInfo? baseCi))
                {
                    baseCi = BuildAdHocClassInfo(baseDecl);
                    _classInfos[baseDecl] = baseCi;
                }

                MergeInheritedVisibleMembers(ci, baseCi);
            }

            return ci;
        }

        /// <summary>
        /// The EnterFunctionLocals
        /// </summary>
        /// <param name="parameters">The parameters<see cref="IEnumerable{string}"/></param>
        private void EnterFunctionLocals(IEnumerable<string> parameters)
        {
            HashSet<string> inherited;

            if (_localVarsStack.Count > 0)
                inherited = new HashSet<string>(_localVarsStack.Peek(), StringComparer.Ordinal);
            else
                inherited = new HashSet<string>(StringComparer.Ordinal);

            foreach (string p in parameters)
                inherited.Add(p);

            _localVarsStack.Push(inherited);
        }

        /// <summary>
        /// The LeaveFunctionLocals
        /// </summary>
        private void LeaveFunctionLocals()
        {
            if (_localVarsStack.Count > 0)
                _localVarsStack.Pop();
        }

        /// <summary>
        /// The IsReceiverIdentifier
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReceiverIdentifier(string name)
            => name == "this" || name == "type" || name == "super" || name == "outer";

        /// <summary>
        /// The DetermineReceiverContext
        /// </summary>
        /// <param name="fe">The fe<see cref="FuncExpr"/></param>
        /// <returns>The <see cref="ReceiverContextKind"/></returns>
        private ReceiverContextKind DetermineReceiverContext(FuncExpr fe)
        {
            if (_currentClass == null || fe.Parameters.Count == 0)
                return ReceiverContextKind.None;

            return fe.Parameters[0] switch
            {
                "this" => ReceiverContextKind.InstanceMethod,
                "type" => ReceiverContextKind.StaticMethod,
                _ => ReceiverContextKind.None
            };
        }

        /// <summary>
        /// The ValidateReceiverUsage
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void ValidateReceiverUsage(string name, Node node)
        {
            if (!IsReceiverIdentifier(name))
                return;

            switch (name)
            {
                case "this":
                    if (_receiverContext != ReceiverContextKind.InstanceMethod)
                        throw new CompilerException(
                            "invalid receiver usage 'this': only available in instance methods",
                            node.Line, node.Col, node.OriginFile);
                    return;

                case "type":
                    if (_receiverContext == ReceiverContextKind.None)
                        throw new CompilerException(
                            "invalid receiver usage 'type': only available in class methods",
                            node.Line, node.Col, node.OriginFile);
                    return;

                case "super":
                    if (_receiverContext == ReceiverContextKind.None)
                        throw new CompilerException(
                            "invalid receiver usage 'super': only available in class methods with a base class",
                            node.Line, node.Col, node.OriginFile);
                    if (_currentClass == null || string.IsNullOrWhiteSpace(_currentClass.BaseName))
                        throw new CompilerException(
                            "invalid receiver usage 'super': class has no base class",
                            node.Line, node.Col, node.OriginFile);
                    return;

                case "outer":
                    if (_receiverContext != ReceiverContextKind.InstanceMethod || _currentClass == null || !_currentClass.IsNested)
                        throw new CompilerException(
                            "invalid receiver usage 'outer': only available in nested instance methods",
                            node.Line, node.Col, node.OriginFile);
                    return;
            }
        }

        /// <summary>
        /// The ValidateReceiverAssignment
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void ValidateReceiverAssignment(string name, Node node)
        {
            if (!IsReceiverIdentifier(name))
                return;

            ValidateReceiverUsage(name, node);
            throw new CompilerException(
                $"invalid receiver assignment '{name}': receiver identifiers are read-only",
                node.Line, node.Col, node.OriginFile);
        }

        /// <summary>
        /// The ResolveImplicitMemberResolution
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="ImplicitMemberResolutionKind"/></returns>
        private ImplicitMemberResolutionKind ResolveImplicitMemberResolution(string name)
        {
            if (_currentClass == null)
                return ImplicitMemberResolutionKind.None;

            if (name == "this" || name == "type" || name == "super" || name == "outer")
                return ImplicitMemberResolutionKind.None;

            if (CurrentLocals.Contains(name))
                return ImplicitMemberResolutionKind.None;

            bool hasInstanceMember = !_currentMethodIsStatic && _currentClass.IsInstanceMember(name);
            bool hasStaticMember = _currentClass.IsStaticMember(name);

            if (hasInstanceMember && hasStaticMember)
                return ImplicitMemberResolutionKind.Ambiguous;

            if (hasInstanceMember)
                return ImplicitMemberResolutionKind.Instance;

            if (hasStaticMember)
                return ImplicitMemberResolutionKind.Static;

            return ImplicitMemberResolutionKind.None;
        }

        /// <summary>
        /// The TryEmitImplicitMemberLoad
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryEmitImplicitMemberLoad(string name, Node node)
        {
            ImplicitMemberResolutionKind resolution = ResolveImplicitMemberResolution(name);
            if (resolution == ImplicitMemberResolutionKind.Ambiguous)
                throw new CompilerException(
                    $"ambiguous member reference '{name}' in class '{_currentClass!.Name}': both instance and static members are visible",
                    node.Line, node.Col, node.OriginFile);

            if (resolution == ImplicitMemberResolutionKind.Instance)
            {
                _insns.Add(new Instruction(OpCode.LOAD_VAR, "this", node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, name, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, null, node.Line, node.Col, node.OriginFile));
                return true;
            }

            if (resolution == ImplicitMemberResolutionKind.Static)
            {
                ClassInfo cls = _currentClass
                    ?? throw new CompilerException("internal compiler error: missing current class for static member load", node.Line, node.Col, node.OriginFile);
                _insns.Add(new Instruction(OpCode.LOAD_VAR, cls.Name, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, name, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, null, node.Line, node.Col, node.OriginFile));
                return true;
            }

            return false;
        }

        /// <summary>
        /// The TryEmitImplicitMemberStore
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryEmitImplicitMemberStore(string name, Node node)
        {
            ImplicitMemberResolutionKind resolution = ResolveImplicitMemberResolution(name);
            if (resolution == ImplicitMemberResolutionKind.Ambiguous)
                throw new CompilerException(
                    $"ambiguous member reference '{name}' in class '{_currentClass!.Name}': both instance and static members are visible",
                    node.Line, node.Col, node.OriginFile);

            if (resolution == ImplicitMemberResolutionKind.Instance)
            {
                _insns.Add(new Instruction(OpCode.LOAD_VAR, "this", node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, name, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.ROT, null, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_SET, null, node.Line, node.Col, node.OriginFile));
                return true;
            }

            if (resolution == ImplicitMemberResolutionKind.Static)
            {
                ClassInfo cls = _currentClass
                    ?? throw new CompilerException("internal compiler error: missing current class for static member store", node.Line, node.Col, node.OriginFile);
                _insns.Add(new Instruction(OpCode.LOAD_VAR, cls.Name, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, name, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.ROT, null, node.Line, node.Col, node.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_SET, null, node.Line, node.Col, node.OriginFile));
                return true;
            }

            return false;
        }
    }
}

