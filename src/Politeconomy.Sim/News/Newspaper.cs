namespace Politeconomy.Sim.News;

/// <summary>
/// Материал газеты: из какой новости (0 — написан редакцией), вид (news — новость ленты, praise — похвала власти,
/// expose — разоблачение приукрашенного доклада, quiet — месяц без новостей), страна, рубрика, тон, заголовок и абзац.
/// </summary>
public sealed record PaperStory(int NewsId, string Kind, string? Country, string Topic, string Tone, string Headline, string Body);

/// <summary>
/// Номер газеты: номер, дата (1-е число), страна, тон издания (free, loyal, state), название и девиз, заголовок месяца
/// и материалы первой полосы. MODEL.md, «Газета».
/// </summary>
public sealed record Newspaper(int Id, DateOnly Date, string Country, string Press, string Name, string Motto, PaperStory Lead,
    IReadOnlyList<PaperStory> Stories);
