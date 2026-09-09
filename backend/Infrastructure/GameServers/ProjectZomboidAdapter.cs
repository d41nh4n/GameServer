namespace GamePanel.Infrastructure.GameServers;
using GamePanel.Domain.Entities;
using Microsoft.Extensions.Configuration;

/// <summary>
/// Adapter dla Project Zomboid zarządzanego przez systemd (pzserver-game.service).
///
/// Założenia (sprawdzone na Ubuntu runtime):
/// - Status odczytujemy z systemctl show (ActiveState/SubState/MainPID) — BEZ sudo.
/// - Start/Stop/restart przez root-owned wrapper /usr/local/sbin/pz-gamectl
///   wywoływany przez sudo. Wrapper jest allowlistą — adapter NIGDY nie buduje
///   dowolnych komend shell, tylko stałe argumenty sterujące.
/// - Jeden właściciel cyklu życia: systemd. Nie tworzymy drugiego procesu PZ.
/// - MainPID z systemd jest źródłem prawdy dla /proc — naprawia to TODO
///   o "stale ProcessId po restarcie backendu".
/// </summary>
public class ProjectZomboidAdapter : IGameServerAdapter
{
    private readonly ICommandRunner _runner;
    private readonly string _serviceName;
    private readonly string _controlExecutable;
    private readonly string _sudoUser;
    private readonly bool _useSudo;

    public ProjectZomboidAdapter(
        ICommandRunner runner,
        IConfiguration config)
    {
        _runner = runner;
        var serviceName = config["GameServers:ProjectZomboid:ServiceName"];
        var controlExecutable = config["GameServers:ProjectZomboid:ControlExecutable"];
        var sudoUser = config["GameServers:ProjectZomboid:SudoUser"];
        _serviceName = string.IsNullOrWhiteSpace(serviceName) ? "pzserver-game.service" : serviceName;
        _controlExecutable = string.IsNullOrWhiteSpace(controlExecutable) ? "/usr/local/sbin/pz-gamectl" : controlExecutable;
        _sudoUser = sudoUser ?? "";
        _useSudo = true;
    }

    // ---- building blocks (wszystko przez ArgumentList, bez shell) ----

    private List<string> ControlArgv(string subcommand)
    {
        var argv = new List<string>();
        if (_useSudo)
        {
            argv.Add("/usr/bin/sudo");
            argv.Add("-n");
            if (!string.IsNullOrWhiteSpace(_sudoUser))
            {
                argv.Add("-u");
                argv.Add(_sudoUser);
            }
        }
        argv.Add(_controlExecutable);
        argv.Add(subcommand);
        return argv;
    }

    private List<string> SystemctlShowArgv(string property)
    {
        return new List<string>
        {
            "/usr/bin/systemctl",
            "show",
            _serviceName,
            "--property=" + property,
            "--value",
        };
    }

    private string LastLine(string s)
    {
        var trimmed = s.Trim();
        var eol = trimmed.LastIndexOf("\n");
        return eol < 0 ? trimmed : trimmed.Substring(eol + 1);
    }

    // ---- IGameServerAdapter ----

    public async Task<(bool Success, int Pid)> StartAsync(ServerInstance instance, CancellationToken ct = default)
    {
        // Uruchamiamy przez istniejący systemd lifecycle (ExecStartPre → preflight → ProjectZomboid64).
        var res = await _runner.RunAsync(ControlArgv("start"), ct);
        if (!res.Succeeded)
        {
            return (false, 0);
        }

        // Odczytaj rzeczywisty MainPID z systemd (źródło prawdy). Type=simple zgłasza
        // active zanim PZ wstanie — czekamy bounded (max 150s) aż MainPID się pojawi.
        // Nie czekamy na RCON-readiness (to osobny follow-up CR-PZ-08).
        var deadline = DateTime.UtcNow.AddSeconds(150);
        var mainPid = 0;
        while (DateTime.UtcNow < deadline && mainPid <= 0)
        {
            mainPid = await ReadMainPidAsync(ct);
            if (mainPid <= 0)
            {
                await Task.Delay(1000, ct);
            }
        }
        if (mainPid <= 0)
        {
            return (false, 0);
        }
        return (true, mainPid);
    }

    public async Task<bool> StopAsync(int pid, CancellationToken ct = default)
    {
        // Graczowałby graceful shutdown: systemd KillSignal=SIGINT (KILLS worker gry).
        // Wrapper pz-gamectl stop używa kontrolowanego zatrzymania usługi.
        var res = await _runner.RunAsync(ControlArgv("stop"), ct);

        // Poczekaj (ograniczony czas) aż service zgaśnie — to naprawia stale-PID.
        var deadline = DateTime.UtcNow.AddSeconds(24);
        while (DateTime.UtcNow < deadline && await IsActiveAsync(ct))
        {
            await Task.Delay(1000, ct);
        }

        return res.Succeeded;
    }

    public async Task<GameServerStatus> GetStatusAsync(int pid, CancellationToken ct = default)
    {
        var active = await IsActiveAsync(ct);
        if (!active)
        {
            return GameServerStatus.Stopped;
        }

        // Dodatkowo potwierdź przez /proc, że MainPID naprawdę żyje.
        var mainPid = await ReadMainPidAsync(ct);
        if (mainPid <= 0)
        {
            return GameServerStatus.Stopped;
        }
        return Directory.Exists($"/proc/{mainPid}") ? GameServerStatus.Running : GameServerStatus.Stopped;
    }

    // ---- helpers ----

    private async Task<bool> IsActiveAsync(CancellationToken ct)
    {
        var r = await _runner.RunAsync(SystemctlShowArgv("ActiveState"), ct);
        return r.Succeeded && LastLine(r.StdOut).Trim() == "active";
    }

    private async Task<int> ReadMainPidAsync(CancellationToken ct)
    {
        var r = await _runner.RunAsync(SystemctlShowArgv("MainPID"), ct);
        if (!r.Succeeded)
        {
            return 0;
        }
        var line = LastLine(r.StdOut).Trim();
        if (line == "0")
        {
            return 0;
        }
        return TryParseInt(line);
    }

    private static int TryParseInt(string s)
    {
        var result = 0;
        var started = false;
        foreach (var c in s)
        {
            if (c < '0' || c > '9')
            {
                return 0;
            }
            if (result > (2147483647 - (c - '0')) / 10)
            {
                return 0;
            }
            result = result * 10 + (c - '0');
            started = true;
        }
        return started ? result : 0;
    }
}