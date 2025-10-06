using System.Reflection;

namespace CFGS_VM.VMCore.Plugin
{
    /// <summary>
    /// Defines the <see cref="PluginLoader" />
    /// </summary>
    public static class PluginLoader
    {
        /// <summary>
        /// The LoadDirectory
        /// </summary>
        /// <param name="directoryPath">The directoryPath<see cref="string"/></param>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public static void LoadDirectory(string directoryPath, IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {

            if (!Directory.Exists(directoryPath)) return;
            foreach (string dll in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    Assembly asm = Assembly.LoadFrom(dll);
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
            foreach (Type? t in asm.GetTypes().Where(t => !t.IsAbstract && typeof(IVmPlugin).IsAssignableFrom(t)))
            {
                try
                {
                    IVmPlugin plugin = (IVmPlugin)Activator.CreateInstance(t)!;
                    plugin.Register(builtins, intrinsics);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[PluginLoader] Failed to activate {t.FullName}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (Type t in asm.GetTypes())
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    foreach (BuiltinAttribute b in m.GetCustomAttributes(typeof(BuiltinAttribute), inherit: false).Cast<BuiltinAttribute>())
                    {
                        BuiltinInvoker inv = (args, instr) => m.Invoke(null, new object?[] { args, instr })!;
                        builtins.Register(new BuiltinDescriptor(b.Name, b.ArityMin, b.ArityMax, inv));
                    }
                    foreach (IntrinsicAttribute a in m.GetCustomAttributes(typeof(IntrinsicAttribute), inherit: false).Cast<IntrinsicAttribute>())
                    {
                        IntrinsicInvoker inv = (recv, args, instr) => m.Invoke(null, new object?[] { recv, args, instr })!;
                        intrinsics.Register(a.ReceiverType, new IntrinsicDescriptor(a.Name, a.ArityMin, a.ArityMax, inv));
                    }
                }
            }
        }
    }
}
