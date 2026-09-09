namespace GamePanel.Api.CoreContracts;

/// <summary>Body POST /api/auth/login. Walidacja wymaga obecności username i password.</summary>
public record LoginRequest(
    string Username,
    string Password);

/// <summary>Odpowiedź sukcesu POST /api/auth/login.</summary>
public record LoginResponse(
    string Token,
    DateTime ExpiresAt);