using Politeconomy.Sim.Data;
using Politeconomy.Sim.Events;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.News;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;

namespace Politeconomy.Sim.Tests;

public class NewsTests
{
    static World NewWorld(string? player = "FRA", ulong seed = 1)
    {
        var world = GameSetup.NewGame(TestData.Game, seed).Simulation.World;
        if (player != null)
            PoliticsActions.PlayAs(world, player);
        return world;
    }

    static void Set(World world, string country, string stat, double value) =>
        world[country].Stats[stat] = world[country].Stats[stat] with { Value = value };

    static List<NewsItem> About(World world, string country, string source) =>
        world.News!.Items.Where(i => i.Country == country && i.Source == source).ToList();

    [Fact]
    public void TemplatesCoverTheModel()
    {
        var news = TestData.Game.News;
        Assert.True(news.Indicators.Count >= 20);
        Assert.Equal(3, news.Prices.Count);
        // У каждой категории событий есть рубрика, у каждой формулировки — текст.
        Assert.All(TestData.Game.Events.Categories, c => Assert.True(news.Events.ContainsKey(c.Id)));
        Assert.All(news.Indicators.SelectMany(i => (i.Up ?? []).Concat(i.Down ?? [])), p => Assert.False(string.IsNullOrWhiteSpace(p.Text)));
        Assert.Equal("Безработица выросла до 8,4%", NewsModel.Fill("Безработица выросла до {value}", "percent", 8.4, 7.9));
        // Между числом и «ВВП» — неразрывный пробел, как во всех числах игры.
        var gdp = Text.Fmt.Nbsp + "ВВП";
        Assert.Equal($"Дефицит 4,6%{gdp}, было −3,0%{gdp}, сдвиг 1,6%{gdp}", NewsModel.Fill("Дефицит {abs}, было {was}, сдвиг {change}", "percent_gdp_signed", -4.6, -3));
    }

    [Fact]
    public void IndicatorNewsFollowsTheModel()
    {
        var world = NewWorld();
        // Первый месяц только запоминает показатели.
        NewsModel.Month(world);
        Assert.Empty(world.News!.Items);
        var start = world["FRA"].Stat("unemployment_pct");
        // Показатель не изменился или сдвинулся чуть-чуть — новости нет.
        Set(world, "FRA", "unemployment_pct", start + 0.2);
        NewsModel.Month(world);
        Assert.Empty(About(world, "FRA", "stat:unemployment_pct"));
        // Вырос заметно — новость с новым значением, дома.
        Set(world, "FRA", "unemployment_pct", start + 1.04);
        NewsModel.Month(world);
        var up = Assert.Single(About(world, "FRA", "stat:unemployment_pct"));
        Assert.Equal($"Безработица выросла до {Text.Fmt.Value("percent", start + 1.04)}", up.Text);
        Assert.True(up.Home);
        Assert.Equal("bad", up.Tone);
        Assert.Equal("economy", up.Topic);
        // Об одном и том же дважды не сообщают.
        NewsModel.Month(world);
        Assert.Single(About(world, "FRA", "stat:unemployment_pct"));
        // Упал от того значения, о котором сообщали, — новость о снижении.
        Set(world, "FRA", "unemployment_pct", start);
        NewsModel.Month(world);
        var down = About(world, "FRA", "stat:unemployment_pct").Last();
        Assert.StartsWith("Безработица снизилась до", down.Text);
        Assert.Equal("good", down.Tone);
        // Формулировка зависит от значения: спад — не «замедление».
        Set(world, "FRA", "gdp_growth_pct", -2.5);
        NewsModel.Month(world);
        Assert.StartsWith("Экономика в рецессии", About(world, "FRA", "stat:gdp_growth_pct").Last().Text);
    }

