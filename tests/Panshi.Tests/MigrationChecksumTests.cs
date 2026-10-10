using Panshi.Repository;
using Xunit;

namespace Panshi.Tests;

/// <summary>
/// 迁移内容指纹（sys_db_migration.checksum，迁移 0014）。
/// 归一化这条最要紧：镜像在 CI（LF）与本机 Windows 构建（CRLF）下拿到的资源字节不一样，
/// 若按原始字节算哈希，换一台机器构建就会把老库拦在启动外。
/// </summary>
public class MigrationChecksumTests
{
    [Fact]
    public void Checksum_Normalizes_Line_Endings()
    {
        var lf = DbMigrationRunner.ChecksumOf("select 1;\n");

        Assert.Equal(lf, DbMigrationRunner.ChecksumOf("select 1;\r\n"));
        Assert.Equal(lf, DbMigrationRunner.ChecksumOf("select 1;\r"));
        Assert.Equal(lf, DbMigrationRunner.ChecksumOf("select 1;\n\n\n   "));
        Assert.Matches("^[0-9a-f]{64}$", lf);
    }

    [Fact]
    public void Checksum_Changes_When_Content_Changes()
    {
        var before = DbMigrationRunner.ChecksumOf("create unique index uk_a on t (c);");
        var after = DbMigrationRunner.ChecksumOf("create unique index uk_a on t (c, d);");

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Checksum_Of_Empty_Text_Is_Stable()
        => Assert.Equal(DbMigrationRunner.ChecksumOf(""), DbMigrationRunner.ChecksumOf("\r\n\n"));
}
