using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics.Systems;

/// <summary>Взгляды классов за месяц понемногу идут к тем, что диктуют удовлетворённость и радикализм. MODEL.md, «Взгляды».</summary>
public sealed class ViewsSystem : ISimSystem
{
    public string Name => "Взгляды классов";
    public SimPhase Phase => SimPhase.Politics;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        var politics = world.Politics ?? throw new InvalidOperationException("Политика не создана: вызовите PoliticsModel.Setup");
        var b = world.Data.PoliticsBalance;
        var classes = world.Data.Classes.Classes;
        foreach (var country in world.Countries)
        {
            var p = politics[country.Id];
            var s = world.Society![country.Id];
            for (var k = 0; k < p.Views.Length && k < classes.Count; k++)
            {
                var target = PoliticalViews.Target(classes[k], p.Offset, s.Satisfaction[k], s.Radicalism[k], b);
                for (var a = 0; a < PoliticalViews.Axes; a++)
                    p.Views[k][a] += (target[a] - p.Views[k][a]) / b.ViewMonths;
            }
        }
    }
}
