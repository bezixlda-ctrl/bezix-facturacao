using System.Text;
using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Importacao;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Pages.Shared;

/// <summary>
/// Importação CSV em dois passos: 1) enviar e analisar (não grava), 2) confirmar e importar.
/// Entre os dois passos o ficheiro fica em App_Data/uploads/importacoes, identificado por um nome aleatório.
/// </summary>
public abstract partial class PaginaImportacao(IArmazenamentoFicheiros ficheiros) : PaginaBase
{
    public const long TamanhoMaximo = 5 * 1024 * 1024;

    [BindProperty] public IFormFile? Ficheiro { get; set; }
    [BindProperty] public ModoDuplicados Modo { get; set; } = ModoDuplicados.Ignorar;
    [BindProperty] public string? Token { get; set; }

    public RelatorioImportacao? Relatorio { get; private set; }

    public abstract string Titulo { get; }
    public abstract string NomeRegistos { get; }
    public abstract string ChaveDuplicado { get; }
    public abstract string NomeModelo { get; }
    public abstract string ConteudoModelo { get; }
    public abstract string PaginaLista { get; }

    protected abstract Task<RelatorioImportacao> AnalisarAsync(byte[] csv, ModoDuplicados modo);
    protected abstract Task<RelatorioImportacao> ImportarAsync(byte[] csv, ModoDuplicados modo);

    public void OnGet() { }

    /// <summary>Modelo em CSV com BOM UTF-8 e ";" — abre directamente no Excel em português.</summary>
    public IActionResult OnGetModelo() =>
        File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ConteudoModelo)).ToArray(), "text/csv; charset=utf-8", NomeModelo);

    public async Task<IActionResult> OnPostAnalisarAsync()
    {
        if (Ficheiro is null || Ficheiro.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Escolha um ficheiro CSV.");
            return Page();
        }
        if (Ficheiro.Length > TamanhoMaximo)
        {
            ModelState.AddModelError(string.Empty, "O ficheiro tem mais de 5 MB. Divida-o em ficheiros mais pequenos.");
            return Page();
        }
        if (!Ficheiro.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) && !Ficheiro.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(string.Empty, "Use um ficheiro .csv. No Excel: Ficheiro → Guardar como → CSV (separado por vírgulas).");
            return Page();
        }

        using var memoria = new MemoryStream();
        await Ficheiro.CopyToAsync(memoria);
        var bytes = memoria.ToArray();
        if (bytes.AsSpan(0, Math.Min(bytes.Length, 4)).StartsWith("PK"u8))
        {
            ModelState.AddModelError(string.Empty, "Este ficheiro é um livro do Excel (.xlsx) com outro nome. Guarde-o como CSV no Excel e tente outra vez.");
            return Page();
        }

        Relatorio = await AnalisarAsync(bytes, Modo);
        if (Relatorio.PodeImportar)
            Token = await ficheiros.GuardarAsync("importacoes", ".csv", bytes);
        return Page();
    }

    public async Task<IActionResult> OnPostImportarAsync()
    {
        if (Token is null || !TokenValido().IsMatch(Token))
            return RecomecarComErro();

        byte[] bytes;
        await using (var stream = ficheiros.Abrir(Token))
        {
            if (stream is null) return RecomecarComErro();
            using var memoria = new MemoryStream();
            await stream.CopyToAsync(memoria);
            bytes = memoria.ToArray();
        }

        var r = await ImportarAsync(bytes, Modo);
        ficheiros.Apagar(Token);
        if (r.ErroFicheiro is not null)
        {
            Erro(r.ErroFicheiro);
            return RedirectToPage();
        }

        var partes = new List<string>();
        if (r.Novos > 0) partes.Add($"{r.Novos} {(r.Novos == 1 ? "criado" : "criados")}");
        if (r.Actualizados > 0) partes.Add($"{r.Actualizados} {(r.Actualizados == 1 ? "actualizado" : "actualizados")}");
        if (r.Ignorados + r.Erros > 0) partes.Add($"{r.Ignorados + r.Erros} {(r.Ignorados + r.Erros == 1 ? "linha não importada" : "linhas não importadas")}");
        return RedirecionarComSucesso($"Importação concluída: {string.Join(", ", partes)}.", PaginaLista);
    }

    private IActionResult RecomecarComErro()
    {
        Erro("O ficheiro analisado já não está disponível. Envie-o outra vez.");
        return RedirectToPage();
    }

    [GeneratedRegex(@"^importacoes/\d{4}/[0-9a-f]{32}\.csv$")]
    private static partial Regex TokenValido();
}
