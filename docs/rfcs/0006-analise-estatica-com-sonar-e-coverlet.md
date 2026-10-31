# RFC 0006: Análise estática com Sonar e Coverlet

- **Status**: Aceita
- **Data**: 2026-10-31
- **Requisito**: Análise estática de código como métrica de qualidade da entrega de software

## Contexto

A esteira compila, testa e implanta, mas não mede issues, duplicação nem cobertura. Os projetos de teste já tinham o `coverlet.collector`, sem uso na esteira.

## Decisão

| Dependência | Versão | Onde | Para quê |
|---|---|---|---|
| `coverlet.msbuild` | 6.0.4 | `tsts/UnitTests`, `tsts/IntegratedTests`, `tsts/FunctionalTests` | Coletar cobertura por propriedades do MSBuild, como `dotnet test /p:CollectCoverage=true` |
| `dotnet-sonarscanner` | 12.0.0 | `.github/workflows/static-analysis.yml` | Analisar o código e enviar o resultado ao SonarQube Cloud |

O workflow gera a cobertura com o `coverlet.collector`, em formato OpenCover. Os dois coletores não são usados na mesma execução, para não instrumentar os assemblies duas vezes.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Não adicionar dependência (implementar internamente) | Não há ganho em reescrever a análise de código e a instrumentação de cobertura |
| Apenas o `coverlet.collector` | O `coverlet.msbuild` foi pedido para também permitir a coleta pelo MSBuild |
| Atualizar o Coverlet para a versão 10.1.0 | Mudaria o `coverlet.collector` em uso em duas versões principais, o que está fora deste requisito |
| CodeQL | Foca em segurança e não cobre as métricas de duplicação e cobertura pedidas |

## Justificativa

O SonarQube Cloud reúne issues, duplicação e cobertura em um só lugar e suporta .NET 10. Coverlet tem licença MIT. As duas versões do Coverlet ficam iguais, e o `PrivateAssets="all"` impede que o `coverlet.msbuild` chegue a outro projeto pela referência a `UnitTests`. Nada disso entra na imagem da API.

## Consequências

- A esteira passa a depender do secret `SONAR_TOKEN` e das variáveis `SONAR_PROJECT_KEY` e `SONAR_ORGANIZATION`.
- O projeto no SonarQube Cloud precisa estar com a análise automática desligada, porque ela não convive com a análise pela esteira.
- O workflow roda de novo os testes unitários e integrados e sobe um SQL Server próprio, o que aumenta o tempo de cada execução.
- O `CI` chama o workflow antes dos testes funcionais, então uma falha na execução da análise bloqueia os testes funcionais e o deploy. O quality gate reprovado não falha o workflow.
- A cobertura não inclui os testes funcionais, que rodam depois da análise. O código exercitado só por eles, como os controllers, aparece sem cobertura.

## Como reverter

Remover `.github/workflows/static-analysis.yml`, o pacote `coverlet.msbuild` dos `.csproj` de teste e a configuração no GitHub.
