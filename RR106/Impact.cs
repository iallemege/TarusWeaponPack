using HarmonyLib;
using System.Reflection;
using UnityEngine;

namespace RR106
{
    internal static class Impact
    {
        private const int MaxSteps = 1800;
        private const float StepDt = 0.04f;
        private const int TerrainMask = -8193;
        private const float MinForwardDot = 0.08f;
        private static readonly FieldInfo MuzzlesField =
            AccessTools.Field(typeof(Gun), "muzzles");

        private static bool _has;
        private static Vector3 _world;
        private static Vector3 _smooth;
        private static float _tof;
        private static Vector3 _muzzleWorld;
        private static float _nextCompute;
        private static GUIStyle _label;

        internal static void Tick(Aircraft ac)
        {
            if (ac == null || ac.disabled || !Rifle.CurrentStationIsOurs(ac))
            {
                _has = false;
                return;
            }
            if (!InGunView())
            {
                _has = false;
                return;
            }
            float now = Time.unscaledTime;
            if (now < _nextCompute)
                return;
            _nextCompute = now + 0.05f;
            Compute(ac);
        }

        internal static bool TryScreenPoint(out Vector3 sp)
        {
            sp = Vector3.zero;
            if (!_has)
                return false;
            Camera cam = ViewCam();
            if (cam == null)
                return false;
            Vector3 to = _smooth - cam.transform.position;
            if (to.sqrMagnitude < 4f)
                return false;
            if (Vector3.Dot(to.normalized, cam.transform.forward) < MinForwardDot)
                return false;
            try
            {
                sp = cam.WorldToScreenPoint(_smooth);
            }
            catch
            {
                return false;
            }
            return sp.z > 0.05f;
        }

        internal static void Draw()
        {
            if (!_has)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            if (!InGunView())
                return;
            Camera cam = ViewCam();
            if (cam == null)
                return;
            Vector3 to = _smooth - cam.transform.position;
            if (to.sqrMagnitude < 4f)
                return;
            if (Vector3.Dot(to.normalized, cam.transform.forward) < MinForwardDot)
                return;
            Vector3 sp;
            try
            {
                sp = cam.WorldToScreenPoint(_smooth);
            }
            catch
            {
                return;
            }
            if (sp.z <= 0.05f)
                return;
            float x = sp.x;
            float y = (float)Screen.height - sp.y;
            if (x < 6f || x > (float)Screen.width - 6f || y < 6f || y > (float)Screen.height - 6f)
                return;

            Color pip = Rifle.ModePipColor();
            DrawPip(x, y, pip);
            EnsureLabel();
            Color prev = GUI.color;
            GUI.color = pip;
            GUI.Label(new Rect(x + 14f, y + 10f, 180f, 18f),
                "T+" + _tof.ToString("F1") + "s", _label);
            GUI.color = prev;
        }

