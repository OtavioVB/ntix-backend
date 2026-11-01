# language: pt
Funcionalidade: Cadastro de produtora
  Para começar a usar a plataforma da Ntix
  Como visitante
  Quero cadastrar minha produtora informando os dados básicos e o usuário administrador

  Cenário: Produtora se cadastra com dados válidos
    Quando cadastro a produtora "Ação Eventos & Shows" com o email "contato@acao.com"
    Então a produtora é criada aguardando o envio do cadastro para aprovação
    E o usuário administrador é criado aguardando a verificação do email
    E a produtora recebe o endereço "acao-eventos-shows"
    E um link de verificação é enviado para "contato@acao.com"
    E recebo o aviso "A produtora foi cadastrada com sucesso. Verifique o email para acessar a plataforma."

  Cenário: Não é possível cadastrar uma produtora com um email já cadastrado
    Dado que já existe uma produtora "Ação Eventos" com o email "contato@acao.com"
    Quando cadastro a produtora "Outra Produtora" com o email "CONTATO@ACAO.COM"
    Então a produtora não é criada
    E recebo o aviso "Já existe uma produtora ou um usuário com o email informado."

  Cenário: Duas produtoras com o mesmo nome recebem endereços diferentes
    Dado que já existe uma produtora "Ação Eventos" com o email "contato@acao.com"
    Quando cadastro a produtora "Ação Eventos" com o email "outra@acao.com"
    Então a produtora é criada aguardando o envio do cadastro para aprovação
    E a produtora recebe o endereço "acao-eventos" seguido de uma versão de cinco letras ou números

  Esquema do Cenário: Um nome igual a uma palavra reservada recebe uma versão no endereço
    Quando cadastro a produtora "<nome>" com o email "contato@acao.com"
    Então a produtora é criada aguardando o envio do cadastro para aprovação
    E a produtora recebe o endereço "<endereço>" seguido de uma versão de cinco letras ou números

    Exemplos:
      | nome  | endereço |
      | Admin | admin    |
      | WWW   | www      |
      | App   | app      |

  Cenário: O telefone é guardado no formato internacional
    Quando cadastro uma produtora com o telefone "+55 (11) 99999-9999"
    Então a produtora é criada aguardando o envio do cadastro para aprovação
    E o telefone "+5511999999999" fica registrado

  Esquema do Cenário: Não é possível cadastrar uma produtora com dados inválidos
    Quando cadastro a produtora "<nome>" com o email "<email>"
    Então a produtora não é criada
    E recebo o aviso "<mensagem>"

    Exemplos:
      | nome         | email            | mensagem                                                             |
      |              | contato@acao.com | O nome da produtora não pode ser vazio.                              |
      | !!!          | contato@acao.com | Não foi possível gerar o slug: informe ao menos uma letra ou número. |
      | Ação Eventos |                  | O email não pode ser vazio.                                          |
      | Ação Eventos | contato          | O email informado não possui um formato válido.                      |

  Esquema do Cenário: Não é possível cadastrar uma produtora com um telefone inválido
    Quando cadastro uma produtora com o telefone "<telefone>"
    Então a produtora não é criada
    E recebo o aviso "<mensagem>"

    Exemplos:
      | telefone    | mensagem                                                                        |
      |             | O telefone não pode ser vazio.                                                  |
      | 11999999999 | O telefone deve conter o código do país, o DDD e o número, como +5511999999999. |

  Cenário: A produtora pode informar o documento no cadastro inicial
    Quando cadastro uma produtora com o documento "11.222.333/0001-81"
    Então a produtora é criada aguardando o envio do cadastro para aprovação
    E o documento "11222333000181" fica registrado

  Cenário: Não é possível cadastrar uma produtora com um documento inválido
    Quando cadastro uma produtora com o documento "123"
    Então a produtora não é criada
    E recebo o aviso "O documento informado não é um CPF ou CNPJ válido."

  Cenário: Não é possível cadastrar uma produtora com um documento já cadastrado
    Dado que já existe uma produtora com o documento "11222333000181"
    Quando cadastro uma produtora com o documento "11.222.333/0001-81"
    Então a produtora não é criada
    E recebo o aviso "Já existe uma produtora com o documento informado."

  Cenário: A senha do administrador segue a política de senha
    Quando cadastro uma produtora com a senha do administrador "senha"
    Então a produtora não é criada
    E recebo o aviso "A senha deve ter ao menos 8 caracteres."

  Cenário: Uma falha no envio do email não desfaz o cadastro
    Dado que o envio de emails para "contato@acao.com" está indisponível
    Quando cadastro a produtora "Ação Eventos" com o email "contato@acao.com"
    Então a produtora é criada aguardando o envio do cadastro para aprovação
    E nenhum link de verificação é enviado

  Cenário: O cadastro público é aceito sem usuário executor
    Quando cadastro uma produtora sem informar o usuário executor
    Então a produtora é criada aguardando o envio do cadastro para aprovação
