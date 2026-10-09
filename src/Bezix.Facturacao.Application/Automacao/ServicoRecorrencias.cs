using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Comunicacao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Automacao;

public sealed record DadosRecorrencia(string Descricao, Periodicidade Periodicidade, DateOnly ProximaData, DateOnly? DataFim, bool EmitirAutomaticamente, bool EnviarEmail);

public sealed record RecorrenciaVista(Recorrencia Recorrencia, string Cliente, string? Modelo, decimal Total, string? UltimoDocumento);

/// <summary>
/// Facturas recorrentes: na data prevista o sistema copia o documento modelo (linhas, preços, desconto),
/// com a data do dia; emite e envia por email, ou deixa um rascunho para alguém rever.
/// </summary>
public sealed class ServicoRecorrencias(
    IAppDbContext db,
    ServicoDocumentos documentos,
    ServicoEmissao emissao,
    ServicoComunicacao comunicacao,
    IUtilizadorActual utilizador,
    IServicoAuditoria auditoria,
    TimeProvider relogio)
{
    public const string ModeloEmail = "recorrente.emitida";

    public async Task<List<RecorrenciaVista>> ListarAsync(CancellationToken ct = default)
    {
        var q = from r in db.Recorrencias.AsNoTracking()
                join m in db.Documentos on r.ModeloDocumentoId equals m.Id
                join c in db.Clientes on r.ClienteId equals c.Id
                join u in db.Documentos on r.UltimoDocumentoId equals u.Id into us
                from u in us.DefaultIfEmpty()
                orderby r.Activa descending, r.ProximaData
                select new RecorrenciaVista(r, c.Nome, m.NumeroFormatado ?? (m.TipoDoc + " rascunho"), m.Total, u.NumeroFormatado);
        return await q.ToListAsync(ct);
    }

    public Task<Recorrencia?> ObterAsync(int id, CancellationToken ct = default) =>
        db.Recorrencias.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id, ct);

    /// <summary>Recorrências a executar nos próximos dias (para o painel).</summary>
    public async Task<List<RecorrenciaVista>> ProximasAsync(int dias, CancellationToken ct = default)
    {
        var limite = Hoje().AddDays(dias);
        return (await ListarAsync(ct)).Where(r => r.Recorrencia.Activa && r.Recorrencia.ProximaData <= limite).ToList();
    }

    public async Task<(Resultado Resultado, int? Id)> CriarAsync(int modeloDocumentoId, DadosRecorrencia d, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Automacao.Gerir)) return (Resultado.Falha("O seu perfil não permite criar automações."), null);
        var modelo = await documentos.ObterAsync(modeloDocumentoId, ct);
        if (modelo is null) return (Resultado.Falha("Documento modelo não encontrado."), null);
        if (modelo.TipoDoc is not (TipoDocumento.FT or TipoDocumento.FR)) return (Resultado.Falha("Só facturas e facturas-recibo podem ser recorrentes."), null);
        var erros = Validar(d);
        if (erros.Count > 0) return (Resultado.Falha([.. erros]), null);

        var r = new Recorrencia { ModeloDocumentoId = modelo.Id, ClienteId = modelo.ClienteId };
        Aplicar(r, d);
        db.Recorrencias.Add(r);
        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(Recorrencia), r.Id.ToString(), "Recorrencia.Criar", null, new { r.Descricao, Periodicidade = r.Periodicidade.ToString(), r.ProximaData, modelo.NumeroFormatado });
        await db.SaveChangesAsync(ct);
        return (Resultado.Ok(), r.Id);
    }

    public async Task<Resultado> ActualizarAsync(int id, DadosRecorrencia d, bool activa, CancellationToken ct = default)
    {
        if (!utilizador.TemPermissao(Permissoes.Automacao.Gerir)) return Resultado.Falha("O seu perfil não permite alterar automações.");
        var erros = Validar(d);
        if (erros.Count > 0) return Resultado.Falha([.. erros]);
        var r = await db.Recorrencias.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Resultado.Falha("Recorrência não encontrada.");
        var antes = new { r.Descricao, r.Periodicidade, r.ProximaData, r.DataFim, r.EmitirAutomaticamente, r.EnviarEmail, r.Activa };
        Aplicar(r, d);
        r.Activa = activa;
        auditoria.Registar(nameof(Recorrencia), id.ToString(), "Recorrencia.Alterar", antes, new { r.Descricao, r.Periodicidade, r.ProximaData, r.DataFim, r.EmitirAutomaticamente, r.EnviarEmail, r.Activa });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    /// <summary>Executa as recorrências com data até hoje. Chamado pelo jobs/tick (no máximo uma execução por recorrência).</summary>
    public async Task<int> ProcessarAsync(CancellationToken ct = default)
    {
        var hoje = Hoje();
        var devidas = await db.Recorrencias.AsNoTracking().Where(r => r.Activa && r.ProximaData <= hoje).Select(r => r.Id).ToListAsync(ct);
        var feitas = 0;
        foreach (var id in devidas)
            if (await ExecutarAsync(id, ct) is { Sucesso: true }) feitas++;
        return feitas;
    }

    /// <summary>Gera agora o documento desta recorrência e avança a próxima data.</summary>
    public async Task<Resultado> ExecutarAsync(int id, CancellationToken ct = default)
    {
        var r = await db.Recorrencias.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (r is null || !r.Activa) return Resultado.Falha("Recorrência inexistente ou inactiva.");

        var (dup, docId) = await documentos.DuplicarAsync(r.ModeloDocumentoId, ct);
        r = await db.Recorrencias.SingleAsync(x => x.Id == id, ct);
        r.UltimaExecucao = relogio.GetUtcNow().UtcDateTime;
        Resultado resultado;
        if (!dup.Sucesso || docId is null)
        {
            r.UltimoErro = string.Join(" ", dup.Erros);
            resultado = dup;
        }
        else
        {
            r.UltimoDocumentoId = docId;
            r.UltimoErro = null;
            resultado = Resultado.Ok();
            if (r.EmitirAutomaticamente)
            {
                var e = await emissao.EmitirAsync(docId.Value, "Factura recorrente", ct);
                r = await db.Recorrencias.SingleAsync(x => x.Id == id, ct);
                if (e.Tipo != ResultadoEmissaoTipo.Emitido)
                {
                    r.UltimoErro = "Ficou em rascunho: " + string.Join(" ", e.Resultado.Erros);
                }
                else if (r.EnviarEmail)
                {
                    var (env, _) = await comunicacao.EnviarComModeloAsync(docId.Value, ModeloEmail, ct);
                    r = await db.Recorrencias.SingleAsync(x => x.Id == id, ct);
                    if (!env.Sucesso) r.UltimoErro = "Emitida, mas o email não saiu: " + string.Join(" ", env.Erros);
                }
            }
        }

        r.ProximaData = r.Seguinte(r.ProximaData);
        if (r.DataFim is { } fim && r.ProximaData > fim) r.Activa = false;
        auditoria.Registar(nameof(Recorrencia), id.ToString(), "Recorrencia.Executar", null, new { r.UltimoDocumentoId, r.UltimoErro, r.ProximaData });
        await db.SaveChangesAsync(ct);
        return resultado;
    }

    private DateOnly Hoje() => DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime.AddHours(2));

    private static List<string> Validar(DadosRecorrencia d)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Descricao)) erros.Add("Dê uma descrição (ex.: \"Avença mensal de manutenção\").");
        if (d.DataFim is { } fim && fim < d.ProximaData) erros.Add("A data de fim não pode ser anterior à próxima emissão.");
        return erros;
    }

    private static void Aplicar(Recorrencia r, DadosRecorrencia d)
    {
        r.Descricao = d.Descricao.Trim();
        r.Periodicidade = d.Periodicidade;
        r.ProximaData = d.ProximaData;
        r.DataFim = d.DataFim;
        r.EmitirAutomaticamente = d.EmitirAutomaticamente;
        r.EnviarEmail = d.EnviarEmail;
    }
}
