namespace Politeconomy.Sim.Government;

/// <summary>
/// Персонаж: глава власти, министр или кандидат. Качества — от 0 до 100; взгляды — по трём осям от −100 до 100
/// (экономическая: перераспределение − / рынок +; гражданская: свободы − / порядок +; внешняя: открытость − / суверенитет +).
/// Портрета пока нет: интерфейс рисует силуэт по полу и возрасту. Party — id партии (null — беспартийный).
/// </summary>
public sealed class Character
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool Female { get; set; }
    public int BirthYear { get; set; }
    public string Culture { get; set; } = "";
    public double Competence { get; set; }
    public double Loyalty { get; set; }
    public double Corruption { get; set; }
    public double Popularity { get; set; }
    public double[] Views { get; set; } = new double[3];
    public List<string> Traits { get; set; } = [];
    public string? Party { get; set; }
    /// <summary>Сколько сроков персонаж уже был главой (для ограничения сроков).</summary>
    public int Terms { get; set; }
    /// <summary>Когда занял нынешний пост.</summary>
    public DateOnly Since { get; set; }

    public int Age(DateOnly date) => date.Year - BirthYear;

    public bool Has(string trait) => Traits.Contains(trait);
}
