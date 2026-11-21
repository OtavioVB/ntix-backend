# language: pt
Funcionalidade: Vendas dos eventos da produtora
  Para acompanhar quanto cada evento vendeu e reagir a tempo
  Como produtora de eventos
  Quero ver a capacidade, os ingressos vendidos e as vendas brutas de cada evento, por lote e por dia

  Contexto:
    Dado que a produtora "mare-cultural" vende o evento "Festival Horizonte" com um lote de 100 ingressos a 12000 centavos

  Cenário: Resumo do evento depois de um pagamento
    Dado que um comprador pagou 3 ingressos
    Quando consulto meus eventos
    Então o "Festival Horizonte" mostra 3 de 100 ingressos vendidos
    E o "Festival Horizonte" mostra 36000 centavos em vendas brutas e 1 pedido

  Cenário: Reserva ainda não paga não conta como venda
    Dado que um comprador reservou 2 ingressos e ainda não pagou
    Quando consulto meus eventos
    Então o "Festival Horizonte" mostra 0 de 100 ingressos vendidos

  Cenário: Aviso de pagamento repetido não duplica a venda
    Dado que um comprador pagou 2 ingressos
    E que o aviso desse pagamento chegou de novo
    Quando consulto meus eventos
    Então o "Festival Horizonte" mostra 2 de 100 ingressos vendidos

  Cenário: Detalhe de vendas do lote
    Dado que um comprador pagou 2 ingressos
    E que outro comprador pagou 5 ingressos
    Quando consulto os lotes do evento
    Então o lote mostra 7 vendidos, 84000 centavos em vendas brutas e 2 pedidos

  Cenário: Vendas por dia sem buracos
    Dado que um comprador pagou 2 ingressos
    Quando consulto as vendas por dia dos últimos 3 dias
    Então recebo 3 dias, e só hoje tem 2 ingressos vendidos

  Cenário: Período de vendas inválido
    Quando consulto as vendas por dia com o início depois do fim
    Então sou avisada de que o período de vendas é inválido

  Cenário: Evento encerrado
    Dado que o "Festival Horizonte" terminou há 1 minuto
    Quando filtro meus eventos encerrados
    Então vejo o "Festival Horizonte" encerrado
    E ele não aparece entre os publicados
