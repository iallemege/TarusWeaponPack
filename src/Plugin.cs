using BepInEx;
using BepInEx.Logging;

namespace TarusWeaponPack
{
    internal static class PackInfo
    {
        public const string GUID = "com.ial.tarusweaponpack";
        public const string Name = "TarusWeaponPack";
        public const string Version = "1.1.8";
    }

#if !TARUS_PACK
#error TarusWeaponPack must be compiled with /define:TARUS_PACK
#endif

    [BepInPlugin(PackInfo.GUID, PackInfo.Name, PackInfo.Version)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            RailcannonPod.Plugin.Boot(Logger, Config, "57mm");
            RR106.Plugin.Boot(Logger, Config, "106mm");
            KMO2.Plugin.Boot(Logger, Config, "KMO2");
            Log.LogInfo(PackInfo.Name + " v" + PackInfo.Version
                + " 57mm + 106mm + KMO-2 (standalone plugins skip while this pack is loaded)");
        }

        private void Update()
        {
            RailcannonPod.Plugin.TickFrame();
            RR106.Plugin.TickFrame();
            KMO2.Plugin.TickFrame();
        }

        private void OnGUI()
        {
            RailcannonPod.Plugin.DrawFrame();
            RR106.Plugin.DrawFrame();
            KMO2.Plugin.DrawFrame();
        }
    }
}
