using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;
using System.Numerics;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Defines the <see cref="Compiler" />
    /// </summary>
    public class Compiler(string fname)
    {
        /// <summary>
        /// Defines the _anonCounter
        /// </summary>
        private int _anonCounter = 0;

        /// <summary>
        /// Gets or sets the FileName
        /// </summary>
        public string FileName { get; set; } = fname;

        /// <summary>
        /// Defines the _insns
        /// </summary>
        private readonly List<Instruction> _insns = [];

        /// <summary>
        /// Defines the LoopLeavePatch
        /// </summary>
        private readonly record struct LoopLeavePatch(int Index, int ScopeDepth);

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
        /// Defines the ReceiverContextKind
        /// </summary>
        private enum ReceiverContextKind
        {
            None,
            InstanceMethod,
            StaticMethod
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
        private readonly record struct InheritedMemberInfo(InheritedMemberKind Kind, string OwnerClass, FuncDeclStmt? MethodDecl = null);

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
            "__outer",
            "new"
        };

        /// <summary>
        /// Defines the _breakLists
        /// </summary>
        private readonly Stack<List<LoopLeavePatch>> _breakLists = new();

        /// <summary>
        /// Defines the _continueLists
        /// </summary>
        private readonly Stack<List<LoopLeavePatch>> _continueLists = new();

        /// <summary>
        /// Gets the Functions
        /// </summary>
        public Dictionary<string, FunctionInfo> Functions { get; } = [];

        /// <summary>
        /// Defines the _classInfos
        /// </summary>
        private readonly Dictionary<ClassDeclStmt, ClassInfo> _classInfos = new();

        /// <summary>
        /// Defines the _topLevelClassDecls
        /// </summary>
        private readonly Dictionary<string, ClassDeclStmt> _topLevelClassDecls = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines all known class declarations indexed by qualified path.
        /// </summary>
        private readonly Dictionary<string, ClassDeclStmt> _qualifiedClassDecls = new(StringComparer.Ordinal);

        /// <summary>
        /// Defines the qualified path for each known class declaration.
        /// </summary>
        private readonly Dictionary<ClassDeclStmt, string> _classQualifiedPaths = new();

        /// <summary>
        /// Defines the _classMemberSetCache
        /// </summary>
        private readonly Dictionary<ClassDeclStmt, (HashSet<string> InstanceMembers, HashSet<string> StaticMembers)> _classMemberSetCache = new();

        /// <summary>
        /// Defines the _currentClass
        /// </summary>
        private ClassInfo? _currentClass;

        /// <summary>
        /// Defines the current class declaration being compiled.
        /// </summary>
        private ClassDeclStmt? _currentClassDecl;

        /// <summary>
        /// Defines the _currentMethodIsStatic
        /// </summary>
        private bool _currentMethodIsStatic;

        /// <summary>
        /// Defines the _receiverContext
        /// </summary>
        private ReceiverContextKind _receiverContext;

        /// <summary>
        /// Defines the _localVarsStack
        /// </summary>
        private readonly Stack<HashSet<string>> _localVarsStack = new();

        /// <summary>
        /// Defines the _scopeDepth
        /// </summary>
        private int _scopeDepth;

        /// <summary>
        /// Defines the _asyncFunctionDepth
        /// </summary>
        private int _asyncFunctionDepth;

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
                _insns.Clear();
                Functions.Clear();

                _classInfos.Clear();
                _topLevelClassDecls.Clear();
                _qualifiedClassDecls.Clear();
                _classQualifiedPaths.Clear();
                _classMemberSetCache.Clear();
                _currentClass = null;
                _currentClassDecl = null;
                _currentMethodIsStatic = false;
                _receiverContext = ReceiverContextKind.None;
                _localVarsStack.Clear();
                _scopeDepth = 0;
                _asyncFunctionDepth = 0;

                List<FuncDeclStmt> funcDecls = new();
                List<ClassDeclStmt> classDecls = new();

                foreach (Stmt raw in program)
                {
                    Stmt s = raw is ExportStmt ex ? ex.Inner : raw;

                    if (s is FuncDeclStmt f)
                    {
                        if (Functions.ContainsKey(f.Name))
                            throw new CompilerException(
                                $"duplicate function '{f.Name}'",
                                f.Line, f.Col, f.OriginFile);

                        Functions[f.Name] = new FunctionInfo(f.Parameters, -1, f.MinArgs, f.RestParameter, f.IsAsync);
                        funcDecls.Add(f);
                    }
                    else if (s is ClassDeclStmt c)
                    {
                        classDecls.Add(c);
                        _topLevelClassDecls[c.Name] = c;
                        RegisterQualifiedClassDecl(c, c.Name);
                    }
                    else if (s is BlockStmt b && TryGetNamespaceScopePath(b, out string nsPath))
                    {
                        RegisterNamespaceScopeClasses(b, nsPath);
                    }
                }

                ValidateReservedClassDeclarations(_qualifiedClassDecls.Values.Distinct());

                int jmpOverAllFuncsIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP, null, 0, 0));

                List<(FuncDeclStmt fd, int funcStart)> orderedFuncs = new();

                foreach (FuncDeclStmt fd in funcDecls)
                {
                    try
                    {
                        int funcStart = _insns.Count;
                        Functions[fd.Name] = new FunctionInfo(fd.Parameters, funcStart, fd.MinArgs, fd.RestParameter, fd.IsAsync);

                        if (fd.Body is BlockStmt b)
                            b.IsFunctionBody = true;
                        else
                            throw new CompilerException(
                                $"function '{fd.Name}' must have a block body",
                                fd.Line, fd.Col, fd.OriginFile);

                        ReceiverContextKind prevReceiverContext = _receiverContext;
                        _receiverContext = ReceiverContextKind.None;
                        EnterFunctionLocals(fd.Parameters);
                        if (fd.IsAsync) _asyncFunctionDepth++;
                        try
                        {
                            CompileStmt(fd.Body, insideFunction: true);
                        }
                        finally
                        {
                            if (fd.IsAsync) _asyncFunctionDepth--;
                            LeaveFunctionLocals();
                            _receiverContext = prevReceiverContext;
                        }

                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fd.Line, fd.Col, fd.OriginFile));
                        _insns.Add(new Instruction(OpCode.RET, null, fd.Line, fd.Col, fd.OriginFile));

                        orderedFuncs.Add((fd, funcStart));
                    }

                    catch (CompilerException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new CompilerException(
                            $"internal compiler error while compiling function '{fd.Name}': {ex.Message}",
                            fd.Line, fd.Col, fd.OriginFile);
                    }
                }

                _insns[jmpOverAllFuncsIdx] = new Instruction(OpCode.JMP, _insns.Count, 0, 0);

                foreach ((FuncDeclStmt fd, int funcStart) in orderedFuncs)
                {
                    _insns.Add(new Instruction(
                        OpCode.PUSH_CLOSURE,
                        new object[] { funcStart, fd.Name },
                        fd.Line, fd.Col, fd.OriginFile));

                    _insns.Add(new Instruction(
                        OpCode.VAR_DECL,
                        fd.Name,
                        fd.Line, fd.Col, fd.OriginFile));
                }

                List<ClassDeclStmt> sortedClasses = OrderClassesByInheritance(classDecls);
                ValidateInheritanceOverrides(sortedClasses);
                ValidateBaseConstructorCalls(sortedClasses);

                BuildClassInfos(sortedClasses);

                foreach (ClassDeclStmt cds in sortedClasses)
                {
                    try
                    {
                        CompileStmt(cds, insideFunction: false);
                    }
                    catch (CompilerException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new CompilerException(
                            $"internal compiler error while compiling class '{cds.Name}': {ex.Message}",
                            cds.Line, cds.Col, cds.OriginFile);
                    }
                }

                foreach (Stmt s in program)
                {
                    Stmt unwrapped = s is ExportStmt exportStmt ? exportStmt.Inner : s;
                    if (unwrapped is FuncDeclStmt) continue;
                    if (unwrapped is ClassDeclStmt) continue;

                    try
                    {
                        CompileStmt(s, insideFunction: false);
                    }
                    catch (CompilerException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new CompilerException(
                            $"internal compiler error at top-level: {ex.Message}",
                            s.Line, s.Col, s.OriginFile);
                    }
                }

                _insns.Add(new Instruction(OpCode.HALT, null, 0, 0));
                return _insns;
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
        /// The RegisterQualifiedClassDecl
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="qualifiedPath">The qualifiedPath<see cref="string"/></param>
        private void RegisterQualifiedClassDecl(ClassDeclStmt decl, string qualifiedPath)
        {
            if (!_qualifiedClassDecls.TryAdd(qualifiedPath, decl))
            {
                throw new CompilerException(
                    $"duplicate class '{qualifiedPath}'",
                    decl.Line, decl.Col, decl.OriginFile);
            }

            _classQualifiedPaths[decl] = qualifiedPath;

            foreach (ClassDeclStmt nested in decl.NestedClasses)
                RegisterQualifiedClassDecl(nested, $"{qualifiedPath}.{nested.Name}");
        }

        /// <summary>
        /// The RegisterNamespaceScopeClasses
        /// </summary>
        /// <param name="namespaceScope">The namespaceScope<see cref="BlockStmt"/></param>
        /// <param name="namespacePath">The namespacePath<see cref="string"/></param>
        private void RegisterNamespaceScopeClasses(BlockStmt namespaceScope, string namespacePath)
        {
            foreach (Stmt stmt in namespaceScope.Statements)
            {
                if (stmt is not ClassDeclStmt c)
                    continue;

                RegisterQualifiedClassDecl(c, $"{namespacePath}.{c.Name}");
            }
        }

        /// <summary>
        /// The TryGetNamespaceScopePath
        /// </summary>
        /// <param name="block">The block<see cref="BlockStmt"/></param>
        /// <param name="namespacePath">The namespacePath<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool TryGetNamespaceScopePath(BlockStmt block, out string namespacePath)
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
        /// The ValidateReservedClassDeclarations
        /// </summary>
        /// <param name="classDecls">The classDecls<see cref="IEnumerable{ClassDeclStmt}"/></param>
        private static void ValidateReservedClassDeclarations(IEnumerable<ClassDeclStmt> classDecls)
        {
            foreach (ClassDeclStmt cls in classDecls)
            {
                foreach (string p in cls.Parameters)
                {
                    if (IsReservedRuntimeMemberName(p) || IsReservedInternalMemberName(p))
                    {
                        throw new CompilerException(
                            $"invalid constructor parameter '{p}' in class '{cls.Name}': reserved member name",
                            cls.Line, cls.Col, cls.OriginFile);
                    }
                }

                foreach (string n in cls.Fields.Keys)
                {
                    if (IsReservedRuntimeMemberName(n) || IsReservedInternalMemberName(n))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{n}' in class '{cls.Name}': reserved member name",
                            cls.Line, cls.Col, cls.OriginFile);
                    }
                }

                foreach (string n in cls.StaticFields.Keys)
                {
                    if (IsReservedRuntimeMemberName(n) || IsReservedInternalMemberName(n))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{n}' in class '{cls.Name}': reserved member name",
                            cls.Line, cls.Col, cls.OriginFile);
                    }
                }

                foreach (FuncDeclStmt m in cls.Methods)
                {
                    if (IsReservedRuntimeMemberName(m.Name) || IsReservedInternalMemberName(m.Name))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{m.Name}' in class '{cls.Name}': reserved member name",
                            m.Line, m.Col, m.OriginFile);
                    }
                }

                foreach (FuncDeclStmt m in cls.StaticMethods)
                {
                    if (IsReservedRuntimeMemberName(m.Name) || IsReservedInternalMemberName(m.Name))
                    {
                        throw new CompilerException(
                            $"invalid member declaration '{m.Name}' in class '{cls.Name}': reserved member name",
                            m.Line, m.Col, m.OriginFile);
                    }
                }
            }
        }

        /// <summary>
        /// The OrderClassesByInheritance
        /// </summary>
        /// <param name="classDecls">The classDecls<see cref="List{ClassDeclStmt}"/></param>
        /// <returns>The <see cref="List{ClassDeclStmt}"/></returns>
        private static List<ClassDeclStmt> OrderClassesByInheritance(List<ClassDeclStmt> classDecls)
        {
            Dictionary<string, ClassDeclStmt> byName = new(StringComparer.Ordinal);
            foreach (ClassDeclStmt cds in classDecls)
            {
                if (!byName.TryAdd(cds.Name, cds))
                {
                    throw new CompilerException(
                        $"duplicate class '{cds.Name}'",
                        cds.Line, cds.Col, cds.OriginFile);
                }
            }

            List<ClassDeclStmt> result = new();
            HashSet<string> permMark = new(StringComparer.Ordinal);
            HashSet<string> tempMark = new(StringComparer.Ordinal);

            void Visit(string name)
            {
                if (permMark.Contains(name))
                    return;

                if (tempMark.Contains(name))
                {
                    ClassDeclStmt cds = byName[name];
                    throw new CompilerException(
                        $"cyclic inheritance involving class '{name}'",
                        cds.Line, cds.Col, cds.OriginFile);
                }

                tempMark.Add(name);

                ClassDeclStmt cls = byName[name];

                if (!string.IsNullOrEmpty(cls.BaseName) &&
                    byName.ContainsKey(cls.BaseName))
                {
                    Visit(cls.BaseName);
                }

                tempMark.Remove(name);
                permMark.Add(name);
                result.Add(cls);
            }

            foreach (string name in byName.Keys)
                Visit(name);

            return result;
        }

        /// <summary>
        /// The MethodArityShape
        /// </summary>
        /// <param name="m">The m<see cref="FuncDeclStmt"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string MethodArityShape(FuncDeclStmt m)
        {
            bool hasRest = !string.IsNullOrWhiteSpace(m.RestParameter);
            return hasRest
                ? $"{m.MinArgs}..*"
                : $"{m.MinArgs}..{m.Parameters.Count}";
        }

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
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceField, cls.Name);
                return true;
            }

            FuncDeclStmt? instMethod = cls.Methods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
            if (instMethod != null)
            {
                member = new InheritedMemberInfo(InheritedMemberKind.InstanceMethod, cls.Name, instMethod);
                return true;
            }

            if (cls.StaticFields.ContainsKey(name))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticField, cls.Name);
                return true;
            }

            FuncDeclStmt? staticMethod = cls.StaticMethods.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.Ordinal));
            if (staticMethod != null)
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticMethod, cls.Name, staticMethod);
                return true;
            }

            if (cls.Enums.Any(e => string.Equals(e.Name, name, StringComparison.Ordinal)))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticEnum, cls.Name);
                return true;
            }

            if (cls.NestedClasses.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
            {
                member = new InheritedMemberInfo(InheritedMemberKind.StaticClass, cls.Name);
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
        private static bool TryFindInheritedMember(
            Dictionary<string, ClassDeclStmt> byName,
            ClassDeclStmt cls,
            string name,
            out InheritedMemberInfo member)
        {
            string? baseName = string.IsNullOrWhiteSpace(cls.BaseName) ? null : cls.BaseName;
            while (!string.IsNullOrWhiteSpace(baseName) && byName.TryGetValue(baseName, out ClassDeclStmt? baseCls))
            {
                if (TryFindOwnMember(baseCls, name, out member))
                    return true;

                baseName = string.IsNullOrWhiteSpace(baseCls.BaseName) ? null : baseCls.BaseName;
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
                    $"incompatible override for method '{derivedMethod.Name}' in class '{derivedClass.Name}': expected arity {MethodArityShape(baseMethod)} from base class '{baseMember.OwnerClass}', got {MethodArityShape(derivedMethod)}",
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
            Dictionary<string, ClassDeclStmt> byName,
            ClassDeclStmt derivedClass,
            string memberName,
            InheritedMemberKind derivedKind,
            InheritedMemberInfo baseMember,
            int line,
            int col,
            string file)
        {
            if (!byName.TryGetValue(baseMember.OwnerClass, out ClassDeclStmt? baseDecl))
            {
                throw new CompilerException(
                    $"internal compiler error: missing base class metadata for '{baseMember.OwnerClass}'",
                    line, col, file);
            }

            MemberVisibility baseVisibility = GetDeclaredMemberVisibilityByKind(baseDecl, memberName, baseMember.Kind);
            MemberVisibility derivedVisibility = GetDeclaredMemberVisibilityByKind(derivedClass, memberName, derivedKind);

            if (VisibilityRank(derivedVisibility) < VisibilityRank(baseVisibility))
            {
                throw new CompilerException(
                    $"incompatible visibility override for member '{memberName}' in class '{derivedClass.Name}': inherited member in base class '{baseMember.OwnerClass}' is '{VisibilityLabel(baseVisibility)}', override is '{VisibilityLabel(derivedVisibility)}'",
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
                $"invalid override for member '{memberName}' in class '{derivedClass.Name}': declared as {DerivedMemberKindLabel(derivedKind)} but inherited member in base class '{baseMember.OwnerClass}' is {InheritedMemberKindLabel(baseMember.Kind)}",
                line, col, file);
        }

        /// <summary>
        /// The ValidateInheritanceOverrides
        /// </summary>
        /// <param name="sortedClasses">The sortedClasses<see cref="List{ClassDeclStmt}"/></param>
        private static void ValidateInheritanceOverrides(List<ClassDeclStmt> sortedClasses)
        {
            Dictionary<string, ClassDeclStmt> byName = new(StringComparer.Ordinal);
            foreach (ClassDeclStmt cls in sortedClasses)
                byName[cls.Name] = cls;

            foreach (ClassDeclStmt cls in sortedClasses)
            {
                if (string.IsNullOrWhiteSpace(cls.BaseName))
                    continue;

                foreach (KeyValuePair<string, Expr?> field in cls.Fields)
                {
                    if (!TryFindInheritedMember(byName, cls, field.Key, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, field.Key, InheritedMemberKind.InstanceField, inherited, cls.Line, cls.Col, cls.OriginFile);
                    ValidateMemberVisibilityCompatibility(byName, cls, field.Key, InheritedMemberKind.InstanceField, inherited, cls.Line, cls.Col, cls.OriginFile);
                }

                foreach (FuncDeclStmt method in cls.Methods)
                {
                    if (!TryFindInheritedMember(byName, cls, method.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, method.Name, InheritedMemberKind.InstanceMethod, inherited, method.Line, method.Col, method.OriginFile);
                    ValidateMemberVisibilityCompatibility(byName, cls, method.Name, InheritedMemberKind.InstanceMethod, inherited, method.Line, method.Col, method.OriginFile);
                    ValidateMethodOverrideShape(cls, method, inherited);
                }

                foreach (KeyValuePair<string, Expr?> staticField in cls.StaticFields)
                {
                    if (!TryFindInheritedMember(byName, cls, staticField.Key, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, staticField.Key, InheritedMemberKind.StaticField, inherited, cls.Line, cls.Col, cls.OriginFile);
                    ValidateMemberVisibilityCompatibility(byName, cls, staticField.Key, InheritedMemberKind.StaticField, inherited, cls.Line, cls.Col, cls.OriginFile);
                }

                foreach (FuncDeclStmt staticMethod in cls.StaticMethods)
                {
                    if (!TryFindInheritedMember(byName, cls, staticMethod.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, staticMethod.Name, InheritedMemberKind.StaticMethod, inherited, staticMethod.Line, staticMethod.Col, staticMethod.OriginFile);
                    ValidateMemberVisibilityCompatibility(byName, cls, staticMethod.Name, InheritedMemberKind.StaticMethod, inherited, staticMethod.Line, staticMethod.Col, staticMethod.OriginFile);
                    ValidateMethodOverrideShape(cls, staticMethod, inherited);
                }

                foreach (EnumDeclStmt en in cls.Enums)
                {
                    if (!TryFindInheritedMember(byName, cls, en.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, en.Name, InheritedMemberKind.StaticEnum, inherited, en.Line, en.Col, en.OriginFile);
                    ValidateMemberVisibilityCompatibility(byName, cls, en.Name, InheritedMemberKind.StaticEnum, inherited, en.Line, en.Col, en.OriginFile);
                }

                foreach (ClassDeclStmt nested in cls.NestedClasses)
                {
                    if (!TryFindInheritedMember(byName, cls, nested.Name, out InheritedMemberInfo inherited))
                        continue;

                    ValidateMemberKindCompatibility(cls, nested.Name, InheritedMemberKind.StaticClass, inherited, nested.Line, nested.Col, nested.OriginFile);
                    ValidateMemberVisibilityCompatibility(byName, cls, nested.Name, InheritedMemberKind.StaticClass, inherited, nested.Line, nested.Col, nested.OriginFile);
                }
            }
        }

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
        private static void ValidateBaseConstructorCalls(List<ClassDeclStmt> sortedClasses)
        {
            Dictionary<string, ClassDeclStmt> byName = new(StringComparer.Ordinal);
            foreach (ClassDeclStmt cls in sortedClasses)
                byName[cls.Name] = cls;

            foreach (ClassDeclStmt cls in sortedClasses)
            {
                if (string.IsNullOrWhiteSpace(cls.BaseName))
                    continue;

                if (!byName.TryGetValue(cls.BaseName, out ClassDeclStmt? baseClass))
                    continue;

                ConstructorSignature baseCtor = GetConstructorSignature(baseClass);

                int implicitOuterArgs = 0;
                if (baseClass.IsNested)
                {
                    if (!cls.IsNested)
                    {
                        throw new CompilerException(
                            $"invalid base constructor call in class '{cls.Name}': base class '{baseClass.Name}' is nested and requires an outer instance argument '__outer'",
                            cls.Line, cls.Col, cls.OriginFile);
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

        /// <summary>
        /// The IsReservedRuntimeMemberName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedRuntimeMemberName(string name)
            => ReservedRuntimeMemberNames.Contains(name);

        /// <summary>
        /// The IsReservedInternalMemberName
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsReservedInternalMemberName(string name)
            => name.StartsWith("__", StringComparison.Ordinal);

        /// <summary>
        /// The TryResolveBaseClassDecl
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <param name="baseDecl">The baseDecl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryResolveBaseClassDecl(ClassDeclStmt decl, out ClassDeclStmt baseDecl)
        {
            baseDecl = null!;

            if (string.IsNullOrWhiteSpace(decl.BaseName))
                return false;

            if (decl.BaseName.Contains('.'))
                return _qualifiedClassDecls.TryGetValue(decl.BaseName, out baseDecl!);

            if (_classQualifiedPaths.TryGetValue(decl, out string? currentPath))
            {
                int lastDot = currentPath.LastIndexOf('.');
                if (lastDot > 0)
                {
                    string scopedBasePath = $"{currentPath[..lastDot]}.{decl.BaseName}";
                    if (_qualifiedClassDecls.TryGetValue(scopedBasePath, out baseDecl!))
                        return true;
                }
            }

            return _topLevelClassDecls.TryGetValue(decl.BaseName, out baseDecl!);
        }

        /// <summary>
        /// The TryResolveKnownClassDeclFromPath
        /// </summary>
        /// <param name="classPath">The classPath<see cref="string"/></param>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryResolveKnownClassDeclFromPath(string classPath, out ClassDeclStmt decl)
        {
            decl = null!;
            if (string.IsNullOrWhiteSpace(classPath))
                return false;

            if (_qualifiedClassDecls.TryGetValue(classPath, out decl!))
                return true;

            string[] parts = classPath.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            if (!_topLevelClassDecls.TryGetValue(parts[0], out ClassDeclStmt? current))
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

        /// <summary>
        /// The TryResolveKnownClassDeclFromExpr
        /// </summary>
        /// <param name="expr">The expr<see cref="Expr"/></param>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private bool TryResolveKnownClassDeclFromExpr(Expr expr, out ClassDeclStmt decl)
        {
            decl = null!;

            if (TryExtractQualifiedPath(expr, out string qPath))
            {
                int rootSep = qPath.IndexOf('.');
                string root = rootSep >= 0 ? qPath[..rootSep] : qPath;
                if (!CurrentLocals.Contains(root) && _qualifiedClassDecls.TryGetValue(qPath, out decl!))
                    return true;
            }

            if (expr is VarExpr ve)
            {
                if (CurrentLocals.Contains(ve.Name))
                    return false;
                return _topLevelClassDecls.TryGetValue(ve.Name, out decl!);
            }

            if (expr is not IndexExpr idx || idx.Index is not StringExpr s)
                return false;

            if (idx.Target == null)
                return false;

            if (!TryResolveKnownClassDeclFromExpr(idx.Target, out ClassDeclStmt parent))
                return false;

            ClassDeclStmt? nested = parent.NestedClasses.FirstOrDefault(c => string.Equals(c.Name, s.Value, StringComparison.Ordinal));
            if (nested == null)
                return false;

            decl = nested;
            return true;
        }

        /// <summary>
        /// The GetOrBuildClassMemberSets
        /// </summary>
        /// <param name="decl">The decl<see cref="ClassDeclStmt"/></param>
        /// <returns>The <see cref="(HashSet{string} InstanceMembers, HashSet{string} StaticMembers)"/></returns>
        private (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) GetOrBuildClassMemberSets(ClassDeclStmt decl)
        {
            if (_classMemberSetCache.TryGetValue(decl, out (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) cached))
                return cached;

            HashSet<string> instance = new(StringComparer.Ordinal);
            HashSet<string> statik = new(StringComparer.Ordinal);
            HashSet<ClassDeclStmt> visited = new();

            void Collect(ClassDeclStmt c)
            {
                if (!visited.Add(c))
                    return;

                foreach (string n in c.Fields.Keys)
                    instance.Add(n);
                foreach (FuncDeclStmt m in c.Methods)
                    instance.Add(m.Name);

                ConstructorSignature ctor = GetConstructorSignature(c);
                foreach (string p in ctor.Parameters)
                {
                    if (!string.Equals(p, "__outer", StringComparison.Ordinal))
                        instance.Add(p);
                }

                foreach (string n in c.StaticFields.Keys)
                    statik.Add(n);
                foreach (FuncDeclStmt m in c.StaticMethods)
                    statik.Add(m.Name);
                foreach (EnumDeclStmt en in c.Enums)
                    statik.Add(en.Name);
                foreach (ClassDeclStmt nested in c.NestedClasses)
                    statik.Add(nested.Name);

                if (!string.IsNullOrWhiteSpace(c.BaseName) && TryResolveBaseClassDecl(c, out ClassDeclStmt baseDecl))
                    Collect(baseDecl);
            }

            Collect(decl);

            (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) built = (instance, statik);
            _classMemberSetCache[decl] = built;
            return built;
        }

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
        {
            if (!TryFindMemberVisibilityInHierarchy(decl, memberName, expectInstance, out ClassDeclStmt ownerDecl, out MemberVisibility visibility))
                return;

            if (IsMemberAccessAllowed(ownerDecl, visibility))
                return;

            throw new CompilerException(
                $"inaccessible member '{memberName}' in class '{ownerDecl.Name}': '{VisibilityLabel(visibility)}' access",
                node.Line, node.Col, node.OriginFile);
        }

        /// <summary>
        /// The ValidateMemberAccessAgainstCurrentClass
        /// </summary>
        /// <param name="memberName">The memberName<see cref="string"/></param>
        /// <param name="expectInstance">The expectInstance<see cref="bool"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void ValidateMemberAccessAgainstCurrentClass(string memberName, bool expectInstance, Node node)
        {
            if (_currentClass == null || _currentClassDecl == null)
                return;

            bool hasInstance = _currentClass.IsInstanceMember(memberName);
            bool hasStatic = _currentClass.IsStaticMember(memberName);

            if (expectInstance && !hasInstance && hasStatic)
            {
                throw new CompilerException(
                    $"invalid instance member access '{memberName}' in class '{_currentClass.Name}': member is static",
                    node.Line, node.Col, node.OriginFile);
            }

            if (!expectInstance && hasInstance && !hasStatic)
            {
                throw new CompilerException(
                    $"invalid static member access '{memberName}' in class '{_currentClass.Name}': member is instance",
                    node.Line, node.Col, node.OriginFile);
            }

            ValidateMemberVisibilityAgainstKnownClass(_currentClassDecl, memberName, expectInstance, node);
        }

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
        {
            (HashSet<string> instanceMembers, HashSet<string> staticMembers) = GetOrBuildClassMemberSets(decl);
            bool hasInstance = instanceMembers.Contains(memberName);
            bool hasStatic = staticMembers.Contains(memberName);

            if (expectInstance && !hasInstance && hasStatic)
            {
                throw new CompilerException(
                    $"invalid instance member access '{memberName}' in class '{decl.Name}': member is static",
                    node.Line, node.Col, node.OriginFile);
            }

            if (!expectInstance && hasInstance && !hasStatic)
            {
                throw new CompilerException(
                    $"invalid static member access '{memberName}' in class '{decl.Name}': member is instance",
                    node.Line, node.Col, node.OriginFile);
            }

            ValidateMemberVisibilityAgainstKnownClass(decl, memberName, expectInstance, node);
        }

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
        {
            bool hasKnownDecl = TryResolveKnownClassDeclFromPath(ne.ClassName, out ClassDeclStmt decl);
            if (hasKnownDecl)
                ValidateMemberVisibilityAgainstKnownClass(decl, "new", expectInstance: false, ne);

            if (ne.Initializers == null || ne.Initializers.Count == 0)
                return;

            foreach ((string name, Expr valueExpr) in ne.Initializers)
            {
                if (IsReservedRuntimeMemberName(name))
                {
                    throw new CompilerException(
                        $"invalid initializer member '{name}': reserved member name",
                        valueExpr.Line, valueExpr.Col, valueExpr.OriginFile);
                }
            }

            if (!hasKnownDecl)
                return;

            (HashSet<string> instanceMembers, HashSet<string> _) = GetOrBuildClassMemberSets(decl);
            foreach ((string name, Expr valueExpr) in ne.Initializers)
            {
                if (!instanceMembers.Contains(name))
                {
                    throw new CompilerException(
                        $"unknown initializer member '{name}' for class '{decl.Name}'",
                        valueExpr.Line, valueExpr.Col, valueExpr.OriginFile);
                }

                ValidateMemberVisibilityAgainstKnownClass(decl, name, expectInstance: true, valueExpr);
            }
        }

        /// <summary>
        /// The EmitPushScope
        /// </summary>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitPushScope(Node node)
        {
            _insns.Add(new Instruction(OpCode.PUSH_SCOPE, null, node.Line, node.Col, node.OriginFile));
            _scopeDepth++;
        }

        /// <summary>
        /// The EmitPopScope
        /// </summary>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitPopScope(Node node)
        {
            if (_scopeDepth <= 0)
                throw new CompilerException("internal compiler error: scope underflow while emitting POP_SCOPE", node.Line, node.Col, node.OriginFile);

            _insns.Add(new Instruction(OpCode.POP_SCOPE, null, node.Line, node.Col, node.OriginFile));
            _scopeDepth--;
        }

        /// <summary>
        /// The ScopePopsTo
        /// </summary>
        /// <param name="fromDepth">The fromDepth<see cref="int"/></param>
        /// <param name="targetDepth">The targetDepth<see cref="int"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        /// <returns>The <see cref="int"/></returns>
        private static int ScopePopsTo(int fromDepth, int targetDepth, Node node)
        {
            int pops = fromDepth - targetDepth;
            if (pops < 0)
                throw new CompilerException("internal compiler error: negative scope-pop count for loop leave", node.Line, node.Col, node.OriginFile);
            return pops;
        }

        /// <summary>
        /// The EnterMatchArmLocals
        /// </summary>
        /// <returns>The <see cref="bool"/></returns>
        private bool EnterMatchArmLocals()
        {
            if (_localVarsStack.Count == 0)
                return false;

            _localVarsStack.Push(new HashSet<string>(_localVarsStack.Peek(), StringComparer.Ordinal));
            return true;
        }

        /// <summary>
        /// The LeaveMatchArmLocals
        /// </summary>
        /// <param name="entered">The entered<see cref="bool"/></param>
        private void LeaveMatchArmLocals(bool entered)
        {
            if (entered && _localVarsStack.Count > 0)
                _localVarsStack.Pop();
        }

        /// <summary>
        /// The EmitVarDeclTracked
        /// </summary>
        /// <param name="name">The name<see cref="string"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        /// <param name="trackInLocals">The trackInLocals<see cref="bool"/></param>
        private void EmitVarDeclTracked(string name, Node node, bool trackInLocals)
        {
            _insns.Add(new Instruction(OpCode.VAR_DECL, name, node.Line, node.Col, node.OriginFile));
            if (trackInLocals && _localVarsStack.Count > 0)
                CurrentLocals.Add(name);
        }

        /// <summary>
        /// The EmitPatternFailJump
        /// </summary>
        /// <param name="failJumps">The failJumps<see cref="List{int}"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitPatternFailJump(List<int> failJumps, Node node)
        {
            int idx = _insns.Count;
            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, node.Line, node.Col, node.OriginFile));
            failJumps.Add(idx);
        }

        /// <summary>
        /// The EmitPatternMatch
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <param name="sourceVar">The sourceVar<see cref="string"/></param>
        /// <param name="failJumps">The failJumps<see cref="List{int}"/></param>
        private void EmitPatternMatch(MatchPattern pattern, string sourceVar, List<int> failJumps)
        {
            switch (pattern)
            {
                case WildcardMatchPattern:
                    return;

                case BindingMatchPattern bind:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, bind.Line, bind.Col, bind.OriginFile));
                    EmitVarDeclTracked(bind.Name, bind, trackInLocals: true);
                    return;

                case ValueMatchPattern val:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, val.Line, val.Col, val.OriginFile));
                    CompileExpr(val.Value);
                    _insns.Add(new Instruction(OpCode.EQ, null, val.Line, val.Col, val.OriginFile));
                    EmitPatternFailJump(failJumps, val);
                    return;

                case ArrayMatchPattern arr:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.IS_ARRAY, null, arr.Line, arr.Col, arr.OriginFile));
                    EmitPatternFailJump(failJumps, arr);

                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.LEN, null, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_INT, arr.Elements.Count, arr.Line, arr.Col, arr.OriginFile));
                    _insns.Add(new Instruction(OpCode.EQ, null, arr.Line, arr.Col, arr.OriginFile));
                    EmitPatternFailJump(failJumps, arr);

                    for (int i = 0; i < arr.Elements.Count; i++)
                    {
                        string elemVar = $"__match_elem_{_anonCounter++}";
                        MatchPattern sub = arr.Elements[i];
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, i, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitVarDeclTracked(elemVar, sub, trackInLocals: false);
                        EmitPatternMatch(sub, elemVar, failJumps);
                    }
                    return;

                case DictMatchPattern dict:
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, dict.Line, dict.Col, dict.OriginFile));
                    _insns.Add(new Instruction(OpCode.IS_DICT, null, dict.Line, dict.Col, dict.OriginFile));
                    EmitPatternFailJump(failJumps, dict);

                    foreach ((string key, MatchPattern sub) in dict.Entries)
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, key, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.HAS_KEY, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitPatternFailJump(failJumps, sub);

                        string valueVar = $"__match_key_{_anonCounter++}";
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, key, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitVarDeclTracked(valueVar, sub, trackInLocals: false);
                        EmitPatternMatch(sub, valueVar, failJumps);
                    }
                    return;

                default:
                    throw new CompilerException($"unsupported match pattern '{pattern.GetType().Name}'", pattern.Line, pattern.Col, pattern.OriginFile);
            }
        }

        /// <summary>
        /// Defines the DestructureBindMode
        /// </summary>
        private enum DestructureBindMode
        {
            /// <summary>
            /// Defines the VarDecl
            /// </summary>
            VarDecl,

            /// <summary>
            /// Defines the ConstDecl
            /// </summary>
            ConstDecl,

            /// <summary>
            /// Defines the Assign
            /// </summary>
            Assign
        }

        /// <summary>
        /// The EmitDestructure
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <param name="value">The value<see cref="Expr"/></param>
        /// <param name="mode">The mode<see cref="DestructureBindMode"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitDestructure(MatchPattern pattern, Expr value, DestructureBindMode mode, Node node)
        {
            CompileExpr(value);
            EmitDestructureBindingFromValue(pattern, mode, node);
        }

        /// <summary>
        /// The EmitDestructureBinding
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <param name="sourceVar">The sourceVar<see cref="string"/></param>
        /// <param name="mode">The mode<see cref="DestructureBindMode"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitDestructureBinding(MatchPattern pattern, string sourceVar, DestructureBindMode mode, Node node)
        {
            _insns.Add(new Instruction(OpCode.LOAD_VAR, sourceVar, pattern.Line, pattern.Col, pattern.OriginFile));
            EmitDestructureBindingFromValue(pattern, mode, node);
        }

        /// <summary>
        /// The EmitDestructureBindingFromValue
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <param name="mode">The mode<see cref="DestructureBindMode"/></param>
        /// <param name="node">The node<see cref="Node"/></param>
        private void EmitDestructureBindingFromValue(MatchPattern pattern, DestructureBindMode mode, Node node)
        {
            switch (pattern)
            {
                case WildcardMatchPattern:
                    _insns.Add(new Instruction(OpCode.POP, null, pattern.Line, pattern.Col, pattern.OriginFile));
                    return;

                case BindingMatchPattern bind:
                    switch (mode)
                    {
                        case DestructureBindMode.VarDecl:
                            _insns.Add(new Instruction(OpCode.VAR_DECL, bind.Name, bind.Line, bind.Col, bind.OriginFile));
                            if (_localVarsStack.Count > 0) CurrentLocals.Add(bind.Name);
                            break;
                        case DestructureBindMode.ConstDecl:
                            _insns.Add(new Instruction(OpCode.CONST_DECL, bind.Name, bind.Line, bind.Col, bind.OriginFile));
                            if (_localVarsStack.Count > 0) CurrentLocals.Add(bind.Name);
                            break;
                        case DestructureBindMode.Assign:
                            if (!TryEmitImplicitMemberStore(bind.Name, node))
                                _insns.Add(new Instruction(OpCode.STORE_VAR, bind.Name, bind.Line, bind.Col, bind.OriginFile));
                            break;
                    }
                    return;

                case ArrayMatchPattern arr:
                    for (int i = 0; i < arr.Elements.Count; i++)
                    {
                        MatchPattern sub = arr.Elements[i];
                        _insns.Add(new Instruction(OpCode.DUP, null, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, i, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitDestructureBindingFromValue(sub, mode, node);
                    }
                    _insns.Add(new Instruction(OpCode.POP, null, pattern.Line, pattern.Col, pattern.OriginFile));
                    return;

                case DictMatchPattern dict:
                    foreach ((string key, MatchPattern sub) in dict.Entries)
                    {
                        _insns.Add(new Instruction(OpCode.DUP, null, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, key, sub.Line, sub.Col, sub.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, sub.Line, sub.Col, sub.OriginFile));
                        EmitDestructureBindingFromValue(sub, mode, node);
                    }
                    _insns.Add(new Instruction(OpCode.POP, null, pattern.Line, pattern.Col, pattern.OriginFile));
                    return;

                default:
                    throw new CompilerException($"unsupported destructuring pattern '{pattern.GetType().Name}'", pattern.Line, pattern.Col, pattern.OriginFile);
            }
        }

        /// <summary>
        /// The CollectDestructureBindingNames
        /// </summary>
        /// <param name="pattern">The pattern<see cref="MatchPattern"/></param>
        /// <returns>The <see cref="List{string}"/></returns>
        private static List<string> CollectDestructureBindingNames(MatchPattern pattern)
        {
            List<string> names = new();

            static void Walk(MatchPattern p, List<string> acc)
            {
                switch (p)
                {
                    case WildcardMatchPattern:
                        return;

                    case BindingMatchPattern b:
                        acc.Add(b.Name);
                        return;

                    case ArrayMatchPattern a:
                        foreach (MatchPattern elem in a.Elements)
                            Walk(elem, acc);
                        return;

                    case DictMatchPattern d:
                        foreach ((string _, MatchPattern sub) in d.Entries)
                            Walk(sub, acc);
                        return;

                    default:
                        throw new CompilerException($"unsupported destructuring pattern '{p.GetType().Name}'", p.Line, p.Col, p.OriginFile);
                }
            }

            Walk(pattern, names);
            return names;
        }

        /// <summary>
        /// The CompileStmt
        /// </summary>
        /// <param name="s">The s<see cref="Stmt"/></param>
        /// <param name="insideFunction">The insideFunction<see cref="bool"/></param>
        private void CompileStmt(Stmt s, bool insideFunction)
        {
            switch (s)
            {
                case ExportStmt ex:
                    CompileStmt(ex.Inner, insideFunction);
                    break;

                case VarDecl v:
                    if (v.Value != null)
                        CompileExpr(v.Value);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, s.Line, s.Col, s.OriginFile));

                    _insns.Add(new Instruction(OpCode.VAR_DECL, v.Name, s.Line, s.Col, s.OriginFile));

                    if (_localVarsStack.Count > 0)
                        CurrentLocals.Add(v.Name);

                    break;

                case ConstDecl c:
                    CompileExpr(c.Value);
                    _insns.Add(new Instruction(OpCode.CONST_DECL, c.Name, s.Line, s.Col, s.OriginFile));

                    if (_localVarsStack.Count > 0)
                        CurrentLocals.Add(c.Name);

                    break;

                case DestructureDeclStmt dd:
                    EmitDestructure(dd.Pattern, dd.Value, dd.IsConst ? DestructureBindMode.ConstDecl : DestructureBindMode.VarDecl, dd);
                    break;

                case AssignStmt a:
                    {
                        ValidateReceiverAssignment(a.Name, a);
                        CompileExpr(a.Value);

                        if (!TryEmitImplicitMemberStore(a.Name, a))
                        {
                            _insns.Add(new Instruction(OpCode.STORE_VAR, a.Name, s.Line, s.Col, s.OriginFile));
                        }
                        break;
                    }

                case DestructureAssignStmt da:
                    EmitDestructure(da.Pattern, da.Value, DestructureBindMode.Assign, da);
                    break;

                case EmptyStmt etst:
                    break;

                case AssignIndexExprStmt aies:
                    {
                        CompileExpr(aies.Value);
                        CompileExpr(aies.Target);
                        _insns.Add(new Instruction(OpCode.ROT, null, aies.Line, aies.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET, null, aies.Line, aies.Col, s.OriginFile));
                        break;
                    }

                case SliceSetStmt sliceSet:
                    CompileExpr(sliceSet.Slice.Target);

                    if (sliceSet.Slice.Start is not null)
                        CompileExpr(sliceSet.Slice.Start);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, sliceSet.Line, sliceSet.Col, s.OriginFile));

                    if (sliceSet.Slice.End is not null)
                        CompileExpr(sliceSet.Slice.End);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, sliceSet.Line, sliceSet.Col, s.OriginFile));

                    CompileExpr(sliceSet.Value);

                    _insns.Add(new Instruction(OpCode.SLICE_SET, null, sliceSet.Line, sliceSet.Col, s.OriginFile));
                    break;

                case AssignExprStmt aes:
                    {
                        CompileExpr(aes.Value);
                        CompileLValueStore(aes.Target);
                        break;
                    }

                case PushStmt ps:
                    {
                        CompileExpr(ps.Value);
                        if (ps.Target is VarExpr or IndexExpr)
                        {
                            CompileExpr(ps.Target);
                            _insns.Add(new Instruction(OpCode.ARRAY_PUSH, null, s.Line, s.Col, s.OriginFile));
                        }
                        else
                        {
                            throw new CompilerException("invalid use of 'push' []", ps.Line, ps.Col, s.OriginFile);
                        }
                        break;
                    }

                case DeleteIndexStmt di:
                    {
                        VarExpr targetExpr = new(di.Name, di.Line, di.Col, s.OriginFile);
                        CompileExpr(targetExpr);
                        CompileExpr(di.Index);
                        _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM, null, s.Line, s.Col, s.OriginFile));
                        break;
                    }

                case DeleteVarStmt dv:
                    {
                        VarExpr targetExpr = new(dv.Name, dv.Line, dv.Col, s.OriginFile);
                        CompileExpr(targetExpr);
                        _insns.Add(new Instruction(OpCode.ARRAY_CLEAR, null, dv.Line, dv.Col, s.OriginFile));
                        break;
                    }

                case DeleteExprStmt des:
                    {
                        if (des.Target is SliceExpr se)
                        {
                            CompileExpr(se.Target);

                            if (se.Start != null)
                                CompileExpr(se.Start);
                            else
                                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));

                            if (se.End != null)
                                CompileExpr(se.End);
                            else
                                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, des.Line, des.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_SLICE, null, des.Line, des.Col, s.OriginFile));
                            break;
                        }

                        if (des.Target is IndexExpr ie)
                        {
                            CompileExpr(ie.Target);
                            CompileExpr(ie.Index);
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM, null, des.Line, des.Col, s.OriginFile));
                            break;
                        }

                        if (des.Target is VarExpr v2 && des.DeleteAll)
                        {
                            CompileExpr(v2);
                            _insns.Add(new Instruction(OpCode.ARRAY_CLEAR, null, des.Line, des.Col, s.OriginFile));
                            break;
                        }

                        throw new CompilerException("unsupported delete target", des.Line, des.Col, s.OriginFile);
                    }

                case DeleteAllStmt das:
                    {
                        if (das.Target is VarExpr var)
                        {
                            CompileExpr(var);

                            _insns.Add(new Instruction(OpCode.ARRAY_CLEAR, null, das.Line, das.Col, s.OriginFile));
                        }
                        else if (das.Target is IndexExpr xie)
                        {
                            CompileExpr(xie.Target);
                            CompileExpr(xie.Index);
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_ELEM_ALL, null, das.Line, das.Col, s.OriginFile));
                        }
                        else if (das.Target is SliceExpr xse)
                        {
                            CompileExpr(xse.Target);
                            if (xse.Start != null) CompileExpr(xse.Start); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, das.Line, das.Col, s.OriginFile));
                            if (xse.End != null) CompileExpr(xse.End); else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, das.Line, das.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.ARRAY_DELETE_SLICE, null, das.Line, das.Col, s.OriginFile));
                        }
                        else
                        {
                            throw new CompilerException("invalid use of 'delete'", das.Line, das.Col, s.OriginFile);
                        }
                        break;
                    }

                case ClassDeclStmt cds:
                    {
                        ReceiverContextKind prevReceiverContext = _receiverContext;
                        _receiverContext = ReceiverContextKind.None;
                        try
                        {
                            int jmpOverCtorIdx = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, cds.Line, cds.Col, s.OriginFile));

                        FuncDeclStmt? initMethod = cds.Methods.FirstOrDefault(m => m.Name == "init");
                        List<string> ctorParams = initMethod != null
                            ? new List<string>(initMethod.Parameters)
                            : new List<string>(cds.Parameters);

                        bool insertedOuterParam = false;
                        if (cds.IsNested && (ctorParams.Count == 0 || ctorParams[0] != "__outer"))
                        {
                            ctorParams.Insert(0, "__outer");
                            insertedOuterParam = true;
                        }

                        int ctorMinArgs = initMethod != null ? initMethod.MinArgs : ctorParams.Count;
                        string? ctorRestParameter = initMethod?.RestParameter;
                        if (initMethod != null && insertedOuterParam)
                            ctorMinArgs++;

                        int ctorStart = _insns.Count;
                        string ctorFuncName = $"__ctor_{cds.Name}_{_anonCounter++}";
                        Functions[ctorFuncName] = new FunctionInfo(ctorParams, ctorStart, ctorMinArgs, ctorRestParameter);

                        const string SELF = "__obj";
                        _insns.Add(new Instruction(OpCode.NEW_OBJECT, cds.Name, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, SELF, cds.Line, cds.Col, s.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__type", cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, cds.Name, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));

                        if (!string.IsNullOrEmpty(cds.BaseName))
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__type", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "new", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, cds.Line, cds.Col, s.OriginFile));

                            for (int i = cds.BaseCtorArgs.Count - 1; i >= 0; i--)
                                CompileExpr(cds.BaseCtorArgs[i]);

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, cds.BaseCtorArgs.Count, cds.Line, cds.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.ROT, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        if (cds.IsNested)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__outer", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, "__outer", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (KeyValuePair<string, Expr?> kv in cds.Fields)
                        {
                            string fieldName = kv.Key;
                            Expr? initExpr = kv.Value;

                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, fieldName, cds.Line, cds.Col, s.OriginFile));

                            if (initExpr != null)
                                CompileExpr(initExpr);
                            else
                                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, cds.Line, cds.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (string p in ctorParams)
                        {
                            if (p == "__outer" || IsReservedInternalMemberName(p)) continue;
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, p, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.ROT, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (FuncDeclStmt func in cds.Methods)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, func.Line, func.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, func.Name, func.Line, func.Col, s.OriginFile));

                            List<string> methodParams = new(func.Parameters);
                            methodParams.Insert(0, "this");

                            int methodMinArgs = func.MinArgs + 1;
                            FuncExpr methodFuncExpr = new(methodParams, func.Body, methodMinArgs, func.RestParameter, func.Line, func.Col, s.OriginFile, func.IsAsync);

                            ClassInfo? prevClass = _currentClass;
                            ClassDeclStmt? prevClassDecl = _currentClassDecl;
                            bool prevIsStatic = _currentMethodIsStatic;

                            if (!_classInfos.TryGetValue(cds, out _currentClass))
                                _currentClass = BuildAdHocClassInfo(cds);
                            _currentClassDecl = cds;
                            _currentMethodIsStatic = false;

                            CompileExpr(methodFuncExpr);

                            _currentClass = prevClass;
                            _currentClassDecl = prevClassDecl;
                            _currentMethodIsStatic = prevIsStatic;

                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, func.Line, func.Col, s.OriginFile));
                        }

                        if (initMethod != null)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "init", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, cds.Line, cds.Col, s.OriginFile));

                            for (int i = ctorParams.Count - 1; i >= 0; i--)
                            {
                                string p = ctorParams[i];
                                if (p == "__outer") continue;
                                _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, s.OriginFile));
                                if (string.Equals(initMethod.RestParameter, p, StringComparison.Ordinal))
                                    _insns.Add(new Instruction(OpCode.MAKE_SPREAD_ARG, null, cds.Line, cds.Col, s.OriginFile));
                            }

                            int argCountForInit = ctorParams.Count(p => p != "__outer");
                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, argCountForInit, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.POP, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.RET, null, cds.Line, cds.Col, s.OriginFile));

                        _insns[jmpOverCtorIdx] = new Instruction(OpCode.JMP, _insns.Count, cds.Line, cds.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.NEW_STATIC, cds.Name, cds.Line, cds.Col, s.OriginFile));

                        List<(string Name, int Code)> instanceVisibilityEntries = EnumerateDeclaredInstanceVisibilityEntries(cds);
                        _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__vis_inst", cds.Line, cds.Col, s.OriginFile));
                        foreach ((string memberName, int visCode) in instanceVisibilityEntries)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_STR, memberName, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_INT, visCode, cds.Line, cds.Col, s.OriginFile));
                        }
                        _insns.Add(new Instruction(OpCode.NEW_DICT, instanceVisibilityEntries.Count, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));

                        List<(string Name, int Code)> staticVisibilityEntries = EnumerateDeclaredStaticVisibilityEntries(cds);
                        _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__vis_static", cds.Line, cds.Col, s.OriginFile));
                        foreach ((string memberName, int visCode) in staticVisibilityEntries)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_STR, memberName, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_INT, visCode, cds.Line, cds.Col, s.OriginFile));
                        }
                        _insns.Add(new Instruction(OpCode.NEW_DICT, staticVisibilityEntries.Count, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));

                        if (cds.ConstFields.Count > 0)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__const_inst", cds.Line, cds.Col, s.OriginFile));
                            foreach (string cfName in cds.ConstFields)
                            {
                                _insns.Add(new Instruction(OpCode.PUSH_STR, cfName, cds.Line, cds.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_INT, 1, cds.Line, cds.Col, s.OriginFile));
                            }
                            _insns.Add(new Instruction(OpCode.NEW_DICT, cds.ConstFields.Count, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        if (cds.StaticConstFields.Count > 0)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__const_static", cds.Line, cds.Col, s.OriginFile));
                            foreach (string cfName in cds.StaticConstFields)
                            {
                                _insns.Add(new Instruction(OpCode.PUSH_STR, cfName, cds.Line, cds.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_INT, 1, cds.Line, cds.Col, s.OriginFile));
                            }
                            _insns.Add(new Instruction(OpCode.NEW_DICT, cds.StaticConstFields.Count, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "new", cds.Line, cds.Col, s.OriginFile));
                        _insns.Add(new Instruction(
                            OpCode.PUSH_CLOSURE,
                            new object[] { ctorStart, ctorFuncName },
                            cds.Line, cds.Col, s.OriginFile
                        ));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));

                        if (!string.IsNullOrEmpty(cds.BaseName))
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, cds.BaseName, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (KeyValuePair<string, Expr?> kv in cds.StaticFields)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, kv.Key, cds.Line, cds.Col, s.OriginFile));
                            if (kv.Value != null) CompileExpr(kv.Value);
                            else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, cds.Line, cds.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, s.OriginFile));
                        }

                        foreach (FuncDeclStmt func in cds.StaticMethods)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, func.Line, func.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, func.Name, func.Line, func.Col, s.OriginFile));

                            List<string> methodParams = new(func.Parameters);
                            methodParams.Insert(0, "type");

                            int methodMinArgs = func.MinArgs + 1;
                            FuncExpr methodFuncExpr = new(methodParams, func.Body, methodMinArgs, func.RestParameter, func.Line, func.Col, s.OriginFile, func.IsAsync);

                            ClassInfo? prevClass = _currentClass;
                            ClassDeclStmt? prevClassDecl = _currentClassDecl;
                            bool prevIsStatic = _currentMethodIsStatic;

                            if (!_classInfos.TryGetValue(cds, out _currentClass))
                                _currentClass = BuildAdHocClassInfo(cds);
                            _currentClassDecl = cds;
                            _currentMethodIsStatic = true;

                            CompileExpr(methodFuncExpr);

                            _currentClass = prevClass;
                            _currentClassDecl = prevClassDecl;
                            _currentMethodIsStatic = prevIsStatic;

                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, func.Line, func.Col, s.OriginFile));
                        }

                        foreach (EnumDeclStmt en in cds.Enums)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, en.Line, en.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, en.Name, en.Line, en.Col, s.OriginFile));

                            foreach (EnumMemberNode member in en.Members)
                            {
                                _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, en.Line, en.Col, s.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_INT, (int)member.Value, en.Line, en.Col, s.OriginFile));
                            }

                            _insns.Add(new Instruction(OpCode.PUSH_INT, en.Members.Count, en.Line, en.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.NEW_ENUM, en.Name, en.Line, en.Col, s.OriginFile));

                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, en.Line, en.Col, s.OriginFile));
                        }

                        foreach (ClassDeclStmt inner in cds.NestedClasses)
                        {
                            CompileStmt(inner, insideFunction: false);
                            _insns.Add(new Instruction(OpCode.DUP, null, inner.Line, inner.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, inner.Name, inner.Line, inner.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, inner.Name, inner.Line, inner.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, inner.Line, inner.Col, s.OriginFile));
                        }

                            _insns.Add(new Instruction(OpCode.VAR_DECL, cds.Name, cds.Line, cds.Col, s.OriginFile));
                        }
                        finally
                        {
                            _receiverContext = prevReceiverContext;
                        }
                        break;
                    }

                case EnumDeclStmt eds:
                    {

                        foreach (EnumMemberNode member in eds.Members)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, eds.Line, eds.Col, eds.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_INT, (int)member.Value, eds.Line, eds.Col, eds.OriginFile));
                        }

                        _insns.Add(new Instruction(OpCode.PUSH_INT, eds.Members.Count, eds.Line, eds.Col, eds.OriginFile));
                        _insns.Add(new Instruction(OpCode.NEW_ENUM, eds.Name, eds.Line, eds.Col, eds.OriginFile));

                        _insns.Add(new Instruction(OpCode.VAR_DECL, eds.Name, eds.Line, eds.Col, eds.OriginFile));
                        break;
                    }

                case BlockStmt b:
                    if (insideFunction && b.IsFunctionBody)
                    {
                        foreach (Stmt sub in b.Statements)
                            CompileStmt(sub, insideFunction: true);
                    }
                    else
                    {
                        EmitPushScope(s);
                        try
                        {
                            if (TryGetNamespaceScopePath(b, out _))
                            {
                                List<ClassDeclStmt> namespaceClasses = b.Statements
                                    .OfType<ClassDeclStmt>()
                                    .ToList();

                                List<ClassDeclStmt> sortedNamespaceClasses = OrderClassesByInheritance(namespaceClasses);
                                ValidateInheritanceOverrides(sortedNamespaceClasses);
                                ValidateBaseConstructorCalls(sortedNamespaceClasses);

                                foreach (ClassDeclStmt nsClass in sortedNamespaceClasses)
                                    CompileStmt(nsClass, insideFunction: false);

                                foreach (Stmt sub in b.Statements)
                                {
                                    if (sub is ClassDeclStmt)
                                        continue;

                                    CompileStmt(sub, insideFunction);
                                }
                            }
                            else
                            {
                                foreach (Stmt sub in b.Statements)
                                    CompileStmt(sub, insideFunction);
                            }
                        }
                        finally
                        {
                            EmitPopScope(s);
                        }
                    }
                    break;

                case IfStmt ifs:
                    {
                        CompileExpr(ifs.Condition);
                        int jmpFalseIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col, s.OriginFile));

                        CompileStmt(ifs.ThenBlock, insideFunction);

                        if (ifs.ElseBranch != null)
                        {
                            int jmpEndIdx = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, s.Line, s.Col, s.OriginFile));
                            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);
                            CompileStmt(ifs.ElseBranch, insideFunction);
                            _insns[jmpEndIdx] = new Instruction(OpCode.JMP, _insns.Count, s.Line, s.Col, s.OriginFile);
                        }
                        else
                        {
                            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);
                        }
                        break;
                    }

                case MatchStmt ms:
                    {
                        EmitPushScope(ms);

                        string scrutineeVar = $"__match_scrut_{_anonCounter++}";
                        CompileExpr(ms.Expression);
                        EmitVarDeclTracked(scrutineeVar, ms, trackInLocals: false);

                        List<int> endJumps = new();

                        foreach (CaseClause c in ms.Cases)
                        {
                            bool enteredArmLocals = EnterMatchArmLocals();
                            EmitPushScope(c);

                            List<int> failJumps = new();
                            EmitPatternMatch(c.Pattern, scrutineeVar, failJumps);

                            if (c.Guard != null)
                            {
                                CompileExpr(c.Guard);
                                EmitPatternFailJump(failJumps, c);
                            }

                            CompileStmt(c.Body, insideFunction);
                            EmitPopScope(c);

                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, c.Line, c.Col, c.OriginFile));
                            endJumps.Add(jmpEnd);

                            int failTarget = _insns.Count;
                            foreach (int failIdx in failJumps)
                                _insns[failIdx] = new Instruction(OpCode.JMP_IF_FALSE, failTarget, c.Line, c.Col, c.OriginFile);

                            _insns.Add(new Instruction(OpCode.POP_SCOPE, null, c.Line, c.Col, c.OriginFile));
                            LeaveMatchArmLocals(enteredArmLocals);
                        }

                        if (ms.DefaultCase != null)
                        {
                            CompileStmt(ms.DefaultCase, insideFunction);
                        }

                        int endTarget = _insns.Count;
                        EmitPopScope(ms);
                        foreach (int idx in endJumps)
                            _insns[idx] = new Instruction(OpCode.JMP, endTarget, ms.Line, ms.Col, s.OriginFile);

                        break;
                    }

                case DoWhileStmt dws:
                    {
                        int loopStart = _insns.Count;
                        int loopScopeDepth = _scopeDepth;
                        _breakLists.Push(new List<LoopLeavePatch>());
                        _continueLists.Push(new List<LoopLeavePatch>());

                        CompileStmt(dws.Body, insideFunction);

                        int condStart = _insns.Count;

                        foreach (LoopLeavePatch patch in _continueLists.Peek())
                            _insns[patch.Index] = new Instruction(
                                OpCode.LEAVE,
                                new object[] { condStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                s.Line, s.Col, s.OriginFile);

                        CompileExpr(dws.Condition);
                        int jmpFalseIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col, s.OriginFile));

                        _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile));
                        _insns[jmpFalseIdx] = new Instruction(
                            OpCode.JMP_IF_FALSE,
                            _insns.Count,
                            s.Line, s.Col, s.OriginFile);

                        foreach (LoopLeavePatch patch in _breakLists.Peek())
                            _insns[patch.Index] = new Instruction(
                                OpCode.LEAVE,
                                new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                s.Line, s.Col, s.OriginFile);

                        _breakLists.Pop();
                        _continueLists.Pop();
                        break;
                    }

                case WhileStmt ws:
                    {
                        int loopStart = _insns.Count;
                        int loopScopeDepth = _scopeDepth;
                        _breakLists.Push(new List<LoopLeavePatch>());
                        _continueLists.Push(new List<LoopLeavePatch>());

                        CompileExpr(ws.Condition);
                        int jmpFalseIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col, s.OriginFile));

                        CompileStmt(ws.Body, insideFunction);

                        foreach (LoopLeavePatch patch in _continueLists.Peek())
                            _insns[patch.Index] = new Instruction(
                                OpCode.LEAVE,
                                new object[] { loopStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                s.Line, s.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile));
                        _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);

                        foreach (LoopLeavePatch patch in _breakLists.Peek())
                            _insns[patch.Index] = new Instruction(
                                OpCode.LEAVE,
                                new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                s.Line, s.Col, s.OriginFile);

                        _breakLists.Pop();
                        _continueLists.Pop();
                        break;
                    }

                case ForStmt fs:
                    {
                        EmitPushScope(s);
                        int loopScopeDepth = _scopeDepth;

                        if (fs.Init != null) CompileStmt(fs.Init, insideFunction);
                        int loopStart = _insns.Count;
                        _breakLists.Push(new List<LoopLeavePatch>());
                        _continueLists.Push(new List<LoopLeavePatch>());

                        if (fs.Condition != null)
                        {
                            CompileExpr(fs.Condition);
                            int jmpFalseIdx = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, s.Line, s.Col, s.OriginFile));

                            CompileStmt(fs.Body, insideFunction);

                            int incStart = _insns.Count;
                            foreach (LoopLeavePatch patch in _continueLists.Peek())
                                _insns[patch.Index] = new Instruction(
                                    OpCode.LEAVE,
                                    new object[] { incStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                    s.Line, s.Col, s.OriginFile);

                            if (fs.Increment != null) CompileStmt(fs.Increment, insideFunction);
                            _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile));
                            _insns[jmpFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, s.Line, s.Col, s.OriginFile);

                            foreach (LoopLeavePatch patch in _breakLists.Peek())
                                _insns[patch.Index] = new Instruction(
                                    OpCode.LEAVE,
                                    new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                    s.Line, s.Col, s.OriginFile);
                        }
                        else
                        {
                            CompileStmt(fs.Body, insideFunction);

                            int incStart = _insns.Count;
                            foreach (LoopLeavePatch patch in _continueLists.Peek())
                                _insns[patch.Index] = new Instruction(
                                    OpCode.LEAVE,
                                    new object[] { incStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                    s.Line, s.Col, s.OriginFile);

                            if (fs.Increment != null) CompileStmt(fs.Increment, insideFunction);
                            _insns.Add(new Instruction(OpCode.JMP, loopStart, s.Line, s.Col, s.OriginFile));

                            foreach (LoopLeavePatch patch in _breakLists.Peek())
                                _insns[patch.Index] = new Instruction(
                                    OpCode.LEAVE,
                                    new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, s) },
                                    s.Line, s.Col, s.OriginFile);
                        }

                        _breakLists.Pop();
                        _continueLists.Pop();

                        EmitPopScope(s);
                        break;
                    }

                case ForeachStmt fe:
                    {

                        EmitPushScope(s);
                        int loopScopeDepth = _scopeDepth;

                        string seqNm = $"__fe_seq_{_anonCounter++}";
                        string keysNm = $"__fe_keys_{_anonCounter++}";
                        string lenNm = $"__fe_len_{_anonCounter++}";
                        string idxNm = $"__fe_i_{_anonCounter++}";
                        string isDictNm = $"__fe_isdict_{_anonCounter++}";

                        CompileExpr(fe.Iterable);
                        _insns.Add(new Instruction(OpCode.VAR_DECL, seqNm, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.IS_DICT, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, isDictNm, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, isDictNm, fe.Line, fe.Col, fe.OriginFile));
                        int jmpIfNotDict = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "keys", fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, 0, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, keysNm, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, keysNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "len", fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, 0, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, lenNm, fe.Line, fe.Col, fe.OriginFile));

                        int jmpAfterDictInit = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, fe.Line, fe.Col, fe.OriginFile));

                        int notDictAddr = _insns.Count;
                        _insns[jmpIfNotDict] = new Instruction(OpCode.JMP_IF_FALSE, notDictAddr, fe.Line, fe.Col, fe.OriginFile);

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "len", fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, 0, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, lenNm, fe.Line, fe.Col, fe.OriginFile));

                        int afterInit = _insns.Count;
                        _insns[jmpAfterDictInit] = new Instruction(OpCode.JMP, afterInit, fe.Line, fe.Col, fe.OriginFile);

                        _insns.Add(new Instruction(OpCode.PUSH_INT, 0, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, idxNm, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
                        if (_localVarsStack.Count > 0)
                            CurrentLocals.Add(fe.VarName);

                        if (fe.DeclareLocal && fe.TargetPattern is not null)
                        {
                            foreach (string binding in CollectDestructureBindingNames(fe.TargetPattern))
                            {
                                _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fe.Line, fe.Col, fe.OriginFile));
                                _insns.Add(new Instruction(OpCode.VAR_DECL, binding, fe.Line, fe.Col, fe.OriginFile));
                                if (_localVarsStack.Count > 0)
                                    CurrentLocals.Add(binding);
                            }
                        }

                        int loopStart = _insns.Count;
                        _breakLists.Push(new List<LoopLeavePatch>());
                        _continueLists.Push(new List<LoopLeavePatch>());

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, lenNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.LT, null, fe.Line, fe.Col, fe.OriginFile));
                        int jmpIfFalse = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, isDictNm, fe.Line, fe.Col, fe.OriginFile));
                        int jmpToSeqPath = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, keysNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.SWAP, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, keysNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.SWAP, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.NEW_ARRAY, 2, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.STORE_VAR, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
                        if (fe.TargetPattern is not null)
                            EmitDestructureBinding(fe.TargetPattern, fe.VarName, DestructureBindMode.Assign, fe);

                        int jmpAfterSet = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, fe.Line, fe.Col, fe.OriginFile));

                        int seqPathAddr = _insns.Count;
                        _insns[jmpToSeqPath] = new Instruction(OpCode.JMP_IF_FALSE, seqPathAddr, fe.Line, fe.Col, fe.OriginFile);

                        if (fe.UseIndexValuePair)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.NEW_ARRAY, 2, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.STORE_VAR, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
                        }
                        else
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, seqNm, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, fe.Line, fe.Col, fe.OriginFile));
                            _insns.Add(new Instruction(OpCode.STORE_VAR, fe.VarName, fe.Line, fe.Col, fe.OriginFile));
                        }
                        if (fe.TargetPattern is not null)
                            EmitDestructureBinding(fe.TargetPattern, fe.VarName, DestructureBindMode.Assign, fe);

                        int afterSet = _insns.Count;
                        _insns[jmpAfterSet] = new Instruction(OpCode.JMP, afterSet, fe.Line, fe.Col, fe.OriginFile);

                        CompileStmt(fe.Body, insideFunction);

                        int incStart = _insns.Count;
                        foreach (LoopLeavePatch patch in _continueLists.Peek())
                            _insns[patch.Index] = new Instruction(
                                OpCode.LEAVE,
                                new object[] { incStart, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fe) },
                                fe.Line, fe.Col, fe.OriginFile);

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, 1, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.ADD, null, fe.Line, fe.Col, fe.OriginFile));
                        _insns.Add(new Instruction(OpCode.STORE_VAR, idxNm, fe.Line, fe.Col, fe.OriginFile));

                        _insns.Add(new Instruction(OpCode.JMP, loopStart, fe.Line, fe.Col, fe.OriginFile));
                        _insns[jmpIfFalse] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, fe.Line, fe.Col, fe.OriginFile);

                        foreach (LoopLeavePatch patch in _breakLists.Peek())
                            _insns[patch.Index] = new Instruction(
                                OpCode.LEAVE,
                                new object[] { _insns.Count, ScopePopsTo(patch.ScopeDepth, loopScopeDepth, fe) },
                                fe.Line, fe.Col, fe.OriginFile);

                        _breakLists.Pop();
                        _continueLists.Pop();

                        EmitPopScope(s);
                        break;
                    }

                case TryStmt ts:
                    {
                        int tryPushIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.TRY_PUSH, null, ts.Line, ts.Col, ts.OriginFile));

                        CompileStmt(ts.TryBlock, insideFunction);

                        int afterTryIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.TRY_POP, null, ts.Line, ts.Col, ts.OriginFile));

                        int jmpAfterTryToEndIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, ts.Line, ts.Col, ts.OriginFile));

                        int catchStart = -1;
                        if (ts.CatchBlock != null)
                        {
                            catchStart = _insns.Count;

                            if (ts.CatchIdent != null)
                            {
                                EmitPushScope(ts.CatchBlock);
                                _insns.Add(new Instruction(OpCode.VAR_DECL, ts.CatchIdent, ts.CatchBlock.Line, ts.CatchBlock.Col, ts.CatchBlock.OriginFile));
                            }
                            else
                            {
                                _insns.Add(new Instruction(OpCode.POP, null, ts.CatchBlock.Line, ts.CatchBlock.Col, ts.CatchBlock.OriginFile));
                            }

                            CompileStmt(ts.CatchBlock, insideFunction);

                            if (ts.CatchIdent != null)
                                EmitPopScope(ts.CatchBlock);

                            _insns.Add(new Instruction(OpCode.TRY_POP, null, ts.Line, ts.Col, ts.OriginFile));
                        }

                        int finallyStart = -1;
                        if (ts.FinallyBlock != null)
                        {
                            finallyStart = _insns.Count;

                            EmitPushScope(ts.FinallyBlock);
                            CompileStmt(ts.FinallyBlock, insideFunction);
                            EmitPopScope(ts.FinallyBlock);
                        }

                        int endTryPopIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.TRY_POP, null, ts.Line, ts.Col, ts.OriginFile));

                        _insns[tryPushIdx] = new Instruction(
                            OpCode.TRY_PUSH,
                            new object[] { catchStart, finallyStart },
                            ts.Line, ts.Col, ts.OriginFile
                        );

                        _insns[jmpAfterTryToEndIdx] = new Instruction(
                            OpCode.JMP,
                            endTryPopIdx,
                            ts.Line, ts.Col, ts.OriginFile
                        );

                        break;
                    }

                case ThrowStmt th:
                    {
                        CompileExpr(th.Value);
                        _insns.Add(new Instruction(OpCode.THROW, null, th.Line, th.Col, th.OriginFile));
                        break;
                    }

                case YieldStmt ys:
                    {
                        if (!insideFunction)
                            throw new CompilerException("yield can only be used in function statements", ys.Line, ys.Col, ys.OriginFile);

                        if (_asyncFunctionDepth <= 0)
                            throw new CompilerException("yield can only be used in async function statements", ys.Line, ys.Col, ys.OriginFile);

                        _insns.Add(new Instruction(OpCode.YIELD, null, ys.Line, ys.Col, ys.OriginFile));
                        _insns.Add(new Instruction(OpCode.POP, null, ys.Line, ys.Col, ys.OriginFile));
                        break;
                    }

                case ContinueStmt:
                    {
                        if (_continueLists.Count == 0)
                            throw new VMException("Compile error: 'continue' outside of loop.", s.Line, s.Col, s.OriginFile, false, null!);
                        int leaveIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.LEAVE, null, s.Line, s.Col, s.OriginFile));
                        _continueLists.Peek().Add(new LoopLeavePatch(leaveIdx, _scopeDepth));
                        break;
                    }

                case BreakStmt:
                    {
                        if (_breakLists.Count == 0)
                            throw new VMException("Compile error: 'break' outside of loop.", s.Line, s.Col, s.OriginFile, false, null!);
                        int leaveIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.LEAVE, null, s.Line, s.Col, s.OriginFile));
                        _breakLists.Peek().Add(new LoopLeavePatch(leaveIdx, _scopeDepth));
                        break;
                    }

                case ExprStmt es:
                    CompileExpr(es.Expression);
                    _insns.Add(new Instruction(OpCode.POP, null, s.Line, s.Col, s.OriginFile));
                    break;

                case CompoundAssignStmt ca:
                    CompileLValue(ca.Target, load: true);
                    CompileExpr(ca.Value);

                    _insns.Add(new Instruction(OpFromToken(ca.Op, ca, FileName), null, s.Line, s.Col, s.OriginFile));
                    CompileLValueStore(ca.Target);
                    break;

                case FuncDeclStmt fd:
                    {
                        int jmpOverFuncIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, fd.Line, fd.Col, s.OriginFile));

                        int funcStart = _insns.Count;
                        string internalName = $"__local_{fd.Name}_{_anonCounter++}";
                        Functions[internalName] = new FunctionInfo(fd.Parameters, funcStart, fd.MinArgs, fd.RestParameter, fd.IsAsync);

                        if (fd.Body is BlockStmt fb) fb.IsFunctionBody = true;

                        ReceiverContextKind prevReceiverContext = _receiverContext;
                        _receiverContext = ReceiverContextKind.None;
                        EnterFunctionLocals(fd.Parameters);
                        if (fd.IsAsync) _asyncFunctionDepth++;
                        try
                        {
                            CompileStmt(fd.Body, insideFunction: true);
                        }
                        finally
                        {
                            if (fd.IsAsync) _asyncFunctionDepth--;
                            LeaveFunctionLocals();
                            _receiverContext = prevReceiverContext;
                        }

                        if (_insns.Count == 0 || _insns[^1].Code != OpCode.RET)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, fd.Line, fd.Col, s.OriginFile));
                            _insns.Add(new Instruction(OpCode.RET, null, fd.Line, fd.Col, s.OriginFile));
                        }

                        _insns[jmpOverFuncIdx] = new Instruction(OpCode.JMP, _insns.Count, fd.Line, fd.Col, s.OriginFile);

                        _insns.Add(new Instruction(OpCode.PUSH_CLOSURE, new object[] { funcStart, fd.Name }, fd.Line, fd.Col, s.OriginFile));
                        _insns.Add(new Instruction(OpCode.VAR_DECL, fd.Name, fd.Line, fd.Col, s.OriginFile));

                        if (_localVarsStack.Count > 0)
                            CurrentLocals.Add(fd.Name);

                        break;
                    }

                case ReturnStmt rs:
                    if (rs.Value != null) CompileExpr(rs.Value);
                    else _insns.Add(new Instruction(OpCode.PUSH_NULL, null, s.Line, s.Col, s.OriginFile));
                    _insns.Add(new Instruction(OpCode.RET, null, s.Line, s.Col, s.OriginFile));
                    break;

                default:
                    throw new CompilerException($"unknown statement type {s.GetType().Name}", s.Line, s.Col, s.OriginFile);
            }
        }

        /// <summary>
        /// The CompileExpr
        /// </summary>
        /// <param name="e">The e<see cref="Expr?"/></param>
        private void CompileExpr(Expr? e)
        {
            switch (e)
            {
                case NumberExpr n:
                    if (n.Value is int)
                        _insns.Add(new Instruction(OpCode.PUSH_INT, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is long)
                        _insns.Add(new Instruction(OpCode.PUSH_LNG, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is float)
                        _insns.Add(new Instruction(OpCode.PUSH_FLT, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is double)
                        _insns.Add(new Instruction(OpCode.PUSH_DBL, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is decimal)
                        _insns.Add(new Instruction(OpCode.PUSH_DEC, n.Value, e.Line, e.Col, e.OriginFile));
                    else if (n.Value is BigInteger)
                        _insns.Add(new Instruction(OpCode.PUSH_SPC, n.Value, e.Line, e.Col, e.OriginFile));
                    else
                        throw new CompilerException("invalid number value", n.Line, n.Col, e.OriginFile);
                    break;

                case StringExpr s:
                    _insns.Add(new Instruction(OpCode.PUSH_STR, s.Value, e.Line, e.Col, e.OriginFile));
                    break;

                case CharExpr che:
                    _insns.Add(new Instruction(OpCode.PUSH_CHR, che.Value, e.Line, e.Col, e.OriginFile));
                    break;
                case BoolExpr bxe:
                    _insns.Add(new Instruction(OpCode.PUSH_BOOL, bxe.Value, e.Line, e.Col, e.OriginFile));
                    break;

                case VarExpr v:
                    {
                        string name = v.Name;

                        if (IsReceiverIdentifier(name))
                        {
                            ValidateReceiverUsage(name, v);
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, name, e.Line, e.Col, e.OriginFile));
                            break;
                        }

                        if (CurrentLocals.Contains(name))
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, name, e.Line, e.Col, e.OriginFile));
                            break;
                        }

                        if (!TryEmitImplicitMemberLoad(name, v))
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, name, e.Line, e.Col, e.OriginFile));
                        }
                        break;
                    }

                case ArrayExpr a:
                    foreach (Expr elem in a.Elements) CompileExpr(elem);
                    _insns.Add(new Instruction(OpCode.NEW_ARRAY, a.Elements.Count, e.Line, e.Col, e.OriginFile));
                    break;

                case IndexExpr idx:
                    if (idx.Index == null)
                        throw new CompilerException("empty index '[]' cannot be used as expression", idx.Line, idx.Col, e.OriginFile);

                    ValidateExplicitMemberAccess(idx, isStore: false);
                    CompileExpr(idx.Target);
                    CompileExpr(idx.Index);
                    _insns.Add(new Instruction(OpCode.INDEX_GET, null, idx.Line, idx.Col, e.OriginFile));
                    break;

                case SliceExpr slice:
                    CompileExpr(slice.Target);

                    if (slice.Start is not null)
                        CompileExpr(slice.Start);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, slice.Line, slice.Col, e.OriginFile));

                    if (slice.End is not null)
                        CompileExpr(slice.End);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, slice.Line, slice.Col, e.OriginFile));

                    _insns.Add(new Instruction(OpCode.SLICE_GET, null, slice.Line, slice.Col, e.OriginFile));
                    break;
                case NullExpr nil:
                    _insns.Add(new Instruction(OpCode.PUSH_NULL, null, e.Line, e.Col, e.OriginFile));
                    break;

                case BinaryExpr b:
                    {
                        if (b.Op == TokenType.AndAnd)
                        {
                            CompileExpr(b.Left);

                            int jmpIfLeftFalse = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, b.Line, b.Col, b.OriginFile));

                            CompileExpr(b.Right);

                            int jmpIfRightFalse = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, b.Line, b.Col, b.OriginFile));

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, true, b.Line, b.Col, b.OriginFile));
                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, b.Line, b.Col, b.OriginFile));

                            int lFalse = _insns.Count;
                            _insns[jmpIfLeftFalse] = new Instruction(OpCode.JMP_IF_FALSE, lFalse, b.Line, b.Col, b.OriginFile);
                            _insns[jmpIfRightFalse] = new Instruction(OpCode.JMP_IF_FALSE, lFalse, b.Line, b.Col, b.OriginFile);

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, false, b.Line, b.Col, b.OriginFile));

                            _insns[jmpEnd] = new Instruction(OpCode.JMP, _insns.Count, b.Line, b.Col, b.OriginFile);
                            break;
                        }
                        else if (b.Op == TokenType.OrOr)
                        {
                            CompileExpr(b.Left);

                            int jmpIfLeftTrue = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_TRUE, null, b.Line, b.Col, b.OriginFile));

                            CompileExpr(b.Right);

                            int jmpIfRightTrue = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_TRUE, null, b.Line, b.Col, b.OriginFile));

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, false, b.Line, b.Col, b.OriginFile));
                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, b.Line, b.Col, b.OriginFile));

                            int lTrue = _insns.Count;
                            _insns[jmpIfLeftTrue] = new Instruction(OpCode.JMP_IF_TRUE, lTrue, b.Line, b.Col, b.OriginFile);
                            _insns[jmpIfRightTrue] = new Instruction(OpCode.JMP_IF_TRUE, lTrue, b.Line, b.Col, b.OriginFile);

                            _insns.Add(new Instruction(OpCode.PUSH_BOOL, true, b.Line, b.Col, b.OriginFile));

                            _insns[jmpEnd] = new Instruction(OpCode.JMP, _insns.Count, b.Line, b.Col, b.OriginFile);
                            break;
                        }
                        else if (b.Op == TokenType.QQNull)
                        {
                            CompileExpr(b.Left);

                            _insns.Add(new Instruction(OpCode.DUP, null, b.Line, b.Col, b.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, b.Line, b.Col, b.OriginFile));
                            _insns.Add(new Instruction(OpCode.EQ, null, b.Line, b.Col, b.OriginFile));

                            int jmpIfNull = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP_IF_TRUE, null, b.Line, b.Col, b.OriginFile));

                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, b.Line, b.Col, b.OriginFile));

                            _insns[jmpIfNull] = new Instruction(OpCode.JMP_IF_TRUE, _insns.Count, b.Line, b.Col, b.OriginFile);

                            _insns.Add(new Instruction(OpCode.POP, null, b.Line, b.Col, b.OriginFile));
                            CompileExpr(b.Right);

                            _insns[jmpEnd] = new Instruction(OpCode.JMP, _insns.Count, b.Line, b.Col, b.OriginFile);
                            break;
                        }

                        OpCode op = OpFromToken(b.Op, b, FileName);
                        CompileExpr(b.Left);
                        CompileExpr(b.Right);
                        _insns.Add(new Instruction(op, null, b.Line, b.Col, b.OriginFile));
                        break;
                    }

                case UnaryExpr ue:
                    CompileExpr(ue.Right);
                    switch (ue.Op)
                    {
                        case TokenType.Minus: _insns.Add(new Instruction(OpCode.NEG, null, e.Line, e.Col, e.OriginFile)); break;
                        case TokenType.Plus: break;
                        case TokenType.Not: _insns.Add(new Instruction(OpCode.NOT, null, e.Line, e.Col, e.OriginFile)); break;
                        default: throw new CompilerException($"unknown unary operator {ue.Op}", ue.Line, ue.Col, e.OriginFile);
                    }
                    break;

                case DictExpr d:
                    foreach ((Expr k, Expr v) in d.Pairs)
                    {
                        CompileExpr(k);
                        CompileExpr(v);
                    }
                    _insns.Add(new Instruction(OpCode.NEW_DICT, d.Pairs.Count, e.Line, e.Col, e.OriginFile));
                    break;

                case PostfixExpr pf:
                    CompileLValue(pf.Target, load: true);
                    _insns.Add(new Instruction(OpCode.DUP, null, e.Line, e.Col, e.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_INT, 1, e.Line, e.Col, e.OriginFile));
                    _insns.Add(new Instruction(pf.Op == TokenType.PlusPlus ? OpCode.ADD : OpCode.SUB, null, e.Line, e.Col, e.OriginFile));
                    CompileLValueStore(pf.Target);
                    break;

                case PrefixExpr pre:
                    CompileLValue(pre.Target, load: true);
                    _insns.Add(new Instruction(OpCode.PUSH_INT, 1, e.Line, e.Col, e.OriginFile));
                    _insns.Add(new Instruction(pre.Op == TokenType.PlusPlus ? OpCode.ADD : OpCode.SUB, null, e.Line, e.Col, e.OriginFile));
                    CompileLValueStore(pre.Target);
                    CompileLValue(pre.Target, load: true);
                    break;

                case AwaitExpr aw:
                    {

                        CompileExpr(aw.Inner);
                        _insns.Add(new Instruction(OpCode.AWAIT, null, e.Line, e.Col, e.OriginFile));
                        break;
                    }
                case CallExpr call:
                    {
                        if (call.Target is IndexExpr ie)
                        {
                            CompileExpr(ie.Target);
                            if (ie.Index is StringExpr s)
                                _insns.Add(new Instruction(OpCode.PUSH_STR, s.Value, e.Line, e.Col, e.OriginFile));
                            else
                                CompileExpr(ie.Index);

                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, e.Line, e.Col, e.OriginFile));

                            for (int i = call.Args.Count - 1; i >= 0; i--)
                                CompileExpr(call.Args[i]);

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, call.Args.Count, e.Line, e.Col, e.OriginFile));
                        }
                        else
                        {
                            CompileExpr(call.Target);

                            for (int i = call.Args.Count - 1; i >= 0; i--)
                                CompileExpr(call.Args[i]);

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, call.Args.Count, e.Line, e.Col, e.OriginFile));
                        }
                        break;
                    }

                case NewExpr ne:
                    {
                        string[] parts = ne.ClassName.Split('.');
                        bool usedOuterBinding = false;
                        ValidateNewObjectInitializers(ne);

                        if (_currentClass != null && !_currentMethodIsStatic)
                        {
                            if (parts.Length == 1)
                            {
                                if (_currentClassDecl != null && _classInfos.TryGetValue(_currentClassDecl, out ClassInfo? ci)
                                    && ci.StaticMembers.Contains(parts[0]))
                                {
                                    _insns.Add(new Instruction(OpCode.LOAD_VAR, "this", ne.Line, ne.Col, e.OriginFile));
                                    _insns.Add(new Instruction(OpCode.PUSH_STR, parts[0], ne.Line, ne.Col, e.OriginFile));
                                    _insns.Add(new Instruction(OpCode.INDEX_GET, null, ne.Line, ne.Col, e.OriginFile));
                                    usedOuterBinding = true;
                                }
                            }
                            else if (string.Equals(parts[0], _currentClass.Name, StringComparison.Ordinal))
                            {
                                _insns.Add(new Instruction(OpCode.LOAD_VAR, "this", ne.Line, ne.Col, e.OriginFile));
                                for (int i = 1; i < parts.Length; i++)
                                {
                                    _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], ne.Line, ne.Col, e.OriginFile));
                                    _insns.Add(new Instruction(OpCode.INDEX_GET, null, ne.Line, ne.Col, e.OriginFile));
                                }
                                usedOuterBinding = true;
                            }
                        }

                        if (!usedOuterBinding)
                        {
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, parts[0], ne.Line, ne.Col, e.OriginFile));
                            for (int i = 1; i < parts.Length; i++)
                            {
                                _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], ne.Line, ne.Col, e.OriginFile));
                                _insns.Add(new Instruction(OpCode.INDEX_GET, null, ne.Line, ne.Col, e.OriginFile));
                            }
                        }

                        for (int i = ne.Args.Count - 1; i >= 0; i--)
                            CompileExpr(ne.Args[i]);

                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, ne.Args.Count, ne.Line, ne.Col, e.OriginFile));

                        if (ne.Initializers != null && ne.Initializers.Count > 0)
                        {
                            foreach ((string name, Expr valueExpr) in ne.Initializers)
                            {
                                _insns.Add(new Instruction(OpCode.DUP, null, ne.Line, ne.Col, e.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_STR, name, ne.Line, ne.Col, e.OriginFile));
                                CompileExpr(valueExpr);
                                _insns.Add(new Instruction(OpCode.INDEX_SET, null, ne.Line, ne.Col, e.OriginFile));
                            }
                        }

                        break;
                    }

                case ObjectInitExpr oi:
                    {
                        if (oi.Target is CallExpr ce &&
                            ce.Target is IndexExpr ie &&
                            ie.Index is StringExpr keyStr)
                        {
                            string tmpOuter = $"__tmp_outer_{_anonCounter++}";

                            CompileExpr(ie.Target);
                            _insns.Add(new Instruction(OpCode.VAR_DECL, tmpOuter, oi.Line, oi.Col, oi.OriginFile));

                            _insns.Add(new Instruction(OpCode.LOAD_VAR, tmpOuter, oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, "__type", oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, oi.Line, oi.Col, oi.OriginFile));

                            _insns.Add(new Instruction(OpCode.PUSH_STR, keyStr.Value, oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, oi.Line, oi.Col, oi.OriginFile));

                            _insns.Add(new Instruction(OpCode.PUSH_STR, "new", oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_GET, null, oi.Line, oi.Col, oi.OriginFile));

                            for (int i = ce.Args.Count - 1; i >= 0; i--)
                                CompileExpr(ce.Args[i]);

                            _insns.Add(new Instruction(OpCode.LOAD_VAR, tmpOuter, oi.Line, oi.Col, oi.OriginFile));

                            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, ce.Args.Count + 1, oi.Line, oi.Col, oi.OriginFile));

                            _insns.Add(new Instruction(OpCode.DUP, null, oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.LOAD_VAR, tmpOuter, oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, keyStr.Value, oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.ROT, null, oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, oi.Line, oi.Col, oi.OriginFile));

                            foreach ((string fieldName, Expr fieldExpr) in oi.Inits)
                            {
                                _insns.Add(new Instruction(OpCode.DUP, null, oi.Line, oi.Col, oi.OriginFile));
                                _insns.Add(new Instruction(OpCode.PUSH_STR, fieldName, oi.Line, oi.Col, oi.OriginFile));
                                CompileExpr(fieldExpr);
                                _insns.Add(new Instruction(OpCode.INDEX_SET, null, oi.Line, oi.Col, oi.OriginFile));
                            }

                            break;
                        }

                        CompileExpr(oi.Target);
                        foreach ((string fieldName, Expr fieldExpr) in oi.Inits)
                        {
                            _insns.Add(new Instruction(OpCode.DUP, null, oi.Line, oi.Col, oi.OriginFile));
                            _insns.Add(new Instruction(OpCode.PUSH_STR, fieldName, oi.Line, oi.Col, oi.OriginFile));
                            CompileExpr(fieldExpr);
                            _insns.Add(new Instruction(OpCode.INDEX_SET, null, oi.Line, oi.Col, oi.OriginFile));
                        }
                        break;
                    }

                case MethodCallExpr mce:
                    {
                        CompileExpr(mce.Target);
                        _insns.Add(new Instruction(OpCode.PUSH_STR, mce.Method, mce.Line, mce.Col, e.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, null, mce.Line, mce.Col, e.OriginFile));

                        for (int i = mce.Args.Count - 1; i >= 0; i--)
                            CompileExpr(mce.Args[i]);

                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, mce.Args.Count, mce.Line, mce.Col, e.OriginFile));
                        break;
                    }

                case FuncExpr fe:
                    {
                        int jmpOverFuncIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, e.Line, e.Col, e.OriginFile));

                        int funcStart = _insns.Count;

                        string anonName = $"__anon_{_anonCounter++}";
                        Functions[anonName] = new FunctionInfo(fe.Parameters, funcStart, fe.MinArgs, fe.RestParameter, fe.IsAsync);

                        ReceiverContextKind prevReceiverContext = _receiverContext;
                        _receiverContext = DetermineReceiverContext(fe);
                        EnterFunctionLocals(fe.Parameters);
                        if (fe.IsAsync) _asyncFunctionDepth++;
                        try
                        {
                            CompileStmt(fe.Body, insideFunction: true);
                        }
                        finally
                        {
                            if (fe.IsAsync) _asyncFunctionDepth--;
                            LeaveFunctionLocals();
                            _receiverContext = prevReceiverContext;
                        }

                        if (_insns.Count == 0 || _insns[^1].Code != OpCode.RET)
                        {
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, e.Line, e.Col, e.OriginFile));
                            _insns.Add(new Instruction(OpCode.RET, null, e.Line, e.Col, e.OriginFile));
                        }

                        _insns[jmpOverFuncIdx] = new Instruction(OpCode.JMP, _insns.Count, e.Line, e.Col, e.OriginFile);

                        _insns.Add(new Instruction(OpCode.PUSH_CLOSURE, new object[] { funcStart, anonName }, e.Line, e.Col, e.OriginFile));
                        break;
                    }

                case NamedArgExpr na:
                    {
                        CompileExpr(na.Value);
                        _insns.Add(new Instruction(OpCode.MAKE_NAMED_ARG, na.Name, e.Line, e.Col, e.OriginFile));
                        break;
                    }

                case SpreadArgExpr sa:
                    {
                        CompileExpr(sa.Value);
                        _insns.Add(new Instruction(OpCode.MAKE_SPREAD_ARG, null, e.Line, e.Col, e.OriginFile));
                        break;
                    }

                case OutExpr ox:
                    {
                        List<Stmt> stmts = ox.Body.Statements;

                        int lastExprIdx = -1;
                        for (int i = stmts.Count - 1; i >= 0; i--)
                            if (stmts[i] is ExprStmt) { lastExprIdx = i; break; }

                        EmitPushScope(ox);

                        for (int i = 0; i < stmts.Count; i++)
                        {
                            if (stmts[i] is ExprStmt es)
                            {
                                CompileExpr(es.Expression);
                                if (i != lastExprIdx)
                                    _insns.Add(new Instruction(OpCode.POP, null, es.Line, es.Col, es.OriginFile));
                            }
                            else
                            {
                                CompileStmt(stmts[i], insideFunction: false);
                            }
                        }

                        if (lastExprIdx == -1)
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, ox.Line, ox.Col, ox.OriginFile));

                        EmitPopScope(ox);
                        break;
                    }

                case ConditionalExpr cnd:
                    {
                        CompileExpr(cnd.Condition);

                        int jmpIfFalseIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP_IF_FALSE, null, e.Line, e.Col, e.OriginFile));

                        CompileExpr(cnd.ThenExpr);

                        int jmpEndIdx = _insns.Count;
                        _insns.Add(new Instruction(OpCode.JMP, null, e.Line, e.Col, e.OriginFile));

                        _insns[jmpIfFalseIdx] = new Instruction(OpCode.JMP_IF_FALSE, _insns.Count, e.Line, e.Col, e.OriginFile);

                        CompileExpr(cnd.ElseExpr);

                        _insns[jmpEndIdx] = new Instruction(OpCode.JMP, _insns.Count, e.Line, e.Col, e.OriginFile);
                        break;
                    }

                case MatchExpr me:
                    {
                        EmitPushScope(me);

                        string scrutineeVar = $"__match_scrut_{_anonCounter++}";
                        CompileExpr(me.Scrutinee);
                        EmitVarDeclTracked(scrutineeVar, me, trackInLocals: false);

                        List<int> endJumps = new();

                        foreach (CaseExprArm arm in me.Arms)
                        {
                            bool enteredArmLocals = EnterMatchArmLocals();
                            EmitPushScope(arm);

                            List<int> failJumps = new();
                            EmitPatternMatch(arm.Pattern, scrutineeVar, failJumps);

                            if (arm.Guard != null)
                            {
                                CompileExpr(arm.Guard);
                                EmitPatternFailJump(failJumps, arm);
                            }

                            CompileExpr(arm.Body);
                            EmitPopScope(arm);

                            int jmpEnd = _insns.Count;
                            _insns.Add(new Instruction(OpCode.JMP, null, arm.Line, arm.Col, arm.OriginFile));
                            endJumps.Add(jmpEnd);

                            int failTarget = _insns.Count;
                            foreach (int failIdx in failJumps)
                                _insns[failIdx] = new Instruction(OpCode.JMP_IF_FALSE, failTarget, arm.Line, arm.Col, arm.OriginFile);

                            _insns.Add(new Instruction(OpCode.POP_SCOPE, null, arm.Line, arm.Col, arm.OriginFile));
                            LeaveMatchArmLocals(enteredArmLocals);
                        }

                        if (me.DefaultArm != null)
                            CompileExpr(me.DefaultArm);
                        else
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, me.Line, me.Col, me.OriginFile));

                        int endTarget = _insns.Count;
                        EmitPopScope(me);
                        foreach (int idx in endJumps)
                            _insns[idx] = new Instruction(OpCode.JMP, endTarget, me.Line, me.Col, me.OriginFile);

                        break;
                    }

                default:
                    throw new CompilerException($"unknown expr type {e?.GetType().Name}", e?.Line ?? -1, e?.Col ?? -1, e?.OriginFile ?? "");
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
        private void BuildClassInfos(List<ClassDeclStmt> sortedClasses)
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
