namespace Politeconomy.Sim.Living;

/// <summary>
/// Жильё, преступность, коррупция и экология страны. Housing — м² на человека; Desired — сколько хотят при нынешних
/// доходах; Crime и Pollution — индексы 0–100. Mood — прибавка к удовлетворённости каждого класса от этих условий;
/// Effects — поправки к модели. MODEL.md, «Жильё, преступность, коррупция, экология».
/// </summary>
public sealed class CountryLiving
{
    public double Housing { get; set; }
    /// <summary>Весь жилой фонд, м² (площадь на человека = фонд / население).</summary>
    public double Stock { get; set; }
    public double Desired { get; set; }
    public double Housing0 { get; set; }
    /// <summary>Сколько м² строится в год на старте (чтобы площадь на человека держалась при росте населения и износе).</summary>
    public double Build0 { get; set; }
    public double Construction0 { get; set; }
    public double Crime { get; set; }
    public double Crime0 { get; set; }
    public double Pollution { get; set; }
    public double Pollution0 { get; set; }
    public double Industry0 { get; set; }
    /// <summary>Реальный доход на душу к началу модели (на старте партии).</summary>
    public double Income0 { get; set; } = 1;
    public double Corruption0 { get; set; }
    public double Administration0 { get; set; }
    public double Police0 { get; set; }
    public double Unemployment0 { get; set; }
    public double Living0 { get; set; }
    public double Youth0 { get; set; }
    public double[] Mood { get; set; } = [];
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
}

public sealed class WorldLiving
{
    public Dictionary<string, CountryLiving> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryLiving this[string country] => Countries[country];
}
