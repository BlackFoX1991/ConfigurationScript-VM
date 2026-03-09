using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace CFGS_VM.VMCore.Plugin
{
    /// <summary>
    /// Defines the <see cref="PluginLoader" />
    /// </summary>
    public static class PluginLoader
    {
        private sealed class LoaderState
        {
            public readonly ConcurrentDictionary<string, byte> LoadedAssemblies = new();
            public readonly ConcurrentDictionary<string, byte> ActivatedPluginTypes = new();
            public readonly ConcurrentDictionary<string, Assembly> LoadedDllByPath = new(StringComparer.OrdinalIgnoreCase);
            public readonly object SyncRoot = new();
        }

        private static readonly ConditionalWeakTable<IBuiltinRegistry, LoaderState> _states = new();

        /// <summary>
        /// Defines the Verbose
        /// </summary>
        private static readonly bool Verbose =
            string.Equals(Environment.GetEnvironmentVariable("CFGS_PLUGIN_VERBOSE"), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable("CFGS_PLUGIN_VERBOSE"), "true", StringComparison.OrdinalIgnoreCase);

        private static LoaderState GetState(IBuiltinRegistry builtins)
        {
            ArgumentNullException.ThrowIfNull(builtins);
            return _states.GetValue(builtins, _ => new LoaderState());
        }

        private sealed class StagedBuiltinRegistry : IBuiltinRegistry
        {
            private readonly IBuiltinRegistry _baseRegistry;
            private readonly Dictionary<string, BuiltinDescriptor> _staged = new(StringComparer.Ordinal);

            public StagedBuiltinRegistry(IBuiltinRegistry baseRegistry)
            {
                _baseRegistry = baseRegistry;
            }

            public void Register(BuiltinDescriptor d)
            {
                if (_staged.ContainsKey(d.Name))
                    throw new InvalidOperationException($"Duplicate builtin '{d.Name}'.");
                _staged[d.Name] = d;
            }

            public bool TryGet(string name, out BuiltinDescriptor d)
            {
                if (_staged.TryGetValue(name, out d!))
                    return true;
                return _baseRegistry.TryGet(name, out d!);
            }

            public bool Contains(string name)
                => _staged.ContainsKey(name) || _baseRegistry.Contains(name);

            public bool Remove(string name)
                => _staged.Remove(name);

            public IReadOnlyList<BuiltinDescriptor> Snapshot()
                => _staged.Values.ToList();
        }

        private sealed class StagedIntrinsicRegistry : IIntrinsicRegistry
        {
            private readonly IIntrinsicRegistry _baseRegistry;
            private readonly Dictionary<Type, Dictionary<string, IntrinsicDescriptor>> _staged = new();

            public StagedIntrinsicRegistry(IIntrinsicRegistry baseRegistry)
            {
                _baseRegistry = baseRegistry;
            }

            public void Register(Type receiverType, IntrinsicDescriptor d)
            {
                if (!_staged.TryGetValue(receiverType, out Dictionary<string, IntrinsicDescriptor>? bucket))
                {
                    bucket = new Dictionary<string, IntrinsicDescriptor>(StringComparer.Ordinal);
                    _staged[receiverType] = bucket;
                }

                if (bucket.ContainsKey(d.Name))
                    throw new InvalidOperationException($"Duplicate intrinsic '{d.Name}' for {receiverType.Name}.");

                bucket[d.Name] = d;
            }

            public void Register(Type receiverType, IEnumerable<IntrinsicDescriptor> ds)
            {
                foreach (IntrinsicDescriptor d in ds)
                    Register(receiverType, d);
            }

            public bool TryGet(Type receiverType, string name, out IntrinsicDescriptor d)
            {
                if (_staged.TryGetValue(receiverType, out Dictionary<string, IntrinsicDescriptor>? bucket) &&
                    bucket.TryGetValue(name, out d!))
                    return true;

                Type? t = receiverType.BaseType;
                while (t != null)
                {
                    if (_staged.TryGetValue(t, out bucket) && bucket.TryGetValue(name, out d!))
                        return true;
                    t = t.BaseType;
                }

                foreach (Type iface in receiverType.GetInterfaces())
                {
                    if (_staged.TryGetValue(iface, out bucket) && bucket.TryGetValue(name, out d!))
                        return true;
                }

                return _baseRegistry.TryGet(receiverType, name, out d!);
            }

            public bool ContainsExact(Type receiverType, string name)
                => (_staged.TryGetValue(receiverType, out Dictionary<string, IntrinsicDescriptor>? bucket) &&
                    bucket.ContainsKey(name))
                   || _baseRegistry.ContainsExact(receiverType, name);

            public bool RemoveExact(Type receiverType, string name)
            {
                if (!_staged.TryGetValue(receiverType, out Dictionary<string, IntrinsicDescriptor>? bucket))
                    return false;

                bool removed = bucket.Remove(name);
                if (bucket.Count == 0)
                    _staged.Remove(receiverType);

                return removed;
            }

            public IReadOnlyList<(Type ReceiverType, IntrinsicDescriptor Descriptor)> Snapshot()
            {
                List<(Type ReceiverType, IntrinsicDescriptor Descriptor)> result = new();
                foreach (KeyValuePair<Type, Dictionary<string, IntrinsicDescriptor>> entry in _staged)
                {
                    foreach (IntrinsicDescriptor desc in entry.Value.Values)
                        result.Add((entry.Key, desc));
                }

                return result;
            }
        }

        private static void RollbackRegistrations(
            IBuiltinRegistry builtins,
            IIntrinsicRegistry intrinsics,
            IReadOnlyList<string> appliedBuiltins,
            IReadOnlyList<(Type ReceiverType, string Name)> appliedIntrinsics)
        {
            for (int i = appliedBuiltins.Count - 1; i >= 0; i--)
                _ = builtins.Remove(appliedBuiltins[i]);

            for (int i = appliedIntrinsics.Count - 1; i >= 0; i--)
            {
                (Type receiverType, string name) = appliedIntrinsics[i];
                _ = intrinsics.RemoveExact(receiverType, name);
            }
        }

        private static void CommitStagedRegistrations(
            StagedBuiltinRegistry stagedBuiltins,
            StagedIntrinsicRegistry stagedIntrinsics,
            IBuiltinRegistry builtins,
            IIntrinsicRegistry intrinsics,
            LoaderState state)
        {
            IReadOnlyList<BuiltinDescriptor> builtinsToAdd = stagedBuiltins.Snapshot();
            IReadOnlyList<(Type ReceiverType, IntrinsicDescriptor Descriptor)> intrinsicsToAdd = stagedIntrinsics.Snapshot();

            lock (state.SyncRoot)
            {
                foreach (BuiltinDescriptor b in builtinsToAdd)
                {
                    if (builtins.Contains(b.Name))
                        throw new InvalidOperationException($"Duplicate builtin '{b.Name}'.");
                }

                foreach ((Type ReceiverType, IntrinsicDescriptor Descriptor) entry in intrinsicsToAdd)
                {
                    if (intrinsics.ContainsExact(entry.ReceiverType, entry.Descriptor.Name))
                        throw new InvalidOperationException($"Duplicate intrinsic '{entry.Descriptor.Name}' for {entry.ReceiverType.Name}.");
                }

                List<string> appliedBuiltins = new();
                List<(Type ReceiverType, string Name)> appliedIntrinsics = new();

                try
                {
                    foreach (BuiltinDescriptor b in builtinsToAdd)
                    {
                        builtins.Register(b);
                        appliedBuiltins.Add(b.Name);
                    }

                    foreach ((Type ReceiverType, IntrinsicDescriptor Descriptor) entry in intrinsicsToAdd)
                    {
                        intrinsics.Register(entry.ReceiverType, entry.Descriptor);
                        appliedIntrinsics.Add((entry.ReceiverType, entry.Descriptor.Name));
                    }
                }
                catch
                {
                    RollbackRegistrations(builtins, intrinsics, appliedBuiltins, appliedIntrinsics);
                    throw;
                }
            }
        }

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
            List<Exception> failures = new();

            foreach (string dll in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    LoadDll(dll, builtins, intrinsics);
                }
                catch (Exception ex)
                {
                    // LoadDll already logs concrete failure details; preserve all failures for caller diagnostics.
                    failures.Add(new InvalidOperationException($"Failed to load plugin dll '{dll}'.", ex));
                }
            }

            if (failures.Count > 0)
                throw new AggregateException($"One or more plugins failed to load from '{directoryPath}'.", failures);
        }

        /// <summary>
        /// The LoadDll
        /// </summary>
        /// <param name="dllPath">The dllPath<see cref="string"/></param>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadDll(string dllPath, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            ArgumentNullException.ThrowIfNull(builtins);
            ArgumentNullException.ThrowIfNull(intrinsics);

            if (string.IsNullOrWhiteSpace(dllPath))
                throw new ArgumentException("Plugin dll path is empty.", nameof(dllPath));

            string full = dllPath;

            try
            {
                full = Path.GetFullPath(dllPath);
                if (!full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Plugin import requires a .dll path, got '{dllPath}'.", nameof(dllPath));

                if (!File.Exists(full))
                    throw new FileNotFoundException("Plugin dll not found.", full);

                LoaderState state = GetState(builtins);
                if (!state.LoadedDllByPath.TryGetValue(full, out Assembly? asm))
                {
                    lock (state.SyncRoot)
                    {
                        if (!state.LoadedDllByPath.TryGetValue(full, out asm))
                        {
                            LogInfo($"Loading dll: {full}");
                            var plc = new PluginLoadContext(full);
                            asm = plc.LoadFromAssemblyPath(full);
                            state.LoadedDllByPath[full] = asm;
                        }
                    }
                }
                else if (Verbose)
                {
                    LogInfo($"DLL already loaded in this VM: {full}");
                }

                LoadFromAssembly(asm, builtins, intrinsics, state);
            }
            catch (Exception ex)
            {
                LogError($"Failed to load '{full}'", ex);
                throw;
            }
        }

        /// <summary>
        /// The LoadFromAssembly
        /// </summary>
        /// <param name="asm">The asm<see cref="Assembly"/></param>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadFromAssembly(Assembly asm, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
            => LoadFromAssembly(asm, builtins, intrinsics, GetState(builtins));

        private static void LoadFromAssembly(Assembly asm, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, LoaderState state)
        {
            if (asm == null) return;

            string key = $"{AsmName(asm)}::{AsmLocation(asm)}";

            if (!state.LoadedAssemblies.TryAdd(key, 0))
            {
                if (Verbose)
                    LogInfo($"Assembly already processed: {AsmName(asm)} @ {AsmLocation(asm)}");
                return;
            }

            bool completed = false;
            try
            {
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
                List<Exception> pluginActivationFailures = new();

                foreach (Type t in types)
                {
                    if (t is null) continue;
                    if (t.IsAbstract) continue;
                    if (!typeof(IVmPlugin).IsAssignableFrom(t)) continue;

                    pluginCandidates++;

                    string tkey = $"{AsmName(t.Assembly)}::{AsmLocation(t.Assembly)}::{t.FullName ?? t.Name}";
                    if (!state.ActivatedPluginTypes.TryAdd(tkey, 0))
                    {
                        if (Verbose)
                            LogInfo($"Plugin type already activated: {tkey}");
                        continue;
                    }

                    bool pluginTypeActivated = false;
                    try
                    {
                        if (Verbose)
                            LogInfo($"Activating plugin: {t.FullName}");

                        IVmPlugin plugin = (IVmPlugin)Activator.CreateInstance(t)!;
                        StagedBuiltinRegistry stagedBuiltins = new(builtins);
                        StagedIntrinsicRegistry stagedIntrinsics = new(intrinsics);
                        plugin.Register(stagedBuiltins, stagedIntrinsics);
                        CommitStagedRegistrations(stagedBuiltins, stagedIntrinsics, builtins, intrinsics, state);

                        pluginActivated++;
                        pluginTypeActivated = true;
                    }
                    catch (TargetInvocationException tie)
                    {
                        Exception inner = tie.InnerException ?? tie;
                        LogError($"Failed to activate {t.FullName} (ctor/initializer)", inner);
                        pluginActivationFailures.Add(new InvalidOperationException($"Plugin activation failed: {t.FullName}", inner));
                    }
                    catch (FileNotFoundException fnf)
                    {
                        LogError($"Failed to activate {t.FullName} (missing dependency?)", fnf);
                        pluginActivationFailures.Add(new InvalidOperationException($"Plugin activation failed: {t.FullName}", fnf));
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to activate {t.FullName}", ex);
                        pluginActivationFailures.Add(new InvalidOperationException($"Plugin activation failed: {t.FullName}", ex));
                    }
                    finally
                    {
                        if (!pluginTypeActivated)
                            _ = state.ActivatedPluginTypes.TryRemove(tkey, out _);
                    }
                }

                if (Verbose && pluginCandidates > 0)
                    LogInfo($"Plugin activation summary for '{AsmName(asm)}': candidates={pluginCandidates}, activated={pluginActivated}");

                if (pluginActivationFailures.Count > 0)
                    throw new AggregateException($"Plugin activation failed in assembly '{AsmName(asm)}'.", pluginActivationFailures);

                int builtinCount = 0;
                int intrinsicCount = 0;
                StagedBuiltinRegistry stagedAttrBuiltins = new(builtins);
                StagedIntrinsicRegistry stagedAttrIntrinsics = new(intrinsics);

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
                        throw new InvalidOperationException($"GetMethods failed for type {t.FullName}.", ex);
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
                            throw new InvalidOperationException($"GetCustomAttributes(Builtin) failed for {t.FullName}.{m.Name}.", ex);
                        }

                        foreach (BuiltinAttribute b in battrs)
                        {
                            BuiltinInvoker inv = (args, instr) => InvokeStaticMethod(m, args, instr)!;
                            bool smartAwait = b.SmartAwait || IsAwaitableReturnType(m.ReturnType);
                            stagedAttrBuiltins.Register(new BuiltinDescriptor(
                                b.Name,
                                b.ArityMin,
                                b.ArityMax,
                                inv,
                                smartAwait: smartAwait,
                                nonBlocking: b.NonBlocking));
                            builtinCount++;

                            if (Verbose)
                                LogInfo($"Staged builtin '{b.Name}' from {t.FullName}.{m.Name} (smartAwait={smartAwait}, nonBlocking={b.NonBlocking})");
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
                            throw new InvalidOperationException($"GetCustomAttributes(Intrinsic) failed for {t.FullName}.{m.Name}.", ex);
                        }

                        foreach (IntrinsicAttribute a in iattrs)
                        {
                            Type recv = a.ReceiverType ?? throw new InvalidOperationException($"Intrinsic '{a.Name}' has null receiver type on {t.FullName}.{m.Name}.");
                            string rname = recv.FullName ?? recv.Name;
                            IntrinsicInvoker inv = (recvObj, args, instr) => InvokeStaticMethod(m, recvObj, args, instr)!;
                            bool smartAwait = a.SmartAwait || IsAwaitableReturnType(m.ReturnType);
                            stagedAttrIntrinsics.Register(recv, new IntrinsicDescriptor(
                                a.Name,
                                a.ArityMin,
                                a.ArityMax,
                                inv,
                                smartAwait: smartAwait,
                                nonBlocking: a.NonBlocking));
                            intrinsicCount++;

                            if (Verbose)
                                LogInfo($"Staged intrinsic '{a.Name}' (recv={rname}) from {t.FullName}.{m.Name} (smartAwait={smartAwait}, nonBlocking={a.NonBlocking})");
                        }
                    }
                }

                CommitStagedRegistrations(stagedAttrBuiltins, stagedAttrIntrinsics, builtins, intrinsics, state);

                if (Verbose && (builtinCount > 0 || intrinsicCount > 0))
                    LogInfo($"Attribute registration summary for '{AsmName(asm)}': builtins={builtinCount}, intrinsics={intrinsicCount}");

                completed = true;
            }
            finally
            {
                if (!completed)
                    _ = state.LoadedAssemblies.TryRemove(key, out _);
            }
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
        /// The InvokeStaticMethod
        /// </summary>
        /// <param name="method">The method<see cref="MethodInfo"/></param>
        /// <param name="args">The args<see cref="object?[]"/></param>
        /// <returns>The <see cref="object?"/></returns>
        private static object? InvokeStaticMethod(MethodInfo method, params object?[] args)
        {
            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        /// The IsAwaitableReturnType
        /// </summary>
        /// <param name="returnType">The returnType<see cref="Type"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool IsAwaitableReturnType(Type returnType)
        {
            if (typeof(Task).IsAssignableFrom(returnType))
                return true;

            if (returnType == typeof(ValueTask))
                return true;

            if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
                return true;

            return false;
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

