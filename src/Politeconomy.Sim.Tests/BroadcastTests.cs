using Politeconomy.Sim.Events;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.News;
using Politeconomy.Sim.Politics;
using Politeconomy.Sim.Save;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Tests;

public class BroadcastTests
{
    static World NewWorld(string? player = "FRA", ulong seed = 1)
    {
        var world = GameSetup.NewGame(TestData.Game, seed).Simulation.World;
        if (player != null)
            PoliticsActions.PlayAs(world, player);
        world.News = new WorldNews();
        return world;
    }

    static NewsItem Publish(World world, string? country, int weight, string source, string text, bool? home = null) =>
        NewsModel.Publish(world, country, "politics", text, weight, home: home, source: source)!;

    [Fact]
    public void TextIsWrittenForTheSpeechSynthesizer()
    {
        var nb = Fmt.Nbsp;
        Assert.Equal("Безработица выросла до 8,4 процента.", Speech.Say("Безработица выросла до 8,4%"));
        Assert.Equal("Рост минус 2,1 процента, было плюс 1 процент.", Speech.Say($"Рост {Fmt.Signed(-2.1, 1)}%, было +1%"));
        Assert.Equal("Цены выросли на 12 процентов, потом на 22 процента.", Speech.Say("Цены выросли на 12%, потом на 22%."));
        Assert.Equal("ВВП 1,2 миллиарда долларов, 5 миллиардов долларов, 12500 долларов.",
            Speech.Say($"ВВП 1,2{nb}млрд{nb}$, 5{nb}млрд{nb}$, 12{nb}500{nb}$"));
        // После «до», «от»… — родительный падеж: «до 21 миллиона», «до 5 процентов»; после «на» — как обычно.
        Assert.Equal("Население выросло до 21 миллиона, потом до 22 миллионов человек.", Speech.Say($"Население выросло до 21{nb}млн, потом до 22{nb}млн человек"));
        Assert.Equal("Безработица выросла до 5 процентов, цены — на 22 процента, население — 21 миллион.", Speech.Say($"Безработица выросла до 5%, цены — на 22%, население — 21{nb}млн"));
        Assert.Equal("Кредитный рейтинг страны понижен до BBB минус.", Speech.Say("Кредитный рейтинг страны понижен до BBB−"));
        Assert.Equal("Дефицит 4,6 процента ВВП.", Speech.Say($"Дефицит 4,6%{nb}ВВП"));
        Assert.Equal("В Совет безопасности на 2027 — 2028 годы.", Speech.Say("В Совет безопасности на 2027–2028 годы"));
        // Кавычки голос не читает — они остаются.
        Assert.Equal("Закон «Пенсии».", Speech.Say("Закон «Пенсии»"));
        // Ни одного знака, который синтезатор прочтёт неверно.
        var all = Speech.Say($"−3,0%{nb}ВВП, +12%, 7{nb}млрд{nb}$, AA+");
        Assert.DoesNotContain('%', all);
        Assert.DoesNotContain('$', all);
        Assert.DoesNotContain(Fmt.Minus, all);
        Assert.DoesNotContain(nb, all);
    }

