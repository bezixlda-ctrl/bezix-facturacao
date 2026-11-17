using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Infrastructure.Email;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Bezix.Facturacao.Tests.AmbienteTeste;

/// <summary>Separação teste/produção (decisão D28): o site de teste nunca pode parecer válido nem escrever a clientes reais.</summary>
public class AmbienteTesteTests
{
    [Fact]
    public void Rodape_em_producao_nao_tem_aviso()
    {
        var o = new OpcoesCertificacao();
        Assert.DoesNotContain("TESTE", o.TextoRodape);
        Assert.StartsWith("Software:", o.TextoRodape);
    }

    [Fact]
    public void Rodape_no_ambiente_de_teste_comeca_pelo_aviso()
    {
        var o = new OpcoesCertificacao { AmbienteTeste = true, NumeroCertificado = "123" };
        Assert.StartsWith(OpcoesCertificacao.AvisoTeste, o.TextoRodape);
        Assert.Contains("123/AT", o.TextoRodape);
    }

    [Fact]
    public async Task Email_redireccionado_nao_vai_para_o_cliente()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "bz-email-" + Guid.NewGuid().ToString("N"));
        try
        {
            var enviador = new EnviadorPastaLocal(Options.Create(new OpcoesEmail
            {
                PastaLocal = pasta,
                RedireccionarPara = "testes@bezixcenter.net",
                PrefixoAssunto = "[TESTE] "
            }), Path.GetTempPath());

            await enviador.EnviarAsync(new MensagemEmail("cliente@exemplo.co.mz", "contabilidade@exemplo.co.mz", "oculto@exemplo.co.mz",
                "Factura FT A2026/0001", "<p>Segue a factura.</p>", []));

            var msg = await MimeMessage.LoadAsync(Directory.GetFiles(pasta).Single());
            Assert.Equal("testes@bezixcenter.net", Assert.Single(msg.To.Mailboxes).Address);
            Assert.Empty(msg.Cc);
            Assert.Empty(msg.Bcc);
            Assert.Equal("[TESTE] Factura FT A2026/0001", msg.Subject);
            Assert.Contains("cliente@exemplo.co.mz", msg.HtmlBody);
            Assert.Contains("Segue a factura.", msg.HtmlBody);
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        }
    }

    [Fact]
    public async Task Email_sem_redireccionamento_mantem_destinatarios()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "bz-email-" + Guid.NewGuid().ToString("N"));
        try
        {
            var enviador = new EnviadorPastaLocal(Options.Create(new OpcoesEmail { PastaLocal = pasta }), Path.GetTempPath());
            await enviador.EnviarAsync(new MensagemEmail("cliente@exemplo.co.mz", null, null, "Recibo", "<p>x</p>", []));

            var msg = await MimeMessage.LoadAsync(Directory.GetFiles(pasta).Single());
            Assert.Equal("cliente@exemplo.co.mz", Assert.Single(msg.To.Mailboxes).Address);
            Assert.Equal("Recibo", msg.Subject);
        }
        finally
        {
            if (Directory.Exists(pasta)) Directory.Delete(pasta, true);
        }
    }
}
