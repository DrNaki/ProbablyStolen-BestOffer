"""Materialize the 1.6.1 source for Unity's native Mono loader.

The Nexus sources and binary are untouched. Platform-independent algorithms are
copied verbatim; changes below adapt runtime, identity, JSON and loader bindings.
"""
from pathlib import Path
import re
import hashlib
import json

root = Path(__file__).resolve().parent.parent
source = root / 'src' / 'BestOffer'
target = root / 'src' / 'BestOffer.Workshop' / 'Generated'
target.mkdir(parents=True, exist_ok=True)
hashes = {}
def without_method(text, signature):
    start = text.index(signature)
    opening = text.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[:start] + text[end:]

for path in source.glob('*.cs'):
    if path.name == 'LocalModDetails.cs':
        continue
    original = path.read_text(encoding='utf-8-sig')
    hashes[path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    text = original.replace('using Il2Cpp;\n', '').replace('using Il2CppTMPro;', 'using TMPro;').replace('Il2CppTMPro.', 'TMPro.')
    text = text.replace('using Il2CppInterop.Runtime.InteropTypes.Arrays;\n', '')
    text = text.replace('using MelonLoader;\n', '').replace('using MelonLoader.Utils;\n', '')
    text = text.replace('Il2CppSystem.Collections.Generic.', 'System.Collections.Generic.')
    text = text.replace('Il2Cpp.ItemFeature', 'ItemFeature')
    text = text.replace('Il2CppSystem.Action<', 'System.Action<')
    text = text.replace('Il2CppReferenceArray<Il2CppSystem.Object>', 'object[]')
    text = text.replace('Il2CppSystem.Object', 'object').replace('Il2CppSystem.IFormatProvider', 'System.IFormatProvider')
    text = text.replace('Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase', 'object')
    text = re.sub(r'(\w+(?:\.\w+)*)\.Cast<GraphNodeStorage>\(\)', r'((GraphNodeStorage)\1)', text)
    text = text.replace('textLocale.Identifier.CultureInfo?.Cast<System.IFormatProvider>()', 'textLocale.Identifier.CultureInfo')
    text = text.replace('arguments?.Cast<System.Collections.Generic.IList<object>>()', 'arguments')
    text = re.sub(r'\.Pointer\b', '.NativeId()', text)
    text = text.replace('MelonLoader.Utils.MelonEnvironment.UserDataDirectory', 'WorkshopRuntime.UserDataDirectory')
    text = text.replace('MelonEnvironment.UserDataDirectory', 'WorkshopRuntime.UserDataDirectory')
    text = text.replace('System.Text.Json.Serialization.JsonIgnore', 'Newtonsoft.Json.JsonIgnore')
    text = text.replace('System.Text.Json.JsonSerializer', 'WorkshopJson')
    text = text.replace('System.Text.Json.JsonSerializerOptions', 'WorkshopJsonOptions')
    text = text.replace('WorkshopJsonOptions', 'WorkshopJsonOptions')
    text = re.sub(r'(?<![\w.])Directory\.', 'System.IO.Directory.', text)
    text = text.replace("text.Split('+', StringSplitOptions.TrimEntries)", "text.Split('+').Select(value => value.Trim())")
    text = text.replace('neg.acceptOfferButtonButtonTMP?.font ?? neg.titleTMP?.font', 'neg.uiObject?.GetComponentInChildren<TextMeshProUGUI>(true)?.font')
    text = text.replace('neg?.acceptOfferButtonButtonTMP?.font ?? neg?.titleTMP?.font', 'neg?.uiObject?.GetComponentInChildren<TextMeshProUGUI>(true)?.font')
    text = text.replace('if (nativeButtonSprite == null) nativeButtonSprite = neg.acceptOfferButtonButton?.GetComponent<Image>()?.sprite;', 'ResolveLegacyArtwork();')
    text = text.replace('nativeTabletSprite = neg.uiObject.GetComponent<Image>()?.sprite;', '// Original artwork is resolved by name; release-tablet art has a different slicing layout.')
    text = text.replace('                if (nativeTabletSprite == null && image?.sprite != null && rt != null && rt.sizeDelta.x > 500 && rt.sizeDelta.y > 300)\n                    nativeTabletSprite = image.sprite;\n', '')
    text = text.replace('nativeTabletSprite = nativeButtonSprite = null;', 'nativeTabletSprite = nativeButtonSprite = null; legacyArtworkResolved = false;')
    text = text.replace('NegociationUIManager.PlayAcceptDealDialogueOnce(transactionClient)', 'PlayNativeDealDialogueOnce(transactionClient)')
    text = text.replace('ui.UpdateAcceptOfferButtonForSell();', 'ui.RefreshUI();')
    text = text.replace('public sealed class GameTradeAdapter', 'public sealed partial class GameTradeAdapter')
    text = text.replace('Passed = passed, baseline.CanTrade,',
                        'GameVersion = UnityEngine.Application.version, ModVersion = "1.7.3",\n'
                        '            NativeCategories = NativeCategoryPolicy.Categories().Select(c => new { c.Id, c.Name, HasIcon = c.HasDemandException || CategoryIconCatalog.Icons.ContainsKey(c.Id) }),\n'
                        '            Passed = passed, baseline.CanTrade,')
    text = text.replace('1.6.1', '1.7.3')
    # Palette and drawn controls now come from the release game's TabletStyle.
    text = text.replace('new Color(.93f, .85f, .65f)', 'InputPaper')
    text = text.replace('new Color(.88f, .81f, .60f)', 'CardPaper')
    text = text.replace('new Color(.96f, .89f, .70f)', 'CardPaper')
    text = text.replace('new Color(.99f, .94f, .77f)', 'TabletStyle.TAG_INTEREST_TEXT')
    text = text.replace('filter == f ? Rust : Muted', 'filter == f ? Ink : Muted')
    text = text.replace('index == selected ? Rust : Muted', 'index == selected ? Green : Muted')
    text = text.replace('captureHotkey == 1 ? Rust : Muted', 'captureHotkey == 1 ? Ink : Muted')
    text = text.replace('captureHotkey == 2 ? Rust : Muted', 'captureHotkey == 2 ? Ink : Muted')
    text = text.replace('new Color(.09f, .06f, .08f,', 'new Color(.10f, .12f, .16f,')
    text = text.replace('using System.Text.Json;', 'using JsonSerializer = DeepSpaceBestOffer.WorkshopJson;\nusing JsonSerializerOptions = DeepSpaceBestOffer.WorkshopJsonOptions;')
    # Unity Mono implements .NET Standard 2.1 but not this newer File.Move overload.
    text = text.replace('File.Move(temporary, categoryRulePath, true);', 'WorkshopRuntime.ReplaceFile(temporary, categoryRulePath);')
    if path.name == 'ManagerIntegration.cs':
        tail = text[text.index('    internal static bool CaptureFunctionHotkey'):]
        text = ('using UnityEngine;\nusing DeepSpaceBestOffer;\nnamespace DeepSpaceModSupport;\n'
                'internal static class ManagerIntegration\n{\n'
                '    internal static bool Visible => BestOfferMod.Current?.WorkshopSettingsVisible == true;\n'
                '    internal static bool Hide() => BestOfferMod.Current?.HideWorkshopSettings() ?? true;\n' + tail)
    elif path.name == 'ManagedSettings.cs':
        text = re.sub(r'    MelonPreferences_Category\?.*?    string pendingCapturedHotkey', '    string pendingCapturedHotkey', text, flags=re.S)
        start = text.index('    void InitializeLocalSettings()')
        end = text.index('    void CaptureLocalHotkey()', start)
        text = text[:start] + ('    // Native ModConfig integration is implemented in WorkshopSettings.cs.\n') + text[end:]
        text = text.replace('public override void OnGUI()', 'void OnGUI()')
    elif path.name == 'BestOfferMod.cs':
        text = re.sub(r'^\[assembly: Melon.*?\]\n', '', text, flags=re.M)
        text = text.replace(': MelonMod', ': MonoBehaviour')
        text = text.replace('public override void OnInitializeMelon()', 'internal void Initialize()')
        text = text.replace('public override void OnUpdate()', 'void Update()')
        text = text.replace('public override void OnSceneWasLoaded(int buildIndex, string sceneName)', 'internal void SceneLoaded(int buildIndex, string sceneName)')
        text = text.replace('public override void OnDeinitializeMelon()', 'void OnDestroy()')
    elif path.name == 'NativeTradeUI.cs':
        text = text.replace('    void DestroyNativeUI()\n    {', '    void DestroyNativeUI()\n    {\n        DestroyWorkshopFonts();')
        text = text.replace('    Sprite? nativeButtonSprite, nativeTabletSprite;\n', '')
        text = text.replace('nativeTabletSprite = nativeButtonSprite = null; ', '')
        text = without_method(text, '    void RebuildNativePage()')
        text = without_method(text, '    Button MakeButton(')
        text = without_method(text, '    TextMeshProUGUI MakeText(')
        text = re.sub(r'    static readonly Color Ink = .*?;\n    static readonly Color Rust = .*?;\n    static readonly Color Green = .*?;',
                      '    static readonly Color Ink = TabletStyle.INK, Paper = TabletStyle.SCREEN;\n'
                      '    static readonly Color Rust = TabletStyle.RED, Muted = TabletStyle.GREY_FACE;\n'
                      '    static readonly Color Green = TabletStyle.GREEN, Line = TabletStyle.ROW_SEPARATOR;', text, flags=re.S)
        text = text.replace('ResolveLegacyArtwork();', 'ResolveTabletSkin(neg);')
        text = text.replace('legacyArtworkResolved = false;', 'tabletSkin = null;')
        text = text.replace('        var textObject = RectObject("Value", go.transform,',
                            '        TabletFrameImage(go.transform, 0, 0, w, h, Ink);\n        var textObject = RectObject("Value", go.transform,')
        text = text.replace('        var textObject = RectObject("Quantity", go.transform,',
                            '        TabletFrameImage(go.transform, 0, 0, w, h, Ink);\n        var textObject = RectObject("Quantity", go.transform,')
        text = text.replace('        var label = RectObject("Days", go.transform,',
                            '        TabletFrameImage(go.transform, 0, 0, w, h, Ink);\n        var label = RectObject("Days", go.transform,')
        text = text.replace('MakeText(root, left, 227, column, 30, L.T("报价货源"), 21, Rust);',
                            'MakeSectionTitle(root, left, 227, column, L.T("报价货源"));')
        text = text.replace('MakeText(root, right, 227, column, 30, L.T("当天行情"), 21, Rust);',
                            'MakeSectionTitle(root, right, 227, column, L.T("当天行情"));')
    elif path.name == 'CategoryIconCatalog.cs':
        anchor = '        ["type:SEED"] = new("Items/items_hydroponic_plant", "nutrifruit_seed_packet"),'
        assert anchor in text
        text = text.replace(anchor, anchor + '\n        ["type:MODIFIED_SEED"] = new("Items/items_hydroponic_plant", "nutrifruit_modified_seed"),')
    (target / path.name).write_text(text, encoding='utf-8')
(target / 'source-hashes.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
print(f'Prepared {len(hashes)} source files in {target}')
