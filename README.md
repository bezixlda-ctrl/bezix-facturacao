# Bezix Facturação

Sistema interno de facturação para a Bezix (Moçambique): documentos fiscais conformes com o Código do IVA, clientes, recibos e conta corrente, alojado no Winhost.

- Especificação: [docs/especificacao.md](docs/especificacao.md)
- Decisões que alteram a especificação: [docs/decisoes.md](docs/decisoes.md)
- Regras de cálculo: [docs/regras-calculo.md](docs/regras-calculo.md)
- Instalação no Winhost: [docs/instalacao-winhost.md](docs/instalacao-winhost.md)
- Ofício à AT: [docs/oficio-AT.md](docs/oficio-AT.md)
- Manual do utilizador: [docs/manual-utilizador.md](docs/manual-utilizador.md)
- API v1 e webhooks: [docs/api.md](docs/api.md) · colecção de pedidos: [docs/api.http](docs/api.http)
- Backups e restauro: [docs/backups-restauro.md](docs/backups-restauro.md)
- Testes de carga: [docs/carga.md](docs/carga.md) (ferramenta em `tools/Bezix.Facturacao.Carga`)
- Dossier para a certificação AT: [docs/certificacao.md](docs/certificacao.md)

## Estado

| Fase | Conteúdo | Estado |
|---|---|---|
| 1. Fundação | Solução, Identity, perfis e permissões, configurações, logótipo, contas bancárias, séries, taxas, auditoria, CI, deploy | Feito (falta o deploy de teste no Winhost) |
| 2. Clientes e artigos | Clientes, actividades, artigos, importação CSV, NUIT duplicado, pesquisa global (Ctrl+K) | Feito |
| 3. Documentos | FT, FR, FG, NC, ND, CT, GR em meticais; emissão imutável; hash; aprovações; anulação; PDF; email | Feito |
| 4. Recibos e conta corrente | Recibos com alocação a várias facturas, adiantamentos, estados de pagamento, extracto (PDF e email), antiguidade de saldos (CSV) | Feito |
| 5. Dashboard, comunicação, automação, API | Painel por perfil com gráficos, modelos de email editáveis, link seguro para o cliente com comunicação de pagamento, lembretes, facturas recorrentes, cobranças, tarefas automáticas, API REST v1 ([api.md](docs/api.md)) e webhooks | Feito |
| 6. Fiscal e produção | Relatórios de vendas e IVA, exportação mensal AT (formato provisório) com verificação de integridade, backups externos cifrados com restauro testado, monitorização, mudança de password obrigatória no primeiro acesso, testes de carga, manuais | Feito (aguarda formato oficial e certificação da AT) |

## Tecnologia

ASP.NET Core 10 (Razor Pages + HTMX), EF Core 10, SQL Server, ASP.NET Identity, Serilog, Bootstrap 5. Publicação self-contained win-x64.

```
src/
  Bezix.Facturacao.Domain          Entidades e regras fiscais (sem dependências)
  Bezix.Facturacao.Application     Casos de uso e abstracções
  Bezix.Facturacao.Infrastructure  EF Core, Identity, ficheiros, PDF, email, exportação AT, backups, migrações
  Bezix.Facturacao.Web             Razor Pages, endpoints, segurança
tests/
  Bezix.Facturacao.Tests           Testes unitários e de integração (SQL Server LocalDB)
tools/
  Bezix.Facturacao.Carga           Teste de carga pela API (docs/carga.md)
```

## Desenvolvimento local

Requisitos: .NET SDK 10 e SQL Server LocalDB.

```bash
dotnet run --project src/Bezix.Facturacao.Web --launch-profile http
```

Abre em `http://localhost:5011`. No primeiro arranque cria a base de dados `BezixFacturacao` no LocalDB e um administrador de desenvolvimento, com as credenciais definidas em `src/Bezix.Facturacao.Web/appsettings.Development.json` (só para uso local; este ficheiro não é publicado).

Testes (criam e apagam uma base de dados temporária no LocalDB):

```bash
dotnet test
```

Nova migração depois de alterar o modelo:

```bash
dotnet ef migrations add NomeDaMigracao -p src/Bezix.Facturacao.Infrastructure -s src/Bezix.Facturacao.Web -o Persistencia/Migracoes
```
