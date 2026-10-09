using Bezix.Facturacao.Domain.Comum;

namespace Bezix.Facturacao.Domain.Automacao;

/// <summary>Calendário de lembretes de cobrança (registo único).</summary>
public class PlanoLembretes : Entidade
{
    public const int IdUnico = 1;

    public bool Activo { get; set; } = true;

    /// <summary>Lembrete amigável N dias antes do vencimento (0 = não enviar).</summary>
    public int DiasAntes { get; set; } = 3;

    /// <summary>Dias depois do vencimento para o 1.º, 2.º e 3.º aviso, separados por vírgulas (ex.: "1,15,30").</summary>
    public string DiasDepois { get; set; } = "1,15,30";

    public int MaxAvisos { get; set; } = 3;

    /// <summary>Hora de Maputo a partir da qual os lembretes do dia podem sair.</summary>
    public int HoraEnvio { get; set; } = 8;

    public bool SoDiasUteis { get; set; } = true;

    public IReadOnlyList<int> ListaDiasDepois() =>
        DiasDepois.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, out var n) ? n : -1).Where(n => n > 0).Distinct().Order().Take(Math.Max(0, MaxAvisos)).ToList();
}

/// <summary>Lembrete já enviado para um documento (evita repetir o mesmo aviso).</summary>
public class LembreteEnviado
{
    public long Id { get; set; }
    public int DocumentoId { get; set; }
    public TipoLembrete Tipo { get; set; }
    public DateTime EnviadoEm { get; set; }
    public int? EmailId { get; set; }
    public string? EnviadoPor { get; set; }
}

public enum TipoLembrete { Antes = 0, Aviso1 = 1, Aviso2 = 2, Aviso3 = 3, Manual = 9 }

/// <summary>Factura emitida automaticamente a partir de um documento modelo.</summary>
public class Recorrencia : Entidade
{
    public string Descricao { get; set; } = "";
    public int ModeloDocumentoId { get; set; }
    public int ClienteId { get; set; }
    public Periodicidade Periodicidade { get; set; } = Periodicidade.Mensal;
    public DateOnly ProximaData { get; set; }
    public DateOnly? DataFim { get; set; }

    /// <summary>true: emite e numera; false: deixa um rascunho para alguém rever e emitir.</summary>
    public bool EmitirAutomaticamente { get; set; }
    public bool EnviarEmail { get; set; } = true;
    public bool Activa { get; set; } = true;

    public DateTime? UltimaExecucao { get; set; }
    public int? UltimoDocumentoId { get; set; }
    public string? UltimoErro { get; set; }

    public DateOnly Seguinte(DateOnly data) => Periodicidade switch
    {
        Periodicidade.Semanal => data.AddDays(7),
        Periodicidade.Mensal => data.AddMonths(1),
        Periodicidade.Trimestral => data.AddMonths(3),
        _ => data.AddYears(1),
    };
}

public enum Periodicidade { Semanal = 0, Mensal = 1, Trimestral = 2, Anual = 3 }

/// <summary>Histórico das execuções do jobs/tick (substitui o histórico do Hangfire; ver decisão D18).</summary>
public class ExecucaoTarefa
{
    public long Id { get; set; }
    public string Tarefa { get; set; } = "";
    public DateTime Inicio { get; set; }
    public DateTime Fim { get; set; }
    public bool Sucesso { get; set; }
    public string? Resultado { get; set; }
    public string? Erro { get; set; }
}

/// <summary>Marca de "já feito" para acontecimentos que só devem ocorrer uma vez (ex.: evento documento.vencido).</summary>
public class MarcaUnica
{
    public long Id { get; set; }
    public string Chave { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
