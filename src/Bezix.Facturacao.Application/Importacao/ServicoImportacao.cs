using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Artigos;
using Bezix.Facturacao.Application.Clientes;
using Bezix.Facturacao.Domain.Artigos;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Comum;
using Bezix.Facturacao.Domain.Fiscal;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Importacao;

public enum EstadoLinha { Novo, Actualizar, Ignorado, Erro }

/// <summary>O que fazer quando a linha corresponde a um registo existente (mesmo NUIT ou mesmo código).</summary>
public enum ModoDuplicados { Ignorar, Actualizar }

public sealed record LinhaAnalisada(int Numero, string Identificacao, EstadoLinha Estado, IReadOnlyList<string> Mensagens);

public sealed record RelatorioImportacao(
    IReadOnlyList<LinhaAnalisada> Linhas,
    IReadOnlyList<string> ColunasIgnoradas,
    string? ErroFicheiro,
    bool Gravado = false)
{
    public int Novos => Linhas.Count(l => l.Estado == EstadoLinha.Novo);
    public int Actualizados => Linhas.Count(l => l.Estado == EstadoLinha.Actualizar);
    public int Ignorados => Linhas.Count(l => l.Estado == EstadoLinha.Ignorado);
    public int Erros => Linhas.Count(l => l.Estado == EstadoLinha.Erro);
    public bool PodeImportar => ErroFicheiro is null && Novos + Actualizados > 0;

    public static RelatorioImportacao Falha(string erro) => new([], [], erro);
}

/// <summary>
/// Importação de clientes e artigos a partir de CSV. Funciona em dois passos com o mesmo ficheiro:
/// analisar (não grava nada) e importar (grava tudo numa única transacção, ou nada).
/// </summary>
public sealed class ServicoImportacao(IAppDbContext db, IUtilizadorActual utilizador, IServicoAuditoria auditoria, IPublicadorEventos eventos, TimeProvider relogio)
{
    // Nomes de coluna aceites (já normalizados: minúsculas, sem acentos nem espaços).
    private static readonly string[] CNome = ["nome", "cliente", "name", "designacao", "razaosocial"];
    private static readonly string[] CTipo = ["tipo", "tipodecliente"];
    private static readonly string[] CNuit = ["nuit", "nif"];
    private static readonly string[] CEndereco = ["endereco", "morada"];
    private static readonly string[] CCidade = ["cidade", "localidade"];
    private static readonly string[] CTelefone = ["telefone", "tel"];
    private static readonly string[] CCelular = ["celular", "telemovel", "movel", "cel"];
    private static readonly string[] CEmail = ["email", "correioelectronico", "correioeletronico"];
    private static readonly string[] CContacto = ["contacto", "att", "pessoadecontacto"];
    private static readonly string[] CPrazo = ["prazo", "prazopagamento", "prazodepagamento", "prazodias"];
    private static readonly string[] CLimite = ["limitecredito", "limitedecredito", "limite"];
    private static readonly string[] CDesconto = ["desconto", "descontopadrao"];
    private static readonly string[] CNotas = ["notas", "observacoes", "obs"];
    private static readonly string[] CEtiquetas = ["etiquetas", "tags", "categoria"];
    private static readonly string[] CEmailFacturacao = ["emailfacturacao", "emaildefacturacao", "emailfaturacao"];

    private static readonly string[] CCodigo = ["codigo", "ref", "referencia", "code"];
    private static readonly string[] CDescricao = ["descricao", "designacao", "artigo", "produto", "nome"];
    private static readonly string[] CTipoArtigo = ["tipo"];
    private static readonly string[] CUnidade = ["unidade", "un", "unid"];
    private static readonly string[] CPreco = ["preco", "precounitario", "pvp", "valor"];
    private static readonly string[] CIva = ["iva", "taxa", "taxaiva", "taxadeiva", "codigoiva"];

    public const string ModeloClientes =
        "Nome;Tipo;NUIT;Endereço;Cidade;Telefone;Celular;Email;Contacto;Prazo pagamento;Limite crédito;Desconto;Etiquetas;Notas\r\n" +
        "Exemplo Comercial, Lda;Empresa;400000000;Av. 24 de Julho, 100;Maputo;+258 21 300 000;+258 84 000 0000;geral@exemplo.co.mz;Sr. João;30;50.000,00;5;revenda;\r\n";

