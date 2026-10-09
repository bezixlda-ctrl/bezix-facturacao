# Bezix Facturação

Sistema interno de facturação para a Bezix (Moçambique): documentos fiscais conformes com o Código do IVA, clientes, recibos e conta corrente, alojado no Winhost.

- Especificação: [docs/especificacao.md](docs/especificacao.md)
- Decisões que alteram a especificação: [docs/decisoes.md](docs/decisoes.md)
- Regras de cálculo: [docs/regras-calculo.md](docs/regras-calculo.md)
- Instalação no Winhost: [docs/instalacao-winhost.md](docs/instalacao-winhost.md)
- Ofício à AT: [docs/oficio-AT.md](docs/oficio-AT.md)

## Estado

| Fase | Conteúdo | Estado |
|---|---|---|
| 1. Fundação | Solução, Identity, perfis e permissões, configurações, logótipo, contas bancárias, séries, taxas, auditoria, CI, deploy | Feito (falta o deploy de teste no Winhost) |
| 2. Clientes e artigos | CRM base, importação CSV | Por fazer |
| 3. Documentos | Emissão, numeração, hash, PDF, email | Por fazer (cálculo fiscal já feito e testado) |
| 4. Recibos e conta corrente | | Por fazer |
| 5. Dashboard, comunicação, automação, API | | Por fazer |
| 6. Fiscal e produção | Exportação AT, backups, certificação | Por fazer |

## Tecnologia

ASP.NET Core 10 (Razor Pages + HTMX), EF Core 10, SQL Server, ASP.NET Identity, Serilog, Bootstrap 5. Publicação self-contained win-x64.

```
src/
  Bezix.Facturacao.Domain          Entidades e regras fiscais (sem dependências)
  Bezix.Facturacao.Application     Casos de uso e abstracções
  Bezix.Facturacao.Infrastructure  EF Core, Identity, ficheiros, imagens, migrações
  Bezix.Facturacao.Web             Razor Pages, endpoints, segurança
tests/
  Bezix.Facturacao.Tests           Testes unitários e de integração (SQL Server LocalDB)
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