    [Fact]
    public void StoriesAreTheMostImportantNewsHomeFirst()
    {
        var world = NewWorld();
        var tv = TestData.Game.News.Broadcast!;
        Publish(world, "FRA", 20, "law", "Парламент принял закон «Пенсии»");
        Publish(world, "FRA", 60, "election", "Выборы в парламент");
        Publish(world, "DEU", 80, "war", "Война");
        Publish(world, "USA", 90, "election", "Выборы президента");
        Publish(world, "USA", 85, "default", "Дефолт");
        Publish(world, "CHN", 50, "election", "Выборы в Китае");
        Publish(world, null, 40, "price:oil_price", "Нефть дорожает: мировые цены выросли на 12%");
        var b = BroadcastModel.Air(world, "FRA")!;
        Assert.NotNull(b);
        Assert.Equal(world.Date, b.Date);
        var stories = b.Stories;
        Assert.InRange(stories.Count, 3, 5);
        // Сначала своё, по важности; одна выборная новость на выпуск (своя важнее чужой), одна страна — один мировой сюжет.
        Assert.True(stories[0].Home);
        Assert.Equal("Выборы в парламент", stories[0].Headline);
        Assert.Equal(Speech.Say(tv.Home), stories[0].Lead);
        Assert.Equal(stories.OrderByDescending(s => s.Home).ToList(), stories);
        Assert.Single(stories, s => s.Country == "USA");
        Assert.DoesNotContain(stories, s => s.Headline == "Выборы президента" || s.Headline == "Выборы в Китае");
        Assert.Contains(stories, s => s.Headline == "Война");
        var first = stories.First(s => !s.Home);
        Assert.Equal(Speech.Say(tv.World), first.Lead);
        Assert.StartsWith(world["DEU"].Name + ". ", stories.First(s => s.Country == "DEU").Speech);
        // Новость о мировых рынках подписана «Мировые рынки», а не страной.
        if (stories.FirstOrDefault(s => s.Country == null) is { } market)
            Assert.StartsWith(tv.Markets + ". ", market.Speech);
        // Весь выпуск — приветствие с именем ведущего, сюжеты, прощание; без знаков, которые голос не прочтёт.
        var script = b.Script().ToList();
        Assert.Contains(b.Anchor.Name, script[0]);
        Assert.Contains("«" + tv.Show + "»", script[0]);
        Assert.Equal(b.Closing, script[^1]);
        Assert.All(script, line => Assert.True(line.IndexOfAny(['%', '$', Fmt.Minus, Fmt.Nbsp, '{']) < 0, line));
        // Через неделю те же новости не повторяются.
        world.Date = world.Date.AddDays(7);
        var next = BroadcastModel.Air(world, "FRA");
        Assert.True(next == null || next.Stories.All(s => s.NewsId == 0 || stories.All(o => o.NewsId != s.NewsId)));
    }

    [Fact]
    public void QuietWeekGetsTheCountryFigures()
    {
        var world = NewWorld();
        var tv = TestData.Game.News.Broadcast!;
        // За две недели ничего не случилось — выпуска нет.
        Assert.Null(BroadcastModel.Air(world, "FRA"));
        Assert.Empty(world.News!.Broadcasts);
        // Одна мировая новость: дома тихо, ведущий называет главные цифры страны.
        Publish(world, "USA", 90, "election", "Выборы президента");
        var b = BroadcastModel.Air(world, "FRA")!;
        Assert.Equal(2, b.Stories.Count);
        var figures = b.Stories[0];
        Assert.True(figures.Home);
        Assert.Equal(0, figures.NewsId);
        Assert.StartsWith(tv.Figures.Title, figures.Headline);
        Assert.Contains(tv.Figures.Stats[0].Label, figures.Headline);
        Assert.Equal(Speech.Say(tv.HomeQuiet + " " + tv.Figures.Lead), figures.Lead);
        Assert.Equal(Speech.Say(tv.World), b.Stories[1].Lead);
        // Старая новость теряет важность: свежая пятидесятка обходит месячной давности девяностку.
        world.Date = world.Date.AddDays(20);
        Publish(world, "DEU", 55, "law", "Закон");
        world.Date = world.Date.AddDays(1);
        Publish(world, "ITA", 50, "default", "Дефолт");
        var stale = world.News.Items[^2];
        world.News.Items[^2] = stale with { Date = stale.Date.AddDays(-20) };
        world.Date = world.Date.AddDays(6);
        var later = BroadcastModel.Air(world, "FRA")!;
        var titles = later.Stories.Where(s => !s.Home).Select(s => s.Headline).ToList();
        Assert.Equal(["Дефолт", "Закон"], titles);
    }

