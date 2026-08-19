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

Entenda primeiro o que é um **gym**, **zoo** e um **museum** na prototipagem de jogos:
### Gym: 
Area para testar loops com testes reais, melhor do que guardar em documentos coisas como
- Quão longe o personagem pula?
- Qual a velocidade máxima em tantos metros?
- Qual o tamanho do pulo?
- Quão rápido ele nada?
- Qual o dano base?

O playground é de longe um dos mais importantes.

### Zoo: 
Coletâneas são feitas com zoos.
- 3D assets, itens, npcs
- VFX, audio, materiais
- Vinhetas e art directors
Você consegue realmente ver o tamanho dos objetos comparados um com os outros, com luz e mais.

### Museum: 
- Para tecnologias e sistemas
- shaders e renderização
- fisica e prefabs
Diferente de um zoo, vemos os assets com explicações e dados, similar a um gym.
Area para visualizar itens em diferentes versões, modelos antigos, construções em várias versões.

#### Esses não são os únicos
São os tipos mais usados de mini-games no desenvolvimento. Mas tambem temos que testar loops como construção, navegação, batalha naval, etc... 

Logo, teremos **vários** Gym, Zoos e Museums.

Aprenda mais a fundo em: https://www.youtube.com/watch?v=5PJRCz0t7yY