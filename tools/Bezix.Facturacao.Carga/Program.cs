// Teste de carga da Bezix Facturação (fase 6): N utilizadores em simultâneo a criar e emitir facturas pela API,
// todos na mesma série (o pior caso: a numeração é sequencial e bloqueia a série), e a pedir o PDF.
// Metas (especificação, secção 12): emissão < 500 ms e PDF < 2 s com 50 utilizadores simultâneos.
//
// Uso (só contra um ambiente de testes: cria clientes, facturas e chaves de API):
//   dotnet run --project tools/Bezix.Facturacao.Carga -- --url http://localhost:5011 --ligacao "<connection string>" [--utilizadores 50] [--segundos 60]
//
// A ligação à base de dados serve só para criar uma chave de API por utilizador virtual (o limite é 60 pedidos/minuto
// por chave) e revogá-las no fim.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

var opcoes = Argumentos(args);
var url = opcoes.GetValueOrDefault("url") ?? "http://localhost:5011";
var ligacao = opcoes.GetValueOrDefault("ligacao") ?? throw new ArgumentException("Falta --ligacao.");
var utilizadores = int.Parse(opcoes.GetValueOrDefault("utilizadores") ?? "50");
var segundos = int.Parse(opcoes.GetValueOrDefault("segundos") ?? "60");
var pausa = TimeSpan.FromMilliseconds(int.Parse(opcoes.GetValueOrDefault("pausa-ms") ?? "1000"));
// "rajada": todos começam ao mesmo tempo e emitem em sincronia (pior caso). Por omissão, cada utilizador começa num
// momento aleatório e as pausas variam ±50%, como pessoas reais a trabalhar ao mesmo tempo.
var rajada = opcoes.GetValueOrDefault("modo") == "rajada";

Console.WriteLine($"Teste de carga: {utilizadores} utilizadores durante {segundos} s contra {url} " +
    $"(uma emissão a cada {pausa.TotalSeconds:0.#} s por utilizador, {(rajada ? "em rajada sincronizada" : "desfasadas")})");

var marca = Guid.NewGuid().ToString("N")[..8];
var chaves = await CriarChavesAsync(ligacao, utilizadores, marca);
var medicoes = new ConcurrentDictionary<string, ConcurrentBag<double>>();
var erros = new ConcurrentDictionary<string, int>();

try
{
    using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(60) };
    var clienteId = await CriarClienteAsync(http, chaves[0], marca);
    Console.WriteLine($"Cliente de teste {clienteId}; a aquecer...");
    await EmitirAsync(http, chaves[0], clienteId, null, null);

    using var fim = new CancellationTokenSource(TimeSpan.FromSeconds(segundos));
    var relogio = Stopwatch.StartNew();
    var tarefas = chaves.Select((chave, i) => Task.Run(async () =>
    {
        var n = 0;
        if (!rajada) try { await Task.Delay(Random.Shared.Next((int)pausa.TotalMilliseconds), fim.Token); } catch (OperationCanceledException) { }
        while (!fim.IsCancellationRequested)
        {
            var id = await EmitirAsync(http, chave, clienteId, medicoes, erros);
            // Um em cada cinco também abre o PDF (como quem imprime depois de emitir).
            if (id is not null && n++ % 5 == 0) await MedirAsync(http, chave, HttpMethod.Get, $"/api/v1/documentos/{id}/pdf", null, "PDF", medicoes, erros);
            var espera = rajada ? pausa : TimeSpan.FromMilliseconds(pausa.TotalMilliseconds * (0.5 + Random.Shared.NextDouble()));
            try { await Task.Delay(espera, fim.Token); } catch (OperationCanceledException) { }
        }
    })).ToList();
    await Task.WhenAll(tarefas);
    relogio.Stop();

    Console.WriteLine();
    Console.WriteLine($"{"Operação",-10}{"Pedidos",9}{"p50 ms",9}{"p95 ms",9}{"p99 ms",9}{"máx ms",9}  Meta");
    foreach (var (nome, bag) in medicoes.OrderBy(m => m.Key))
    {
        var v = bag.Order().ToArray();
        var meta = nome == "Emitir" ? 500 : 2000;
        var p95 = Percentil(v, 95);
        Console.WriteLine($"{nome,-10}{v.Length,9}{Percentil(v, 50),9:0}{p95,9:0}{Percentil(v, 99),9:0}{v[^1],9:0}  < {meta} ms (p95) {(p95 < meta ? "OK" : "FALHA")}");
    }
    var total = medicoes.Values.Sum(b => b.Count);
    Console.WriteLine($"Débito: {total / relogio.Elapsed.TotalSeconds:0.0} pedidos/s; erros: {(erros.IsEmpty ? "nenhum" : string.Join(", ", erros.Select(e => $"{e.Key} ×{e.Value}")))}");
    return erros.IsEmpty ? 0 : 1;
}
finally
{
    await RevogarChavesAsync(ligacao, marca);
}

