using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Bezix.Facturacao.Web.Formatacao;

/// <summary>CSV que o Excel português abre bem: separador ";", vírgula decimal sem separador de milhares, UTF-8 com BOM.</summary>
public sealed class CsvExcel
{
    private readonly StringBuilder _sb = new();

    public CsvExcel(params string[] cabecalho) => Linha(cabecalho);

    public CsvExcel Linha(params object?[] campos)
    {
        for (var i = 0; i < campos.Length; i++)
        {
            if (i > 0) _sb.Append(';');
            var texto = campos[i] switch
            {
                null => "",
                decimal d => d.ToString("0.00", Formato.Cultura),
                DateOnly d => Formato.Data(d),
                DateTime d => Formato.DataHora(d),
                var o => o.ToString() ?? "",
            };
            if (texto.IndexOfAny([';', '"', '\n', '\r']) >= 0 || campos[i] is string) texto = "\"" + texto.Replace("\"", "\"\"") + "\"";
            _sb.Append(texto);
        }
        _sb.Append("\r\n");
        return this;
    }

    public FileContentResult Ficheiro(string nome) =>
        new([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(_sb.ToString())], "text/csv; charset=utf-8") { FileDownloadName = nome };
}
