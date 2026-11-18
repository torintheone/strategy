using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Ai.Systems;

/// <summary>Ход ИИ всех стран без игрока, раз в месяц после политики. MODEL.md, «ИИ стран».</summary>
public sealed class AiSystem : ISimSystem
{
    public string Name => "ИИ стран";
    public SimPhase Phase => SimPhase.AiDecisions;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world) => AiModel.Month(world);
}
