namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 默认地形生成器（MAP-TYPE）：把当前这套方块地图算法作为「默认地形」装进策略口。
/// 本阶段是薄壳——生成算法仍暂驻 BlockField.Build，本类只负责把 (seed, config) 转交进去；
/// 第 2 阶段容器拆分时算法才搬进本类，调用方与配置卡片都不用再改。
///
/// 9 条规则（GND-001 / MAP-001~009）与全部数值逐字保留，不改一行算法。
/// </summary>
public sealed class DefaultMapGenerator : IMapGenerator
{
    public void Generate(BlockField field, int seed, MapGenConfig config)
    {
        field.Build(seed, config);
    }
}
