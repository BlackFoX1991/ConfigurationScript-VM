using CFGS_VM.VMCore;

namespace CFGS_VM.VMCore.Extensions.Core
{
    public class TryHandler
    {
        public int CallDepth;

        public int CatchAddr;

        public int FinallyAddr;

        public Value? Exception;

        public int ScopeDepthAtTry;

        /// <summary>
        /// Operand-stack depth captured when the guarded region was entered. Used to discard any
        /// partially-evaluated operands when an exception unwinds into the catch/finally region.
        /// </summary>
        public int StackDepthAtTry;

        public bool InFinally;

        public bool HasPendingReturn;

        /// <summary>
        /// The value of a <c>return</c> that is parked while a <c>finally</c> block runs. Typed as
        /// <see cref="Value"/> rather than <see cref="object"/> so parking it does not box: RET carries a
        /// tagged value, and going through object here would have re-introduced the allocation the tagged
        /// representation exists to avoid.
        /// </summary>
        public Value PendingReturnValue;

        public bool HasPendingLeave;

        public int PendingLeaveTargetIp;

        public int PendingLeaveScopes;

        /// <summary>
        /// Initializes a new instance of the <see cref="TryHandler"/> class.
        /// </summary>
        /// <param name="catchAddr">The catchAddr<see cref="int"/></param>
        /// <param name="finallyAddr">The finallyAddr<see cref="int"/></param>
        /// <param name="scopeDepthAtTry">The scopeDepthAtTry<see cref="int"/></param>
        /// <param name="callDepth">The callDepth<see cref="int"/></param>
        public TryHandler(int catchAddr, int finallyAddr, int scopeDepthAtTry, int callDepth, int stackDepthAtTry = 0)
        {
            CatchAddr = catchAddr;
            FinallyAddr = finallyAddr;
            ScopeDepthAtTry = scopeDepthAtTry;
            CallDepth = callDepth;
            StackDepthAtTry = stackDepthAtTry;
            Exception = null;

            InFinally = false;
            HasPendingReturn = false;
            PendingReturnValue = Value.Null;

            HasPendingLeave = false;
            PendingLeaveTargetIp = -1;
            PendingLeaveScopes = 0;
        }
    }
}
