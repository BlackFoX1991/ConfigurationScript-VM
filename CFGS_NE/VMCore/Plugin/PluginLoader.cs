using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.Loader;

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
        /// The LoadDirectory
        /// </summary>
        /// <param name="directoryPath">The directoryPath<see cref="string"/></param>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadDirectory(string directoryPath, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            foreach (string dll in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    Assembly asm = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(dll));
                    LoadFromAssembly(asm, builtins, intrinsics);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[PluginLoader] Failed to load '{dll}': {ex.GetType().Name}: {ex.Message}");
                }
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

            string key = asm.FullName
                         ?? asm.GetName().Name
                         ?? asm.GetHashCode().ToString();

            if (!_loadedAssemblies.TryAdd(key, 0))
                return;

            Type[] types = Array.Empty<Type>();
            try
            {
                types = asm.GetTypes();
            }
            catch (ReflectionTypeLoadException rtle)
            {
                types = rtle.Types.Where(t => t != null).Cast<Type>().ToArray();
                Console.Error.WriteLine($"[PluginLoader] Partial type load for '{AsmName(asm)}' – continuing with {types.Length} types.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PluginLoader] GetTypes() failed for '{AsmName(asm)}': {ex.GetType().Name}: {ex.Message}");
                return;
            }

            foreach (Type t in types)
            {
                if (t is null) continue;
                if (t.IsAbstract) continue;
                if (!typeof(IVmPlugin).IsAssignableFrom(t)) continue;

                string tkey = t.FullName ?? t.Name;
                if (!_activatedPluginTypes.TryAdd(tkey, 0))
                    continue;

                try
                {
                    IVmPlugin plugin = (IVmPlugin)Activator.CreateInstance(t)!;
                    plugin.Register(builtins, intrinsics);
                }
                catch (TargetInvocationException tie)
                {
                    Console.Error.WriteLine($"[PluginLoader] Failed to activate {t.FullName}: {tie.InnerException?.GetType().Name}: {tie.InnerException?.Message}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[PluginLoader] Failed to activate {t.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (Type t in types)
            {
                if (t is null) continue;

                MethodInfo[] methods;
                try
                {
                    methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                catch
                {
                    continue;
                }

                foreach (MethodInfo m in methods)
                {
                    foreach (BuiltinAttribute b in m.GetCustomAttributes(typeof(BuiltinAttribute), inherit: false).Cast<BuiltinAttribute>())
                    {
                        string bkey = $"builtin::{b.Name}";
                        if (!_attrRegistered.TryAdd(bkey, 0))
                            continue;

                        try
                        {
                            BuiltinInvoker inv = (args, instr) => m.Invoke(null, new object?[] { args, instr })!;
                            builtins.Register(new BuiltinDescriptor(b.Name, b.ArityMin, b.ArityMax, inv));
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[PluginLoader] Builtin '{b.Name}' from {t.FullName}.{m.Name} failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }

                    foreach (IntrinsicAttribute a in m.GetCustomAttributes(typeof(IntrinsicAttribute), inherit: false).Cast<IntrinsicAttribute>())
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
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"[PluginLoader] Intrinsic '{a.Name}' (recv={rname}) from {t.FullName}.{m.Name} failed: {ex.GetType().Name}: {ex.Message}");
                        }
                    }
                }
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
                    Console.Error.WriteLine($"[PluginLoader] Skipped '{AsmName(asm)}': {ex.GetType().Name}: {ex.Message}");
                }
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

    }
}
