namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="FunctionInfo" />
    /// </summary>
    public class FunctionInfo
    {
        /// <summary>
        /// Gets the Parameters
        /// </summary>
        public List<string> Parameters { get; }

        /// <summary>
        /// Gets the Address
        /// </summary>
        public int Address { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionInfo"/> class.
        /// </summary>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="address">The address<see cref="int"/></param>
        public FunctionInfo(List<string> parameters, int address)
        {
            Parameters = parameters;
            Address = address;
        }
    }

}
