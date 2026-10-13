using DeepSpaceModSupport;

int assertions = 0;
void Check(bool condition) { assertions++; if (!condition) throw new Exception("Hotkey test failed at assertion " + assertions); }
foreach (var sample in new[] { ("F8", "F8", false, false, false), ("shift+F7", "Shift+F7", false, false, true),
    (" Alt + Control + F6 ", "Ctrl+Alt+F6", true, true, false), ("Shift+Ctrl+Alt+Equals", "Ctrl+Alt+Shift+Equals", true, true, true) })
{
    Check(KeyChord.TryParse(sample.Item1, out var chord));
    Check(chord.ToString() == sample.Item2);
    Check(chord.Ctrl == sample.Item3 && chord.Alt == sample.Item4 && chord.Shift == sample.Item5);
    Check(KeyChord.TryParse(chord.ToString(), out var again) && again == chord);
}
foreach (string? invalid in new string?[] { null, "", " ", "Shift", "Ctrl+Shift", "Ctrl+Ctrl+F7", "Control+Ctrl+F7", "F7+F8", "F7+", "+F7", "Ctrl++F8" })
    Check(!KeyChord.TryParse(invalid, out _));
Console.WriteLine($"PASS: {assertions} hotkey parsing/roundtrip assertions.");