    [Fact]
    public void RoundedIndicatorsDoNotFlicker()
    {
        var world = NewWorld();
        Set(world, "FRA", "credit_rating", 16.4);
        NewsModel.Month(world);
        // Рейтинг колеблется у границы ступени — новости нет.
        foreach (var value in new[] { 16.6, 16.4, 16.7, 16.3 })
        {
            Set(world, "FRA", "credit_rating", value);
            NewsModel.Month(world);
        }
        Assert.Empty(About(world, "FRA", "stat:credit_rating"));
        Set(world, "FRA", "credit_rating", 15.2);
        NewsModel.Month(world);
        Assert.Equal("Кредитный рейтинг страны понижен до A−", Assert.Single(About(world, "FRA", "stat:credit_rating")).Text);
        Set(world, "FRA", "credit_rating", 15.6);
        NewsModel.Month(world);
        Assert.Single(About(world, "FRA", "stat:credit_rating"));
    }

    [Fact]
    public void WorldPricesMakeNews()
    {
        var world = NewWorld();
        NewsModel.Month(world);
        world.Economy!.OilPrice *= 1.3;
        NewsModel.Month(world);
        var oil = Assert.Single(world.News!.Items, i => i.Source == "price:oil_price");
        Assert.Null(oil.Country);
        Assert.False(oil.Home);
        Assert.Equal("Нефть и газ дорожают: мировые цены выросли на 30%", oil.Text);
    }

    [Fact]
    public void ForeignNewsIsFilteredByImportance()
    {
        var world = NewWorld();
        var b = TestData.Game.NewsBalance;
        // Рядовая новость далёкой малой страны в ленту не идёт, такая же у державы и у соседа — идёт.
        Assert.Null(NewsModel.Publish(world, "TUV", "economy", "Безработица выросла", 40));
        var usa = NewsModel.Publish(world, "USA", "economy", "Безработица выросла", 40);
        Assert.NotNull(usa);
        Assert.False(usa.Home);
        Assert.True(usa.Weight >= b.WorldMin);
        Assert.NotNull(NewsModel.Publish(world, "BEL", "politics", "Выборы", 50));
        Assert.Null(NewsModel.Publish(world, "LSO", "politics", "Выборы", 50));
        // Очень важное — из любой страны.
        Assert.NotNull(NewsModel.Publish(world, "TUV", "war", "Война", 90));
        // О своей стране — всё, и оно важнее чужого.
        var home = NewsModel.Publish(world, "FRA", "politics", "Министр ушёл в отставку.", 10);
        Assert.NotNull(home);
        Assert.True(home.Home);
        Assert.Equal(10 + b.HomeBonus, home.Weight);
        Assert.Equal("Министр ушёл в отставку", home.Text);
        // Новость, где своя страна — вторая сторона, тоже «дома».
        Assert.True(NewsModel.Publish(world, "TUV", "diplomacy", "Введены санкции", 10, partner: "FRA")!.Home);
        Assert.Equal(2, world.News!.Counts["skipped"]);
        // Наблюдатель без своей страны видит только мировые новости.
        var observer = NewWorld(player: null);
        Assert.False(NewsModel.Publish(observer, "USA", "economy", "Безработица выросла", 40)!.Home);
        Assert.Null(NewsModel.Publish(observer, "BEL", "politics", "Отставка", 20));
    }

    [Fact]
    public void JournalAndEventsReachTheFeed()
    {
        var world = NewWorld("BRA");
        // Запись журнала с источником из news.json — новость; подсказки игроку и служебные записи — нет.
        world.Log("BRA", "law", "Парламент принял закон «Минимальная зарплата: высокая»: за 300, против 200.");
        world.Log("BRA", "bill", "Начата подготовка закона.");
        world.Log("BRA", "election", "Выборы прошли.", NewsModel.None);
        world.Log("BRA", "budget", "Министерство финансов внесло проект бюджета на 2027 год. Парламент проголосует 1 октября.");
        Assert.Equal(["law", "budget"], world.News!.Items.Select(i => i.Source));
        Assert.Equal("Министерство финансов внесло проект бюджета на 2027 год", world.News.Items[1].Text);
        Assert.Equal(4, world.Journal.Count(j => j.Country == "BRA" && j.Date == world.Date && j.Kind is "law" or "bill" or "election" or "budget"));
        // Событие у державы: правительство решает само, новость — что случилось и что решили.
        var record = TestData.Game.Events.Event("earthquake")!;
        var instance = EventModel.Fire(world, world["JPN"], record)!;
        var quake = Assert.Single(world.News.Items, i => i.Source == "event:earthquake");
        Assert.Equal("JPN", quake.Country);
        Assert.Equal("disaster", quake.Topic);
        Assert.Equal(instance.Region, quake.Region);
        Assert.StartsWith("Землетрясение: ", quake.Text);
        Assert.Contains(". Решение: ", quake.Text);
        Assert.DoesNotContain("{", quake.Text);
        // Своё событие попадает в ленту, когда игрок решил.
        var mine = EventModel.Fire(world, world["BRA"], TestData.Game.Events.Event("record_harvest")!)!;
        Assert.DoesNotContain(world.News.Items, i => i.Source == "event:record_harvest");
        EventModel.Decide(world, "BRA", mine.Id, TestData.Game.Events.Event("record_harvest")!.Options[0].Id);
        Assert.True(Assert.Single(world.News.Items, i => i.Source == "event:record_harvest").Home);
    }

