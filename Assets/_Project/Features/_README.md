# Como fazer o código dos projetos.

Para termos códigos organizados, é melhor seguir um estilo de arquitetura chamado de Vertical Slice.

Sendo assim, cada feature seria um sistema, isolado.

     Features/
     ├── Authentication/
     ├── Questions/
     ├── Inventory/
     ├── Shop/
     └── Tutorial/

Esses sistemas vão se **comunicar** entre si.
Uma recomendação disso é utilizando EventBus que ira falar com os outros sistemas.

## Vamos imaginar aqui um sistema de inventário

     Features/
     └── Inventory/
         ├── InventoryEventBus.cs -> Recebe a comunicação dos outros e vai ter os eventos principais
         ├── InventoryController.cs -> Faz os comandos e vai ter as principais variáveis
         ├── InventoryModel.cs -> Se o inventario tivesse uma entidade por exemplo
         ├── InventoryView.cs -> Controla a UI do inventory Window
         ├── InventoryConfig.asset
         └── InventoryWindow.prefab -> Temos tambem os seus prefabs aqui

(UI e artes são nas suas pastas Content correspondentes)