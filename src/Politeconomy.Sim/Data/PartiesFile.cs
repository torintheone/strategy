namespace Politeconomy.Sim.Data;

// Партии и оси взглядов. Поля описаны в data/README.md.

public sealed record PartiesFile(
    IReadOnlyList<AxisRecord> Axes,
    IReadOnlyList<PartyFamilyRecord> Families,
    PartyFamilyRecord Power,
    IReadOnlyList<PartyNounRecord> Nouns);

/// <summary>
/// Ось взглядов: что значит минус (Low) и плюс (High); вопрос на предвыборных дебатах по этой оси и три ответа —
/// к минусу, к середине, к плюсу.
/// </summary>
public sealed record AxisRecord(string Id, string Name, string Low, string High, string Debate = "", string LowAnswer = "",
    string CenterAnswer = "", string HighAnswer = "");

/// <summary>
/// Семейство партий: позиция на трёх осях для страны со средними взглядами, цвет, вес при выборе семейства
/// (ByIncome — множитель по группам дохода) и части названия: прилагательные в трёх родах, хвосты в родительном
/// падеже, названия в кавычках.
/// </summary>
public sealed record PartyFamilyRecord(
    string Id,
    string Name,
    IReadOnlyList<double> Position,
    string Color,
    double Weight,
    IReadOnlyList<IReadOnlyList<string>> Adjectives,
    IReadOnlyList<string> Tails,
    IReadOnlyList<string> Quoted,
    IReadOnlyList<double>? ByIncome = null);

/// <summary>Слово для названия партии («партия», «союз»…): род (m, f, n), вес и множители по частям света.</summary>
public sealed record PartyNounRecord(string Word, string Gender, double Weight, IReadOnlyDictionary<string, double>? Regions = null);
