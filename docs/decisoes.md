# Decisões técnicas

Registo das decisões que alteram ou concretizam a [especificação](especificacao.md). Cada entrada diz o que foi decidido, porquê, e o que implica.

## D1. .NET 10 LTS em vez de .NET 8 — 09/10/2026

**Decisão:** ASP.NET Core 10 e EF Core 10, publicados em modo **self-contained win-x64**.

**Porquê:** o .NET 8 (e o .NET 9) deixam de receber correcções de segurança a 10/11/2026, um mês depois do início do projecto. Um sistema fiscal com retenção de 10 anos não pode arrancar numa versão sem suporte. O .NET 10 tem suporte até Novembro de 2028.

**Implica:** a publicação leva o runtime dentro (~135 MB), por isso funciona mesmo que o Winhost só tenha o .NET 8/9 instalado. Confirmar no primeiro deploy de teste. Se o Winhost passar a ter o .NET 10, pode mudar-se para framework-dependent (pacote menor) sem alterar código.

## D2. Razor Pages + HTMX (não Blazor Server) — 09/10/2026

**Porquê:** o Blazor Server mantém uma ligação SignalR e estado em memória por utilizador. Com o limite de 500 MB–1 GB do Winhost e a reciclagem do app pool, isso gasta memória e faz cair as sessões. Razor Pages gera HTML no servidor sem estado entre pedidos; o HTMX dá interactividade pontual (paginação, totais ao vivo) sem build Node.

## D3. SkiaSharp para imagens (não ImageSharp)

**Porquê:** o ImageSharp 4.x passou a exigir chave de licença comercial (Six Labors Split License). O SkiaSharp é MIT. Usado só para validar e redimensionar o logótipo.

## D4. Perfis e permissões sobre o ASP.NET Identity

A especificação prevê as tabelas `Permissao`, `Perfil`, `PerfilPermissao` e `LimiteDesconto`. Foram implementadas sobre as tabelas do Identity:

| Especificação | Implementação |
|---|---|
| Perfil | `Perfis` (AspNetRoles renomeada) + colunas `Descricao`, `Sistema`, `DescontoMaximoPct` |
| PerfilPermissao | `PerfilPermissoes` (AspNetRoleClaims), claim `permissao` = código |
| Permissao | Catálogo em código: `Domain/Seguranca/Permissoes.cs` (código, grupo, descrição) |
| LimiteDesconto | Coluna `Perfis.DescontoMaximoPct` (nulo = sem limite) |

**Porquê:** as permissões vão no cookie de sessão como claims e cada código é uma policy (`[Authorize(Policy = Permissoes.Documentos.Anular)]`), sem consultas extra à BD por pedido. O catálogo em código garante que uma permissão usada no código existe sempre; os perfis continuam editáveis pelo Administrador.

**Regras de segurança implementadas:** o perfil Administrador tem sempre todas as permissões; o último administrador activo não pode ser desactivado nem mudar de perfil; ao mudar permissões ou desactivar um utilizador, as sessões abertas actualizam-se em menos de 1 minuto (security stamp).

## D5. Tabela de logs criada pela migração

O Serilog escreve avisos e erros na tabela `Registos`, criada pela migração do EF (e não pelo Serilog no arranque), porque no primeiro arranque a base de dados ainda não existe quando o Serilog é configurado.

## D6. Token do `jobs/tick` aceite na query string

A especificação pede `POST /internal/jobs/tick`. O Scheduled Task Tool do Winhost só faz pedidos GET a um URL e não envia cabeçalhos. O endpoint aceita GET e POST, com o token no cabeçalho `X-Jobs-Token` ou em `?token=`. Sem token configurado, o endpoint responde 404. O token deve ser longo (≥ 32 caracteres) e mudado se aparecer em logs.

## D7. Campos de valor em texto com formato moçambicano

Os campos de valores e percentagens usam `<input type="text" inputmode="decimal">` e um model binder próprio que lê "30.804,10". Com `type="number"`, o browser envia "30804.10" e, com a cultura moçambicana (ponto = separador de milhares), o valor seria lido como 3.080.410.

## D8. Desenvolvimento local em HTTP

Em `Development` os cookies aceitam HTTP, para não ser preciso instalar um certificado de confiança no Windows. Nos outros ambientes os cookies são só HTTPS, há redireccionamento para HTTPS e HSTS de 1 ano.

## D9. Artigo guarda o código da taxa de IVA, não o Id — fase 2

A especificação prevê `Artigo.TaxaIvaId`. Como as taxas são versionadas por vigência (uma mudança legal fecha a taxa e cria outra com o mesmo código), o artigo guarda o **código** (`NOR`, `ISE`). Na emissão (fase 3) usa-se a taxa com esse código em vigor na data do documento. Assim, quando a taxa normal mudar, os artigos não precisam de ser editados.

## D10. NUIT do cliente não é único — fase 2

"Detecção de NUIT duplicado" foi implementada como aviso com confirmação, não como bloqueio: filiais da mesma empresa podem partilhar o NUIT. Ao gravar um cliente com NUIT já existente, o sistema mostra os clientes com esse NUIT e só grava se o utilizador confirmar. Na importação CSV, NUIT já existente → ignorar ou actualizar (escolha do utilizador); NUIT repetido dentro do ficheiro → erro na linha.

O NUIT do cliente é opcional no registo. A obrigatoriedade (sempre para empresas; para particulares acima do limite configurado) é verificada na emissão do documento (fase 3). A lista de clientes assinala empresas sem NUIT.

## D11. Pesquisa sem acentos

As colunas `Clientes.Nome`, `Clientes.Contacto` e `Artigos.Descricao` usam a colação `Latin1_General_100_CI_AI`: "conceicao" encontra "Conceição". NUIT e telefones pesquisam-se pelos algarismos (`TelefonesPesquisa` guarda só os algarismos do telefone e do celular).

## D12. "Só os próprios"

Opção em Configurações → Empresa. Quando activa, quem não tem a permissão `Documentos.VerTodos` (por omissão, o Facturador) só vê os clientes de que é responsável; na fase 3 aplica-se também aos documentos. O responsável é quem cria o cliente; quem tem `Documentos.VerTodos` pode mudá-lo.

## Pendentes de confirmação

- **QuestPDF (fase 3):** licença Community gratuita só para empresas com receita anual abaixo de 1 M USD. Confirmar a receita da Bezix; acima disso é preciso licença paga.
- **Motivo de isenção por omissão:** o texto "Isento nos termos do artigo 9 do Código do IVA" é provisório. Confirmar com o contabilista.
- **Limite do NUIT obrigatório**, regras da factura global e requisitos de certificação: aguardam resposta da AT ([oficio-AT.md](oficio-AT.md)).
- **Limpeza de importações abandonadas (fase 5):** ficheiros CSV analisados mas não importados ficam em `App_Data/uploads/importacoes`. O `jobs/tick` deve apagar os que tenham mais de 24 horas.
- **2FA obrigatório para administradores:** por agora é recomendado (lista de primeiros passos no painel). Tornar obrigatório na fase 6, depois de todos os administradores o terem activado.
