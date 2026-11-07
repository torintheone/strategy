using Politeconomy.Sim.Ai;
using Politeconomy.Sim.Diplomacy;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Geo;
using Politeconomy.Sim.Military;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Views;

namespace Politeconomy.Sim.Tests;

public class WarTests
{
    static readonly WorldMap Map = WorldMap.From(TestData.Game.Map);

    static World NewWorld() => GameSetup.NewGame(TestData.Game, 1).Simulation.World;

    /// <summary>Россия нарушает перемирие с Украиной: до года боёв, пока не взят хотя бы один спорный регион.</summary>
    static (Simulation Sim, War War) RussiaAttacksUkraine()
    {
        var sim = TestData.WithoutAi(3);
        var world = sim.World;
        var war = WarModel.Declare(world, "RUS", "UKR");
        Assert.NotNull(war);
        for (var month = 0; month < 18 && !war!.Goal.Any(r => world.RegionById(r)!.Controller == "RUS") && world.War!.Wars.Contains(war); month++)
            sim.RunUntil(world.Date.AddMonths(1));
        return (sim, war!);
    }

    [Fact]
    public void ArmiesAndStrengthAtStart()
    {
        var world = NewWorld();
        var w = world.War!;
        Assert.Equal(world.Countries.Count, w.Armies.Count);
        // У Исландии армии нет; у остальных есть солдаты и сила.
        Assert.Equal(0, w.Armies["ISL"].Soldiers);
        Assert.All(w.Armies.Where(kv => kv.Key != "ISL"), kv => Assert.True(kv.Value.Soldiers > 0, kv.Key));
        Assert.True(w.Armies.Count(kv => WarModel.Strength(world, kv.Key) > 0) > 0.95 * w.Armies.Count);
        double S(string id) => WarModel.Strength(world, id);
        Assert.True(S("USA") > S("CHN") && S("CHN") > S("DEU") && S("RUS") > S("POL") && S("POL") > S("EST"));
        Assert.Equal(880000, w.Armies["UKR"].Soldiers);
        // На старте — перемирие России и Украины, войн нет.
        Assert.Empty(w.Wars);
        Assert.NotNull(WarModel.CeasefireBetween(world, "UKR", "RUS"));
        Assert.Contains("military_strength", world["USA"].Stats.Keys);
    }

    [Fact]
    public void DeclaringNeedsBorderAndBreaksTruce()
    {
        var world = NewWorld();
        Assert.False(WarModel.CanDeclare(world, "FRA", "JPN").Allowed);
        Assert.False(WarModel.CanDeclare(world, "FRA", "DEU").Allowed); // союзники по НАТО
        Assert.True(WarModel.CanDeclare(world, "RUS", "UKR").Allowed);
        var before = DiplomacyModel.Relations(world, "RUS", "BRA");
        var war = WarModel.Declare(world, "RUS", "UKR")!;
        Assert.Null(WarModel.CeasefireBetween(world, "RUS", "UKR"));
        Assert.Contains(war.Goal, r => world.RegionById(r)!.Owner == "UKR");
        Assert.Contains("нарушив перемирие", war.News[0].Text);
        Assert.True(DiplomacyModel.Relations(world, "RUS", "BRA") < before);
        Assert.False(WarModel.CanDeclare(world, "RUS", "UKR").Allowed);
    }

    [Fact]
    public void BlocAlliesJoinTheDefender()
    {
        var world = NewWorld();
        var war = WarModel.Declare(world, "RUS", "EST")!;
        Assert.Equal("EST", war.Defender);
        Assert.Contains("USA", war.Defenders);
        Assert.Contains("DEU", war.Defenders);
        Assert.DoesNotContain("BLR", war.Defenders);
        Assert.True(WarModel.SideStrength(world, war, 2) > WarModel.SideStrength(world, war, 1));
    }

