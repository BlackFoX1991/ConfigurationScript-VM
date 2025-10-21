using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CFGS_VM.VMCore.Extensions.internal_plugin
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

                return Task.Run<object?>(async () =>
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
                });
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_post", 2, 4, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                string body = args[1]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 3 && args[2] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 4 ? Convert.ToInt32(args[3], CultureInfo.InvariantCulture) : 100000;

                return Task.Run<object?>(async () =>
                {
                    using CancellationTokenSource cts = new(timeout);
                    using HttpRequestMessage req = new(HttpMethod.Post, url);

                    req.Content = new StringContent(body, Encoding.UTF8, "text/plain");

                    ApplyHeaders(req, headers);

                    using HttpResponseMessage resp = await _client
                        .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                        .ConfigureAwait(false);

                    string respBody = await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                    return ToVmResponse(resp, respBody);
                });
            }, smartAwait: true));

            builtins.Register(new BuiltinDescriptor("http_download", 2, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                string path = args[1]?.ToString() ?? "";
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;

                bool allowFile = true;
                try
                {
                    Type? t = Type.GetType("CFGS_VM.VMCore.CorePlugin.CFGS_STDLIB, CFGS_VM");
                    if (t != null)
                    {
                        System.Reflection.PropertyInfo? p = t.GetProperty("AllowFileIO", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (p != null && p.PropertyType == typeof(bool))
                            allowFile = (bool)(p.GetValue(null) ?? true);
                    }
                }
                catch { }

                if (!allowFile)
                    throw new VMException("Runtime error: file I/O is disabled (AllowFileIO=false)", instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream);

                return Task.Run<object?>(async () =>
                {
                    using CancellationTokenSource cts = new(timeout);
                    byte[] bytes = await _client.GetByteArrayAsync(url, cts.Token).ConfigureAwait(false);
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
                    await File.WriteAllBytesAsync(path, bytes, cts.Token).ConfigureAwait(false);
                    return (long)bytes.LongLength;
                });
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
            intrinsics.Register(T, new IntrinsicDescriptor("is_running", 0, 0, (recv, a, i) => ((ServerHandle)recv).IsRunning));
            intrinsics.Register(T, new IntrinsicDescriptor("pending_count", 0, 0, (recv, a, i) => ((ServerHandle)recv).PendingCount));

            intrinsics.Register(T, new IntrinsicDescriptor("poll", 0, 1, (recv, a, i) =>
            {
                int? timeout = a.Count >= 1 ? Convert.ToInt32(a[0], CultureInfo.InvariantCulture) : (int?)null;
                return ((ServerHandle)recv).Poll(timeout);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("respond", 3, 4, (recv, a, i) =>
            {
                string id = a[0]?.ToString() ?? "";
                int status = Convert.ToInt32(a[1], CultureInfo.InvariantCulture);
                string body = a[2]?.ToString() ?? "";
                Dictionary<string, object>? headers = a.Count >= 4 && a[3] is Dictionary<string, object> d ? d : null;
                return ((ServerHandle)recv).Respond(id, status, body, headers);
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, a, i) => { ((ServerHandle)recv).Close(); return 1; }));
        }

        /// <summary>
        /// Defines the <see cref="ServerHandle" />
        /// </summary>
        public sealed class ServerHandle
        {
            /// <summary>
            /// Defines the _listener
            /// </summary>
            private readonly HttpListener _listener = new();

            /// <summary>
            /// Defines the _cts
            /// </summary>
            private readonly CancellationTokenSource _cts = new();

            /// <summary>
            /// Defines the _inflight
            /// </summary>
            private readonly ConcurrentDictionary<string, HttpListenerContext> _inflight = new();

            /// <summary>
            /// Defines the _queue
            /// </summary>
            private readonly ConcurrentQueue<string> _queue = new();

            /// <summary>
            /// Defines the _port
            /// </summary>
            private readonly int _port;

            /// <summary>
            /// Defines the _loop
            /// </summary>
            private Task? _loop;

            /// <summary>
            /// Defines the _running
            /// </summary>
            private volatile bool _running;

            /// <summary>
            /// Defines the ActiveByPort
            /// </summary>
            private static readonly ConcurrentDictionary<int, ServerHandle> ActiveByPort = new();

            /// <summary>
            /// Defines the _activeResponses
            /// </summary>
            private int _activeResponses = 0;

            /// <summary>
            /// Defines the _noActiveResponses
            /// </summary>
            private readonly ManualResetEventSlim _noActiveResponses = new(initialState: true);

            /// <summary>
            /// Initializes a new instance of the <see cref="ServerHandle"/> class.
            /// </summary>
            /// <param name="port">The port<see cref="int"/></param>
            public ServerHandle(int port)
            {
                if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
                _port = port;
                _listener.Prefixes.Add($"http://localhost:{_port}/");
                _listener.IgnoreWriteExceptions = true;
            }

            /// <summary>
            /// Gets a value indicating whether IsRunning
            /// </summary>
            public bool IsRunning => _running;

            /// <summary>
            /// Gets the PendingCount
            /// </summary>
            public int PendingCount => _queue.Count;

            /// <summary>
            /// The Start
            /// </summary>
            public void Start()
            {
                if (_running) return;

                if (ActiveByPort.TryGetValue(_port, out ServerHandle? prev) && !ReferenceEquals(prev, this))
                {
                    try { prev.Stop(); } catch { }
                    try { prev.Close(); } catch { }
                    Thread.Sleep(100);
                }

                ActiveByPort[_port] = this;

                try
                {
                    _listener.Start();
                }
                catch (HttpListenerException ex)
                {
                    ActiveByPort.TryRemove(_port, out _);
                    throw new VMException(
                        $"Cannot start HTTP server on http://localhost:{_port}/: {ex.Message} (code {ex.ErrorCode})",
                        0, 0, "", VM.IsDebugging, VM.DebugStream
                    );
                }

                _running = true;
                _loop = Task.Run(LoopAsync);
            }

            /// <summary>
            /// The Stop
            /// </summary>
            public void Stop()
            {
                if (!_running) return;
                _running = false;
                _cts.Cancel();
                try { _listener.Stop(); } catch { }
                try { _loop?.Wait(2000); } catch { }
            }

            /// <summary>
            /// The Close
            /// </summary>
            public void Close()
            {
                try { Stop(); } catch { }

                try { _noActiveResponses.Wait(TimeSpan.FromSeconds(2)); } catch { }

                try { _listener.Close(); } catch { }
                Thread.Sleep(100);
                _cts.Dispose();

                ActiveByPort.TryRemove(_port, out _);
            }

            /// <summary>
            /// The Poll
            /// </summary>
            /// <param name="timeoutMs">The timeoutMs<see cref="int?"/></param>
            /// <returns>The <see cref="Dictionary{string, object}?"/></returns>
            public Dictionary<string, object>? Poll(int? timeoutMs = null)
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
                        Thread.Sleep(10);
                        if (!_running) break;
                    }
                }
                return null;
            }

            /// <summary>
            /// The Respond
            /// </summary>
            /// <param name="id">The id<see cref="string"/></param>
            /// <param name="status">The status<see cref="int"/></param>
            /// <param name="body">The body<see cref="string"/></param>
            /// <param name="headers">The headers<see cref="Dictionary{string, object}?"/></param>
            /// <returns>The <see cref="int"/></returns>
            public int Respond(string id, int status, string body, Dictionary<string, object>? headers)
            {
                if (!_inflight.TryRemove(id, out HttpListenerContext? ctx))
                    return 0;

                _noActiveResponses.Reset();
                Interlocked.Increment(ref _activeResponses);

                HttpListenerResponse resp = ctx.Response;
                resp.StatusCode = status;
                resp.KeepAlive = false;

                bool methodIsHead = string.Equals(ctx.Request.HttpMethod, "HEAD", StringComparison.OrdinalIgnoreCase);
                bool noBody = methodIsHead || status == 204 || status == 304 || (status >= 100 && status < 200);

                if (headers != null)
                {
                    foreach (KeyValuePair<string, object> kv in headers)
                    {
                        string k = kv.Key;
                        string v = kv.Value?.ToString() ?? "";

                        if (k.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                        if (k.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase)) continue;
                        if (k.Equals("Connection", StringComparison.OrdinalIgnoreCase)) continue;
                        if (k.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)) continue;
                        if (k.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) continue;

                        if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                            resp.ContentType = v;
                        else
                            resp.Headers[k] = v;
                    }
                }
                if (string.IsNullOrWhiteSpace(resp.ContentType))
                    resp.ContentType = "text/plain; charset=utf-8";

                byte[] payload = Array.Empty<byte>();
                if (!noBody)
                    payload = Encoding.UTF8.GetBytes(body ?? "");

                resp.SendChunked = false;
                resp.ContentLength64 = noBody ? 0 : payload.LongLength;

                try
                {
                    if (!noBody && payload.Length > 0)
                    {
                        Stream s = resp.OutputStream;
                        int off = 0;
                        while (off < payload.Length)
                        {
                            int n = Math.Min(64 * 1024, payload.Length - off);
                            s.Write(payload, off, n);
                            off += n;
                        }
                        s.Flush();
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch (HttpListenerException ex)
                {
                    if (ex.ErrorCode != 64 && ex.ErrorCode != 1229) throw;
                }
                catch (IOException ioEx) when (ioEx.InnerException is HttpListenerException hlex
                                               && (hlex.ErrorCode == 64 || hlex.ErrorCode == 1229))
                {
                }
                finally
                {
                    try { resp.OutputStream.Close(); } catch { }
                    try { resp.Close(); } catch { }

                    if (Interlocked.Decrement(ref _activeResponses) == 0)
                        _noActiveResponses.Set();
                }

                return 1;
            }

            /// <summary>
            /// The LoopAsync
            /// </summary>
            /// <returns>The <see cref="Task"/></returns>
            private async Task LoopAsync()
            {
                while (!_cts.IsCancellationRequested)
                {
                    HttpListenerContext? ctx = null;
                    try
                    {
                        ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (HttpListenerException) when (_cts.IsCancellationRequested) { break; }
                    catch (ObjectDisposedException) when (_cts.IsCancellationRequested) { break; }
                    catch
                    {
                        if (!_running) break;
                    }

                    if (ctx == null) continue;

                    string id = Guid.NewGuid().ToString("N");
                    _inflight[id] = ctx;
                    _queue.Enqueue(id);
                }
            }

            /// <summary>
            /// The TryBuildRequestDict
            /// </summary>
            /// <param name="id">The id<see cref="string"/></param>
            /// <returns>The <see cref="Dictionary{string, object}?"/></returns>
            private Dictionary<string, object>? TryBuildRequestDict(string id)
            {
                if (!_inflight.TryGetValue(id, out HttpListenerContext? ctx)) return null;

                HttpListenerRequest req = ctx.Request;
                string body = "";
                try
                {
                    using StreamReader sr = new(req.InputStream, req.ContentEncoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 8192, leaveOpen: true);
                    body = sr.ReadToEnd();
                }
                catch { }

                Dictionary<string, object> headers = new(StringComparer.OrdinalIgnoreCase);
                foreach (string key in req.Headers.AllKeys)
                    headers[key] = req.Headers[key] ?? "";

                Dictionary<string, object> query = ParseQuery(req.Url?.Query);

                Dictionary<string, object> dict = new()
                {
                    ["id"] = id,
                    ["method"] = req.HttpMethod ?? "GET",
                    ["path"] = req.Url?.AbsolutePath ?? "/",
                    ["query"] = query,
                    ["headers"] = headers,
                    ["body"] = body,
                    ["remote"] = req.RemoteEndPoint?.ToString() ?? ""
                };
                return dict;
            }
        }

        /// <summary>
        /// The ParseQuery
        /// </summary>
        /// <param name="queryString">The queryString<see cref="string?"/></param>
        /// <returns>The <see cref="Dictionary{string, object}"/></returns>
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
                query[key] = val ?? "";
            }
            return query;
        }
    }
}
