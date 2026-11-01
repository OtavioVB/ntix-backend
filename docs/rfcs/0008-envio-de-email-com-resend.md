# RFC 0008: Envio de email com Resend

- **Status**: Aceita
- **Data**: 2026-10-31
- **Requisito**: REQ1, criação de produtor de eventos (RN19)

## Contexto

O cadastro de produtor envia um link de verificação ao email informado. A solução ainda não tem nenhum envio de email.

## Decisão

Usar a Resend, definida pelo usuário, como serviço externo de envio, chamando a API HTTP (`POST https://api.resend.com/emails`) a partir da `Infra`, com `Microsoft.Extensions.Http` 10.0.14 para gerenciar o `HttpClient`.

## Alternativas consideradas

| Alternativa | Por que não foi escolhida |
|---|---|
| Não adicionar dependência (`new HttpClient` próprio) | Exige gerenciar o ciclo de vida das conexões, que o `IHttpClientFactory` já resolve |
| SDK da Resend para .NET | Traz um pacote de terceiro para uma única chamada POST |
| SMTP genérico | O usuário escolheu a Resend |
| Amazon SES | O usuário escolheu a Resend |

## Justificativa

`Microsoft.Extensions.Http` é da Microsoft, tem licença MIT, acompanha a versão das demais `Microsoft.Extensions.*` do projeto e é compatível com .NET 10. O uso da API da Resend fica atrás de `IEmailSender`, então o caso de uso não conhece o provedor.

## Consequências

- A API exige `ResendConfiguration__ApiKey`, `ResendConfiguration__From` e `EmailVerificationConfiguration__LinkBaseUrl` para subir.
- O remetente precisa ser de um domínio verificado na conta da Resend, e o envio tem custo conforme o plano contratado.
- O endereço de email e o link de verificação passam pela Resend, que vira operadora de dados pessoais para a LGPD.
- `devops/docker-compose.yml` não ganha serviço novo, porque a Resend é externa. Os testes integrados e funcionais substituem `IEmailSender` e não enviam emails reais.

## Como reverter

Trocar a implementação de `IEmailSender`, remover `Infra/Resend`, o pacote `Microsoft.Extensions.Http` e as variáveis `ResendConfiguration__*`.
