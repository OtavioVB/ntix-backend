# REQ3 - Exposição de Ponto de Extremidade para Consulta das Configurações do Whitelabel

- **Status**: Proposto
- **Área**: produtor / plataforma
- **Depende de**: REQ1 (cadastro do produtor, que gera o slug); whitelabel do front (`web/src/whitelabel/`)

## Objetivo de Negócio

Cada produtora de eventos opera na Ntix com a sua própria identidade visual: cores primária e secundária, tons neutros, tipografia, espaçamentos, raios, movimento, nome, logo e favicon. O back-end passa a expor essa configuração por um endpoint, consultado pelo front no carregamento de cada implantação.

**O que entrega**: o endpoint `GET /api/v1/producers/{slug}/whitelabel` devolve a configuração whitelabel da produtora, lida de um arquivo `{slug}.json` (ex.: `ntix.json`) implantado em uma pasta do servidor, sempre validada contra a estrutura oficial antes de sair.

**O que impacta**:
- **Venda**: a produtora vende ingressos com a cara da marca dela, o que aumenta confiança do comprador e conversão.
- **Implantação**: uma nova plataforma whitelabel passa a ser só um arquivo novo na pasta, sem build nem deploy do front.
- **Operação**: a Ntix controla, no servidor, o que cada produtora pode alterar; o front deixa de depender de um arquivo estático por implantação.
- **Produto**: a mesma build do front atende todas as produtoras, com experiência diferente para cada uma.

**Por que é positivo**: transforma o whitelabel em um serviço da plataforma (vendável, centralizado e auditável), reduz o custo de entrada de cada nova produtora e prepara a futura edição da marca pelo próprio painel do produtor.

## Problema de Negócio

Hoje o front lê `web/public/whitelabel.json`, um arquivo estático junto da build. Na prática:

- Cada produtora precisaria de uma implantação do front com o seu arquivo: o custo e o tempo de entrada crescem a cada cliente.
- Não existe ponto único de verdade sobre a marca de cada produtora; trocar uma cor exige mexer na implantação do front.
- Nada garante que o arquivo segue a estrutura: um arquivo malformado ou adulterado chega direto ao navegador do comprador.

O requisito resolve isso: o back-end vira a fonte oficial da configuração, por produtora (slug), com validação a cada consulta.

## Riscos

### Da ausência
| Risco | Impacto | Mitigação |
|---|---|---|
| Whitelabel restrito a uma implantação por produtora | Custo de implantação e de manutenção cresce linearmente com os clientes; onboarding lento | Este requisito |
| Marca sem fonte oficial | Produtora vê a plataforma com cores erradas ou de outra produtora; perda de confiança | Configuração no servidor, por slug |
| Arquivo estático sem validação | Configuração quebrada derruba o visual para todos os compradores daquela implantação | Validação a cada consulta (RN4) |

### De negócio no requisito
| Risco | Impacto | Mitigação |
|---|---|---|
| Devolver o arquivo como está | Qualquer conteúdo colocado na pasta (dado interno, comentário, chave, campo não previsto) vaza para a internet | Desserializar no modelo tipado e devolver só os campos do contrato (RN5) |
| Configuração de uma produtora servida para outra | Marca errada para o comprador; quebra de contrato com a produtora | Arquivo resolvido só pelo slug validado; teste integrado por slug |
| Enumeração de produtoras pelo endpoint público | Concorrência descobre a carteira de clientes | Mesma resposta para slug inexistente e sem arquivo (RN3); rate limiting |
| Marca com contraste ilegível | Comprador não consegue ler ou comprar | Validação de formato de cor; contraste é responsabilidade do front (`readableInk`) e fica fora deste requisito |