    [Fact]
    public void CapturedRegionChangesControllerAndColor()
    {
        var (sim, war) = RussiaAttacksUkraine();
        var world = sim.World;
        var taken = war.Goal.Where(r => world.RegionById(r)!.Controller == "RUS").ToList();
        Assert.NotEmpty(taken);
        var region = world.RegionById(taken[0])!;
        Assert.Equal("UKR", region.Owner);
        Assert.Contains(war.News, n => n.Text.StartsWith("Взят", StringComparison.Ordinal));
        Assert.True(war.AttackerLosses > 0 && war.DefenderLosses > 0);
        Assert.True(world.War!.Armies["UKR"].Exhaustion > 0);
        // На политической карте — цвет занявшей страны, в режиме «Войны» — цвет занятого.
        var snapshot = WorldSnapshot.Capture(world, 1, true, 0);
        var data = TestData.Game;
        var political = new MapModeView(data, Map, data.MapModes.Modes.First(m => m.Kind == "political"));
        Assert.Equal(political.PoliticalColor("RUS"), political.ColorOf(Map.Region(region.Id)!, snapshot));
        var wars = new MapModeView(data, Map, data.MapModes.Modes.First(m => m.Kind == "war"));
        Assert.Equal("#8e6bb8", wars.ColorOf(Map.Region(region.Id)!, snapshot)); // «занято другой страной»
        Assert.NotEqual(wars.ColorOf(Map.Region(region.Id)!, snapshot), wars.ColorOf(Map.Region(world.Regions.First(r => r.Owner == "BRA").Id)!, snapshot));
        // Занятый регион кормит экономику занявшего, а не владельца.
        Assert.True(WarModel.Output(world, "UKR") < 1);
    }

    [Fact]
    public void WarAndOccupationSurviveSaving()
    {
        var (sim, war) = RussiaAttacksUkraine();
        var world = sim.World;
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = SaveGame.Read(TestData.Game, stream);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
        Assert.Equal(world.War!.Wars.Count, loaded.War!.Wars.Count);
        Assert.All(war.Goal, r => Assert.Equal(world.RegionById(r)!.Controller, loaded.RegionById(r)!.Controller));
        // После загрузки война идёт так же, как без неё.
        var resumed = new Simulation(loaded, GameSetup.Systems().Where(s => s is not Ai.Systems.AiSystem));
        resumed.RunUntil(world.Date.AddMonths(2));
        sim.RunUntil(world.Date.AddMonths(2));
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded));
    }

    [Fact]
    public void CeasefireFreezesTheFrontLine()
    {
        var (sim, war) = RussiaAttacksUkraine();
        var world = sim.World;
        PoliticsActions.PlayAs(world, "UKR");
        var held = world.Regions.Where(r => r.Owner == "UKR" && r.Controller == "RUS").Select(r => r.Id).ToList();
        world.War!.Armies["RUS"].Exhaustion = 50;
        Assert.True(WarModel.ProposeCeasefire(world, "UKR", war.Id));
        Assert.Empty(world.War.Wars);
        Assert.NotNull(WarModel.CeasefireBetween(world, "RUS", "UKR"));
        Assert.Contains(world.War.History, h => h.Id == war.Id && h.Attacker == "RUS" && h.End == world.Date);
        Assert.Contains(world.Journal, j => j.Kind == "war");
        // Линия фронта заморожена: занятое остаётся занятым, боёв нет.
        sim.RunUntil(world.Date.AddMonths(3));
        Assert.All(held, r => Assert.Equal("RUS", world.RegionById(r)!.Controller));
        Assert.Empty(world.War.Wars);
    }

    [Fact]
    public void AiAttacksOnlyWithAdvantageAndExplainsWhy()
    {
        var world = GameSetup.NewGame(TestData.Game, 9).Simulation.World;
        var country = world["VEN"];
        var ai = world.Ai!["VEN"];
        ai.Priorities = [0.1, 0.1, 0.7, 0.1];
        void Try(int times)
        {
            for (var i = 0; i < times && !WarModel.AtWar(world, "VEN"); i++)
            {
                ai.Last.Remove("war");
                AiWar.Consider(new AiTurn(world, country, ai));
            }
        }
        // На старте у Венесуэлы нет нужного перевеса над Гайаной с её друзьями.
        Try(40);
        Assert.False(WarModel.AtWar(world, "VEN"));
        // Армия намного сильнее — нападает за спорные регионы и пишет почему.
        world.War!.Strength["VEN"] *= 1000;
        Try(200);
        var war = Assert.Single(world.War.Wars);
        Assert.Equal(("VEN", "GUY"), (war.Attacker, war.Defender));
        var decision = ai.Decisions.Last(d => d.Kind == "war");
        Assert.Contains("Гайана", decision.Text);
        Assert.Contains("перевес сил", decision.Why);
        Assert.Contains("спорные регионы", decision.Why);
    }
}
