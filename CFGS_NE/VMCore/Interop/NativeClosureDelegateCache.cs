using System.Runtime.CompilerServices;
using CFGS_VM.VMCore.Extensions.Core;

namespace CFGS_VM.VMCore.Interop
{
    /// <summary>
    /// Caches CLR delegates compiled for CFGS closures.
    /// <para>
    /// Entries are keyed by <b>closure reference identity</b> (not hash codes), so two
    /// distinct closures can never collide onto the same compiled delegate. The closure
    /// is held <b>weakly</b> as the primary key, so when a closure becomes unreachable its
    /// compiled delegates are released automatically instead of accumulating for the whole
    /// VM lifetime.
    /// </para>
    /// </summary>
    internal sealed class NativeClosureDelegateCache
    {
        private readonly struct Entry
        {
            public Entry(object? receiver, Type delegateType, Delegate compiled)
            {
                Receiver = receiver;
                DelegateType = delegateType;
                Delegate = compiled;
            }

            public object? Receiver { get; }
            public Type DelegateType { get; }
            public Delegate Delegate { get; }
        }

        private readonly ConditionalWeakTable<Closure, List<Entry>> _entries = new();
        private readonly object _sync = new();

        /// <summary>
        /// Returns the cached delegate for the given (closure, receiver, delegate type) triple,
        /// compiling and caching a new one via <paramref name="factory"/> on a miss. Matching is
        /// by reference identity for closure and receiver and by exact <see cref="Type"/> equality
        /// for the delegate type.
        /// </summary>
        public Delegate GetOrAdd(Closure closure, object? receiver, Type delegateType, Func<Delegate> factory)
        {
            lock (_sync)
            {
                List<Entry> list = _entries.GetOrCreateValue(closure);
                foreach (Entry entry in list)
                {
                    if (ReferenceEquals(entry.Receiver, receiver) && entry.DelegateType == delegateType)
                        return entry.Delegate;
                }

                Delegate compiled = factory();
                list.Add(new Entry(receiver, delegateType, compiled));
                return compiled;
            }
        }
    }
}
