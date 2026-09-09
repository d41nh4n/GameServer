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
            if (proc is { } p)
            {
                p.WaitForExit(300000);
                exitCode = p.ExitCode;
                if (p.StandardOutput is { } so)
                {
                    stdout.AppendLine(so.ReadToEnd());
                }
                if (p.StandardError is { } se)
                {
                    stderr.AppendLine(se.ReadToEnd());
                }
            }
        }
        catch (Exception e)
        {
            stderr.AppendLine(e.Message);
            exitCode = -1;
        }

        return new CommandResult { ExitCode = exitCode, StdOut = stdout.ToString(), StdErr = stderr.ToString() };
    }
}