    public const string ModeloArtigos =
        "Código;Descrição;Tipo;Unidade;Preço;IVA\r\n" +
        "SERV-001;Consultoria técnica;Serviço;h;2.500,00;NOR\r\n" +
        "PROD-001;Cabo de rede Cat6 (caixa 305 m);Produto;cx;8.900,00;16\r\n";

    // ------------------------------------------------------------------ Clientes

    public Task<RelatorioImportacao> AnalisarClientesAsync(byte[] csv, ModoDuplicados modo, CancellationToken ct = default) =>
        ProcessarClientesAsync(csv, modo, gravar: false, ct);

    public Task<RelatorioImportacao> ImportarClientesAsync(byte[] csv, ModoDuplicados modo, CancellationToken ct = default) =>
        ProcessarClientesAsync(csv, modo, gravar: true, ct);

    private async Task<RelatorioImportacao> ProcessarClientesAsync(byte[] csv, ModoDuplicados modo, bool gravar, CancellationToken ct)
    {
        if (!TentarLer(csv, out var ficheiro, out var erroLeitura)) return RelatorioImportacao.Falha(erroLeitura!);
        if (!TemColuna(ficheiro, CNome)) return RelatorioImportacao.Falha("O ficheiro não tem a coluna \"Nome\". Use o modelo disponível nesta página.");

        var conhecidas = new[] { CNome, CTipo, CNuit, CEndereco, CCidade, CTelefone, CCelular, CEmail, CContacto, CPrazo, CLimite, CDesconto, CNotas, CEtiquetas, CEmailFacturacao }.SelectMany(x => x).ToHashSet();
        var ignoradas = ficheiro.Cabecalhos.Where(c => c.Length > 0 && !conhecidas.Contains(c)).ToList();

        // Clientes existentes por NUIT e por nome (os sem NUIT só se reconhecem pelo nome).
        var existentes = await db.Clientes.Select(c => new { c.Id, c.Nome, c.Nuit }).ToListAsync(ct);
        var porNuit = existentes.Where(c => c.Nuit != null).GroupBy(c => c.Nuit!).ToDictionary(g => g.Key, g => g.First().Id);
        var porNome = existentes.GroupBy(c => TextoPesquisa.Normalizar(c.Nome)).ToDictionary(g => g.Key, g => g.First().Id);

        var nuitsNoFicheiro = new Dictionary<string, int>();
        var linhas = new List<LinhaAnalisada>();
        var aGravar = new List<(int? Id, DadosCliente Dados)>();

        foreach (var l in ficheiro.Linhas)
        {
            var msgs = new List<string>();
            var nome = l.Valor(CNome) ?? "";
            var nuit = TextoPesquisa.Algarismos(l.Valor(CNuit));

            var tipo = TipoCliente.Empresa;
            var tipoTexto = TextoPesquisa.Normalizar(l.Valor(CTipo));
            if (tipoTexto.Length > 0)
            {
                if (tipoTexto.StartsWith("part") || tipoTexto.StartsWith("sing") || tipoTexto == "individual") tipo = TipoCliente.Particular;
                else if (!tipoTexto.StartsWith("emp") && !tipoTexto.StartsWith("col")) msgs.Add($"Tipo \"{l.Valor(CTipo)}\" desconhecido; use Empresa ou Particular.");
            }

            var prazo = LerInteiro(l.Valor(CPrazo), "prazo de pagamento", msgs);
            var limite = LerDecimal(l.Valor(CLimite), "limite de crédito", msgs);
            var desconto = LerDecimal(l.Valor(CDesconto), "desconto", msgs) ?? 0;

            var dados = new DadosCliente(nome, tipo, nuit.Length == 0 ? null : nuit, l.Valor(CEndereco), l.Valor(CCidade),
                l.Valor(CTelefone), l.Valor(CCelular), l.Valor(CEmail), l.Valor(CContacto), prazo, limite, desconto,
                l.Valor(CNotas), l.Valor(CEtiquetas), l.Valor(CEmailFacturacao), null, true, null);
            msgs.AddRange(ServicoClientes.Validar(dados));

            if (nuit.Length > 0)
            {
                if (nuitsNoFicheiro.TryGetValue(nuit, out var primeira))
                    msgs.Add($"NUIT repetido no ficheiro (já aparece na linha {primeira}).");
                else nuitsNoFicheiro[nuit] = l.Numero;
            }

            var identificacao = nuit.Length > 0 ? $"{nome} ({nuit})" : nome;
            if (msgs.Count > 0)
            {
                linhas.Add(new(l.Numero, identificacao, EstadoLinha.Erro, msgs));
                continue;
            }

            if (nuit.Length > 0 && porNuit.TryGetValue(nuit, out var idExistente))
            {
                if (modo == ModoDuplicados.Actualizar)
                {
                    linhas.Add(new(l.Numero, identificacao, EstadoLinha.Actualizar, ["Já existe um cliente com este NUIT: os dados vão ser actualizados."]));
                    aGravar.Add((idExistente, dados));
                }
                else linhas.Add(new(l.Numero, identificacao, EstadoLinha.Ignorado, ["Já existe um cliente com este NUIT."]));
                continue;
            }
            if (nuit.Length == 0 && porNome.ContainsKey(TextoPesquisa.Normalizar(nome)))
            {
                linhas.Add(new(l.Numero, identificacao, EstadoLinha.Ignorado, ["Já existe um cliente com este nome. Sem NUIT não é possível confirmar que é o mesmo; crie-o à mão se for outro."]));
                continue;
            }

            linhas.Add(new(l.Numero, identificacao, EstadoLinha.Novo, []));
            aGravar.Add((null, dados));
        }

        if (!gravar || aGravar.Count == 0) return new RelatorioImportacao(linhas, ignoradas, null);

        var ids = aGravar.Where(x => x.Id is not null).Select(x => x.Id!.Value).ToList();
        var actualizar = await db.Clientes.Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        var criados = new List<Cliente>();
        foreach (var (id, d) in aGravar)
        {
            var c = id is null ? new Cliente { ResponsavelId = utilizador.Id } : actualizar[id.Value];
            if (id is null) { db.Clientes.Add(c); criados.Add(c); }
            c.Nome = d.Nome.Trim();
            c.Tipo = d.Tipo;
            c.Nuit = d.Nuit;
            // Na actualização, colunas vazias no ficheiro não apagam dados que já existem.
            c.Endereco = d.Endereco ?? c.Endereco;
            c.Cidade = d.Cidade ?? c.Cidade;
            c.Telefone = d.Telefone ?? c.Telefone;
            c.Celular = d.Celular ?? c.Celular;
            c.TelefonesPesquisa = string.Join(" ", new[] { c.Telefone, c.Celular }.Select(TextoPesquisa.Algarismos).Where(x => x.Length > 0));
            c.Email = d.Email ?? c.Email;
            c.Contacto = d.Contacto ?? c.Contacto;
            c.PrazoPagamentoDias = d.PrazoPagamentoDias ?? c.PrazoPagamentoDias;
            c.LimiteCredito = d.LimiteCredito ?? c.LimiteCredito;
            if (d.DescontoPadraoPct != 0 || id is null) c.DescontoPadraoPct = d.DescontoPadraoPct;
            c.Notas = d.Notas ?? c.Notas;
            var etiquetas = string.Join(",", Cliente.NormalizarEtiquetas(string.Join(",", c.Etiquetas, d.Etiquetas)));
            c.Etiquetas = etiquetas.Length == 0 ? null : etiquetas;
            c.EmailFacturacao = d.EmailFacturacao ?? c.EmailFacturacao;
        }

        var relatorio = new RelatorioImportacao(linhas, ignoradas, null, Gravado: true);
        auditoria.Registar(nameof(Cliente), null, "Cliente.Importar", null,
            new { relatorio.Novos, relatorio.Actualizados, relatorio.Ignorados, relatorio.Erros, Modo = modo.ToString() });
        await db.SaveChangesAsync(ct);
        if (criados.Count > 0)
        {
            foreach (var c in criados) eventos.Publicar(Domain.Integracoes.EventosWebhook.ClienteCriado, ServicoClientes.DadosEvento(c));
            await db.SaveChangesAsync(ct);
        }
        return relatorio;
    }

