using System.Net.Http;

namespace CFGS_VM.Analytic.Modules
{
    internal sealed class SourceResolver
    {
        private static readonly HttpClient Http = CreateImportHttpClient();
        private static readonly int HttpImportMaxBytes = ParsePositiveEnvInt("CFGS_IMPORT_HTTP_MAX_BYTES", 4 * 1024 * 1024);
        private static readonly int HttpImportMaxRedirects = ParsePositiveEnvInt("CFGS_IMPORT_HTTP_MAX_REDIRECTS", 5);
        private readonly string? _workingDirectory;

        public SourceResolver(string? workingDirectory = null)
        {
            _workingDirectory = NormalizeWorkingDirectory(workingDirectory);
        }

        public bool IsHttpUrl(string? source, out Uri uri)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out uri!) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                return true;
            }

            uri = default!;
            return false;
        }

        public byte[] DownloadHttpImportBytes(Uri uri)
        {
            Uri current = uri;
            for (int redirect = 0; redirect <= HttpImportMaxRedirects; redirect++)
            {
                ValidateRemoteImportUri(current);

                using HttpRequestMessage request = new(HttpMethod.Get, current);
                using HttpResponseMessage response = Http.Send(request, HttpCompletionOption.ResponseHeadersRead);

                if (IsRedirect(response))
                {
                    if (redirect == HttpImportMaxRedirects)
                        throw new IOException($"import redirect limit exceeded ({HttpImportMaxRedirects})");

                    Uri? location = response.Headers.Location;
                    if (location is null)
                        throw new IOException($"HTTP {(int)response.StatusCode} redirect without Location header");

                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    throw new IOException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value > HttpImportMaxBytes)
                {
                    throw new IOException($"import exceeds size limit ({contentLength.Value} > {HttpImportMaxBytes} bytes)");
                }

                using Stream stream = response.Content.ReadAsStream();
                return ReadAllBytesWithLimit(stream, HttpImportMaxBytes);
            }

            throw new IOException($"import redirect limit exceeded ({HttpImportMaxRedirects})");
        }

        public string? ResolveImportPath(string rawPath, string sourceFileName)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return null;

            if (Path.IsPathRooted(rawPath))
            {
                string fullAbsolutePath = Path.GetFullPath(rawPath);
                return File.Exists(fullAbsolutePath) ? fullAbsolutePath : null;
            }

            foreach (string baseDirectory in GetSearchBases(sourceFileName))
            {
                try
                {
                    string candidate = Path.GetFullPath(Path.Combine(baseDirectory, rawPath));
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException) { }
                catch (IOException) { }
            }

            try
            {
                string fileNameOnly = Path.GetFileName(rawPath);
                if (!string.IsNullOrWhiteSpace(fileNameOnly))
                {
                    string candidate = Path.GetFullPath(Path.Combine(GetExeBaseDir(), fileNameOnly));
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
            catch (ArgumentException) { }
            catch (IOException) { }

            return null;
        }

        private static int ParsePositiveEnvInt(string name, int fallback)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            if (int.TryParse(raw, out int parsed) && parsed > 0)
                return parsed;
            return fallback;
        }

        private static bool IsEnabledEnv(string name)
        {
            string? raw = Environment.GetEnvironmentVariable(name);
            return string.Equals(raw, "1", StringComparison.Ordinal) ||
                   string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateRemoteImportUri(Uri uri)
        {
            if (uri.Scheme == Uri.UriSchemeHttps)
                return;

            if (uri.Scheme == Uri.UriSchemeHttp && IsEnabledEnv("CFGS_IMPORT_ALLOW_INSECURE_HTTP"))
                return;

            throw new IOException(
                uri.Scheme == Uri.UriSchemeHttp
                    ? "insecure http imports are disabled; use https or set CFGS_IMPORT_ALLOW_INSECURE_HTTP=1"
                    : $"unsupported import URI scheme '{uri.Scheme}'");
        }

        private static bool IsRedirect(HttpResponseMessage response)
        {
            int status = (int)response.StatusCode;
            return status is >= 300 and <= 399;
        }

        private static HttpClient CreateImportHttpClient()
        {
            HttpClient client = new(new HttpClientHandler
            {
                AllowAutoRedirect = false
            });

            client.Timeout = TimeSpan.FromMilliseconds(ParsePositiveEnvInt("CFGS_IMPORT_HTTP_TIMEOUT_MS", 15000));
            return client;
        }

        private static byte[] ReadAllBytesWithLimit(Stream stream, int maxBytes)
        {
            byte[] buffer = new byte[8192];
            int total = 0;
            using MemoryStream memory = new();

            while (true)
            {
                int read = stream.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                    break;

                total += read;
                if (total > maxBytes)
                    throw new IOException($"import exceeds size limit ({total} > {maxBytes} bytes)");

                memory.Write(buffer, 0, read);
            }

            return memory.ToArray();
        }

        private string GetExeBaseDir()
        {
            try
            {
                return AppContext.BaseDirectory;
            }
            catch
            {
                return _workingDirectory ?? Directory.GetCurrentDirectory();
            }
        }

        private IEnumerable<string> GetSearchBases(string sourceFileName)
        {
            if (!string.IsNullOrWhiteSpace(sourceFileName))
            {
                string? sourceDirectory = null;
                try
                {
                    sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourceFileName));
                }
                catch (ArgumentException) { }
                catch (IOException) { }

                if (!string.IsNullOrWhiteSpace(sourceDirectory))
                    yield return sourceDirectory!;
            }

            if (!string.IsNullOrWhiteSpace(_workingDirectory))
                yield return _workingDirectory!;
            else
                yield return Directory.GetCurrentDirectory();

            yield return GetExeBaseDir();
        }

        private static string? NormalizeWorkingDirectory(string? workingDirectory)
        {
            if (string.IsNullOrWhiteSpace(workingDirectory))
                return null;

            try
            {
                string fullPath = Path.GetFullPath(workingDirectory);
                return Directory.Exists(fullPath) ? fullPath : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }
    }
}
