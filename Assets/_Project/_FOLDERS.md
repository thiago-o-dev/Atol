# Organização das pastas
# **Features**
## O que fica aqui?

Aqui temos o código separado em vertical slices do nosso jogo, funmcionalidades reais para serem utilizadas em cenas e games.

# **Framework**
## O que fica aqui?

Aqui temos somente código **SEM REFERENCIAS EXTERNAS INSEGURAS**, que pode ser usado por qualquer sistema, sem problemas.

É separado em um asmdef devido isso, para dificultar o uso indevido de referencias externas.

# **Scenes**
## O que fica aqui?

Aqui não temos códigos e nem prefabs.

Uma Scene aqui se trata de uma parte integral da progressão do jogo, como uma cena **bootstrap**, **menu**, **Salacia**, ou os **Arquipélagos**.

# **Games**
## O que fica aqui?

Aqui são pequenas areas teste.

Entenda primeiro o que é um **playground**, **museum** e um **zoo** na prototipagem de jogos:
### Playground: 
Area para testar loops com testes reais, melhor do que guardar em documentos coisas como
- Quão longe o personagem pula?
- Qual a velocidade máxima em tantos metros?
- Qual o tamanho do pulo?
- Quão rápido ele nada?
- Qual o dano base?

O playground é de longe um dos mais importantes.

### Museum: 
Area para testar items, visualizar modelos, construções pré-montadas.

### Zoo: 
Area para conhecer, lutar e olhar os animais do jogo.

#### Esses não são os únicos
São os tipos mais usados de mini-games no desenvolvimento. Mas tambem temos que testar loops como construção, navegação, batalha naval, etc... 

Logo, teremos **vários** Playgrounds, Zoos e Museums.