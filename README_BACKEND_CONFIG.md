Launcher recuperado y preparado para backend configurable.

Archivo esperado en AppData:
- `C:\Users\facun\AppData\Roaming\JurassicCraft\backend.json`

Variables de entorno soportadas:
- `JC_GITHUB_OWNER`
- `JC_MODPACK_REPO`
- `JC_LAUNCHER_REPO`
- `JC_GITHUB_TOKEN`
- `JC_MINECRAFT_VERSION`
- `JC_FORGE_VERSION`

Ejemplo:

```json
{
  "GitHubOwner": "facudiazz",
  "ModpackRepoName": "JurassicCraft-Modpack",
  "LauncherRepoName": "JurassicCraft-Launcher",
  "GitHubBranch": "main",
  "GitHubToken": "",
  "DefaultMinecraftVersion": "1.20.1",
  "DefaultForgeVersion": "47.4.10"
}
```

Notas:
- El token viejo fue removido del código.
- Si el repositorio del modpack es público, podés dejar `GitHubToken` vacío.
- Si el modpack es privado, usá un token tuyo con permisos mínimos.
