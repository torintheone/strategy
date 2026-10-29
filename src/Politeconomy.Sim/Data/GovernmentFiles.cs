namespace Politeconomy.Sim.Data;

// Правительство и имена персонажей. Поля описаны в data/README.md.

public sealed record GovernmentFile(
    IReadOnlyList<SystemRecord> Systems,
    IReadOnlyDictionary<string, string> LeaderTitles,
    IReadOnlyList<PostRecord> Posts,
    IReadOnlyList<TraitRecord> Traits,
    IReadOnlyDictionary<string, int> ParliamentSeats);

/// <summary>Тип политической системы: как называется глава и какой у страны парламент (real, formal, none).</summary>
public sealed record SystemRecord(string Id, string Leader, string Parliament, string ParliamentName);

/// <summary>Пост в правительстве и его ведомство. Spending — статьи бюджета ведомства.</summary>
public sealed record PostRecord(
    string Id,
    string Name,
    string Ministry,
    string Short,
    IReadOnlyList<string> Spending,
    bool Security = false,
    bool Finance = false);

/// <summary>Черта персонажа: прибавки к его качествам и как часто она встречается.</summary>
public sealed record TraitRecord(
    string Id,
    string Name,
    string Description,
    double Competence = 0,
    double Loyalty = 0,
    double Corruption = 0,
    double Popularity = 0,
    double Weight = 1);

/// <summary>
/// Культура имён: страны, мужские и женские имена, фамилии. Order — given_family («Иван Петров») или family_given
/// («Ван Лэй»). Feminine — как фамилия меняется для женщин: пары «окончание — замена», берётся самое длинное подходящее.
/// </summary>
public sealed record CultureRecord(
    string Id,
    string Name,
    string Order,
    IReadOnlyList<string> Countries,
    IReadOnlyList<string> Male,
    IReadOnlyList<string> Female,
    IReadOnlyList<string> Surnames,
    IReadOnlyList<IReadOnlyList<string>> Feminine);
