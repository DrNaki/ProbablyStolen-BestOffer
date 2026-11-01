using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod
{
    TabletSkin? tabletSkin;
    readonly Dictionary<TMP_FontAsset, TMP_FontAsset> workshopFonts = new();
    static readonly Color Shell = TabletStyle.Hex("a9ada2");
    static readonly Color InputPaper = Color.Lerp(TabletStyle.SCREEN, Color.white, .30f);
    static readonly Color CardPaper = Color.Lerp(TabletStyle.SCREEN, Color.white, .12f);

    TMP_FontAsset? WorkshopEnglishFont(bool heading)
    {
        var source = (heading ? tabletSkin?.fontLubrifont : tabletSkin?.fontNotoRegular) ?? nativeFont;
        if (source == null) return null;
        if (workshopFonts.TryGetValue(source, out var owned) && owned != null) return owned;
        owned = UnityEngine.Object.Instantiate(source);
        owned.name = "BestOffer.ReleaseFont." + source.name;
        owned.fallbackFontAssetTable = source.fallbackFontAssetTable == null
            ? new List<TMP_FontAsset>() : new List<TMP_FontAsset>(source.fallbackFontAssetTable);
        var chinese = FontLoader.Instance?.simplifiedChineseFont;
        if (chinese != null && chinese != source && !owned.fallbackFontAssetTable.Contains(chinese))
            owned.fallbackFontAssetTable.Add(chinese);
        workshopFonts[source] = owned;
        return owned;
    }

    void DestroyWorkshopFonts()
    {
        foreach (var font in workshopFonts.Values) if (font != null) UnityEngine.Object.Destroy(font);
        workshopFonts.Clear();
    }

    void ResolveTabletSkin(NegociationUIManager negotiation)
    {
        if (tabletSkin == null && negotiation.uiObject != null)
            tabletSkin = negotiation.uiObject.GetComponent<TabletSkin>();
    }

    Image TabletFrameImage(Transform parent, float x, float y, float width, float height,
        Color color, bool notched = false)
    {
        var image = RectObject("TabletFrame", parent, x, y, width, height).AddComponent<Image>();
        image.sprite = notched ? tabletSkin?.frameNotched : tabletSkin?.frame2;
        image.type = Image.Type.Sliced;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    void MakeSectionTitle(Transform root, float x, float y, float width, string title)
    {
        MakeBox(root, x, y, width, 30, Ink);
        MakeText(root, x + 12, y + 2, width - 24, 26, title, 20, Paper)
            .verticalAlignment = VerticalAlignmentOptions.Middle;
    }

    void RebuildNativePage()
    {
        if (blocker == null) return;
        ResetSettingTooltip(); checkedButtonLabels.Clear();
        if (nativePage != null) { nativePage.SetActive(false); UnityEngine.Object.Destroy(nativePage); }
        float pageHeight = uiTab == 1 ? CategoryPageHeight : NativeHeight;
        nativePage = RectObject("BestOffer.ReleaseTablet", blocker.transform, 0, 0, NativeWidth, pageHeight);
        nativePage.SetActive(false);
        var rect = nativePage.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = Vector2.zero;
        var root = nativePage.transform;

        // Rebuild the expanded tablet with the release skin's actual sliced
        // frame pieces. Its narrow negotiation artwork is never stretched.
        nativePage.AddComponent<Image>().color = Shell;
        TabletFrameImage(root, 0, 0, NativeWidth, pageHeight, Ink, true);
        MakeBox(root, 24, 42, 1072, pageHeight - 64, Paper);
        TabletFrameImage(root, 24, 42, 1072, pageHeight - 64, Ink, true);
        MakeText(root, 36, 7, 650, 28, L.T("最佳报价"), 22, Ink);
        MakeText(root, 828, 9, 200, 24, "BEST OFFER  ·  1.7.1", 12, Ink, TextAlignmentOptions.Right);
        MakeButton(root, 1044, 6, 32, 28, "×", Close, Muted);

        string buyer = snapshot == null || string.IsNullOrEmpty(snapshot.ClientKey)
            ? L.T("等待买家  ·  可先设置库存规则") : snapshot.ClientName;
        MakeText(root, 48, 56, 690, 34, buyer, 24, Ink);
        string context = snapshot?.CanTrade == true
            ? L.T("商店终端  /  自动选货") + (snapshot.RequireVariety ? L.T("  /  每种限一件") : "")
            : snapshot?.Message ?? L.T("等待买家到来；现在可以设置售卖规则。");
        MakeText(root, 49, 93, 1000, 22, context, 13, Muted);
        if (snapshot?.CanTrade == true)
            MakeText(root, 760, 61, 312, 30, snapshot.Budget.ToString("N0") + " " + L.T("信用点"), 24, Green, TextAlignmentOptions.Right);

        string[] tabs = { L.T("库存规则"), L.T("分类规则"), L.T("报价方案"), L.T("交易设置") };
        for (int n = 0; n < tabs.Length; n++)
        {
            int tab = n;
            var button = MakeButton(root, 48 + n * 258, 124, 250, 36, tabs[n],
                () => { uiTab = tab; uiDirty = true; }, uiTab == n ? Ink : Muted);
            if (uiTab == n) MakeBox(button.transform, 6, 30, 238, 3, Green).GetComponent<Image>().raycastTarget = false;
        }
        MakeBox(root, 48, 174, 1024, 1, Line);
        if (uiTab == 0) BuildInventoryPage(root);
        else if (uiTab == 1) BuildCategoryRulesPage(root);
        else if (uiTab == 2) BuildPlansPage(root);
        else BuildSettingsPage(root);

        var footer = MakeBox(root, 48, pageHeight - 97, 1024, 1, Line);
        nativeStatus = MakeText(root, 48, pageHeight - 86, 806, 46, status, 13, Ink);
        var help = MakeText(root, 874, pageHeight - 73, 198, 22, L.T("Esc 返回交易  ·  1.7.1"), 12, Muted, TextAlignmentOptions.Right);
        bool showFooter = uiTab != 1 || !categoryPickerOpen;
        footer.SetActive(showFooter); nativeStatus.gameObject.SetActive(showFooter); help.gameObject.SetActive(showFooter);
        if (uiTab == 1 && categoryPickerOpen) BuildCategoryPicker(root);
        nativePage.SetActive(true); blocker.SetActive(IsOpen); LayoutCheckedButtonLabels();
    }

    Button MakeButton(Transform parent, float x, float y, float width, float height,
        string title, Action action, Color color, bool enabled = true)
    {
        var root = MakeBox(parent, x, y, width, height, color);
        root.name = "TabletButton." + title;
        var image = root.GetComponent<Image>();
        var button = root.AddComponent<Button>(); button.targetGraphic = image;
        // Match the release's double-line button construction. The outer and
        // inner frames only draw borders; the root supplies the intent colour.
        if (color.a >= .9f)
        {
            TabletFrameImage(root.transform, 0, 0, width, height, Paper);
            TabletFrameImage(root.transform, 4, 4, width - 8, height - 8, Paper);
        }
        var colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f);
        colors.pressedColor = new Color(.72f, .72f, .72f); colors.selectedColor = Color.white;
        colors.disabledColor = Color.white;
        colors.fadeDuration = .08f; button.colors = colors;
        TabletUI.NoKeyboardFocus(button); button.interactable = enabled;
        var group = root.AddComponent<CanvasGroup>();
        group.alpha = enabled ? 1 : TabletStyle.SMALL_BUTTON_DISABLED_ALPHA;
        var state = root.AddComponent<WorkshopButtonState>(); state.Button = button; state.Group = group;
        if (!L.Chinese && title.Contains('✓')) MakeCheckedButtonLabel(root.transform, width, height, title);
        else MakeText(root.transform, 7, 2, width - 14, height - 4, title, height > 45 ? 16 : 14, Paper, TextAlignmentOptions.Center);
        button.onClick.AddListener((UnityAction)(() =>
        {
            SuppressTransitionInput();
            try { action(); }
            catch (Exception error) { status = L.T("操作失败：") + error.Message; LoggerInstance.Warning(error.ToString()); }
            uiDirty = true;
        }));
        return button;
    }

    TextMeshProUGUI MakeText(Transform parent, float x, float y, float width, float height,
        string content, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left)
    {
        var text = RectObject("Label", parent, x, y, width, height).AddComponent<TextMeshProUGUI>();
        // Match native roles for headings versus dense inventory rows, while
        // preserving the mod's explicit Chinese/English language contract.
        text.font = L.Chinese ? FontLoader.Instance?.simplifiedChineseFont : WorkshopEnglishFont(size >= 21);
        if (text.font == null) text.font = nativeFont;
        text.fontSize = size; text.color = color == Rust && size >= 20 ? Ink : color;
        text.text = content; text.alignment = alignment;
        text.enableAutoSizing = true; text.fontSizeMin = size * .78f; text.fontSizeMax = size;
        text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false; text.richText = false;
        return text;
    }
}

internal sealed class WorkshopButtonState : MonoBehaviour
{
    internal Button? Button;
    internal CanvasGroup? Group;
    void LateUpdate()
    {
        if (Button != null && Group != null)
            Group.alpha = Button.interactable ? 1 : TabletStyle.SMALL_BUTTON_DISABLED_ALPHA;
    }
}
