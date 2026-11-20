# language: pt
Funcionalidade: Vitrine pública de eventos
  Para escolher o que comprar
  Como comprador
  Quero ver os eventos publicados, com os lotes e quantos ingressos posso escolher

  Contexto:
    Dado que a produtora "mare-cultural" publicou o evento "Festival Horizonte" em Recife-PE com o lote "Pista" de 12000 centavos
    E que a produtora "mare-cultural" tem o evento "Teatro Z" em rascunho

  Cenário: Hub lista só eventos publicados
    Quando o comprador abre o hub
    Então vê o evento "Festival Horizonte" a partir de 12000 centavos
    E não vê o evento "Teatro Z"
    E a resposta pode ficar em cache por 15 segundos

  Cenário: Filtro por UF
    Quando o comprador filtra a UF "SP"
    Então a lista vem vazia

  Cenário: Loja da produtora
    Dado que a produtora "produtora-y" publicou o evento "Show Y" em Recife-PE com o lote "Pista" de 8000 centavos
    Quando o comprador abre a loja da "mare-cultural"
    Então vê só o evento "Festival Horizonte"

  Cenário: Página do evento
    Quando o comprador abre a página do evento "Festival Horizonte"
    Então vê o lote "Pista" em venda com até 6 ingressos selecionáveis
    E a página não mostra a quantidade total do lote

  Cenário: Lote pausado não aparece
    Dado que o lote "VIP" do evento "Festival Horizonte" está pausado
    Quando o comprador abre a página do evento "Festival Horizonte"
    Então não vê o lote "VIP"

  Cenário: Rascunho não é encontrado
    Quando o comprador abre a página do evento "Teatro Z"
    Então a página do evento não é encontrada

  Cenário: Filtro inválido
    Quando o comprador pede 500 eventos por página
    Então a consulta é recusada com "EVENT_FILTER_INVALID"
