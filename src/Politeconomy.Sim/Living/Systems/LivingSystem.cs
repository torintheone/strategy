using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Living.Systems;

/// <summary>Месяц жилья, преступности, коррупции и экологии. MODEL.md, «Жильё, преступность, коррупция, экология».</summary>
public sealed class LivingSystem : ISimSystem
{
    public string Name => "Жильё, преступность, экология";
    public SimPhase Phase => SimPhase.SocialSphere;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var living = world.Living ?? throw new InvalidOperationException("Условия жизни не созданы: вызовите LivingModel.Setup");
        foreach (var country in world.Countries)
            LivingModel.Update(world, country, living[country.Id]);
    }
}
