using Microsoft.AspNetCore.Identity;

namespace Bezix.Facturacao.Web.Seguranca;

/// <summary>Mensagens do ASP.NET Identity em português.</summary>
public sealed class DescricaoErrosIdentidade : IdentityErrorDescriber
{
    public override IdentityError DuplicateEmail(string email) => Erro(nameof(DuplicateEmail), $"Já existe um utilizador com o email {email}.");
    public override IdentityError DuplicateUserName(string userName) => Erro(nameof(DuplicateUserName), $"Já existe um utilizador com o email {userName}.");
    public override IdentityError InvalidEmail(string? email) => Erro(nameof(InvalidEmail), $"O email {email} não é válido.");
    public override IdentityError InvalidUserName(string? userName) => Erro(nameof(InvalidUserName), "O email só pode ter letras, algarismos e os símbolos @ . _ -");
    public override IdentityError PasswordTooShort(int length) => Erro(nameof(PasswordTooShort), $"A password tem de ter pelo menos {length} caracteres.");
    public override IdentityError PasswordRequiresDigit() => Erro(nameof(PasswordRequiresDigit), "A password tem de ter pelo menos um algarismo.");
    public override IdentityError PasswordRequiresLower() => Erro(nameof(PasswordRequiresLower), "A password tem de ter pelo menos uma letra minúscula.");
    public override IdentityError PasswordRequiresUpper() => Erro(nameof(PasswordRequiresUpper), "A password tem de ter pelo menos uma letra maiúscula.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Erro(nameof(PasswordRequiresNonAlphanumeric), "A password tem de ter pelo menos um símbolo.");
    public override IdentityError PasswordMismatch() => Erro(nameof(PasswordMismatch), "A password actual está errada.");
    public override IdentityError InvalidToken() => Erro(nameof(InvalidToken), "O código expirou ou não é válido.");
    public override IdentityError DuplicateRoleName(string role) => Erro(nameof(DuplicateRoleName), $"Já existe o perfil {role}.");
    public override IdentityError UserAlreadyInRole(string role) => Erro(nameof(UserAlreadyInRole), $"O utilizador já tem o perfil {role}.");
    public override IdentityError ConcurrencyFailure() => Erro(nameof(ConcurrencyFailure), "Outro utilizador alterou este registo ao mesmo tempo. Recarregue a página e tente outra vez.");
    public override IdentityError DefaultError() => Erro(nameof(DefaultError), "Ocorreu um erro. Tente outra vez.");

    private static IdentityError Erro(string codigo, string descricao) => new() { Code = codigo, Description = descricao };
}
