using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace CFGS.Unit.Tests;

/// <summary>
/// Makes the existing executable and script suites part of `dotnet test` without
/// weakening their per-case checks or running them twice in run-all-tests.ps1.
/// </summary>
public sealed class LegacySuiteTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string Configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent?.Name
        ?? throw new InvalidOperationException("Could not determine the build configuration.");

    [Fact]
    public async Task ToolingSuite()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "CFGS.Tooling.Tests.dll");
        RunResult run = await RunAsync("dotnet", [dll]);
        AssertSummary(run, "TOOLING_TEST_SUMMARY", 89);
    }

    [Fact]
    public async Task LspSuite()
    {
        string dll = Path.Combine(AppContext.BaseDirectory, "CFGS.Lsp.Tests.dll");
        RunResult run = await RunAsync("dotnet", [dll]);
        AssertSummary(run, "LSP_TEST_SUMMARY", 30);
    }

    [Fact]
    public async Task EdgecaseSuite()
    {
        string script = Path.Combine(RepoRoot, "CFGS_NE", "Edgecases", "run_all_edgecases.ps1");
        RunResult run = await RunAsync(PowerShell(), ["-NoProfile", "-File", script]);
        AssertSummary(run, "EDGECASE_SUMMARY", 341);
    }

    [Fact]
    public async Task SampleSuite()
    {
        string script = Path.Combine(RepoRoot, "CFGS_NE", "Samples", "run_all_samples.ps1");
        RunResult run = await RunAsync(PowerShell(),
            ["-NoProfile", "-File", script, "-Configuration", Configuration, "-SkipBuild"]);
        AssertSummary(run, "SAMPLES_SUMMARY", 19);
    }

    private static string PowerShell()
    {
        if (!OperatingSystem.IsWindows())
            return "pwsh";

        string installedPwsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "PowerShell", "7", "pwsh.exe");
        return File.Exists(installedPwsh) ? installedPwsh : "powershell.exe";
    }

    private static async Task<RunResult> RunAsync(string executable, string[] arguments)
    {
        ProcessStartInfo start = new(executable)
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = new() { StartInfo = start };
        Assert.True(process.Start(), $"Could not start {executable}.");

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw;
        }

        return new RunResult(process.ExitCode, (await stdout) + Environment.NewLine + (await stderr));
    }

    private static void AssertSummary(RunResult run, string prefix, int minimumTests)
    {
        string pattern = $@"(?m)^{Regex.Escape(prefix)}:TOTAL=(\d+):PASSED=(\d+):FAILED=(\d+)(?::SKIPPED=(\d+))?\r?$";
        Match summary = Regex.Match(run.Output, pattern, RegexOptions.CultureInvariant);
        Assert.True(summary.Success, $"Missing {prefix} summary (exit {run.ExitCode}).{Environment.NewLine}{Tail(run.Output)}");

        int total = int.Parse(summary.Groups[1].Value);
        int passed = int.Parse(summary.Groups[2].Value);
        int failed = int.Parse(summary.Groups[3].Value);
        int skipped = summary.Groups[4].Success ? int.Parse(summary.Groups[4].Value) : 0;

        Assert.True(total >= minimumTests,
            $"{prefix} ran only {total} tests; expected at least {minimumTests}.{Environment.NewLine}{Tail(run.Output)}");
        Assert.True(run.ExitCode == 0 && failed == 0 && skipped == 0 && passed == total,
            $"{prefix} exit={run.ExitCode}, total={total}, passed={passed}, failed={failed}, skipped={skipped}."
            + Environment.NewLine + Tail(run.Output));
    }

    private static string Tail(string output) =>
        string.Join(Environment.NewLine, output.Split('\n').TakeLast(50));

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CFGS_VM.sln")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate CFGS_VM.sln above the test output directory.");
    }

    private sealed record RunResult(int ExitCode, string Output);
}
