# language: pt
Funcionalidade: Pedidos e ingressos do cliente
  Para achar meu ingresso sem depender de e-mail
  Como comprador
  Quero ver meus pedidos e meus ingressos na minha conta

  Contexto:
    Dado o evento "Festival Horizonte" publicado pela produtora "mare-cultural"
    E o lote "Pista" de 12000 centavos com 10 ingressos em venda no evento "Festival Horizonte"
    E a cliente Marina autenticada

  Cenário: Meus pedidos, do mais recente ao mais antigo
    Dado o pedido de Marina com 2 ingressos "Pista" criado há 16 minutos
    E o pedido de Marina com 1 ingresso "Pista"
    Quando Marina lista os pedidos
    Então vê 2 pedidos
    E o primeiro pedido está "PENDING_PAYMENT" e o segundo "EXPIRED"

  Cenário: Filtrar pedidos expirados
    Dado o pedido de Marina com 2 ingressos "Pista" criado há 16 minutos
    E o pedido de Marina com 1 ingresso "Pista"
    Quando Marina lista os pedidos com status "EXPIRED"
    Então vê 1 pedido

  Cenário: Próximos ingressos
    Dado o pedido de Marina com 2 ingressos "Pista"
    E que os ingressos do pedido mais recente de Marina foram emitidos: 2 ingressos "Pista"
    Quando Marina lista os ingressos próximos
    Então vê 2 ingressos do "Festival Horizonte"

  Cenário: Detalhe do ingresso
    Dado o pedido de Marina com 1 ingresso "Pista"
    E que os ingressos do pedido mais recente de Marina foram emitidos: 1 ingresso "Pista"
    Quando Marina abre o primeiro ingresso dela
    Então vê o evento "Festival Horizonte", o lote "Pista", o titular "Marina Souza" e o status "VALID"

  Cenário: Ingresso de outro cliente
    Dado o pedido de Marina com 1 ingresso "Pista"
    E que os ingressos do pedido mais recente de Marina foram emitidos: 1 ingresso "Pista"
    E o pedido de João com 1 ingresso "Pista"
    E que os ingressos do pedido mais recente de João foram emitidos: 1 ingresso "Pista"
    Quando Marina abre o ingresso de João
    Então recebe 404 com "TICKET_NOT_FOUND"

  Cenário: Paginação inválida
    Quando Marina lista os pedidos com pageSize 100
    Então recebe 400 com "ORDER_FILTER_INVALID"
