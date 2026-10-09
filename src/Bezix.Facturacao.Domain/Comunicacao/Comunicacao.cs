using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Comunicacao;

/// <summary>
/// Modelo de texto dirigido ao cliente. Nenhum texto para o cliente fica fixo no código:
/// os modelos são criados na primeira execução e editados em Configurações (editor na fase 5).
/// Variáveis no formato {cliente.nome}, {documento.numero}, etc.
/// </summary>
public class ModeloComunicacao : Entidade
{
    public string Codigo { get; set; } = "";
    public string Nome { get; set; } = "";
    public string Canal { get; set; } = "email";
    public string Assunto { get; set; } = "";
    public string Corpo { get; set; } = "";
    public string? Cc { get; set; }
    public bool AnexarPdf { get; set; } = true;
    public bool Activo { get; set; } = true;
}

/// <summary>Fila de emails: tudo o que se envia passa por aqui, com repetições em caso de falha.</summary>
public class EmailFila : Entidade
{
    public string Para { get; set; } = "";
    public string? Cc { get; set; }
    public string? Bcc { get; set; }
    public string Assunto { get; set; } = "";
    public string Corpo { get; set; } = "";

    /// <summary>Caminhos relativos (App_Data/uploads) separados por ";" e nome a mostrar após "|".</summary>
    public string? Anexos { get; set; }

    public EstadoEmail Estado { get; set; } = EstadoEmail.Pendente;
    public int Tentativas { get; set; }
    public string? ErroUltimo { get; set; }
    public DateTime? EnviadoEm { get; set; }
    public DateTime? ProximaTentativa { get; set; }

    public int? ClienteId { get; set; }
    public int? DocumentoId { get; set; }
    public string? ModeloCodigo { get; set; }
}

public enum EstadoEmail { Pendente = 0, Enviado = 1, Falhou = 2 }
