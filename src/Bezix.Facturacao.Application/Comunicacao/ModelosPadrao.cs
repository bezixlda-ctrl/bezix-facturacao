using Bezix.Facturacao.Domain.Comunicacao;

namespace Bezix.Facturacao.Application.Comunicacao;

/// <summary>
/// Textos originais dos modelos de comunicação. São copiados para a base de dados na primeira execução e
/// depois editados em Comunicação → Modelos; "Repor texto original" volta a estes.
/// </summary>
public static class ModelosPadrao
{
    public sealed record Variavel(string Nome, string Descricao);

    public static readonly IReadOnlyList<Variavel> Variaveis =
    [
        new("{cliente.nome}", "Nome do cliente"),
        new("{cliente.contacto}", "Pessoa de contacto (ou o nome do cliente)"),
        new("{documento.tipo}", "Factura, Recibo, …"),
        new("{documento.numero}", "Ex.: FT A2026/0008"),
        new("{documento.data}", "Data do documento"),
        new("{documento.vencimento}", "Data de vencimento"),
        new("{documento.total}", "Total, ex.: 28.586,20 MZN"),
        new("{documento.emaberto}", "Valor ainda por pagar"),
        new("{saldo.vencido}", "Total vencido do cliente"),
        new("{saldo.final}", "Saldo do extracto"),
        new("{extracto.periodo}", "Período do extracto"),
        new("{dias.atraso}", "Dias desde o vencimento"),
        new("{link.documento}", "Link seguro para ver o documento e comunicar o pagamento"),
        new("{empresa.nome}", "Nome da empresa"),
        new("{empresa.contas}", "Contas bancárias visíveis nos documentos"),
        new("{utilizador.nome}", "Quem envia"),
        new("{assinatura}", "Assinatura de quem envia (ou a da empresa)"),
    ];

    private const string Assinatura = "\n\nPara qualquer esclarecimento, basta responder a este email.\n\nCumprimentos,\n{assinatura}";
    private const string Contas = "\n\nPode pagar por transferência para:\n{empresa.contas}";
    private const string Link = "\n\nPode ver o documento e comunicar o pagamento aqui: {link.documento}";

    public static readonly IReadOnlyList<ModeloComunicacao> Todos =
    [
        new() { Codigo = "documento.FT", Nome = "Envio de factura", Assunto = "Factura {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a factura {documento.numero}, de {documento.data}, no valor de {documento.total}, com vencimento a {documento.vencimento}." + Contas + Link + Assinatura },
        new() { Codigo = "documento.FG", Nome = "Envio de factura global", Assunto = "Factura global {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a factura global {documento.numero}, de {documento.data}, no valor de {documento.total}, com vencimento a {documento.vencimento}." + Contas + Link + Assinatura },
        new() { Codigo = "documento.FR", Nome = "Envio de factura-recibo", Assunto = "Factura-recibo {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nAgradecemos o seu pagamento. Enviamos em anexo a factura-recibo {documento.numero}, de {documento.data}, no valor de {documento.total}." + Assinatura },
        new() { Codigo = "documento.NC", Nome = "Envio de nota de crédito", Assunto = "Nota de crédito {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a nota de crédito {documento.numero}, de {documento.data}, no valor de {documento.total}." + Assinatura },
        new() { Codigo = "documento.ND", Nome = "Envio de nota de débito", Assunto = "Nota de débito {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a nota de débito {documento.numero}, de {documento.data}, no valor de {documento.total}, com vencimento a {documento.vencimento}." + Contas + Link + Assinatura },
        new() { Codigo = "documento.CT", Nome = "Envio de cotação", Assunto = "Cotação {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nConforme solicitado, enviamos em anexo a cotação {documento.numero}, no valor de {documento.total}, válida até {documento.vencimento}.\n\nPara avançar, basta confirmar por resposta a este email." + Assinatura },
        new() { Codigo = "documento.GR", Nome = "Envio de guia de remessa", Assunto = "Guia de remessa {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo a guia de remessa {documento.numero}, de {documento.data}, referente à mercadoria entregue." + Assinatura },
        new() { Codigo = "documento.RC", Nome = "Envio de recibo", Assunto = "Recibo {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nConfirmamos a recepção do seu pagamento de {documento.total}. Enviamos em anexo o recibo {documento.numero}, de {documento.data}.\n\nObrigado pela preferência." + Assinatura },
        new() { Codigo = "extracto", Nome = "Envio de extracto de conta corrente", Assunto = "Extracto de conta corrente · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nEnviamos em anexo o extracto da sua conta corrente de {extracto.periodo}.\n\nSaldo a esta data: {saldo.final}, dos quais {saldo.vencido} já vencidos." + Contas + Assinatura },

        new() { Codigo = "lembrete.antes", Nome = "Lembrete antes do vencimento", Assunto = "Lembrete: {documento.numero} vence a {documento.vencimento}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nLembramos que a {documento.tipo} {documento.numero}, no valor em aberto de {documento.emaberto}, vence a {documento.vencimento}.\n\nSe já efectuou o pagamento, agradecemos e pedimos que ignore esta mensagem." + Contas + Link + Assinatura },
        new() { Codigo = "lembrete.aviso1", Nome = "1.º aviso de pagamento em atraso", Assunto = "{documento.numero} venceu a {documento.vencimento}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nA {documento.tipo} {documento.numero} venceu a {documento.vencimento} e tem ainda {documento.emaberto} em aberto.\n\nPode ter sido um esquecimento: agradecemos que regularize assim que possível. Se já pagou, envie-nos por favor o comprovativo." + Contas + Link + Assinatura },
        new() { Codigo = "lembrete.aviso2", Nome = "2.º aviso de pagamento em atraso", Assunto = "2.º aviso: {documento.numero} em atraso há {dias.atraso} dias",
            Corpo = "Exmo(a). {cliente.contacto},\n\nApesar do nosso aviso anterior, a {documento.tipo} {documento.numero} continua por pagar: {documento.emaberto} em atraso há {dias.atraso} dias.\n\nPedimos que regularize esta situação nos próximos dias ou que nos contacte para combinarmos uma solução." + Contas + Link + Assinatura },
        new() { Codigo = "lembrete.aviso3", Nome = "3.º aviso de pagamento em atraso", Assunto = "Último aviso: {documento.numero} em atraso há {dias.atraso} dias",
            Corpo = "Exmo(a). {cliente.contacto},\n\nA {documento.tipo} {documento.numero} está em atraso há {dias.atraso} dias, com {documento.emaberto} por pagar. O total vencido da sua conta é de {saldo.vencido}.\n\nSem regularização ou contacto da sua parte nos próximos 5 dias úteis, seremos obrigados a suspender novos fornecimentos." + Contas + Link + Assinatura },
        new() { Codigo = "pagamento.confirmado", Nome = "Confirmação de pagamento recebido", Assunto = "Pagamento recebido · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nRecebemos e validámos o seu pagamento de {documento.total}. Enviamos em anexo o recibo {documento.numero}.\n\nObrigado." + Assinatura },
        new() { Codigo = "recorrente.emitida", Nome = "Factura recorrente emitida", Assunto = "Factura {documento.numero} · {empresa.nome}",
            Corpo = "Exmo(a). {cliente.contacto},\n\nConforme acordado, enviamos em anexo a factura {documento.numero} de {documento.data}, no valor de {documento.total}, com vencimento a {documento.vencimento}." + Contas + Link + Assinatura },
    ];

    public static ModeloComunicacao? Obter(string codigo) => Todos.FirstOrDefault(m => m.Codigo == codigo);
}
