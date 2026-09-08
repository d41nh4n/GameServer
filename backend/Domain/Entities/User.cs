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
    public UserRole Role { get; set; } = UserRole.User;
}