using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Health.Systems;

/// <summary>Месяц здравоохранения: врачи, койки, доступность, индекс здоровья, потери от болезней, пенсии и старение. MODEL.md, «Здравоохранение».</summary>
public sealed class HealthSystem : ISimSystem
{
    public string Name => "Здравоохранение";
    public SimPhase Phase => SimPhase.SocialSphere;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var health = world.Health ?? throw new InvalidOperationException("Здравоохранение не создано: вызовите HealthModel.Setup");
        foreach (var country in world.Countries)
            HealthModel.Update(world, country, health[country.Id]);
    }
}