    // ------------------------------------------------------------------ Artigos

    public Task<RelatorioImportacao> AnalisarArtigosAsync(byte[] csv, ModoDuplicados modo, CancellationToken ct = default) =>
        ProcessarArtigosAsync(csv, modo, gravar: false, ct);

    public Task<RelatorioImportacao> ImportarArtigosAsync(byte[] csv, ModoDuplicados modo, CancellationToken ct = default) =>
        ProcessarArtigosAsync(csv, modo, gravar: true, ct);

    private async Task<RelatorioImportacao> ProcessarArtigosAsync(byte[] csv, ModoDuplicados modo, bool gravar, CancellationToken ct)
    {
        if (!TentarLer(csv, out var ficheiro, out var erroLeitura)) return RelatorioImportacao.Falha(erroLeitura!);
        if (!TemColuna(ficheiro, CCodigo) || !TemColuna(ficheiro, CDescricao))
            return RelatorioImportacao.Falha("O ficheiro tem de ter as colunas \"Código\" e \"Descrição\". Use o modelo disponível nesta página.");

        var conhecidas = new[] { CCodigo, CDescricao, CTipoArtigo, CUnidade, CPreco, CIva }.SelectMany(x => x).ToHashSet();
        var ignoradas = ficheiro.Cabecalhos.Where(c => c.Length > 0 && !conhecidas.Contains(c)).ToList();

        var taxas = await db.TaxasIva.AsNoTracking().ToListAsync(ct);
        var hoje = DateOnly.FromDateTime(relogio.GetUtcNow().UtcDateTime);
        var existentes = await db.Artigos.Select(a => new { a.Id, a.Codigo }).ToDictionaryAsync(a => a.Codigo, a => a.Id, ct);

        var codigosNoFicheiro = new Dictionary<string, int>();
        var linhas = new List<LinhaAnalisada>();
        var aGravar = new List<(int? Id, DadosArtigo Dados)>();

        foreach (var l in ficheiro.Linhas)
        {
            var msgs = new List<string>();
            var codigo = (l.Valor(CCodigo) ?? "").ToUpperInvariant();
            var descricao = l.Valor(CDescricao) ?? "";

            var tipo = TipoArtigo.Servico;
            var tipoTexto = TextoPesquisa.Normalizar(l.Valor(CTipoArtigo));
            if (tipoTexto.StartsWith("prod") || tipoTexto.StartsWith("merc") || tipoTexto.StartsWith("bem")) tipo = TipoArtigo.Produto;
            else if (tipoTexto.Length > 0 && !tipoTexto.StartsWith("serv")) msgs.Add($"Tipo \"{l.Valor(CTipoArtigo)}\" desconhecido; use Produto ou Serviço.");

            var preco = LerDecimal(l.Valor(CPreco), "preço", msgs) ?? 0;
            var codigoTaxa = ResolverTaxa(l.Valor(CIva), taxas, hoje, msgs);

            var dados = new DadosArtigo(codigo, descricao, tipo, l.Valor(CUnidade) ?? "un", preco, codigoTaxa ?? "");
            msgs.AddRange(ServicoArtigos.Validar(dados));
            if (codigo.Length > 0)
            {
                if (codigosNoFicheiro.TryGetValue(codigo, out var primeira)) msgs.Add($"Código repetido no ficheiro (já aparece na linha {primeira}).");
                else codigosNoFicheiro[codigo] = l.Numero;
            }

            var identificacao = $"{codigo} · {descricao}";
            if (msgs.Count > 0)
            {
                linhas.Add(new(l.Numero, identificacao, EstadoLinha.Erro, msgs));
                continue;
            }

            if (existentes.TryGetValue(codigo, out var idExistente))
            {
                if (modo == ModoDuplicados.Actualizar)
                {
                    linhas.Add(new(l.Numero, identificacao, EstadoLinha.Actualizar, ["Já existe um artigo com este código: os dados vão ser actualizados."]));
                    aGravar.Add((idExistente, dados));
                }
                else linhas.Add(new(l.Numero, identificacao, EstadoLinha.Ignorado, ["Já existe um artigo com este código."]));
                continue;
            }

            linhas.Add(new(l.Numero, identificacao, EstadoLinha.Novo, []));
            aGravar.Add((null, dados));
        }

        if (!gravar || aGravar.Count == 0) return new RelatorioImportacao(linhas, ignoradas, null);

        var ids = aGravar.Where(x => x.Id is not null).Select(x => x.Id!.Value).ToList();
        var actualizar = await db.Artigos.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var (id, d) in aGravar)
        {
            var a = id is null ? new Artigo() : actualizar[id.Value];
            if (id is null) db.Artigos.Add(a);
            a.Codigo = d.Codigo;
            a.Descricao = d.Descricao.Trim();
            a.Tipo = d.Tipo;
            a.Unidade = d.Unidade.Trim();
            a.Preco = d.Preco;
            a.CodigoTaxaIva = d.CodigoTaxaIva;
        }

