using GamePanel.Domain.Entities;
using GamePanel.Domain.ValueObjects;

namespace GamePanel.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Tạo JWT đã ký HS256 cho userId với role.</summary>
    JwtToken GenerateToken(Guid userId, UserRole role);

    /// <summary>Kiểm tra token (chữ ký + issuer/audience + lifetime). Trả về User nếu hợp lệ, null nếu invalid/expired.</summary>
    User? ValidateToken(string token);

    /// <summary>
    /// Xác thực local user bằng username + password. Trả về user nếu hợp lệ, null nếu
    /// username không tồn tại HOẶC password sai (không tiết lộ field nào sai).
    /// </summary>
    Task<User?> AuthenticateAsync(string username, string password, CancellationToken ct = default);

    /// <summary>True nếu bảng Users đang trống (dùng cho bootstrap admin đầu tiên).</summary>
    Task<bool> UsersNeedBootstrapAsync(CancellationToken ct = default);

    /// <summary>
    /// Tạo admin đầu tiên (idempotent) về username/password từ env bootstrap.
    /// Được gọi chỉ khi Users rỗng. Hash password trước khi lưu; NÉM nếu username/password thiếu.
    /// Không in/ghi rõ plaintext password.
    /// </summary>
    Task EnsureBootstrapAdminAsync(string envUsername, string envPassword, CancellationToken ct = default);
}