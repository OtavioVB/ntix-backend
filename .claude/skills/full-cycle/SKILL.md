---
name: full-cycle
description: Desenvolvimento end-to-end de um requisito de negócio no ntix-backend (.NET 10): endpoint, presentation, casos de uso, infraestrutura, domínio, RFC de dependência nova e testes unitários, integrados, funcionais (Reqnroll) e de carga (k6). Use sempre que o usuário pedir uma funcionalidade, regra de negócio, endpoint, caso de uso ou alteração de comportamento neste repositório, mesmo que ele não fale em "end-to-end" ou mencione só uma das camadas ou só parte dos testes.
---

# Full cycle: do requisito de negócio à entrega

O usuário define as regras de negócio e as definições de tecnologia. Você implementa, de ponta a ponta, seguindo os padrões que ele estipula. Uma entrega só está completa quando todas as etapas abaixo foram cobertas.

## 1. Antes de escrever código

1. Leia [references/standards.md](references/standards.md), a fonte da verdade dos padrões e das decisões ainda em aberto, e [references/code-patterns.md](references/code-patterns.md), a forma do código esperada em cada camada.
2. Releia o requisito e o código existente nas áreas afetadas. O que já existe no código é precedente a seguir.
3. Veja se o requisito depende de alguma decisão em aberto ou de alguma regra de negócio ambígua. Se depender, pergunte tudo de uma vez, antes de implementar, e registre as respostas em `standards.md`.

Siga os padrões à risca. Não introduza por conta própria arquitetura, biblioteca, abstração ou convenção que o usuário não tenha estipulado: quem decide isso é ele. Antes de adicionar qualquer estrutura, avalie se o requisito realmente precisa dela, mesmo que ela exista no projeto de referência. Detalhes pequenos, sem impacto nos padrões, você decide e menciona na entrega.

## 2. Dependência nova

Você tem autonomia para adicionar uma dependência (pacote NuGet, imagem de contêiner, serviço externo, extensão do k6), desde que nesta ordem:

1. Explique ao usuário, em poucas linhas, qual é a necessidade e por que essa dependência.
2. Escreva a RFC em `docs/rfcs/NNNN-titulo-em-kebab-case.md`, a partir de [docs/rfcs/0000-template.md](../../../docs/rfcs/0000-template.md), com o próximo número livre.
3. Só então adicione a dependência, com versão exata, como nos `.csproj` atuais.

Se ela contrariar uma definição de tecnologia já registrada em `standards.md`, pare e peça a decisão do usuário antes de adicionar. Referência entre projetos da própria solução (`ProjectReference`) não exige RFC, mas segue a direção definida em `standards.md`.

## 3. Implementação

Comece pelo domínio, de que as demais camadas dependem. Cubra todas as camadas que o requisito exigir, respeitando a direção Presentation -> Application -> Infra e Domain:

| Camada | Projeto | Responsabilidade |
|---|---|---|
| Domain | `src/Domain` | Informações de domínio, objetos de valor, entidades e as regras de cada conceito do domínio, reutilizáveis por outras regras de negócio |
| Infra | `src/Infra` | Configurações de dependências externas, repositórios, integrações |
| Application | `src/Application` | Casos de uso: regras de negócio de orquestração, chamando serviços de domínio, de infraestrutura e externos |
| Presentation | `src/WebApi` (endpoints), workers, console | Apresentação: traduz a entrada para o caso de uso e o resultado para a saída |

Todo caso de uso recebe o `AuditableInfoValueObject` como parâmetro secundário, além do input, e registra logs semânticos com dados pessoais ofuscados.

Se o requisito criar uma dependência de execução (banco, fila etc.), inclua-a em `devops/docker-compose.yml`. Se criar uma nova aplicação de apresentação, ela referencia apenas `Application` e entra na solução; o Dockerfile dela copia os `.csproj` de que depende.

Compile com `dotnet build ntix.slnx`, incluindo os projetos de teste, para não entregar código que não compila. Trate os avisos `NU190x` de vulnerabilidade, inclusive de pacotes transitivos: fixe uma versão corrigida e registre na RFC da dependência que os trouxe.

## 4. Testes

Escreva os quatro níveis, seguindo [references/testing.md](references/testing.md):

- **Unitários**: `tsts/UnitTests`
- **Integrados**: `tsts/IntegratedTests`
- **Funcionais**: `tsts/FunctionalTests` (Reqnroll + Gherkin)
- **Carga**: `tsts/LoadTests` (k6)

**Não execute os testes** (`dotnet test`, `k6 run`): apenas escreva. A validação é do usuário, e a entrega deve dizer claramente que eles não foram executados.

## 5. Entrega

Reporte de forma curta:

- o que foi implementado em cada camada e os cenários cobertos em cada nível de teste;
- as RFCs criadas e as decisões menores que você tomou;
- o resultado do build e o aviso de que os testes não foram executados.

Só faça commit, push ou PR se o usuário pedir.

## 6. Evolução da skill

Antes de encerrar, pergunte-se o que este requisito ensinou e acelere o próximo:

- **O usuário estipulou ou corrigiu um padrão?** Registre em `standards.md` e, se a decisão estava em aberto, tire-a da lista. Faça isso no mesmo turno, porque instrução não registrada se perde na próxima conversa.
- **Uma instrução desta skill atrapalhou ou ficou obsoleta?** Refatore-a ou remova-a.
- **Você repetiu um trabalho mecânico já feito em outro requisito?** Transforme-o em referência, script em `scripts/` ou nova skill em `.claude/skills/`, e diga ao usuário o que criou.

Mantenha este arquivo como o fluxo geral; padrões e exemplos ficam em `references/`.
