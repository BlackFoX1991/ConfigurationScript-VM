using CFGS_VM.Analytic.Semantics;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
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
        /// The TryResolveKnownClassDeclFromPath
        /// </summary>
        private bool TryResolveKnownClassDeclFromPath(string classPath, out ClassDeclStmt decl)
            => new MemberAccessRules().TryResolveKnownClassDeclFromPath(this, classPath, out decl);

        /// <summary>
        /// The TryResolveKnownClassDeclFromExpr
        /// </summary>
        private bool TryResolveKnownClassDeclFromExpr(Expr expr, out ClassDeclStmt decl)
            => new MemberAccessRules().TryResolveKnownClassDeclFromExpr(this, expr, CurrentLocals, out decl);

        /// <summary>
        /// The GetOrBuildClassMemberSets
        /// </summary>
        private (HashSet<string> InstanceMembers, HashSet<string> StaticMembers) GetOrBuildClassMemberSets(ClassDeclStmt decl)
            => new MemberAccessRules().GetOrBuildClassMemberSets(this, decl);

        /// <summary>
        /// The ValidateMemberVisibilityAgainstKnownClass
        /// </summary>
        private void ValidateMemberVisibilityAgainstKnownClass(
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            Node node,
            MemberAccessRules.MemberAccessKind accessKind = MemberAccessRules.MemberAccessKind.Read)
            => new MemberAccessRules().ValidateMemberVisibilityAgainstKnownClass(this, decl, memberName, expectInstance, _currentClassDecl, node, accessKind);

        /// <summary>
        /// The ValidateMemberAccessAgainstCurrentClass
        /// </summary>
        private void ValidateMemberAccessAgainstCurrentClass(
            string memberName,
            bool expectInstance,
            Node node,
            MemberAccessRules.MemberAccessKind accessKind = MemberAccessRules.MemberAccessKind.Read)
            => new MemberAccessRules().ValidateMemberAccessAgainstCurrentClass(this, _currentClass, _currentClassDecl, memberName, expectInstance, node, accessKind);

        /// <summary>
        /// The ValidateMemberAccessAgainstKnownClass
        /// </summary>
        private void ValidateMemberAccessAgainstKnownClass(
            ClassDeclStmt decl,
            string memberName,
            bool expectInstance,
            Node node,
            MemberAccessRules.MemberAccessKind accessKind = MemberAccessRules.MemberAccessKind.Read)
            => new MemberAccessRules().ValidateMemberAccessAgainstKnownClass(this, decl, memberName, expectInstance, _currentClassDecl, node, accessKind);

        /// <summary>
        /// The ValidateExplicitMemberAccess
        /// </summary>
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
                        ValidateMemberAccessAgainstCurrentClass(memberName, expectInstance: true, idx, isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
                        return;
                    case "type":
                        ValidateMemberAccessAgainstCurrentClass(memberName, expectInstance: false, idx, isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
                        return;
                    case "super":
                        if (string.IsNullOrWhiteSpace(_currentClass.BaseName))
                            return;
                        if (_currentClassDecl != null && TryResolveBaseClassDecl(_currentClassDecl, out ClassDeclStmt baseDecl))
                        {
                            bool expectInstance = _receiverContext == ReceiverContextKind.InstanceMethod;
                            ValidateMemberAccessAgainstKnownClass(baseDecl, memberName, expectInstance, idx, isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
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

                ValidateMemberAccessAgainstKnownClass(decl, memberName, expectInstance: false, idx, isStore ? MemberAccessRules.MemberAccessKind.Write : MemberAccessRules.MemberAccessKind.Read);
            }
        }

        /// <summary>
        /// The ValidateNewObjectInitializers
        /// </summary>
        private void ValidateNewObjectInitializers(NewExpr ne)
            => new MemberAccessRules().ValidateNewObjectInitializers(this, ne, _currentClassDecl);

        /// <summary>
        /// The IsReceiverIdentifier
        /// </summary>
        private static bool IsReceiverIdentifier(string name)
            => name == "this" || name == "type" || name == "super" || name == "outer";

        /// <summary>
        /// Defines the reserved property backing field identifier.
        /// </summary>
        private static bool IsPropertyBackingFieldIdentifier(string name)
            => name == "field";

        /// <summary>
        /// The DetermineReceiverContext
        /// </summary>
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
        /// Emits the current property's hidden backing slot target.
        /// </summary>
        private bool TryEmitCurrentPropertyBackingSlotTarget(Node node)
        {
            if (string.IsNullOrWhiteSpace(_currentPropertyBackingSlotName) ||
                string.IsNullOrWhiteSpace(_currentPropertyBackingReceiverName))
            {
                return false;
            }

            _insns.Add(new Instruction(OpCode.LOAD_VAR, _currentPropertyBackingReceiverName, node.Line, node.Col, node.OriginFile));
            _insns.Add(new Instruction(OpCode.PUSH_STR, _currentPropertyBackingSlotName, node.Line, node.Col, node.OriginFile));
            return true;
        }

        /// <summary>
        /// Tries to emit a read from the current property's hidden backing field.
        /// </summary>
        private bool TryEmitPropertyBackingFieldLoad(string name, Node node)
        {
            if (!IsPropertyBackingFieldIdentifier(name))
                return false;

            if (!TryEmitCurrentPropertyBackingSlotTarget(node))
                return false;

            _insns.Add(new Instruction(OpCode.INDEX_GET, null, node.Line, node.Col, node.OriginFile));
            return true;
        }

        /// <summary>
        /// Tries to emit a write to the current property's hidden backing field.
        /// </summary>
        private bool TryEmitPropertyBackingFieldStore(string name, Node node)
        {
            if (!IsPropertyBackingFieldIdentifier(name))
                return false;

            if (!TryEmitCurrentPropertyBackingSlotTarget(node))
                return false;

            _insns.Add(new Instruction(OpCode.ROT, null, node.Line, node.Col, node.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, node.Line, node.Col, node.OriginFile));
            return true;
        }

        /// <summary>
        /// The ResolveImplicitMemberResolution
        /// </summary>
        private ImplicitMemberResolutionKind ResolveImplicitMemberResolution(string name)
        {
            if (_currentClass == null)
                return ImplicitMemberResolutionKind.None;

            if (name == "this" || name == "type" || name == "super" || name == "outer" ||
                (IsPropertyBackingFieldIdentifier(name) && !string.IsNullOrWhiteSpace(_currentPropertyBackingSlotName)))
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
        private bool TryEmitImplicitMemberLoad(string name, Node node)
        {
            if (TryEmitPropertyBackingFieldLoad(name, node))
                return true;

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
        private bool TryEmitImplicitMemberStore(string name, Node node)
        {
            if (TryEmitPropertyBackingFieldStore(name, node))
                return true;

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
