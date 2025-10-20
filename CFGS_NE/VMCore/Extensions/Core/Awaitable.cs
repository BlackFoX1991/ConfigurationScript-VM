namespace CFGS_VM.VMCore.Extensions.Core
{
    /// <summary>
    /// Defines the <see cref="IAwaitable" />
    /// </summary>
    public interface IAwaitable
    {
        /// <summary>
        /// The AsTask
        /// </summary>
        /// <returns>The <see cref="Task{object?}"/></returns>
        Task<object?> AsTask();
    }

    /// <summary>
    /// Defines the <see cref="AwaitableAdapter" />
    /// </summary>
    internal static class AwaitableAdapter
    {
        /// <summary>
        /// The TryGetTask
        /// </summary>
        /// <param name="v">The v<see cref="object?"/></param>
        /// <param name="task">The task<see cref="Task{object?}"/></param>
        /// <returns>The <see cref="bool"/></returns>
        public static bool TryGetTask(object? v, out Task<object?> task)
        {
            switch (v)
            {
                case Task<object?> tObj:
                    task = tObj;
                    return true;

                case Task t:
                    task = t.ContinueWith<object?>(_ => null, TaskScheduler.Default);
                    return true;

                case ValueTask<object?> vtObj:
                    task = vtObj.AsTask();
                    return true;

                case ValueTask vt:
                    task = vt.AsTask().ContinueWith<object?>(_ => null, TaskScheduler.Default);
                    return true;

                case IAwaitable aw:
                    task = aw.AsTask();
                    return true;

                default:
                    task = null!;
                    return false;
            }
        }
    }

}
