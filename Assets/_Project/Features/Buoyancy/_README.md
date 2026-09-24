# Buoyancy

Faz objetos com Rigidbody boiarem na água calculada pelo WaterController (CPU).

Cada FloatPoint compara sua altura com a altura da onda naquele ponto. Se estiver abaixo, empurra o objeto para cima proporcional à profundidade, aplicando a força no próprio ponto. Isso faz o objeto inclinar e balançar com as ondas.

- WaterSurface: o objeto do plano de água, usamos o Y dele como nível do mar.
- BuoyancyStrength: acima de 1 boia, abaixo de 1 afunda.
- Coloque os FloatPoints nos cantos do casco para um balanço mais realista.
