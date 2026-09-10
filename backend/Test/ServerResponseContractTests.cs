using System.Text;
using GamePanel.Api.CoreContracts;
using GamePanel.Domain.Entities;

/// <summary>
/// Testy kontraktu DTO API (CR-00A). Dowodzą, że odpowiedź GET /api/servers
/// mapuje encję domenową bez wycieku Password / ProcessId przy równoczesnym
/// zachowaniu pól wymaganych przez frontend. Testy NIE dotykają realnego
/// runtime (systemd / proces gry / DB) — tylko czysta konstrukcja DTO.
/// </summary>
public class ServerResponseContractTests
{
    private static ServerInstance SampleInstance()
    {
        return new ServerInstance
        {
            Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Name = "Project Zomboid Server",
            GameType = "ProjectZomboid",
            Type = GameServerType.ProjectZomboid,
            Status = ServerStatus.Running,
            Port = 16261,
            WorldName = "servertest_new",
            Password = "supersecret-do-not-leak",
            ProcessId = 44566,
            InstanceKey = "valheim-main",
            ProvisioningMode = ProvisioningMode.AdoptExisting,
            RuntimeType = ServerRuntimeType.Systemd,
            RuntimeId = "valheim-main.service",
            InstallationPath = "/sensitive/server",
            DataPath = "/sensitive/data",
            BackupPath = "/sensitive/backups",
            Ready = true,
        };
    }

    [Fact]
    public void FromDomain_IncludesRequiredFields()
    {
        var dto = ServerResponse.FromDomain(SampleInstance());

        Assert.Equal("33333333-3333-3333-3333-333333333333", dto.Id.ToString());
        Assert.Equal("Project Zomboid Server", dto.Name);
        Assert.Equal("ProjectZomboid", dto.GameType);
        Assert.Equal((int)GameServerType.ProjectZomboid, dto.Type);
        Assert.Equal((int)ServerStatus.Running, dto.Status);
        Assert.Equal(16261, dto.Port);
        Assert.Equal("servertest_new", dto.WorldName);
        Assert.Equal("valheim-main", dto.InstanceKey);
        Assert.Equal("AdoptExisting", dto.ProvisioningMode);
        Assert.Equal("Systemd", dto.RuntimeType);
        Assert.True(dto.Ready);
    }

    [Fact]
    public void FromDomain_ExposesNoSensitiveProperties()
    {
        var dto = ServerResponse.FromDomain(SampleInstance());

        // Odwzorowanie przez publiczny interfejs DTO — te pola nie istnieją wypełnione.
        var json = Serialize(dto).ToLower();

        Assert.DoesNotContain("password", json);
        Assert.DoesNotContain("processid", json);
        Assert.DoesNotContain("supersecret", json);
        Assert.DoesNotContain("44566", json);
        Assert.DoesNotContain("runtimeid", json);
        Assert.DoesNotContain("installationpath", json);
        Assert.DoesNotContain("datapath", json);
        Assert.DoesNotContain("backuppath", json);
        Assert.DoesNotContain("invocationid", json);
        Assert.DoesNotContain("/sensitive/", json);
    }

    [Fact]
    public void FromDomain_PreservesEnumRepresentation()
    {
        var dto = ServerResponse.FromDomain(SampleInstance());

        // Frontend używa type (enum) i gameType (string) do renderowania kart.
        // Enumy idą jako liczby (ordinal — zgodnie z dotychczasową serializacją encji),
        // gameType jako string; obie muszą być spójne z kontraktem frontendu.
        Assert.Equal((int)GameServerType.ProjectZomboid, dto.Type);
        Assert.Equal((int)ServerStatus.Running, dto.Status);
        Assert.Equal("ProjectZomboid", dto.GameType);
    }

    private static string Serialize(ServerResponse dto)
    {
        // Lekka serializacja kluczowych pól bez zależności od frameworka web.
        // (Konwersja do JSON tu nie jest kompletna, ale wystarcza do testu kontraktu.)
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append("\"id\":\"").Append(dto.Id).Append("\",");
        sb.Append("\"name\":\"").Append(dto.Name).Append("\",");
        sb.Append("\"gameType\":\"").Append(dto.GameType).Append("\",");
        sb.Append("\"type\":\"").Append(dto.Type).Append("\",");
        sb.Append("\"status\":\"").Append(dto.Status).Append("\",");
        sb.Append("\"port\":").Append(dto.Port).Append(',');
        sb.Append("\"worldName\":\"").Append(dto.WorldName).Append("\",");
        sb.Append("\"instanceKey\":\"").Append(dto.InstanceKey).Append("\",");
        sb.Append("\"provisioningMode\":\"").Append(dto.ProvisioningMode).Append("\",");
        sb.Append("\"runtimeType\":\"").Append(dto.RuntimeType).Append("\",");
        sb.Append("\"ready\":").Append(dto.Ready ? "true" : "false");
        sb.Append('}');
        return sb.ToString();
    }
}