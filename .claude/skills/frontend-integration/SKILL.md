---
name: frontend-integration
description: Integração do front (web/, React + Vite) com a API do ntix-backend a partir do OpenAPI da WebApi. Use sempre que um endpoint novo ou alterado precisar ser consumido por uma tela, quando o usuário pedir para "ligar", "integrar" ou "conectar" o front ao backend, ou ao fim da skill full-cycle quando o requisito tiver tela em web/.
---

# Integração front ↔ backend

O front (`web/`) nasceu como demonstração, com dados locais. A integração liga cada fluxo à API sem quebrar a demonstração: **por padrão o front continua em demonstração**, e só faz requisições reais com a flag de query param.

## 1. Entender o contrato pelo OpenAPI

A WebApi publica o OpenAPI em `/openapi/v1.json` (`AddOpenApi` + `MapOpenApi`). É ele a fonte do contrato: rota, payload, headers e respostas.

- Com a API no ar (`dotnet run --project src/WebApi`, perfil `https`), rode `bash .claude/skills/frontend-integration/scripts/fetch-openapi.sh [base-url]`: salva o spec em `docs/openapi/v1.json` e lista as operações.
- Sem a API no ar, leia o controller (`src/WebApi/Controllers/*V1Controller.cs`) e o payload (`Controllers/Payloads/*V1Request.cs`), que geram o spec.
- O corpo das respostas 200 e 400 é sempre `Notification[]` (`code`, `message`, `type`); 499 é cancelamento; 500 não tem corpo útil. Os códigos ficam nas constantes `*_NOTIFICATION_CODE` dos value objects e do caso de uso.

## 2. Onde o código fica

```
web/src/api/
  mode.ts       flag demo/real (?api=real | ?api=demo, lembrada no sessionStorage da aba)
  client.ts     base URL, headers auditáveis, postJson e tradução de erro de rede/500
  <recurso>.ts  tipos do request (espelho do *V1Request do OpenAPI) e a função da operação
  api.test.ts   testes do cliente, da flag e das operações
```

- Um módulo por recurso da API (`producers.ts` para `/api/v1/producers`). O tipo do request repete o nome do payload do OpenAPI (`CreateProducerRequest` ↔ `CreateProducerV1Request`), com as propriedades em camelCase como o JSON.
- A tela não chama `fetch`: chama a função do fluxo (ex.: `submitProducerSignup` em `pages/producers/signup.ts`), que decide entre demonstração e API real com `isRealApi()`.
- Não adicione biblioteca (gerador de cliente, axios, react-query) sem explicação prévia e RFC em `docs/rfcs/`, como qualquer dependência nova.

## 3. Regras da integração

- **Flag**: `?api=real` liga, `?api=demo` desliga. O padrão é demonstração, inclusive em produção, até o usuário decidir o contrário. Nunca troque o padrão por conta própria.
- **Demonstração intacta**: o ramo de demonstração mantém o comportamento atual (atraso simulado, sessão local, avisos de "Demonstração").
- **Headers auditáveis** em toda chamada, pelo `auditableHeaders()`: `X-Correlation-Id` (UUID novo por requisição), `X-Origin-Service: ntix-web`, `X-Requested-At` (`toISOString()`, UTC com milissegundos). `X-Execution-User` só com usuário autenticado; cadastro público não envia.
- **Base URL**: `VITE_API_BASE_URL` quando definida; senão `https://localhost:5000` em localhost e `https://api.ntix.com.br` nos domínios. A origem do front precisa estar em `CorsConfiguration:AllowedOrigins` do `src/WebApi/appsettings.json`; ao criar uma origem nova, inclua-a lá.
- **Erros**: mapeie cada `code` da API para o campo do formulário por prefixo (`EMAIL_` → e-mail, `PASSWORD_` → senha...), exibindo a `message` da API. O que não tiver campo vira mensagem geral (`role="alert"`). Falha de rede e 500 viram a notificação `NETWORK_ERROR`.
- **Validação local espelha a da API**: se o value object exige uma regra (ex.: política de senha), o front valida igual antes de enviar, para a pessoa não descobrir o erro só no servidor.
- **Estado do negócio manda na tela**: se a API cria algo que ainda não dá acesso (ex.: usuário `PENDING` até verificar o e-mail), o modo real não abre sessão local nem leva ao painel.
- **Segredos**: senha vai só no corpo da requisição; nunca em sessão, storage, log ou URL.

## 4. Testes

Vitest + Testing Library, já no projeto. Escreva, não execute (mesma regra do backend). O `tsc --noEmit` pode ser rodado para garantir que compila.

- `api.test.ts`: flag (padrão demo, liga, desliga, valor desconhecido), base URL, formato dos headers, erro de rede e 500, contrato do request (corpo igual ao `*V1Request`) e mapeamento de códigos para campos. Use `vi.stubGlobal("fetch", ...)`.
- Teste da página: o fluxo em demonstração continua igual; com `sessionStorage["ntix:api-mode"] = "real"` e `fetch` simulado, sucesso e recusa da API aparecem na tela.

## 5. Entrega

Diga ao usuário: o endpoint integrado, como testar (`https://localhost:5173/...?api=real` com a API em `https://localhost:5000`), o que muda entre demonstração e modo real, e o aviso de que os testes foram escritos e não executados.

Se a integração ensinou um padrão novo (outro verbo HTTP, autenticação, paginação), registre-o nesta skill no mesmo turno.
