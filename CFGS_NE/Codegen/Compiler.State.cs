using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        /// <summary>
        /// Defines the _insns
        /// </summary>
        private List<Instruction> _insns => _context.Instructions;

        /// <summary>
        /// Defines the _breakLists
        /// </summary>
        private Stack<List<LoopLeavePatch>> _breakLists => _emission.BreakLists;

        /// <summary>
        /// Defines the _continueLists
        /// </summary>
        private Stack<List<LoopLeavePatch>> _continueLists => _emission.ContinueLists;

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
        /// Defines the _interfacePropertyContractCache
        /// </summary>
        private Dictionary<InterfaceDeclStmt, Dictionary<string, InterfacePropertyDecl>> _interfacePropertyContractCache => _context.InterfacePropertyContractCache;

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
        /// Defines the current property backing slot name for accessor compilation.
        /// </summary>
        private string? _currentPropertyBackingSlotName
        {
            get => _emission.CurrentPropertyBackingSlotName;
            set => _emission.CurrentPropertyBackingSlotName = value;
        }

        /// <summary>
        /// Defines the current property backing receiver variable name for accessor compilation.
        /// </summary>
        private string? _currentPropertyBackingReceiverName
        {
            get => _emission.CurrentPropertyBackingReceiverName;
            set => _emission.CurrentPropertyBackingReceiverName = value;
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
    }
}
