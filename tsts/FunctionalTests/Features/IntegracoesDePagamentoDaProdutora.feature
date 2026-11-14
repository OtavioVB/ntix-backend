# language: pt
Funcionalidade: Integrações de pagamento da produtora
  Para receber o dinheiro dos ingressos na minha própria conta, pelo fornecedor que eu escolher
  Como produtora de eventos
  Quero conectar meus fornecedores de pagamento e definir, para cada método, por qual conta cobrar primeiro

  Contexto:
    Dado que sou a produtora "produtora-x" e estou conectada ao painel
    E o catálogo da Ntix oferece o AbacatePay para Pix, boleto e cartão de crédito

  Cenário: Conectar o AbacatePay
    Quando conecto o AbacatePay como "AbacatePay - conta principal" com a chave de API "abc_dev_chave-secreta-a91f"
    Então a integração "AbacatePay - conta principal" fica ativa
    E vejo da chave de API só o final "a91f"
    E a chave de API não fica guardada como foi escrita

  Cenário: Chave de API ausente
    Quando conecto o AbacatePay como "AbacatePay - conta principal" sem a chave de API
    Então sou avisada de que falta uma credencial obrigatória
    E nenhuma integração é criada

  Cenário: Credencial que o fornecedor não usa
    Quando conecto o AbacatePay como "AbacatePay - conta principal" informando também a credencial "SENHA"
    Então sou avisada de que informei uma credencial que o fornecedor não usa
    E nenhuma integração é criada

  Cenário: Pix por uma conta, boleto e cartão por outra
    Dado que tenho as integrações ativas "Conta Pix" e "Conta boleto e cartão"
    Quando defino o Pix pela "Conta Pix"
    E defino o boleto pela "Conta boleto e cartão"
    E defino o cartão de crédito pela "Conta boleto e cartão"
    Então o Pix é cobrado na ordem "Conta Pix"
    E o boleto é cobrado na ordem "Conta boleto e cartão"
    E o cartão de crédito é cobrado na ordem "Conta boleto e cartão"

  Cenário: A segunda integração assume quando a primeira é inativada
    Dado que tenho as integrações ativas "Conta principal" e "Conta reserva"
    E o Pix está definido pela "Conta principal" e depois pela "Conta reserva"
    Quando inativo a integração "Conta principal"
    Então o Pix é cobrado na ordem "Conta reserva"
    E o Pix continua listando a "Conta principal" como inativa

  Cenário: A Ntix desliga um fornecedor do catálogo
    Dado que o catálogo também oferece um fornecedor de teste só para Pix
    E que tenho a integração ativa "Conta fornecedor de teste" nesse fornecedor
    E que tenho a integração ativa "Conta AbacatePay" no AbacatePay
    E o Pix está definido pela "Conta fornecedor de teste" e depois pela "Conta AbacatePay"
    Quando a Ntix desliga o fornecedor de teste
    Então o Pix é cobrado na ordem "Conta AbacatePay"

  Cenário: Método sem integração não é oferecido
    Dado que tenho as integrações ativas "Conta Pix" e "Conta reserva"
    E o Pix está definido pela "Conta Pix" e depois pela "Conta reserva"
    Então o boleto não está disponível para os compradores

  Cenário: A mesma integração duas vezes no método
    Dado que tenho as integrações ativas "Conta principal" e "Conta reserva"
    Quando defino o Pix pela "Conta principal" e depois pela "Conta principal"
    Então sou avisada de que uma integração só pode aparecer uma vez no método

  Cenário: Fornecedor que não aceita o método
    Dado que o catálogo também oferece um fornecedor de teste só para Pix
    E que tenho a integração ativa "Conta fornecedor de teste" nesse fornecedor
    Quando defino o boleto pela "Conta fornecedor de teste"
    Então sou avisada de que o fornecedor não aceita esse método

  Cenário: Integração de outra produtora não entra na minha rota
    Dado que a produtora "produtora-y" tem a integração ativa "Conta da Y"
    Quando defino o Pix pela "Conta da Y"
    Então sou avisada de que a integração não existe para a minha produtora

  Cenário: Painel sem estar conectada
    Quando alguém consulta as integrações da "produtora-x" sem estar conectado
    Então o acesso é negado por falta de autenticação

  Cenário: Painel de outra produtora
    Dado que a produtora "produtora-y" está conectada ao painel
    Quando ela consulta as integrações da "produtora-x"
    Então o acesso é negado por ser de outra produtora
