using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Fiscal;

/// <summary>Taxa de IVA ou isenção, com período de vigência. Mudança de taxa = novo registo, nunca editar o antigo.</summary>
public class TaxaIva : Entidade
{
    public string Codigo { get; set; } = "";
    public string Descricao { get; set; } = "";
    public decimal Percentagem { get; set; }

    /// <summary>Obrigatório quando a percentagem é 0 (menção legal da isenção no documento).</summary>
    public string? MotivoIsencao { get; set; }

    public DateOnly VigenteDesde { get; set; }
    public DateOnly? VigenteAte { get; set; }

    public bool EstaVigente(DateOnly data) =>
        data >= VigenteDesde && (VigenteAte is null || data <= VigenteAte);

    public bool IsIsencao => Percentagem == 0m;
}
