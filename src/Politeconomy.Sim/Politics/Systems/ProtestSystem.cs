using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics.Systems;

/// <summary>Месяц протестов: лояльность силовиков, новые протесты, эскалация, ответ власти, поправки к модели. MODEL.md, «Протесты».</summary>
public sealed class ProtestSystem : ISimSystem
{
    public string Name => "Протесты";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var politics = world.Politics ?? throw new InvalidOperationException("Политика не создана: вызовите PoliticsModel.Setup");
        foreach (var country in world.Countries)
            ProtestModel.Update(world, country, politics[country.Id]);
    }
}
