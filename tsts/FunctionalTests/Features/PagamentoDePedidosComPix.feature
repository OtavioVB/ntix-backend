# language: pt
Funcionalidade: Pagamento de pedidos com Pix
  Para pagar o ingresso que reservei
  Como comprador
  Quero gerar um Pix na conta do produtor, mesmo que uma das contas falhe

  Contexto:
    Dado o produtor "mare-cultural" com as integrações AbacatePay "Conta A" e "Conta B" na rota de PIX, nessa ordem
    E o pedido de Marina de 2 ingressos de 19500 centavos criado agora

  Cenário: Métodos disponíveis
    Quando Marina consulta os métodos do pedido
    Então recebe só PIX

  Cenário: Gerar o Pix
    Quando Marina paga o pedido com PIX
    Então recebe o QR Code e o Pix copia e cola
    E o Pix vale até 2 minutos antes do fim da reserva
    E o pagamento fica PENDING com a integração "Conta A"
    E o AbacatePay recebeu 39000 centavos com o CPF de Marina

  Cenário: Fallback para a próxima integração
    Dado que a "Conta A" não responde em 5 segundos
    Quando Marina paga o pedido com PIX
    Então recebe o QR Code e o Pix copia e cola
    E o pagamento fica PENDING com a integração "Conta B"

  Cenário: Todas as integrações falham
    Dado que "Conta A" e "Conta B" respondem 500
    Quando Marina paga o pedido com PIX
    Então o pagamento é recusado com "PAYMENT_PROVIDERS_UNAVAILABLE"
    E nenhum pagamento é gravado

  Cenário: Segundo clique devolve o mesmo Pix
    Dado que Marina já gerou o Pix do pedido
    Quando Marina paga o pedido com PIX de novo
    Então recebe o mesmo Pix copia e cola
    E o AbacatePay foi chamado uma vez só

  Cenário: Reserva quase vencida
    Dado o pedido criado há 9 minutos
    Quando Marina paga o pedido com PIX
    Então o pagamento é recusado com "ORDER_PAYMENT_WINDOW_TOO_SHORT"

  Cenário: Pedido vencido
    Dado o pedido criado há 16 minutos
    Quando Marina paga o pedido com PIX
    Então o pagamento é recusado com "ORDER_EXPIRED"

  Cenário: Método sem integração
    Dado que a rota de PIX do produtor está vazia
    Quando Marina paga o pedido com PIX
    Então o pagamento é recusado com "PAYMENT_METHOD_UNAVAILABLE"

  Cenário: Pedido de outra cliente
    Quando João paga o pedido de Marina
    Então o pagamento é recusado com "ORDER_NOT_FOUND"

  Cenário: Pedido já pago
    Dado que o pedido de Marina já foi pago
    Quando Marina paga o pedido com PIX
    Então o pagamento é recusado com "ORDER_ALREADY_PAID"
