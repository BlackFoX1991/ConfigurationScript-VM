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
        /// Gets the MinArgs
        /// </summary>
        public int MinArgs { get; }

        /// <summary>
        /// Gets the RestParameter
        /// </summary>
        public string? RestParameter { get; }

        public bool isAsync { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FunctionInfo"/> class.
        /// </summary>
        /// <param name="parameters">The parameters<see cref="List{string}"/></param>
        /// <param name="address">The address<see cref="int"/></param>
        /// <param name="minArgs">The minArgs<see cref="int"/></param>
        /// <param name="restParameter">The restParameter<see cref="string?"/></param>
        /// <param name="isAsync">The isAsync<see cref="bool"/></param>
        public FunctionInfo(List<string> parameters, int address, int minArgs = -1, string? restParameter = null, bool isAsync = false)
        {
            Parameters = parameters;
            Address = address;
            MinArgs = (minArgs < 0) ? parameters.Count : minArgs;
            RestParameter = restParameter;
            this.isAsync = isAsync;

        }
    }

}
