using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CFGS_VM.VMCore;

namespace CFGS.Web.Http
{
    /// <summary>
    /// Defines the <see cref="CFGS_HTTP" />
    /// </summary>
    public sealed class CFGS_HTTP : IVmPlugin
    {
        /// <summary>
        /// Defines the _client
        /// </summary>
        private static readonly HttpClient _client = CreateClient();

        /// <summary>
        /// The CreateClient
        /// </summary>
        /// <returns>The <see cref="HttpClient"/></returns>
        private static HttpClient CreateClient()
        {
            HttpClient h = new(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            h.Timeout = TimeSpan.FromSeconds(100);
            return h;
        }

        /// <summary>
        /// The Register
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics)
        {
            RegisterBuiltins(builtins);
            RegisterServerIntrinsics(intrinsics);

            builtins.Register(new BuiltinDescriptor("http_server", 1, 1, (args, instr) =>
            {
                int port = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                return new ServerHandle(port);
            }));
        }

        /// <summary>
        /// The RegisterBuiltins
        /// </summary>
        /// <param name="builtins">The builtins<see cref="IBuiltinRegistry"/></param>
        private static void RegisterBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("http_get", 1, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 2 && args[1] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;
                return HttpGetAsync(url, headers, timeout);
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_post", 2, 5, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                string body = args[1]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 3 && args[2] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 4 ? Convert.ToInt32(args[3], CultureInfo.InvariantCulture) : 100000;
                string contentType = args.Count >= 5 ? args[4]?.ToString() ?? "text/plain" : "text/plain";
                return HttpSendWithBodyAsync(HttpMethod.Post, url, body, headers, timeout, contentType);
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_put", 2, 5, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                string body = args[1]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 3 && args[2] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 4 ? Convert.ToInt32(args[3], CultureInfo.InvariantCulture) : 100000;
                string contentType = args.Count >= 5 ? args[4]?.ToString() ?? "text/plain" : "text/plain";
                return HttpSendWithBodyAsync(HttpMethod.Put, url, body, headers, timeout, contentType);
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_delete", 1, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 2 && args[1] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;
                return HttpDeleteAsync(url, headers, timeout);
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_patch", 2, 5, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                string body = args[1]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 3 && args[2] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 4 ? Convert.ToInt32(args[3], CultureInfo.InvariantCulture) : 100000;
                string contentType = args.Count >= 5 ? args[4]?.ToString() ?? "text/plain" : "text/plain";
                return HttpSendWithBodyAsync(HttpMethod.Patch, url, body, headers, timeout, contentType);
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_head", 1, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 2 && args[1] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;
                return HttpHeadAsync(url, headers, timeout);
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_download", 2, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                string path = args[1]?.ToString() ?? "";
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;

                bool allowFile = VM.AllowFileIO;
                try
                {
                    Type? t = Type.GetType("CFGS.StandardLibrary.CFGS_STDLIB, CFGS.StandardLibrary");
                    if (t != null)
                    {
                        System.Reflection.PropertyInfo? p = t.GetProperty("AllowFileIO", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (p != null && p.PropertyType == typeof(bool))
                            allowFile = (bool)(p.GetValue(null) ?? allowFile);
                    }
                }
                catch { }

                if (!allowFile)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);

                return HttpDownloadAsync(url, path, timeout);
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("urlencode", 1, 1, (args, instr) =>
            {
                return WebUtility.UrlEncode(args[0]?.ToString() ?? "");
            }));

            builtins.Register(new BuiltinDescriptor("urldecode", 1, 1, (args, instr) =>
            {
                return WebUtility.UrlDecode(args[0]?.ToString() ?? "");
            }));
        }

        /// <summary>
        /// The HttpGetAsync
        /// </summary>
        /// <param name="url">The url<see cref="string"/></param>
        /// <param name="headers">The headers<see cref="Dictionary{string, object}"/></param>
        /// <param name="timeout">The timeout<see cref="int"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> HttpGetAsync(string url, Dictionary<string, object> headers, int timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            using HttpRequestMessage req = new(HttpMethod.Get, url);

