# Testes de carga

Meta da especificação (secção 12): ecrãs < 1 s, PDF < 2 s, emissão < 500 ms com 50 utilizadores simultâneos; painel < 2 s.

## Ferramenta

`tools/Bezix.Facturacao.Carga` simula N utilizadores a criar e emitir facturas pela API, **todos na mesma série** (o pior caso: a numeração sequencial obriga a bloquear a série) e a descarregar o PDF de uma em cada cinco. Cria uma chave de API por utilizador (o limite é 60 pedidos/minuto por chave) e revoga-as no fim.

**Só contra uma base de dados de teste:** cria clientes, facturas e chaves.

```bash
dotnet run --project tools/Bezix.Facturacao.Carga -c Release -- --url https://teste.dominio --ligacao "<connection string da base de teste>" --utilizadores 50 --segundos 90 --pausa-ms 10000
```

| Opção | Por omissão | |
|---|---|---|
| `--utilizadores` | 50 | Utilizadores em simultâneo |
| `--segundos` | 60 | Duração |
| `--pausa-ms` | 1000 | Tempo médio entre emissões de cada utilizador |
| `--modo rajada` | — | Todos emitem no mesmo instante (pior caso); sem esta opção, cada um começa num momento aleatório e as pausas variam ±50% |

A empresa da base de teste tem de ter NUIT preenchido. A primeira execução depois de arrancar inclui o aquecimento (JIT, caches); repetir para ter valores estáveis.

## Resultados (09/10/2026, desenvolvimento)

Portátil de 8 núcleos lógicos, SQL Server LocalDB, aplicação em Release; 8.764 documentos na base, todos do mesmo cliente e série.

| Perfil | Emissões | p50 | p95 | p99 | Meta |
|---|---|---|---|---|---|
| 50 utilizadores, uma emissão a cada ~10 s, desfasadas | 447 | 50 ms | 91 ms | 108 ms | OK |
| 50 utilizadores, todos no mesmo instante a cada 10 s | 450 | 74 ms | 395 ms | 515 ms | OK (p95) |
| 50 utilizadores, uma emissão por segundo cada (≈ 45/s) | 2.498 | 38 ms | 63 ms | 83 ms | OK* |
| PDF (descarregar) | 100–500 | 6–13 ms | 9–56 ms | — | OK |

\* Neste perfil, 378 pedidos receberam 429 (limite de 60 pedidos/minuto por chave): é o comportamento esperado.

| Página (sessão de administrador, mesma base) | 1.º pedido após arranque | Seguintes (mediana) |
|---|---|---|
| Painel | 1.433 ms | 109 ms |
| Documentos | 190 ms | 83 ms |
| Conta corrente | 120 ms | 77 ms |
| Relatórios de vendas | 171 ms | 83 ms |

Antes da emissão em duas etapas (decisão D24), a rajada de 50 tinha p95 de 1,9 s e p99 de 7,5 s.

## Depois do deploy no Winhost

O Winhost é partilhado: CPU e SQL Server são divididos com outros sites. Repetir o perfil "desfasado" contra o ambiente de teste no Winhost (base de dados de teste, nunca a de produção) e registar aqui. Se a emissão passar de 500 ms no p95, as opções são, por ordem: plano Winhost com mais recursos, mover o SQL Server para um serviço dedicado, ou VPS.
