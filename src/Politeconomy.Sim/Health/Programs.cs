using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Health;

/// <summary>Программы ведомств страны: какие работают сейчас и какие работали на старте (поправки считаются от старта).</summary>
public sealed class CountryPrograms
{
    public HashSet<string> Current { get; set; } = new(StringComparer.Ordinal);
    public HashSet<string> Start { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Поправки к модели: эффекты включённых программ минус эффекты стартовых; цена — в расходы на статью ведомства.</summary>
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
    public int Version { get; set; }
}

public sealed class WorldPrograms
{
    public Dictionary<string, CountryPrograms> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryPrograms this[string country] => Countries[country];
}

/// <summary>
/// Программы ведомств (data/programs.json): включаются и выключаются сразу, без парламента. Цена идёт в расходы
/// бюджета на статью ведомства, эффекты — в поправки модели, как у законов. MODEL.md, «Программы».
/// </summary>
public static class ProgramModel
{
    public static void Setup(World world)
    {
        var programs = new WorldPrograms();
        foreach (var country in world.Countries)
        {
            var income = Math.Clamp((int)country.Income, 0, 3);
            var c = new CountryPrograms();
            foreach (var p in world.Data.Programs.Programs.Where(p => income < p.Start.Count && p.Start[income]))
            {
                c.Current.Add(p.Id);
                c.Start.Add(p.Id);
            }
            programs.Countries[country.Id] = c;
        }
        world.Programs = programs;
    }

    /// <summary>
    /// Пересчитать поправки: для каждой программы, которая включена сейчас, но не на старте, — её эффекты и цена;
    /// выключенная стартовая — с обратным знаком. Цена — в «spending.&lt;статья ведомства&gt;» (% ВВП).
    /// </summary>
    public static void Recompute(World world, string countryId)
    {
        var data = world.Data;
        var c = world.Programs![countryId];
        c.Effects.Clear();
        foreach (var p in data.Programs.Programs)
        {
            var sign = (c.Current.Contains(p.Id) ? 1 : 0) - (c.Start.Contains(p.Id) ? 1 : 0);
            if (sign == 0)
                continue;
            // Без врачей сложная программа почти бесполезна: эффект × (врачи / MinDoctors), не больше 1.
            var doctors = world.Health?.Countries.GetValueOrDefault(countryId)?.Doctors ?? p.MinDoctors;
            var efficacy = p.MinDoctors > 0 ? Math.Clamp(doctors / p.MinDoctors, 0, 1) : 1;
            foreach (var (key, value) in p.Effects)
                c.Effects[key] = c.Effects.GetValueOrDefault(key) + sign * value * efficacy;
            var item = data.Government.Posts.First(x => x.Id == p.Post).Spending.FirstOrDefault();
            if (item != null)
                c.Effects["spending." + item] = c.Effects.GetValueOrDefault("spending." + item) + sign * p.Cost;
        }
        c.Version++;
    }

    /// <summary>Включить или выключить программу (игрок); запись в журнал.</summary>
    public static bool Toggle(World world, string countryId, string programId)
    {
        if (world.PlayerCountry != countryId || world.Programs?.Countries.GetValueOrDefault(countryId) is not { } c
            || world.Data.Programs.Programs.FirstOrDefault(p => p.Id == programId) is not { } program)
            return false;
        var on = c.Current.Add(programId);
        if (!on)
            c.Current.Remove(programId);
        Recompute(world, countryId);
        world.Log(countryId, "program", on
            ? $"Запущена программа «{program.Name}»: {Text.Fmt.Number(program.Cost, 2)}% ВВП в год из бюджета."
            : $"Программа «{program.Name}» закрыта: бюджет сэкономит {Text.Fmt.Number(program.Cost, 2)}% ВВП в год.");
        return true;
    }
}
