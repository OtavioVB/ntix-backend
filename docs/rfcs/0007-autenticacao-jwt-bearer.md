# RFC 0007: Autenticação com JWT Bearer

- **Status**: Proposta
- **Data**: 2026-11-14
- **Requisito**: REQ5 - Autenticação do Usuário do Produtor

## Contexto

O REQ5 exige que o usuário do produtor se autentique e que os endpoints do painel validem a sessão em toda requisição. O usuário escolheu um access token JWT de 15 minutos com refresh token rotativo.

O ASP.NET Core já traz `AddAuthentication` e `AddAuthorization` no framework compartilhado, mas não o handler que valida um JWT. Esse handler é distribuído em um pacote à parte.

## Decisão

Adotar `Microsoft.AspNetCore.Authentication.JwtBearer`, na versão 10.0.x mais recente na implementação, alinhada ao SDK do .NET 10.

- **Onde**: só no projeto `WebApi`, para validar o token.
- **Emissão do token**: usa o `Microsoft.IdentityModel.JsonWebTokens`, que chega por transitividade, na camada `Application`.
  - Se essa referência transitiva não alcançar a `Application`, o pacote entra explicitamente nela, na mesma versão, e esta RFC é atualizada.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Não adicionar dependência (implementar internamente) | Assinar e validar JWT à mão (HMAC, Base64Url, `exp`, `aud`, `iss`, `alg`) é código de segurança sensível, fácil de errar, e duplicaria o que a Microsoft mantém |
| Sessão opaca em cookie validada no banco | Sem pacote novo, mas consulta o banco em toda requisição e exige CORS com credenciais e proteção CSRF; não foi a escolha do usuário |
| Provedor de identidade externo (Keycloak, Auth0, Duende) | Infraestrutura ou custo a mais para um único tipo de usuário; fora do que foi estipulado |

## Justificativa

- Pacote oficial da Microsoft, versionado junto com o ASP.NET Core 10 e suportado pelo mesmo ciclo de vida.
- Licença MIT, sem custo.
- Integra com `[Authorize]`, políticas e `ClaimsPrincipal` sem código de infraestrutura próprio.

## Consequências

- `WebApi.csproj` ganha um `PackageReference`; o `Dockerfile` não muda, porque o `dotnet restore` já baixa os pacotes.
- Nova configuração obrigatória `JwtConfiguration:SigningKey`, vinda do secret `JWT_SIGNING_KEY` na esteira e do `devops/.env` no ambiente local.
- Testes funcionais emitem tokens com a mesma chave de teste para chamar endpoints protegidos.
- Nenhum serviço novo em `devops/docker-compose.yml`.

## Como reverter

1. Remover o `PackageReference` e a chamada `AddJwtBearer`.
2. Trocar por outro esquema de autenticação registrado em `AddAuthentication`.
3. Os controllers não mudam, porque dependem só de `[Authorize]` e das políticas.
4. As sessões gravadas em `producers.producer_user_session` continuam válidas para um esquema de token opaco.
