# Architecture
## Aqui temos tipos de entidade essenciais

Antes de criar um singleton, pense, isso **necessita** estar exposto?

Algo como um DataContainer não deveria estar exposto, logo ele não é um singleton, mas sim, um STATIC.
Um static é algo que só existe 1 na memória, e é acessado pelo nome do tipo.