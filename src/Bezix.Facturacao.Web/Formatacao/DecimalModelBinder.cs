using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bezix.Facturacao.Web.Formatacao;

/// <summary>
/// Lê valores decimais no formato moçambicano ("30.804,10") em todos os formulários.
/// Os campos de valor usam input de texto (inputmode="decimal"), nunca type="number",
/// que enviaria "30804.10" e seria lido de forma diferente consoante o browser.
/// </summary>
public sealed class DecimalModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext contexto)
    {
        var valor = contexto.ValueProvider.GetValue(contexto.ModelName);
        if (valor == ValueProviderResult.None) return Task.CompletedTask;
        contexto.ModelState.SetModelValue(contexto.ModelName, valor);

        var texto = valor.FirstValue;
        var anulavel = Nullable.GetUnderlyingType(contexto.ModelType) is not null;
        if (string.IsNullOrWhiteSpace(texto))
        {
            if (anulavel) contexto.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (Formato.TentarLerDecimal(texto, out var numero))
            contexto.Result = ModelBindingResult.Success(numero);
        else
            contexto.ModelState.TryAddModelError(contexto.ModelName, $"\"{texto}\" não é um valor válido. Use o formato 1.234,56.");
        return Task.CompletedTask;
    }
}

public sealed class DecimalModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext contexto) =>
        contexto.Metadata.ModelType == typeof(decimal) || contexto.Metadata.ModelType == typeof(decimal?)
            ? new DecimalModelBinder()
            : null;
}
