namespace GamePanel.Infrastructure.GameServers;

public struct CommandResult
{
    public int ExitCode { get; set; }
    public string StdOut { get; set; }
    public string StdErr { get; set; }

    public CommandResult()
    {
        ExitCode = 0;
        StdOut = "";
        StdErr = "";
    }

    public bool Succeeded => ExitCode == 0;
}

/// <summary>
/// Testowalny borderline między adapterami i systemem: wywołuje zewnętrzny plik
/// przez ProcessStartInfo + ArgumentList (bez shell). Wstrzykiwany, żeby testy
/// mogły symulować różne wyniki (start OK, start fail, brak uprawnień itp.).
/// Bateria konkretnych poleceń (np. sudo, systemctl) żyje w warstwie adaptera,
/// nie tutaj.
/// </summary>
public interface ICommandRunner
{
    Task<CommandResult> RunAsync(List<string> program, CancellationToken ct = default);
}