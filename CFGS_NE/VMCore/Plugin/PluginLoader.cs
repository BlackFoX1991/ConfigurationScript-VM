using System.Collections.Concurrent;
using System.Reflection;

namespace CFGS_VM.VMCore.Plugin
{
    /// <summary>
    /// Defines the <see cref="PluginLoader" />
    /// </summary>
    public static class PluginLoader
    {
        /// <summary>
        /// Defines the _loadedAssemblies
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> _loadedAssemblies = new();

        /// <summary>
        /// Defines the _activatedPluginTypes
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> _activatedPluginTypes = new();

        /// <summary>
        /// Defines the _attrRegistered
        /// </summary>
        private static readonly ConcurrentDictionary<string, byte> _attrRegistered = new();

        /// <summary>
        /// Defines the Verbose
        /// </summary>
        private static readonly bool Verbose =
            string.Equals(Environment.GetEnvironmentVariable("CFGS_PLUGIN_VERBOSE"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("CFGS_PLUGIN_VERBOSE"), "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The LoadDirectory
        /// </summary>
        /// <param name="directoryPath">The directoryPath<see cref="string"/></param>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadDirectory(string directoryPath, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                LogWarn($"LoadDirectory ignored: '{directoryPath}' not found.");
                return;
            }

            LogInfo($"Scanning plugin directory: {directoryPath}");

            foreach (string dll in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string full = Path.GetFullPath(dll);
                    LogInfo($"Loading dll from directory: {full}");

                    var plc = new PluginLoadContext(full);
                    Assembly asm = plc.LoadFromAssemblyPath(full);

                    LoadFromAssembly(asm, builtins, intrinsics);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to load '{dll}'", ex);
                }
            }
        }

        /// <summary>
        /// The LoadDll
        /// </summary>
        /// <param name="dllPath">The dllPath<see cref="string"/></param>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadDll(string dllPath, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            if (string.IsNullOrWhiteSpace(dllPath))
                return;

