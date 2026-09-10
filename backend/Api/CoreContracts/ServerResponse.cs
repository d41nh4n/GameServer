namespace GamePanel.Api.CoreContracts;
using GamePanel.Domain.Entities;

/// <summary>
/// API response DTO dla GET /api/servers. Świadomie NIE zawiera Password ani
/// ProcessId — te pola należą do warstwy runtime/domain i nie powinny wyciekać
/// przez transport. Enumy (Type/Status) są rzutowane na liczby (ordinal),
/// identycznie jak serializacja encji ServerInstance przez ASP.NET — dzięki temu
/// kontrakt JSON (m.in. porównania liczby w frontendzie) pozostaje zgodny.
/// </summary>
public record ServerResponse(
    Guid Id,
    string Name,
    string GameType,
    int Type,
    int Status,
    int Port,
    string WorldName,
    string? InstanceKey,
    string ProvisioningMode,
    string RuntimeType,
    bool Ready)
{
    /// <summary>Rzutuje encję domenową na kontrakt API (bez wrażliwych pól).</summary>
    public static ServerResponse FromDomain(ServerInstance s) => new(
        s.Id,
        s.Name,
        s.GameType,
        (int)s.Type,
        (int)s.Status,
        s.Port,
        s.WorldName,
        s.InstanceKey,
        s.ProvisioningMode.ToString(),
        s.RuntimeType.ToString(),
        s.Ready);
}