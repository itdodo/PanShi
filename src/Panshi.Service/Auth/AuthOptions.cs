namespace Panshi.Service.Auth;

/// <summary>JWT 配置（appsettings Jwt 节绑定；生产必须环境变量覆盖 SecretKey）。</summary>
public class AuthOptions
{
    public string SecretKey { get; set; } = "";

    public string Issuer { get; set; } = "Panshi";

    public string Audience { get; set; } = "Panshi.Web";

    public int AccessTokenMinutes { get; set; } = 120;

    public int RefreshTokenDays { get; set; } = 7;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SecretKey) || SecretKey.Length < 32)
            throw new InvalidOperationException("Jwt:SecretKey 缺失或短于 32 字符（生产环境必须环境变量覆盖）");
    }
}
