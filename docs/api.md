# API v1

API REST para ligar outros sistemas (loja online, ERP, contabilidade) à facturação. A documentação interactiva está em `https://bezixcenter.net/facturacao/api/docs` e o contrato OpenAPI em `/api/openapi/v1.json`. As regras de negócio são as mesmas da interface: numeração, IVA, imutabilidade, aprovações e auditoria (decisão D21).

Todos os valores são em **meticais (MZN)**. Datas no formato `AAAA-MM-DD`.

## Autenticação

Criar uma chave em **Administração → Integrações → Nova chave**, escolhendo só o que o sistema precisa. A chave é mostrada **uma única vez**.

```
Authorization: Bearer bzx_1a2b3c4d_...
```

| Escopo | Permite |
|---|---|
| `clientes:ler` | Ler clientes e extractos de conta corrente |
| `clientes:escrever` | Criar e alterar clientes |
| `artigos:ler` / `artigos:escrever` | Ler / criar artigos |
| `documentos:ler` | Ler documentos e o PDF |
| `documentos:escrever` | Criar rascunhos |
| `documentos:emitir` | Emitir (`"emitir": true`) |
| `documentos:enviar` | Enviar documentos por email |
| `documentos:anular` | Anular documentos |
| `recibos:escrever` | Registar recibos |

## Respostas e erros

| Código | Quando |
|---|---|
| 200 / 201 | Sucesso (`201` com cabeçalho `Location` ao criar) |
| 400 | Pedido mal formado, ou falta o `Idempotency-Key` |
| 401 | Sem chave, chave inválida ou revogada |
| 403 | A chave não tem o escopo necessário |
| 404 | Não existe |
| 409 | Um pedido com o mesmo `Idempotency-Key` ainda está a ser processado |
| 422 | Dados inválidos (lista em `erros`), ou `Idempotency-Key` repetido com outro corpo |
| 429 | Mais de 60 pedidos por minuto com a mesma chave (ver `Retry-After`) |

Os erros vêm em `application/problem+json`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.21",
  "title": "Pedido recusado",
  "status": 422,
  "detail": "O documento tem de ter pelo menos uma linha.",
  "erros": ["O documento tem de ter pelo menos uma linha."]
}
```

## Idempotência

`POST /documentos` e `POST /recibos` exigem `Idempotency-Key`, um valor único por operação (ex.: um GUID ou o n.º da encomenda). Se a rede falhar e o pedido for repetido com a mesma chave, a resposta é a mesma e **não é criada outra factura**. A chave é guardada 7 dias.

## Exemplos

### Criar e emitir uma factura

```http
POST /api/v1/documentos
Authorization: Bearer bzx_...
Idempotency-Key: encomenda-10452
Content-Type: application/json

{
  "tipo": "FT",
  "clienteId": 12,
  "vencimento": "2026-11-08",
  "descontoGlobalPct": 0,
  "emitir": true,
  "linhas": [
    { "artigoCodigo": "MANUT-01", "quantidade": 2, "descontoPct": 0 },
    { "descricao": "Deslocação", "quantidade": 1, "precoUnitario": 1500, "descontoPct": 0, "codigoTaxaIva": "NOR" }
  ]
}
```

- Com `artigoCodigo`, a descrição, a unidade, o preço e a taxa de IVA vêm do artigo (podem ser substituídos no pedido).
- Sem `serieId`, usa a série activa do tipo. Sem `"emitir": true`, fica em rascunho.
- Um desconto acima do limite deixa a factura "a aguardar aprovação" (não é emitida).

Resposta `201`:

```json
{
  "id": 341, "tipo": "FT", "numero": "FT A2026/0342", "estado": "Emitido",
  "data": "2026-10-09", "vencimento": "2026-11-08", "clienteId": 12, "clienteNome": "Cliente, Lda",
  "moeda": "MZN", "baseTributavel": 4400.00, "totalIva": 704.00, "total": 5104.00,
  "emAberto": 5104.00, "estadoPagamento": "Emitido", "hash": "…", "linhas": [ … ]
}
```

### Outros pedidos

```http
GET  /api/v1/clientes?pesquisa=matola&pagina=1&tamanho=25
GET  /api/v1/clientes/12
POST /api/v1/clientes                      { "nome": "Cliente, Lda", "tipo": "Empresa", "nuit": "400123456", "email": "contas@cliente.co.mz" }
PUT  /api/v1/clientes/12
GET  /api/v1/clientes/12/extracto?de=2026-01-01&ate=2026-10-31
GET  /api/v1/artigos?pesquisa=manut
POST /api/v1/artigos                       { "codigo": "MANUT-01", "descricao": "Manutenção", "preco": 1450, "codigoTaxaIva": "NOR" }
GET  /api/v1/documentos/341
GET  /api/v1/documentos/341/pdf
POST /api/v1/documentos/341/email          { "para": "contas@cliente.co.mz" }   (corpo opcional: usa o modelo de email)
POST /api/v1/documentos/341/anular         { "motivo": "Duplicada" }
POST /api/v1/recibos                       (com Idempotency-Key)
     { "clienteId": 12, "valor": 5104, "metodo": "Transferencia", "referencia": "TRF-889",
       "alocacoes": [ { "documentoId": 341, "valor": 5104 } ] }
```

Métodos de pagamento: `Numerario`, `Transferencia`, `MPesa`, `EMola`, `Cheque`, `Pos`.

## Webhooks

Em **Administração → Integrações → Novo webhook**: um endereço `https` e os eventos a receber. O segredo de assinatura é mostrado **uma única vez**.

| Evento | Quando |
|---|---|
| `documento.emitido` | Um documento foi emitido (facturas, notas, cotações, guias) |
| `documento.anulado` | Um documento foi anulado |
| `documento.enviado` | Um documento foi enviado ao cliente por email |
| `pagamento.recebido` | Foi registado um recibo |
| `documento.vencido` | Uma factura passou o vencimento sem estar paga (uma vez por factura) |
| `cliente.criado` | Foi criado um cliente (na interface, pela API ou por importação) |

Cada evento chega por `POST` em JSON:

```http
POST https://erp.exemplo.co.mz/webhooks/facturacao
Content-Type: application/json
X-Evento: documento.emitido
X-Entrega-Id: 6f1c…            (igual em todas as tentativas do mesmo evento: use-o para ignorar repetições)
X-Timestamp: 1791561843
X-Signature: sha256=5d41…

{ "id": "6f1c…", "evento": "documento.emitido", "ocorridoEm": "2026-10-09T14:03:22Z",
  "dados": { "id": 341, "tipo": "FT", "numero": "FT A2026/0342", "total": 5104.00, … } }
```

**Verificar a assinatura** (exemplo em C#): calcular HMAC-SHA256 de `"{X-Timestamp}.{corpo}"` com o segredo e comparar com `X-Signature`. Recusar pedidos com timestamp com mais de 5 minutos.

```csharp
var esperado = "sha256=" + Convert.ToHexString(
    HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes($"{timestamp}.{corpo}"))).ToLowerInvariant();
var valido = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(esperado), Encoding.UTF8.GetBytes(assinatura));
```

Responder com `2xx` em menos de 10 segundos. Outra resposta (ou nenhuma) conta como falha: o evento é repetido após 1, 5 e 30 minutos, 2 e 12 horas. Depois de **5 eventos seguidos falhados**, o webhook é desactivado e aparece um alerta no painel; reactiva-se na página do webhook. As entregas falhadas podem ser reenviadas à mão em **Integrações → Entregas**.
