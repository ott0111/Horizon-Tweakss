using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Horizon.Core.Interfaces;
using Horizon.Core.Models;

namespace Horizon.Platform;

public sealed class PowerShellCommandRunner : ICommandRunner
{
    public bool IsWindows => OperatingSystem.IsWindows();

    public async Task<CommandResult> RunPowerShellAsync(string script, bool elevated = false, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (!IsWindows) throw new PlatformNotSupportedException("This operation requires Windows 10 or Windows 11.");
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);
        using var timeoutSource = new CancellationTokenSource(effectiveTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var token = linked.Token;

        var windowsPowerShell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(windowsPowerShell)) throw new PlatformNotSupportedException("Windows PowerShell is not available on this PC.");
        var info = new ProcessStartInfo(windowsPowerShell)
        {
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (elevated)
        {
            info.UseShellExecute = true;
            info.Verb = "runas";
            info.Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}";
        }
        else
        {
            info.UseShellExecute = false;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-ExecutionPolicy");
            info.ArgumentList.Add("Bypass");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(encoded);
        }

        try
        {
            using var process = Process.Start(info) ?? throw new InvalidOperationException("Windows PowerShell could not be started.");
            try
            {
                var outputTask = elevated ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync(token);
                var errorTask = elevated ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);
                var output = (await outputTask).Trim().TrimStart('\uFEFF');
                var error = (await errorTask).Trim();
                if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? $"PowerShell exited with code {process.ExitCode}." : error);
                return new(process.ExitCode, output, error);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
                if (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    throw new TimeoutException($"The operation exceeded {effectiveTimeout.TotalSeconds:0} seconds.");
                throw;
            }
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            throw new OperationCanceledException("Administrator permission was cancelled.", exception, cancellationToken);
        }
    }
}
