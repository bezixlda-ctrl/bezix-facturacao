namespace Bezix.Facturacao.Domain.Auditoria;

/// <summary>Registo imutável de acções sensíveis: emissão, anulação, NC, permissões, configurações, login.</summary>
public class RegistoAuditoria
{
    public long Id { get; set; }
    public string Entidade { get; set; } = "";
    public string? EntidadeId { get; set; }
    public string Accao { get; set; } = "";
    public string? Antes { get; set; }
    public string? Depois { get; set; }
    public string? Utilizador { get; set; }
    public string? Ip { get; set; }
    public DateTime Data { get; set; }
}