static async Task<int?> EmitirAsync(HttpClient http, string chave, int clienteId,
    ConcurrentDictionary<string, ConcurrentBag<double>>? medicoes, ConcurrentDictionary<string, int>? erros)
{
    var corpo = new
    {
        tipo = "FT", clienteId, descontoGlobalPct = 0, emitir = true,
        linhas = new[] { new { descricao = "Teste de carga", quantidade = 1, precoUnitario = 1000, descontoPct = 0, codigoTaxaIva = "NOR" } },
    };
    var r = await MedirAsync(http, chave, HttpMethod.Post, "/api/v1/documentos", corpo, "Emitir", medicoes, erros);
    if (r is null) return null;
    using var json = JsonDocument.Parse(r);
    return json.RootElement.GetProperty("id").GetInt32();
}

static async Task<string?> MedirAsync(HttpClient http, string chave, HttpMethod metodo, string caminho, object? corpo, string nome,
    ConcurrentDictionary<string, ConcurrentBag<double>>? medicoes, ConcurrentDictionary<string, int>? erros)
{
    using var pedido = new HttpRequestMessage(metodo, caminho);
    pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chave);
    if (corpo is not null)
    {
        pedido.Content = JsonContent.Create(corpo);
        pedido.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
    }
    var t = Stopwatch.StartNew();
    try
    {
        using var resposta = await http.SendAsync(pedido);
        var texto = await resposta.Content.ReadAsStringAsync();
        t.Stop();
        if (!resposta.IsSuccessStatusCode)
        {
            erros?.AddOrUpdate($"{nome} {(int)resposta.StatusCode}", 1, (_, n) => n + 1);
            if (erros is null) throw new InvalidOperationException($"{nome}: {(int)resposta.StatusCode} {texto}");
            return null;
        }
        medicoes?.GetOrAdd(nome, _ => []).Add(t.Elapsed.TotalMilliseconds);
        return texto;
    }
    catch (HttpRequestException ex) when (erros is not null)
    {
        erros.AddOrUpdate($"{nome} {ex.Message}", 1, (_, n) => n + 1);
        return null;
    }
}

static async Task<int> CriarClienteAsync(HttpClient http, string chave, string marca)
{
    var corpo = new { nome = $"Cliente carga {marca}", tipo = "Empresa", nuit = $"4{RandomNumberGenerator.GetInt32(10_000_000, 99_999_999)}", aceitarNuitDuplicado = true };
    var texto = await MedirAsync(http, chave, HttpMethod.Post, "/api/v1/clientes", corpo, "Cliente", null, null);
    using var json = JsonDocument.Parse(texto!);
    return json.RootElement.GetProperty("id").GetInt32();
}

// Chaves no mesmo formato da aplicação (bzx_prefixo_segredo, guardada como SHA-256).
static async Task<List<string>> CriarChavesAsync(string ligacao, int quantas, string marca)
{
    var chaves = new List<string>();
    await using var cn = new SqlConnection(ligacao);
    await cn.OpenAsync();
    for (var i = 0; i < quantas; i++)
    {
        var prefixo = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant();
        var chave = $"bzx_{prefixo}_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}";
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO ChavesApi (Nome, Prefixo, HashChave, Escopos, CreatedAt, CreatedBy)
            VALUES (@nome, @prefixo, @hash, 'clientes:escrever clientes:ler documentos:escrever documentos:emitir documentos:ler', SYSUTCDATETIME(), 'teste-carga')
            """;
        cmd.Parameters.AddWithValue("@nome", $"Carga {marca} {i + 1:00}");
        cmd.Parameters.AddWithValue("@prefixo", prefixo);
        cmd.Parameters.AddWithValue("@hash", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(chave))));
        await cmd.ExecuteNonQueryAsync();
        chaves.Add(chave);
    }
    return chaves;
}

static async Task RevogarChavesAsync(string ligacao, string marca)
{
    await using var cn = new SqlConnection(ligacao);
    await cn.OpenAsync();
    await using var cmd = cn.CreateCommand();
    cmd.CommandText = "UPDATE ChavesApi SET RevogadaEm = SYSUTCDATETIME() WHERE Nome LIKE @nome AND RevogadaEm IS NULL";
    cmd.Parameters.AddWithValue("@nome", $"Carga {marca} %");
    await cmd.ExecuteNonQueryAsync();
}

static double Percentil(double[] ordenados, double p) =>
    ordenados.Length == 0 ? 0 : ordenados[Math.Clamp((int)Math.Ceiling(p / 100 * ordenados.Length) - 1, 0, ordenados.Length - 1)];

static Dictionary<string, string> Argumentos(string[] a)
{
    var d = new Dictionary<string, string>();
    for (var i = 0; i + 1 < a.Length; i += 2) d[a[i].TrimStart('-')] = a[i + 1];
    return d;
}
