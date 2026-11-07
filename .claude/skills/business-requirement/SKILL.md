---
name: business-requirement
description: Escrita de requisitos de negócio do ntix em backlog/ (REQn), com Objetivo de Negócio, Problema de Negócio, Riscos e Implementação de Tecnologia. Use sempre que o usuário pedir para escrever, redigir, detalhar, refinar ou registrar um requisito, história, demanda ou item de backlog, mesmo que não use a palavra "requisito". A implementação do requisito escrito segue depois pela skill full-cycle.
---

# Escrita de requisito de negócio

O usuário define o negócio; o requisito é o documento que traduz essa definição em algo que pode ser implementado e validado sem voltar a perguntar. Ele é lido por quem decide (negócio) e por quem implementa (a skill `full-cycle`), então as duas leituras precisam funcionar.

## 1. Antes de escrever

1. Leia `.claude/skills/full-cycle/references/standards.md` (padrões e decisões em aberto) e os requisitos já existentes em `backlog/`. Um requisito novo não contradiz um padrão registrado sem dizer isso explicitamente.
2. Leia o código das áreas afetadas (controllers, casos de uso, value objects, `web/`). Cite nomes reais: `SlugValueObject`, `ProducersV1Controller`, `WhitelabelConfig`.
3. Se uma regra de negócio for ambígua e mudar o desenho, pergunte antes. Se for detalhe, decida, escreva a decisão no requisito e liste-a em **Decisões em aberto** para o usuário confirmar.

## 2. Arquivo

- Pasta `backlog/` na raiz do repositório.
- Nome: `REQ<n>-<titulo-em-kebab-case>.md`, com o próximo número livre (`ls backlog`). O título no arquivo é `# REQ<n> - <Título como o usuário escreveu>`.
- Logo abaixo do título: `Status` (Proposto, Aprovado, Em implementação, Entregue), `Área` (produtor, cliente, plataforma...), `Depende de` (outros REQs ou "nenhum").

## 3. Seções obrigatórias, nesta ordem

### Objetivo de Negócio
- O que o requisito entrega, para quem (produtor, comprador, operação Ntix) e com que dados.
- O que ele impacta: receita, operação, experiência, implantação, parceiros.
- Por que é positivo para o negócio: o ganho concreto, não "melhorar a plataforma".

### Problema de Negócio
- Como é hoje, sem o requisito, e qual problema isso causa.
- Que problema exatamente ele resolve. Se não houver problema real, o requisito não deveria existir: diga isso ao usuário.

### Riscos
Três grupos, cada risco com **impacto** e **mitigação**:
- **Da ausência**: o que acontece com o negócio se o requisito não for feito.
- **De negócio no requisito**: o que pode dar errado para o negócio ao implementá-lo (exposição de dado, marca errada para um cliente, custo operacional).
- **De tecnologia no requisito**: segurança, disponibilidade, desempenho, consistência, compatibilidade.

### Implementação de Tecnologia
Tudo que a skill `full-cycle` precisa para implementar sem perguntar:
- **Contrato**: rota, verbo, versão, autenticação, headers (auditáveis), corpo de entrada e de saída com exemplo, status HTTP e códigos de notificação (`*_NOTIFICATION_CODE`).
- **Regras de negócio e validações**, numeradas (RN1, RN2...), uma por linha.
- **Camadas**: o que cabe a Domain, Infra, Application e Presentation, citando os padrões de `standards.md` e `code-patterns.md`.
- **Configuração e implantação**: chaves de `appsettings`, variáveis, volumes, impacto no `devops/` e no `web/`.
- **Observabilidade**: logs semânticos (sem dado pessoal), métricas e traces relevantes.
- **Testes**: cenários de unitário, integrado, funcional (Gherkin em português) e carga (k6, com o perfil).
- **Dependências**: nenhuma nova, ou qual e a RFC necessária.
- **Fora do escopo**: o que o requisito explicitamente não faz.

### Critérios de Aceite
Lista verificável em Gherkin (`Dado / Quando / Então`), cobrindo o caminho feliz e cada RN.

### Decisões em aberto
Perguntas para o usuário, cada uma com a decisão provisória adotada no texto.

## 4. Estilo

- pt-BR, frases curtas, voz ativa. Termos do código em `código`.
- Sem promessas vagas ("seguro", "rápido"): diga como e quanto.
- O requisito descreve o quê e o porquê; a implementação descreve o como no nível de contrato e camada, não o código linha a linha.

## 5. Entrega

Diga ao usuário o caminho do arquivo, um resumo de duas linhas e as decisões em aberto. Não implemente: a implementação começa quando o usuário pedir, pela skill `full-cycle`.
