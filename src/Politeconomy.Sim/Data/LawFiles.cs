namespace Politeconomy.Sim.Data;

// Законы из data/laws.json. Поля описаны в data/README.md, смысл эффектов — в MODEL.md, «Законы».

public sealed record LawsFile(
    IReadOnlyList<LawGroupRecord> Groups,
    IReadOnlyDictionary<string, EffectRecord> Effects,
    IReadOnlyList<LawRecord> Laws);

public sealed record LawGroupRecord(string Id, string Name);

/// <summary>Что означает эффект закона: название, единица, пояснение.</summary>
public sealed record EffectRecord(string Name, string Unit, string Description);

/// <summary>
/// Закон: группа, ведомство, которое его готовит (Post), сроки подготовки и перехода, нужно ли конституционное
/// большинство, варианты и правило выбора варианта на старте.
/// </summary>
public sealed record LawRecord(
    string Id,
    string Group,
    string Name,
    string Post,
    string Description,
    int DraftingMonths,
    int TransitionMonths,
    bool Constitutional,
    IReadOnlyList<LawVariantRecord> Variants,
    LawStartRecord Start)
{
    public LawVariantRecord Variant(string id) =>
        Variants.FirstOrDefault(v => v.Id == id) ?? throw new KeyNotFoundException($"У закона {Id} нет варианта {id}");
}

/// <summary>Вариант закона: эффекты (ключи из LawsFile.Effects) и отношение классов от −3 до +3.</summary>
public sealed record LawVariantRecord(
    string Id,
    string Name,
    string Description,
    IReadOnlyDictionary<string, double> Effects,
    IReadOnlyDictionary<string, double> Attitudes);

/// <summary>Вариант на старте: Default, затем по группе дохода, типу системы, режиму и стране — частное правило побеждает.</summary>
public sealed record LawStartRecord(
    string Default,
    IReadOnlyDictionary<string, string>? Income = null,
    IReadOnlyDictionary<string, string>? System = null,
    IReadOnlyDictionary<string, string>? Regime = null,
    IReadOnlyDictionary<string, string>? Countries = null);
