using DeepSpaceBestOffer;
using DeepSpaceModSupport;
using UnityEngine;
using UnityEngine.EventSystems;

int checks = 0, scenarios = 0;
void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
var fixtureBase = Path.GetFullPath(Path.GetTempPath());
var auditDirectory = Path.GetFullPath(Path.Combine(fixtureBase, "BestOffer-trade-hotkeys-" + Guid.NewGuid().ToString("N")));
Directory.CreateDirectory(auditDirectory);
WorkshopRuntime.UserDataDirectory = auditDirectory;
BestOfferMod New()
{
    scenarios++;
    Input.Clear(); Application.isFocused = true; Time.frameCount = 10; Time.unscaledTime = 1;
    Resources.Objects.Clear(); EventSystem.current = null; Event.current = null;
    EmporiumEntry.Instance = new(); NegociationUIManager.Instance = new();
    var mod = new BestOfferMod(); BestOfferMod.Current = mod; return mod;
}
Event Key(KeyCode code, bool ctrl = false, bool alt = false, bool shift = false) =>
    new() { keyCode = code, control = ctrl, alt = alt, shift = shift };
void Down(KeyCode code, bool ctrl = false, bool alt = false, bool shift = false)
{
    Input.Clear(); Input.Down.Add(code);
    if (ctrl) Input.Held.Add(KeyCode.LeftControl);
    if (alt) Input.Held.Add(KeyCode.LeftAlt);
    if (shift) Input.Held.Add(KeyCode.LeftShift);
}
void Next(float seconds = .016f)
{ Input.Clear(); Time.frameCount++; Time.unscaledTime += seconds; }
void ExpectTrade(BestOfferMod mod, bool sell, string context, int expectedSuppressions = 1)
{
    Check(mod.Calculations.Count == 1, context + ": expected exactly one calculation");
    Check(mod.Calculations[0] == (true, sell), context + ": wrong placement/sale action");
    Check(mod.Toggles == 0 && mod.adapter.Captures == 0, context + ": triggered panel/diagnostics instead");
    Check(mod.Suppressions == expectedSuppressions && mod.InputSuppressedBeforeCalculation, context + ": input not consumed before native handling");
}
try
{
    var mod = New();
    Check(mod.Bindings.SequenceEqual(new[] { "F8", "F9", "Ctrl+F8", "Ctrl+F9" }), "Default four bindings changed");
    Check(KeyChord.ValidHotkeySet("F8", "F9", "", ""), "Optional actions cannot be disabled");
    Check(KeyChord.ValidHotkeySet("F8", "F9", "Ctrl+F8", "Ctrl+F9"), "Modified defaults conflict with plain keys");
    Check(!KeyChord.ValidHotkeySet("F8", "F9", "Control+f8", "Ctrl+F8"), "Equivalent optional chords accepted twice");
    Check(!KeyChord.ValidHotkeySet("F8", "F9", "F8", "Ctrl+F9"), "Place can duplicate panel");
    Check(!KeyChord.ValidHotkeySet("F8", "F9", "Ctrl+F8", "f9"), "Sell can duplicate diagnostics");
    Check(!KeyChord.ValidHotkeySet("F8", "F9", "Escape", ""), "Escape accepted for action");
    Check(BestOfferMod.NormalizeAction("") == (true, ""), "Empty action is not a valid disabled binding");
    Check(BestOfferMod.NormalizeAction(" Control + F11 ") == (true, "Ctrl+F11"), "Action normalization lost chord");
    Check(!BestOfferMod.NormalizeAction("not-a-key").Valid, "Unknown action key accepted");
    Check(!BestOfferMod.NormalizeAction("LeftControl").Valid, "Modifier-only action accepted");
    for (int disabled = 0; disabled < 16; disabled++)
    {
        var bindings = new[] { "F8", "F9", "Ctrl+F8", "Ctrl+F9" };
        for (int i = 0; i < 4; i++) if ((disabled & (1 << i)) != 0) bindings[i] = "";
        Check(KeyChord.ValidHotkeySet(bindings[0], bindings[1], bindings[2], bindings[3]), "Cleared hotkey combination rejected: " + disabled);
    }

    // All four Clear buttons use this production callback. The cleared key
    // must stay disabled, preserve other keys and cancel queued actions.
    for (int action = 1; action <= 4; action++)
    {
        mod = New(); var original = mod.Bindings;
        mod.BeginCapture(action); mod.ClearBinding(action);
        Check(mod.Bindings[action - 1] == "", "Clear left action bound: " + action);
        Check(mod.Bindings.Where((_, i) => i != action - 1).SequenceEqual(original.Where((_, i) => i != action - 1)), "Clear changed unrelated keys");
        Check(mod.Capturing == 0 && mod.Saves == 1 && mod.Syncs == 1, "Clear did not exit capture, save and sync exactly once");
        Next(.2f); mod.Poll(); mod.OpenForAudit();
        var code = action is 1 or 3 ? KeyCode.F8 : KeyCode.F9;
        mod.Gui(Key(code, ctrl: action >= 3)); Down(code, ctrl: action >= 3); mod.Poll(); Next(); mod.Poll();
        Check(mod.Toggles == 0 && mod.adapter.Captures == 0 && mod.Calculations.Count == 0, "Cleared shortcut still executed: " + action);
    }
    mod = New(); Down(KeyCode.F9, ctrl: true); mod.Poll(); mod.ClearBinding(4); Next(); mod.Poll();
    Check(mod.Calculations.Count == 0, "Clearing sale allowed an old queued sale");
    mod = New(); mod.SetBindings("", "", "Ctrl+F8", "Ctrl+F9"); mod.Repair();
    Check(mod.Bindings.SequenceEqual(new[] { "", "", "Ctrl+F8", "Ctrl+F9" }), "Migration re-enabled cleared panel/diagnostics or disabled valid actions");
    Next(.2f); mod.Gui(Key(KeyCode.F8, ctrl: true)); ExpectTrade(mod, false, "Placement with panel and diagnostic keys disabled", expectedSuppressions: 1);

    mod = New();

    mod.Gui(Key(KeyCode.F8));
    Check(mod.Toggles == 1 && mod.IsOpen && mod.Calculations.Count == 0, "Plain F8 must only open panel");
    Next(.2f); mod.Gui(Key(KeyCode.F9));
    Check(mod.adapter.Captures == 1 && mod.Calculations.Count == 0, "Plain F9 must only export diagnostics");
    Check(File.Exists(Path.Combine(auditDirectory, "BestOffer-capture-audit.json")), "Diagnostics boundary was not exercised");
    mod = New(); mod.Gui(Key(KeyCode.F9));
    Check(mod.adapter.Captures == 0 && mod.Calculations.Count == 0, "Closed-panel diagnostics must be ignored");
    mod = New();
    foreach (var code in new[] { KeyCode.F8, KeyCode.F9 })
    {
        var release = Key(code, ctrl: true); release.rawType = EventType.KeyUp; mod.Gui(release);
    }
    Check(mod.Calculations.Count == 0, "Key-up events triggered a trade");

    foreach (bool sell in new[] { false, true })
    {
        mod = New(); var evt = Key(sell ? KeyCode.F9 : KeyCode.F8, ctrl: true); mod.Gui(evt);
        ExpectTrade(mod, sell, "GUI default chord"); Check(evt.Used, "GUI trade key was not consumed");
        mod = New(); Down(sell ? KeyCode.F9 : KeyCode.F8, ctrl: true); mod.Poll();
        Check(mod.Calculations.Count == 0, "Polled trade action must wait for exact GUI modifiers");
        Next(); mod.Poll(); ExpectTrade(mod, sell, "Polled default chord");
        mod = New(); mod.OpenForAudit(); mod.Gui(Key(sell ? KeyCode.F9 : KeyCode.F8, ctrl: true));
        ExpectTrade(mod, sell, "Chord with panel open");
    }

    // A single key calculates the requested action. No precomputed plan or
    // second confirmation key is required, including for direct selling.
    mod = New(); mod.Gui(Key(KeyCode.F9, ctrl: true));
    Check(mod.Calculations.SequenceEqual(new[] { (true, true) }), "Sell chord did not request direct calculate-and-sell");

    foreach (bool sell in new[] { false, true })
    {
        var code = sell ? KeyCode.F9 : KeyCode.F8;
        mod = New(); mod.CalculationStartsWork = false; Down(code, ctrl: true); mod.Poll(); mod.Gui(Key(code, ctrl: true));
        Next(); mod.Poll(); ExpectTrade(mod, sell, "Poll then GUI duplicate");
        Check(mod.Notices == 1, "Immediate calculation failure must show exactly one notice");
        Down(code, ctrl: true); mod.Poll(); Next(); mod.Poll();
        Check(mod.Calculations.Count == 1, "Adjacent-frame duplicate was executed twice");
        Next(.2f); mod.Poll(); mod.Gui(Key(code, ctrl: true));
        Check(mod.Calculations.Count == 2, "Later distinct press was suppressed permanently");
        mod = New(); mod.CalculationStartsWork = false; mod.Gui(Key(code, ctrl: true)); Down(code, ctrl: true); mod.Poll();
        Next(); mod.Poll(); ExpectTrade(mod, sell, "GUI then poll duplicate");
    }

    // Extra modifiers reported by OnGUI must cancel an imprecise queued poll.
    mod = New(); Down(KeyCode.F8, ctrl: true); mod.Poll(); mod.Gui(Key(KeyCode.F8, ctrl: true, shift: true));
    Next(); mod.Poll(); Check(mod.Calculations.Count == 0 && mod.Toggles == 0, "Extra Shift failed to invalidate queued Ctrl+F8");
    mod = New(); mod.Gui(Key(KeyCode.F9, alt: true)); Next(); mod.Poll();
    Check(mod.Calculations.Count == 0 && mod.adapter.Captures == 0, "Unknown Alt+F9 fell through to sale/diagnostics");

    // Runtime guards are checked both before queueing and before executing.
    var guards = new (string Name, Action<BestOfferMod> Block)[]
    {
        ("background", _ => Application.isFocused = false),
        ("busy", m => m.Busy = true),
        ("native settings", m => m.WorkshopSettingsVisible = true),
        ("capturing another binding", m => m.BeginCapture(3)),
        ("outside shop", _ => EmporiumEntry.Instance = null),
        ("buyer not tradable", m => m.TradingReady = false),
        ("no trade UI", _ => NegociationUIManager.Instance = null),
        ("hidden trade UI", _ => NegociationUIManager.Instance!.uiObject!.activeInHierarchy = false),
        ("focused Unity input", _ => { var go = new GameObject(); go.Add(new UnityEngine.UI.InputField { isFocused = true }); EventSystem.current = new() { currentSelectedGameObject = go }; }),
        ("focused TMP input", _ => { var go = new GameObject(); go.Add(new TMPro.TMP_InputField { isFocused = true }); go.Add(new Il2CppTMPro.TMP_InputField { isFocused = true }); EventSystem.current = new() { currentSelectedGameObject = go }; }),
        ("escape menu", _ => Resources.Objects.Add(new EscapeUIManager { menuPanel = new() }))
    };
    foreach (var guard in guards)
    {
        foreach (bool sell in new[] { false, true })
        {
            var code = sell ? KeyCode.F9 : KeyCode.F8;
            mod = New(); guard.Block(mod); var evt = Key(code, ctrl: true); mod.Gui(evt);
            Check(mod.Calculations.Count == 0, guard.Name + ": GUI traded while blocked");
            // A GUI key in capture mode can legitimately finish rebinding;
            // use a fresh blocked context when checking the polling path.
            mod = New(); guard.Block(mod);
            Down(code, ctrl: true); mod.Poll(); Next(); mod.Poll();
            Check(mod.Calculations.Count == 0, guard.Name + ": polling traded while blocked");
            mod = New(); Down(code, ctrl: true); mod.Poll(); guard.Block(mod); Next(); mod.Poll();
            Check(mod.Calculations.Count == 0, guard.Name + ": stale pending traded after state changed");
        }
    }
    mod = New(); Application.isFocused = false; mod.Gui(Key(KeyCode.F8));
    Check(mod.Toggles == 0, "Background GUI toggled panel");
    mod.OpenForAudit(); mod.Gui(Key(KeyCode.F9)); Check(mod.adapter.Captures == 0, "Background GUI exported audit");
    mod = New(); var selected = new GameObject(); selected.Add(new UnityEngine.UI.InputField { isFocused = false });
    EventSystem.current = new() { currentSelectedGameObject = selected }; mod.Gui(Key(KeyCode.F8, ctrl: true));
    ExpectTrade(mod, false, "Selected but unfocused input");
    mod = New(); Resources.Objects.Add(new EscapeUIManager { menuPanel = new() { activeInHierarchy = false } });
    mod.Gui(Key(KeyCode.F8, ctrl: true)); ExpectTrade(mod, false, "Inactive escape menu");

    // Holding the physical main key must never turn OS key-repeat into a
    // second trade, even after work finishes and the debounce period expires.
    foreach (bool sell in new[] { false, true })
    {
        var code = sell ? KeyCode.F9 : KeyCode.F8;
        mod = New(); mod.CalculationStartsWork = false;
        Down(code, ctrl: true); Input.Held.Add(code); mod.Gui(Key(code, ctrl: true));
        for (int repeat = 0; repeat < 5; repeat++)
        {
            Time.frameCount++; Time.unscaledTime += .3f;
            mod.Poll(); mod.Gui(Key(code, ctrl: true));
        }
        Check(mod.Calculations.Count == 1, "Held key repeat performed another trade");
        Next(.2f); mod.Poll(); // a frame with no held key and no key-down releases the latch
        mod.Gui(Key(code, ctrl: true));
        Check(mod.Calculations.Count == 2, "Physical release did not allow next deliberate trade");
    }
    mod = New(); Down(KeyCode.F8, ctrl: true); mod.Poll();
    NegociationUIManager.Instance!.uiObject!.activeInHierarchy = false; Next(); mod.Poll();
    NegociationUIManager.Instance.uiObject.activeInHierarchy = true; Next(.2f); mod.Poll();
    Check(mod.Calculations.Count == 0, "Pending placement survived leaving and returning to trade UI");
    mod = New(); Down(KeyCode.F9, ctrl: true); mod.Poll();
    Application.isFocused = false; Next(); mod.Poll(); Application.isFocused = true; Next(.2f); mod.Poll();
    Check(mod.Calculations.Count == 0, "Pending sale survived application losing focus");

    mod = New(); mod.SetBindings("F8", "F9", "", "");
    mod.Gui(Key(KeyCode.F8, ctrl: true)); mod.Gui(Key(KeyCode.F9, ctrl: true)); Down(KeyCode.F8, ctrl: true); mod.Poll(); Next(); mod.Poll();
    Check(mod.Calculations.Count == 0, "Disabled actions still triggered");
    mod = New(); mod.SetBindings("Ctrl+F8", "F9", "Ctrl+F8", "Ctrl+F9"); mod.Repair();
    Check(mod.Bindings.SequenceEqual(new[] { "Ctrl+F8", "F9", "", "Ctrl+F9" }), "Migration overwrote old custom panel or disabled unrelated sale");
    mod = New(); mod.SetBindings("F8", "Ctrl+F9", "Ctrl+F8", "Ctrl+F9"); mod.Repair();
    Check(mod.Bindings.SequenceEqual(new[] { "F8", "Ctrl+F9", "Ctrl+F8", "" }), "Migration overwrote old diagnostics or disabled unrelated placement");

    // Capture each of the four production fields and reject conflicts without
    // committing either JSON settings or the native config mirror.
    for (int action = 1; action <= 4; action++)
    {
        mod = New(); var original = mod.Bindings; mod.BeginCapture(action); var evt = Key(KeyCode.F11, alt: true); mod.Gui(evt);
        Check(mod.Bindings[action - 1] == "Alt+F11", "Capture updated wrong action " + action);
        Check(mod.Bindings.Where((_, i) => i != action - 1).SequenceEqual(original.Where((_, i) => i != action - 1)), "Capture changed unrelated bindings");
        Check(mod.Capturing == 0 && mod.Saves == 1 && mod.Syncs == 1 && evt.Used, "Capture did not commit once");
        Check(mod.Calculations.Count == 0 && mod.Toggles == 0, "Captured key also executed an action");
    }
    mod = New(); mod.BeginCapture(3); mod.Gui(Key(KeyCode.F9));
    Check(mod.Capturing == 3 && mod.Bindings[2] == "Ctrl+F8" && mod.Saves == 0, "Duplicate capture changed binding or exited capture");
    Check(mod.Status.Length > 0 && mod.adapter.Captures == 0, "Duplicate capture fell through to audit");
    mod.Gui(Key(KeyCode.Escape)); Check(mod.Capturing == 0 && mod.Saves == 0, "Escape must cancel without saving");
    mod = New(); mod.BeginCapture(4); Down(KeyCode.F12, shift: true); mod.CapturePoll();
    Check(mod.Bindings[3] == "Ctrl+F9" && mod.Capturing == 4, "Polled capture committed before exact GUI event");
    Next(); mod.CapturePoll(); Check(mod.Bindings[3] == "Shift+F12" && mod.Saves == 1, "Polled-only capture failed");
    mod = New(); Down(KeyCode.F8, ctrl: true); mod.Poll(); mod.BeginCapture(3); mod.Gui(Key(KeyCode.F11));
    Next(); mod.Poll(); Check(mod.Calculations.Count == 0, "Old pending placement survived rebinding");
    Next(.2f); mod.Gui(Key(KeyCode.F8, ctrl: true)); Check(mod.Calculations.Count == 0, "Old placement chord remained active");
    mod.Gui(Key(KeyCode.F11)); ExpectTrade(mod, false, "Rebound placement", expectedSuppressions: 2);

    Console.WriteLine($"PASS: {checks} checks across {scenarios} isolated trade-hotkey scenarios; generated action/capture code tested, no Unity/game/Steam process executed.");
}
finally
{
    // This directory is generated by this test and is never a game directory.
    if (auditDirectory.StartsWith(fixtureBase, StringComparison.OrdinalIgnoreCase) &&
        Path.GetFileName(auditDirectory).StartsWith("BestOffer-trade-hotkeys-", StringComparison.Ordinal))
        Directory.Delete(auditDirectory, recursive: true);
}
