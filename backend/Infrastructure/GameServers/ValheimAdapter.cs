namespace GamePanel.Infrastructure.GameServers;
using System.Diagnostics;
using GamePanel.Domain.Entities;
using Microsoft.Extensions.Configuration;

public class ValheimAdapter : IGameServerAdapter
{
    private readonly string _executablePath;

    public ValheimAdapter(IConfiguration config)
    {
        // Path đi từ config (không hardcode)
        var path = config["GameServers:Valheim:ExecutablePath"];
        _executablePath = string.IsNullOrWhiteSpace(path)
            ? throw new InvalidOperationException("Missing config GameServers:Valheim:ExecutablePath")
            : ExpandHome(path);
    }

    /// <summary>Thay "~" và "{HOME}" bằng thư mục home thật để appsettings portablẹ, không hardcode đường dẫn máy.</summary>
    private static string ExpandHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.Replace("{HOME}", home).Replace("~", home);
    }

    public Task<(bool Success, int Pid)> StartAsync(ServerInstance instance, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_executablePath) ?? ".",
        };
        psi.ArgumentList.Add("-name");
        psi.ArgumentList.Add(instance.Name);
        psi.ArgumentList.Add("-port");
        psi.ArgumentList.Add(instance.Port.ToString());
        psi.ArgumentList.Add("-world");
        psi.ArgumentList.Add(instance.WorldName);
        psi.ArgumentList.Add("-password");
        psi.ArgumentList.Add(instance.Password);
        psi.ArgumentList.Add("-logFile");
        psi.ArgumentList.Add("-");

        Process? proc;
        try
        {
            proc = Process.Start(psi);
        }
        catch (Exception)
        {
            return Task.FromResult((false, 0));
        }

        if (proc is null) return Task.FromResult((false, 0));
        return Task.FromResult((true, proc.Id));
    }

    public Task<bool> StopAsync(int pid, CancellationToken ct = default)
    {
        if (pid <= 0) return Task.FromResult(false);
        try
        {
            // Gửi SIGTERM (-15) như yêu cầu
            var psi = new ProcessStartInfo
            {
                FileName = "kill",
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("-15");
            psi.ArgumentList.Add(pid.ToString());
            using var kill = Process.Start(psi);
            kill?.WaitForExit(10000);
            return Task.FromResult(true);
        }
        catch (Exception)
        {
            return Task.FromResult(false);
        }
    }

    public Task<GameServerStatus> GetStatusAsync(int pid, CancellationToken ct = default)
    {
        if (pid <= 0) return Task.FromResult(GameServerStatus.Stopped);
        var procExists = Directory.Exists($"/proc/{pid}");
        return Task.FromResult(procExists ? GameServerStatus.Running : GameServerStatus.Stopped);
    }
}