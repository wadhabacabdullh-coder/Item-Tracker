using System;
using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ShadowContract.Game
{
    /// <summary>Every rebindable action in the game.</summary>
    public enum GameAction
    {
        MoveUp, MoveDown, MoveLeft, MoveRight, Attack, Aim, Reload, Interact, Sprint, Crouch,
        Slot1, Slot2, Slot3, Slot4, Slot5, Inventory, Pause, Medkit,
    }

    /// <summary>
    /// Rebindable input. Bindings are stored as <see cref="KeyCode"/> names (mouse buttons included) and polled through
    /// the new Input System when it is active, or the legacy Input Manager otherwise - so the game works with either setting.
    /// </summary>
    public static class GameInput
    {
        public static readonly Dictionary<GameAction, KeyCode> Defaults = new Dictionary<GameAction, KeyCode>
        {
            [GameAction.MoveUp] = KeyCode.W, [GameAction.MoveDown] = KeyCode.S, [GameAction.MoveLeft] = KeyCode.A, [GameAction.MoveRight] = KeyCode.D,
            [GameAction.Attack] = KeyCode.Mouse0, [GameAction.Aim] = KeyCode.Mouse1, [GameAction.Reload] = KeyCode.R, [GameAction.Interact] = KeyCode.E,
            [GameAction.Sprint] = KeyCode.LeftShift, [GameAction.Crouch] = KeyCode.LeftControl,
            [GameAction.Slot1] = KeyCode.Alpha1, [GameAction.Slot2] = KeyCode.Alpha2, [GameAction.Slot3] = KeyCode.Alpha3, [GameAction.Slot4] = KeyCode.Alpha4,
            [GameAction.Slot5] = KeyCode.Alpha5, [GameAction.Inventory] = KeyCode.Tab, [GameAction.Pause] = KeyCode.Escape, [GameAction.Medkit] = KeyCode.H,
        };

        /// <summary>Secondary bindings that always work (crouch on C for browsers/laptops, arrows for movement).</summary>
        private static readonly Dictionary<GameAction, KeyCode> Alternates = new Dictionary<GameAction, KeyCode>
        {
            [GameAction.Crouch] = KeyCode.C, [GameAction.MoveUp] = KeyCode.UpArrow, [GameAction.MoveDown] = KeyCode.DownArrow,
            [GameAction.MoveLeft] = KeyCode.LeftArrow, [GameAction.MoveRight] = KeyCode.RightArrow,
        };

        private static readonly Dictionary<GameAction, KeyCode> Bindings = new Dictionary<GameAction, KeyCode>(Defaults);

        public static KeyCode Get(GameAction a) => Bindings.TryGetValue(a, out var k) ? k : KeyCode.None;

        public static void Set(GameAction a, KeyCode k)
        {
            // Swap with any action that already uses this key so nothing ends up unbound silently.
            foreach (var kv in new List<KeyValuePair<GameAction, KeyCode>>(Bindings))
                if (kv.Key != a && kv.Value == k) Bindings[kv.Key] = Get(a);
            Bindings[a] = k;
        }

        public static void ResetDefaults()
        {
            Bindings.Clear();
            foreach (var kv in Defaults) Bindings[kv.Key] = kv.Value;
        }

        public static void Load(Dictionary<string, string> saved)
        {
            ResetDefaults();
            if (saved == null) return;
            foreach (var kv in saved)
                if (Enum.TryParse(kv.Key, out GameAction a) && Enum.TryParse(kv.Value, out KeyCode k)) Bindings[a] = k;
        }

        public static Dictionary<string, string> Save()
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in Bindings) d[kv.Key.ToString()] = kv.Value.ToString();
            return d;
        }

        public static bool Held(GameAction a) => IsHeld(Get(a)) || (Alternates.TryGetValue(a, out var alt) && IsHeld(alt));
        public static bool Pressed(GameAction a) => IsDown(Get(a)) || (Alternates.TryGetValue(a, out var alt) && IsDown(alt));

        public static Vector2 MousePosition
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
                return Input.mousePosition;
