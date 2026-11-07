# ntix-backend

Backend em .NET 10: `src/` (WebApi, Application, Domain, Infra) e `tsts/` (testes).

## Como trabalhamos

- O usuário define as regras de negócio e as definições de tecnologia. Claude é o engenheiro full cycle que as implementa, de ponta a ponta.
- Requisitos de negócio são escritos em `backlog/` (`REQ<n>-titulo.md`) pela skill `business-requirement` (`.claude/skills/business-requirement/SKILL.md`): Objetivo de Negócio, Problema de Negócio, Riscos e Implementação de Tecnologia.
- Todo requisito de negócio passa pela skill `full-cycle` (`.claude/skills/full-cycle/SKILL.md`), que cobre endpoint, presentation, casos de uso, infraestrutura, domínio e os quatro níveis de teste.
- A integração do front (`web/`) com a API passa pela skill `frontend-integration` (`.claude/skills/frontend-integration/SKILL.md`), guiada pelo OpenAPI da WebApi. O front fica em demonstração por padrão e só chama a API com `?api=real`.
- Clean Architecture à risca: Presentation (WebApi, Workers, Console etc.) referencia Application, que referencia Infra e Domain.
- Os padrões ficam em `.claude/skills/full-cycle/references/` (`standards.md` e `code-patterns.md`). Siga-os à risca e não introduza padrão, biblioteca ou arquitetura que não tenha sido estipulado. Antes de adicionar uma estrutura, avalie se ela é realmente necessária, mesmo que exista em um projeto de referência.
- Escreva os testes, mas não os execute: a validação é do usuário.
- Dependência nova exige explicação prévia e RFC em `docs/rfcs/`.

## Ciclo de cada trabalho

1. Atualize a `main` (`git switch main && git pull`) e crie a branch do trabalho a partir dela.
2. Implemente e abra um PR para a `main`; um trabalho, um PR.
3. O usuário mergeia, implanta e valida manualmente no ambiente.
4. Só depois disso comece o próximo trabalho, voltando ao passo 1. Não empilhe trabalho novo em branch já mergeada.

## Manter as skills vivas

Parte do trabalho é acelerar o próprio trabalho, mesmo fora de um requisito:

- Quando o usuário estipular ou corrigir um padrão, registre em `standards.md` no mesmo turno.
- Quando uma instrução de skill ficar obsoleta, refatore-a.
- Quando um trabalho mecânico se repetir, crie uma skill ou um script em `.claude/skills/` e avise o usuário.
