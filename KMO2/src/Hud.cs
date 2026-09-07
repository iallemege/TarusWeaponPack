using UnityEngine;
using UnityEngine.UI;

namespace KMO2
{
    internal static class Hud
    {
        private static Canvas _canvas;
        private static Text _panel;
        private static Text _pip;
        private static RectTransform _pipRt;
        private static int _tickFrame = -1;
        private static int _toggleFrame = -1;
        private static bool _loggedOnce;
        private static bool _loggedGuns;
        private static Aircraft _ac;
        private static bool _has;
        private static GUIStyle _flashStyle;
        private static string _flash;
        private static float _flashUntil;

        internal static void Tick()
        {
            if (Time.frameCount == _tickFrame)
                return;
            _tickFrame = Time.frameCount;
            _ac = ResolveAircraft();
            PollKeys(false);
            if (_ac == null)
            {
                _has = false;
                SetPanelVisible(false);
                SetPipVisible(false);
                return;
            }
            bool loaded = Munition.AircraftHasOurs(_ac);
            bool selected = loaded && Munition.CurrentStationIsOurs(_ac);
            _has = selected;
            if (loaded)
                Munition.StampCurrent(_ac);
            if (!_loggedOnce)
            {
                _loggedOnce = true;
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("HUD tick ac=" + _ac.name
                        + " loaded=" + loaded
                        + " selected=" + selected
                        + " mode=" + Munition.ModeLabel());
            }
            if (loaded)
            {
                PowerSupply psTick = Munition.PowerOf(_ac);
                Munition.EnsureCapacitor(_ac, psTick);
                Munition.TickBattery(_ac, psTick, Time.unscaledDeltaTime);
            }
            if (!selected)
            {
                if (!loaded && !_loggedGuns)
                    LogGuns(_ac);
                SetPanelVisible(false);
                SetPipVisible(false);
                return;
            }
            PowerSupply ps = Munition.PowerOf(_ac);
            EnsureUi();
            UpdatePanel(_ac, ps);
            SetPipVisible(false);
        }

        internal static void OnCombatHud(CombatHUD hud)
        {
            Tick();
        }

        internal static void DrawImgui()
        {
            PollKeys(true);
            DrawFlash();
        }

