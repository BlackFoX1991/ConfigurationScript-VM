using CFGS_VM.VMCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CFGS_VM.Hosting
{
    public sealed class CfgsScript
    {
        private readonly VM _vm;
        private readonly CfgsScriptPermissions _permissions;
        private readonly SemaphoreSlim _operationGate = new(1, 1);
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
            try
            {
                using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(cancellationToken, out CancellationToken effectiveToken);
                await _operationGate.WaitAsync(effectiveToken).ConfigureAwait(false);
                try
                {
                    using IDisposable fileIoScope = VM.PushAllowFileIOScope(_permissions.AllowFileIO);
                    await RunCoreAsync(autoInvokeMain, effectiveToken).ConfigureAwait(false);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        private async Task RunCoreAsync(bool autoInvokeMain, CancellationToken effectiveToken)
        {
            if (_topLevelRan)
                return;

            await _vm.RunAsync(ct: effectiveToken).ConfigureAwait(false);
            _topLevelRan = true;

            if (autoInvokeMain && CompiledScript.ShouldAutoInvokeMain)
                _ = await InvokeCoreAsync("main", args: null, effectiveToken).ConfigureAwait(false);
        }

        public Task<object?> InvokeAsync(string functionName, params object[] args)
            => InvokeAsync(functionName, (IEnumerable<object?>)args);

        public async Task<object?> InvokeAsync(string functionName, IEnumerable<object?>? args, CancellationToken cancellationToken = default)
        {
            try
            {
                using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(cancellationToken, out CancellationToken effectiveToken);
                await _operationGate.WaitAsync(effectiveToken).ConfigureAwait(false);
                try
                {
                    using IDisposable fileIoScope = VM.PushAllowFileIOScope(_permissions.AllowFileIO);
                    return await InvokeCoreAsync(functionName, args, effectiveToken).ConfigureAwait(false);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        private async Task<object?> InvokeCoreAsync(string functionName, IEnumerable<object?>? args, CancellationToken effectiveToken)
        {
            effectiveToken.ThrowIfCancellationRequested();
            List<object> runtimeArgs = args?.Select(static arg => VM.NormalizeHostValue(arg)!).ToList() ?? [];
            Task<object?> call = _vm.InvokeGlobalFunctionAsync(functionName, runtimeArgs, OriginFile, effectiveToken);
            return await WaitWithCancellation(call, effectiveToken).ConfigureAwait(false);
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
                await _operationGate.WaitAsync(effectiveToken).ConfigureAwait(false);
                try
                {
                    using IDisposable fileIoScope = VM.PushAllowFileIOScope(_permissions.AllowFileIO);
                    Task<object?> call = _vm.NewAsync(className, args, OriginFile, effectiveToken);
                    return await WaitWithCancellation(call, effectiveToken).ConfigureAwait(false);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public object? GetMember(object target, string memberName)
        {
            try
            {
                _operationGate.Wait();
                try
                {
                    using IDisposable fileIoScope = VM.PushAllowFileIOScope(_permissions.AllowFileIO);
                    return _vm.GetMember(target, memberName, OriginFile);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public T? GetMember<T>(object target, string memberName)
            => VM.ConvertToHostValue<T>(GetMember(target, memberName));

        public void SetMember(object target, string memberName, object? value)
        {
            try
            {
                _operationGate.Wait();
                try
                {
                    using IDisposable fileIoScope = VM.PushAllowFileIOScope(_permissions.AllowFileIO);
                    _vm.SetMember(target, memberName, value, OriginFile);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public async Task<object?> InvokeMemberAsync(object target, string memberName, params object[] args)
            => await InvokeMemberAsync(target, memberName, (IEnumerable<object?>)args).ConfigureAwait(false);

        public async Task<object?> InvokeMemberAsync(object target, string memberName, IEnumerable<object?>? args, CancellationToken cancellationToken = default)
        {
            try
            {
                using CancellationTokenSource? timeoutCts = CreateTimeoutTokenSource(cancellationToken, out CancellationToken effectiveToken);
                await _operationGate.WaitAsync(effectiveToken).ConfigureAwait(false);
                try
                {
                    using IDisposable fileIoScope = VM.PushAllowFileIOScope(_permissions.AllowFileIO);
                    Task<object?> call = _vm.InvokeMemberAsync(target, memberName, args, OriginFile, effectiveToken);
                    return await WaitWithCancellation(call, effectiveToken).ConfigureAwait(false);
                }
                finally
                {
                    _operationGate.Release();
                }
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
        {
            try
            {
                _operationGate.Wait();
                try
                {
                    return _vm.TryGetGlobal(name, out value);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                value = null;
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public object? GetGlobal(string name)
        {
            try
            {
                _operationGate.Wait();
                try
                {
                    return _vm.GetGlobal(name);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public T? GetGlobal<T>(string name)
            => VM.ConvertToHostValue<T>(GetGlobal(name));

        public IReadOnlyDictionary<string, object?> GetGlobals()
        {
            try
            {
                _operationGate.Wait();
                try
                {
                    return _vm.GetGlobals();
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public void SetGlobal(string name, object? value)
        {
            try
            {
                _operationGate.Wait();
                try
                {
                    _vm.SetGlobal(name, value);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

        public bool TrySetGlobal(string name, object? value)
        {
            try
            {
                _operationGate.Wait();
                try
                {
                    return _vm.TrySetGlobal(name, value);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (Exception ex)
            {
                throw CfgsScriptException.From(ex, OriginFile);
            }
        }

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
