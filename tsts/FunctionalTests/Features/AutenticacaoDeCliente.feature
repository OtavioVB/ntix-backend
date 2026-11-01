# language: pt
Funcionalidade: Autenticação de cliente
  Para acessar a loja de um produtor
  Como cliente
  Quero entrar com meu email ou CPF e minha senha

  Contexto:
    Dado que sou um cliente cadastrado na loja do produtor

  Cenário: Cliente entra com email e senha
    Quando entro na loja com meu email e minha senha
    Então sou autenticado
    E meu acesso identifica a loja do produtor
    E meu acesso dura 4 horas

  Cenário: Cliente entra com CPF e senha
    Quando entro na loja com meu CPF e minha senha
    Então sou autenticado

  Cenário: Cliente não entra com a senha errada
    Quando entro na loja com meu email e uma senha errada
    Então não sou autenticado por dados não reconhecidos

  Cenário: Pessoa sem cadastro não entra
    Quando entro na loja com um email sem cadastro
    Então não sou autenticado por dados não reconhecidos

  Cenário: Cliente não entra na loja de um produtor sem vínculo
    Quando entro na loja de outro produtor com meu email e minha senha
    Então não sou autenticado por dados não reconhecidos

  Cenário: Não é possível informar email e CPF ao mesmo tempo
    Quando entro na loja informando meu email e meu CPF
    Então sou avisado de que "Informe apenas o email ou apenas o CPF."

  Cenário: É preciso informar o email ou o CPF
    Quando entro na loja informando apenas minha senha
    Então sou avisado de que "Informe o email ou o CPF."