            string full = Path.GetFullPath(dllPath);
            if (!File.Exists(full) || !full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                LogInfo($"Loading dll: {full}");

                var plc = new PluginLoadContext(full);
                Assembly asm = plc.LoadFromAssemblyPath(full);

                LoadFromAssembly(asm, builtins, intrinsics);
            }
            catch (Exception ex)
            {
                LogError($"Failed to load '{full}'", ex);
            }
        }

        /// <summary>
        /// The LoadFromAssembly
        /// </summary>
        /// <param name="asm">The asm<see cref="Assembly"/></param>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadFromAssembly(Assembly asm, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            if (asm == null) return;

            string key = $"{AsmName(asm)}::{AsmLocation(asm)}";

            if (!_loadedAssemblies.TryAdd(key, 0))
            {
                if (Verbose)
                    LogInfo($"Assembly already processed: {AsmName(asm)} @ {AsmLocation(asm)}");
                return;
            }

            if (Verbose)
                LogInfo($"Processing assembly: {AsmName(asm)} @ {AsmLocation(asm)}");

            Type[] types = Array.Empty<Type>();
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                types = rtle.Types.Where(t => t != null).Cast<Type>().ToArray();

                LogWarn($"Partial type load for '{AsmName(asm)}' – continuing with {types.Length} types.");
                LogLoaderExceptions(asm, rtle);
            }
            catch (Exception ex)
            {
                LogError($"GetTypes() failed for '{AsmName(asm)}'", ex);
                return;
            }

            int pluginCandidates = 0;
            int pluginActivated = 0;

            foreach (Type t in types)
            {
                if (t is null) continue;
                if (t.IsAbstract) continue;
                if (!typeof(IVmPlugin).IsAssignableFrom(t)) continue;

                pluginCandidates++;

                string tkey = t.FullName ?? t.Name;
                if (!_activatedPluginTypes.TryAdd(tkey, 0))
                {
                    if (Verbose)
                        LogInfo($"Plugin type already activated: {t.FullName}");
                    continue;
                }

                try
                {
                    if (Verbose)
                        LogInfo($"Activating plugin: {t.FullName}");

                    IVmPlugin plugin = (IVmPlugin)Activator.CreateInstance(t)!;
                    plugin.Register(builtins, intrinsics);

                    pluginActivated++;
                }
                catch (TargetInvocationException tie)
                {
                    Exception? inner = tie.InnerException;

                    if (inner != null)
                        LogError($"Failed to activate {t.FullName} (ctor/initializer)", inner);
                    else
                        LogError($"Failed to activate {t.FullName}", tie);
                }
                catch (FileNotFoundException fnf)
                {
                    LogError($"Failed to activate {t.FullName} (missing dependency?)", fnf);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to activate {t.FullName}", ex);
                }
            }

            if (Verbose && pluginCandidates > 0)
                LogInfo($"Plugin activation summary for '{AsmName(asm)}': candidates={pluginCandidates}, activated={pluginActivated}");

            int builtinCount = 0;
            int intrinsicCount = 0;

            foreach (Type t in types)
            {
                if (t is null) continue;

                MethodInfo[] methods;
                try
                {
                    methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                catch (Exception ex)
                {
                    if (Verbose)
                        LogError($"GetMethods failed for type {t.FullName}", ex);
                    continue;
                }

                foreach (MethodInfo m in methods)
                {
                    BuiltinAttribute[] battrs;
                    try
                    {
                        battrs = m.GetCustomAttributes(typeof(BuiltinAttribute), inherit: false)
                                  .Cast<BuiltinAttribute>()
                                  .ToArray();
                    }
                    catch (Exception ex)
                    {
                        if (Verbose)
                            LogError($"GetCustomAttributes(Builtin) failed for {t.FullName}.{m.Name}", ex);
                        continue;
                    }

                    foreach (BuiltinAttribute b in battrs)
                    {
                        string bkey = $"builtin::{b.Name}";
                        if (!_attrRegistered.TryAdd(bkey, 0))
                            continue;

                        try
                        {
                            BuiltinInvoker inv = (args, instr) => m.Invoke(null, new object?[] { args, instr })!;
                            builtins.Register(new BuiltinDescriptor(b.Name, b.ArityMin, b.ArityMax, inv));
                            builtinCount++;

                            if (Verbose)
                                LogInfo($"Registered builtin '{b.Name}' from {t.FullName}.{m.Name}");
                        }
                        catch (TargetInvocationException tie)
                        {
                            Exception inner = tie.InnerException ?? tie;
                            LogError($"Builtin '{b.Name}' from {t.FullName}.{m.Name} failed", inner);
                        }
                        catch (Exception ex)
                        {
                            LogError($"Builtin '{b.Name}' from {t.FullName}.{m.Name} failed", ex);
                        }
                    }

                    IntrinsicAttribute[] iattrs;
                    try
                    {
                        iattrs = m.GetCustomAttributes(typeof(IntrinsicAttribute), inherit: false)
                                  .Cast<IntrinsicAttribute>()
                                  .ToArray();
                    }
                    catch (Exception ex)
                    {
                        if (Verbose)
                            LogError($"GetCustomAttributes(Intrinsic) failed for {t.FullName}.{m.Name}", ex);
                        continue;
                    }

                    foreach (IntrinsicAttribute a in iattrs)
                    {
                        Type? recv = a.ReceiverType;
                        string rname = recv?.FullName ?? recv?.Name ?? "<null>";
                        string ikey = $"intrinsic::{rname}::{a.Name}";
                        if (!_attrRegistered.TryAdd(ikey, 0))
                            continue;

                        try
                        {
                            IntrinsicInvoker inv = (recvObj, args, instr) => m.Invoke(null, new object?[] { recvObj, args, instr })!;
                            intrinsics.Register(a.ReceiverType, new IntrinsicDescriptor(a.Name, a.ArityMin, a.ArityMax, inv));
                            intrinsicCount++;

                            if (Verbose)
                                LogInfo($"Registered intrinsic '{a.Name}' (recv={rname}) from {t.FullName}.{m.Name}");
                        }
                        catch (TargetInvocationException tie)
                        {
                            Exception inner = tie.InnerException ?? tie;
                            LogError($"Intrinsic '{a.Name}' (recv={rname}) from {t.FullName}.{m.Name} failed", inner);
                        }
                        catch (Exception ex)
                        {
                            LogError($"Intrinsic '{a.Name}' (recv={rname}) from {t.FullName}.{m.Name} failed", ex);
                        }
                    }
                }
            }

            if (Verbose && (builtinCount > 0 || intrinsicCount > 0))
                LogInfo($"Attribute registration summary for '{AsmName(asm)}': builtins={builtinCount}, intrinsics={intrinsicCount}");
        }

        /// <summary>
        /// The LoadThisAssembly
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadThisAssembly(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
            => LoadFromAssembly(typeof(PluginLoader).Assembly, builtins, intrinsics);

        /// <summary>
        /// The LoadFromAssemblyContaining
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadFromAssemblyContaining<T>(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
            => LoadFromAssembly(typeof(T).Assembly, builtins, intrinsics);

        /// <summary>
        /// The LoadCurrentDomain
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        /// <param name="filter">The filter<see cref="Func{Assembly, bool}?"/></param>
        public static void LoadCurrentDomain(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, Func<Assembly, bool>? filter = null)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (filter == null || filter(asm))
                        LoadFromAssembly(asm, builtins, intrinsics);
                }
                catch (Exception ex)
                {
                    LogError($"Skipped '{AsmName(asm)}'", ex);
                }
            }
        }

        /// <summary>
        /// The LogLoaderExceptions
        /// </summary>
        /// <param name="asm">The asm<see cref="Assembly"/></param>
        /// <param name="rtle">The rtle<see cref="ReflectionTypeLoadException"/></param>
        private static void LogLoaderExceptions(Assembly asm, ReflectionTypeLoadException rtle)
        {
            if (rtle.LoaderExceptions == null || rtle.LoaderExceptions.Length == 0)
                return;

            LogWarn($"LoaderExceptions for '{AsmName(asm)}':");

            foreach (Exception? le in rtle.LoaderExceptions)
            {
                if (le == null) continue;

                LogWarn($"  -> {le.GetType().Name}: {le.Message}");

                if (le is FileNotFoundException fnf)
                {
                    if (!string.IsNullOrWhiteSpace(fnf.FileName))
                        LogWarn($"     Missing: {fnf.FileName}");
                }

                if (Verbose && le is FileLoadException fle)
                {
                    if (!string.IsNullOrWhiteSpace(fle.FileName))
                        LogWarn($"     FileLoad: {fle.FileName}");
                }
            }
        }

        /// <summary>
        /// The LogInfo
        /// </summary>
        /// <param name="msg">The msg<see cref="string"/></param>
        private static void LogInfo(string msg)
            => Console.WriteLine($"[PluginLoader] {msg}");

        /// <summary>
        /// The LogWarn
        /// </summary>
        /// <param name="msg">The msg<see cref="string"/></param>
        private static void LogWarn(string msg)
            => Console.Error.WriteLine($"[PluginLoader] {msg}");

        /// <summary>
        /// The LogError
        /// </summary>
        /// <param name="msg">The msg<see cref="string"/></param>
        /// <param name="ex">The ex<see cref="Exception"/></param>
        private static void LogError(string msg, Exception ex)
        {
            Console.Error.WriteLine($"[PluginLoader] {msg}: {ex.GetType().Name}: {ex.Message}");

            if (Verbose)
            {
                Console.Error.WriteLine($"[PluginLoader]   Stack: {ex.StackTrace}");

                if (ex.InnerException != null)
                    Console.Error.WriteLine($"[PluginLoader]   Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
            }
        }

        /// <summary>
        /// The AsmName
        /// </summary>
        /// <param name="asm">The asm<see cref="Assembly"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string AsmName(Assembly asm)
        {
            try
            {
                return asm.FullName
                    ?? asm.GetName().Name
                    ?? "(unknown-assembly)";
            }
            catch
            {
                return "(unknown-assembly)";
            }
        }

        /// <summary>
        /// The AsmLocation
        /// </summary>
        /// <param name="asm">The asm<see cref="Assembly"/></param>
        /// <returns>The <see cref="string"/></returns>
        private static string AsmLocation(Assembly asm)
        {
            try
            {
                return string.IsNullOrWhiteSpace(asm.Location) ? "(dynamic)" : asm.Location;
            }
            catch
            {
                return "(unknown-location)";
            }
        }
    }
}
