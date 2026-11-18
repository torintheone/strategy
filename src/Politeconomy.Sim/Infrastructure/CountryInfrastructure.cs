namespace Politeconomy.Sim.Infrastructure;

/// <summary>
/// Инфраструктура страны. Массивы — по видам data/infrastructure.json: мощность и нагрузка (100 — нагрузка на
/// старте), состояние (доля от нового), строящаяся мощность, обеспеченность (мощность × состояние / нагрузка, не
/// больше 1). Cut — на сколько перегрузка режет выпуск отраслей. Projects — стройки игрока, Loans — кредиты на них.
/// MODEL.md, «Инфраструктура и стройки».
/// </summary>
public sealed class CountryInfrastructure
{
    public double[] Capacity { get; set; } = [];
    public double[] Condition { get; set; } = [];
    public double[] Building { get; set; } = [];
    public double[] Need { get; set; } = [];
    public double[] Coverage { get; set; } = [];
    public double[] Growth { get; set; } = [];
    public double[] Funding { get; set; } = [];
    public double[] Business0 { get; set; } = [];
    public double Population0 { get; set; }
    public double Income0 { get; set; } = 1;
    public double Investment0 { get; set; }
    public double Corruption0 { get; set; }
    public Dictionary<string, double> Spending0 { get; set; } = new(StringComparer.Ordinal);
    public double[] Cut { get; set; } = [];
    public double[] Mood { get; set; } = [];
    public Dictionary<string, double> Effects { get; set; } = new(StringComparer.Ordinal);
    public List<Project> Projects { get; set; } = [];
    public List<Loan> Loans { get; set; } = [];
    public int NextProject { get; set; } = 1;
}

/// <summary>
/// Стройка: что строят (шаблон), как платят (budget — бюджет, loan — кредит, ppp — концессия), цена (% ВВП),
/// сроки, сколько добавит (мощность, состояние или ГВт станций). Concession — до какой даты люди платят за пользование.
/// </summary>
public sealed class Project
{
    public int Id { get; set; }
    public string Template { get; set; } = "";
    public string Name { get; set; } = "";
    public string Financing { get; set; } = "budget";
    public string? Lender { get; set; }
    public double Cost { get; set; }
    public DateOnly Started { get; set; }
    public DateOnly Ends { get; set; }
    public double Amount { get; set; }
    public bool Done { get; set; }
    public DateOnly? Concession { get; set; }
}

/// <summary>Кредит на стройку: кто дал, сколько (% ВВП на дату), когда, на что. Пригодится дипломатии.</summary>
public sealed record Loan(string Lender, double Amount, DateOnly Date, string Project);

public sealed class WorldInfrastructure
{
    public Dictionary<string, CountryInfrastructure> Countries { get; set; } = new(StringComparer.Ordinal);

    public CountryInfrastructure this[string country] => Countries[country];
}
