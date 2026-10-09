using System.Text.Encodings.Web;
using System.Text.Json;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Domain.Auditoria;
using Bezix.Facturacao.Infrastructure.Persistencia;

namespace Bezix.Facturacao.Infrastructure.Servicos;

public sealed class ServicoAuditoria(AppDbContext db, IUtilizadorActual actual, TimeProvider relogio) : IServicoAuditoria
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public void Registar(string entidade, string? entidadeId, string accao, object? antes = null, object? depois = null, string? utilizador = null)
    {
        db.Auditoria.Add(new RegistoAuditoria
        {
            Entidade = entidade,
            EntidadeId = entidadeId,
            Accao = accao,
            Antes = Serializar(antes),
            Depois = Serializar(depois),
            Utilizador = utilizador ?? actual.Email ?? "sistema",
            Ip = actual.Ip,
            Data = relogio.GetUtcNow().UtcDateTime,
        });
    }

    private static string? Serializar(object? valor) => valor switch
    {
        null => null,
        string s => s,
        _ => JsonSerializer.Serialize(valor, Json),
    };
}
