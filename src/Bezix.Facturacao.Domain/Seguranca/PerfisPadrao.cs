using static Bezix.Facturacao.Domain.Seguranca.Permissoes;

namespace Bezix.Facturacao.Domain.Seguranca;

/// <summary>
/// Os seis perfis recomendados na especificação. São criados na primeira execução;
/// depois o Administrador pode ajustá-los sem alterar código.
/// </summary>
public static class PerfisPadrao
{
    public const string Administrador = "Administrador";
    public const string Gestor = "Gestor";
    public const string Facturador = "Facturador";
    public const string Tesouraria = "Tesouraria";
    public const string Contabilista = "Contabilista";
    public const string Consulta = "Consulta";

    public sealed record Definicao(string Nome, string Descricao, decimal? DescontoMaximoPct, IReadOnlyList<string> Permissoes);

    public static readonly IReadOnlyList<Definicao> Todos =
    [
        new(Administrador, "Acesso total, incluindo utilizadores, perfis e configurações", null,
            Permissoes.Todas.Select(p => p.Codigo).ToList()),

        new(Gestor, "Gere vendas, cobranças e aprovações; não altera configurações nem utilizadores", null,
            Permissoes.Todas.Select(p => p.Codigo)
                .Except([Configuracoes.Gerir, Configuracoes.VerTaxas, Administracao.Utilizadores, Administracao.Integracoes])
                .ToList()),

        new(Facturador, "Cria clientes e emite documentos; NC e descontos altos pedem aprovação", 10m,
        [
            Dashboard.Facturacao,
            Permissoes.Clientes.Ver, Permissoes.Clientes.Gerir, Permissoes.Artigos.Ver, Permissoes.Artigos.Gerir,
            Permissoes.Documentos.Ver, Permissoes.Documentos.Rascunho, Permissoes.Documentos.EmitirFactura,
            Permissoes.Documentos.EmitirFacturaRecibo, Permissoes.Documentos.EmitirNotaDebito,
            Permissoes.Documentos.PedirNotaCredito,
            Comunicacao.Enviar, Relatorios.Vendas, Automacao.Ver,
        ]),

        new(Tesouraria, "Regista recebimentos, valida pagamentos e faz cobranças", 0m,
        [
            Dashboard.Recebimentos, Dashboard.Saldos,
            Permissoes.Clientes.Ver, Permissoes.Clientes.Gerir, Permissoes.Artigos.Ver,
            Permissoes.Documentos.Ver, Permissoes.Documentos.VerTodos, Permissoes.Documentos.EmitirFacturaRecibo,
            Recibos.Registar, Recibos.ValidarPagamentoComunicado,
            Comunicacao.Enviar, Automacao.Ver,
        ]),

        new(Contabilista, "Consulta documentos, relatórios fiscais e exportação AT", 0m,
        [
            Dashboard.Facturacao, Dashboard.Recebimentos, Dashboard.Iva,
            Permissoes.Clientes.Ver, Permissoes.Artigos.Ver,
            Permissoes.Documentos.Ver, Permissoes.Documentos.VerTodos,
            Relatorios.Vendas, Relatorios.Fiscal,
            Configuracoes.VerTaxas, Administracao.VerAuditoria,
        ]),

        new(Consulta, "Só consulta", 0m,
        [
            Dashboard.Resumo,
            Permissoes.Clientes.Ver, Permissoes.Artigos.Ver,
            Permissoes.Documentos.Ver, Permissoes.Documentos.VerTodos,
            Relatorios.Vendas,
        ]),
    ];
}
