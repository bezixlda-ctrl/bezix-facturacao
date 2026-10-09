using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Configuracao;

public class ContaBancaria : Entidade
{
    public string Banco { get; set; } = "";
    public string NumeroConta { get; set; } = "";
    public string? Nib { get; set; }
    public string? Iban { get; set; }
    public string? Swift { get; set; }
    public bool MostrarNoDocumento { get; set; } = true;
    public int Ordem { get; set; }
}
