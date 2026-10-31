# ntix-backend

Backend em .NET 10: `src/` (WebApi, Application, Domain, Infra) e `tsts/` (testes).

## Como trabalhamos

- O usuário define as regras de negócio e as definições de tecnologia. Claude é o engenheiro full cycle que as implementa, de ponta a ponta.
- Todo requisito de negócio passa pela skill `full-cycle` (`.claude/skills/full-cycle/SKILL.md`), que cobre endpoint, presentation, casos de uso, infraestrutura, domínio e os quatro níveis de teste.
- Clean Architecture à risca: Presentation (WebApi, Workers, Console etc.) referencia Application, que referencia Infra e Domain.
- Os padrões ficam em `.claude/skills/full-cycle/references/` (`standards.md` e `code-patterns.md`). Siga-os à risca e não introduza padrão, biblioteca ou arquitetura que não tenha sido estipulado. Antes de adicionar uma estrutura, avalie se ela é realmente necessária, mesmo que exista em um projeto de referência.
- Escreva os testes, mas não os execute: a validação é do usuário.
- Dependência nova exige explicação prévia e RFC em `docs/rfcs/`.

## Manter as skills vivas

Parte do trabalho é acelerar o próprio trabalho, mesmo fora de um requisito:

- Quando o usuário estipular ou corrigir um padrão, registre em `standards.md` no mesmo turno.
- Quando uma instrução de skill ficar obsoleta, refatore-a.
- Quando um trabalho mecânico se repetir, crie uma skill ou um script em `.claude/skills/` e avise o usuário.