            if (HasContentType(headers))
                req.Content = new ByteArrayContent(Array.Empty<byte>());

            ApplyHeaders(req, headers);

            using HttpResponseMessage resp = await _client
                .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            string body = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return ToVmResponse(resp, body);
        }

        /// <summary>
        /// The HttpPostAsync
        /// </summary>
        /// <param name="url">The url<see cref="string"/></param>
        /// <param name="body">The body<see cref="string"/></param>
        /// <param name="headers">The headers<see cref="Dictionary{string, object}"/></param>
        /// <param name="timeout">The timeout<see cref="int"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> HttpSendWithBodyAsync(HttpMethod method, string url, string body, Dictionary<string, object> headers, int timeout, string contentType)
        {
            using CancellationTokenSource cts = new(timeout);
            using HttpRequestMessage req = new(method, url);
            req.Content = new StringContent(body, Encoding.UTF8, contentType);

            ApplyHeaders(req, headers);

            using HttpResponseMessage resp = await _client
                .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            string respBody = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return ToVmResponse(resp, respBody);
        }

        private static async Task<object?> HttpDeleteAsync(string url, Dictionary<string, object> headers, int timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            using HttpRequestMessage req = new(HttpMethod.Delete, url);

            if (HasContentType(headers))
                req.Content = new ByteArrayContent(Array.Empty<byte>());

            ApplyHeaders(req, headers);

            using HttpResponseMessage resp = await _client
                .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            string respBody = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return ToVmResponse(resp, respBody);
        }

        private static async Task<object?> HttpHeadAsync(string url, Dictionary<string, object> headers, int timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            using HttpRequestMessage req = new(HttpMethod.Head, url);

            ApplyHeaders(req, headers);

            using HttpResponseMessage resp = await _client
                .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            return ToVmResponse(resp, "");
        }

        /// <summary>
        /// The HttpDownloadAsync
        /// </summary>
        /// <param name="url">The url<see cref="string"/></param>
        /// <param name="path">The path<see cref="string"/></param>
        /// <param name="timeout">The timeout<see cref="int"/></param>
        /// <returns>The <see cref="Task{object?}"/></returns>
        private static async Task<object?> HttpDownloadAsync(string url, string path, int timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            byte[] bytes = await _client.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            await File.WriteAllBytesAsync(path, bytes, cts.Token).ConfigureAwait(false);
            return (long)bytes.LongLength;
        }

        /// <summary>
        /// The HasContentType
        /// </summary>
        /// <param name="headers">The headers<see cref="Dictionary{string, object}"/></param>
        /// <returns>The <see cref="bool"/></returns>
        private static bool HasContentType(Dictionary<string, object> headers)
        {
            foreach (string k in headers.Keys)
                if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// The ApplyHeaders
        /// </summary>
        /// <param name="req">The req<see cref="HttpRequestMessage"/></param>
        /// <param name="headers">The headers<see cref="Dictionary{string, object}"/></param>
        private static void ApplyHeaders(HttpRequestMessage req, Dictionary<string, object> headers)
        {
            foreach (KeyValuePair<string, object> kv in headers)
            {
                string key = kv.Key;
                string val = kv.Value?.ToString() ?? "";

                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    if (req.Content == null)
                        req.Content = new ByteArrayContent(Array.Empty<byte>());
                    try
                    {
                        req.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(val);
                    }
                    catch
                    {
                        req.Content.Headers.TryAddWithoutValidation("Content-Type", val);
                    }
                    continue;
                }

                if (!req.Headers.TryAddWithoutValidation(key, val))
                {
                    if (req.Content == null)
                        req.Content = new ByteArrayContent(Array.Empty<byte>());
                    req.Content.Headers.TryAddWithoutValidation(key, val);
                }
            }
        }

        /// <summary>
        /// The ToVmResponse
        /// </summary>
        /// <param name="resp">The resp<see cref="HttpResponseMessage"/></param>
        /// <param name="body">The body<see cref="string"/></param>
        /// <returns>The <see cref="Dictionary{string, object}"/></returns>
        private static Dictionary<string, object> ToVmResponse(HttpResponseMessage resp, string body)
        {
            Dictionary<string, object> h = new(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, IEnumerable<string>> kv in resp.Headers)
            {
                if (kv.Value is null) continue;
                h[kv.Key] = string.Join(", ", kv.Value);
            }
            foreach (KeyValuePair<string, IEnumerable<string>> kv in resp.Content.Headers)
            {
                if (kv.Value is null) continue;
                h[kv.Key] = string.Join(", ", kv.Value);
            }

            return new Dictionary<string, object>
            {
                ["status"] = (int)resp.StatusCode,
                ["reason"] = resp.ReasonPhrase ?? "",
                ["headers"] = h,
                ["body"] = body
            };
        }

        /// <summary>
        /// The RegisterServerIntrinsics
        /// </summary>
        /// <param name="intrinsics">The intrinsics<see cref="IIntrinsicRegistry"/></param>
        private static void RegisterServerIntrinsics(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(ServerHandle);

            intrinsics.Register(T, new IntrinsicDescriptor("start", 0, 0, (recv, a, i) => { ((ServerHandle)recv).Start(); return recv!; }));
            intrinsics.Register(T, new IntrinsicDescriptor("stop", 0, 0, (recv, a, i) => { ((ServerHandle)recv).Stop(); return recv!; }));
            intrinsics.Register(T, new IntrinsicDescriptor("start_async", 0, 0, (recv, a, i) => ((ServerHandle)recv).StartAsync(), smartAwait: true));
            intrinsics.Register(T, new IntrinsicDescriptor("stop_async", 0, 0, (recv, a, i) => ((ServerHandle)recv).StopAsync(), smartAwait: true));
            intrinsics.Register(T, new IntrinsicDescriptor("is_running", 0, 0, (recv, a, i) => ((ServerHandle)recv).IsRunning));
            intrinsics.Register(T, new IntrinsicDescriptor("pending_count", 0, 0, (recv, a, i) => ((ServerHandle)recv).PendingCount));

            intrinsics.Register(T, new IntrinsicDescriptor("poll", 0, 1, (recv, a, i) =>
            {
                int? timeout = a.Count >= 1 ? Convert.ToInt32(a[0], CultureInfo.InvariantCulture) : (int?)null;
                return ((ServerHandle)recv).Poll(timeout)!;
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("poll_async", 0, 1, (recv, a, i) =>
            {
                int? timeout = a.Count >= 1 ? Convert.ToInt32(a[0], CultureInfo.InvariantCulture) : (int?)null;
                return ((ServerHandle)recv).PollAsync(timeout)!;
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("respond", 3, 4, (recv, a, i) =>
            {
                string id = a[0]?.ToString() ?? "";
                int status = Convert.ToInt32(a[1], CultureInfo.InvariantCulture);
                string body = a[2]?.ToString() ?? "";
                Dictionary<string, object>? headers = a.Count >= 4 && a[3] is Dictionary<string, object> d ? d : null;
                return ((ServerHandle)recv).Respond(id, status, body, headers);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("respond_async", 3, 4, (recv, a, i) =>
            {
                string id = a[0]?.ToString() ?? "";
                int status = Convert.ToInt32(a[1], CultureInfo.InvariantCulture);
                string body = a[2]?.ToString() ?? "";
                Dictionary<string, object>? headers = a.Count >= 4 && a[3] is Dictionary<string, object> d ? d : null;
                return ((ServerHandle)recv).RespondAsync(id, status, body, headers);
            }, smartAwait: true));

            intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, a, i) => { ((ServerHandle)recv).Close(); return 1; }));
            intrinsics.Register(T, new IntrinsicDescriptor("close_async", 0, 0, (recv, a, i) => ((ServerHandle)recv).CloseAsync(), smartAwait: true));
        }

        /// <summary>
        /// Defines the <see cref="ServerHandle" />
        /// </summary>
        public sealed class ServerHandle
        {
            private readonly CancellationTokenSource _cts = new();
            private readonly ConcurrentDictionary<string, PendingRequest> _inflight = new();
            private readonly ConcurrentQueue<string> _queue = new();
            private readonly int _port;
            private volatile bool _running;
            private WebApplication? _app;
            private static readonly ConcurrentDictionary<int, ServerHandle> ActiveByPort = new();
            private int _activeResponses = 0;
            private readonly ManualResetEventSlim _noActiveResponses = new(initialState: true);

            private sealed class PendingRequest(HttpContext context, Dictionary<string, object> request)
            {
                public HttpContext Context { get; } = context;
                public Dictionary<string, object> Request { get; } = request;
                public TaskCompletionSource<PendingResponse> ResponseSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            private sealed class PendingResponse(int status, string body, Dictionary<string, object>? headers)
            {
                public int Status { get; } = status;
                public string Body { get; } = body;
                public Dictionary<string, object>? Headers { get; } = headers;
            }

            public ServerHandle(int port)
            {
                if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
                _port = port;
            }

            public bool IsRunning => _running;
            public int PendingCount => _queue.Count;

            public void Start()
            {
                _ = StartAsync().GetAwaiter().GetResult();
            }

            public void Stop()
            {
                _ = StopAsync().GetAwaiter().GetResult();
            }

            public async Task<object?> StartAsync()
            {
                if (_running) return this;

                if (ActiveByPort.TryGetValue(_port, out ServerHandle? prev) && !ReferenceEquals(prev, this))
                {
                    try { await prev.StopAsync().ConfigureAwait(false); } catch { }
                    try { await prev.CloseAsync().ConfigureAwait(false); } catch { }
                }

                ActiveByPort[_port] = this;

                try
                {
                    WebApplication app = BuildApplication();
                    await app.StartAsync(_cts.Token).ConfigureAwait(false);
                    _app = app;
                }
                catch (Exception ex)
                {
                    ActiveByPort.TryRemove(_port, out _);
                    throw new VMException(
                        $"Cannot start HTTP server on http://localhost:{_port}/: {FlattenMessage(ex)}",
                        0, 0, "", VM.IsDebugging, VM.DebugStream!
                    );
                }

                _running = true;
                return this;
            }

            public async Task<object?> StopAsync()
            {
                if (!_running) return this;
                _running = false;
                _cts.Cancel();
                WebApplication? app = _app;
                if (app != null)
                {
                    try
                    {
                        using CancellationTokenSource stopCts = new(TimeSpan.FromSeconds(2));
                        await app.StopAsync(stopCts.Token).ConfigureAwait(false);
                    }
                    catch { }
                }

                return this;
            }

            public void Close()
            {
                _ = CloseAsync().GetAwaiter().GetResult();
            }

            public async Task<object?> CloseAsync()
            {
                try { await StopAsync().ConfigureAwait(false); } catch { }

                try
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    while (Volatile.Read(ref _activeResponses) > 0 && sw.ElapsedMilliseconds < 2000)
                        await Task.Delay(10).ConfigureAwait(false);
                }
                catch { }

                WebApplication? app = _app;
                _app = null;
                if (app != null)
                {
                    try { await app.DisposeAsync().ConfigureAwait(false); } catch { }
                }
                _cts.Dispose();
                ActiveByPort.TryRemove(_port, out _);
                return 1;
            }

            public Dictionary<string, object>? Poll(int? timeoutMs = null)
            {
                object? polled = PollAsync(timeoutMs).GetAwaiter().GetResult();
                return polled as Dictionary<string, object>;
            }

            public async Task<object?> PollAsync(int? timeoutMs = null)
            {
                if (_queue.TryDequeue(out string? id))
                    return TryBuildRequestDict(id);

                if (timeoutMs.HasValue && timeoutMs.Value > 0)
                {
                    System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                    while (sw.ElapsedMilliseconds < timeoutMs.Value)
                    {
                        if (_queue.TryDequeue(out id))
                            return TryBuildRequestDict(id);
                        if (!_running) break;

                        int remaining = timeoutMs.Value - (int)sw.ElapsedMilliseconds;
                        int delayMs = Math.Clamp(remaining, 1, 10);
                        await Task.Delay(delayMs).ConfigureAwait(false);
                    }
                }
                return null;
            }

            public int Respond(string id, int status, string body, Dictionary<string, object>? headers)
            {
                object? response = RespondAsync(id, status, body, headers).GetAwaiter().GetResult();
                return Convert.ToInt32(response ?? 0, CultureInfo.InvariantCulture);
            }

            public async Task<object?> RespondAsync(string id, int status, string body, Dictionary<string, object>? headers)
            {
                if (!_inflight.TryGetValue(id, out PendingRequest? pending))
                    return 0;

                return pending.ResponseSource.TrySetResult(new PendingResponse(status, body ?? "", headers)) ? 1 : 0;
            }

            private Dictionary<string, object>? TryBuildRequestDict(string id)
            {
                return _inflight.TryGetValue(id, out PendingRequest? pending) ? pending.Request : null;
            }

            private WebApplication BuildApplication()
            {
                WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
                builder.WebHost.UseKestrelCore();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.AddServerHeader = false;
                    options.ListenLocalhost(_port, listenOptions => listenOptions.Protocols = HttpProtocols.Http1);
                });

                WebApplication app = builder.Build();
                app.Run(ctx => HandleIncomingRequestAsync(ctx));
                return app;
            }

            private async Task HandleIncomingRequestAsync(HttpContext ctx)
            {
                string id = Guid.NewGuid().ToString("N");
                Dictionary<string, object> request = await BuildRequestDictAsync(id, ctx.Request, ctx.Connection, _cts.Token).ConfigureAwait(false);
                PendingRequest pending = new(ctx, request);
                _inflight[id] = pending;
                _queue.Enqueue(id);

                try
                {
                    PendingResponse response = await pending.ResponseSource.Task.WaitAsync(_cts.Token).ConfigureAwait(false);
                    await ApplyResponseAsync(pending, response).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                    if (!ctx.Response.HasStarted)
                    {
                        ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                        ctx.Response.ContentLength = 0;
                    }
                }
                finally
                {
                    _inflight.TryRemove(id, out _);
                }
            }

            private async Task ApplyResponseAsync(PendingRequest pending, PendingResponse response)
            {
                _noActiveResponses.Reset();
                Interlocked.Increment(ref _activeResponses);

                HttpContext ctx = pending.Context;
                HttpResponse resp = ctx.Response;
                resp.StatusCode = response.Status;

                bool methodIsHead = string.Equals(ctx.Request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
                bool noBody = methodIsHead || response.Status == 204 || response.Status == 304 || (response.Status >= 100 && response.Status < 200);

                try
                {
                    if (response.Headers != null)
                    {
                        foreach (KeyValuePair<string, object> kv in response.Headers)
                        {
                            string k = kv.Key;
                            string v = kv.Value?.ToString() ?? "";

                            if (k.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)) continue;
                            if (k.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) continue;

                            if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            {
                                resp.ContentType = v;
                            }
                            else if (k.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                            {
                                string[] cookieLines = v.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (string cookieLine in cookieLines)
                                {
                                    string actualCookie = cookieLine.Trim();
                                    if (actualCookie.Length > 0)
                                        resp.Headers.Append("Set-Cookie", actualCookie);
                                }
                            }
                            else
                            {
                                resp.Headers[k] = v;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(resp.ContentType))
                        resp.ContentType = "text/plain; charset=utf-8";

                    if (string.IsNullOrWhiteSpace(resp.Headers["X-Content-Type-Options"]))
                        resp.Headers["X-Content-Type-Options"] = "nosniff";
                    if (string.IsNullOrWhiteSpace(resp.Headers["X-Frame-Options"]))
                        resp.Headers["X-Frame-Options"] = "DENY";

                    byte[] payload = noBody ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(response.Body ?? "");
                    resp.ContentLength = noBody ? 0 : payload.LongLength;

                    if (!noBody && payload.Length > 0)
                        await resp.Body.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cts.IsCancellationRequested)
                {
                }
                catch (IOException)
                {
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    if (Interlocked.Decrement(ref _activeResponses) == 0)
                        _noActiveResponses.Set();
                }
            }

            private static async Task<Dictionary<string, object>> BuildRequestDictAsync(
                string id,
                HttpRequest req,
                ConnectionInfo connection,
                CancellationToken cancellationToken)
            {
                string body = await ReadRequestBodyAsync(req, cancellationToken).ConfigureAwait(false);
                Dictionary<string, object> headers = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in req.Headers)
                    headers[header.Key] = string.Join(", ", header.Value.ToArray());

                return new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["method"] = req.Method ?? "GET",
                    ["path"] = req.Path.HasValue ? req.Path.Value! : "/",
                    ["query"] = ParseQuery(req.QueryString.HasValue ? req.QueryString.Value : ""),
                    ["headers"] = headers,
                    ["body"] = body,
                    ["remote"] = BuildRemote(connection)
                };
            }

            private static async Task<string> ReadRequestBodyAsync(HttpRequest req, CancellationToken cancellationToken)
            {
                const int maxBodySize = 10 * 1024 * 1024;
                long declaredLength = req.ContentLength ?? -1;
                if (declaredLength > maxBodySize)
                {
                    await DrainAsync(req.Body, cancellationToken).ConfigureAwait(false);
                    return $"[body too large: {declaredLength} bytes, limit {maxBodySize}]";
                }

                Encoding encoding = TryGetEncoding(req.ContentType) ?? Encoding.UTF8;
                using MemoryStream ms = new();
                byte[] buffer = new byte[8192];

                while (true)
                {
                    int read = await req.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                    if (read <= 0)
                        break;

                    if (ms.Length + read > maxBodySize)
                    {
                        long observed = ms.Length + read;
                        await DrainAsync(req.Body, cancellationToken).ConfigureAwait(false);
                        return $"[body too large: {observed} bytes, limit {maxBodySize}]";
                    }

                    ms.Write(buffer, 0, read);
                }

                return encoding.GetString(ms.ToArray());
            }

            private static async Task DrainAsync(Stream stream, CancellationToken cancellationToken)
            {
                byte[] buffer = new byte[8192];
                while (await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false) > 0)
                {
                }
            }

            private static Encoding? TryGetEncoding(string? contentType)
            {
                if (string.IsNullOrWhiteSpace(contentType))
                    return null;

                try
                {
                    MediaTypeHeaderValue parsed = MediaTypeHeaderValue.Parse(contentType);
                    if (!string.IsNullOrWhiteSpace(parsed.CharSet))
                        return Encoding.GetEncoding(parsed.CharSet);
                }
                catch
                {
                }

                return null;
            }

            private static string BuildRemote(ConnectionInfo connection)
            {
                if (connection.RemoteIpAddress == null)
                    return "";
                if (connection.RemotePort > 0)
                    return connection.RemoteIpAddress + ":" + connection.RemotePort.ToString(CultureInfo.InvariantCulture);
                return connection.RemoteIpAddress.ToString();
            }

            private static string FlattenMessage(Exception ex)
            {
                if (ex.InnerException == null)
                    return ex.Message;
                return ex.Message + " :: " + FlattenMessage(ex.InnerException);
            }
        }

        private static Dictionary<string, object> ParseQuery(string? queryString)
        {
            Dictionary<string, object> query = new(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(queryString)) return query;
            string q = queryString;
            if (q.StartsWith("?")) q = q.Substring(1);
            foreach (string part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = part.Split('=', 2);
                string? key = WebUtility.UrlDecode(kv[0]);
                string val = kv.Length > 1 ? WebUtility.UrlDecode(kv[1]) : "";
                if (key is null) continue;
                PushQueryValue(query, key, val ?? "");
            }
            return query;
        }

        private static void PushQueryValue(Dictionary<string, object> query, string key, string value)
        {
            if (!query.TryGetValue(key, out object? existing) || existing == null)
            {
                query[key] = value;
                return;
            }

            if (existing is List<object> list)
            {
                list.Add(value);
                return;
            }

            query[key] = new List<object> { existing, value };
        }
    }
}

