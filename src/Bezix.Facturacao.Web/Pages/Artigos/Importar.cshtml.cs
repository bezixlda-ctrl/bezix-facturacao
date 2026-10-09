using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Importacao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Artigos;

[Authorize(Policy = Permissoes.Artigos.Gerir)]
[RequestSizeLimit(6 * 1024 * 1024)]
public class ImportarModel(ServicoImportacao importacao, IArmazenamentoFicheiros ficheiros) : PaginaImportacao(ficheiros)
{
    public override string Titulo => "Importar artigos";
    public override string NomeRegistos => "Artigos";
    public override string ChaveDuplicado => "um artigo com o mesmo código";
    public override string NomeModelo => "modelo-artigos.csv";
    public override string ConteudoModelo => ServicoImportacao.ModeloArtigos;
    public override string PaginaLista => "/Artigos/Index";

    protected override Task<RelatorioImportacao> AnalisarAsync(byte[] csv, ModoDuplicados modo) => importacao.AnalisarArtigosAsync(csv, modo);
    protected override Task<RelatorioImportacao> ImportarAsync(byte[] csv, ModoDuplicados modo) => importacao.ImportarArtigosAsync(csv, modo);
}
