using CFGS_VM.VMCore;

namespace CFGS_VM.Hosting
{
    public sealed class CfgsScript
    {
        private readonly VM _vm;
        private readonly CfgsScriptPermissions _permissions;
        private bool _topLevelRan;

        internal CfgsScript(VM vm, CompiledScript compiledScript, string originFile, CfgsScriptPermissions permissions)
        {
            _vm = vm;
            CompiledScript = compiledScript;
            OriginFile = originFile;
            _permissions = permissions;
        }

        public CompiledScript CompiledScript { get; }

        public string OriginFile { get; }

        public VM Runtime => _vm;

        public async Task RunAsync(bool autoInvokeMain = true, CancellationToken cancellationToken = default)
        {
            if (_topLevelRan)
                return;

            try
            {
                using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(cancellationToken, out CancellationToken effectiveToken);
                bool previousFileIo = VM.AllowFileIO;
                VM.AllowFileIO = _permissions.AllowFileIO;
                try
                {
                    await _vm.RunAsync(ct: effectiveToken).ConfigureAwait(false);
                    _topLevelRan = true;

                    if (autoInvokeMain && CompiledScript.ShouldAutoInvokeMain)
                        _ = await InvokeAsync("main", args: null, effectiveToken).ConfigureAwait(false);
                }
                finally
                {
                    VM.AllowFileIO = previousFileIo;
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public Task<object?> InvokeAsync(string functionName, params object[] args)
            => InvokeAsync(functionName, (IEnumerable<object?>)args);

        public async Task<object?> InvokeAsync(string functionName, IEnumerable<object?>? args, CancellationToken cancellationToken = default)
        {
            try
            {
                using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(cancellationToken, out CancellationToken effectiveToken);
                effectiveToken.ThrowIfCancellationRequested();
                List<object> runtimeArgs = args?.Select(static arg => VM.NormalizeHostValue(arg)!).ToList() ?? [];
                Task<object?> call = _vm.InvokeGlobalFunctionAsync(functionName, runtimeArgs, OriginFile, effectiveToken);
                return await WaitWithCancellation(call, effectiveToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public async Task<T?> InvokeAsync<T>(string functionName, params object[] args)
            => VM.ConvertToHostValue<T>(await InvokeAsync(functionName, (IEnumerable<object?>)args).ConfigureAwait(false));

        public async Task<T?> InvokeAsync<T>(string functionName, IEnumerable<object?>? args, CancellationToken cancellationToken = default)
            => VM.ConvertToHostValue<T>(await InvokeAsync(functionName, args, cancellationToken).ConfigureAwait(false));

        public async Task<object?> NewAsync(string className, params object[] args)
            => await NewAsync(className, (IEnumerable<object?>)args).ConfigureAwait(false);

        public async Task<object?> NewAsync(string className, IEnumerable<object?>? args, CancellationToken cancellationToken = default)
        {
            try
            {
                using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(cancellationToken, out CancellationToken effectiveToken);
                Task<object?> call = _vm.NewAsync(className, args, OriginFile, effectiveToken);
                return await WaitWithCancellation(call, effectiveToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public object? GetMember(object target, string memberName)
        {
            try { return _vm.GetMember(target, memberName, OriginFile); }
            catch (Exception ex) { throw CfgsScriptException.From(ex, OriginFile); }
        }

        public T? GetMember<T>(object target, string memberName)
            => VM.ConvertToHostValue<T>(GetMember(target, memberName));

        public void SetMember(object target, string memberName, object? value)
        {
            try { _vm.SetMember(target, memberName, value, OriginFile); }
            catch (Exception ex) { throw CfgsScriptException.From(ex, OriginFile); }
        }

        public async Task<object?> InvokeMemberAsync(object target, string memberName, params object[] args)
            => await InvokeMemberAsync(target, memberName, (IEnumerable<object?>)args).ConfigureAwait(false);

        public async Task<object?> InvokeMemberAsync(object target, string memberName, IEnumerable<object?>? args, CancellationToken cancellationToken = default)
        {
            try
            {
                using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(cancellationToken, out CancellationToken effectiveToken);
                Task<object?> call = _vm.InvokeMemberAsync(target, memberName, args, OriginFile, effectiveToken);
                return await WaitWithCancellation(call, effectiveToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public async Task<T?> InvokeMemberAsync<T>(object target, string memberName, params object[] args)
            => VM.ConvertToHostValue<T>(await InvokeMemberAsync(target, memberName, (IEnumerable<object?>)args).ConfigureAwait(false));

        public static object? NormalizeValue(object? value)
            => VM.NormalizeHostValue(value);

        public static List<object> CreateArray(params object?[] values)
            => VM.CreateHostArray(values);

        public static Dictionary<string, object> CreateDictionary(IDictionary<string, object?> values)
            => VM.CreateHostDictionary(values);

        public static T? ToHost<T>(object? value)
            => VM.ConvertToHostValue<T>(value);

        public bool TryGetGlobal(string name, out object? value)
            => _vm.TryGetGlobal(name, out value);

        public object? GetGlobal(string name)
            => _vm.GetGlobal(name);

        public T? GetGlobal<T>(string name)
            => _vm.GetGlobal<T>(name);

        public IReadOnlyDictionary<string, object?> GetGlobals()
            => _vm.GetGlobals();

        public void SetGlobal(string name, object? value)
            => _vm.SetGlobal(name, value);

        public bool TrySetGlobal(string name, object? value)
            => _vm.TrySetGlobal(name, value);

        public void SaveCfb(string path)
            => CfbFile.Save(CompiledScript, path);

        private CancellationTokenSource? CreateTimeoutTokenSource(CancellationToken cancellationToken, out CancellationToken effectiveToken)
        {
            if (_permissions.MaxExecutionTime is not TimeSpan timeout)
            {
                effectiveToken = cancellationToken;
                return null;
            }

            CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            effectiveToken = cts.Token;
            return cts;
        }

        private static async Task<T> WaitWithCancellation<T>(Task<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled)
                return await task.ConfigureAwait(false);

            Task completed = await Task.WhenAny(task, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await ((Task<T>)completed).ConfigureAwait(false);
        }
    }
}
