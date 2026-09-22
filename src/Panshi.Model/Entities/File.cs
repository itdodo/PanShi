using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>文件（扩展名白名单 + 大小限制 + magic bytes 内容嗅探后落盘）</summary>
[SugarTable("sys_file")]
public class SysFile : BaseEntity
{
    /// <summary>存储名（GUID 防碰撞）</summary>
    [SugarColumn(Length = 256)]
    public string StoreName { get; set; } = "";

    /// <summary>原始文件名</summary>
    [SugarColumn(Length = 256)]
    public string FileName { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string ContentType { get; set; } = "";

    /// <summary>字节数</summary>
    [SugarColumn(ColumnDataType = "bigint")]
    public long Size { get; set; }

    /// <summary>业务归类（avatar/doc/image…）</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? BizType { get; set; }

    /// <summary>上传者</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? UploaderId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? UploaderName { get; set; }
}
