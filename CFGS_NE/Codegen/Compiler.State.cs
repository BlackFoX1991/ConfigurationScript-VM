using CFGS_VM.Analytic.Tree;
using CFGS_VM.VMCore.Command;
using CFGS_VM.VMCore.Codegen;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore
{
    public partial class Compiler
    {
        private List<Instruction> _insns => _context.Instructions;

        private Stack<List<LoopLeavePatch>> _breakLists => _emission.BreakLists;

        private Stack<List<LoopLeavePatch>> _continueLists => _emission.ContinueLists;

        private Dictionary<ClassDeclStmt, ClassInfo> _classInfos => _context.ClassInfos;

        private Dictionary<string, ClassDeclStmt> _topLevelClassDecls => _context.TopLevelClassDecls;

        private Dictionary<string, InterfaceDeclStmt> _topLevelInterfaceDecls => _context.TopLevelInterfaceDecls;

        /// <summary>
        /// Defines all known class declarations indexed by qualified path.
        /// </summary>
        private Dictionary<string, ClassDeclStmt> _qualifiedClassDecls => _context.QualifiedClassDecls;

        /// <summary>
        /// Defines all known interface declarations indexed by qualified path.
        /// </summary>
        private Dictionary<string, InterfaceDeclStmt> _qualifiedInterfaceDecls => _context.QualifiedInterfaceDecls;

        private Dictionary<ClassDeclStmt, string> _classQualifiedPaths => _context.ClassQualifiedPaths;

        private Dictionary<InterfaceDeclStmt, string> _interfaceQualifiedPaths => _context.InterfaceQualifiedPaths;

        private Dictionary<InterfaceDeclStmt, Dictionary<string, InterfaceMethodDecl>> _interfaceContractCache => _context.InterfaceContractCache;

        private Dictionary<InterfaceDeclStmt, Dictionary<string, InterfacePropertyDecl>> _interfacePropertyContractCache => _context.InterfacePropertyContractCache;

        private Dictionary<InterfaceDeclStmt, Dictionary<string, InterfaceIndexerDecl>> _interfaceIndexerContractCache => _context.InterfaceIndexerContractCache;

        private ClassInfo? _currentClass
        {
            get => _emission.CurrentClass;
            set => _emission.CurrentClass = value;
        }

        private ClassDeclStmt? _currentClassDecl
        {
            get => _emission.CurrentClassDecl;
            set => _emission.CurrentClassDecl = value;
        }

        private bool _currentMethodIsStatic
        {
            get => _emission.CurrentMethodIsStatic;
            set => _emission.CurrentMethodIsStatic = value;
        }

        private ReceiverContextKind _receiverContext
        {
            get => _emission.ReceiverContext;
            set => _emission.ReceiverContext = value;
        }

        private Stack<HashSet<string>> _localVarsStack => _emission.LocalVarsStack;

        private Stack<int> _functionStartStack => _emission.FunctionStartStack;

        private Dictionary<int, int> _functionRanges => _emission.FunctionRanges;

        private Dictionary<int, IReadOnlyList<string>> _functionParams => _emission.FunctionParams;

        private Stack<int> _scopePairingStack => _emission.ScopePairingStack;

        private Dictionary<int, int> _scopePairings => _emission.ScopePairings;

        private List<(int Start, int End)> _nameBasedRanges => _emission.NameBasedRanges;

        private int _scopeDepth
        {
            get => _emission.ScopeDepth;
            set => _emission.ScopeDepth = value;
        }

        private int _asyncFunctionDepth
        {
            get => _emission.AsyncFunctionDepth;
            set => _emission.AsyncFunctionDepth = value;
        }

        private string? _currentPropertyBackingSlotName
        {
            get => _emission.CurrentPropertyBackingSlotName;
            set => _emission.CurrentPropertyBackingSlotName = value;
        }

        private string? _currentPropertyBackingReceiverName
        {
            get => _emission.CurrentPropertyBackingReceiverName;
            set => _emission.CurrentPropertyBackingReceiverName = value;
        }

        private int _anonCounter
        {
            get => _emission.AnonymousCounter;
            set => _emission.AnonymousCounter = value;
        }

        private static readonly HashSet<string> EmptyLocals = new(StringComparer.Ordinal);

        /// <summary>
        /// Gets the CurrentLocals
        /// </summary>
        private HashSet<string> CurrentLocals => _localVarsStack.Count > 0 ? _localVarsStack.Peek() : EmptyLocals;
    }
}
