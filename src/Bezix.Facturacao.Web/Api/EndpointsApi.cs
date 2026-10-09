using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Application.Recebimentos;
using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Api;

// ---------- Contratos da API (JSON camelCase; valores em MZN) ----------

public sealed record ClienteApi(int Id, string Nome, string Tipo, string? Nuit, string? Endereco, string? Cidade, string? Telefone, string? Celular,
    string? Email, string? Contacto, int? PrazoPagamentoDias, decimal? LimiteCredito, decimal DescontoPadraoPct, bool Activo);

public sealed record ClienteEscritaApi(string Nome, string? Tipo, string? Nuit, string? Endereco, string? Cidade, string? Telefone, string? Celular,
    string? Email, string? Contacto, int? PrazoPagamentoDias, decimal? LimiteCredito, decimal? DescontoPadraoPct, string? Etiquetas, bool AceitarNuitDuplicado = false);

public sealed record ArtigoApi(int Id, string Codigo, string Descricao, string Tipo, string Unidade, decimal Preco, string CodigoTaxaIva, bool Activo);

public sealed record ArtigoEscritaApi(string Codigo, string Descricao, string? Tipo, string? Unidade, decimal Preco, string? CodigoTaxaIva);

public sealed record LinhaApi(string? ArtigoCodigo, string? Ref, string? Descricao, string? Unidade, decimal Quantidade, decimal? PrecoUnitario, decimal DescontoPct, string? CodigoTaxaIva);

public sealed record DocumentoEscritaApi(string Tipo, int ClienteId, int? SerieId, DateOnly? Data, DateOnly? Vencimento, decimal DescontoGlobalPct,
    string? Observacoes, string? MetodoPagamento, string? ReferenciaPagamento, int? DocumentoOrigemId, string? Motivo, IReadOnlyList<LinhaApi> Linhas, bool Emitir = false);

public sealed record LinhaDocumentoApi(string? Ref, string Descricao, string? Unidade, decimal Quantidade, decimal PrecoUnitario, decimal DescontoPct, decimal TaxaIvaPct, decimal Total);

public sealed record DocumentoApi(int Id, string Tipo, string? Numero, string Estado, DateOnly Data, DateOnly? Vencimento, int ClienteId, string ClienteNome,
    string? ClienteNuit, string Moeda, decimal Subtotal, decimal DescontoGlobal, decimal BaseTributavel, decimal TotalIva, decimal Total, decimal? EmAberto,
    string? EstadoPagamento, string? Hash, DateTime? EmitidoEm, int? DocumentoOrigemId, IReadOnlyList<LinhaDocumentoApi> Linhas);

public sealed record EmailApi(string? Para, string? Cc, string? Assunto, string? Mensagem);

public sealed record AnulacaoApi(string Motivo);

public sealed record AlocacaoApi(int DocumentoId, decimal Valor);

public sealed record ReciboEscritaApi(int ClienteId, DateOnly? Data, decimal Valor, string Metodo, string? Referencia, string? Observacoes, IReadOnlyList<AlocacaoApi>? Alocacoes);

public sealed record PaginaApi<T>(IReadOnlyList<T> Itens, int Total, int Pagina, int Tamanho);

