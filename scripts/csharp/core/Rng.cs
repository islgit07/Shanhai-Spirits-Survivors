using Godot;

namespace ShanHaiHuanLing;

/// <summary>
/// @trace SEED-001 一局一种子 + FNV-1a 分域随机流隔离。
/// 刷怪 / 地形 / 唤灵 / 营地各走独立流，任一处多抽一次不会串改其他系统结果。
/// </summary>
public static class Rng
{
    public static uint HashStr(string s)
    {
        uint h = 2166136261u;
        foreach (char c in s)
        {
            h ^= (byte)c;
            h *= 16777619u;
        }
        return h;
    }

    public static RandomNumberGenerator Stream(int seed, string domain)
    {
        return new RandomNumberGenerator
        {
            Seed = (ulong)((uint)seed ^ HashStr(domain)) & 0x7FFFFFFFUL,
        };
    }

    /// <summary>查询型随机：同一 (seed, domain, index) 恒返回同一值，不推进任何状态。</summary>
    public static float Deterministic(int seed, string domain, int index)
    {
        unchecked
        {
            uint x = (uint)seed ^ HashStr(domain) ^ (uint)(index * unchecked((int)0x9E3779B1));
            x = (x ^ (x >> 16)) * 0x7FEB352Du;
            x = (x ^ (x >> 15)) * 0x846CA68Bu;
            x ^= x >> 16;
            return x / 4294967296.0f;
        }
    }

    /// <summary>地形用独立 FNV-1a 64 位派生（与 GDScript 版行为对齐）。</summary>
    public static ulong DeriveSeed64(int seed, string domain)
    {
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            foreach (char c in domain)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return hash ^ (ulong)(long)seed;
        }
    }
}
