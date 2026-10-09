# Regras de cálculo dos documentos

Implementadas em `src/Bezix.Facturacao.Domain/Fiscal/CalculadoraDocumento.cs` e testadas em `tests/.../Domain/CalculadoraDocumentoTests.cs`. O ecrã pode calcular ao vivo, mas o valor que fica no documento é sempre o recalculado no servidor por esta classe.

## Passos

1. **Linha:** total líquido = Quantidade × Preço unitário × (1 − Desconto da linha %), arredondado a 2 casas.
2. **Subtotal** = soma dos totais líquidos das linhas.
3. **Desconto global** = Subtotal × Desconto global %, arredondado a 2 casas. É sempre aplicado **antes do IVA**.
4. **Base tributável** = Subtotal − Desconto global.
5. **Repartição por taxa:** o desconto global é repartido pelas taxas de IVA na proporção da base de cada taxa. O cêntimo que sobra do arredondamento fica na taxa com maior base, para que a soma das bases por taxa seja exactamente a base tributável.
6. **IVA por taxa** (modo por omissão, "Por taxa") = Base da taxa × Taxa %, arredondado a 2 casas.
   No modo "Por linha", calcula-se o IVA de cada linha (já com o desconto global), arredonda-se, e soma-se por taxa. O modo escolhe-se em Configurações → Empresa e documentos.
7. **Total** = Base tributável + soma do IVA.

**Arredondamento:** comercial, meio para cima (0,005 → 0,01), `MidpointRounding.AwayFromZero`.

**Validações:** quantidade > 0; preço ≥ 0; descontos e taxas entre 0 e 100%.

## Caso de teste da factura modelo n.º 08/26

| | Valor |
|---|---|
| Subtotal | 30.804,10 |
| Desconto global 20% | 6.160,82 |
| Base tributável | 24.643,28 |
| IVA 16% | 3.942,92 |
| **Total** | **28.586,20** |

A factura modelo impressa mostra IVA 4.928,66, que corresponde a 20% e não a 16%. O sistema calcula sempre pela taxa configurada; há um teste que garante que o valor 4.928,66 nunca é produzido.

## Porque é que os modos podem dar resultados diferentes

Três linhas de 0,03 MT a 16%:

- Por taxa: 0,09 × 16% = 0,0144 → **0,01**
- Por linha: 3 × (0,03 × 16% = 0,0048 → 0,00) = **0,00**

A diferença é sempre de cêntimos. O modo "Por taxa" é o recomendado porque o IVA declarado por taxa coincide com o resumo impresso no documento.
