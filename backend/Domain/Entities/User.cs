namespace GamePanel.Domain.Entities;

public enum UserRole
{
    User,
    Admin,
}

public class User
{
    public Guid Id { get; set; }
    public string Username { get; set; } = "";
    /// <summary>Zachemiczona (case-insensitive) kopia Username używana przy wyszukiwaniu/unique.</summary>
    public string NormalizedUsername { get; set; } = "";
    /// <summary>Hash hasła użytkownika. NIGDY nie przechowujemy plaintext hasła.</summary>
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.User;
}