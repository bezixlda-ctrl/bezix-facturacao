using Bezix.Facturacao.Application.Automacao;
using Bezix.Facturacao.Application.Documentos;
using Bezix.Facturacao.Domain.Automacao;
using Bezix.Facturacao.Domain.Documentos;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Automacao;

/// <summary>Criar (a partir de uma factura: <c>?documentoId=</c>) ou editar uma factura recorrente.</summary>
[Authorize(Policy = Permissoes.Automacao.Gerir)]
public class RecorrenciaModel(ServicoRecorrencias recorrencias, ServicoDocumentos documentos) : PaginaBase
{
    [BindProperty(SupportsGet = true)] public int? Id { get; set; }
    [BindProperty(SupportsGet = true)] public int? DocumentoId { get; set; }

    [BindProperty] public string Descricao { get; set; } = "";
    [BindProperty] public Periodicidade Periodicidade { get; set; } = Periodicidade.Mensal;
    [BindProperty] public DateOnly ProximaData { get; set; }
    [BindProperty] public DateOnly? DataFim { get; set; }
    [BindProperty] public bool EmitirAutomaticamente { get; set; }
    [BindProperty] public bool EnviarEmail { get; set; } = true;
    [BindProperty] public bool Activa { get; set; } = true;

    public Documento? Modelo { get; private set; }
    public Recorrencia? Existente { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        if (Existente is { } r)
        {
            Descricao = r.Descricao;
            Periodicidade = r.Periodicidade;
            ProximaData = r.ProximaData;
            DataFim = r.DataFim;
            EmitirAutomaticamente = r.EmitirAutomaticamente;
            EnviarEmail = r.EnviarEmail;
            Activa = r.Activa;
        }
        else
        {
            var m = Modelo!;
            Descricao = $"{m.ClienteNome} · {m.Linhas.FirstOrDefault()?.Descricao}".TrimEnd(' ', '·');
            if (Descricao.Length > 200) Descricao = Descricao[..200];
            ProximaData = m.Data.AddMonths(1);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await CarregarAsync()) return NotFound();
        var dados = new DadosRecorrencia(Descricao, Periodicidade, ProximaData, DataFim, EmitirAutomaticamente, EnviarEmail);
        if (Existente is null)
        {
            var (r, id) = await recorrencias.CriarAsync(Modelo!.Id, dados);
            if (r.Sucesso) return RedirecionarComSucesso("Factura recorrente criada.", null, new { id, documentoId = (int?)null });
            AdicionarErros(r);
        }
        else
        {
            var r = await recorrencias.ActualizarAsync(Existente.Id, dados, Activa);
            if (r.Sucesso) return RedirecionarComSucesso("Recorrência guardada.", "Index");
            AdicionarErros(r);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostExecutarAsync()
    {
        if (Id is not { } id) return NotFound();
        var r = await recorrencias.ExecutarAsync(id);
        if (r.Sucesso)
        {
            var actual = await recorrencias.ObterAsync(id);
            Sucesso(actual?.UltimoErro is { } aviso ? "Documento gerado. " + aviso : "Documento gerado.");
            if (actual?.UltimoDocumentoId is { } doc) return RedirectToPage("/Documentos/Detalhe", new { id = doc });
        }
        else Erro(string.Join(" ", r.Erros));
        return RedirectToPage(new { id });
    }

    private async Task<bool> CarregarAsync()
    {
        if (Id is { } id)
        {
            Existente = await recorrencias.ObterAsync(id);
            if (Existente is null) return false;
            Modelo = await documentos.ObterAsync(Existente.ModeloDocumentoId);
            return Modelo is not null;
        }
        if (DocumentoId is not { } docId) return false;
        Modelo = await documentos.ObterAsync(docId);
        return Modelo is not null;
    }
}
