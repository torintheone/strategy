using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Infrastructure.Systems;

/// <summary>Месяц инфраструктуры: нагрузка, износ и обслуживание, стройка, обеспеченность, стройки игрока. MODEL.md, «Инфраструктура и стройки».</summary>
public sealed class InfrastructureSystem : ISimSystem
{
    public string Name => "Инфраструктура";
    public SimPhase Phase => SimPhase.Economy;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var infrastructure = world.Infrastructure ?? throw new InvalidOperationException("Инфраструктура не создана: вызовите InfrastructureModel.Setup");
        foreach (var country in world.Countries)
            InfrastructureModel.Update(world, country, infrastructure[country.Id]);
    }
}