### De tecnologia no requisito
| Risco | Impacto | Mitigação |
|---|---|---|
| *Path traversal* pelo slug (`../appsettings`) | Leitura de qualquer arquivo do servidor | Slug validado pelas regras do `SlugValueObject` antes de compor o caminho; caminho final conferido dentro da pasta configurada (RN2) |
| Injeção de CSS ou de URL maliciosa nos valores | Página do comprador quebrada, rastreamento ou *phishing* | Validação por campo: cor em hex, medidas e fontes por padrão restrito, URLs só `https` ou caminho relativo (RN4) |
| Arquivo muito grande ou JSON profundo | Consumo de memória e CPU; negação de serviço | Limite de tamanho do arquivo e de profundidade do JSON (RN4) |
| Leitura de disco a cada requisição | Latência e I/O sob carga, no carregamento de toda página | Rate limiting e `Cache-Control` curto; cache em memória fica fora do escopo (ver Decisões em aberto) |
| Pasta ausente ou sem permissão no servidor | Endpoint indisponível; front cai no padrão Ntix | Front já tem fallback; erro logado e exposto como 500 sem detalhe |
| Contrato do back divergir do `WhitelabelConfig` do front | Campo novo ignorado ou rejeitado de um lado só | Contrato único descrito aqui; mudança de campo atualiza os dois lados no mesmo requisito |

## Implementação de Tecnologia

### Contrato

`GET /api/v1/producers/{slug}/whitelabel`

- **Autenticação**: nenhuma. A configuração é pública: o front a carrega antes de qualquer login.
- **Headers**: os auditáveis do projeto (`X-Correlation-Id`, `X-Origin-Service`, `X-Requested-At`), obrigatórios como nos demais endpoints.
- **Rate limiting**: a mesma política aplicada aos endpoints públicos existentes.

**200 OK**: a configuração validada, no formato do `WhitelabelConfig` do front (`web/src/whitelabel/config.ts`). Todos os campos existentes no arquivo, desde que previstos no contrato, são devolvidos:

```json
{
  "version": 1,
  "brand": { "name": "Ntix", "logoUrl": "", "faviconUrl": "/favicon.svg", "themeColor": { "light": "#ffffff", "dark": "#1b1d20" } },
  "colors": {
    "primary": { "50": "#fbf6e3", "500": "#c99b14", "900": "#40310d" },
    "secondary": { "800": "#2a2c31" },
    "neutral": { "0": "#ffffff", "950": "#1b1d20" },
    "success": { "300": "#7fd1a0", "700": "#1c6b42" },
    "danger": { "300": "#f4a39a", "700": "#a8322a" },
    "action": { "base": "#c99b14", "hover": "#d9af28" }
  },
  "typography": {
    "fontFamily": { "sans": "\"Archivo Variable\", system-ui, sans-serif", "mono": "\"IBM Plex Mono\", monospace" },
    "stylesheets": [],
    "text": { "xs": "0.75rem", "md": "1rem" },
    "display": { "lg": "clamp(3.25rem, 0.8rem + 6.1vw, 7.25rem)" }
  },
  "spacing": { "scale": { "1": "0.25rem", "4": "1rem" }, "gutter": "clamp(1rem, 0.2rem + 3.4vw, 3rem)", "pageMax": "90rem" },
  "radius": { "sm": "4px", "md": "8px", "lg": "14px", "notch": "11px" },
  "motion": { "duration": { "fast": "140ms" }, "easing": { "out": "cubic-bezier(0.16, 1, 0.3, 1)" } }
}
```

O exemplo completo é o próprio `web/public/whitelabel.json`, que serve de base para o `ntix.json` do servidor.

**Demais respostas** (corpo `Notification[]`, como no restante da API):

| Status | Quando | Código |
|---|---|---|
| 400 | Slug com formato inválido | `WHITELABEL_SLUG_INVALID` |
| 404 | Nenhum arquivo `{slug}.json` na pasta | `WHITELABEL_NOT_FOUND` |
| 500 | Arquivo existe, mas não segue a estrutura (JSON inválido, campo fora do formato, tamanho excedido) ou pasta inacessível | sem detalhe no corpo; motivo só no log |
| 499 | Requisição cancelada pelo cliente | — |

Arquivo inválido é falha de implantação da Ntix, não erro de quem consulta: por isso 500, sem revelar o motivo, e o front cai no padrão Ntix.

**Headers de resposta**: `Content-Type: application/json` e `Cache-Control: public, max-age=300`.

### Regras de negócio

