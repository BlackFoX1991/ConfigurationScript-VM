using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CFGS_VM.Packaging
{
    internal static class CfgsPackageManager
    {
        public const string ManifestFileName = "cfgs.json";
        public const string LockFileName = "cfgs.lock.json";
        public const string PackageDirectoryName = ".cfgs";
        public const string PluginDirectoryName = "plugins";
        private const int CurrentLockfileVersion = 2;
        private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static string ResolveProjectRoot(string? projectDirectory = null)
        {
            string start = string.IsNullOrWhiteSpace(projectDirectory)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(projectDirectory));

            if (File.Exists(start))
                start = Path.GetDirectoryName(start) ?? Directory.GetCurrentDirectory();

            DirectoryInfo? current = new(start);
            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, ManifestFileName)))
                    return current.FullName;

                current = current.Parent;
            }

            return Path.GetFullPath(start);
        }

        public static CfgsManifest Init(string? projectDirectory = null)
        {
            string root = ResolveProjectRoot(projectDirectory);
            Directory.CreateDirectory(root);

            string manifestPath = Path.Combine(root, ManifestFileName);
            if (File.Exists(manifestPath))
                return ReadManifest(root);

            CfgsManifest manifest = new()
            {
                Name = new DirectoryInfo(root).Name,
                Version = "0.1.0",
                Plugins = []
            };

            WriteManifest(root, manifest);
            return manifest;
        }

        public static CfgsPluginDependency InstallPlugin(
            string pluginPath,
            string? projectDirectory = null,
            string? name = null,
            string? version = null,
            bool includeDependencies = true,
            bool restore = true)
        {
            if (string.IsNullOrWhiteSpace(pluginPath))
                throw new InvalidDataException("missing plugin path");

            string root = ResolveProjectRoot(projectDirectory);
            if (!File.Exists(Path.Combine(root, ManifestFileName)))
                Init(root);

            CfgsManifest manifest = ReadManifest(root);
            string resolvedPluginPath = ResolvePluginSource(root, pluginPath);
            string pluginName = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(resolvedPluginPath)
                : name.Trim();

            CfgsPluginDependency dependency = new()
            {
                Name = pluginName,
                Version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim(),
                Path = ToPortableRelativePath(root, resolvedPluginPath),
                IncludeDependencies = includeDependencies
            };

            manifest.Plugins ??= [];
            int existingIndex = manifest.Plugins.FindIndex(p =>
                string.Equals(p.Name, dependency.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(p.Path), Path.GetFileName(dependency.Path), StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
                manifest.Plugins[existingIndex] = dependency;
            else
                manifest.Plugins.Add(dependency);

            WriteManifest(root, manifest);

            if (restore)
                Restore(root);

            return dependency;
        }

        public static CfgsLockFile Restore(string? projectDirectory = null)
        {
            string root = ResolveProjectRoot(projectDirectory);
            CfgsManifest manifest = ReadManifest(root);
            List<CfgsPluginDependency> plugins = NormalizePlugins(manifest);

            string packageDirectory = Path.Combine(root, PackageDirectoryName);
            string pluginInstallDirectory = Path.Combine(packageDirectory, PluginDirectoryName);
            if (Directory.Exists(packageDirectory) && new DirectoryInfo(packageDirectory).LinkTarget is not null)
                throw new InvalidDataException("Refusing to restore into a linked package directory.");
            Directory.CreateDirectory(packageDirectory);
            string stagedDirectory = Path.Combine(packageDirectory, "plugins.stage-" + Guid.NewGuid().ToString("N"));
            string backupDirectory = Path.Combine(packageDirectory, "plugins.backup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagedDirectory);

            List<CfgsLockedPlugin> lockedPlugins = new();
            try
            {
                foreach (CfgsPluginDependency plugin in plugins)
                {
                    string sourcePath = ResolvePluginSource(root, plugin.Path);
                    string installedPath = Path.Combine(pluginInstallDirectory, Path.GetFileName(sourcePath));
                    CopyPluginArtifacts(sourcePath, stagedDirectory, plugin.IncludeDependencies);

                    lockedPlugins.Add(new CfgsLockedPlugin
                    {
                        Name = string.IsNullOrWhiteSpace(plugin.Name) ? Path.GetFileNameWithoutExtension(sourcePath) : plugin.Name,
                        Version = string.IsNullOrWhiteSpace(plugin.Version) ? "0.0.0" : plugin.Version,
                        Type = "plugin",
                        Source = ToPortableRelativePath(root, sourcePath),
                        InstalledPath = ToPortableRelativePath(root, installedPath),
                        Sha256 = ComputeSha256(sourcePath),
                        IncludeDependencies = plugin.IncludeDependencies
                    });
                }

                foreach (CfgsLockedPlugin plugin in lockedPlugins)
                {
                    string stagedPlugin = Path.Combine(stagedDirectory, Path.GetFileName(plugin.InstalledPath));
                    if (!PathComparer.Equals(ComputeSha256(stagedPlugin), plugin.Sha256))
                        throw new InvalidDataException($"plugin artifact collision for '{plugin.InstalledPath}'");
                }

                CfgsLockFile lockFile = new()
                {
                    LockfileVersion = CurrentLockfileVersion,
                    GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    Plugins = lockedPlugins,
                    Artifacts = Directory.EnumerateFiles(stagedDirectory, "*", SearchOption.AllDirectories)
                        .Select(file => new CfgsLockedArtifact
                        {
                            Path = ToPortableRelativePath(root, Path.Combine(pluginInstallDirectory, Path.GetRelativePath(stagedDirectory, file))),
                            Sha256 = ComputeSha256(file)
                        })
                        .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
                        .ToList()
                };

                bool hadPreviousInstall = Directory.Exists(pluginInstallDirectory);
                if (hadPreviousInstall && new DirectoryInfo(pluginInstallDirectory).LinkTarget is not null)
                    throw new InvalidDataException("Refusing to replace a linked plugin directory.");
                if (hadPreviousInstall)
                    EnsureNoLinkedDirectories(pluginInstallDirectory);
                if (hadPreviousInstall)
                    Directory.Move(pluginInstallDirectory, backupDirectory);
                try
                {
                    Directory.Move(stagedDirectory, pluginInstallDirectory);
                    WriteLockFile(root, lockFile);
                }
                catch
                {
                    if (Directory.Exists(pluginInstallDirectory))
                        DeleteTemporaryPackageDirectory(pluginInstallDirectory, packageDirectory);
                    if (hadPreviousInstall && Directory.Exists(backupDirectory))
                        Directory.Move(backupDirectory, pluginInstallDirectory);
                    throw;
                }

                if (hadPreviousInstall)
                    DeleteTemporaryPackageDirectory(backupDirectory, packageDirectory);
                return lockFile;
            }
            finally
            {
                if (Directory.Exists(stagedDirectory))
                    DeleteTemporaryPackageDirectory(stagedDirectory, packageDirectory);
            }
        }

        public static CfgsManifest ReadManifest(string projectDirectory)
        {
            string manifestPath = Path.Combine(projectDirectory, ManifestFileName);
            if (!File.Exists(manifestPath))
                throw new FileNotFoundException($"cfgs manifest not found: '{manifestPath}'", manifestPath);

            using FileStream stream = File.OpenRead(manifestPath);
            CfgsManifest? manifest = JsonSerializer.Deserialize<CfgsManifest>(stream, JsonOptions);
            if (manifest is null)
                throw new InvalidDataException($"invalid cfgs manifest: '{manifestPath}'");

            manifest.Plugins ??= [];
            return manifest;
        }

        public static CfgsLockFile? TryReadLockFile(string projectDirectory)
        {
            string lockPath = Path.Combine(projectDirectory, LockFileName);
            if (!File.Exists(lockPath))
                return null;

            using FileStream stream = File.OpenRead(lockPath);
            return JsonSerializer.Deserialize<CfgsLockFile>(stream, JsonOptions);
        }

        public static void VerifyLockedPluginArtifact(string pluginPath)
        {
            string fullPath = Path.GetFullPath(pluginPath);
            if (!TryGetPackageRootForPlugin(fullPath, out string? root))
                return;

            CfgsLockFile? lockFile = TryReadLockFile(root);
            if (lockFile is null || lockFile.LockfileVersion != CurrentLockfileVersion)
                throw new InvalidDataException("plugin lockfile is missing or outdated. Run restore before loading package plugins.");

            string pluginDirectory = Path.GetFullPath(Path.Combine(root, PackageDirectoryName, PluginDirectoryName));
            string packageDirectory = Path.GetDirectoryName(pluginDirectory)!;
            if (lockFile.Artifacts is null || new DirectoryInfo(packageDirectory).LinkTarget is not null ||
                new DirectoryInfo(pluginDirectory).LinkTarget is not null)
                throw new InvalidDataException("plugin directory or lockfile is invalid. Run restore again.");
            EnsureNoLinkedDirectories(pluginDirectory);
            Dictionary<string, string> expected = new(PathComparer);
            foreach (CfgsLockedArtifact artifact in lockFile.Artifacts)
            {
                if (artifact is null || string.IsNullOrWhiteSpace(artifact.Path) || string.IsNullOrWhiteSpace(artifact.Sha256))
                    throw new InvalidDataException("plugin lockfile contains an incomplete artifact. Run restore again.");
                string artifactPath = Path.GetFullPath(Path.Combine(root, artifact.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsWithinDirectory(artifactPath, pluginDirectory) || !expected.TryAdd(artifactPath, artifact.Sha256))
                    throw new InvalidDataException("plugin lockfile contains an invalid or duplicate artifact path. Run restore again.");
            }

            if (!expected.ContainsKey(fullPath))
                throw new InvalidDataException($"plugin artifact '{fullPath}' is not in the lockfile. Run restore again.");

            foreach (string file in Directory.EnumerateFiles(pluginDirectory, "*", SearchOption.AllDirectories))
            {
                string actualPath = Path.GetFullPath(file);
                if (!expected.TryGetValue(actualPath, out string? expectedHash))
                    throw new InvalidDataException($"plugin artifact '{actualPath}' is not in the lockfile. Run restore again.");
                if (new FileInfo(actualPath).LinkTarget is not null)
                    throw new InvalidDataException($"plugin artifact '{actualPath}' is a symbolic link. Run restore again.");
                string actualHash = ComputeSha256(actualPath);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"plugin lockfile hash mismatch for '{actualPath}'. Run restore again.");
                expected.Remove(actualPath);
            }

            if (expected.Count != 0)
                throw new InvalidDataException("plugin lockfile lists missing artifacts. Run restore again.");
        }

        internal static string? GetManagedPluginDirectory(string path)
            => TryGetPackageRootForPlugin(Path.GetFullPath(path), out string root)
                ? Path.GetFullPath(Path.Combine(root, PackageDirectoryName, PluginDirectoryName))
                : null;

        private static bool IsWithinDirectory(string path, string directory)
            => path.StartsWith(directory + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

        private static void EnsureNoLinkedDirectories(string root)
        {
            Stack<string> pending = new();
            pending.Push(root);
            while (pending.TryPop(out string? parent))
            {
                foreach (string directory in Directory.EnumerateDirectories(parent, "*", SearchOption.TopDirectoryOnly))
                {
                    if (new DirectoryInfo(directory).LinkTarget is not null)
                        throw new InvalidDataException($"plugin directory '{directory}' is a symbolic link. Run restore again.");
                    pending.Push(directory);
                }
            }
        }

        private static void DeleteTemporaryPackageDirectory(string path, string packageDirectory)
        {
            string fullPath = Path.GetFullPath(path);
            if (!PathComparer.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(packageDirectory)))
                throw new InvalidOperationException("Refusing to delete a directory outside the package directory.");
            if (new DirectoryInfo(fullPath).LinkTarget is not null)
                throw new InvalidDataException("Refusing to delete a linked package directory.");
            EnsureNoLinkedDirectories(fullPath);
            Directory.Delete(fullPath, recursive: true);
        }

        private static void WriteManifest(string root, CfgsManifest manifest)
        {
            string manifestPath = Path.Combine(root, ManifestFileName);
            using FileStream stream = File.Create(manifestPath);
            JsonSerializer.Serialize(stream, manifest, JsonOptions);
        }

        private static bool TryGetPackageRootForPlugin(string pluginPath, out string root)
        {
            DirectoryInfo? directory = new FileInfo(pluginPath).Directory;
            while (directory is not null)
            {
                if (string.Equals(directory.Name, PluginDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                    directory.Parent is DirectoryInfo packageDirectory &&
                    string.Equals(packageDirectory.Name, PackageDirectoryName, StringComparison.OrdinalIgnoreCase) &&
                    packageDirectory.Parent is DirectoryInfo projectRoot)
                {
                    root = projectRoot.FullName;
                    return true;
                }

                directory = directory.Parent;
            }

            root = string.Empty;
            return false;
        }

        private static void WriteLockFile(string root, CfgsLockFile lockFile)
        {
            string lockPath = Path.Combine(root, LockFileName);
            string temporaryPath = lockPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = File.Create(temporaryPath))
                    JsonSerializer.Serialize(stream, lockFile, JsonOptions);
                File.Move(temporaryPath, lockPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        private static List<CfgsPluginDependency> NormalizePlugins(CfgsManifest manifest)
        {
            List<CfgsPluginDependency> plugins = [];
            if (manifest.Plugins is not null)
                plugins.AddRange(manifest.Plugins.Where(static p => !string.IsNullOrWhiteSpace(p.Path)));

            if (manifest.Dependencies is not null)
            {
                foreach ((string name, CfgsDependency dependency) in manifest.Dependencies)
                {
                    if (!string.Equals(dependency.Type ?? "plugin", "plugin", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (string.IsNullOrWhiteSpace(dependency.Path))
                        continue;

                    plugins.Add(new CfgsPluginDependency
                    {
                        Name = string.IsNullOrWhiteSpace(dependency.Name) ? name : dependency.Name,
                        Version = dependency.Version,
                        Path = dependency.Path,
                        IncludeDependencies = dependency.IncludeDependencies
                    });
                }
            }

            return plugins;
        }

        private static string ResolvePluginSource(string root, string pluginPath)
        {
            string expanded = Environment.ExpandEnvironmentVariables(pluginPath.Trim());
            string candidate = Path.IsPathFullyQualified(expanded)
                ? expanded
                : Path.Combine(root, expanded);

            string fullPath = Path.GetFullPath(candidate);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"plugin dll not found: '{pluginPath}'", fullPath);

            if (!string.Equals(Path.GetExtension(fullPath), ".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"plugin dependency must point to a .dll file: '{pluginPath}'");

            return fullPath;
        }

        private static void CopyPluginArtifacts(string sourcePath, string targetDirectory, bool includeDependencies)
        {
            string sourceDirectory = Path.GetDirectoryName(sourcePath) ?? Directory.GetCurrentDirectory();

            if (!includeDependencies)
            {
                File.Copy(sourcePath, Path.Combine(targetDirectory, Path.GetFileName(sourcePath)), overwrite: true);
                return;
            }

            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(file);
                if (!IsPluginArtifactExtension(extension))
                    continue;

                File.Copy(file, Path.Combine(targetDirectory, Path.GetFileName(file)), overwrite: true);
            }

            string runtimes = Path.Combine(sourceDirectory, "runtimes");
            if (Directory.Exists(runtimes))
                CopyDirectory(runtimes, Path.Combine(targetDirectory, "runtimes"));
        }

        private static bool IsPluginArtifactExtension(string extension)
            => string.Equals(extension, ".dll", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".pdb", StringComparison.OrdinalIgnoreCase);

        private static void CopyDirectory(string sourceDirectory, string targetDirectory)
        {
            Directory.CreateDirectory(targetDirectory);
            foreach (string directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, directory);
                Directory.CreateDirectory(Path.Combine(targetDirectory, relative));
            }

            foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(sourceDirectory, file);
                File.Copy(file, Path.Combine(targetDirectory, relative), overwrite: true);
            }
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            byte[] hash = SHA256.HashData(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private static string ToPortableRelativePath(string root, string path)
        {
            string relative = Path.GetRelativePath(root, path);
            return relative.Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    internal sealed class CfgsManifest
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("plugins")]
        public List<CfgsPluginDependency>? Plugins { get; set; }

        [JsonPropertyName("dependencies")]
        public Dictionary<string, CfgsDependency>? Dependencies { get; set; }
    }

    internal sealed class CfgsPluginDependency
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("includeDependencies")]
        public bool IncludeDependencies { get; set; } = true;
    }

    internal sealed class CfgsDependency
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("includeDependencies")]
        public bool IncludeDependencies { get; set; } = true;
    }

    internal sealed class CfgsLockFile
    {
        [JsonPropertyName("lockfileVersion")]
        public int LockfileVersion { get; set; }

        [JsonPropertyName("generatedAtUtc")]
        public string GeneratedAtUtc { get; set; } = string.Empty;

        [JsonPropertyName("plugins")]
        public List<CfgsLockedPlugin> Plugins { get; set; } = [];

        [JsonPropertyName("artifacts")]
        public List<CfgsLockedArtifact> Artifacts { get; set; } = [];
    }

    internal sealed class CfgsLockedArtifact
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;
    }

    internal sealed class CfgsLockedPlugin
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("type")]
        public string Type { get; set; } = "plugin";

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("installedPath")]
        public string InstalledPath { get; set; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = string.Empty;

        [JsonPropertyName("includeDependencies")]
        public bool IncludeDependencies { get; set; } = true;
    }
}
