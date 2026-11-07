# language: pt
Funcionalidade: Identidade visual da produtora (whitelabel)
  Para vender ingressos com a cara da minha marca
  Como produtora de eventos na Ntix
  Quero que a plataforma mostre as cores, fontes e medidas que a Ntix configurou para mim

  Contexto:
    Dado que a Ntix configurou a identidade visual da produtora "palco-sul" com a cor principal "#0044ff" e o nome "Palco Sul"

  Cenário: A plataforma recebe a identidade visual configurada para a produtora
    Quando a plataforma consulta a identidade visual da produtora "palco-sul"
    Então recebe a cor principal "#0044ff" e o nome "Palco Sul"
    E a identidade visual pode ser guardada em cache por 5 minutos

  Cenário: Informação interna colocada no arquivo não é exposta
    Dado que a configuração da produtora "palco-sul" contém a anotação interna "contrato com desconto de 30%"
    Quando a plataforma consulta a identidade visual da produtora "palco-sul"
    Então a anotação interna não aparece para quem consulta

  Cenário: Produtora sem identidade visual configurada
    Quando a plataforma consulta a identidade visual da produtora "sem-configuracao"
    Então é informado que não existe identidade visual para essa produtora

  Cenário: Endereço de produtora que tenta ler outros arquivos do servidor é recusado
    Quando a plataforma consulta a identidade visual da produtora "..%2Fappsettings"
    Então é informado que o endereço da produtora é inválido

  Cenário: Configuração com valor inseguro não é servida
    Dado que a configuração da produtora "palco-sul" tem o logo "javascript:alert(1)"
    Quando a plataforma consulta a identidade visual da produtora "palco-sul"
    Então a identidade visual não é servida e nenhum detalhe do problema é revelado

  Cenário: Configuração acima do tamanho permitido não é servida
    Dado que a configuração da produtora "palco-sul" passou do tamanho permitido
    Quando a plataforma consulta a identidade visual da produtora "palco-sul"
    Então a identidade visual não é servida e nenhum detalhe do problema é revelado
