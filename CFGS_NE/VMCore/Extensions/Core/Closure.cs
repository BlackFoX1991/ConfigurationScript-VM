using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore.Extensions.Core;

/// <summary>
/// Defines the <see cref="Closure" />
/// </summary>
public class Closure
{
    /// <summary>
    /// Gets the Address
    /// </summary>
    public int Address { get; }

    /// <summary>
    /// Gets the Parameters
    /// </summary>
    public List<string> Parameters { get; }

    /// <summary>
    /// Gets the CapturedEnv
    /// </summary>
    public Env CapturedEnv { get; }

    /// <summary>
    /// Gets the MinArgs
    /// </summary>
    public int MinArgs { get; }

    /// <summary>
    /// Gets the Name
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the RestParameter
    /// </summary>
    public string? RestParameter { get; }

    /// <summary>
    /// Gets a value indicating whether IsAsync
    /// </summary>
    public bool IsAsync { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Closure"/> class.
    /// </summary>
    /// <param name="address">The address<see cref="int"/></param>
    /// <param name="parameters">The parameters<see cref="List{string}"/></param>
    /// <param name="env">The env<see cref="Env"/></param>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="restParameter">The restParameter<see cref="string?"/></param>
    /// <param name="isAsync">The isAsync<see cref="bool"/></param>
    public Closure(int address, List<string> parameters, int minArgs, Env env, string name, string? restParameter = null, bool isAsync = false)
    {
        Address = address;
        Parameters = parameters;
        MinArgs = minArgs;
        CapturedEnv = env;
        Name = name ?? "<anon>";
        RestParameter = restParameter;
        IsAsync = isAsync;
    }

    /// <summary>
    /// The ToString
    /// </summary>
    /// <returns>The <see cref="string"/></returns>
    public override string ToString()
    {
        string paramList = string.Join(", ", Parameters);
        string captured = "";
        if (CapturedEnv != null)
        {
            List<KeyValuePair<string, object>> snapshot;
            lock (CapturedEnv.SyncRoot)
                snapshot = CapturedEnv.Vars.ToList();

            if (snapshot.Count > 0)
            {
                IEnumerable<string> pairs = snapshot
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kvp =>
                {
                    if (kvp.Value == null) return $"{kvp.Key}=null";
                    if (kvp.Value is Closure) return $"{kvp.Key}=<closure>";
                    if (kvp.Value is FunctionInfo fi) return $"{kvp.Key}=<fn:{fi.Address.ToString()}>";
                    return $"{kvp.Key}={kvp.Value.GetType().Name}";
                });
                captured = $" captured: {{{string.Join(", ", pairs)}}}";
            }
        }
        return $"<closure {Name} at {Address} ({paramList}){captured}>";
    }
}
