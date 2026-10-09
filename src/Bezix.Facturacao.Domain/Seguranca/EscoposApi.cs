namespace Bezix.Facturacao.Domain.Seguranca;

/// <summary>
/// Escopos das chaves da API v1. Cada escopo dá um conjunto de permissões: assim a API passa pelas mesmas
/// regras (e pela mesma auditoria) que a interface.
/// </summary>
public static class EscoposApi
{
    public const string ClientesLer = "clientes:ler";
    public const string ClientesEscrever = "clientes:escrever";
    public const string ArtigosLer = "artigos:ler";
    public const string ArtigosEscrever = "artigos:escrever";
    public const string DocumentosLer = "documentos:ler";
    public const string DocumentosEscrever = "documentos:escrever";
    public const string DocumentosEmitir = "documentos:emitir";
    public const string DocumentosEnviar = "documentos:enviar";
    public const string DocumentosAnular = "documentos:anular";
    public const string RecibosEscrever = "recibos:escrever";

    public sealed record Definicao(string Codigo, string Descricao, IReadOnlyList<string> Permissoes);

    public static readonly IReadOnlyList<Definicao> Todos =
    [
        new(ClientesLer, "Ler clientes e extractos", [Seguranca.Permissoes.Clientes.Ver, Seguranca.Permissoes.Documentos.Ver, Seguranca.Permissoes.Documentos.VerTodos]),
        new(ClientesEscrever, "Criar e alterar clientes", [Seguranca.Permissoes.Clientes.Ver, Seguranca.Permissoes.Clientes.Gerir]),
        new(ArtigosLer, "Ler artigos", [Seguranca.Permissoes.Artigos.Ver]),
        new(ArtigosEscrever, "Criar artigos", [Seguranca.Permissoes.Artigos.Ver, Seguranca.Permissoes.Artigos.Gerir]),
        new(DocumentosLer, "Ler documentos e PDF", [Seguranca.Permissoes.Documentos.Ver, Seguranca.Permissoes.Documentos.VerTodos, Seguranca.Permissoes.Clientes.Ver]),
        new(DocumentosEscrever, "Criar rascunhos", [Seguranca.Permissoes.Documentos.Ver, Seguranca.Permissoes.Documentos.VerTodos, Seguranca.Permissoes.Documentos.Rascunho, Seguranca.Permissoes.Clientes.Ver]),
        new(DocumentosEmitir, "Emitir documentos (FT, FR, FG, NC, ND)",
        [
            Seguranca.Permissoes.Documentos.Ver, Seguranca.Permissoes.Documentos.VerTodos, Seguranca.Permissoes.Documentos.Rascunho, Seguranca.Permissoes.Clientes.Ver,
            Seguranca.Permissoes.Documentos.EmitirFactura, Seguranca.Permissoes.Documentos.EmitirFacturaRecibo,
            Seguranca.Permissoes.Documentos.EmitirNotaDebito, Seguranca.Permissoes.Documentos.EmitirNotaCredito, Seguranca.Permissoes.Documentos.DescontoSemLimite,
        ]),
        new(DocumentosEnviar, "Enviar documentos por email", [Seguranca.Permissoes.Documentos.Ver, Seguranca.Permissoes.Documentos.VerTodos, Seguranca.Permissoes.Comunicacao.Enviar]),
        new(DocumentosAnular, "Anular documentos", [Seguranca.Permissoes.Documentos.Ver, Seguranca.Permissoes.Documentos.VerTodos, Seguranca.Permissoes.Documentos.Anular]),
        new(RecibosEscrever, "Registar recibos", [Seguranca.Permissoes.Recibos.Registar, Seguranca.Permissoes.Documentos.Ver, Seguranca.Permissoes.Documentos.VerTodos, Seguranca.Permissoes.Clientes.Ver]),
    ];

    public static bool Existe(string escopo) => Todos.Any(e => e.Codigo == escopo);

    public static IEnumerable<string> PermissoesDe(IEnumerable<string> escopos) =>
        Todos.Where(e => escopos.Contains(e.Codigo)).SelectMany(e => e.Permissoes).Distinct();
}
