namespace Bezix.Facturacao.Domain.Seguranca;

/// <summary>
/// Catálogo de permissões granulares. Cada código é uma claim "permissao" do perfil
/// e dá origem a uma policy de autorização com o mesmo nome.
/// </summary>
public static class Permissoes
{
    public const string ClaimType = "permissao";

    public static class Dashboard
    {
        public const string Facturacao = "Dashboard.Facturacao";
        public const string Recebimentos = "Dashboard.Recebimentos";
        public const string Saldos = "Dashboard.Saldos";
        public const string Iva = "Dashboard.Iva";
        public const string Gestao = "Dashboard.Gestao";
        public const string Resumo = "Dashboard.Resumo";
    }

    public static class Clientes
    {
        public const string Ver = "Clientes.Ver";
        public const string Gerir = "Clientes.Gerir";
    }

    public static class Artigos
    {
        public const string Ver = "Artigos.Ver";
        public const string Gerir = "Artigos.Gerir";
    }

    public static class Documentos
    {
        public const string Ver = "Documentos.Ver";
        public const string VerTodos = "Documentos.VerTodos";
        public const string Rascunho = "Documentos.Rascunho";
        public const string EmitirFactura = "Documentos.EmitirFactura";
        public const string EmitirFacturaRecibo = "Documentos.EmitirFacturaRecibo";
        public const string EmitirNotaDebito = "Documentos.EmitirNotaDebito";
        public const string EmitirNotaCredito = "Documentos.EmitirNotaCredito";
        public const string PedirNotaCredito = "Documentos.PedirNotaCredito";
        public const string Anular = "Documentos.Anular";
        public const string DescontoSemLimite = "Documentos.DescontoSemLimite";
        public const string Aprovar = "Documentos.Aprovar";
    }

    public static class Recibos
    {
        public const string Registar = "Recibos.Registar";
        public const string ValidarPagamentoComunicado = "Recibos.ValidarPagamentoComunicado";
    }

    public static class Comunicacao
    {
        public const string Enviar = "Comunicacao.Enviar";
        public const string GerirModelos = "Comunicacao.GerirModelos";
    }

    public static class Relatorios
    {
        public const string Vendas = "Relatorios.Vendas";
        public const string Fiscal = "Relatorios.Fiscal";
    }

    public static class Automacao
    {
        public const string Ver = "Automacao.Ver";
        public const string Gerir = "Automacao.Gerir";
    }

    public static class Configuracoes
    {
        public const string Gerir = "Configuracoes.Gerir";
        public const string VerTaxas = "Configuracoes.VerTaxas";
    }

    public static class Administracao
    {
        public const string Utilizadores = "Administracao.Utilizadores";
        public const string Integracoes = "Administracao.Integracoes";
        public const string VerAuditoria = "Administracao.VerAuditoria";
    }

    public sealed record Definicao(string Codigo, string Grupo, string Descricao);

    public static readonly IReadOnlyList<Definicao> Todas =
    [
        new(Dashboard.Facturacao, "Painel", "Ver facturação do mês"),
        new(Dashboard.Recebimentos, "Painel", "Ver recebimentos do mês"),
        new(Dashboard.Saldos, "Painel", "Ver valores por receber e vencidos"),
        new(Dashboard.Iva, "Painel", "Ver IVA liquidado"),
        new(Dashboard.Gestao, "Painel", "Ver indicadores de gestão (prazo médio, top clientes)"),
        new(Dashboard.Resumo, "Painel", "Ver resumo do painel"),

        new(Clientes.Ver, "Clientes e artigos", "Consultar clientes"),
        new(Clientes.Gerir, "Clientes e artigos", "Criar e editar clientes"),
        new(Artigos.Ver, "Clientes e artigos", "Consultar artigos"),
        new(Artigos.Gerir, "Clientes e artigos", "Criar e editar artigos"),

        new(Documentos.Ver, "Documentos", "Consultar documentos"),
        new(Documentos.VerTodos, "Documentos", "Ver clientes e documentos de todos os utilizadores (sem esta, só os próprios, se a opção estiver activa)"),
        new(Documentos.Rascunho, "Documentos", "Criar cotações, guias e rascunhos"),
        new(Documentos.EmitirFactura, "Documentos", "Emitir facturas e facturas globais"),
        new(Documentos.EmitirFacturaRecibo, "Documentos", "Emitir facturas-recibo"),
        new(Documentos.EmitirNotaDebito, "Documentos", "Emitir notas de débito"),
        new(Documentos.EmitirNotaCredito, "Documentos", "Emitir notas de crédito"),
        new(Documentos.PedirNotaCredito, "Documentos", "Pedir nota de crédito (fica a aguardar aprovação)"),
        new(Documentos.Anular, "Documentos", "Anular documentos"),
        new(Documentos.DescontoSemLimite, "Documentos", "Aplicar desconto acima do limite do perfil"),
        new(Documentos.Aprovar, "Documentos", "Aprovar descontos e notas de crédito pedidos"),

        new(Recibos.Registar, "Recebimentos", "Registar recibos e alocar pagamentos"),
        new(Recibos.ValidarPagamentoComunicado, "Recebimentos", "Validar pagamentos comunicados pelos clientes"),

        new(Comunicacao.Enviar, "Comunicação", "Enviar documentos e lembretes"),
        new(Comunicacao.GerirModelos, "Comunicação", "Editar modelos de texto e email"),

        new(Relatorios.Vendas, "Relatórios", "Relatórios de vendas"),
        new(Relatorios.Fiscal, "Relatórios", "Relatórios fiscais e exportação AT"),

        new(Automacao.Ver, "Automação", "Ver recorrentes e automações"),
        new(Automacao.Gerir, "Automação", "Gerir recorrentes e automações"),

        new(Configuracoes.Gerir, "Configurações", "Alterar configurações, séries e taxas de IVA"),
        new(Configuracoes.VerTaxas, "Configurações", "Consultar taxas de IVA e séries"),

        new(Administracao.Utilizadores, "Administração", "Gerir utilizadores e perfis"),
        new(Administracao.Integracoes, "Administração", "Gerir chaves API e webhooks"),
        new(Administracao.VerAuditoria, "Administração", "Consultar a auditoria"),
    ];

    public static bool Existe(string codigo) => Todas.Any(p => p.Codigo == codigo);
}
