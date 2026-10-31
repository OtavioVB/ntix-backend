# language: pt
Funcionalidade: Cadastro de cliente
  Para usar a plataforma da Ntix
  Como cliente
  Quero me cadastrar informando meu nome completo, email, CPF, senha e, se quiser, meu celular

  Cenário: Cliente se cadastra com dados válidos
    Quando me cadastro com o nome "Maria da Silva", o email "maria@ntix.com" e o documento "529.982.247-25"
    Então meu cadastro é criado
    E sou informado de que "O cliente foi cadastrado com sucesso."
    E recebo o slug "maria-da-silva"

  Cenário: Não é possível se cadastrar com um email já cadastrado
    Dado que já existe um cliente com o nome "Maria da Silva", o email "maria@ntix.com" e o documento "52998224725"
    Quando me cadastro com o nome "Joana Souza", o email "maria@ntix.com" e o documento "11144477735"
    Então meu cadastro não é criado
    E sou informado de que "Já existe um cliente com o email ou o CPF informado."

  Cenário: Não é possível se cadastrar com um documento já cadastrado
    Dado que já existe um cliente com o nome "Maria da Silva", o email "maria@ntix.com" e o documento "52998224725"
    Quando me cadastro com o nome "Joana Souza", o email "joana@ntix.com" e o documento "52998224725"
    Então meu cadastro não é criado
    E sou informado de que "Já existe um cliente com o email ou o CPF informado."

  Cenário: O mesmo email escrito com letras maiúsculas continua sendo o mesmo cliente
    Dado que já existe um cliente com o nome "Maria da Silva", o email "maria@ntix.com" e o documento "52998224725"
    Quando me cadastro com o nome "Joana Souza", o email "MARIA@NTIX.COM" e o documento "11144477735"
    Então meu cadastro não é criado
    E sou informado de que "Já existe um cliente com o email ou o CPF informado."

  Cenário: O mesmo documento escrito com pontuação continua sendo o mesmo cliente
    Dado que já existe um cliente com o nome "Maria da Silva", o email "maria@ntix.com" e o documento "52998224725"
    Quando me cadastro com o nome "Joana Souza", o email "joana@ntix.com" e o documento "529.982.247-25"
    Então meu cadastro não é criado
    E sou informado de que "Já existe um cliente com o email ou o CPF informado."

  Cenário: Dois clientes com o mesmo nome recebem slugs diferentes
    Dado que já existe um cliente com o nome "Maria da Silva", o email "maria@ntix.com" e o documento "52998224725"
    Quando me cadastro com o nome "Maria da Silva", o email "outra.maria@ntix.com" e o documento "11144477735"
    Então meu cadastro é criado
    E recebo o slug "maria-da-silva" seguido de uma versão de cinco letras ou números

  Cenário: Cada novo cliente com o mesmo nome também recebe uma versão do slug
    Dado que já existe um cliente com o nome "Maria da Silva", o email "maria@ntix.com" e o documento "52998224725"
    E que já existe um cliente com o nome "Maria da Silva", o email "maria2@ntix.com" e o documento "11144477735"
    Quando me cadastro com o nome "Maria da Silva", o email "maria3@ntix.com" e o documento "12345678909"
    Então meu cadastro é criado
    E recebo o slug "maria-da-silva" seguido de uma versão de cinco letras ou números

  Esquema do Cenário: O slug contém apenas letras, números e traços
    Quando me cadastro com o nome "<nome>", o email "cliente@ntix.com" e o documento "52998224725"
    Então meu cadastro é criado
    E recebo o slug "<slug>"

    Exemplos:
      | nome              | slug              |
      | João da Conceição | joao-da-conceicao |
      | Ana-Maria O'Neil  | ana-maria-oneil   |
      | José  Souza  3º   | jose-souza-3      |
      | CARLOS (Júnior)   | carlos-junior     |

  Esquema do Cenário: Não é possível se cadastrar com dados inválidos
    Quando me cadastro com o nome "<nome>", o email "<email>" e o documento "<documento>"
    Então meu cadastro não é criado
    E sou informado de que "<mensagem>"

    Exemplos:
      | nome           | email          | documento      | mensagem                                                             |
      |                | maria@ntix.com | 52998224725    | O nome completo não pode ser vazio.                                  |
      | !!!            | maria@ntix.com | 52998224725    | Não foi possível gerar o slug: informe ao menos uma letra ou número. |
      | Maria da Silva |                | 52998224725    | O email não pode ser vazio.                                          |
      | Maria da Silva | maria          | 52998224725    | O email informado não possui um formato válido.                      |
      | Maria da Silva | maria@ntix.com |                | O CPF não pode ser vazio.                                            |
      | Maria da Silva | maria@ntix.com | 52998224724    | O CPF informado não é válido.                                        |
      | Maria da Silva | maria@ntix.com | 111.111.111-11 | O CPF informado não é válido.                                        |

  Cenário: A senha informada não fica guardada como foi escrita
    Quando me cadastro com a senha "Senha@123"
    Então meu cadastro é criado
    E minha senha não fica guardada como foi escrita

  Esquema do Cenário: Não é possível se cadastrar com uma senha fraca
    Quando me cadastro com a senha "<senha>"
    Então meu cadastro não é criado
    E sou informado de que "<mensagem>"

    Exemplos:
      | senha     | mensagem                                 |
      |           | A senha não pode ser vazia.              |
      | Senha@1   | A senha deve ter ao menos 8 caracteres.  |
      | senha@123 | A senha deve conter uma letra maiúscula. |
      | SENHA@123 | A senha deve conter uma letra minúscula. |
      | Senha@abc | A senha deve conter um número.           |
      | Senha1234 | A senha deve conter um símbolo.          |

  Cenário: O cliente pode se cadastrar sem informar o celular
    Quando me cadastro sem informar o celular
    Então meu cadastro é criado
    E nenhum celular fica registrado

  Esquema do Cenário: O cliente pode informar o celular com DDD e número
    Quando me cadastro com o celular "<celular>"
    Então meu cadastro é criado
    E o celular "11987654321" fica registrado

    Exemplos:
      | celular         |
      | 11987654321     |
      | (11) 98765-4321 |

  Esquema do Cenário: Não é possível se cadastrar com um celular sem DDD e número
    Quando me cadastro com o celular "<celular>"
    Então meu cadastro não é criado
    E sou informado de que "O celular deve conter o DDD e o número, como (11) 98765-4321."

    Exemplos:
      | celular      |
      | 98765-4321   |
      | 1198765432   |
      | 119876543210 |
      | celular      |

  Esquema do Cenário: Uma solicitação sem as informações de auditoria é recusada
    Quando uma solicitação de cadastro chega sem "<informação>"
    Então meu cadastro não é criado
    E sou informado de que "<mensagem>"

    Exemplos:
      | informação                    | mensagem                                 |
      | o identificador de correlação | O header X-Correlation-Id é obrigatório. |
      | o serviço de origem           | O header X-Origin-Service é obrigatório. |
      | o momento da solicitação      | O header X-Requested-At é obrigatório.   |

  Cenário: Uma solicitação sem usuário executor é aceita
    Quando uma solicitação de cadastro chega sem "o usuário executor"
    Então meu cadastro é criado
