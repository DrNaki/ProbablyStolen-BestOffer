// Only Unity input and the host's external boundaries are replaced. The hotkey
// dispatcher, capture, conflict checks and event/poll arbitration are production code.
using System.Text.Json;

#pragma warning disable CS0414 // Fields used by the linked production partial class.

namespace UnityEngine
{
    public enum KeyCode
    {
        None, Escape, LeftControl, RightControl, LeftAlt, RightAlt, LeftShift, RightShift,
        LeftWindows, RightWindows, LeftCommand, RightCommand, AltGr,
        F1 = 100, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12
    }
    public enum EventType { KeyDown, KeyUp, Used }
    public sealed class Event
    {
        public static Event? current;
        public KeyCode keyCode;
        public bool control, alt, shift;
        public EventType rawType = EventType.KeyDown;
        public bool Used;
        public void Use() { Used = true; rawType = EventType.Used; }
    }
    public static class Application { public static bool isFocused = true; }
    public static class Time { public static int frameCount = 10; public static float unscaledTime = 1; }
    public static class Input
    {
        public static readonly HashSet<KeyCode> Held = new(), Down = new(), Up = new();
        public static bool GetKey(KeyCode code) => Held.Contains(code);
        public static bool GetKeyDown(KeyCode code) => Down.Contains(code);
        public static bool GetKeyUp(KeyCode code) => Up.Contains(code);
        public static void Clear() { Held.Clear(); Down.Clear(); Up.Clear(); }
    }
    public sealed class GameObject
    {
        public bool activeInHierarchy = true;
        readonly List<object> components = new();
        public T? GetComponent<T>() where T : class => components.OfType<T>().FirstOrDefault();
        public void Add(object component) => components.Add(component);
    }
    public static class Resources
    {
        public static readonly List<object> Objects = new();
        public static T[] FindObjectsOfTypeAll<T>() => Objects.OfType<T>().ToArray();
    }
}
namespace UnityEngine.UI { public sealed class InputField { public bool isFocused; } }
namespace TMPro { public sealed class TMP_InputField { public bool isFocused; } }
namespace Il2CppTMPro { public sealed class TMP_InputField { public bool isFocused; } }
namespace UnityEngine.EventSystems
{
    public sealed class EventSystem
    {
        public static EventSystem? current;
        public UnityEngine.GameObject? currentSelectedGameObject;
    }
}
public sealed class EmporiumEntry { public static EmporiumEntry? Instance = new(); }
public sealed class NegociationUIManager
{
    public static NegociationUIManager? Instance = new();
    public UnityEngine.GameObject? uiObject = new();
}
public sealed class EscapeUIManager { public UnityEngine.GameObject? menuPanel; }
namespace DeepSpaceBestOffer
{
    internal static class WorkshopRuntime { internal static string UserDataDirectory = ""; }
    internal static class WorkshopJson { internal static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value); }
    public sealed class AuditAdapter
    {
        public int Captures;
        public string DevelopmentOnlyAuditCapture(int count, object options) { Captures++; return "{}"; }
    }
    public sealed class TestLogger { public int Messages; public void Msg(string message) => Messages++; }
    public sealed partial class BestOfferMod
    {
        public static BestOfferMod? Current;
        public bool IsOpen { get; private set; }
        public bool WorkshopSettingsVisible;
        public bool TradingReady = true;
        public bool Busy;
        public bool CalculationStartsWork = true;
        public int Toggles, Saves, Syncs, Suppressions, Notices;
        public bool InputSuppressedBeforeCalculation;
        public readonly List<(bool Quick, bool Sell)> Calculations = new();
        public readonly AuditAdapter adapter = new();
        public readonly TestLogger LoggerInstance = new();
        int captureHotkey;
        bool uiDirty;
        string status = "test status";
        bool busy => Busy;
        public bool HideWorkshopSettings() { WorkshopSettingsVisible = false; return true; }
        bool CanQuickApplyFromUI() => TradingReady;
        void SuppressTransitionInput() => Suppressions++;
        void SaveSettings() => Saves++;
        void SyncManagerHotkeys() => Syncs++;
        void Toggle() { Toggles++; IsOpen = !IsOpen; }
        void Calculate(bool quick, bool sell)
        { InputSuppressedBeforeCalculation = Suppressions > 0; Calculations.Add((quick, sell)); Busy = CalculationStartsWork; }
        void ShowQuickNotice(string message) => Notices++;
        object CurrentOptions() => new();

        public void Poll() => UpdateHotkeyActions();
        public void Gui(UnityEngine.Event evt) { UnityEngine.Event.current = evt; OnGUI(); UnityEngine.Event.current = null; }
        public void CapturePoll() => CaptureLocalHotkey();
        public void BeginCapture(int action) => captureHotkey = action;
        public int Capturing => captureHotkey;
        public string Status => status;
        public string[] Bindings => new[] { panelHotkey, auditHotkey, placeHotkey, sellHotkey };
        public void SetBindings(string panel, string audit, string place, string sell)
        { panelHotkey = panel; auditHotkey = audit; placeHotkey = place; sellHotkey = sell; }
        public void Repair() => RepairActionHotkeys();
        public static (bool Valid, string Normalized) NormalizeAction(string text)
        { bool valid = TryNormalizeOptionalHotkey(text, out var normalized); return (valid, normalized); }
        public void ClearBinding(int action) => ClearHotkey(action);
        public void OpenForAudit() => IsOpen = true;
    }
}
