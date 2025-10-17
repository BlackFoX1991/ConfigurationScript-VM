namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="TryHandler" />
    /// </summary>
    public class TryHandler
    {
        /// <summary>
        /// Defines the CallDepth
        /// </summary>
        public int CallDepth;

        /// <summary>
        /// Defines the CatchAddr
        /// </summary>
        public int CatchAddr;

        /// <summary>
        /// Defines the FinallyAddr
        /// </summary>
        public int FinallyAddr;

        /// <summary>
        /// Defines the Exception
        /// </summary>
        public object? Exception;

        /// <summary>
        /// Defines the ScopeDepthAtTry
        /// </summary>
        public int ScopeDepthAtTry;

        /// <summary>
        /// Defines the InFinally
        /// </summary>
        public bool InFinally;

        /// <summary>
        /// Defines the HasPendingReturn
        /// </summary>
        public bool HasPendingReturn;

        /// <summary>
        /// Defines the PendingReturnValue
        /// </summary>
        public object? PendingReturnValue;

        /// <summary>
        /// Defines the HasPendingLeave
        /// </summary>
        public bool HasPendingLeave;

        /// <summary>
        /// Defines the PendingLeaveTargetIp
        /// </summary>
        public int PendingLeaveTargetIp;

        /// <summary>
        /// Defines the PendingLeaveScopes
        /// </summary>
        public int PendingLeaveScopes;

        /// <summary>
        /// Initializes a new instance of the <see cref="TryHandler"/> class.
        /// </summary>
        /// <param name="catchAddr">The catchAddr<see cref="int"/></param>
        /// <param name="finallyAddr">The finallyAddr<see cref="int"/></param>
        /// <param name="scopeDepthAtTry">The scopeDepthAtTry<see cref="int"/></param>
        /// <param name="callDepth">The callDepth<see cref="int"/></param>
        public TryHandler(int catchAddr, int finallyAddr, int scopeDepthAtTry, int callDepth)
        {
            CatchAddr = catchAddr;
            FinallyAddr = finallyAddr;
            ScopeDepthAtTry = scopeDepthAtTry;
            CallDepth = callDepth;
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

