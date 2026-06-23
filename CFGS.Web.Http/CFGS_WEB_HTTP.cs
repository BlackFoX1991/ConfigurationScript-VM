using CFGS_VM.VMCore.Extensions;
using CFGS_VM.VMCore.Plugin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using CFGS_VM.VMCore;

namespace CFGS.Web.Http
{
    public sealed class CFGS_HTTP : IVmPlugin
    {
        private static readonly HttpClient _client = CreateClient();
        private const long DefaultMaxRequestBodySize = 10L * 1024L * 1024L;

        private static HttpClient CreateClient()
        {
            HttpClient h = new(new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            });
            h.Timeout = TimeSpan.FromSeconds(100);
            return h;
        }

        public void Register(IBuiltinRegistry builtins, IIntrinsicRegistry intrinsics, INamespaceRegistry namespaces)
        {
            BuiltinRegistry namespaceBuiltins = new();
            RegisterBuiltins(namespaceBuiltins);
            RegisterNamespaces(namespaces, namespaceBuiltins);
            RegisterServerIntrinsics(intrinsics);
            RegisterRequestBodyIntrinsics(intrinsics);
        }

        private static void RegisterNamespaces(INamespaceRegistry namespaces, BuiltinRegistry namespaceBuiltins)
        {
            Dictionary<string, BuiltinDescriptor> descriptors = namespaceBuiltins.Snapshot()
                .ToDictionary(static descriptor => descriptor.Name, StringComparer.Ordinal);

            (string MemberName, BuiltinDescriptor Descriptor)[] members =
            [
                ("server", descriptors["http_server"]),
                ("get", descriptors["http_get"]),
                ("post", descriptors["http_post"]),
                ("put", descriptors["http_put"]),
                ("delete", descriptors["http_delete"]),
                ("patch", descriptors["http_patch"]),
                ("head", descriptors["http_head"]),
                ("download", descriptors["http_download"]),
                ("urlencode", descriptors["urlencode"]),
                ("urldecode", descriptors["urldecode"])
            ];

            namespaces.Register(NamespaceDescriptor.FromDescriptors("http", members));
            namespaces.Register(NamespaceDescriptor.FromDescriptors("std.http", members));
        }

