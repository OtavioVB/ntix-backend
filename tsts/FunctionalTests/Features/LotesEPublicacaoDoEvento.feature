# language: pt
Funcionalidade: Lotes de ingressos e publicação do evento
  Para colocar o meu evento à venda com preço e estoque
  Como produtora de eventos
  Quero cadastrar os lotes de cada evento e publicá-lo quando estiver pronto

  Contexto:
    Dado que sou a produtora "mare-cultural" montando o evento "Festival Horizonte" em rascunho

  Cenário: Criar um lote
    Quando crio o lote "1º lote - Pista" de 12000 centavos com 500 ingressos
    Então o lote é criado com 500 ingressos disponíveis

  Cenário: Preço abaixo do mínimo
    Quando crio o lote "Promocional" de 50 centavos com 100 ingressos
    Então a operação é recusada com "BATCH_PRICE_INVALID"

  Cenário: Venda do lote termina depois do evento
    Quando crio o lote "Tardio" com a venda terminando depois do evento
    Então a operação é recusada com "BATCH_SALES_PERIOD_INVALID"

  Cenário: Nome repetido no evento
    Dado que o evento já tem o lote "VIP"
    Quando crio o lote "VIP" de 30000 centavos com 50 ingressos
    Então a operação conflita com "BATCH_NAME_ALREADY_EXISTS"

  Cenário: Pausar um lote
    Dado que o evento já tem o lote "VIP"
    Quando pauso o lote "VIP"
    Então o lote "VIP" fica com o estado de venda "PAUSED"

  Cenário: Publicar o evento
    Dado que o evento já tem o lote "VIP"
    E que recebo Pix por uma integração AbacatePay ativa
    Quando publico o evento
    Então o evento fica publicado
    E a publicação fica registrada na outbox

  Cenário: Publicar sem lote
    Dado que recebo Pix por uma integração AbacatePay ativa
    Quando publico o evento
    Então a operação é recusada com "EVENT_WITHOUT_BATCH"

  Cenário: Publicar sem forma de pagamento
    Dado que o evento já tem o lote "VIP"
    Quando publico o evento
    Então a operação é recusada com "EVENT_WITHOUT_PAYMENT_METHOD"

  Cenário: Publicar duas vezes
    Dado que o evento já tem o lote "VIP"
    E que recebo Pix por uma integração AbacatePay ativa
    E que o evento já foi publicado
    Quando publico o evento
    Então a operação conflita com "EVENT_NOT_DRAFT"

  Cenário: Lotes do evento de outra produtora
    Dado que a produtora "produtora-y" tem um evento com lotes
    Quando consulto os lotes do evento da "produtora-y" pela minha produtora
    Então o evento da outra produtora não é encontrado
