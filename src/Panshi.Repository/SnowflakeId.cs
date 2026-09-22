using Yitter.IdGenerator;

namespace Panshi.Repository;

/// <summary>雪花 ID 封装（Yitter，WorkerId 0-63，位宽 6）。</summary>
public static class SnowflakeId
{
    private static readonly object Gate = new();

    private static bool _initialized;

    /// <summary>初始化（幂等，进程内仅一次；⚠️ 位宽配置变更会导致新旧 Id 不可比较大小）。</summary>
    public static void Init(ushort workerId)
    {
        if (workerId > 63)
            throw new ArgumentOutOfRangeException(nameof(workerId), "WorkerId 必须在 0-63 之间（位宽 6）");
        lock (Gate)
        {
            if (_initialized) return;
            YitIdHelper.SetIdGenerator(new IdGeneratorOptions(workerId) { WorkerIdBitLength = 6 });
            _initialized = true;
        }
    }

    /// <summary>生成新 Id。种子/批量 Insertable 直连路径必须显式调用（红线：不触发 AOP）。</summary>
    public static long NextId()
    {
        if (!_initialized)
            throw new InvalidOperationException("雪花 ID 未初始化（Db:SnowflakeWorkerId）");
        return YitIdHelper.NextId();
    }
}
