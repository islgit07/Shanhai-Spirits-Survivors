namespace ShanhaiSpiritsSurvivors;

/// <summary>
/// 地形生成策略口（MAP-TYPE）：一种地形 = 一个实现。「给我一张图」——实现方负责把
/// BlockField 填满，调用方不关心是默认方块、火山还是雪山。
///
/// 默认地形走 DefaultMapGenerator；以后加火山/雪山只需新增一个实现类并在开局逻辑里指定，
/// BlockField（网格容器 + 查询门面）与玩家移动代码一行不用改。
/// </summary>
public interface IMapGenerator
{
    /// <summary>按种子与配置把 field 重建为一张地图。</summary>
    void Generate(BlockField field, int seed, MapGenConfig config);
}
