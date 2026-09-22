using Panshi.Common.Security;
using Xunit;

namespace Panshi.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void Roundtrip_New_Hash_Verifies()
    {
        var hash = PasswordHasher.Hash("Admin@123456");
        Assert.True(PasswordHasher.Verify("Admin@123456", hash));
        Assert.False(PasswordHasher.Verify("Admin@123456x", hash));
        Assert.False(PasswordHasher.NeedsRehash(hash));
    }

    [Fact]
    public void Verifies_Existing_Seed_Hash()
    {
        const string stored = "PBKDF2$100000$oL5CDTx+82OSoHJGT+APMw==$A1rMK2r8mhpc6gPOcryfjUACpRRZEdfyiC1GM5TDEEU=";
        Assert.True(PasswordHasher.Verify("Admin@123456", stored));
    }

    [Fact]
    public void Legacy_Sha256_Verifies_And_NeedsRehash()
    {
        var legacy = System.Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("abc123")))
            .ToLowerInvariant();
        Assert.True(PasswordHasher.Verify("abc123", legacy));
        Assert.True(PasswordHasher.NeedsRehash(legacy));
    }

    [Theory]
    [InlineData("short1!", false)] // 7 位
    [InlineData("abcdefgh", false)] // 只有一类
    [InlineData("Abc@123456", true)]
    [InlineData("password123", false)] // 小写+数字=两类
    public void Policy(string pwd, bool expectOk)
    {
        var (ok, _) = PasswordHasher.ValidatePolicy(pwd);
        Assert.Equal(expectOk, ok);
    }
}