        private static void Compute(Aircraft ac)
        {
            _has = false;
            Gun gun = Rifle.FindOurGun(ac);
            if (gun == null)
                return;
            Transform muz = ResolveMuzzle(gun);
            if (muz == null)
                muz = gun.transform;
            Vector3 pos = muz.position;
            Vector3 fwd = muz.forward;
            Vector3 vel = Vector3.zero;
            try
            {
                if (ac.rb != null)
                    vel = ac.rb.velocity;
            }
            catch
            {
            }
            PowerSupply ps = Rifle.PowerOf(ac);
            float muzzle = Rifle.PeekMuzzle(ac, ps);
            vel += fwd * muzzle;
            _muzzleWorld = pos;

            float dragCoef = 0f;
            float grav = 1f;
            float v0 = muzzle;
            if (gun.info != null)
            {
                dragCoef = gun.info.dragCoef;
                grav = gun.info.gravMult;
                if (v0 < 1f)
                    v0 = gun.info.muzzleVelocity;
            }
            if (v0 < 1f)
                v0 = Rifle.BaseMuzzle;

            float seaY = 0f;
            try
            {
                seaY = Datum.LocalSeaY;
            }
            catch
            {
            }

            float t = 0f;
            Vector3 prev = pos;
            bool hit = false;
            Vector3 hitPoint = pos;
            for (int i = 0; i < MaxSteps; i++)
            {
                float dt = StepDt;
                vel.y -= 9.81f * dt * grav;
                float spSq = vel.sqrMagnitude;
                if (spSq > 0.01f && dragCoef > 0f && v0 > 1f)
                    vel -= vel.normalized * (spSq * dragCoef * dt / v0);
                Vector3 next = pos + vel * dt;
                t += dt;

                Vector3 delta = next - prev;
                float seg = delta.magnitude;
                if (seg > 0.05f)
                {
                    RaycastHit rh;
                    if (Physics.Raycast(prev, delta / seg, out rh, seg + 0.05f, TerrainMask))
                    {
                        hitPoint = rh.point;
                        hit = true;
                        break;
                    }
                }
                if (next.y < seaY && prev.y >= seaY)
                {
                    float frac = (prev.y - seaY) / Mathf.Max(0.0001f, prev.y - next.y);
                    hitPoint = Vector3.Lerp(prev, next, Mathf.Clamp01(frac));
                    hitPoint.y = seaY;
                    hit = true;
                    break;
                }
                prev = pos;
                pos = next;
                if (t > 80f)
                    break;
            }
            if (!hit)
                return;
            _has = true;
            _world = hitPoint;
            _tof = t;
            if (_smooth.sqrMagnitude < 0.01f)
                _smooth = hitPoint;
            else
                _smooth = Vector3.Lerp(_smooth, hitPoint, 0.35f);
        }

        private static Transform ResolveMuzzle(Gun gun)
        {
            if (gun == null)
                return null;
            if (MuzzlesField != null)
            {
                try
                {
                    Transform[] arr = MuzzlesField.GetValue(gun) as Transform[];
                    if (arr != null && arr.Length > 0 && arr[0] != null)
                        return arr[0];
                }
                catch
                {
                }
            }
            return gun.transform;
        }

        private static bool InGunView()
        {
            try
            {
                CameraMode mode = CameraStateManager.cameraMode;
                if (mode == CameraMode.cockpit || mode == CameraMode.chase || mode == CameraMode.orbit)
                    return true;
            }
            catch
            {
            }
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null)
                {
                    if (object.ReferenceEquals(csm.currentState, csm.cockpitState)
                        || object.ReferenceEquals(csm.currentState, csm.chaseState)
                        || object.ReferenceEquals(csm.currentState, csm.orbitState))
                        return true;
                }
            }
            catch
            {
            }
            return Camera.main != null;
        }

        private static Camera ViewCam()
        {
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null && csm.mainCamera != null && csm.mainCamera.enabled)
                    return csm.mainCamera;
            }
            catch
            {
            }
            return Camera.main;
        }

        private static void DrawPip(float cx, float cy, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            Texture2D tex = Texture2D.whiteTexture;
            float arm = 12f;
            float th = 2f;
            float gap = 4f;
            GUI.DrawTexture(new Rect(cx - arm, cy - th * 0.5f, arm - gap, th), tex);
            GUI.DrawTexture(new Rect(cx + gap, cy - th * 0.5f, arm - gap, th), tex);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy - arm, th, arm - gap), tex);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy + gap, th, arm - gap), tex);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), tex);
            float r = 10f;
            GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2f, 1.5f), tex);
            GUI.DrawTexture(new Rect(cx - r, cy + r - 1.5f, r * 2f, 1.5f), tex);
            GUI.DrawTexture(new Rect(cx - r, cy - r, 1.5f, r * 2f), tex);
            GUI.DrawTexture(new Rect(cx + r - 1.5f, cy - r, 1.5f, r * 2f), tex);
            GUI.color = prev;
        }

        private static void EnsureLabel()
        {
            if (_label != null)
                return;
            _label = new GUIStyle();
            _label.fontSize = 12;
            _label.fontStyle = FontStyle.Bold;
            _label.normal.textColor = Color.white;
        }
    }
}