    [Fact]
    public void FeedKeepsTheLatestOfEachKind()
    {
        var world = NewWorld();
        var b = TestData.Game.NewsBalance;
        for (var k = 0; k < b.HomeKeep + 30; k++)
            NewsModel.Publish(world, "FRA", "economy", $"Дома {k}", 50);
        for (var k = 0; k < b.WorldKeep + 30; k++)
            NewsModel.Publish(world, "USA", "economy", $"В мире {k}", 90);
        var items = world.News!.Items;
        Assert.Equal(b.HomeKeep, items.Count(i => i.Home));
        Assert.Equal(b.WorldKeep, items.Count(i => !i.Home));
        // Поток чужих новостей не вытесняет свои, остаются последние.
        Assert.Equal("Дома 30", items.First(i => i.Home).Text);
        Assert.Equal($"В мире {b.WorldKeep + 29}", items[^1].Text);
        Assert.Equal(items.Select(i => i.Id).Order(), items.Select(i => i.Id));
    }

    [Fact]
    public void YearOfNewsAtHomeAndAbroadSurvivesSaving()
    {
        var sim = GameSetup.NewGame(TestData.Game, 3).Simulation;
        var world = sim.World;
        PoliticsActions.PlayAs(world, "IND");
        var seen = new Dictionary<int, NewsItem>();
        var end = world.Date.AddYears(1);
        while (world.Date < end)
        {
            sim.RunUntil(world.Date.AddDays(7) < end ? world.Date.AddDays(7) : end);
            foreach (var pending in world.Events!["IND"].Pending.ToList())
                EventModel.Decide(world, "IND", pending.Id, EventModel.DefaultOption(world, world["IND"], TestData.Game.Events.Event(pending.Event)!).Id);
            foreach (var item in world.News!.Items)
                seen[item.Id] = item;
        }
        var home = seen.Values.Count(i => i.Home);
        var abroad = seen.Values.Count(i => !i.Home);
        Assert.InRange(home, 5, 120);
        Assert.InRange(abroad, 30, 400);
        Assert.All(seen.Values, i =>
        {
            Assert.NotNull(TestData.Game.News.Topic(i.Topic));
            Assert.DoesNotContain("{", i.Text);
            Assert.False(string.IsNullOrWhiteSpace(i.Text));
            Assert.True(i.Country == null || world.ById(i.Country) != null);
        });
        // В ленте есть и показатели, и то, что случается в странах.
        Assert.Contains(seen.Values, i => i.Source.StartsWith("stat:"));
        Assert.Contains(seen.Values, i => i.Source.StartsWith("event:"));
        Assert.Contains(seen.Values, i => i.Source == "election");
        // Сохранение и загрузка: лента та же, партия идёт так же.
        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        Assert.Equal(world.News!.Items, loaded.World.News!.Items);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded.World));
        sim.RunUntil(world.Date.AddMonths(2));
        loaded.RunUntil(loaded.World.Date.AddMonths(2));
        Assert.Equal(world.News.Items, loaded.World.News.Items);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded.World));
    }
}
