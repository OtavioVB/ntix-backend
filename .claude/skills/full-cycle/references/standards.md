# Padrões do ntix-backend

Fonte da verdade dos padrões. Registre aqui apenas o que o usuário estipulou ou o que o repositório já mostra, nunca uma preferência sua. Ao receber uma instrução nova, atualize este arquivo no mesmo turno.

## Estipulado pelo usuário

### Papéis
- O usuário define regras de negócio e definições de tecnologia. Claude implementa de ponta a ponta.

### Arquitetura
Clean Architecture, seguida sempre à risca, com esta direção de referências:

```
Presentation (WebApi, Workers, Console etc.) -> Application -> Infra
                                                            -> Domain
```

| Camada | Conteúdo |
|---|---|
| Presentation | Apresentação: `WebApi`, `Workers`, `Console` etc. |
| Application | Casos de uso: as regras de negócio de orquestração e a chamada de serviços de domínio, de infraestrutura e externos |
| Infra | Configurações de dependências externas, repositórios etc. |
| Domain | Informações de domínio, objetos de valor, entidades e as regras de cada conceito do domínio |

Regra que pertence a um conceito do domínio, e que outras regras de negócio podem reutilizar, fica no domínio, não no caso de uso. Exemplo: gerar a versão do slug é do `SlugValueObject`; o caso de uso só verifica na base se ela está livre e tenta gravar.

Toda nova aplicação de apresentação referencia apenas `Application`. `Domain` e `Infra` chegam a ela por transitividade.

`Infra` não referencia `Domain`: o repositório troca dados por um modelo de persistência próprio, com tipos primitivos, e a `Application` converte entre a entidade e esse modelo.

### Persistência
- PostgreSQL ([RFC 0006](../../../../docs/rfcs/0006-postgresql-no-lugar-do-sql-server.md)), acessado com SQL nativo e `Npgsql`, sem EF Core, porque performance é critério de implementação ([RFC 0004](../../../../docs/rfcs/0004-sql-nativo-no-lugar-do-ef-core.md)). O SQL Server foi descartado porque a VPS é ARM64.
- Tabelas e índices ficam em scripts na pasta `data/`.
- Unit of Work: o caso de uso faz rollback em qualquer exceção e só faz commit quando a regra de negócio permite a criação.
- Evite consultas com `LIKE`. Prefira consultas exatas sobre índices.
- Mesmo quando um requisito pedir stored procedure dona da transação, mantém-se o SQL nativo com Unit of Work no caso de uso (decidido no REQ1, criação do produtor).
- Evento de integração é gravado na outbox (`outbox.outbox_event`) na mesma transação da operação que o origina. O consumidor só é implementado quando o requisito do sistema de destino existir.

### Exceções
- Toda exceção tratada no caso de uso é devolvida ao controller, exceto o cancelamento da requisição, que é tratado e devolvido com 4XX.

### Observabilidade
- A definição da senha do cliente gera spans, e o dashboard aponta quando as iterações do hash extrapolam.
- Logs, traces e métricas via OpenTelemetry, visualizados no Grafana ([RFC 0005](../../../../docs/rfcs/0005-observabilidade-com-opentelemetry.md)).
- Caso de uso de negócio tem funil em métricas do Prometheus e dashboard próprio: um contador de solicitações e um contador de desfechos.
- Nenhum sinal pode expor dado pessoal completo, nem em mensagem de exceção copiada para um span.

### Integração contínua
- GitHub Actions: build, testes unitários e integrados em paralelo, análise estática e, depois, testes funcionais.
- Jobs de build, testes e análise estática no environment `Staging` do GitHub; o deploy no environment `Production`.
- Valores sensíveis da esteira vêm de secrets do repositório, nunca do arquivo do workflow.
- Deploy em VPS via SSH, depois dos testes: acesso por secrets, variáveis de ambiente geradas no servidor a cada deploy e API em contêiner construído pelo `Dockerfile`.
- O deploy verifica as dependências do servidor e instala o que faltar antes de usá-las.
- O deploy detecta a arquitetura e os recursos do servidor e ajusta imagens e limites a eles. Antes de adotar uma imagem, confira se ela existe para a arquitetura do servidor: a VPS de produção é ARM64.
- Na VPS só a API é pública. Banco e observabilidade escutam em `127.0.0.1`.
- Análise estática em workflow próprio (`static-analysis.yml`), chamado pelo `github-workflows.yml` a cada commit na `main`, com Sonar e cobertura pelo Coverlet ([RFC 0006](../../../../docs/rfcs/0006-analise-estatica-com-sonar-e-coverlet.md)). Exclusões de cobertura, de issues e de duplicação ficam configuradas na pipeline, e a API Key do Sonar vem do GitHub.

### Nomes
- Use o nome mais simples que descreva o negócio: `Customer`, não `CustomerAccount`.

