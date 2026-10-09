namespace Bezix.Facturacao.Domain.Comum;

/// <summary>Campos de rastreio exigidos em todas as tabelas (preenchidos pelo interceptor do EF).</summary>
public interface IRastreavel
{
    DateTime CreatedAt { get; set; }
    string? CreatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
}

public abstract class Entidade : IRastreavel
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
