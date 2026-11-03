# language: pt
Funcionalidade: Cadastro de produtora
  Para começar a usar a Ntix
  Como visitante que representa uma produtora de eventos
  Quero cadastrar a produtora com nome, e-mail e telefone, junto do administrador que vai acessá-la

  Cenário: Produtora se cadastra com dados válidos
    Quando cadastro a produtora "Ação Eventos & Shows" com o e-mail "contato@acao.com" e o administrador "Maria da Silva"
    Então a produtora é cadastrada aguardando o envio para aprovação
    E o administrador "Maria da Silva" é criado aguardando a verificação do e-mail
    E a produtora recebe o endereço "acao-eventos-shows"
    E sou avisado de que "A produtora foi cadastrada. Verifique o e-mail informado para acessar a plataforma."

  Cenário: A carteira interna da produtora é solicitada no cadastro
    Quando cadastro a produtora "Ação Eventos & Shows" com o e-mail "contato@acao.com" e o administrador "Maria da Silva"
    Então a criação da carteira interna da produtora fica registrada uma única vez

  Cenário: O telefone fica registrado no formato internacional
    Quando cadastro a produtora com o telefone "(11) 98765-4321"
    Então a produtora é cadastrada aguardando o envio para aprovação
    E o telefone "+5511987654321" fica registrado na produtora

  Cenário: Não é possível cadastrar uma produtora com um e-mail já cadastrado
    Dado que já existe a produtora "Ação Eventos & Shows" com o e-mail "contato@acao.com"
    Quando cadastro a produtora "Outra Produtora" com o e-mail "CONTATO@acao.com" e o administrador "Joana Souza"
    Então nada é cadastrado
    E sou avisado de que "Já existe um cadastro de produtora com o e-mail informado."

  Cenário: Duas produtoras com o mesmo nome recebem endereços diferentes
    Dado que já existe a produtora "Ação Eventos & Shows" com o e-mail "contato@acao.com"
    Quando cadastro a produtora "Ação Eventos & Shows" com o e-mail "outra@acao.com" e o administrador "Joana Souza"
    Então a produtora é cadastrada aguardando o envio para aprovação
    E a produtora recebe o endereço "acao-eventos-shows" seguido de uma versão de cinco letras ou números

  Esquema do Cenário: Um nome que coincide com uma palavra reservada recebe uma versão
    Quando cadastro a produtora "<nome>" com o e-mail "contato@reservada.com" e o administrador "Maria da Silva"
    Então a produtora é cadastrada aguardando o envio para aprovação
    E a produtora recebe o endereço "<endereço>" seguido de uma versão de cinco letras ou números

    Exemplos:
      | nome  | endereço |
      | WWW   | www      |
      | Api   | api      |
      | Admin | admin    |
      | App   | app      |
      | Hub   | hub      |

  Esquema do Cenário: Não é possível cadastrar uma produtora com dados inválidos
    Quando cadastro a produtora "<nome>" com o e-mail "<email>" e o administrador "<administrador>"
    Então nada é cadastrado
    E sou avisado de que "<mensagem>"

    Exemplos:
      | nome            | email            | administrador  | mensagem                                                             |
      |                 | contato@acao.com | Maria da Silva | O nome da produtora não pode ser vazio.                              |
      | !!!             | contato@acao.com | Maria da Silva | Não foi possível gerar o slug: informe ao menos uma letra ou número. |
      | Ação Eventos    |                  | Maria da Silva | O email não pode ser vazio.                                          |
      | Ação Eventos    | contato          | Maria da Silva | O email informado não possui um formato válido.                      |
      | Ação Eventos    | contato@acao.com |                | O nome completo não pode ser vazio.                                  |

  Esquema do Cenário: Não é possível cadastrar uma produtora com um telefone inválido
    Quando cadastro a produtora com o telefone "<telefone>"
    Então nada é cadastrado
    E sou avisado de que "<mensagem>"

    Exemplos:
      | telefone   | mensagem                                                                                                         |
      |            | O telefone não pode ser vazio.                                                                                   |
      | 98765-4321 | O telefone deve conter o DDD e o número, como (11) 98765-4321, ou estar no formato internacional, como +5511987654321. |

  Esquema do Cenário: A senha do administrador segue a mesma política do cadastro de cliente
    Quando cadastro a produtora com a senha de administrador "<senha>"
    Então nada é cadastrado
    E sou avisado de que "<mensagem>"

    Exemplos:
      | senha     | mensagem                                 |
      | Senha@1   | A senha deve ter ao menos 8 caracteres.  |
      | senha@123 | A senha deve conter uma letra maiúscula. |
      | Senha1234 | A senha deve conter um símbolo.          |

  Cenário: A senha do administrador não fica guardada como foi escrita
    Quando cadastro a produtora com a senha de administrador "Senha@123"
    Então a produtora é cadastrada aguardando o envio para aprovação
    E a senha do administrador não fica guardada como foi escrita

  Cenário: O cadastro é público e não exige usuário executor
    Quando a produtora é cadastrada por um visitante sem usuário identificado
    Então a produtora é cadastrada aguardando o envio para aprovação