/// <summary>API REST v1: autenticação por chave (Bearer), escopos, 60 pedidos/min por chave, erros em application/problem+json.</summary>
public static class EndpointsApi
{
    public static void MapApiV1(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1")
            .RequireRateLimiting("api")
            .WithTags("API v1")
            .AddEndpointFilter(async (c, n) =>
            {
                // Erros de validação dos serviços chegam como Resultado; o resto (ex.: JSON inválido) já é problem+json.
                try { return await n(c); }
                catch (BadHttpRequestException ex) { return Results.Problem(statusCode: 400, title: "Pedido inválido", detail: ex.Message); }
            });

        // ---------- Clientes
        api.MapGet("/clientes", async (ServicoClientes s, string? pesquisa, int pagina = 1, int tamanho = 25) =>
        {
            var p = await s.PesquisarAsync(new FiltroClientes(pesquisa, null, FiltroEstado.Todos, pagina, tamanho));
            var itens = new List<ClienteApi>();
            foreach (var l in p.Itens) itens.Add(Cliente((await s.ObterAsync(l.Id))!));
            return TypedResults.Ok(new PaginaApi<ClienteApi>(itens, p.Total, p.Numero, p.Tamanho));
        }).RequireAuthorization("escopo:" + EscoposApi.ClientesLer);

        api.MapGet("/clientes/{id:int}", async Task<Results<Ok<ClienteApi>, ProblemHttpResult>> (ServicoClientes s, int id) =>
            await s.ObterAsync(id) is { } c ? TypedResults.Ok(Cliente(c)) : NaoEncontrado("Cliente"))
            .RequireAuthorization("escopo:" + EscoposApi.ClientesLer);

        api.MapPost("/clientes", async Task<Results<Created<ClienteApi>, ProblemHttpResult>> (ServicoClientes s, ClienteEscritaApi d) =>
        {
            var r = await s.GuardarAsync(null, DadosCliente(d), d.AceitarNuitDuplicado);
            if (!r.Resultado.Sucesso) return Validacao(r.Resultado, r.PrecisaConfirmacao ? "Envie \"aceitarNuitDuplicado\": true se for outro cliente com o mesmo NUIT." : null);
            return TypedResults.Created($"/api/v1/clientes/{r.Id}", Cliente((await s.ObterAsync(r.Id!.Value))!));
        }).RequireAuthorization("escopo:" + EscoposApi.ClientesEscrever);

        api.MapPut("/clientes/{id:int}", async Task<Results<Ok<ClienteApi>, ProblemHttpResult>> (ServicoClientes s, int id, ClienteEscritaApi d) =>
        {
            if (await s.ObterAsync(id) is null) return NaoEncontrado("Cliente");
            var r = await s.GuardarAsync(id, DadosCliente(d), d.AceitarNuitDuplicado);
            if (!r.Resultado.Sucesso) return Validacao(r.Resultado, null);
            return TypedResults.Ok(Cliente((await s.ObterAsync(id))!));
        }).RequireAuthorization("escopo:" + EscoposApi.ClientesEscrever);

        api.MapGet("/clientes/{id:int}/extracto", async Task<Results<Ok<Extracto>, ProblemHttpResult>> (ServicoContaCorrente cc, int id, DateOnly? de, DateOnly? ate) =>
        {
            var fim = ate ?? cc.Hoje();
            var x = await cc.ExtractoAsync(id, de ?? new DateOnly(fim.Year, 1, 1), fim);
            return x is null ? NaoEncontrado("Cliente") : TypedResults.Ok(x);
        }).RequireAuthorization("escopo:" + EscoposApi.ClientesLer);

        // ---------- Artigos
        api.MapGet("/artigos", async (ServicoArtigos s, string? pesquisa, int pagina = 1, int tamanho = 25) =>
        {
            var p = await s.PesquisarAsync(new FiltroArtigos(pesquisa, FiltroEstado.Todos, pagina, tamanho));
            return TypedResults.Ok(new PaginaApi<ArtigoApi>(p.Itens.Select(Artigo).ToList(), p.Total, p.Numero, p.Tamanho));
        }).RequireAuthorization("escopo:" + EscoposApi.ArtigosLer);

        api.MapPost("/artigos", async Task<Results<Created<ArtigoApi>, ProblemHttpResult>> (ServicoArtigos s, ArtigoEscritaApi d) =>
        {
            var tipo = string.Equals(d.Tipo, "Produto", StringComparison.OrdinalIgnoreCase) ? TipoArtigo.Produto : TipoArtigo.Servico;
            var (r, id) = await s.GuardarAsync(null, new DadosArtigo(d.Codigo ?? "", d.Descricao ?? "", tipo, d.Unidade ?? "un", d.Preco, d.CodigoTaxaIva ?? "NOR"));
            if (!r.Sucesso) return Validacao(r, null);
            return TypedResults.Created($"/api/v1/artigos/{id}", Artigo((await s.ObterAsync(id!.Value))!));
        }).RequireAuthorization("escopo:" + EscoposApi.ArtigosEscrever);

        // ---------- Documentos
        api.MapPost("/documentos", async Task<Results<Created<DocumentoApi>, ProblemHttpResult>> (
            ServicoDocumentos docs, ServicoEmissao emissao, ServicoArtigos artigos, ServicoContaCorrente cc, HttpContext http, DocumentoEscritaApi d) =>
        {
            if (!Enum.TryParse<TipoDocumento>(d.Tipo, true, out var tipo) || !RegrasTipo.Editaveis.Contains(tipo))
                return Problema(400, "Tipo inválido", "Use FT, FR, FG, NC, ND, CT ou GR.");
            if (d.Emitir && !http.User.HasClaim(AutenticacaoApi.ClaimEscopo, EscoposApi.DocumentosEmitir))
                return Problema(403, "Sem permissão", "Para emitir (\"emitir\": true) a chave precisa do escopo documentos:emitir.");

            var novo = await docs.NovoAsync(tipo, d.ClienteId);
            var linhas = new List<LinhaRascunho>();
            foreach (var l in d.Linhas ?? [])
            {
                Artigo? a = null;
                if (!string.IsNullOrWhiteSpace(l.ArtigoCodigo))
                {
                    a = (await artigos.PesquisarAsync(new FiltroArtigos(l.ArtigoCodigo, FiltroEstado.Activos, 1, 5))).Itens
                        .FirstOrDefault(x => string.Equals(x.Codigo, l.ArtigoCodigo.Trim(), StringComparison.OrdinalIgnoreCase));
                    if (a is null) return Problema(422, "Artigo desconhecido", $"Não existe artigo activo com o código {l.ArtigoCodigo}.");
                }
                linhas.Add(new LinhaRascunho(a?.Id, l.Ref ?? a?.Codigo, l.Descricao ?? a?.Descricao ?? "", l.Unidade ?? a?.Unidade, l.Quantidade,
                    l.PrecoUnitario ?? a?.Preco ?? 0, l.DescontoPct, l.CodigoTaxaIva ?? a?.CodigoTaxaIva ?? "NOR"));
            }
            MetodoPagamento? metodo = d.MetodoPagamento is null ? null : Enum.TryParse<MetodoPagamento>(d.MetodoPagamento, true, out var m) ? m : null;
            var dados = novo with
            {
                SerieId = d.SerieId ?? novo.SerieId,
                Data = d.Data ?? novo.Data,
                Vencimento = d.Vencimento ?? novo.Vencimento,
                DescontoGlobalPct = d.DescontoGlobalPct,
                Observacoes = d.Observacoes,
                MetodoPagamento = metodo,
                ReferenciaPagamento = d.ReferenciaPagamento,
                DocOrigemId = d.DocumentoOrigemId,
                MotivoCorreccao = d.Motivo,
                Linhas = linhas,
            };
            var (r, id) = await docs.GuardarRascunhoAsync(null, dados);
            if (!r.Sucesso) return Validacao(r, null);

            if (d.Emitir)
            {
                var e = await emissao.EmitirAsync(id!.Value);
                if (e.Tipo == ResultadoEmissaoTipo.Recusado)
                    return Validacao(e.Resultado, $"O rascunho {id} foi criado mas não foi emitido.");
            }
            return TypedResults.Created($"/api/v1/documentos/{id}", await DocumentoAsync(docs, cc, id!.Value));
        }).RequireAuthorization("escopo:" + EscoposApi.DocumentosEscrever).AddEndpointFilter<Idempotencia>();

        api.MapGet("/documentos/{id:int}", async Task<Results<Ok<DocumentoApi>, ProblemHttpResult>> (ServicoDocumentos docs, ServicoContaCorrente cc, int id) =>
            await docs.ObterAsync(id) is null ? NaoEncontrado("Documento") : TypedResults.Ok(await DocumentoAsync(docs, cc, id)))
            .RequireAuthorization("escopo:" + EscoposApi.DocumentosLer);

        api.MapGet("/documentos/{id:int}/pdf", async Task<Results<FileContentHttpResult, ProblemHttpResult>> (ServicoDocumentos docs, ServicoPdfDocumentos pdfs, int id) =>
        {
            if (await docs.ObterAsync(id) is null) return NaoEncontrado("Documento");
            var pdf = await pdfs.ObterAsync(id);
            return pdf is null ? NaoEncontrado("Documento") : TypedResults.File(pdf.Conteudo, "application/pdf", pdf.Nome);
        }).RequireAuthorization("escopo:" + EscoposApi.DocumentosLer);

        api.MapPost("/documentos/{id:int}/email", async Task<Results<Accepted, ProblemHttpResult>> (ServicoDocumentos docs, ServicoComunicacao com, int id, EmailApi? d) =>
        {
            if (await docs.ObterAsync(id) is null) return NaoEncontrado("Documento");
            var proposto = await com.PrepararDocumentoAsync(id);
            var email = new EmailPreparado(d?.Para ?? proposto!.Para, d?.Cc ?? proposto!.Cc, d?.Assunto ?? proposto!.Assunto, d?.Mensagem ?? proposto!.Corpo, true);
            var r = await com.EnviarDocumentoAsync(id, email);
            // Email que ficou na fila (falha temporária) conta como aceite: o sistema repete.
            return r.Sucesso || r.Erros[0].StartsWith("O email ficou na fila") ? TypedResults.Accepted((string?)null) : Validacao(r, null);
        }).RequireAuthorization("escopo:" + EscoposApi.DocumentosEnviar);

        api.MapPost("/documentos/{id:int}/anular", async Task<Results<Ok<DocumentoApi>, ProblemHttpResult>> (ServicoDocumentos docs, ServicoEmissao emissao, ServicoContaCorrente cc, int id, AnulacaoApi d) =>
        {
            if (await docs.ObterAsync(id) is null) return NaoEncontrado("Documento");
            var r = await emissao.AnularAsync(id, d.Motivo ?? "");
            return r.Sucesso ? TypedResults.Ok(await DocumentoAsync(docs, cc, id)) : Validacao(r, null);
        }).RequireAuthorization("escopo:" + EscoposApi.DocumentosAnular);

        // ---------- Recibos
        api.MapPost("/recibos", async Task<Results<Created<DocumentoApi>, ProblemHttpResult>> (ServicoRecibos recibos, ServicoDocumentos docs, ServicoContaCorrente cc, ReciboEscritaApi d) =>
        {
            if (!Enum.TryParse<MetodoPagamento>(d.Metodo, true, out var metodo))
                return Problema(400, "Método inválido", "Use Numerario, Transferencia, MPesa, EMola, Cheque ou Pos.");
            var (r, id) = await recibos.RegistarAsync(new DadosRecibo(d.ClienteId, d.Data ?? cc.Hoje(), d.Valor, metodo, d.Referencia, d.Observacoes,
                (d.Alocacoes ?? []).Select(a => new Alocacao(a.DocumentoId, a.Valor)).ToList()));
            if (!r.Sucesso) return Validacao(r, null);
            return TypedResults.Created($"/api/v1/documentos/{id}", await DocumentoAsync(docs, cc, id!.Value));
        }).RequireAuthorization("escopo:" + EscoposApi.RecibosEscrever).AddEndpointFilter<Idempotencia>();
    }

