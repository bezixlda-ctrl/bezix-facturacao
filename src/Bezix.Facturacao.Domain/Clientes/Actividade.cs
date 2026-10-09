using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Clientes;

/// <summary>Chamada, reunião, tarefa ou nota registada na ficha do cliente.</summary>
public class Actividade : Entidade
{
    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }

    public TipoActividade Tipo { get; set; }

    /// <summary>Data/hora em UTC: quando aconteceu (chamada, reunião, nota) ou prazo (tarefa).</summary>
    public DateTime Data { get; set; }

    public string Nota { get; set; } = "";

    /// <summary>Id do utilizador responsável.</summary>
    public string? ResponsavelId { get; set; }
    public string? ResponsavelNome { get; set; }

    /// <summary>Só para tarefas.</summary>
    public DateTime? ConcluidaEm { get; set; }
}

public enum TipoActividade
{
    Chamada = 0,
    Reuniao = 1,
    Tarefa = 2,
    Nota = 3,
}