        var relatorio = new RelatorioImportacao(linhas, ignoradas, null, Gravado: true);
        auditoria.Registar(nameof(Artigo), null, "Artigo.Importar", null,
            new { relatorio.Novos, relatorio.Actualizados, relatorio.Ignorados, relatorio.Erros, Modo = modo.ToString() });
        await db.SaveChangesAsync(ct);
        return relatorio;
    }

    // ------------------------------------------------------------------ Auxiliares

    private static bool TentarLer(byte[] csv, out FicheiroCsv ficheiro, out string? erro)
    {
        ficheiro = new FicheiroCsv([], [], ';');
        erro = null;
        if (csv.Length == 0) { erro = "O ficheiro está vazio."; return false; }
        try
        {
            ficheiro = LeitorCsv.Ler(csv);
        }
        catch (InvalidDataException ex)
        {
            erro = ex.Message;
            return false;
        }
        if (ficheiro.Linhas.Count == 0) { erro = "O ficheiro não tem linhas de dados depois do cabeçalho."; return false; }
        return true;
    }

    private static bool TemColuna(FicheiroCsv f, string[] nomes) => f.Cabecalhos.Any(nomes.Contains);

    private static decimal? LerDecimal(string? texto, string campo, List<string> msgs)
    {
        if (texto is null) return null;
        if (Numeros.TentarLerDecimal(texto, out var v)) return v;
        msgs.Add($"O {campo} \"{texto}\" não é um número.");
        return null;
    }

    private static int? LerInteiro(string? texto, string campo, List<string> msgs)
    {
        var v = LerDecimal(texto, campo, msgs);
        if (v is null) return null;
        if (v != decimal.Truncate(v.Value)) { msgs.Add($"O {campo} tem de ser um número inteiro de dias."); return null; }
        return (int)v.Value;
    }

    /// <summary>Aceita o código da taxa ("NOR") ou a percentagem ("16", "16%", "0") de uma taxa em vigor.</summary>
    private static string? ResolverTaxa(string? texto, List<TaxaIva> taxas, DateOnly hoje, List<string> msgs)
    {
        var vigentes = taxas.Where(t => t.EstaVigente(hoje)).ToList();
        if (texto is null)
            return vigentes.Where(t => !t.IsIsencao).OrderByDescending(t => t.Percentagem).FirstOrDefault()?.Codigo;

        var codigo = texto.Trim().ToUpperInvariant();
        if (taxas.Any(t => t.Codigo == codigo)) return codigo;

        if (Numeros.TentarLerDecimal(texto, out var pct))
        {
            var porPct = vigentes.Where(t => t.Percentagem == pct).ToList();
            if (porPct.Count == 1) return porPct[0].Codigo;
            if (porPct.Count > 1) { msgs.Add($"Há várias taxas de {pct}% em vigor; indique o código (ex.: {string.Join(", ", porPct.Select(t => t.Codigo))})."); return null; }
        }
        msgs.Add($"Taxa de IVA \"{texto}\" não encontrada. Use o código ({string.Join(", ", vigentes.Select(t => t.Codigo))}) ou a percentagem.");
        return null;
    }
}
