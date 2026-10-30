using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy;

/// <summary>
/// Обеспеченность отрасли энергией и инфраструктурой: во сколько раз выпуск меньше, чем мог бы быть, из-за
/// веерных отключений и перегруженной инфраструктуры (1 — ничто не мешает). MODEL.md, «Производство».
/// </summary>
public static class Supply
{
    public static double Factor(World world, string country, int industry)
    {
        var energy = world.Energy?.Countries.GetValueOrDefault(country)?.Cut;
        var infrastructure = world.Infrastructure?.Countries.GetValueOrDefault(country)?.Cut;
        return (energy != null && industry < energy.Length ? 1 - energy[industry] : 1)
               * (infrastructure != null && industry < infrastructure.Length ? 1 - infrastructure[industry] : 1);
    }
}
