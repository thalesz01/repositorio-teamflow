using TeamFlow.Runner;
using Xunit;

namespace TeamFlow.Runner.Tests;

// These tests build with the .NET SDK installed on the machine, the same toolchain the sandbox replaces (ADR 0013).
public sealed class HostSandboxTests
{
    private static async Task<ExecutionWorkspace> ProjectAsync(string code)
    {
        var workspace = new ExecutionWorkspace(Path.GetFullPath("work/host-sandbox"));
        await File.WriteAllTextAsync(workspace.Resolve("App.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(workspace.Resolve("Program.cs"), code);
        return workspace;
    }

    [Fact]
    public async Task BuildsInATemporaryCopyAndReportsCompilerErrorsAsync()
    {
        using var workspace = await ProjectAsync("System.Console.WriteLine(\"ok\");");
        var build = new SandboxCommand("dotnet", ["build", "--nologo"], TimeSpan.FromMinutes(2));
        var passed = await new HostSandbox().ExecuteAsync(workspace, build, default);
        Assert.Equal(0, passed.ExitCode);
        Assert.False(passed.TimedOut);
        // Build output stays out of the execution workspace.
        Assert.False(Directory.Exists(workspace.Resolve("bin"))); Assert.False(Directory.Exists(workspace.Resolve("obj")));
        await File.WriteAllTextAsync(workspace.Resolve("Program.cs"), "this is not C#");
        var failed = await new HostSandbox().ExecuteAsync(workspace, build, default);
        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("error CS", failed.StandardOutput);
    }

    [Fact]
    public async Task MissingToolsAndTimeoutsAreReportedWithoutThrowingAsync()
    {
        using var workspace = await ProjectAsync("System.Console.WriteLine(\"ok\");");
        var missing = await new HostSandbox().ExecuteAsync(workspace, new("teamflow-tool-that-does-not-exist", [], TimeSpan.FromSeconds(5)), default);
        Assert.Equal(127, missing.ExitCode);
        Assert.Contains("was not found", missing.StandardError);
        var slow = await new HostSandbox().ExecuteAsync(workspace, new("dotnet", ["build", "--nologo", "--no-incremental"], TimeSpan.FromMilliseconds(300)), default);
        Assert.True(slow.TimedOut);
    }

    [Fact]
    public async Task DoesNotPassTheRunnerTokenOrApiSettingsToTheCommandAsync()
    {
        using var workspace = await ProjectAsync("System.Console.WriteLine(System.Environment.GetEnvironmentVariable(\"TEAMFLOW_RUNNER_TOKEN\") ?? \"absent\");");
        Environment.SetEnvironmentVariable("TEAMFLOW_RUNNER_TOKEN", "secret-token-value");
        try
        {
            var run = await new HostSandbox().ExecuteAsync(workspace, new("dotnet", ["run", "--nologo"], TimeSpan.FromMinutes(2)), default);
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("absent", run.StandardOutput);
            Assert.DoesNotContain("secret-token-value", run.StandardOutput + run.StandardError);
        }
        finally { Environment.SetEnvironmentVariable("TEAMFLOW_RUNNER_TOKEN", null); }
    }
}
