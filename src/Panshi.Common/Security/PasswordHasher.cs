using System.Security.Cryptography;
using System.Text;

namespace Panshi.Common.Security;

/// <summary>
/// 密码哈希：PBKDF2-SHA256（格式 PBKDF2$迭代数$盐$哈希），兼容历史 SHA256(password+固定盐内空盐) 裸哈希校验。
/// 历史哈希命中后由服务层透明升级（NeedsRehash）。
/// </summary>
public static class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;
    private const string Prefix = "PBKDF2";

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations,
            HashAlgorithmName.SHA256, HashBytes);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;

        if (stored.StartsWith(Prefix + "$", StringComparison.Ordinal))
        {
            var parts = stored.Split('$');
            if (parts.Length != 4 || !int.TryParse(parts[1], out var iterations)) return false;
            try
            {
                var salt = Convert.FromBase64String(parts[2]);
                var expected = Convert.FromBase64String(parts[3]);
                var actual = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations,
                    HashAlgorithmName.SHA256, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        // 历史通道：裸 SHA256(password) 小写 hex
        if (stored.Length == 64 && IsHex(stored))
        {
            var sha = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)))
                .ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sha),
                Encoding.UTF8.GetBytes(stored.ToLowerInvariant()));
        }

        return false;
    }

    /// <summary>存储的不是 PBKDF2 → 登录成功后需重哈希升级。</summary>
    public static bool NeedsRehash(string stored) => !stored.StartsWith(Prefix + "$", StringComparison.Ordinal);

    /// <summary>强度策略：≥8 位且含 大写/小写/数字/符号 中三类（服务端强制）。</summary>
    public static (bool Ok, string Msg) ValidatePolicy(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return (false, "密码长度至少 8 位");
        if (password.Length > 64)
            return (false, "密码长度不能超过 64 位");
        var categories = 0;
        if (password.Any(char.IsUpper)) categories++;
        if (password.Any(char.IsLower)) categories++;
        if (password.Any(char.IsDigit)) categories++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) categories++;
        if (categories < 3)
            return (false, "密码须包含大写、小写、数字、符号中至少三类");
        return (true, "ok");
    }

    private static bool IsHex(string s) => s.All(Uri.IsHexDigit);
}
