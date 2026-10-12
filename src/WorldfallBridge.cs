using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldBoxMP
{
    public static class WorldfallBridge
    {
        private static bool _looked;

        private static bool _broken;

        private static PropertyInfo _instance;

        private static FieldInfo _active;

        private static FieldInfo _scene;

        private static FieldInfo _lookAt;

        private static FieldInfo _hooksActive;

        private static FieldInfo _yaw;

        private static FieldInfo _aimPoint;

        private static object _mod;
        private static FieldInfo _day, _timeOfDay, _days;
        private static bool _dayLogged;

        private static Dictionary<Actor, Vector2> _lookTable;

        private static bool _steeringPatched;

        public static bool Present
        {
            get
            {
                Find();
                return !_broken;
            }
        }

        private static void Find()
        {
            if (_looked)
            {
                return;
            }
            _looked = true;
            try
            {
                Type type = AccessTools.TypeByName("FirstPerson.WorldBoxMod");
                Type type2 = AccessTools.TypeByName("FirstPerson.PossessionHooks");
                Type type3 = AccessTools.TypeByName("FirstPerson.SceneCollector");
                if (type == null || type2 == null || type3 == null)
                {
                    _broken = true;
                    return;
                }
                _instance = AccessTools.Property(type, "Instance");
                _active = AccessTools.Field(type, "_active");
                _scene = AccessTools.Field(type, "_scene");
                _lookAt = AccessTools.Field(type3, "LookAt");
                _hooksActive = AccessTools.Field(type2, "Active");
                _yaw = AccessTools.Field(type2, "Yaw");
                _aimPoint = AccessTools.Field(type2, "AimPoint");
                _day = AccessTools.Field(type, "_day");
                Type dayType = AccessTools.TypeByName("FirstPerson.DayCycle");
                if (dayType != null)
                {
                    _timeOfDay = AccessTools.Field(dayType, "TimeOfDay");
                    _days = AccessTools.Field(dayType, "_days");
                }
                if (_instance == null || _active == null || _hooksActive == null || _yaw == null)
                {
                    _broken = true;
                    Debug.Log("[WorldBoxMP] Worldfall found, but not the parts first person sync needs");
                }
            }
            catch (Exception ex)
            {
                _broken = true;
                Debug.Log("[WorldBoxMP] Worldfall bridge off: " + ex.GetBaseException().Message);
            }
        }

        static object DayCycle()
        {
            if (!Present || _day == null || _timeOfDay == null || _days == null) return null;
            try
            {
                object mod = _instance.GetValue(null, null);
                return mod != null ? _day.GetValue(mod) : null;
            }
            catch
            {
                return null;
            }
        }

        public static double DayClock
        {
            get
            {
                object day = DayCycle();
                if (day == null) return -1.0;
                try { return (int)_days.GetValue(day) + (double)(float)_timeOfDay.GetValue(day); }
                catch { return -1.0; }
            }
        }

        public static void SetDayClock(double clock)
        {
            if (clock < 0.0) return;
            object day = DayCycle();
            if (day == null) return;
            try
            {
                int days = (int)Math.Floor(clock);
                float time = (float)(clock - days);
                if (!_dayLogged)
                {
                    _dayLogged = true;
                    SessionLog.Write("Worldfall time of day follows the host: " + time.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                                     " (was " + ((float)_timeOfDay.GetValue(day)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")");
                }
                _days.SetValue(day, days);
                _timeOfDay.SetValue(day, time);
            }
            catch (Exception e)
            {
                Debug.Log("[WorldBoxMP] Worldfall day clock: " + e.GetBaseException().Message);
            }
        }

        public static bool InFirstPerson
        {
            get
            {
                if (!Present)
                {
                    return false;
                }
                try
                {
                    _mod = _instance.GetValue(null, null);
                    return _mod != null && (bool)_active.GetValue(_mod) && (bool)_hooksActive.GetValue(null);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static float Yaw
        {
            get
            {
                try
                {
                    return (float)_yaw.GetValue(null);
                }
                catch
                {
                    return 0f;
                }
            }
        }

        public static bool TryAimPoint(out Vector2 point)
        {
            point = default(Vector2);
            if (_aimPoint == null)
            {
                return false;
            }
            try
            {
                point = (Vector2)_aimPoint.GetValue(null);
                return point != Vector2.zero;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<Actor, Vector2> LookTable()
        {
            if (!Present || _scene == null || _lookAt == null)
            {
                return null;
            }
            try
            {
                _mod = _instance.GetValue(null, null);
                if (_mod == null)
                {
                    return null;
                }
                object value = _scene.GetValue(_mod);
                _lookTable = ((value != null) ? (_lookAt.GetValue(value) as Dictionary<Actor, Vector2>) : null);
                return _lookTable;
            }
            catch
            {
                return null;
            }
        }

        public static void SetLook(Actor a, Vector2 point)
        {
            Dictionary<Actor, Vector2> dictionary = LookTable();
            if (dictionary != null && a != null)
            {
                dictionary[a] = point;
            }
        }

        public static void ClearLook(Actor a)
        {
            if (a != null)
            {
                LookTable()?.Remove(a);
            }
        }

        public static void PatchSteering(Harmony harmony)
        {
            if (_steeringPatched || !Present)
            {
                return;
            }
            _steeringPatched = true;
            try
            {
                MethodInfo methodInfo = AccessTools.Method(AccessTools.TypeByName("FirstPerson.Steering"), "Steer");
                if (methodInfo == null)
                {
                    Debug.Log("[WorldBoxMP] Worldfall steering not found");
                    return;
                }
                harmony.Patch(methodInfo, new HarmonyMethod(typeof(WorldfallBridge), "SteerPrefix"));
                Debug.Log("[WorldBoxMP] Worldfall steering follows the host now");
            }
            catch (Exception ex)
            {
                Debug.Log("[WorldBoxMP] could not patch Worldfall steering: " + ex.GetBaseException().Message);
            }
        }

        private static bool SteerPrefix(Actor a, ref bool __result)
        {
            if (Net.IsClientConnected || (Net.Role == Role.Host && RemotePossession.IsRemote(a)))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
