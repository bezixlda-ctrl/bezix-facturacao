using Microsoft.AspNetCore.Identity;

namespace Bezix.Facturacao.Infrastructure.Identidade;

public class Utilizador : IdentityUser
{
    public string Nome { get; set; } = "";
    public bool Activo { get; set; } = true;

    /// <summary>Assinatura de email pessoal (acrescentada à assinatura da empresa).</summary>
    public string? AssinaturaEmail { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UltimoLogin { get; set; }
}

/// <summary>Perfil = conjunto de permissões (guardadas como claims do perfil).</summary>
public class Perfil : IdentityRole
{
    public Perfil() { }
    public Perfil(string nome) : base(nome) { }

    public string? Descricao { get; set; }

    /// <summary>Perfil criado pelo sistema: pode ser ajustado mas não apagado.</summary>
    public bool Sistema { get; set; }

    /// <summary>Desconto máximo (%) sem aprovação. Nulo = sem limite.</summary>
    public decimal? DescontoMaximoPct { get; set; }
}
