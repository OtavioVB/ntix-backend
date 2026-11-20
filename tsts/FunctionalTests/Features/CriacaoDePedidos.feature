# language: pt
Funcionalidade: Criação de pedidos
  Para pagar com a certeza de que o ingresso está guardado
  Como comprador
  Quero que meu pedido reserve os ingressos pelo preço que vi

  Contexto:
    Dado o evento "Festival Horizonte" publicado pela produtora "mare-cultural"
    E o lote "Pista" de 12000 centavos com 10 ingressos em venda no evento "Festival Horizonte"
    E a cliente Marina autenticada

  Cenário: Criar um pedido
    Quando Marina pede 2 ingressos "Pista"
    Então recebe o pedido aguardando pagamento de 24000 centavos
    E o pedido expira em 15 minutos
    E restam 8 ingressos "Pista"

  Cenário: Preço congelado
    Dado o pedido de Marina com 2 ingressos "Pista"
    Quando o produtor muda o preço do lote "Pista" para 15000 centavos
    Então o pedido de Marina continua com 24000 centavos

  Cenário: Limite por pedido
    Quando Marina pede 7 ingressos "Pista"
    Então o pedido é recusado com "ORDER_TICKET_LIMIT_EXCEEDED"

  Cenário: Estoque insuficiente
    Dado que resta 1 ingresso "Pista"
    Quando Marina pede 2 ingressos "Pista"
    Então o pedido é recusado com "BATCH_INSUFFICIENT_AVAILABILITY"

  Cenário: Último ingresso disputado
    Dado que resta 1 ingresso "Pista"
    E a cliente João autenticada
    Quando Marina e João pedem 1 ingresso "Pista" ao mesmo tempo
    Então só um dos dois recebe o pedido
    E o outro é recusado com "BATCH_INSUFFICIENT_AVAILABILITY"

  Cenário: Reserva vencida volta ao estoque
    Dado o pedido de Marina com 2 ingressos "Pista" criado há 16 minutos
    Quando Marina consulta o pedido
    Então o pedido está expirado
    E restam 10 ingressos "Pista"

  Cenário: Lote fora da janela
    Dado o lote "2º lote" com venda a partir de amanhã no evento "Festival Horizonte"
    Quando Marina pede 1 ingresso "2º lote"
    Então o pedido é recusado com "BATCH_NOT_ON_SALE"

  Cenário: Lote de outro evento
    Dado o evento "Show Y" publicado pela produtora "mare-cultural"
    E o lote "Arquibancada" de 8000 centavos com 10 ingressos em venda no evento "Show Y"
    Quando Marina pede um lote do evento "Show Y" no pedido de "Festival Horizonte"
    Então o pedido é recusado com "BATCH_NOT_FOUND"

  Cenário: Pedido de outra cliente
    Dado a cliente João autenticada
    E o pedido de João com 1 ingresso "Pista"
    Quando Marina consulta o código do pedido de João
    Então a consulta não encontra o pedido

  Cenário: Primeiro pedido cria o vínculo com o produtor
    Quando Marina faz o primeiro pedido com o produtor "mare-cultural"
    Então Marina passa a ser cliente de "mare-cultural"

  Cenário: Pedido exige uma cliente autenticada
    Quando alguém tenta pedir sem entrar
    Então o pedido é recusado por falta de login

  Cenário: Pedido não aceita o usuário de uma produtora
    Quando o usuário da produtora tenta pedir
    Então o pedido é recusado por não ser de cliente