- **RN1**: o arquivo da produtora fica na pasta configurada em `WhitelabelConfiguration:DirectoryPath` e se chama `{slug}.json`, em minúsculas (ex.: `ntix.json`).
- **RN2**: o slug é validado pelas regras do `SlugValueObject` (minúsculas, números e hífen, até `SUBDOMAIN_MAX_LENGTH` = 63) antes de compor o caminho. O caminho completo resolvido precisa estar dentro da pasta configurada; caso contrário, 400.
- **RN3**: slug válido sem arquivo devolve 404 `WHITELABEL_NOT_FOUND`, independentemente de a produtora existir na base. O endpoint não consulta o banco.
- **RN4**: a cada consulta, o arquivo é lido e validado contra a estrutura; nunca é devolvido como está. A validação:
  - limite de 64 KB no arquivo e de profundidade 8 no JSON;
  - `version` igual a `1`, quando informado;
  - só as seções e chaves do contrato; chave desconhecida é descartada, não devolvida;
  - degraus de escala de cor: `0`, `50` e `100` a `950`; valores de cor em hex `#rrggbb` (também em `brand.themeColor` e `colors.action`);
  - medidas (`text`, `display`, `spacing`, `radius`): número com unidade (`px`, `rem`, `em`, `vw`, `vh`, `%`) ou `clamp()`/`calc()` compostos só por isso;
  - durações em `ms` ou `s`; curvas como `cubic-bezier(...)` com quatro números ou palavra-chave CSS (`ease`, `ease-in`, `ease-out`, `ease-in-out`, `linear`);
  - famílias de fonte: nomes entre aspas ou genéricos, separados por vírgula, sem `;`, `{`, `}`, `<`, `>`, `\` nem `url(`;
  - URLs (`logoUrl`, `faviconUrl`, `stylesheets`): `https://` absoluta ou caminho relativo começando com `/` (não `//`); string vazia é tratada como ausente;
  - textos (`brand.name`): até 60 caracteres, sem caracteres de controle;
  - qualquer valor de campo previsto fora do formato torna o arquivo inválido (500): uma marca pela metade não é servida.
- **RN5**: a resposta é serializada a partir do modelo tipado validado, nunca do texto do arquivo. Campos ausentes no arquivo não aparecem na resposta (o front usa o padrão Ntix).
- **RN6**: a mesma regra de validação do front (`toCssVariables` e `safeAssetUrl`) continua valendo lá; o back é mais estrito, e o front segue como segunda barreira.

### Camadas

- **Domain**: `WhitelabelConfiguration` como conjunto de value objects (`ColorValueObject`, `CssLengthValueObject`, `FontFamilyValueObject`, `AssetUrlValueObject`, escalas como coleções por degrau). Cada value object valida o próprio formato e devolve notificação, conforme `code-patterns.md`. Reuso do `SlugValueObject` para validar o slug.
- **Infra**: leitor de arquivo da pasta configurada (`WhitelabelConfiguration:DirectoryPath` em `appsettings`, com options validadas na inicialização), com o limite de tamanho, leitura assíncrona com `CancellationToken` e desserialização por `System.Text.Json` com `MaxDepth`. Devolve o documento bruto ou ausência; não decide regra.
- **Application**: `GetProducerWhitelabelUseCase`, recebendo o slug e o `AuditableInfoValueObject`: valida o slug, pede o arquivo à Infra, monta e valida o domínio e devolve o modelo ou a notificação. Logs semânticos (`GetProducerWhitelabelLog`).
- **Presentation**: ação `GET {slug}/whitelabel` no `ProducersV1Controller` (ou controller próprio sob a mesma rota, se o padrão do repositório indicar), mapeando o resultado para 200/400/404/500 e o `Cache-Control`.

### Configuração e implantação

- `appsettings.json`: `"WhitelabelConfiguration": { "DirectoryPath": "/app/whitelabel" }`.
- `devops/`: volume montado em `/app/whitelabel` no contêiner da API, com o `ntix.json` como arquivo inicial (cópia do `web/public/whitelabel.json`). Somente leitura para o processo.
- `web/`: com o endpoint no ar, a origem do front passa a ser `VITE_WHITELABEL_URL=https://api.ntix.com.br/api/v1/producers/{slug}/whitelabel`, com o slug resolvido pelo subdomínio. A resolução do slug no front fica no escopo da integração (skill `frontend-integration`), seguindo a flag `?api=real`.

### Observabilidade

- Logs: consulta servida, arquivo não encontrado, arquivo inválido (com o campo e a regra que falharam), pasta inacessível. O slug de produtora é nome comercial, não dado pessoal: pode aparecer no log desta consulta (confirmar em Decisões em aberto).
- Métricas e traces pelo OpenTelemetry já configurado, sem métrica nova.

### Testes

- **Unitários**: cada value object (formatos válidos e inválidos, incluindo `url(`, `javascript:`, `//host`, cor fora do hex, degrau desconhecido); caso de uso para slug inválido, ausente, arquivo inválido e válido; descarte de chave desconhecida.
- **Integrados**: leitor de arquivo com pasta temporária (arquivo válido, ausente, acima de 64 KB, profundidade excedida, slug com `..` não sai da pasta).
- **Funcionais** (Reqnroll, Gherkin em português): os cenários dos Critérios de Aceite.
- **Carga** (k6): perfil moderado (até 50 VUs, p95 < 500 ms, < 1% de erros), consultando `ntix`.

### Dependências

Nenhuma nova. `System.Text.Json` e options já fazem parte do .NET.

### Fora do escopo

- Edição da configuração pelo painel do produtor e armazenamento em banco.
- Cache em memória ou distribuído da configuração.
- Upload de logo e fontes (só URLs).
- Validação de contraste entre cores.

## Critérios de Aceite

```gherkin
# language: pt
Funcionalidade: Consulta da configuração whitelabel da produtora

  Cenário: Configuração válida
    Dado que existe o arquivo "ntix.json" válido na pasta de whitelabel
    Quando eu consulto o whitelabel da produtora "ntix"
    Então recebo status 200
    E recebo todos os campos do contrato presentes no arquivo

  Cenário: Campo fora do contrato não é exposto
    Dado que o arquivo "ntix.json" contém o campo "internalNotes"
    Quando eu consulto o whitelabel da produtora "ntix"
    Então recebo status 200
    E a resposta não contém "internalNotes"

  Cenário: Arquivo inexistente
    Quando eu consulto o whitelabel da produtora "sem-arquivo"
    Então recebo status 404 com a notificação "WHITELABEL_NOT_FOUND"

  Cenário: Slug que tenta sair da pasta
    Quando eu consulto o whitelabel da produtora "..%2Fappsettings"
    Então recebo status 400 com a notificação "WHITELABEL_SLUG_INVALID"

  Cenário: Arquivo com valor inseguro
    Dado que o arquivo "ntix.json" tem "logoUrl" igual a "javascript:alert(1)"
    Quando eu consulto o whitelabel da produtora "ntix"
    Então recebo status 500 sem detalhe no corpo

  Cenário: Arquivo acima do limite
    Dado que o arquivo "ntix.json" tem mais de 64 KB
    Quando eu consulto o whitelabel da produtora "ntix"
    Então recebo status 500 sem detalhe no corpo
```

## Decisões em aberto

1. **Arquivo inválido**: 500 sem detalhe (adotado) ou devolver só a parte válida? A parte válida evita tela sem marca, mas pode servir uma marca incoerente.
2. **Slug sem produtora na base**: o endpoint não consulta o banco (adotado). Se for preciso servir só produtoras ativas/aprovadas, entra uma consulta ao repositório de produtores.
3. **Cache**: `Cache-Control: max-age=300` (adotado). Uma troca de marca leva até 5 minutos para aparecer. Cache em memória no servidor fica para quando a carga pedir.
4. **Slug no log**: tratado como nome comercial e logado nesta consulta (adotado). O `standards.md` proíbe slug de cliente no log; confirmar que a regra não vale para produtora.
5. **Rota**: `/api/v1/producers/{slug}/whitelabel` (adotado) ou `/api/v1/whitelabel/{slug}`, se o whitelabel for servir também outros tipos de tenant.
