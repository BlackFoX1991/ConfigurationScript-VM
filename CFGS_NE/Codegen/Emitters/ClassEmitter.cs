using System;
using System.Collections.Generic;
using System.Linq;
using CFGS_VM.Analytic.Tokens;
using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        private static string GetPropertyBackingSlotName(PropertyDeclStmt property)
            => property.IsStatic
                ? $"__prop_back_static_{property.Name}"
                : $"__prop_back_{property.Name}";

        private static string GetPropertyAccessorSlotName(PropertyDeclStmt property, PropertyAccessorKind kind)
        {
            string accessorName = kind switch
            {
                PropertyAccessorKind.Get => "get",
                PropertyAccessorKind.Set => "set",
                PropertyAccessorKind.Init => "init",
                _ => "acc"
            };

            return property.IsStatic
                ? $"__prop_static_{accessorName}_{property.Name}"
                : $"__prop_{accessorName}_{property.Name}";
        }

        private static PropertyAccessorDecl? GetPropertyAccessor(PropertyDeclStmt property, PropertyAccessorKind kind)
            => property.Accessors.FirstOrDefault(a => a.Kind == kind);

        private static MemberVisibility GetEffectiveAccessorVisibility(PropertyDeclStmt property, PropertyAccessorDecl accessor)
            => accessor.HasExplicitVisibility ? accessor.Visibility : property.Visibility;

        private static RuntimePropertyDescriptor CreateRuntimePropertyDescriptor(PropertyDeclStmt property)
        {
            PropertyAccessorDecl? getter = GetPropertyAccessor(property, PropertyAccessorKind.Get);
            PropertyAccessorDecl? setter = GetPropertyAccessor(property, PropertyAccessorKind.Set);
            PropertyAccessorDecl? initAccessor = GetPropertyAccessor(property, PropertyAccessorKind.Init);

            return new RuntimePropertyDescriptor(
                property.Name,
                property.IsStatic,
                getter != null,
                setter != null,
                initAccessor != null,
                getter != null ? ToVisibilityCode(GetEffectiveAccessorVisibility(property, getter)) : 0,
                setter != null ? ToVisibilityCode(GetEffectiveAccessorVisibility(property, setter)) : 0,
                initAccessor != null ? ToVisibilityCode(GetEffectiveAccessorVisibility(property, initAccessor)) : 0,
                getter is { IsAuto: false } ? GetPropertyAccessorSlotName(property, PropertyAccessorKind.Get) : null,
                setter is { IsAuto: false } ? GetPropertyAccessorSlotName(property, PropertyAccessorKind.Set) : null,
                initAccessor is { IsAuto: false } ? GetPropertyAccessorSlotName(property, PropertyAccessorKind.Init) : null,
                property.HasAutoStorage ? GetPropertyBackingSlotName(property) : null,
                property.HasAutoStorage);
        }

        private FuncExpr BuildPropertyAccessorFuncExpr(PropertyDeclStmt property, PropertyAccessorDecl accessor)
        {
            List<string> parameters = new()
            {
                property.IsStatic ? "type" : "this"
            };

            if (!string.IsNullOrWhiteSpace(accessor.ValueParameterName))
                parameters.Add(accessor.ValueParameterName);

            return new FuncExpr(
                parameters,
                accessor.Body ?? new BlockStmt(new List<Stmt>(), accessor.Line, accessor.Col, accessor.OriginFile),
                parameters.Count,
                restParameter: null,
                accessor.Line,
                accessor.Col,
                accessor.OriginFile,
                isAsync: false);
        }

        private void EmitPropertyDescriptorMap(string slotName, IEnumerable<PropertyDeclStmt> properties, Node node)
        {
            List<PropertyDeclStmt> propertyList = properties.ToList();
            if (propertyList.Count == 0)
                return;

            _insns.Add(new Instruction(OpCode.DUP, null, node.Line, node.Col, node.OriginFile));
            _insns.Add(new Instruction(OpCode.PUSH_STR, slotName, node.Line, node.Col, node.OriginFile));
            foreach (PropertyDeclStmt property in propertyList)
            {
                _insns.Add(new Instruction(OpCode.PUSH_STR, property.Name, property.Line, property.Col, property.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_CONST, CreateRuntimePropertyDescriptor(property), property.Line, property.Col, property.OriginFile));
            }
            _insns.Add(new Instruction(OpCode.NEW_DICT_INTERNAL, propertyList.Count, node.Line, node.Col, node.OriginFile));
            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, node.Line, node.Col, node.OriginFile));
        }

        private void CompilePropertyAccessorClosure(PropertyDeclStmt property, PropertyAccessorDecl accessor, ClassDeclStmt classDecl)
        {
            if (accessor.IsAuto || accessor.Body == null)
                return;

            if (property.IsStatic)
                _insns.Add(new Instruction(OpCode.DUP, null, accessor.Line, accessor.Col, accessor.OriginFile));
            else
                _insns.Add(new Instruction(OpCode.LOAD_VAR, "__obj", accessor.Line, accessor.Col, accessor.OriginFile));
            _insns.Add(new Instruction(OpCode.PUSH_STR, GetPropertyAccessorSlotName(property, accessor.Kind), accessor.Line, accessor.Col, accessor.OriginFile));

            ClassInfo? prevClass = _currentClass;
            ClassDeclStmt? prevClassDecl = _currentClassDecl;
            bool prevIsStatic = _currentMethodIsStatic;
            string? prevPropertyBackingSlotName = _currentPropertyBackingSlotName;
            string? prevPropertyBackingReceiverName = _currentPropertyBackingReceiverName;

            if (!_classInfos.TryGetValue(classDecl, out ClassInfo? currentClass))
                currentClass = BuildAdHocClassInfo(classDecl);
            _currentClass = currentClass;
            _currentClassDecl = classDecl;
            _currentMethodIsStatic = property.IsStatic;
            _currentPropertyBackingSlotName = property.HasAutoStorage ? GetPropertyBackingSlotName(property) : null;
            _currentPropertyBackingReceiverName = property.HasAutoStorage
                ? (property.IsStatic ? "type" : "this")
                : null;

            CompileExpr(BuildPropertyAccessorFuncExpr(property, accessor));

            _currentClass = prevClass;
            _currentClassDecl = prevClassDecl;
            _currentMethodIsStatic = prevIsStatic;
            _currentPropertyBackingSlotName = prevPropertyBackingSlotName;
            _currentPropertyBackingReceiverName = prevPropertyBackingReceiverName;

            _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, accessor.Line, accessor.Col, accessor.OriginFile));
        }

        // Registers the runtime field shape (declared fields + auto-promoted ctor params + property backing
        // slots) for this class's OWN layer, so instances store fields in a Value[] instead of a dictionary.
        // A duplicated simple name (NEW_OBJECT keys on it) is marked ambiguous → dictionary fallback.
        private void RegisterClassFieldShape(ClassDeclStmt cds)
        {
            ScopeDescriptorBuilder builder = new();
            foreach (KeyValuePair<string, Expr?> field in cds.Fields)
                builder.Declare(field.Key);
            foreach (string promoted in cds.GetEffectiveConstructorSignature(includeImplicitOuter: cds.IsNested).GetAutoPromotedFieldNames())
                builder.Declare(promoted);
            foreach (PropertyDeclStmt property in cds.Properties.Where(p => p.HasAutoStorage))
                builder.Declare(GetPropertyBackingSlotName(property));

            _context.ClassFieldShapes[cds.Name] = _context.ClassFieldShapes.ContainsKey(cds.Name)
                ? null
                : builder.Build();
        }

        private void CompileClassDeclaration(ClassDeclStmt cds)
        {
            RegisterClassFieldShape(cds);

            ReceiverContextKind prevReceiverContext = _receiverContext;
            _receiverContext = ReceiverContextKind.None;
            try
            {
                int jmpOverCtorIdx = _insns.Count;
                _insns.Add(new Instruction(OpCode.JMP, null, cds.Line, cds.Col, cds.OriginFile));

                FuncDeclStmt? initMethod = cds.GetInitMethod();
                ClassConstructorSignature ctorSignature = cds.GetEffectiveConstructorSignature(includeImplicitOuter: cds.IsNested);
                List<string> ctorParams = ctorSignature.Parameters;
                List<FunctionParameterSpec> ctorParameterSpecs = ctorSignature.ParameterSpecs;
                List<string> autoPromotedCtorMembers = ctorSignature.GetAutoPromotedFieldNames();
                int ctorMinArgs = ctorSignature.MinArgs;
                string? ctorRestParameter = ctorSignature.RestParameter;
                List<Stmt> ctorParamInitializers = BuildConstructorParamInitializers(ctorParameterSpecs);

                int ctorStart = _insns.Count;
                string ctorFuncName = $"__ctor_{cds.Name}_{_anonCounter++}";
                Functions[ctorFuncName] = new FunctionInfo(ctorParams, ctorStart, ctorMinArgs, ctorRestParameter);

                const string SELF = "__obj";
                EnterFunctionLocals(ctorParams);
                try
                {
                    foreach (Stmt ctorInitializer in ctorParamInitializers)
                        CompileStmt(ctorInitializer, insideFunction: true);

                    _insns.Add(new Instruction(OpCode.NEW_OBJECT, cds.Name, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.VAR_DECL, SELF, cds.Line, cds.Col, cds.OriginFile));

                    _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, "__type", cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, cds.Name, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));

                    if (!string.IsNullOrEmpty(cds.BaseName))
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__type", cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, true, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, true, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "new", cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, true, cds.Line, cds.Col, cds.OriginFile));

                        for (int i = cds.BaseCtorArgs.Count - 1; i >= 0; i--)
                            CompileExpr(cds.BaseCtorArgs[i]);

                        _insns.Add(new Instruction(OpCode.CALL_INDIRECT, cds.BaseCtorArgs.Count, cds.Line, cds.Col, cds.OriginFile));

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.ROT, null, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                    }

                    if (cds.IsNested)
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, "__outer", cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, "__outer", cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                    }

                    foreach (KeyValuePair<string, Expr?> kv in cds.Fields)
                    {
                        string fieldName = kv.Key;
                        Expr? initExpr = kv.Value;

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, fieldName, cds.Line, cds.Col, cds.OriginFile));

                        if (initExpr != null)
                            CompileExpr(initExpr);
                        else
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, cds.Line, cds.Col, cds.OriginFile));

                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                    }

                    foreach (PropertyDeclStmt property in cds.Properties.Where(p => p.HasAutoStorage))
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, property.Line, property.Col, property.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, GetPropertyBackingSlotName(property), property.Line, property.Col, property.OriginFile));
                        if (property.Initializer != null)
                            CompileExpr(property.Initializer);
                        else
                            _insns.Add(new Instruction(OpCode.PUSH_NULL, null, property.Line, property.Col, property.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, property.Line, property.Col, property.OriginFile));
                    }

                    foreach (string p in autoPromotedCtorMembers)
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, p, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.ROT, null, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                    }
                }
                finally
                {
                    LeaveFunctionLocals();
                }

                // Instance methods are hoisted onto the shared class (StaticInstance) once at class-declaration
                // time — see EmitInstanceMethodsOntoStaticInstance below — instead of being rebuilt into every
                // instance by this constructor. The instance keeps only its own fields; method access falls
                // back to the class via __type.

                foreach (PropertyDeclStmt property in cds.Properties)
                {
                    foreach (PropertyAccessorDecl accessor in property.Accessors)
                        CompilePropertyAccessorClosure(property, accessor, cds);
                }

                if (initMethod != null)
                {
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, "init", cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_GET, true, cds.Line, cds.Col, cds.OriginFile));

                    for (int i = ctorParams.Count - 1; i >= 0; i--)
                    {
                        string p = ctorParams[i];
                        if (p == "__outer")
                            continue;

                        _insns.Add(new Instruction(OpCode.LOAD_VAR, p, cds.Line, cds.Col, cds.OriginFile));
                        if (string.Equals(ctorRestParameter, p, StringComparison.Ordinal))
                            _insns.Add(new Instruction(OpCode.MAKE_SPREAD_ARG, null, cds.Line, cds.Col, cds.OriginFile));
                    }

                    int argCountForInit = ctorParams.Count(p => p != "__outer");
                    _insns.Add(new Instruction(OpCode.CALL_INDIRECT, argCountForInit, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.POP, null, cds.Line, cds.Col, cds.OriginFile));
                }

                _insns.Add(new Instruction(OpCode.LOAD_VAR, SELF, cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(OpCode.RET, null, cds.Line, cds.Col, cds.OriginFile));

                _insns[jmpOverCtorIdx] = new Instruction(OpCode.JMP, _insns.Count, cds.Line, cds.Col, cds.OriginFile);

                _insns.Add(new Instruction(OpCode.NEW_STATIC, cds.Name, cds.Line, cds.Col, cds.OriginFile));

                // Define each instance method ONCE onto the shared class (the StaticInstance on the stack).
                // At call time p.method() finds it via __type (see the instance-method fallback in
                // ObjectRuntime); the closure takes `this` as its first parameter, so it is bound per call.
                foreach (FuncDeclStmt func in cds.Methods)
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, func.Line, func.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, func.RuntimeMemberName, func.Line, func.Col, cds.OriginFile));

                    List<string> methodParams = new(func.Parameters);
                    methodParams.Insert(0, "this");

                    int methodMinArgs = func.MinArgs + 1;
                    FuncExpr methodFuncExpr = new(methodParams, func.Body, methodMinArgs, func.RestParameter, func.Line, func.Col, cds.OriginFile, func.IsAsync);

                    ClassInfo? prevClass = _currentClass;
                    ClassDeclStmt? prevClassDecl = _currentClassDecl;
                    bool prevIsStatic = _currentMethodIsStatic;

                    if (!_classInfos.TryGetValue(cds, out ClassInfo? currentClass))
                        currentClass = BuildAdHocClassInfo(cds);
                    _currentClass = currentClass;
                    _currentClassDecl = cds;
                    _currentMethodIsStatic = false;

                    CompileExpr(methodFuncExpr);

                    _currentClass = prevClass;
                    _currentClassDecl = prevClassDecl;
                    _currentMethodIsStatic = prevIsStatic;

                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, func.Line, func.Col, cds.OriginFile));
                }

                List<(string Name, int Code)> instanceVisibilityEntries = EnumerateDeclaredInstanceVisibilityEntries(cds);
                _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, "__vis_inst", cds.Line, cds.Col, cds.OriginFile));
                foreach ((string memberName, int visCode) in instanceVisibilityEntries)
                {
                    _insns.Add(new Instruction(OpCode.PUSH_STR, memberName, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_INT, visCode, cds.Line, cds.Col, cds.OriginFile));
                }
                _insns.Add(new Instruction(OpCode.NEW_DICT_INTERNAL, instanceVisibilityEntries.Count, cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));

                List<(string Name, int Code)> staticVisibilityEntries = EnumerateDeclaredStaticVisibilityEntries(cds);
                _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, "__vis_static", cds.Line, cds.Col, cds.OriginFile));
                foreach ((string memberName, int visCode) in staticVisibilityEntries)
                {
                    _insns.Add(new Instruction(OpCode.PUSH_STR, memberName, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_INT, visCode, cds.Line, cds.Col, cds.OriginFile));
                }
                _insns.Add(new Instruction(OpCode.NEW_DICT_INTERNAL, staticVisibilityEntries.Count, cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));

                EmitPropertyDescriptorMap("__props_inst", cds.Properties, cds);
                EmitPropertyDescriptorMap("__props_static", cds.StaticProperties, cds);

                if (cds.ConstFields.Count > 0)
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, "__const_inst", cds.Line, cds.Col, cds.OriginFile));
                    foreach (string cfName in cds.ConstFields)
                    {
                        _insns.Add(new Instruction(OpCode.PUSH_STR, cfName, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, 1, cds.Line, cds.Col, cds.OriginFile));
                    }
                    _insns.Add(new Instruction(OpCode.NEW_DICT_INTERNAL, cds.ConstFields.Count, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                }

                if (cds.StaticConstFields.Count > 0)
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, "__const_static", cds.Line, cds.Col, cds.OriginFile));
                    foreach (string cfName in cds.StaticConstFields)
                    {
                        _insns.Add(new Instruction(OpCode.PUSH_STR, cfName, cds.Line, cds.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, 1, cds.Line, cds.Col, cds.OriginFile));
                    }
                    _insns.Add(new Instruction(OpCode.NEW_DICT_INTERNAL, cds.StaticConstFields.Count, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                }

                _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, "new", cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(
                    OpCode.PUSH_CLOSURE,
                    new object[] { ctorStart, ctorFuncName },
                    cds.Line, cds.Col, cds.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));

                if (!string.IsNullOrEmpty(cds.BaseName))
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, "__base", cds.Line, cds.Col, cds.OriginFile));
                    EmitLoadQualifiedRuntimeValue(cds.BaseName, cds);
                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                }

                if (cds.ImplementedInterfaces.Count > 0)
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, "__interfaces", cds.Line, cds.Col, cds.OriginFile));
                    foreach (string ifaceName in cds.ImplementedInterfaces)
                        EmitLoadQualifiedRuntimeValue(ifaceName, cds);
                    _insns.Add(new Instruction(OpCode.NEW_ARRAY, cds.ImplementedInterfaces.Count, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                }

                foreach (KeyValuePair<string, Expr?> kv in cds.StaticFields)
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, kv.Key, cds.Line, cds.Col, cds.OriginFile));
                    if (kv.Value != null)
                        CompileExpr(kv.Value);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, cds.Line, cds.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, cds.Line, cds.Col, cds.OriginFile));
                }

                foreach (PropertyDeclStmt property in cds.StaticProperties.Where(p => p.HasAutoStorage))
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, property.Line, property.Col, property.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, GetPropertyBackingSlotName(property), property.Line, property.Col, property.OriginFile));
                    if (property.Initializer != null)
                        CompileExpr(property.Initializer);
                    else
                        _insns.Add(new Instruction(OpCode.PUSH_NULL, null, property.Line, property.Col, property.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, property.Line, property.Col, property.OriginFile));
                }

                foreach (FuncDeclStmt func in cds.StaticMethods)
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, func.Line, func.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, func.RuntimeMemberName, func.Line, func.Col, cds.OriginFile));

                    List<string> methodParams = new(func.Parameters);
                    methodParams.Insert(0, "type");

                    int methodMinArgs = func.MinArgs + 1;
                    FuncExpr methodFuncExpr = new(methodParams, func.Body, methodMinArgs, func.RestParameter, func.Line, func.Col, cds.OriginFile, func.IsAsync);

                    ClassInfo? prevClass = _currentClass;
                    ClassDeclStmt? prevClassDecl = _currentClassDecl;
                    bool prevIsStatic = _currentMethodIsStatic;

                    if (!_classInfos.TryGetValue(cds, out ClassInfo? currentClass))
                        currentClass = BuildAdHocClassInfo(cds);
                    _currentClass = currentClass;
                    _currentClassDecl = cds;
                    _currentMethodIsStatic = true;

                    CompileExpr(methodFuncExpr);

                    _currentClass = prevClass;
                    _currentClassDecl = prevClassDecl;
                    _currentMethodIsStatic = prevIsStatic;

                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, func.Line, func.Col, cds.OriginFile));
                }

                foreach (PropertyDeclStmt property in cds.StaticProperties)
                {
                    foreach (PropertyAccessorDecl accessor in property.Accessors)
                        CompilePropertyAccessorClosure(property, accessor, cds);
                }

                foreach (EnumDeclStmt en in cds.Enums)
                {
                    _insns.Add(new Instruction(OpCode.DUP, null, en.Line, en.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.PUSH_STR, en.Name, en.Line, en.Col, cds.OriginFile));

                    foreach (EnumMemberNode member in en.Members)
                    {
                        _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, en.Line, en.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_INT, (int)member.Value, en.Line, en.Col, cds.OriginFile));
                    }

                    _insns.Add(new Instruction(OpCode.PUSH_INT, en.Members.Count, en.Line, en.Col, cds.OriginFile));
                    _insns.Add(new Instruction(OpCode.NEW_ENUM, en.Name, en.Line, en.Col, cds.OriginFile));

                    _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, en.Line, en.Col, cds.OriginFile));
                }

                foreach (ClassDeclStmt inner in cds.NestedClasses)
                {
                    EmitPushScope(inner);
                    try
                    {
                        CompileStmt(inner, insideFunction: false);
                        _insns.Add(new Instruction(OpCode.DUP, null, inner.Line, inner.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, inner.Name, inner.Line, inner.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, inner.Name, inner.Line, inner.Col, cds.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_SET_INTERNAL, null, inner.Line, inner.Col, cds.OriginFile));
                    }
                    finally
                    {
                        EmitPopScope(inner);
                    }
                }

                _insns.Add(new Instruction(OpCode.VAR_DECL, cds.Name, cds.Line, cds.Col, cds.OriginFile));
            }
            finally
            {
                _receiverContext = prevReceiverContext;
            }
        }

        private static List<Stmt> BuildConstructorParamInitializers(IEnumerable<FunctionParameterSpec> parameterSpecs)
        {
            List<Stmt> statements = new();
            foreach (FunctionParameterSpec parameter in parameterSpecs)
            {
                if (parameter.IsRest)
                    continue;

                if (parameter.DefaultValue is not null)
                {
                    // Mirror the ParamLowerer prologue: apply the default only when the constructor argument
                    // was omitted, not when an explicit null was passed. The presence test reads the Unset
                    // sentinel that BuildCallEnv leaves in an omitted optional-parameter slot.
                    Expr condition = new ArgUnsetTestExpr(parameter.Name, parameter.Line, parameter.Col, parameter.File);

                    BlockStmt thenBlock = new(
                        new List<Stmt>
                        {
                            new AssignStmt(parameter.Name, parameter.DefaultValue, parameter.Line, parameter.Col, parameter.File)
                        },
                        parameter.Line,
                        parameter.Col,
                        parameter.File);

                    statements.Add(new IfStmt(condition, thenBlock, null, parameter.Line, parameter.Col, parameter.File));
                }

                if (parameter.DestructurePattern is not null)
                {
                    statements.Add(new DestructureDeclStmt(
                        parameter.DestructurePattern,
                        new VarExpr(parameter.Name, parameter.Line, parameter.Col, parameter.File),
                        isConst: false,
                        parameter.Line,
                        parameter.Col,
                        parameter.File));
                }
            }

            return statements;
        }

        private void CompileNewExpression(NewExpr ne)
        {
            string[] parts = ne.ClassName.Split('.');
            bool usedOuterBinding = false;

            if (_currentClass != null && !_currentMethodIsStatic)
            {
                if (parts.Length == 1)
                {
                    if (_currentClass.IsStaticMember(parts[0]))
                    {
                        _insns.Add(new Instruction(OpCode.LOAD_VAR, "this", ne.Line, ne.Col, ne.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, parts[0], ne.Line, ne.Col, ne.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, true, ne.Line, ne.Col, ne.OriginFile));
                        usedOuterBinding = true;
                    }
                }
                else if (string.Equals(parts[0], _currentClass.Name, StringComparison.Ordinal))
                {
                    _insns.Add(new Instruction(OpCode.LOAD_VAR, "this", ne.Line, ne.Col, ne.OriginFile));
                    for (int i = 1; i < parts.Length; i++)
                    {
                        _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], ne.Line, ne.Col, ne.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, true, ne.Line, ne.Col, ne.OriginFile));
                    }
                    usedOuterBinding = true;
                }
            }

            if (!usedOuterBinding)
            {
                if (TryEmitHiddenNamespaceRuntimeValueLoad(ne.ClassName, ne))
                {
                    usedOuterBinding = true;
                }
            }

            if (!usedOuterBinding)
            {
                _insns.Add(new Instruction(OpCode.LOAD_VAR, parts[0], ne.Line, ne.Col, ne.OriginFile));
                for (int i = 1; i < parts.Length; i++)
                {
                    _insns.Add(new Instruction(OpCode.PUSH_STR, parts[i], ne.Line, ne.Col, ne.OriginFile));
                    _insns.Add(new Instruction(OpCode.INDEX_GET, true, ne.Line, ne.Col, ne.OriginFile));
                }
            }

            for (int i = ne.Args.Count - 1; i >= 0; i--)
                CompileExpr(ne.Args[i]);

            _insns.Add(new Instruction(OpCode.CALL_INDIRECT, ne.Args.Count, ne.Line, ne.Col, ne.OriginFile));

            if (ne.Initializers != null && ne.Initializers.Count > 0)
                CompileObjectInitializerEntries(ne.Initializers, ne);
        }

        private void CompileObjectInitializerExpression(ObjectInitExpr oi)
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
                _insns.Add(new Instruction(OpCode.INDEX_GET, true, oi.Line, oi.Col, oi.OriginFile));

                _insns.Add(new Instruction(OpCode.PUSH_STR, keyStr.Value, oi.Line, oi.Col, oi.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, true, oi.Line, oi.Col, oi.OriginFile));

                _insns.Add(new Instruction(OpCode.PUSH_STR, "new", oi.Line, oi.Col, oi.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_GET, true, oi.Line, oi.Col, oi.OriginFile));

                for (int i = ce.Args.Count - 1; i >= 0; i--)
                    CompileExpr(ce.Args[i]);

                _insns.Add(new Instruction(OpCode.LOAD_VAR, tmpOuter, oi.Line, oi.Col, oi.OriginFile));
                _insns.Add(new Instruction(OpCode.CALL_INDIRECT, ce.Args.Count + 1, oi.Line, oi.Col, oi.OriginFile));

                _insns.Add(new Instruction(OpCode.DUP, null, oi.Line, oi.Col, oi.OriginFile));
                _insns.Add(new Instruction(OpCode.LOAD_VAR, tmpOuter, oi.Line, oi.Col, oi.OriginFile));
                _insns.Add(new Instruction(OpCode.PUSH_STR, keyStr.Value, oi.Line, oi.Col, oi.OriginFile));
                _insns.Add(new Instruction(OpCode.ROT, null, oi.Line, oi.Col, oi.OriginFile));
                _insns.Add(new Instruction(OpCode.INDEX_INIT, ie.IsDotAccess ? true : null, oi.Line, oi.Col, oi.OriginFile));

                CompileObjectInitializerEntries(oi.Inits, oi);

                return;
            }

            CompileExpr(oi.Target);
            CompileObjectInitializerEntries(oi.Inits, oi);
        }

        private void CompileObjectInitializerEntries(IReadOnlyList<ObjectInitializerEntry> entries, Node origin)
        {
            foreach (ObjectInitializerEntry entry in entries)
            {
                switch (entry)
                {
                    case ObjectMemberInitializerEntry member when member.IsNested:
                        _insns.Add(new Instruction(OpCode.DUP, null, member.Line, member.Col, member.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, member.Line, member.Col, member.OriginFile));
                        _insns.Add(new Instruction(OpCode.INDEX_GET, true, member.Line, member.Col, member.OriginFile));
                        CompileObjectInitializerEntries(member.NestedInitializers, member);
                        _insns.Add(new Instruction(OpCode.POP, null, member.Line, member.Col, member.OriginFile));
                        break;

                    case ObjectMemberInitializerEntry member:
                        _insns.Add(new Instruction(OpCode.DUP, null, member.Line, member.Col, member.OriginFile));
                        _insns.Add(new Instruction(OpCode.PUSH_STR, member.Name, member.Line, member.Col, member.OriginFile));
                        CompileExpr(member.Value ?? throw new InvalidOperationException("Object initializer member value is missing."));
                        _insns.Add(new Instruction(OpCode.INDEX_INIT, true, member.Line, member.Col, member.OriginFile));
                        break;

                    case ObjectIndexerInitializerEntry indexer:
                        _insns.Add(new Instruction(OpCode.DUP, null, indexer.Line, indexer.Col, indexer.OriginFile));
                        CompileExpr(indexer.Index);
                        CompileExpr(indexer.Value);
                        _insns.Add(new Instruction(OpCode.INDEX_INIT, null, indexer.Line, indexer.Col, indexer.OriginFile));
                        break;
                }
            }
        }
    }
}
