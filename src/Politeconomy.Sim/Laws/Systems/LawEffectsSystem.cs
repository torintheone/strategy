using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Laws.Systems;

/// <summary>
/// В начале месяца пересчитывает поправки законов к модели: переходные периоды продвигаются, закончившиеся
/// убираются. Остальные системы читают готовые поправки (LawModel.Effect). MODEL.md, «Законы».
/// </summary>
public sealed class LawEffectsSystem : ISimSystem
{
    public string Name => "Действие законов";
    public SimPhase Phase => SimPhase.Laws;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var laws = world.Laws ?? throw new InvalidOperationException("Законы не созданы: вызовите LawModel.Setup");
        foreach (var c in laws.Countries.Values)
            if (c.Transitions.Count > 0)
            {
                LawModel.Recompute(world.Data, c, world.Date);
                c.Version++;
            }
    }
}