        private static void RegisterBuiltins(IBuiltinRegistry builtins)
        {
            builtins.Register(new BuiltinDescriptor("http_server", 1, 4, (args, instr) =>
            {
                int port = Convert.ToInt32(args[0], CultureInfo.InvariantCulture);
                ServerOptions options = ParseServerOptions(args.Skip(1), instr);
                return new ServerHandle(port, options);
            }));

            builtins.Register(new BuiltinDescriptor("http_get", 1, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 2 && args[1] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;
                return HttpGetAsync(url, headers, timeout);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("http_post", 2, 5, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                object? body = args[1];
                Dictionary<string, object> headers = args.Count >= 3 && args[2] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 4 ? Convert.ToInt32(args[3], CultureInfo.InvariantCulture) : 100000;
                string contentType = args.Count >= 5 ? args[4]?.ToString() ?? "text/plain" : "text/plain";
                return HttpSendWithBodyAsync(HttpMethod.Post, url, body, headers, timeout, contentType);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("http_put", 2, 5, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                object? body = args[1];
                Dictionary<string, object> headers = args.Count >= 3 && args[2] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 4 ? Convert.ToInt32(args[3], CultureInfo.InvariantCulture) : 100000;
                string contentType = args.Count >= 5 ? args[4]?.ToString() ?? "text/plain" : "text/plain";
                return HttpSendWithBodyAsync(HttpMethod.Put, url, body, headers, timeout, contentType);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("http_delete", 1, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 2 && args[1] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;
                return HttpDeleteAsync(url, headers, timeout);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("http_patch", 2, 5, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                object? body = args[1];
                Dictionary<string, object> headers = args.Count >= 3 && args[2] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 4 ? Convert.ToInt32(args[3], CultureInfo.InvariantCulture) : 100000;
                string contentType = args.Count >= 5 ? args[4]?.ToString() ?? "text/plain" : "text/plain";
                return HttpSendWithBodyAsync(HttpMethod.Patch, url, body, headers, timeout, contentType);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("http_head", 1, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                Dictionary<string, object> headers = args.Count >= 2 && args[1] is Dictionary<string, object> d1 ? d1 : new Dictionary<string, object>();
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;
                return HttpHeadAsync(url, headers, timeout);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("http_download", 2, 3, (args, instr) =>
            {
                string url = args[0]?.ToString() ?? "";
                string path = args[1]?.ToString() ?? "";
                int timeout = args.Count >= 3 ? Convert.ToInt32(args[2], CultureInfo.InvariantCulture) : 100000;

                EnsureFileIo(instr);
                return HttpDownloadAsync(url, path, timeout);
            }, NativeCallOptions.Awaitable));

            builtins.Register(new BuiltinDescriptor("urlencode", 1, 1, (args, instr) =>
            {
                return WebUtility.UrlEncode(args[0]?.ToString() ?? "");
            }));

            builtins.Register(new BuiltinDescriptor("urldecode", 1, 1, (args, instr) =>
            {
                return WebUtility.UrlDecode(args[0]?.ToString() ?? "");
            }));
        }

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

            byte[] body = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            return ToVmResponse(resp, body);
        }

        private static async Task<object?> HttpSendWithBodyAsync(HttpMethod method, string url, object? body, Dictionary<string, object> headers, int timeout, string contentType)
        {
            using CancellationTokenSource cts = new(timeout);
            using HttpRequestMessage req = new(method, url);
            req.Content = CreateBodyContent(body, contentType);

            ApplyHeaders(req, headers);

            using HttpResponseMessage resp = await _client
                .SendAsync(req, HttpCompletionOption.ResponseContentRead, cts.Token)
                .ConfigureAwait(false);

            byte[] respBody = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
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

            byte[] respBody = await resp.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
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

            return ToVmResponse(resp, Array.Empty<byte>());
        }

        private static async Task<object?> HttpDownloadAsync(string url, string path, int timeout)
        {
            using CancellationTokenSource cts = new(timeout);
            using HttpRequestMessage req = new(HttpMethod.Get, url);
            using HttpResponseMessage resp = await _client
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);

            resp.EnsureSuccessStatusCode();

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

            await using Stream input = await resp.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
            await using FileStream output = new(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

            byte[] buffer = new byte[81920];
            long written = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
                if (read <= 0)
                    break;

                await output.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
                written += read;
            }

            return written;
        }

        private static void EnsureFileIo(Instruction instr)
        {
            bool allowFile = VM.AllowFileIO;
            try
            {
                Type? t = Type.GetType("CFGS.IO.CFGS_IO, CFGS.IO");
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
        }

        private static bool HasContentType(Dictionary<string, object> headers)
        {
            foreach (string k in headers.Keys)
                if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static HttpContent CreateBodyContent(object? body, string contentType)
        {
            byte[] payload = ToBodyBytes(body);
            ByteArrayContent content = new(payload);
            string actualContentType = string.IsNullOrWhiteSpace(contentType) ? "text/plain" : contentType;

            try
            {
                MediaTypeHeaderValue parsed = MediaTypeHeaderValue.Parse(actualContentType);
                if (!IsBinaryBody(body) && string.IsNullOrWhiteSpace(parsed.CharSet) && IsTextLikeMediaType(parsed.MediaType))
                    parsed.CharSet = Encoding.UTF8.WebName;
                content.Headers.ContentType = parsed;
            }
            catch
            {
                content.Headers.TryAddWithoutValidation("Content-Type", actualContentType);
            }

            return content;
        }

        private static bool IsBinaryBody(object? body)
        {
            return body is byte[] or List<object>;
        }

        private static bool IsTextLikeMediaType(string? mediaType)
        {
            if (string.IsNullOrWhiteSpace(mediaType))
                return true;

            string mt = mediaType.ToLowerInvariant();
            return mt.StartsWith("text/", StringComparison.Ordinal)
                || mt.Contains("json", StringComparison.Ordinal)
                || mt.Contains("xml", StringComparison.Ordinal)
                || mt.Contains("javascript", StringComparison.Ordinal)
                || mt.Contains("form-urlencoded", StringComparison.Ordinal);
        }

        private static Encoding? TryGetEncoding(string? contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return null;

            try
            {
                MediaTypeHeaderValue parsed = MediaTypeHeaderValue.Parse(contentType);
                if (!string.IsNullOrWhiteSpace(parsed.CharSet))
                    return Encoding.GetEncoding(parsed.CharSet.Trim('"'));
            }
            catch
            {
            }

            return null;
        }

        private static byte[] ToBodyBytes(object? body)
        {
            if (body == null)
                return Array.Empty<byte>();

            if (body is byte[] bytes)
                return bytes;

            if (body is List<object> list)
                return ConvertVmByteArray(list, "body");

            return Encoding.UTF8.GetBytes(body.ToString() ?? "");
        }

        private static byte[] ConvertVmByteArray(List<object> list, string name)
        {
            byte[] result = new byte[list.Count];
            for (int i = 0; i < list.Count; i++)
            {
                int value = Convert.ToInt32(list[i] ?? 0, CultureInfo.InvariantCulture);
                if (value < 0 || value > 255)
                    throw new InvalidOperationException($"{name} byte array item at index {i} is outside 0..255");
                result[i] = (byte)value;
            }

            return result;
        }

        private static List<object> ToVmByteArray(byte[] bytes)
        {
            List<object> result = new(bytes.Length);
            foreach (byte b in bytes)
                result.Add((int)b);
            return result;
        }

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

        private static Dictionary<string, object> ToVmResponse(HttpResponseMessage resp, byte[] bodyBytes)
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

            Encoding encoding = TryGetEncoding(resp.Content.Headers.ContentType?.ToString()) ?? Encoding.UTF8;
            string body = encoding.GetString(bodyBytes);

            return new Dictionary<string, object>
            {
                ["status"] = (int)resp.StatusCode,
                ["reason"] = resp.ReasonPhrase ?? "",
                ["headers"] = h,
                ["body"] = body,
                ["body_bytes"] = ToVmByteArray(bodyBytes),
                ["body_length"] = (long)bodyBytes.LongLength
            };
        }

        private static void RegisterServerIntrinsics(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(ServerHandle);

            intrinsics.Register(T, new IntrinsicDescriptor("start", 0, 0, (recv, a, i) => { ((ServerHandle)recv).Start(); return recv!; }));
            intrinsics.Register(T, new IntrinsicDescriptor("stop", 0, 0, (recv, a, i) => { ((ServerHandle)recv).Stop(); return recv!; }));
            intrinsics.Register(T, new IntrinsicDescriptor("start_async", 0, 0, (recv, a, i) => ((ServerHandle)recv).StartAsync(), NativeCallOptions.Awaitable));
            intrinsics.Register(T, new IntrinsicDescriptor("stop_async", 0, 0, (recv, a, i) => ((ServerHandle)recv).StopAsync(), NativeCallOptions.Awaitable));
            intrinsics.Register(T, new IntrinsicDescriptor("is_running", 0, 0, (recv, a, i) => ((ServerHandle)recv).IsRunning));
            intrinsics.Register(T, new IntrinsicDescriptor("is_https", 0, 0, (recv, a, i) => ((ServerHandle)recv).IsHttps));
            intrinsics.Register(T, new IntrinsicDescriptor("scheme", 0, 0, (recv, a, i) => ((ServerHandle)recv).Scheme));
            intrinsics.Register(T, new IntrinsicDescriptor("url", 0, 0, (recv, a, i) => ((ServerHandle)recv).Url));
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
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("respond", 3, 4, (recv, a, i) =>
            {
                string id = a[0]?.ToString() ?? "";
                int status = Convert.ToInt32(a[1], CultureInfo.InvariantCulture);
                object? body = a[2];
                Dictionary<string, object>? headers = a.Count >= 4 && a[3] is Dictionary<string, object> d ? d : null;
                return ((ServerHandle)recv).Respond(id, status, body, headers);
            }));
            intrinsics.Register(T, new IntrinsicDescriptor("respond_async", 3, 4, (recv, a, i) =>
            {
                string id = a[0]?.ToString() ?? "";
                int status = Convert.ToInt32(a[1], CultureInfo.InvariantCulture);
                object? body = a[2];
                Dictionary<string, object>? headers = a.Count >= 4 && a[3] is Dictionary<string, object> d ? d : null;
                return ((ServerHandle)recv).RespondAsync(id, status, body, headers);
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("close", 0, 0, (recv, a, i) => { ((ServerHandle)recv).Close(); return 1; }));
            intrinsics.Register(T, new IntrinsicDescriptor("close_async", 0, 0, (recv, a, i) => ((ServerHandle)recv).CloseAsync(), NativeCallOptions.Awaitable));
        }

        private static void RegisterRequestBodyIntrinsics(IIntrinsicRegistry intrinsics)
        {
            Type T = typeof(ServerHandle.RequestBodyHandle);

            intrinsics.Register(T, new IntrinsicDescriptor("read_bytes", 1, 1, (recv, a, instr) =>
            {
                int count = Convert.ToInt32(a[0], CultureInfo.InvariantCulture);
                return ((ServerHandle.RequestBodyHandle)recv).ReadBytesVmAsync(count, instr);
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("read_all_bytes", 0, 1, (recv, a, instr) =>
            {
                long? maxBytes = a.Count >= 1 && a[0] != null ? Convert.ToInt64(a[0], CultureInfo.InvariantCulture) : null;
                return ((ServerHandle.RequestBodyHandle)recv).ReadAllBytesVmAsync(maxBytes, instr);
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("read_text", 0, 2, (recv, a, instr) =>
            {
                long? maxBytes = a.Count >= 1 && a[0] != null ? Convert.ToInt64(a[0], CultureInfo.InvariantCulture) : null;
                string? encoding = a.Count >= 2 ? a[1]?.ToString() : null;
                return ((ServerHandle.RequestBodyHandle)recv).ReadTextAsync(maxBytes, encoding, instr);
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("copy_to", 1, 2, (recv, a, instr) =>
            {
                EnsureFileIo(instr);
                string path = a[0]?.ToString() ?? "";
                long? maxBytes = a.Count >= 2 && a[1] != null ? Convert.ToInt64(a[1], CultureInfo.InvariantCulture) : null;
                return ((ServerHandle.RequestBodyHandle)recv).CopyToAsync(path, maxBytes, instr);
            }, NativeCallOptions.Awaitable));

            intrinsics.Register(T, new IntrinsicDescriptor("bytes_read", 0, 0, (recv, a, instr) =>
            {
                return ((ServerHandle.RequestBodyHandle)recv).BytesRead;
            }));

            intrinsics.Register(T, new IntrinsicDescriptor("is_consumed", 0, 0, (recv, a, instr) =>
            {
                return ((ServerHandle.RequestBodyHandle)recv).IsConsumed;
            }));
        }

        internal sealed class ServerOptions
        {
            public long MaxRequestBodySize { get; set; } = DefaultMaxRequestBodySize;
            public bool StreamBodies { get; set; }
            public bool UseHttps { get; set; }
            public string Host { get; set; } = "localhost";
            public string? CertificatePath { get; set; }
            public string? CertificatePassword { get; set; }
            public string Protocols { get; set; } = "http1";
            public HttpProtocols ResolvedProtocols { get; set; } = HttpProtocols.Http1;
        }

        private static ServerOptions ParseServerOptions(IEnumerable<object?> args, Instruction instr)
        {
            ServerOptions options = new();

            foreach (object? arg in args)
            {
                if (arg is null)
                    continue;

                if (arg is Dictionary<string, object> dict)
                {
                    ApplyServerOptions(options, dict, instr);
                    continue;
                }

                if (arg is string or char)
                {
                    string text = arg.ToString() ?? "";
                    if (string.Equals(text, "stream", StringComparison.OrdinalIgnoreCase))
                    {
                        options.StreamBodies = true;
                        continue;
                    }

                    if (string.Equals(text, "https", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(text, "tls", StringComparison.OrdinalIgnoreCase))
                    {
                        options.UseHttps = true;
                        continue;
                    }

                    throw NewVmException(instr, $"Runtime error: unknown http.server option '{text}'");
                }

                options.MaxRequestBodySize = Convert.ToInt64(arg, CultureInfo.InvariantCulture);
            }

            ValidateServerOptions(options, instr);
            return options;
        }

        private static void ApplyServerOptions(ServerOptions options, Dictionary<string, object> values, Instruction instr)
        {
            foreach (KeyValuePair<string, object> kv in values)
            {
                string key = NormalizeOptionKey(kv.Key);
                object? value = kv.Value;

                switch (key)
                {
                    case "https":
                    case "tls":
                    case "ssl":
                    case "usehttps":
                        options.UseHttps = ToBool(value, instr, kv.Key);
                        break;
                    case "cert":
                    case "certificate":
                    case "certificatepath":
                    case "certpath":
                    case "pfx":
                    case "pfxpath":
                        options.CertificatePath = value?.ToString();
                        break;
                    case "certificatepassword":
                    case "certpassword":
                    case "pfxpassword":
                    case "password":
                        options.CertificatePassword = value?.ToString();
                        break;
                    case "host":
                    case "bind":
                    case "address":
                    case "ip":
                        options.Host = value?.ToString() ?? "";
                        break;
                    case "protocol":
                    case "protocols":
                        options.Protocols = value?.ToString() ?? "";
                        break;
                    case "stream":
                    case "streambodies":
                    case "streambody":
                        options.StreamBodies = ToBool(value, instr, kv.Key);
                        break;
                    case "maxrequestbodysize":
                    case "maxbodysize":
                    case "maxbodybytes":
                    case "maxbytes":
                        options.MaxRequestBodySize = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                        break;
                    default:
                        throw NewVmException(instr, $"Runtime error: unknown http.server option '{kv.Key}'");
                }
            }

            ValidateServerOptions(options, instr);
        }

        private static void ValidateServerOptions(ServerOptions options, Instruction instr)
        {
            if (options.MaxRequestBodySize <= 0)
                throw NewVmException(instr, "Runtime error: http.server max request body size must be > 0");

            options.Host = string.IsNullOrWhiteSpace(options.Host) ? "localhost" : options.Host.Trim();
            options.Protocols = string.IsNullOrWhiteSpace(options.Protocols) ? "http1" : NormalizeOptionKey(options.Protocols);

            options.ResolvedProtocols = ResolveProtocols(options.Protocols, instr);

            if (options.UseHttps && string.IsNullOrWhiteSpace(options.CertificatePath))
                throw NewVmException(instr, "Runtime error: http.server HTTPS needs certificatePath / certPath / pfxPath");
        }

        private static string NormalizeOptionKey(string key)
        {
            return key.Trim()
                .Replace("_", "", StringComparison.Ordinal)
                .Replace("-", "", StringComparison.Ordinal)
                .Replace(" ", "", StringComparison.Ordinal)
                .ToLowerInvariant();
        }

        private static bool ToBool(object? value, Instruction instr, string optionName)
        {
            if (value is bool b)
                return b;

            string text = value?.ToString()?.Trim() ?? "";
            if (bool.TryParse(text, out bool parsed))
                return parsed;

            if (text == "1" || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "on", StringComparison.OrdinalIgnoreCase))
                return true;

            if (text == "0" || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "off", StringComparison.OrdinalIgnoreCase))
                return false;

            throw NewVmException(instr, $"Runtime error: http.server option '{optionName}' needs a boolean value");
        }

        private static HttpProtocols ResolveProtocols(string protocols, Instruction instr)
        {
            return protocols switch
            {
                "http1" or "http11" or "http/1.1" => HttpProtocols.Http1,
                "http2" or "h2" or "http/2" => HttpProtocols.Http2,
                "http1andhttp2" or "http1http2" or "http1+http2" or "http/1.1+http/2" => HttpProtocols.Http1AndHttp2,
                _ => throw NewVmException(instr, $"Runtime error: unsupported http.server protocol option '{protocols}'")
            };
        }

        private static VMException NewVmException(Instruction instr, string message)
        {
            return new VMException(message, instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
        }

        public sealed class ServerHandle
        {
            private readonly CancellationTokenSource _cts = new();
            private readonly ConcurrentDictionary<string, PendingRequest> _inflight = new();
            private readonly ConcurrentQueue<string> _queue = new();
            private readonly int _port;
            private readonly long _maxRequestBodySize;
            private readonly bool _streamBodies;
            private readonly bool _useHttps;
            private readonly string _host;
            private readonly string? _certificatePath;
            private readonly string? _certificatePassword;
            private readonly HttpProtocols _protocols;
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

            private sealed class PendingResponse(int status, object? body, Dictionary<string, object>? headers)
            {
                public int Status { get; } = status;
                public object? Body { get; } = body;
                public Dictionary<string, object>? Headers { get; } = headers;
            }

            private sealed class RequestBodyData(string text, byte[] bytes, bool tooLarge, long observedLength, long limit)
            {
                public string Text { get; } = text;
                public byte[] Bytes { get; } = bytes;
                public bool TooLarge { get; } = tooLarge;
                public long ObservedLength { get; } = observedLength;
                public long Limit { get; } = limit;
            }

            public sealed class RequestBodyHandle
            {
                private readonly Stream _stream;
                private readonly CancellationToken _cancellationToken;
                private readonly long _defaultMaxBytes;
                private readonly SemaphoreSlim _gate = new(1, 1);
                private long _bytesRead;
                private bool _consumed;

                internal RequestBodyHandle(Stream stream, long defaultMaxBytes, CancellationToken cancellationToken)
                {
                    _stream = stream;
                    _defaultMaxBytes = defaultMaxBytes;
                    _cancellationToken = cancellationToken;
                }

                public long BytesRead => Interlocked.Read(ref _bytesRead);
                public bool IsConsumed => _consumed;

                public async Task<object?> ReadBytesVmAsync(int count, Instruction instr)
                {
                    if (count < 0)
                        throw NewVmException(instr, "Runtime error: read_bytes(count) needs count >= 0");

                    await _gate.WaitAsync(_cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (count == 0 || _consumed)
                            return CFGS_HTTP.ToVmByteArray(Array.Empty<byte>());

                        byte[] buffer = new byte[count];
                        int read = await _stream.ReadAsync(buffer.AsMemory(0, count), _cancellationToken).ConfigureAwait(false);
                        if (read <= 0)
                        {
                            _consumed = true;
                            return CFGS_HTTP.ToVmByteArray(Array.Empty<byte>());
                        }

                        Interlocked.Add(ref _bytesRead, read);
                        if (read == buffer.Length)
                            return CFGS_HTTP.ToVmByteArray(buffer);

                        byte[] actual = new byte[read];
                        Array.Copy(buffer, actual, read);
                        return CFGS_HTTP.ToVmByteArray(actual);
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }

                public async Task<object?> ReadAllBytesVmAsync(long? maxBytes, Instruction instr)
                {
                    byte[] bytes = await ReadAllBytesInternalAsync(ResolveMaxBytes(maxBytes, instr), instr).ConfigureAwait(false);
                    return CFGS_HTTP.ToVmByteArray(bytes);
                }

                public async Task<object?> ReadTextAsync(long? maxBytes, string? encodingName, Instruction instr)
                {
                    byte[] bytes = await ReadAllBytesInternalAsync(ResolveMaxBytes(maxBytes, instr), instr).ConfigureAwait(false);
                    Encoding encoding = ResolveEncoding(encodingName, instr);
                    return encoding.GetString(bytes);
                }

                public async Task<object?> CopyToAsync(string path, long? maxBytes, Instruction instr)
                {
                    if (string.IsNullOrWhiteSpace(path))
                        throw NewVmException(instr, "Runtime error: copy_to(path) needs a non-empty path");

                    long limit = ResolveMaxBytes(maxBytes, instr);

                    await _gate.WaitAsync(_cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (_consumed)
                            return 0L;

                        string fullPath = Path.GetFullPath(path);
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? ".");

                        long written = 0;
                        byte[] buffer = new byte[81920];
                        await using FileStream output = new(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);

                        while (true)
                        {
                            int read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cancellationToken).ConfigureAwait(false);
                            if (read <= 0)
                            {
                                _consumed = true;
                                return written;
                            }

                            if (written + read > limit)
                            {
                                long observed = written + read;
                                Interlocked.Add(ref _bytesRead, read);
                                throw NewVmException(instr, $"Runtime error: request body exceeds stream limit ({observed} > {limit} bytes)");
                            }

                            await output.WriteAsync(buffer.AsMemory(0, read), _cancellationToken).ConfigureAwait(false);
                            written += read;
                            Interlocked.Add(ref _bytesRead, read);
                        }
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }

                private async Task<byte[]> ReadAllBytesInternalAsync(long limit, Instruction instr)
                {
                    await _gate.WaitAsync(_cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (_consumed)
                            return Array.Empty<byte>();

                        using MemoryStream ms = new();
                        byte[] buffer = new byte[81920];

                        while (true)
                        {
                            int read = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), _cancellationToken).ConfigureAwait(false);
                            if (read <= 0)
                            {
                                _consumed = true;
                                return ms.ToArray();
                            }

                            if (ms.Length + read > limit)
                            {
                                long observed = ms.Length + read;
                                Interlocked.Add(ref _bytesRead, read);
                                throw NewVmException(instr, $"Runtime error: request body exceeds stream limit ({observed} > {limit} bytes)");
                            }

                            ms.Write(buffer, 0, read);
                            Interlocked.Add(ref _bytesRead, read);
                        }
                    }
                    finally
                    {
                        _gate.Release();
                    }
                }

                private long ResolveMaxBytes(long? maxBytes, Instruction instr)
                {
                    long limit = maxBytes ?? _defaultMaxBytes;
                    if (limit <= 0)
                        throw NewVmException(instr, "Runtime error: stream byte limit must be > 0");
                    return limit;
                }

                private static Encoding ResolveEncoding(string? encodingName, Instruction instr)
                {
                    string actualName = string.IsNullOrWhiteSpace(encodingName) ? Encoding.UTF8.WebName : encodingName.Trim();
                    try
                    {
                        return Encoding.GetEncoding(actualName);
                    }
                    catch (Exception ex)
                    {
                        throw NewVmException(instr, $"Runtime error: encoding '{actualName}' is not supported ({ex.GetType().Name}: {ex.Message})");
                    }
                }

                private static VMException NewVmException(Instruction instr, string message)
                {
                    return new VMException(message, instr.Line, instr.Col, instr.OriginFile, VM.IsDebugging, VM.DebugStream!);
                }
            }

            internal ServerHandle(int port, ServerOptions options)
            {
                if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
                if (options.MaxRequestBodySize <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxRequestBodySize));
                _port = port;
                _maxRequestBodySize = options.MaxRequestBodySize;
                _streamBodies = options.StreamBodies;
                _useHttps = options.UseHttps;
                _host = options.Host;
                _certificatePath = options.CertificatePath;
                _certificatePassword = options.CertificatePassword;
                _protocols = options.ResolvedProtocols;
            }

            public bool IsRunning => _running;
            public int PendingCount => _queue.Count;
            public bool IsHttps => _useHttps;
            public string Scheme => _useHttps ? "https" : "http";
            public string Url => $"{Scheme}://{DisplayHost(_host)}:{_port}/";

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
                        $"Cannot start HTTP server on {Url}: {FlattenMessage(ex)}",
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

            public int Respond(string id, int status, object? body, Dictionary<string, object>? headers)
            {
                object? response = RespondAsync(id, status, body, headers).GetAwaiter().GetResult();
                return Convert.ToInt32(response ?? 0, CultureInfo.InvariantCulture);
            }

            public async Task<object?> RespondAsync(string id, int status, object? body, Dictionary<string, object>? headers)
            {
                if (!_inflight.TryGetValue(id, out PendingRequest? pending))
                    return 0;

                return pending.ResponseSource.TrySetResult(new PendingResponse(status, body, headers)) ? 1 : 0;
            }

            private Dictionary<string, object>? TryBuildRequestDict(string id)
            {
                return _inflight.TryGetValue(id, out PendingRequest? pending) ? pending.Request : null;
            }

            private void ConfigureEndpoint(KestrelServerOptions options)
            {
                void ConfigureListen(ListenOptions listenOptions)
                {
                    listenOptions.Protocols = _protocols;
                    if (_useHttps)
                        listenOptions.UseHttps(LoadCertificate());
                }

                if (IsAnyHost(_host))
                {
                    options.ListenAnyIP(_port, ConfigureListen);
                    return;
                }

                if (IsLocalhost(_host))
                {
                    options.ListenLocalhost(_port, ConfigureListen);
                    return;
                }

                if (IPAddress.TryParse(_host, out IPAddress? address))
                {
                    options.Listen(address, _port, ConfigureListen);
                    return;
                }

                throw new InvalidOperationException($"unsupported server host '{_host}'. Use localhost, *, 0.0.0.0, ::, or a concrete IP address.");
            }

            private X509Certificate2 LoadCertificate()
            {
                string path = Path.GetFullPath(_certificatePath ?? "");
                if (!File.Exists(path))
                    throw new FileNotFoundException("HTTPS certificate file was not found", path);

                X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(path, _certificatePassword, X509KeyStorageFlags.DefaultKeySet);
                if (!certificate.HasPrivateKey)
                    throw new InvalidOperationException("HTTPS certificate needs a private key");

                return certificate;
            }

            private static bool IsAnyHost(string host)
            {
                return host == "*" ||
                       host == "+" ||
                       host == "0.0.0.0" ||
                       host == "::" ||
                       string.Equals(host, "any", StringComparison.OrdinalIgnoreCase);
            }

            private static bool IsLocalhost(string host)
            {
                return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);
            }

            private static string DisplayHost(string host)
            {
                string display = IsAnyHost(host) ? "0.0.0.0" : host;
                return display.Contains(":", StringComparison.Ordinal) && !display.StartsWith("[", StringComparison.Ordinal)
                    ? $"[{display}]"
                    : display;
            }

            private WebApplication BuildApplication()
            {
                WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
                builder.WebHost.UseKestrelCore();
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.AddServerHeader = false;
                    ConfigureEndpoint(options);
                });

                WebApplication app = builder.Build();
                app.Run(ctx => HandleIncomingRequestAsync(ctx));
                return app;
            }

            private async Task HandleIncomingRequestAsync(HttpContext ctx)
            {
                string id = Guid.NewGuid().ToString("N");
                Dictionary<string, object> request = _streamBodies
                    ? BuildStreamingRequestDict(id, ctx.Request, ctx.Connection, _maxRequestBodySize, _cts.Token)
                    : await BuildRequestDictAsync(id, ctx.Request, ctx.Connection, _maxRequestBodySize, _cts.Token).ConfigureAwait(false);
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

                    if (noBody)
                    {
                        resp.ContentLength = 0;
                    }
                    else if (TryGetFileBodyPath(response.Body, out string filePath))
                    {
                        await WriteFileBodyAsync(resp, filePath).ConfigureAwait(false);
                    }
                    else
                    {
                        byte[] payload = ToResponseBytes(response.Body);
                        resp.ContentLength = payload.LongLength;

                        if (payload.Length > 0)
                            await resp.Body.WriteAsync(payload, _cts.Token).ConfigureAwait(false);
                    }
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

            private static byte[] ToResponseBytes(object? body)
            {
                if (body == null)
                    return Array.Empty<byte>();

                if (body is byte[] bytes)
                    return bytes;

                if (body is List<object> list)
                {
                    byte[] result = new byte[list.Count];
                    for (int i = 0; i < list.Count; i++)
                    {
                        object? item = list[i];
                        int value = Convert.ToInt32(item ?? 0, CultureInfo.InvariantCulture);
                        if (value < 0 || value > 255)
                            throw new InvalidOperationException($"response byte array item at index {i} is outside 0..255");
                        result[i] = (byte)value;
                    }
                    return result;
                }

                return Encoding.UTF8.GetBytes(body.ToString() ?? "");
            }

            private static bool TryGetFileBodyPath(object? body, out string path)
            {
                path = string.Empty;
                if (body is not Dictionary<string, object> dict)
                    return false;

                if (!dict.TryGetValue("__cfgs_http_body", out object? kind) || !string.Equals(kind?.ToString(), "file", StringComparison.Ordinal))
                    return false;

                if (!dict.TryGetValue("path", out object? value))
                    return false;

                path = value?.ToString() ?? string.Empty;
                return path.Length > 0;
            }

            private async Task WriteFileBodyAsync(HttpResponse resp, string path)
            {
                if (!File.Exists(path))
                {
                    byte[] missing = Encoding.UTF8.GetBytes("Not Found");
                    resp.StatusCode = StatusCodes.Status404NotFound;
                    resp.ContentType = "text/plain; charset=utf-8";
                    resp.ContentLength = missing.LongLength;
                    await resp.Body.WriteAsync(missing, _cts.Token).ConfigureAwait(false);
                    return;
                }

                await using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
                resp.ContentLength = file.Length;
                await file.CopyToAsync(resp.Body, 81920, _cts.Token).ConfigureAwait(false);
            }

            private static async Task<Dictionary<string, object>> BuildRequestDictAsync(
                string id,
                HttpRequest req,
                ConnectionInfo connection,
                long maxBodySize,
                CancellationToken cancellationToken)
            {
                RequestBodyData body = await ReadRequestBodyDataAsync(req, maxBodySize, cancellationToken).ConfigureAwait(false);
                Dictionary<string, object> headers = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in req.Headers)
                    headers[header.Key] = string.Join(", ", header.Value.ToArray());

                return new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["method"] = req.Method ?? "GET",
                    ["scheme"] = req.Scheme ?? "http",
                    ["is_https"] = req.IsHttps,
                    ["host"] = req.Host.HasValue ? req.Host.Value : "",
                    ["path"] = req.Path.HasValue ? req.Path.Value! : "/",
                    ["query"] = ParseQuery(req.QueryString.HasValue ? req.QueryString.Value : ""),
                    ["headers"] = headers,
                    ["body"] = body.Text,
                    ["body_bytes"] = CFGS_HTTP.ToVmByteArray(body.Bytes),
                    ["body_length"] = body.ObservedLength,
                    ["body_too_large"] = body.TooLarge,
                    ["body_bytes_truncated"] = body.TooLarge,
                    ["body_limit"] = body.Limit,
                    ["local"] = BuildLocal(connection),
                    ["remote"] = BuildRemote(connection)
                };
            }

            private static Dictionary<string, object> BuildStreamingRequestDict(
                string id,
                HttpRequest req,
                ConnectionInfo connection,
                long maxBodySize,
                CancellationToken cancellationToken)
            {
                Dictionary<string, object> headers = new(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues> header in req.Headers)
                    headers[header.Key] = string.Join(", ", header.Value.ToArray());

                long declaredLength = req.ContentLength ?? -1;

                return new Dictionary<string, object>
                {
                    ["id"] = id,
                    ["method"] = req.Method ?? "GET",
                    ["scheme"] = req.Scheme ?? "http",
                    ["is_https"] = req.IsHttps,
                    ["host"] = req.Host.HasValue ? req.Host.Value : "",
                    ["path"] = req.Path.HasValue ? req.Path.Value! : "/",
                    ["query"] = ParseQuery(req.QueryString.HasValue ? req.QueryString.Value : ""),
                    ["headers"] = headers,
                    ["body"] = "",
                    ["body_bytes"] = CFGS_HTTP.ToVmByteArray(Array.Empty<byte>()),
                    ["body_length"] = declaredLength >= 0 ? declaredLength : 0L,
                    ["body_too_large"] = false,
                    ["body_bytes_truncated"] = false,
                    ["body_limit"] = maxBodySize,
                    ["body_stream"] = new RequestBodyHandle(req.Body, maxBodySize, cancellationToken),
                    ["body_streaming"] = true,
                    ["local"] = BuildLocal(connection),
                    ["remote"] = BuildRemote(connection)
                };
            }

            private static async Task<RequestBodyData> ReadRequestBodyDataAsync(HttpRequest req, long maxBodySize, CancellationToken cancellationToken)
            {
                long declaredLength = req.ContentLength ?? -1;
                if (declaredLength > maxBodySize)
                {
                    await DrainAsync(req.Body, cancellationToken).ConfigureAwait(false);
                    return new RequestBodyData(
                        $"[body too large: {declaredLength} bytes, limit {maxBodySize}]",
                        Array.Empty<byte>(),
                        true,
                        declaredLength,
                        maxBodySize);
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
                        return new RequestBodyData(
                            $"[body too large: {observed} bytes, limit {maxBodySize}]",
                            Array.Empty<byte>(),
                            true,
                            observed,
                            maxBodySize);
                    }

                    ms.Write(buffer, 0, read);
                }

                byte[] bytes = ms.ToArray();
                return new RequestBodyData(encoding.GetString(bytes), bytes, false, bytes.LongLength, maxBodySize);
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
                        return Encoding.GetEncoding(parsed.CharSet.Trim('"'));
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

            private static string BuildLocal(ConnectionInfo connection)
            {
                if (connection.LocalIpAddress == null)
                    return "";
                if (connection.LocalPort > 0)
                    return connection.LocalIpAddress + ":" + connection.LocalPort.ToString(CultureInfo.InvariantCulture);
                return connection.LocalIpAddress.ToString();
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

