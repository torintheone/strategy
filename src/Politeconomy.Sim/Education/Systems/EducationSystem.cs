using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Education.Systems;

/// <summary>Месяц образования: охват, качество, выпуск, квалификация рабочей силы, человеческий капитал. MODEL.md, «Образование».</summary>
public sealed class EducationSystem : ISimSystem
{
    public string Name => "Образование";
    public SimPhase Phase => SimPhase.SocialSphere;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var education = world.Education ?? throw new InvalidOperationException("Образование не создано: вызовите EducationModel.Setup");
        foreach (var country in world.Countries)
            EducationModel.Update(world, country, education[country.Id]);
    }
}
