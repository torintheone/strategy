using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Text;

/// <summary>Текст подсказки показателя: значение, что оно значит и из чего складывается.</summary>
public static class Explain
{
    public static string Stat(IndicatorDef def, StatValue? stat)
    {
        if (stat is null)
            return $"{def.Name}\n{def.Description}";
        var lines = new List<string>
        {
            $"{def.Name}: {Fmt.Value(def.Format, stat.Value, def.Unit)}",
            def.Description,
        };
        if (stat.Factors is { Count: > 0 } factors)
        {
            lines.Add("");
            lines.Add("Из чего складывается:");
            foreach (var f in factors)
                lines.Add($"  {f.Label}: {Fmt.Value(f.Format, f.Value)}");
        }
        return string.Join('\n', lines);
    }
}
