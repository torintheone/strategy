using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Military.Systems;

/// <summary>Месяц армий и войн: армии, бои на каждом фронте, мирные переговоры и репарации, поправки к модели, показатели. MODEL.md, «Армии и войны».</summary>
public sealed class WarSystem : ISimSystem
{
    public string Name => "Армии и войны";
    public SimPhase Phase => SimPhase.Diplomacy;
    public SimFrequency Frequency => SimFrequency.Monthly;

    public void Run(World world)
    {
        WarModel.Ensure(world);
        WarModel.UpdateArmies(world);
        foreach (var war in world.War!.Wars.ToList())
            WarFront.Month(world, war);
        PeaceModel.Month(world);
        WarModel.Apply(world);
        WarModel.Publish(world);
    }
}
