namespace Politeconomy.Sim.News;

/// <summary>
/// Ведущий телевыпуска страны: имя по культуре страны, пол и год рождения (для портрета-заглушки), с какого дня
/// ведёт и до какого (потом его сменит новый), номер (какой по счёту ведущий у страны; от него — портрет и случайность).
/// </summary>
public sealed record BroadcastAnchor(string Name, bool Female, int BirthYear, DateOnly Since, DateOnly Until, int Number)
{
    public int Age(DateOnly date) => date.Year - BirthYear;
}

/// <summary>
/// Сюжет выпуска: из какой новости ленты, о какой стране (null — мировые рынки), рубрика, тон, своя ли страна,
/// заголовок на экране, вступление ведущего перед сюжетом («Главное в стране», «Теперь — новости из-за рубежа», пусто)
/// и текст для чтения вслух (без знаков, которые синтезатор прочтёт неверно).
/// </summary>
public sealed record BroadcastStory(int NewsId, string? Country, string Topic, string Tone, bool Home, string Headline, string Lead, string Speech);

/// <summary>
/// Телевыпуск: номер, дата (понедельник), страна, для которой он вышел, название передачи, ведущий, первый ли это его
/// выпуск, сюжеты (сначала свои, потом мировые), приветствие и прощание. Весь текст — для чтения вслух. MODEL.md, «Телевыпуск».
/// </summary>
public sealed record Broadcast(int Id, DateOnly Date, string Country, string Show, BroadcastAnchor Anchor, bool Debut,
    IReadOnlyList<BroadcastStory> Stories, string Opening, string Closing)
{
    /// <summary>Весь выпуск по порядку — то, что прочтёт голос: приветствие, вступления и сюжеты, прощание.</summary>
    public IEnumerable<string> Script()
    {
        yield return Opening;
        foreach (var story in Stories)
        {
            if (story.Lead.Length > 0)
                yield return story.Lead;
            yield return story.Speech;
        }
        yield return Closing;
    }
}
