using System.Text.RegularExpressions;
using Bezix.Facturacao.Application.Abstracoes;
using Bezix.Facturacao.Application.Comum;
using Bezix.Facturacao.Application.Configuracao;
using Bezix.Facturacao.Domain.Clientes;
using Bezix.Facturacao.Domain.Comum;
using Bezix.Facturacao.Domain.Seguranca;
using Microsoft.EntityFrameworkCore;

namespace Bezix.Facturacao.Application.Clientes;

public sealed record DadosCliente(
    string Nome,
    TipoCliente Tipo,
    string? Nuit,
    string? Endereco,
    string? Cidade,
    string? Telefone,
    string? Celular,
    string? Email,
    string? Contacto,
    int? PrazoPagamentoDias,
    decimal? LimiteCredito,
    decimal DescontoPadraoPct,
    string? Notas,
    string? Etiquetas,
    string? EmailFacturacao,
    string? EmailCc,
    bool LembretesActivos,
    string? ResponsavelId);

public enum FiltroEstado { Activos, Inactivos, Todos }

public sealed record FiltroClientes(string? Texto = null, string? Etiqueta = null, FiltroEstado Estado = FiltroEstado.Activos, int Pagina = 1, int Tamanho = Pagina<object>.TamanhoPadrao);

public sealed record ClienteLinha(int Id, string Nome, TipoCliente Tipo, string? Nuit, string? Cidade, string? Telefone, string? Contacto, string? Email, bool Activo, string? Etiquetas);

public sealed record ClienteDuplicado(int Id, string Nome);

/// <summary>Resultado de guardar: além dos erros, pode trazer clientes com o mesmo NUIT (pede confirmação).</summary>
public sealed record ResultadoGuardarCliente(Resultado Resultado, int? Id, IReadOnlyList<ClienteDuplicado> Duplicados)
{
    public bool PrecisaConfirmacao => !Resultado.Sucesso && Duplicados.Count > 0;
}

