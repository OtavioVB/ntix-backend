# language: pt
Funcionalidade: Autenticação do usuário da produtora
  Para operar o painel com dados reais e com cada alteração atribuída a quem a fez
  Como administradora de uma produtora
  Quero entrar com e-mail e senha e continuar conectada sem digitar a senha a toda hora

  Contexto:
    Dado a produtora "produtora-x" com a administradora "ana@produtora.com.br" e a senha "Senha@123"

  Cenário: Login de quem ainda não verificou o e-mail
    Dado que a administradora "ana@produtora.com.br" ainda não verificou o e-mail
    Quando "ana@produtora.com.br" entra com a senha "Senha@123"
    Então ela recebe uma sessão para usar o painel da "produtora-x"
    E a sessão não revela o e-mail dela

  Cenário: Senha errada e e-mail inexistente têm a mesma resposta
    Quando "ana@produtora.com.br" entra com a senha "Errada@123"
    E "ninguem@produtora.com.br" entra com a senha "Senha@123"
    Então as duas tentativas são recusadas com a mesma mensagem de e-mail ou senha incorretos

  Cenário: Usuária bloqueada
    Dado que a administradora "ana@produtora.com.br" está bloqueada
    Quando "ana@produtora.com.br" entra com a senha "Senha@123"
    Então ela é avisada de que o acesso está bloqueado

  Cenário: Limite de tentativas de login
    Dado que já houve 10 tentativas de login com "ana@produtora.com.br" nos últimos 15 minutos
    Quando "ana@produtora.com.br" entra com a senha "Senha@123"
    Então ela é avisada de que houve tentativas demais
    E fica sabendo em quanto tempo pode tentar de novo

  Cenário: Renovação da sessão
    Dado que a administradora "ana@produtora.com.br" está conectada
    Quando ela renova a sessão
    Então ela recebe uma sessão nova
    E a renovação anterior deixa de funcionar

  Cenário: Reuso de uma renovação já usada
    Dado que a administradora "ana@produtora.com.br" está conectada
    E que ela já renovou a sessão uma vez
    Quando a renovação anterior é apresentada de novo
    Então a renovação é recusada
    E a renovação mais nova também deixa de funcionar

  Cenário: Saída do painel
    Dado que a administradora "ana@produtora.com.br" está conectada
    Quando ela sai do painel
    Então a saída é confirmada
    E a renovação dela deixa de funcionar

  Cenário: Painel com a sessão do login
    Dado que a administradora "ana@produtora.com.br" está conectada
    Quando ela abre a aba Integrações da "produtora-x"
    Então o painel atende

  Cenário: Painel sem sessão
    Quando alguém abre a aba Integrações da "produtora-x" sem sessão
    Então o painel pede para entrar

  Cenário: Isolamento entre produtoras
    Dado a produtora "produtora-y" com a administradora "bia@produtora-y.com.br" e a senha "Senha@123"
    E que a administradora "bia@produtora-y.com.br" está conectada
    Quando ela abre a aba Integrações da "produtora-x"
    Então o painel recusa por ser de outra produtora