    [Fact]
    public void BroadcastAirsOnMondaysForThePlayerAndSurvivesSaving()
    {
        var sim = GameSetup.NewGame(TestData.Game, 2).Simulation;
        var world = sim.World;
        // Без страны игрока выпуска нет.
        sim.RunUntil(world.Date.AddDays(40));
        Assert.Empty(world.News!.Broadcasts);
        PoliticsActions.PlayAs(world, "BRA");
        var end = world.Date.AddMonths(5);
        var seen = new SortedDictionary<int, Broadcast>();
        while (world.Date < end)
        {
            sim.StepDay();
            foreach (var pending in world.Events!["BRA"].Pending.ToList())
                EventModel.Decide(world, "BRA", pending.Id, EventModel.DefaultOption(world, world["BRA"], TestData.Game.Events.Event(pending.Event)!).Id);
            foreach (var show in world.News.Broadcasts)
                seen[show.Id] = show;
        }
        var shows = seen.Values.ToList();
        var b = TestData.Game.BroadcastBalance;
        // Выпуск — почти каждую неделю; хранятся последние.
        Assert.InRange(shows.Count, 17, 22);
        Assert.Equal(b.Keep, world.News.Broadcasts.Count);
        Assert.All(shows, s =>
        {
            Assert.Equal(DayOfWeek.Monday, s.Date.DayOfWeek);
            Assert.Equal("BRA", s.Country);
            Assert.InRange(s.Stories.Count, 1, b.StoriesMax);
            Assert.Equal(s.Stories.OrderByDescending(x => x.Home).ToList(), s.Stories);
            Assert.Equal(s.Stories.Where(x => x.NewsId > 0).Count(), s.Stories.Where(x => x.NewsId > 0).Select(x => x.NewsId).Distinct().Count());
        });
        // Обычно в выпуске три сюжета и больше (в первые месяцы мировых новостей меньше).
        Assert.True(shows.Count(s => s.Stories.Count >= b.StoriesMin) >= shows.Count * 0.5);
        // Ведущий один и тот же.
        Assert.Single(shows.Select(s => s.Anchor).Distinct());
        Assert.False(shows[0].Debut);

        using var stream = new MemoryStream();
        SaveGame.Write(world, stream);
        stream.Position = 0;
        var loaded = GameSetup.Resume(SaveGame.Read(TestData.Game, stream));
        Assert.Equal(world.News.Broadcasts.Select(s => s.Script().ToList()), loaded.World.News!.Broadcasts.Select(s => s.Script().ToList()));
        Assert.Equal(world.News.Anchors, loaded.World.News.Anchors);
        Assert.Equal(SaveGame.Hash(world), SaveGame.Hash(loaded.World));
    }

    [Fact]
    public void AnchorIsLocalAndChangesRarely()
    {
        var world = NewWorld("JPN");
        Publish(world, "JPN", 60, "election", "Выборы");
        var first = BroadcastModel.Air(world, "JPN")!;
        var anchor = first.Anchor;
        var b = TestData.Game.BroadcastBalance;
        Assert.InRange(anchor.Age(world.Date), b.AnchorAge[0], b.AnchorAge[1]);
        Assert.InRange(anchor.Until.Year - anchor.Since.Year, b.AnchorYears[0], b.AnchorYears[1]);
        // Имя — по культуре страны.
        var culture = Government.CharacterFactory.CultureOf(TestData.Game, "JPN");
        Assert.Contains(culture.Surnames, s => anchor.Name.Contains(s));
        // Тот же мир с тем же зерном — тот же ведущий; общий генератор мира ведущий не трогает.
        var twin = NewWorld("JPN");
        var before = twin.Random.State.ToArray();
        Publish(twin, "JPN", 60, "election", "Выборы");
        Assert.Equal(anchor, BroadcastModel.Air(twin, "JPN")!.Anchor);
        Assert.Equal(before, twin.Random.State);
        // Пока срок не истёк — тот же ведущий; потом новый, и он представляется.
        world.Date = anchor.Until.AddDays(-1);
        Publish(world, "JPN", 60, "law", "Закон");
        Assert.Same(anchor, BroadcastModel.Air(world, "JPN")!.Anchor);
        world.Date = anchor.Until;
        Publish(world, "JPN", 60, "default", "Дефолт");
        var next = BroadcastModel.Air(world, "JPN")!;
        Assert.True(next.Debut);
        Assert.Equal(2, next.Anchor.Number);
        Assert.NotEqual(anchor.Name, next.Anchor.Name);
        Assert.EndsWith(Speech.Say(TestData.Game.News.Broadcast!.Debut), next.Opening);
        // У другой страны — свой ведущий.
        Publish(world, "DEU", 60, "election", "Wahl", home: true);
        Assert.NotEqual(next.Anchor.Name, BroadcastModel.Air(world, "DEU")!.Anchor.Name);
    }
}
