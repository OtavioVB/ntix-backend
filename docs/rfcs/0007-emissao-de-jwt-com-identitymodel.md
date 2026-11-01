# RFC 0007: Emissão de JWT com Microsoft.IdentityModel.JsonWebTokens

- **Status**: Aceita
- **Data**: 2026-10-31
- **Requisito**: autenticação do cliente na plataforma, com token JWT de até 4 horas contendo o ID do produtor

## Contexto

A autenticação do cliente devolve um JWT assinado. A biblioteca padrão do .NET oferece HMAC, mas não a montagem e a serialização do token.

## Decisão

Adotar `Microsoft.IdentityModel.JsonWebTokens` 8.16.0 no projeto `Infra`, atrás da interface `IJwtTokenGenerator`. É a versão mínima exigida pelo `Microsoft.Data.SqlClient` 7.1.1, que já trazia o pacote de forma transitiva. A assinatura é HS256, com a chave em `JwtConfiguration__SecretKey`.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Não adicionar dependência (implementar internamente) | Escrever serialização e assinatura de token à mão aumenta o risco em um ponto sensível de segurança |
| `System.IdentityModel.Tokens.Jwt` | É a API anterior; a própria Microsoft indica `JsonWebTokens` para código novo |
| Assinatura assimétrica (RS256) | Hoje só esta API emite e consome o token, então uma chave simétrica basta |

## Justificativa

É mantida pela Microsoft, compatível com .NET 10, tem licença MIT e não tem custo.

## Consequências

- A API passa a exigir `JwtConfiguration__SecretKey`, com ao menos 32 bytes, e não inicia sem ela.
- O deploy passa a exigir o secret `VPS_JWT_SECRET_KEY`.
- Trocar a chave invalida os tokens já emitidos.
- A validação do token nos endpoints protegidos fica para o requisito que precisar dela.

## Como reverter

Remover o pacote de `Infra.csproj` e substituir a implementação de `IJwtTokenGenerator`.
