# Tarus Weapon Pack

BepInEx plugin pack for [Nuclear Option](https://store.steampowered.com/app/2167580/Nuclear_Option/): **57mm Railgun**, **106mm Recoilless Rifle**, and **KMO-2**. Manufacturer: Tarus Electronautic.

独立武器包。装这一个 DLL 即可；同目录里的 `RailcannonPod.dll` / `RR106.dll` / `KMO2.dll` 会自动跳过。

## Install

1. [BepInEx 5](https://github.com/BepInEx/BepInEx) in the Nuclear Option game folder.
2. Download **TarusWeaponPack-1.1.8.zip** from [Releases](https://github.com/iallemege/TarusWeaponPack/releases).
3. Extract into the game folder so these land in `BepInEx/plugins/`:
   - `TarusWeaponPack.dll`
   - `TarusWeaponPackAssets\` (meshes + hangar icons)
4. Fully quit Steam, then launch.

Plugin GUID: `com.ial.tarusweaponpack`.

## Weapons

| Hangar name | Key | Notes |
|-------------|-----|--------|
| **57mm Railgun** | `gun_155mm_pod_P` | Energy railgun. **K** cycles NORMAL / CHARGE / GUIDED on the selected gun. Outer/all pylons (not naval cells). |
| **106mm Recoilless Rifle** | `gun_106mm_rr` | Recoilless. **K** cycles NORMAL / CHARGE / GUIDED / RECON / CHG+GUIDE. |
| **KMO-2** | `KMO2_single` | Forced onto every aircraft **outer** pylon. Neighbor pylon stays empty. **K** cycles GUIDED / CHG+GUIDE. 5 kt shockwave-only warhead. |

Same **K** key as each other: only the **selected** station cycles.

## Build from source

Windows, `csc` from .NET Framework 4.x. Edit `GAME=` in `build.bat` if the Steam folder is not `d:\Steam\steamapps\common\Nuclear Option`.

```bat
cd TarusWeaponPack
build.bat
```

Requires the game `Managed` assemblies and `BepInEx\core`. Output installs into `BepInEx\plugins`.

## Credits

- Weapons / plugin: IAL / [iallemege](https://github.com/iallemege)
- Game: [Shockfront / Nuclear Option](https://store.steampowered.com/app/2167580/Nuclear_Option/)