### Regras de negócio
- Não invente regra que não foi estipulada, mesmo que pareça uma proteção técnica, como um limite de tentativas.
- Slug único: o primeiro usa só o nome; se já existir, gera-se uma versão aleatória de 5 letras ou números, verifica-se na base e usa-se a primeira livre. Vale também para o produtor, mesmo que o requisito fale em sufixo sequencial (`-2`, `-3`).
- Slug do produtor é rótulo de subdomínio: no máximo 63 caracteres, contando o sufixo, e nunca uma palavra reservada (que recebe a versão aleatória).
- Produtor: o e-mail do administrador é o mesmo da produtora; o front não pede um segundo e-mail.
- Verificação de e-mail e carteira do produtor ficam para requisitos próprios: no cadastro, só o evento `ProducerRegistered` vai para a outbox.
- Senha do cliente: obrigatória, com ao menos 8 caracteres, uma letra maiúscula, uma minúscula, um número e um símbolo. É persistida como hash que combina a chave privada do servidor com o hash da senha, e cada cliente tem o próprio salt.
- Celular do cliente: opcional, com DDD e número.

### Padrões de código
- Seguir os padrões dos projetos de referência `Tech.Aquisition.Customers` e `ntix`, resumidos em [code-patterns.md](code-patterns.md).
- **Antes de adicionar uma estrutura, avalie se ela é realmente necessária.** Um padrão existir na referência não é motivo para trazê-lo: só entra o que o requisito pede ou o que o usuário estipulou. Na dúvida, pergunte. Foi assim que uma camada de feature flag entrou e teve de sair.

### Auditabilidade
- Toda requisição traz as informações auditáveis (`AuditableInfoValueObject`), e o caso de uso as recebe como parâmetro secundário, além do input: `ExecuteAsync(input, auditableInfo, cancellationToken)`.
- Campos: `CorrelationId`, `OriginService`, `ExecutionUser` e `RequestedAt`.

### Logs
- Todo caso de uso registra logs semânticos, com informações relevantes para o negócio (o que foi criado, o que foi recusado e por quê).
- Dado pessoal em log aparece só parcialmente, para atender à LGPD. A ofuscação é um método do value object do dado (`EmailValueObject.GetObfuscatedValue()`, `CpfValueObject.GetObfuscatedValue()`), para o padrão ficar fixo no domínio.

### Escopo de uma entrega
- Endpoint, presentation, casos de uso, infraestrutura e domínio, conforme os padrões estipulados.
- Casos de uso são orquestradores de serviços de domínio, de infraestrutura e externos.
- Testes unitários, integrados, funcionais e de carga.
- Os testes são escritos, não executados. Quem os valida é o usuário.

### Testes
- Unitários: xUnit, padrão AAA, nomes `Given_[Cenário]_When_[Comportamento]_Should_[ResultadoEsperado]`.
- Integrados: xUnit, usando as dependências criadas, padrão AAA, mesma nomenclatura.
- Funcionais: Reqnroll e Gherkin em português (`# language: pt`), com definições semânticas ao negócio.
- Carga: k6. O cadastro de conta, o cadastro de produtor e a consulta do whitelabel (REQ3) usam o perfil moderado: até 50 usuários virtuais, p95 abaixo de 500 ms e menos de 1% de erros.

### Whitelabel (front)
- Cada implantação é whitelabel: a identidade visual vem de uma configuração em runtime, nunca de build. Hoje `web/public/whitelabel.json` (substituído por implantação, sem rebuild); no futuro, a API, apontada por `VITE_WHITELABEL_URL`, no mesmo formato.
- No back-end (REQ3), a configuração é o arquivo `{slug}.json` da produtora (ex.: `ntix.json`) em uma pasta do servidor. Nunca é devolvido como está: a cada consulta é validado contra a estrutura e a resposta sai do modelo tipado.
- Decisões do REQ3, aprovadas pelo usuário: arquivo fora da estrutura devolve 500 sem detalhe (nunca a parte válida); a consulta não verifica a produtora na base; `Cache-Control: public, max-age=300`; rota `/api/v1/producers/{slug}/whitelabel`; o slug de produtora é nome comercial e pode aparecer no log (a regra de não logar slug vale para cliente).
- O arquivo de cada produtora parte de um default de cor em `devops/whitelabel-defaults/` (amarelo, laranja = Ntix, azul, verde, roxo, branco, rosa). Os defaults não são servidos; só `devops/whitelabel/` é montado na API. Default novo entra no teste `Given_DefaultColorFile_When_UsedAsProducerFile_Should_BeServed`.
- O contrato é o tipo `WhitelabelConfig` (`web/src/whitelabel/config.ts`): `brand` (nome, logo, favicon, cor da barra), `colors` (`primary`, `secondary`, `neutral`, `success`, `danger`, `action`), `typography`, `spacing`, `radius`, `motion`. Tudo opcional; o que faltar fica no padrão Ntix de `tokens.css`.
- A configuração sobrescreve só os primitivos (`--n-primary-*`, `--n-secondary-*`, `--n-neutral-*`, `--n-space-*`...). Cenas e componentes referenciam primitivos e semânticos por `var()`, nunca cor literal nem nome de marca (`gold`, `graphite`).
- Campo novo no contrato: tipo em `config.ts`, tradução em `toCssVariables`, valor padrão em `tokens.css` e em `whitelabel.json`, e teste em `whitelabel.test.ts`.
- A configuração é entrada não confiável: valores passam pelo filtro de `toCssVariables` e URLs por `safeAssetUrl` (só https ou caminho do próprio front). Falha ao carregar mantém o padrão; a plataforma sempre abre.