#endif
            }
        }

        public static float Scroll
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mathf.Sign(Mouse.current.scroll.ReadValue().y) * (Mathf.Abs(Mouse.current.scroll.ReadValue().y) > 0.01f ? 1 : 0) : 0f;
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }

        public static bool AnyKeyDown(out KeyCode key)
        {
            key = KeyCode.None;
            foreach (KeyCode k in RebindableKeys)
                if (IsDown(k)) { key = k; return true; }
            return false;
        }

        public static string KeyName(KeyCode k)
        {
            switch (k)
            {
                case KeyCode.Mouse0: return "LMB";
                case KeyCode.Mouse1: return "RMB";
                case KeyCode.Mouse2: return "MMB";
                case KeyCode.LeftShift: return "Shift";
                case KeyCode.RightShift: return "RShift";
                case KeyCode.LeftControl: return "Ctrl";
                case KeyCode.RightControl: return "RCtrl";
                case KeyCode.LeftAlt: return "Alt";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Return: return "Enter";
                case KeyCode.Space: return "Space";
                case KeyCode.Tab: return "Tab";
                default:
                    string s = k.ToString();
                    return s.StartsWith("Alpha") ? s.Substring(5) : s;
            }
        }

        public static string Label(GameAction a) => KeyName(Get(a));

        public static readonly KeyCode[] RebindableKeys = BuildRebindable();

        private static KeyCode[] BuildRebindable()
        {
            var list = new List<KeyCode>();
            for (var k = KeyCode.A; k <= KeyCode.Z; k++) list.Add(k);
            for (var k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) list.Add(k);
            list.AddRange(new[]
            {
                KeyCode.Space, KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftControl, KeyCode.RightControl, KeyCode.LeftAlt, KeyCode.Tab,
                KeyCode.Return, KeyCode.Backspace, KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                KeyCode.Mouse0, KeyCode.Mouse1, KeyCode.Mouse2, KeyCode.Mouse3, KeyCode.Mouse4, KeyCode.Escape,
                KeyCode.F1, KeyCode.F2, KeyCode.F3, KeyCode.F4, KeyCode.Q, KeyCode.BackQuote,
            });
            return list.ToArray();
        }

        // ------------------------------------------------------------------ backend

        private static bool IsHeld(KeyCode k)
        {
            if (k == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
            if (IsMouse(k, out int b)) return MouseButton(b)?.isPressed ?? false;
            var key = ToKey(k);
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].isPressed;
#else
            return Input.GetKey(k);
#endif
        }

        private static bool IsDown(KeyCode k)
        {
            if (k == KeyCode.None) return false;
#if ENABLE_INPUT_SYSTEM
            if (IsMouse(k, out int b)) return MouseButton(b)?.wasPressedThisFrame ?? false;
            var key = ToKey(k);
            return key != Key.None && Keyboard.current != null && Keyboard.current[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(k);
#endif
        }

        private static bool IsMouse(KeyCode k, out int button)
        {
            button = k - KeyCode.Mouse0;
            return k >= KeyCode.Mouse0 && k <= KeyCode.Mouse6;
        }

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.Controls.ButtonControl MouseButton(int b)
        {
            var m = Mouse.current;
            if (m == null) return null;
            switch (b)
            {
                case 0: return m.leftButton;
                case 1: return m.rightButton;
                case 2: return m.middleButton;
                case 3: return m.backButton;
                case 4: return m.forwardButton;
                default: return null;
            }
        }

        private static readonly Dictionary<KeyCode, Key> KeyMap = new Dictionary<KeyCode, Key>();

        private static Key ToKey(KeyCode k)
        {
            if (KeyMap.TryGetValue(k, out var cached)) return cached;
            Key result = Key.None;
            // Input System orders digits 1..9 then 0, unlike KeyCode (0..9): map explicitly.
            if (k == KeyCode.Alpha0) result = Key.Digit0;
            else if (k >= KeyCode.Alpha1 && k <= KeyCode.Alpha9) result = Key.Digit1 + (k - KeyCode.Alpha1);
            else
            {
                switch (k)
                {
                    case KeyCode.LeftControl: result = Key.LeftCtrl; break;
                    case KeyCode.RightControl: result = Key.RightCtrl; break;
                    case KeyCode.Return: result = Key.Enter; break;
                    case KeyCode.BackQuote: result = Key.Backquote; break;
                    case KeyCode.LeftAlt: result = Key.LeftAlt; break;
                    default:
                        if (!Enum.TryParse(k.ToString(), out result)) result = Key.None;
                        break;
                }
            }
            KeyMap[k] = result;
            return result;
        }
#endif
    }
}
