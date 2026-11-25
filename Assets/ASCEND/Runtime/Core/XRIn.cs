using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using InputDevice = UnityEngine.XR.InputDevice;
using CommonUsages = UnityEngine.XR.CommonUsages;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Ascend
{
    public enum Hand { Left = 0, Right = 1 }
    public enum Btn { Grip, Trigger, Primary, Secondary, Menu, Stick }

    /// <summary>Controller state for both Touch controllers (grip, trigger, A/B/X/Y, menu, sticks, finger touches),
    /// with edge detection and haptics. Falls back to keyboard/mouse in the editor.</summary>
    public static class XRIn
    {
        struct State
        {
            public float grip, trigger; public bool primary, secondary, menu, stick, triggerTouch, thumbTouch; public Vector2 axis;
            public bool Get(Btn b)
            {
                switch (b)
                {
                    case Btn.Grip: return grip > 0.6f;
                    case Btn.Trigger: return trigger > 0.6f;
                    case Btn.Primary: return primary;
                    case Btn.Secondary: return secondary;
                    case Btn.Menu: return menu;
                    default: return stick;
                }
            }
        }

        static readonly State[] cur = new State[2], prev = new State[2];
        static int lastFrame = -1;
        static readonly List<InputDevice> tmp = new List<InputDevice>();
        static InputDevice[] dev = new InputDevice[2];
        static readonly InputFeatureUsage<bool> TriggerTouch = new InputFeatureUsage<bool>("TriggerTouch");
        static readonly InputFeatureUsage<bool> IndexTouch = new InputFeatureUsage<bool>("IndexTouch");
        static readonly InputFeatureUsage<bool> ThumbrestTouch = new InputFeatureUsage<bool>("ThumbrestTouch");

        public static bool HasHeadset { get { return XRSettings.isDeviceActive; } }

        static void Poll()
        {
            if (lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            for (int h = 0; h < 2; h++)
            {
                prev[h] = cur[h];
                var d = dev[h];
                if (!d.isValid)
                {
                    tmp.Clear();
                    InputDevices.GetDevicesWithCharacteristics((h == 0 ? InputDeviceCharacteristics.Left : InputDeviceCharacteristics.Right) | InputDeviceCharacteristics.Controller, tmp);
                    if (tmp.Count > 0) { d = tmp[0]; dev[h] = d; }
                }
                var s = new State();
                if (d.isValid)
                {
                    d.TryGetFeatureValue(CommonUsages.grip, out s.grip);
                    d.TryGetFeatureValue(CommonUsages.trigger, out s.trigger);
                    d.TryGetFeatureValue(CommonUsages.primaryButton, out s.primary);
                    d.TryGetFeatureValue(CommonUsages.secondaryButton, out s.secondary);
                    d.TryGetFeatureValue(CommonUsages.menuButton, out s.menu);
                    d.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out s.stick);
                    d.TryGetFeatureValue(CommonUsages.primary2DAxis, out s.axis);
                    bool tt = false, it = false;
                    d.TryGetFeatureValue(TriggerTouch, out tt);
                    d.TryGetFeatureValue(IndexTouch, out it);
                    s.triggerTouch = tt || it || s.trigger > 0.05f;
                    bool pt, st, at, rt = false;
                    d.TryGetFeatureValue(CommonUsages.primaryTouch, out pt);
                    d.TryGetFeatureValue(CommonUsages.secondaryTouch, out st);
                    d.TryGetFeatureValue(CommonUsages.primary2DAxisTouch, out at);
                    d.TryGetFeatureValue(ThumbrestTouch, out rt);
                    s.thumbTouch = pt || st || at || rt || s.primary || s.secondary;
                }
                else Desktop(h, ref s);
                cur[h] = s;
            }
        }

        static void Desktop(int h, ref State s)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; var m = Mouse.current;
            if (k == null) return;
            if (h == 1)
            {
                s.grip = k.gKey.isPressed ? 1f : 0f;
                s.trigger = (m != null && m.middleButton.isPressed) || k.fKey.isPressed ? 1f : 0f;
                s.primary = k.eKey.isPressed; s.secondary = k.rKey.isPressed;
            }
            else
            {
                s.menu = k.tabKey.isPressed || k.mKey.isPressed;
                // (Q is the desktop turn-left key in DesktopTester: it must not double as Y, which toggles the main menu)
                s.primary = k.xKey.isPressed; s.secondary = k.yKey.isPressed;
                s.grip = k.hKey.isPressed ? 1f : 0f;
            }
#endif
        }

        public static float Grip(Hand h) { Poll(); return cur[(int)h].grip; }
        public static float Trigger(Hand h) { Poll(); return cur[(int)h].trigger; }
        public static bool TriggerTouched(Hand h) { Poll(); return cur[(int)h].triggerTouch; }
        public static bool ThumbTouched(Hand h) { Poll(); return cur[(int)h].thumbTouch; }
        public static Vector2 Stick(Hand h) { Poll(); return cur[(int)h].axis; }
        public static bool Held(Hand h, Btn b) { Poll(); return cur[(int)h].Get(b); }
        public static bool Down(Hand h, Btn b) { Poll(); return cur[(int)h].Get(b) && !prev[(int)h].Get(b); }
        public static bool Up(Hand h, Btn b) { Poll(); return !cur[(int)h].Get(b) && prev[(int)h].Get(b); }
        public static bool AnyDown(Btn b) { return Down(Hand.Left, b) || Down(Hand.Right, b); }

        /// <summary>Index finger out, other fingers closed on the grip = pointing.</summary>
        public static bool Pointing(Hand h) { Poll(); var s = cur[(int)h]; return s.grip > 0.55f && s.trigger < 0.15f && !s.triggerTouch; }

        public static void Haptic(Hand h, float amplitude, float seconds)
        {
            var d = dev[(int)h];
            if (!d.isValid) return;
            HapticCapabilities caps;
            if (d.TryGetHapticCapabilities(out caps) && caps.supportsImpulse) d.SendHapticImpulse(0, Mathf.Clamp01(amplitude), seconds);
        }
        public static void HapticBoth(float amplitude, float seconds) { Haptic(Hand.Left, amplitude, seconds); Haptic(Hand.Right, amplitude, seconds); }

        /// <summary>A short rhythmic pattern (wins, jackpots).</summary>
        public static System.Collections.IEnumerator Pattern(Hand h, int pulses, float amp, float gap)
        {
            for (int i = 0; i < pulses; i++) { Haptic(h, amp, 0.05f); yield return new WaitForSeconds(gap); }
        }

#if ENABLE_INPUT_SYSTEM
        static Keyboard keysOf;
        static readonly Dictionary<string, UnityEngine.InputSystem.Controls.KeyControl> keyCache = new Dictionary<string, UnityEngine.InputSystem.Controls.KeyControl>();
#endif
        /// <summary>Key pressed this frame, by its display name on the current layout ("t", "tab"…). Lookups are cached:
        /// several components poll keys every frame and a layout search walks every key.</summary>
        public static bool KeyDown(string key)
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current; if (k == null) return false;
            if (k != keysOf) { keysOf = k; keyCache.Clear(); }
            UnityEngine.InputSystem.Controls.KeyControl c;
            if (!keyCache.TryGetValue(key, out c))
            {
                // physical key names first ("tab", "space", "e"): layout names change with the language (Tabulador, Espacio...)
                UnityEngine.InputSystem.Key kk;
                c = key.Length > 1 && System.Enum.TryParse(key, true, out kk) && kk != UnityEngine.InputSystem.Key.None ? k[kk] : null;
                if (c == null) c = k.FindKeyOnCurrentKeyboardLayout(key);
                if (c == null && key.Length == 1 && System.Enum.TryParse(key, true, out kk)) c = k[kk];
                keyCache[key] = c;
            }
            return c != null && c.wasPressedThisFrame;
#else
            return false;
#endif
        }

        /// <summary>Desktop testing without a headset: holding Left Ctrl counts as ducking (the raid laser).</summary>
        public static bool DesktopCrouch
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                var k = Keyboard.current;
                return !HasHeadset && k != null && k.leftCtrlKey.isPressed;
#else
                return false;
#endif
            }
        }
    }
}
