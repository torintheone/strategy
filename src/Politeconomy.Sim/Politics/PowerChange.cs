using Politeconomy.Sim.Government;
using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Смена власти без выборов: kind — no_confidence (вотум недоверия), impeachment (импичмент), palace_coup (дворцовый
/// переворот), military_coup (военный переворот), handover (хунта передала власть гражданским), revolution (революция).
/// </summary>
public sealed record PowerChange(DateOnly Date, string Kind, string OldLeader, string NewLeader, string? NewParty);

/// <summary>
/// Хунта у власти: какой была страна до переворота (строй, режим, парламент) и когда военные обещают вернуть власть
/// гражданским (null — не обещают).
/// </summary>
public sealed class Junta
{
    public PoliticalSystem System { get; set; }
    public Regime Regime { get; set; }
    public Parliament Parliament { get; set; } = new();
    public DateOnly? HandOver { get; set; }
}
