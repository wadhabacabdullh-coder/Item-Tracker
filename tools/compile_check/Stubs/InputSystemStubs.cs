// Compile-check stubs for com.unity.inputsystem (only what GameInput uses).
namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;

    // Same ordering as the real enum: digits run 1..9 then 0.
    public enum Key
    {
        None, Space, Enter, Tab, Backquote, Quote, Semicolon, Comma, Period, Slash, Backslash, LeftBracket, RightBracket, Minus, Equals,
        A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        Digit1, Digit2, Digit3, Digit4, Digit5, Digit6, Digit7, Digit8, Digit9, Digit0,
        LeftShift, RightShift, LeftAlt, RightAlt, AltGr = RightAlt, LeftCtrl, RightCtrl, LeftMeta, RightMeta, ContextMenu,
        Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Backspace, PageDown, PageUp, Home, End, Insert, Delete, CapsLock, NumLock,
        PrintScreen, ScrollLock, Pause, NumpadEnter, NumpadDivide, NumpadMultiply, NumpadPlus, NumpadMinus, NumpadPeriod, NumpadEquals,
        Numpad0, Numpad1, Numpad2, Numpad3, Numpad4, Numpad5, Numpad6, Numpad7, Numpad8, Numpad9,
        F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    }

    public class InputDevice { }
    public class Keyboard : InputDevice
    {
        public static Keyboard current { get; }
        public KeyControl this[Key key] => null;
    }
    public class Pointer : InputDevice
    {
        public Vector2Control position { get; }
    }
    public class Mouse : Pointer
    {
        public static Mouse current { get; }
        public Vector2Control scroll { get; }
        public ButtonControl leftButton { get; }
        public ButtonControl rightButton { get; }
        public ButtonControl middleButton { get; }
        public ButtonControl backButton { get; }
        public ButtonControl forwardButton { get; }
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class InputControl { }
    public class ButtonControl : InputControl
    {
        public bool isPressed { get; }
        public bool wasPressedThisFrame { get; }
    }
    public class KeyControl : ButtonControl { }
    public class Vector2Control : InputControl
    {
        public Vector2 ReadValue() => default;
    }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule { public void AssignDefaultActions() { } }
}
