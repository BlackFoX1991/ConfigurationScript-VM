using System.Collections;
using System.Runtime.CompilerServices;

namespace CFGS_VM.VMCore
{
    /// <summary>
    /// Operand stack backed by a raw array plus a top pointer. Semantically identical to
    /// <see cref="Stack{Value}"/> (LIFO; enumeration and <see cref="ToArray"/> yield top-to-bottom),
    /// but <see cref="Push"/>/<see cref="Pop"/>/<see cref="Peek"/> are aggressively inlined bare
    /// array accesses instead of non-inlined framework method calls. Every opcode touches this stack,
    /// so shaving the per-access overhead speeds up the hottest loop in the VM across all workloads.
    /// </summary>
    internal sealed class ValueStack : IEnumerable<Value>
    {
        private Value[] _array;
        private int _size;

        public ValueStack()
        {
            _array = new Value[64];
        }

        /// <summary>Number of values currently on the stack.</summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _size;
        }

        /// <summary>Pushes a value onto the top of the stack.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Push(Value value)
        {
            Value[] array = _array;
            int size = _size;
            if ((uint)size < (uint)array.Length)
            {
                array[size] = value;
                _size = size + 1;
            }
            else
            {
                PushWithResize(value);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void PushWithResize(Value value)
        {
            Array.Resize(ref _array, _array.Length == 0 ? 4 : _array.Length * 2);
            _array[_size] = value;
            _size++;
        }

        /// <summary>Removes and returns the value at the top of the stack.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Value Pop()
        {
            int size = _size - 1;
            Value[] array = _array;
            if ((uint)size >= (uint)array.Length)
                ThrowEmpty();

            _size = size;
            Value value = array[size];
            array[size] = default; // release any reference the popped Value held, matching Stack<T>
            return value;
        }

        /// <summary>Returns the value at the top of the stack without removing it.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Value Peek()
        {
            int size = _size - 1;
            Value[] array = _array;
            if ((uint)size >= (uint)array.Length)
                ThrowEmpty();

            return array[size];
        }

        /// <summary>Removes all values.</summary>
        public void Clear()
        {
            Array.Clear(_array, 0, _size);
            _size = 0;
        }

        /// <summary>Copies the stack to a new array, top-to-bottom (matching <see cref="Stack{T}.ToArray"/>).</summary>
        public Value[] ToArray()
        {
            Value[] result = new Value[_size];
            for (int i = 0; i < _size; i++)
                result[i] = _array[_size - 1 - i];
            return result;
        }

        private static void ThrowEmpty()
        {
            throw new InvalidOperationException("Operand stack is empty.");
        }

        /// <summary>Enumerates top-to-bottom, matching <see cref="Stack{T}"/>'s LIFO enumeration order.</summary>
        public IEnumerator<Value> GetEnumerator()
        {
            for (int i = _size - 1; i >= 0; i--)
                yield return _array[i];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