### Dependências
- Dependência nova é responsabilidade de Claude, com explicação prévia e RFC que a justifique.
- As RFCs ficam em `docs/rfcs/`.

## Observado no repositório

- .NET 10 (`net10.0`), `Nullable` e `ImplicitUsings` habilitados, solução em `ntix.slnx`.
- Projetos: `src/WebApi`, `src/Application`, `src/Domain`, `src/Infra`.
- Testes em `tsts/` (é esse o nome da pasta): `UnitTests`, `IntegratedTests`, `FunctionalTests`.
- `WebApi` usa controllers (`AddControllers`/`MapControllers`) e `AddOpenApi`.
- Versões atuais: xUnit 2.9.3, Reqnroll.xUnit 3.3.4, Microsoft.NET.Test.Sdk 17.14.1, coverlet.collector 6.0.4, coverlet.msbuild 6.0.4.
- `devops/docker-compose.yml` sobe as dependências locais; os segredos ficam em `devops/.env`, ignorado pelo Git, com modelo em `devops/.env.example`.
- Bibliotecas de teste NSubstitute e `Mvc.Testing`, asserções com o `Assert` do xUnit ([RFCs](../../../../docs/rfcs)).
- O Dockerfile de uma aplicação copia o `.csproj` de cada projeto de que ela depende antes do `dotnet restore`.

## Interpretações a confirmar

Assumidas por Claude. Valem até o usuário dizer o contrário; ao confirmar, mova para "Estipulado".

- "As dependências criadas" dos testes integrados são as dependências reais do requisito (banco etc.), não dublês.
- Não executar os testes não impede compilar: `dotnet build` é permitido.
- Testes de carga ficam em `tsts/LoadTests/`, que ainda não existe. Ela não entra na solução porque não é um projeto .NET.
- Referências dos testes: `UnitTests` e `IntegratedTests` referenciam `Application`; `FunctionalTests` referencia `WebApi`. As demais camadas chegam por transitividade.
- Headers auditáveis: `X-Correlation-Id`, `X-Origin-Service` e `X-Requested-At` são obrigatórios; `X-Execution-User` é opcional, como o usuário na referência, porque o cadastro é anônimo.
- `ExecutionUser` é um identificador e aparece sem ofuscação nos logs, então quem chama não deve enviar nele um dado pessoal, como um email.
- Slug de cliente não aparece nos logs, porque revela o nome completo do cliente. Slug de produtora pode aparecer (REQ3).
- O Sonar da esteira é o SonarQube Cloud, e a API Key é o secret `SONAR_TOKEN`, exposto ao job como variável de ambiente.
- A esteira gera a cobertura com o `coverlet.collector`, e o `coverlet.msbuild` fica disponível para `dotnet test /p:CollectCoverage=true`.
- Algoritmo da senha: PBKDF2-SHA256 com 600 mil iterações e salt aleatório de 16 bytes, seguido de HMAC-SHA256 com a chave privada (`PasswordConfiguration__PrivateKey`). Só a biblioteca padrão do .NET, sem RFC.
- Símbolo da senha é qualquer caractere que não seja letra, número ou espaço. A senha não tem tamanho máximo, porque nenhum foi estipulado.
- Celular tem exatamente 11 dígitos (2 de DDD e 9 de número), aceita `(`, `)`, `-` e espaços na entrada e é persistido só com dígitos. Não há validação da lista de DDDs nem do nono dígito, e o celular não aparece nos logs.
- O 4XX do cancelamento é o 499, e a exceção que chega ao controller não é capturada e vira 500.
- O número de iterações é fixo, então "extrapolar" é medido pelo tempo do hash: p95 amarelo a partir de 250 ms e vermelho a partir de 500 ms. As iterações ficam no hash, porque gerar o salt não itera.
- Palavras reservadas de slug de produtor (lista definitiva em aberto no requisito): `www`, `api`, `admin`, `app`, `hub`, `produtores`.
- Telefone do produtor é obrigatório e persistido em E.164: com 10 ou 11 dígitos nacionais recebe `+55`; começando com `+`, aceita de 8 a 15 dígitos.
- O front chama a API por CORS, com as origens permitidas em `CorsConfiguration:AllowedOrigins`.
- O controller lê os headers com `[FromHeader]` e o caso de uso valida o `AuditableInfoValueObject`, sem middleware.

## Decisões em aberto

Pergunte ao usuário quando um requisito depender de uma delas. Não presuma a resposta.

1. **Carga em novos fluxos**: o perfil moderado foi definido só para o cadastro de conta. Pergunte o perfil de cada novo fluxo.