    // ------------------------------------------------------------------ Conversões

    private static ClienteApi Cliente(Cliente c) => new(c.Id, c.Nome, c.Tipo.ToString(), c.Nuit, c.Endereco, c.Cidade, c.Telefone, c.Celular, c.Email,
        c.Contacto, c.PrazoPagamentoDias, c.LimiteCredito, c.DescontoPadraoPct, c.Activo);

    private static DadosCliente DadosCliente(ClienteEscritaApi d) => new(
        d.Nome ?? "", string.Equals(d.Tipo, "Particular", StringComparison.OrdinalIgnoreCase) ? TipoCliente.Particular : TipoCliente.Empresa,
        d.Nuit, d.Endereco, d.Cidade, d.Telefone, d.Celular, d.Email, d.Contacto, d.PrazoPagamentoDias, d.LimiteCredito, d.DescontoPadraoPct ?? 0,
        null, d.Etiquetas, null, null, true, null);

    private static ArtigoApi Artigo(Artigo a) => new(a.Id, a.Codigo, a.Descricao, a.Tipo.ToString(), a.Unidade, a.Preco, a.CodigoTaxaIva, a.Activo);

    private static async Task<DocumentoApi> DocumentoAsync(ServicoDocumentos docs, ServicoContaCorrente cc, int id)
    {
        var d = (await docs.ObterAsync(id))!;
        var s = (await cc.SituacaoAsync([id])).GetValueOrDefault(id);
        return new DocumentoApi(d.Id, d.TipoDoc.ToString(), d.NumeroFormatado, d.Estado.ToString(), d.Data, d.Vencimento, d.ClienteId, d.ClienteNome,
            d.ClienteNuit, d.Moeda, d.Subtotal, d.DescontoGlobal, d.BaseTributavel, d.TotalIva, d.Total, s?.EmAberto, s?.Estado.ToString(), d.Hash, d.EmitidoEm,
            d.DocOrigemId, d.Linhas.Select(l => new LinhaDocumentoApi(l.Ref, l.Descricao, l.Unidade, l.Quantidade, l.PrecoUnitario, l.DescontoPct, l.TaxaIvaPct, l.TotalLinha)).ToList());
    }

    // ------------------------------------------------------------------ Erros (application/problem+json)

    private static ProblemHttpResult NaoEncontrado(string o) => TypedResults.Problem(statusCode: 404, title: $"{o} não encontrado");

    private static ProblemHttpResult Problema(int codigo, string titulo, string detalhe) => TypedResults.Problem(statusCode: codigo, title: titulo, detail: detalhe);

    private static ProblemHttpResult Validacao(Resultado r, string? nota) => TypedResults.Problem(new ProblemDetails
    {
        Status = 422,
        Title = "Pedido recusado",
        Detail = nota ?? string.Join(" ", r.Erros),
        Extensions = { ["erros"] = r.Erros },
    });
}
