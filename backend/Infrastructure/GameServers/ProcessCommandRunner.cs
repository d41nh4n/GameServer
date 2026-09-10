namespace GamePanel.Infrastructure.GameServers;
using System.Diagnostics;
using System.Text;

/// <summary>ICommandRunner oparty o ProcessStartInfo + ArgumentList (bez shell).</summary>
public class ProcessCommandRunner : ICommandRunner
{
    public async Task<CommandResult> RunAsync(List<string> argv, CancellationToken ct = default)
    {
        if (argv == null || argv.Count == 0)
        {
            return new CommandResult { ExitCode = -1, StdErr = "Empty program argv" };
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var psi = new ProcessStartInfo
        {
            FileName = argv[0],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        for (var i = 1; i < argv.Count; i++)
        {
            psi.ArgumentList.Add(argv[i]);
        }

        Process? proc = null;
        var exitCode = -1;
        try
        {
            proc = Process.Start(psi);
            if (proc is not { } process)
            {
                return new CommandResult
                {
                    ExitCode = -1,
                    StdErr = "Process failed to start",
                };
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            stdout.AppendLine(await stdoutTask);
            stderr.AppendLine(await stderrTask);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (proc is { HasExited: false }) proc.Kill(entireProcessTree: true);
            }
            catch
            {
                // Best effort: cancellation must still propagate to the caller.
            }
            throw;
        }
        catch (Exception e)
        {
            stderr.AppendLine(e.Message);
            exitCode = -1;
        }
        finally
        {
            proc?.Dispose();
        }

        return new CommandResult { ExitCode = exitCode, StdOut = stdout.ToString(), StdErr = stderr.ToString() };
    }
}