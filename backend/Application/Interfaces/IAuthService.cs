using GamePanel.Domain.Entities;
using GamePanel.Domain.ValueObjects;

namespace GamePanel.Application.Interfaces;

public interface IAuthService
{
    /// <summary>Tạo JWT đã ký HS256 cho userId với role.</summary>
    JwtToken GenerateToken(Guid userId, UserRole role);

    /// <summary>Kiểm tra token (chữ ký + issuer/audience + lifetime). Trả về User nếu hợp lệ, null nếu invalid/expired.</summary>
    User? ValidateToken(string token);
}