        private static Aircraft ResolveAircraft()
        {
            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null && hud.aircraft != null)
                    return hud.aircraft;
            }
            catch
            {
            }
            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                    return ac;
            }
            catch
            {
            }
            return null;
        }

        private static void PollKeys(bool fromGui)
        {
            bool hit = false;
            if (fromGui)
            {
                Event e = Event.current;
                if (e == null || e.type != EventType.KeyDown || e.keyCode == KeyCode.None)
                    return;
                hit = IsToggleKey(e.keyCode);
            }
            else
            {
                if (Plugin.ChargeToggleKey != null && Plugin.ChargeToggleKey.Value != KeyCode.None)
                    hit = Input.GetKeyDown(Plugin.ChargeToggleKey.Value);
            }
            if (!hit)
                return;
            if (Time.frameCount == _toggleFrame)
                return;
            if (_ac == null || !Munition.CurrentStationIsOurs(_ac))
                return;
            _toggleFrame = Time.frameCount;
            Munition.CycleMode();
            string msg = Munition.DisplayName + " " + Munition.ModeLabel();
            _flash = msg;
            _flashUntil = Time.unscaledTime + 2.4f;
            try
            {
                if (SceneSingleton<AircraftActionsReport>.i != null)
                    SceneSingleton<AircraftActionsReport>.i.ReportText(msg, 2.5f);
            }
            catch
            {
            }
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("KMO-2 mode=" + Munition.ModeLabel());
            Munition.StampCurrent(_ac);
        }

        private static bool IsToggleKey(KeyCode k)
        {
            if (Plugin.ChargeToggleKey == null || Plugin.ChargeToggleKey.Value == KeyCode.None)
                return false;
            return k == Plugin.ChargeToggleKey.Value;
        }

        private static void LogGuns(Aircraft ac)
        {
            _loggedGuns = true;
            if (Plugin.Log == null || ac == null)
                return;
            Gun[] guns = ac.GetComponentsInChildren<Gun>(true);
            int n = guns != null ? guns.Length : 0;
            Plugin.Log.LogInfo("no KMO-2 on ac");
            if (guns == null)
                return;
            int max = n < 12 ? n : 12;
            for (int i = 0; i < max; i++)
            {
                if (guns[i] == null || guns[i].info == null)
                    continue;
                Plugin.Log.LogInfo(" gun " + guns[i].info.weaponName
                    + " / " + guns[i].info.shortName);
            }
        }

        private static void UpdatePanel(Aircraft ac, PowerSupply ps)
        {
            if (_panel == null)
                return;
            _panel.gameObject.SetActive(true);
            float next = Munition.PeekShotCost(ac, ps);
            float have = Munition.ReadKJ(ac, ps);
            float vel = Munition.PeekMuzzle(ac, ps);
            string mode = Munition.ModeLabel();
            string last = Munition.LastShotKJ > 0f ? FormatKj(Munition.LastShotKJ) : "--";
            _panel.color = Munition.ModeColor();
            string shotExtra = "";
            if (Munition.ChargeGuidedMode)
                shotExtra = "  ALL";
            else
                shotExtra = "  OPT";
            string range = Munition.PeekRangeKm().ToString("F0") + " km";
            _panel.text = Munition.DisplayName + "  " + mode
                + "\nLAUNCH  " + FormatKj(next) + shotExtra
                + "\nSPD     " + vel.ToString("F0") + " m/s"
                + "\nRANGE " + range
                + "\nLAST  " + last
                + "\nBATT  " + FormatKj(have)
                + "\n" + Plugin.ToggleHint() + "  mode";
        }

        private static void UpdatePip()
        {
            SetPipVisible(false);
        }

        private static Font _hudFont;

        private static void EnsureUi()
        {
            Font font = PanelFont();
            if (_canvas != null && _panel != null)
            {
                if (_canvas.gameObject.name != "HUD_KMO2")
                {
                    UnityEngine.Object.Destroy(_canvas.gameObject);
                    _canvas = null;
                    _panel = null;
                    _pip = null;
                    _pipRt = null;
                }
                else
                {
                    if (font != null)
                    {
                        _panel.font = font;
                        if (_pip != null)
                            _pip.font = font;
                    }
                    return;
                }
            }
            GameObject root = new GameObject("HUD_KMO2");
            UnityEngine.Object.DontDestroyOnLoad(root);
            _canvas = root.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 8000;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            root.AddComponent<GraphicRaycaster>();

            GameObject panelGo = new GameObject("Panel");
            panelGo.transform.SetParent(root.transform, false);
            _panel = panelGo.AddComponent<Text>();
            _panel.font = font;
            _panel.fontSize = 16;
            _panel.alignment = TextAnchor.UpperLeft;
            _panel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _panel.verticalOverflow = VerticalWrapMode.Overflow;
            _panel.raycastTarget = false;
            RectTransform prt = _panel.rectTransform;
            prt.anchorMin = new Vector2(0f, 1f);
            prt.anchorMax = new Vector2(0f, 1f);
            prt.pivot = new Vector2(0f, 1f);
            prt.anchoredPosition = new Vector2(18f, -18f);
            prt.sizeDelta = new Vector2(380f, 168f);

            Outline outline = panelGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1.2f, -1.2f);

            GameObject pipGo = new GameObject("Pip");
            pipGo.transform.SetParent(root.transform, false);
            _pip = pipGo.AddComponent<Text>();
            _pip.font = font;
            _pip.fontSize = 28;
            _pip.alignment = TextAnchor.MiddleCenter;
            _pip.raycastTarget = false;
            _pip.text = "+";
            _pipRt = _pip.rectTransform;
            _pipRt.sizeDelta = new Vector2(40f, 40f);
            _pipRt.anchorMin = new Vector2(0f, 0f);
            _pipRt.anchorMax = new Vector2(0f, 0f);
            _pipRt.pivot = new Vector2(0.5f, 0.5f);
            SetPipVisible(false);
        }

        private static Font PanelFont()
        {
            if (_hudFont != null)
                return _hudFont;
            Font stolen = StealVanillaHudFont();
            if (stolen != null)
            {
                _hudFont = stolen;
                return stolen;
            }
            return BuiltinFont();
        }

        private static Font StealVanillaHudFont()
        {
            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null)
                {
                    Font f = BestFromTexts(hud.GetComponentsInChildren<Text>(true));
                    if (f != null)
                        return f;
                }
            }
            catch { }
            try
            {
                FlightHud fh = UnityEngine.Object.FindObjectOfType<FlightHud>();
                if (fh != null)
                {
                    Font f = BestFromTexts(fh.GetComponentsInChildren<Text>(true));
                    if (f != null)
                        return f;
                }
            }
            catch { }
            Font[] all = Resources.FindObjectsOfTypeAll<Font>();
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                Font f = all[i];
                if (f == null || IsCjkFont(f) || string.IsNullOrEmpty(f.name))
                    continue;
                string n = f.name.ToLowerInvariant();
                if (n.IndexOf("hud") >= 0 || n.IndexOf("digital") >= 0)
                    return f;
            }
            return null;
        }

        private static Font BestFromTexts(Text[] ts)
        {
            if (ts == null)
                return null;
            Font fallback = null;
            for (int i = 0; i < ts.Length; i++)
            {
                Text t = ts[i];
                if (t == null || t.font == null)
                    continue;
                Font f = t.font;
                if (IsCjkFont(f))
                    continue;
                string n = f.name != null ? f.name.ToLowerInvariant() : "";
                if (n.IndexOf("hud") >= 0 || n.IndexOf("digital") >= 0)
                    return f;
                if (!f.dynamic)
                    return f;
                if (fallback == null)
                    fallback = f;
            }
            return fallback;
        }

        private static bool IsCjkFont(Font f)
        {
            if (f == null || string.IsNullOrEmpty(f.name))
                return false;
            string n = f.name.ToLowerInvariant();
            return n.IndexOf("noto") >= 0
                || n.IndexOf("cjk") >= 0
                || n.IndexOf("msyh") >= 0
                || n.IndexOf("yahei") >= 0
                || n.IndexOf("sourcehan") >= 0
                || n.IndexOf("source han") >= 0
                || n.IndexOf("oritasy") >= 0
                || n.IndexOf("simhei") >= 0
                || n.IndexOf("simsun") >= 0
                || n.IndexOf("wenquan") >= 0
                || n.IndexOf("harmonyos") >= 0
                || n.IndexOf("droid sans fallback") >= 0;
        }

        private static Font BuiltinFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        private static void EnsureImgui()
        {
            if (_flashStyle != null)
                return;
            _flashStyle = new GUIStyle();
            Font hud = PanelFont();
            if (hud != null)
                _flashStyle.font = hud;
            _flashStyle.fontSize = 22;
            _flashStyle.fontStyle = FontStyle.Bold;
            _flashStyle.alignment = TextAnchor.UpperCenter;
            _flashStyle.normal.textColor = new Color(1f, 0.9f, 0.3f, 1f);
        }

        private static void DrawFlash()
        {
            if (string.IsNullOrEmpty(_flash) || Time.unscaledTime >= _flashUntil)
                return;
            EnsureImgui();
            GUI.Label(new Rect((float)Screen.width * 0.5f - 280f, 72f, 560f, 36f),
                _flash, _flashStyle);
        }

        private static void SetPanelVisible(bool on)
        {
            if (_panel != null)
                _panel.gameObject.SetActive(on);
        }

        private static void SetPipVisible(bool on)
        {
            if (_pip != null)
                _pip.gameObject.SetActive(on);
        }

        internal static string FormatKj(float kj)
        {
            if (kj >= 1000f)
                return (kj / 1000f).ToString("F2") + " MJ";
            if (kj >= 100f)
                return kj.ToString("F0") + " kJ";
            return kj.ToString("F1") + " kJ";
        }
    }
}
