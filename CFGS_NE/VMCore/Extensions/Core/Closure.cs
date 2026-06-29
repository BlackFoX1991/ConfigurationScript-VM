using CFGS_VM.VMCore;
using CFGS_VM.VMCore.Extensions.Core;
using CFGS_VM.VMCore.Extensions.Instance;

namespace CFGS_VM.VMCore.Extensions.Core;

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
    /// Gets the receiver captured lexically at definition time (C#-style), or null when the
    /// closure was not defined inside a method. Used so this/type/super/outer resolve to the
    /// enclosing receiver when the closure is later invoked without its own receiver.
    /// </summary>
    public object? CapturedThis { get; }

    /// <summary>
    /// Gets the access type (static type) captured alongside <see cref="CapturedThis"/>.
    /// </summary>
    public StaticInstance? CapturedAccessType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Closure"/> class.
    /// </summary>
    /// <param name="address">The address<see cref="int"/></param>
    /// <param name="parameters">The parameters<see cref="List{string}"/></param>
    /// <param name="env">The env<see cref="Env"/></param>
    /// <param name="name">The name<see cref="string"/></param>
    /// <param name="restParameter">The restParameter<see cref="string?"/></param>
    /// <param name="isAsync">The isAsync<see cref="bool"/></param>
    /// <param name="capturedThis">The lexically captured receiver<see cref="object?"/></param>
    /// <param name="capturedAccessType">The lexically captured access type<see cref="StaticInstance?"/></param>
    public Closure(int address, List<string> parameters, int minArgs, Env env, string name, string? restParameter = null, bool isAsync = false, object? capturedThis = null, StaticInstance? capturedAccessType = null)
    {
        Address = address;
        Parameters = parameters;
        MinArgs = minArgs;
        CapturedEnv = env;
        Name = name ?? "<anon>";
        RestParameter = restParameter;
        IsAsync = isAsync;
        CapturedThis = capturedThis;
        CapturedAccessType = capturedAccessType;
    }

    public override string ToString()
    {
        string paramList = string.Join(", ", Parameters);
        string captured = "";
        if (CapturedEnv != null)
        {
            List<KeyValuePair<string, Value>> snapshot;
            lock (CapturedEnv.SyncRoot)
                snapshot = CapturedEnv.Vars.ToList();

            if (snapshot.Count > 0)
            {
                IEnumerable<string> pairs = snapshot
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kvp =>
                {
                    Value value = kvp.Value;
                    if (value.IsNull) return $"{kvp.Key}=null";
                    if (value.Kind != ValueKind.Ref)
                        return $"{kvp.Key}={value.Kind}";

                    object raw = value.Reference!;
                    if (raw is Closure) return $"{kvp.Key}=<closure>";
                    if (raw is FunctionInfo fi) return $"{kvp.Key}=<fn:{fi.Address.ToString()}>";
                    return $"{kvp.Key}={raw.GetType().Name}";
                });
                captured = $" captured: {{{string.Join(", ", pairs)}}}";
            }
        }
        return $"<closure {Name} at {Address} ({paramList}){captured}>";
    }
}
