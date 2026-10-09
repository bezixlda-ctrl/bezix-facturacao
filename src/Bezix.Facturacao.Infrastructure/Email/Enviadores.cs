using Bezix.Facturacao.Application.Abstracoes;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Bezix.Facturacao.Infrastructure.Email;

public sealed class OpcoesEmail
{
    /// <summary>Servidor SMTP (Winhost, SendGrid, Brevo). Vazio = grava os emails em App_Data/emails (desenvolvimento).</summary>
    public string? Servidor { get; set; }
    public int Porta { get; set; } = 587;
    public string? Utilizador { get; set; }
    public string? Password { get; set; }
    public string Remetente { get; set; } = "facturacao@localhost";
    public string? NomeRemetente { get; set; }

    /// <summary>Auto, StartTls, SslOnConnect ou None.</summary>
    public string Seguranca { get; set; } = "Auto";

    /// <summary>Pasta para os .eml quando não há servidor SMTP (relativa ao ContentRoot).</summary>
    public string PastaLocal { get; set; } = "App_Data/emails";
}

internal static class Mensagens
{
    public static MimeMessage Construir(MensagemEmail m, OpcoesEmail o)
    {
        var msg = new MimeMessage();
        var remetente = string.IsNullOrWhiteSpace(o.Remetente) ? "facturacao@localhost" : o.Remetente.Trim();
        msg.From.Add(new MailboxAddress(string.IsNullOrWhiteSpace(o.NomeRemetente) ? remetente : o.NomeRemetente, remetente));
        msg.To.AddRange(Enderecos(m.Para));
        msg.Cc.AddRange(Enderecos(m.Cc));
        msg.Bcc.AddRange(Enderecos(m.Bcc));
        msg.Subject = m.Assunto;

        var corpo = new BodyBuilder { HtmlBody = m.CorpoHtml };
        foreach (var a in m.Anexos) corpo.Attachments.Add(a.Nome, a.Conteudo, ContentType.Parse(a.TipoMime));
        msg.Body = corpo.ToMessageBody();
        return msg;
    }

    private static IEnumerable<MailboxAddress> Enderecos(string? lista) =>
        (lista ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(MailboxAddress.Parse);
}

public sealed class EnviadorSmtp(IOptions<OpcoesEmail> opcoes) : IEnviadorEmail
{
    public async Task EnviarAsync(MensagemEmail mensagem, CancellationToken ct = default)
    {
        var o = opcoes.Value;
        var seguranca = Enum.TryParse<SecureSocketOptions>(o.Seguranca, true, out var s) ? s : SecureSocketOptions.Auto;

        using var cliente = new SmtpClient { Timeout = 30_000 };
        await cliente.ConnectAsync(o.Servidor ?? throw new InvalidOperationException("Email:Servidor não configurado."), o.Porta, seguranca, ct);
        if (!string.IsNullOrEmpty(o.Utilizador)) await cliente.AuthenticateAsync(o.Utilizador, o.Password ?? "", ct);
        await cliente.SendAsync(Mensagens.Construir(mensagem, o), ct);
        await cliente.DisconnectAsync(true, ct);
    }
}

/// <summary>Grava cada email como ficheiro .eml (abre no Outlook/Thunderbird). Usado sem servidor SMTP configurado.</summary>
public sealed class EnviadorPastaLocal(IOptions<OpcoesEmail> opcoes, string contentRoot) : IEnviadorEmail
{
    public string Pasta { get; } = Path.GetFullPath(Path.Combine(contentRoot, opcoes.Value.PastaLocal));

    public async Task EnviarAsync(MensagemEmail mensagem, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Pasta);
        var caminho = Path.Combine(Pasta, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.eml");
        await Mensagens.Construir(mensagem, opcoes.Value).WriteToAsync(caminho, ct);
    }
}
