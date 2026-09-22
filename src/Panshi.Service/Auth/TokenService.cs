using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Panshi.Model.Entities;

namespace Panshi.Service.Auth;

/// <summary>JWT(2h) + RefreshToken(7天轮换) 令牌工厂。</summary>
public class TokenService
{
    public TokenService(AuthOptions options)
    {
        Options = options;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SecretKey));
    }

    private readonly SymmetricSecurityKey _key;

    public AuthOptions Options { get; }

    /// <summary>签发访问令牌（jti=会话 TokenId，服务端会话表校验存在性）。</summary>
    public string CreateAccessToken(SysUser user, string tokenId)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, tokenId),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new("nick", user.NickName)
        };
        if (user.DeptId is not null) claims.Add(new Claim("dept", user.DeptId.Value.ToString()));

        var token = new JwtSecurityToken(
            issuer: Options.Issuer,
            audience: Options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(Options.AccessTokenMinutes),
            signingCredentials: new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>新 RefreshToken（48 字节随机 base64url；只存哈希）</summary>
    public static string NewRefreshToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string NewTokenId() => Guid.NewGuid().ToString("N");

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
