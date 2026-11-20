# language: pt
Funcionalidade: Autenticação do cliente
  Para comprar ingressos e ver os meus pedidos
  Como comprador
  Quero entrar na Ntix com o meu e-mail ou CPF e a minha senha

  Contexto:
    Dado que a cliente "Marina Souza" tem conta com o e-mail "marina@exemplo.com"

  Cenário: Login por e-mail
    Quando ela entra com o e-mail e a senha correta
    Então recebe a sessão com access token e refresh token
    E o login devolve só o primeiro nome "Marina"

  Cenário: Login por CPF com máscara
    Quando ela entra com o CPF com máscara e a senha correta
    Então recebe a sessão com access token e refresh token

  Cenário: Conta inexistente e senha errada respondem igual
    Quando alguém entra com "ninguem@exemplo.com" e a senha correta
    E ela entra com o e-mail e a senha "Senha@errada1"
    Então as duas tentativas são recusadas como credencial inválida

  Cenário: Limite de tentativas
    Dado que ela errou a senha 10 vezes com o CPF
    Quando ela entra com o CPF com máscara e a senha correta
    Então o login é recusado pelo limite de tentativas, com o tempo de espera

  Cenário: Renovação rotativa
    Dado que ela está conectada
    Quando a cliente renova a sessão
    Então recebe um novo par de tokens
    E o refresh token anterior deixa de funcionar

  Cenário: Meus dados
    Dado que ela está conectada
    Quando ela consulta os próprios dados
    Então vê o CPF ofuscado

  Cenário: Token do cliente não abre o painel do produtor
    Dado que ela está conectada
    Quando ela tenta abrir os eventos da produtora "mare-cultural" com o token dela
    Então o acesso é recusado com 403

  Cenário: Token do produtor não abre a conta do cliente
    Dado que o usuário da produtora "mare-cultural" está conectado ao painel
    Quando ele consulta os dados do cliente com o token dele
    Então o acesso é recusado com 403
