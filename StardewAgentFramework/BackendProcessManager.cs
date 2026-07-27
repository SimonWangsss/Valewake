using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using StardewModdingAPI;

namespace StardewAgentFramework;

/// <summary>Starts and owns the bundled local agent backend when one isn't already running.</summary>
public sealed class BackendProcessManager : IDisposable
{
    private readonly IMonitor monitor;
    private readonly ModConfig config;
    private readonly string modDirectory;
    private readonly HttpClient healthClient = new() { Timeout = TimeSpan.FromSeconds(2) };
    private readonly SemaphoreSlim startLock = new(1, 1);
    private readonly Uri healthEndpoint;
    private Process? ownedProcess;
    private bool disposed;

    public BackendProcessManager(IMonitor monitor, ModConfig config, string modDirectory)
    {
        this.monitor = monitor;
        this.config = config;
        this.modDirectory = modDirectory;
        healthEndpoint = new Uri(config.BackendHealthUrl);
    }

    public async Task<bool> EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        if (disposed)
            return false;
        if (await IsHealthyAsync(cancellationToken))
            return true;
        if (!config.AutoStartBackend)
            return false;

        await startLock.WaitAsync(cancellationToken);
        try
        {
            if (await IsHealthyAsync(cancellationToken))
                return true;

            if (ownedProcess is null || ownedProcess.HasExited)
                StartBackendProcess();
            Process process = ownedProcess!;

            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(3, config.BackendStartupTimeoutSeconds));
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited)
                {
                    monitor.Log($"Bundled agent backend exited with code {process.ExitCode}.", LogLevel.Error);
                    return false;
                }
                if (await IsHealthyAsync(cancellationToken))
                {
                    monitor.Log($"Agent backend is ready at {healthEndpoint}.", LogLevel.Info);
                    return true;
                }
                await Task.Delay(250, cancellationToken);
            }

            monitor.Log($"Agent backend did not become ready within {config.BackendStartupTimeoutSeconds} seconds.", LogLevel.Error);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            monitor.Log($"Could not start the bundled agent backend: {ex.Message}", LogLevel.Error);
            return false;
        }
        finally
        {
            startLock.Release();
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await healthClient.GetAsync(healthEndpoint, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void StartBackendProcess()
    {
        string executablePath = Path.IsPathRooted(config.BackendExecutablePath)
            ? config.BackendExecutablePath
            : Path.Combine(modDirectory, config.BackendExecutablePath);
        executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(executablePath))
            throw new FileNotFoundException("Bundled backend executable was not found", executablePath);

        Uri chatEndpoint = new(config.BackendUrl);
        if (!chatEndpoint.IsLoopback || !healthEndpoint.IsLoopback)
            throw new InvalidOperationException("Automatic backend startup is only allowed for loopback URLs.");

        string workingDirectory = Path.GetFullPath(Path.Combine(modDirectory, config.BackendWorkingDirectory));
        Directory.CreateDirectory(workingDirectory);
        ProcessStartInfo startInfo = new()
        {
            FileName = executablePath,
            Arguments = $"--host 127.0.0.1 --port {chatEndpoint.Port}",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        ownedProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        ownedProcess.OutputDataReceived += (_, e) =>
        {
            if (config.DebugLogging && !string.IsNullOrWhiteSpace(e.Data))
                monitor.Log($"Backend: {e.Data}", LogLevel.Trace);
        };
        ownedProcess.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                monitor.Log($"Backend: {e.Data}", LogLevel.Debug);
        };
        if (!ownedProcess.Start())
            throw new InvalidOperationException("Windows did not start the bundled backend process.");
        ownedProcess.BeginOutputReadLine();
        ownedProcess.BeginErrorReadLine();
        monitor.Log($"Starting bundled agent backend (PID {ownedProcess.Id}).", LogLevel.Info);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;

        try
        {
            if (ownedProcess is not null && !ownedProcess.HasExited)
            {
                ownedProcess.Kill(entireProcessTree: true);
                ownedProcess.WaitForExit(3000);
                monitor.Log("Stopped the bundled agent backend.", LogLevel.Trace);
            }
        }
        catch (Exception ex)
        {
            monitor.Log($"Could not stop the bundled agent backend cleanly: {ex.Message}", LogLevel.Warn);
        }
        finally
        {
            ownedProcess?.Dispose();
            healthClient.Dispose();
            startLock.Dispose();
        }
    }
}
