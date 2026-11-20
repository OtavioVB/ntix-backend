# language: pt
Funcionalidade: Eventos da produtora
  Para preparar o que vou vender, com calma e sem expor antes da hora
  Como produtora de eventos
  Quero criar, listar, consultar e editar meus eventos em rascunho

  Contexto:
    Dado que sou a produtora "mare-cultural" e estou conectada à aba Eventos

  Cenário: Criar um evento em rascunho
    Quando crio o evento "Festival Horizonte" daqui a 60 dias
    Então o evento é criado
    E o evento fica em rascunho com o endereço "festival-horizonte"

  Cenário: Endereço já usado por outra produtora
    Dado que a produtora "produtora-y" já criou o evento "Festival Horizonte"
    Quando crio o evento "Festival Horizonte" daqui a 60 dias
    Então o evento é criado
    E o endereço do evento é "festival-horizonte" com uma versão de 5 caracteres

  Cenário: Período inválido
    Quando crio um evento que termina antes de começar
    Então sou avisada com "EVENT_PERIOD_INVALID"

  Cenário: Categoria desconhecida
    Quando crio o evento "Rodeio da Serra" na categoria "RODEIO"
    Então sou avisada com "EVENT_CATEGORY_INVALID"

  Cenário: Editar o título não muda o endereço
    Dado que criei o evento "Festival Horizonte" daqui a 60 dias
    Quando mudo o título do evento para "Festival Horizonte 2027"
    Então o evento passa a se chamar "Festival Horizonte 2027"
    E o endereço do evento continua "festival-horizonte"

  Cenário: Listar por situação
    Dado que criei o evento "Festival Horizonte" daqui a 60 dias
    E que criei o evento "Teatro Aberto" daqui a 30 dias, já publicado
    Quando listo meus eventos em rascunho
    Então vejo só o evento "Festival Horizonte"

  Cenário: Evento de outra produtora
    Dado que a produtora "produtora-y" já criou o evento "Show Y"
    Quando consulto o evento "Show Y"
    Então o evento não é encontrado

  Cenário: Evento já iniciado não é editado
    Dado que criei o evento "Festival Horizonte" daqui a 60 dias
    E que o evento "Festival Horizonte" começou há 1 hora
    Quando mudo o título do evento para "Festival Horizonte 2027"
    Então sou avisada com "EVENT_ALREADY_STARTED"

  Cenário: Painel sem sessão
    Quando listo os eventos da produtora "mare-cultural" sem estar conectada
    Então o acesso é negado por falta de sessão

  Cenário: Painel de outra produtora
    Dado que a produtora "produtora-y" já criou o evento "Show Y"
    Quando listo os eventos da produtora "produtora-y"
    Então o acesso é proibido