public sealed partial class ServicoClientes(
    IAppDbContext db,
    IUtilizadorActual utilizador,
    ServicoEmpresa empresas,
    IServicoAuditoria auditoria,
    IPublicadorEventos eventos,
    TimeProvider relogio)
{
    public const string MensagemNuitDuplicado = "Já existe outro cliente com este NUIT.";

    /// <summary>
    /// Clientes que o utilizador actual pode ver. Com "só os próprios" activo e sem Documentos.VerTodos,
    /// limita aos clientes de que é responsável.
    /// </summary>
    public async Task<IQueryable<Cliente>> VisiveisAsync(CancellationToken ct = default)
    {
        var q = db.Clientes.AsQueryable();
        if (await SoProprioAsync(ct))
        {
            var id = utilizador.Id;
            q = q.Where(c => c.ResponsavelId == id);
        }
        return q;
    }

    public async Task<bool> SoProprioAsync(CancellationToken ct = default) =>
        (await empresas.ObterAsync(ct)).RestringirAosProprios && !utilizador.TemPermissao(Permissoes.Documentos.VerTodos);

    public async Task<Pagina<ClienteLinha>> PesquisarAsync(FiltroClientes f, CancellationToken ct = default)
    {
        var (numero, tamanho) = Pagina<ClienteLinha>.Normalizar(f.Pagina, f.Tamanho);
        var q = (await VisiveisAsync(ct)).AsNoTracking();

        q = f.Estado switch
        {
            FiltroEstado.Activos => q.Where(c => c.Activo),
            FiltroEstado.Inactivos => q.Where(c => !c.Activo),
            _ => q,
        };

        if (!string.IsNullOrWhiteSpace(f.Texto))
        {
            var texto = f.Texto.Trim();
            var digitos = TextoPesquisa.Algarismos(texto);
            // Nome com colação sem acentos (ver AppDbContext); NUIT e telefones pelos algarismos.
            q = digitos.Length >= 3
                ? q.Where(c => c.Nome.Contains(texto) || (c.Nuit != null && c.Nuit.Contains(digitos))
                    || (c.TelefonesPesquisa != null && c.TelefonesPesquisa.Contains(digitos)))
                : q.Where(c => c.Nome.Contains(texto) || (c.Contacto != null && c.Contacto.Contains(texto)) || (c.Email != null && c.Email.Contains(texto)));
        }

        if (!string.IsNullOrWhiteSpace(f.Etiqueta))
        {
            var etiqueta = f.Etiqueta.Trim().ToLowerInvariant();
            q = q.Where(c => c.Etiquetas != null && ("," + c.Etiquetas + ",").Contains("," + etiqueta + ","));
        }

        var total = await q.CountAsync(ct);
        var itens = await q.OrderBy(c => c.Nome)
            .Skip((numero - 1) * tamanho).Take(tamanho)
            .Select(c => new ClienteLinha(c.Id, c.Nome, c.Tipo, c.Nuit, c.Cidade, c.Telefone ?? c.Celular, c.Contacto, c.Email, c.Activo, c.Etiquetas))
            .ToListAsync(ct);
        return new Pagina<ClienteLinha>(itens, total, numero, tamanho);
    }

    public async Task<Cliente?> ObterAsync(int id, CancellationToken ct = default) =>
        await (await VisiveisAsync(ct)).AsNoTracking().SingleOrDefaultAsync(c => c.Id == id, ct);

    public async Task<List<string>> EtiquetasAsync(CancellationToken ct = default)
    {
        var todas = await db.Clientes.AsNoTracking().Where(c => c.Etiquetas != null).Select(c => c.Etiquetas).ToListAsync(ct);
        return todas.SelectMany(Cliente.NormalizarEtiquetas).Distinct().Order().ToList();
    }

    public async Task<List<ClienteDuplicado>> ProcurarPorNuitAsync(string? nuit, int? excluirId = null, CancellationToken ct = default)
    {
        var n = TextoPesquisa.Algarismos(nuit);
        if (n.Length == 0) return [];
        return await db.Clientes.AsNoTracking()
            .Where(c => c.Nuit == n && c.Id != excluirId)
            .OrderBy(c => c.Nome)
            .Select(c => new ClienteDuplicado(c.Id, c.Nome))
            .ToListAsync(ct);
    }

    /// <summary>
    /// Cria ou actualiza. Se outro cliente já tiver o mesmo NUIT, não grava e devolve os duplicados,
    /// a não ser que <paramref name="aceitarNuitDuplicado"/> confirme (ex.: filiais da mesma empresa).
    /// </summary>
    public async Task<ResultadoGuardarCliente> GuardarAsync(int? id, DadosCliente dados, bool aceitarNuitDuplicado = false, CancellationToken ct = default)
    {
        var erros = Validar(dados);
        if (erros.Count > 0) return new(Resultado.Falha([.. erros]), id, []);

        var nuit = string.IsNullOrWhiteSpace(dados.Nuit) ? null : TextoPesquisa.Algarismos(dados.Nuit);
        var duplicados = await ProcurarPorNuitAsync(nuit, id, ct);
        if (duplicados.Count > 0 && !aceitarNuitDuplicado)
            return new(Resultado.Falha(MensagemNuitDuplicado), null, duplicados);

        Cliente cliente;
        object? antes = null;
        if (id is null)
        {
            cliente = new Cliente { ResponsavelId = dados.ResponsavelId ?? utilizador.Id };
            db.Clientes.Add(cliente);
        }
        else
        {
            cliente = await (await VisiveisAsync(ct)).SingleOrDefaultAsync(c => c.Id == id, ct)
                ?? throw new KeyNotFoundException("Cliente não encontrado.");
            antes = Instantaneo(cliente);
            if (dados.ResponsavelId is not null) cliente.ResponsavelId = dados.ResponsavelId;
        }

        Aplicar(cliente, dados, nuit);
        await db.SaveChangesAsync(ct);
        auditoria.Registar(nameof(Cliente), cliente.Id.ToString(), id is null ? "Cliente.Criar" : "Cliente.Alterar", antes, Instantaneo(cliente));
        if (id is null) eventos.Publicar(Domain.Integracoes.EventosWebhook.ClienteCriado, DadosEvento(cliente));
        await db.SaveChangesAsync(ct);
        return new(Resultado.Ok(), cliente.Id, duplicados);
    }

    /// <summary>Clientes não se apagam (têm histórico fiscal): desactivam-se.</summary>
    public async Task<Resultado> DefinirActivoAsync(int id, bool activo, CancellationToken ct = default)
    {
        var cliente = await (await VisiveisAsync(ct)).SingleOrDefaultAsync(c => c.Id == id, ct);
        if (cliente is null) return Resultado.Falha("Cliente não encontrado.");
        cliente.Activo = activo;
        auditoria.Registar(nameof(Cliente), id.ToString(), activo ? "Cliente.Activar" : "Cliente.Desactivar");
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    // ---------- Actividades ----------

    public async Task<List<Actividade>> ActividadesAsync(int clienteId, CancellationToken ct = default) =>
        await db.Actividades.AsNoTracking()
            .Where(a => a.ClienteId == clienteId)
            .OrderBy(a => a.Tipo == TipoActividade.Tarefa && a.ConcluidaEm == null ? 0 : 1)
            .ThenByDescending(a => a.Data)
            .Take(200)
            .ToListAsync(ct);

    public async Task<Resultado> AdicionarActividadeAsync(int clienteId, TipoActividade tipo, DateTime dataUtc, string nota, string? responsavelNome, CancellationToken ct = default)
    {
        if (await ObterAsync(clienteId, ct) is null) return Resultado.Falha("Cliente não encontrado.");
        if (string.IsNullOrWhiteSpace(nota)) return Resultado.Falha("Escreva uma nota sobre a actividade.");
        if (nota.Length > 2000) return Resultado.Falha("A nota pode ter no máximo 2000 caracteres.");

        db.Actividades.Add(new Actividade
        {
            ClienteId = clienteId,
            Tipo = tipo,
            Data = DateTime.SpecifyKind(dataUtc, DateTimeKind.Utc),
            Nota = nota.Trim(),
            ResponsavelId = utilizador.Id,
            ResponsavelNome = responsavelNome,
        });
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public async Task<Resultado> ConcluirTarefaAsync(int actividadeId, CancellationToken ct = default)
    {
        var a = await db.Actividades.SingleOrDefaultAsync(x => x.Id == actividadeId, ct);
        if (a is null || await ObterAsync(a.ClienteId, ct) is null) return Resultado.Falha("Tarefa não encontrada.");
        if (a.Tipo != TipoActividade.Tarefa) return Resultado.Falha("Só as tarefas se marcam como concluídas.");
        a.ConcluidaEm = a.ConcluidaEm is null ? relogio.GetUtcNow().UtcDateTime : null;
        await db.SaveChangesAsync(ct);
        return Resultado.Ok();
    }

    public static object DadosEvento(Cliente c) => new
    {
        id = c.Id, nome = c.Nome, tipo = c.Tipo.ToString(), nuit = c.Nuit, email = c.Email, telefone = c.Telefone ?? c.Celular, cidade = c.Cidade,
    };

    // ---------- Regras ----------

    public static List<string> Validar(DadosCliente d)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Nome)) erros.Add("Indique o nome do cliente.");
        else if (d.Nome.Trim().Length > 200) erros.Add("O nome pode ter no máximo 200 caracteres.");
        if (!string.IsNullOrWhiteSpace(d.Nuit) && !ServicoEmpresa.NuitValido(TextoPesquisa.Algarismos(d.Nuit)))
            erros.Add("O NUIT tem de ter 9 algarismos.");
        if (!EmailValido(d.Email)) erros.Add("O email não é válido.");
        if (!EmailValido(d.EmailFacturacao)) erros.Add("O email de facturação não é válido.");
        if (!string.IsNullOrWhiteSpace(d.EmailCc) && d.EmailCc.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries).Any(e => !EmailValido(e.Trim())))
            erros.Add("Um dos emails em cópia não é válido.");
        if (d.PrazoPagamentoDias is < 0 or > 365) erros.Add("O prazo de pagamento tem de estar entre 0 e 365 dias.");
        if (d.LimiteCredito is < 0) erros.Add("O limite de crédito não pode ser negativo.");
        if (d.DescontoPadraoPct is < 0 or > 100) erros.Add("O desconto tem de estar entre 0 e 100%.");
        return erros;
    }

    public static bool EmailValido(string? email) =>
        string.IsNullOrWhiteSpace(email) || Email().IsMatch(email.Trim());

    private static void Aplicar(Cliente c, DadosCliente d, string? nuit)
    {
        c.Nome = d.Nome.Trim();
        c.Tipo = d.Tipo;
        c.Nuit = nuit;
        c.Endereco = Limpar(d.Endereco);
        c.Cidade = Limpar(d.Cidade);
        c.Telefone = Limpar(d.Telefone);
        c.Celular = Limpar(d.Celular);
        c.TelefonesPesquisa = Limpar(TextoPesquisa.Algarismos(d.Telefone) + " " + TextoPesquisa.Algarismos(d.Celular));
        c.Email = Limpar(d.Email);
        c.Contacto = Limpar(d.Contacto);
        c.PrazoPagamentoDias = d.PrazoPagamentoDias;
        c.LimiteCredito = d.LimiteCredito;
        c.DescontoPadraoPct = d.DescontoPadraoPct;
        c.Notas = Limpar(d.Notas);
        var etiquetas = string.Join(",", Cliente.NormalizarEtiquetas(d.Etiquetas));
        c.Etiquetas = etiquetas.Length == 0 ? null : etiquetas;
        c.EmailFacturacao = Limpar(d.EmailFacturacao);
        c.EmailCc = Limpar(d.EmailCc);
        c.LembretesActivos = d.LembretesActivos;
    }

    private static string? Limpar(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static object Instantaneo(Cliente c) => new
    {
        c.Nome, Tipo = c.Tipo.ToString(), c.Nuit, c.Endereco, c.Cidade, c.Telefone, c.Celular, c.Email, c.Contacto,
        c.PrazoPagamentoDias, c.LimiteCredito, c.DescontoPadraoPct, c.Etiquetas, c.EmailFacturacao, c.EmailCc, c.LembretesActivos, c.ResponsavelId,
    };

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex Email();
}
