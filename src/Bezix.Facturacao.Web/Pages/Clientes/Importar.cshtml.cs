using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Importacao;
using Bezix.Facturacao.Domain.Seguranca;
using Bezix.Facturacao.Web.Pages.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Clientes;

[Authorize(Policy = Permissoes.Clientes.Gerir)]
[RequestSizeLimit(6 * 1024 * 1024)]
public class ImportarModel(ServicoImportacao importacao, IArmazenamentoFicheiros ficheiros) : PaginaImportacao(ficheiros)
{
    public override string Titulo => "Importar clientes";
    public override string NomeRegistos => "Clientes";
    public override string ChaveDuplicado => "um cliente com o mesmo NUIT";
    public override string NomeModelo => "modelo-clientes.csv";
    public override string ConteudoModelo => ServicoImportacao.ModeloClientes;
    public override string PaginaLista => "/Clientes/Index";

    protected override Task<RelatorioImportacao> AnalisarAsync(byte[] csv, ModoDuplicados modo) => importacao.AnalisarClientesAsync(csv, modo);
    protected override Task<RelatorioImportacao> ImportarAsync(byte[] csv, ModoDuplicados modo) => importacao.ImportarClientesAsync(csv, modo);
}
