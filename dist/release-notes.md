Tarus Weapon Pack 1.1.8 for Nuclear Option (BepInEx).

## Install

Extract into the Nuclear Option game folder so these land in `BepInEx/plugins/`:

- `TarusWeaponPack.dll`
- `TarusWeaponPackAssets/` (meshes + hangar icons)

Requires [BepInEx 5](https://github.com/BepInEx/BepInEx). Fully quit Steam, then launch.

If `RailcannonPod.dll`, `RR106.dll`, or `KMO2.dll` are also present, they skip while this pack is loaded.

## Weapons

- **57mm Railgun** (`gun_155mm_pod_P`) — energy railgun. **K** cycles NORMAL / CHARGE / GUIDED. Outer/all pylons (not naval cells).
- **106mm Recoilless Rifle** (`gun_106mm_rr`) — **K** cycles NORMAL / CHARGE / GUIDED / RECON / CHG+GUIDE.
- **KMO-2** (`KMO2_single`) — forced onto every aircraft outer pylon (neighbor stays empty). **K** cycles GUIDED / CHG+GUIDE. 5 kt shockwave-only warhead.

Same **K** key: only the selected station cycles.

Plugin GUID: `com.ial.tarusweaponpack`.
