namespace Politeconomy.Sim.Data;

public sealed record ClassesFile(IReadOnlyList<ClassRecord> Classes);

/// <summary>
/// Класс населения: название, источник дохода, интересы, организованность, спокойный радикализм, норма сбережений,
/// доля в доходах от собственности, страх безработицы и чувствительность к инфляции. Поля описаны в data/classes.json.
/// </summary>
public sealed record ClassRecord(
    string Id,
    string Name,
    string Income,
    IReadOnlyList<string> Interests,
    double Organization,
    double Radicalism,
    double Saving,
    double Property,
    double JobRisk,
    double Inflation,
    string Color,
    string? ShortName = null);
