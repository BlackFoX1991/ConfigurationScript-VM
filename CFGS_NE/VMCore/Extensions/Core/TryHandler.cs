namespace CFGS_VM.VMCore.Extensions.Core
{
    public class TryHandler
    {
        public int CallDepth;

        public int CatchAddr;

        public int FinallyAddr;

        public object? Exception;

        public int ScopeDepthAtTry;

        /// <summary>
        /// Operand-stack depth captured when the guarded region was entered. Used to discard any
        /// partially-evaluated operands when an exception unwinds into the catch/finally region.
        /// </summary>
        public int StackDepthAtTry;

        public bool InFinally;

        public bool HasPendingReturn;

        public object? PendingReturnValue;

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
            PendingReturnValue = null;

            HasPendingLeave = false;
            PendingLeaveTargetIp = -1;
            PendingLeaveScopes = 0;
        }
    }
}

