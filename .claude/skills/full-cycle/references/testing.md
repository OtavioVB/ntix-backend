# Testes

Os quatro níveis são obrigatórios em cada requisito. Os testes são escritos, não executados.

Os exemplos mostram a forma esperada, não o domínio. Use `tsts/UnitTests/UseCases/CreateCustomer` e os demais testes do cadastro de cliente como modelo para os próximos requisitos. Nos namespaces de teste, não repita `Domain` nem `Application` (`UnitTests.ValueObjects`, por exemplo), para não esconder os namespaces de produção.

## Unitários (`tsts/UnitTests`)

xUnit e AAA. Cubra os value objects, as regras de negócio e a orquestração dos casos de uso, incluindo cenários de erro e valores-limite, sem dependências reais. Verifique o `MethodResult` e o código das notificações, não só o sucesso ou o erro.

Verifique o controle da transação: commit só quando a regra permite e rollback nas recusas e exceções.

Verifique também os logs com `Support/CapturingLogger`: o evento esperado foi registrado, o dado pessoal aparece ofuscado e o valor completo não aparece.

```csharp
public sealed class EmailValueObjectTests
{
    [Fact]
    public void Given_EmptyEmail_When_Building_Should_ReturnEmptyEmailNotification()
    {
        // Arrange
        var email = string.Empty;

        // Act
        var valueObject = EmailValueObject.Build(email);

        // Assert
        Assert.False(valueObject.IsValid);
        Assert.Contains(valueObject.MethodResult.Notifications, notification => notification.Code == "EMAIL_COULD_NOT_BE_EMPTY");
    }
}
```

## Integrados (`tsts/IntegratedTests`)

xUnit, AAA e a mesma nomenclatura, usando as dependências criadas para o requisito, no ar via `devops/docker-compose.yml`. Verifique o comportamento contra a dependência real (o que foi gravado, lido ou enviado), não só o retorno do método, incluindo concorrência quando a regra depender dela.

`tsts/Shared/SqlServerTestDatabase`, vinculado aos projetos integrados e funcionais, cria um banco exclusivo por execução, aplica os scripts de `data/` e o remove no final. Os dados de cada teste são únicos, para os testes não dependerem da ordem de execução.

```csharp
public sealed class OrderRepositoryTests
{
    [Fact]
    public async Task Given_ValidOrder_When_Saving_Should_PersistOrder()
    {
        // Arrange
        // Act
        // Assert
    }
}
```

## Funcionais (`tsts/FunctionalTests`)

Reqnroll com Gherkin em português (`# language: pt`). Features em `Features/`, passos em `StepDefinitions/` e `Support/NtixApiHooks`, que sobe a `WebApi` real com `WebApplicationFactory` e limpa os dados antes de cada cenário. Por isso a paralelização fica desligada neste projeto.

Escreva como alguém do negócio descreveria a regra: o cenário diz o que acontece para quem usa o sistema, não como o sistema faz. Rota, status HTTP, JSON e tabela ficam nos step definitions. Um bom teste é o usuário ler a feature e reconhecer a regra que definiu.

```gherkin
Funcionalidade: Confirmação de pedido
  Para garantir que só pedidos válidos sigam adiante
  Como cliente
  Quero ser avisado quando meu pedido não puder ser confirmado

  Cenário: Pedido sem itens não pode ser confirmado
    Dado que tenho um pedido sem itens
    Quando tento confirmar o pedido
    Então sou informado de que o pedido precisa ter ao menos um item
```

Evite: `Dado que envio um POST para /orders com body vazio` / `Então o status é 400`.

## Carga (`tsts/LoadTests`)

Fluxo com funil tem também um script `<funcionalidade>-funnel-tests.js`, com o ramo de cada requisição sorteado por pesos que mudam aleatoriamente ao longo da execução (nunca proporções fixas), classificação pela resposta e totais no `handleSummary`, como `customers-funnel-tests.js`. Não adicione gatilho de falha em produção para exercitar erros: documente como provocá-los pela infraestrutura.

k6, um script por fluxo: `tsts/LoadTests/<funcionalidade>-<fluxo>-tests.js`. Use cenários com `ramping-vus` e métricas próprias (`Rate`, `Trend`) para os pontos que importam, nomeando os `check` como `[Fluxo][Etapa] Resultado`. Leia URL e credenciais de variáveis de ambiente. Os números de carga e os thresholds vêm do usuário; enquanto não forem definidos, pergunte e não invente.

```javascript
import http from "k6/http";
import { check } from "k6";
import { Rate } from "k6/metrics";

const BASE_URL = __ENV.BASE_URL;

const requestFailRate = new Rate("create_order_fail");

export const options = {
    scenarios: {
        create_order: {
            executor: "ramping-vus",
            startVUs: 0,
            stages: [], // definidos com o usuário
            gracefulRampDown: "30s",
        },
    },
    thresholds: {}, // definidos com o usuário
};

export default function () {
    const response = http.post(`${BASE_URL}/api/v1/orders`, JSON.stringify({}), {
        headers: { "Content-Type": "application/json" },
    });

    const ok = check(response, {
        "[CreateOrder][Request] Status 200": (r) => r.status === 200,
    });

    requestFailRate.add(!ok);
}
```
