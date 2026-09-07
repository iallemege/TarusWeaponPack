using System;
using System.IO;
using UnityEngine;

namespace RR106
{
    internal static class Icon
    {
        private const string FileName = "106mmRR_icon.png";
        private const int FallbackWidth = 1024;
        private const int FallbackHeight = 256;
        private const float FallbackPpu = 100f;
        private static Sprite _sprite;
        private static bool _tried;

        internal static void Apply(WeaponInfo info)
        {
            if (info == null)
                return;
            Sprite donor = null;
            try
            {
                donor = info.weaponIcon;
            }
            catch
            {
            }
            Sprite icon = Get(donor);
            if (icon == null)
                return;
            try
            {
                info.weaponIcon = icon;
            }
            catch
            {
            }
        }

        internal static Sprite Get(Sprite donor)
        {
            if (_sprite != null)
                return _sprite;
            if (_tried)
                return null;
            _tried = true;
            string path = Visual.ResolveAssetPath(FileName);
            if (string.IsNullOrEmpty(path))
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("106mm RR icon PNG missing");
                return null;
            }
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.Destroy(tex);
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("106mm RR icon decode failed");
                    return null;
                }
                float ppu = FallbackPpu;
                ReadDonorPpu(donor, ref ppu);
                Texture2D fitted = FitToCanvas(tex, FallbackWidth, FallbackHeight);
                if (fitted != tex)
                    UnityEngine.Object.Destroy(tex);
                fitted.name = "106mmRR_IconTex";
                fitted.wrapMode = TextureWrapMode.Clamp;
                fitted.filterMode = FilterMode.Point;
                _sprite = Sprite.Create(
                    fitted,
                    new Rect(0f, 0f, fitted.width, fitted.height),
                    new Vector2(0.5f, 0.5f),
                    ppu);
                _sprite.name = "106mmRR_Icon";
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("106mm RR icon " + fitted.width + "x" + fitted.height);
                return _sprite;
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("106mm RR icon: " + ex.Message);
                return null;
            }
        }

        private static void ReadDonorPpu(Sprite donor, ref float ppu)
        {
            if (donor == null)
                return;
            try
            {
                if (donor.pixelsPerUnit > 1f)
                    ppu = donor.pixelsPerUnit;
            }
            catch
            {
            }
        }

        private static Texture2D FitToCanvas(Texture2D src, int dw, int dh)
        {
            if (src == null)
                return src;
            if (dw < 8)
                dw = FallbackWidth;
            if (dh < 8)
                dh = FallbackHeight;
            if (src.width == dw && src.height == dh)
                return src;

            Texture2D dst = new Texture2D(dw, dh, TextureFormat.RGBA32, false);
            Color[] fill = new Color[dw * dh];
            Color black = new Color(0f, 0f, 0f, 1f);
            for (int i = 0; i < fill.Length; i++)
                fill[i] = black;

            float sx = (float)dw / (float)src.width;
            float sy = (float)dh / (float)src.height;
            float scale = sx < sy ? sx : sy;
            int nw = Mathf.Max(1, Mathf.RoundToInt(src.width * scale));
            int nh = Mathf.Max(1, Mathf.RoundToInt(src.height * scale));
            int ox = (dw - nw) / 2;
            int oy = (dh - nh) / 2;
            Color[] srcPx = src.GetPixels();
            int sw = src.width;
            int sh = src.height;
            for (int y = 0; y < nh; y++)
            {
                int syi = (int)(((y + 0.5f) * sh) / nh);
                if (syi < 0)
                    syi = 0;
                if (syi >= sh)
                    syi = sh - 1;
                int srcRow = syi * sw;
                int dstRow = (oy + y) * dw + ox;
                for (int x = 0; x < nw; x++)
                {
                    int sxi = (int)(((x + 0.5f) * sw) / nw);
                    if (sxi < 0)
                        sxi = 0;
                    if (sxi >= sw)
                        sxi = sw - 1;
                    fill[dstRow + x] = srcPx[srcRow + sxi];
                }
            }
            dst.SetPixels(fill);
            dst.Apply(false, false);
            return dst;
        }
    }
}
