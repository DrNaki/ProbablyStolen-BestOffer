using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace DeepSpaceBestOffer;

// Keep the game's existing scroll view and full permission information. Only
// the selected Best Offer card is resized; restore before another card is shown.
sealed class WorkshopModMenuUI : MonoBehaviour
{
    internal static WorkshopModMenuUI? Instance { get; private set; }
    sealed class HeightState
    {
        internal readonly LayoutElement Layout;
        internal readonly float Minimum, Preferred, Flexible;
        internal HeightState(LayoutElement layout)
        { Layout = layout; Minimum = layout.minHeight; Preferred = layout.preferredHeight; Flexible = layout.flexibleHeight; }
        internal void Restore()
        {
            if (Layout == null) return;
            Layout.minHeight = Minimum; Layout.preferredHeight = Preferred; Layout.flexibleHeight = Flexible;
        }
    }

    CustomUIWindow? window;
    CustomUIElement? previewPanel, preview, description;
    ScrollRect? scroll;
    RectTransform? detailColumn;
    HeightState? panelHeight, imageHeight, scrollHeight;
    bool dirty;
    Vector2 lastSize;
    float lastOtherHeight;
    string lastText = "";

    void Awake() => Instance = this;
    void OnDisable() => Restore();
    void OnDestroy() { Restore(); if (Instance == this) Instance = null; }

    internal void Restore()
    {
        panelHeight?.Restore(); imageHeight?.Restore(); scrollHeight?.Restore();
        if (detailColumn != null) LayoutRebuilder.MarkLayoutForRebuild(detailColumn);
        window = null; previewPanel = preview = description = null;
        scroll = null; detailColumn = null;
        panelHeight = imageHeight = scrollHeight = null;
        dirty = false;
    }

    internal void BeforePreview()
    {
        // The original method recomputes the image's preferred height. Let it
        // operate on native values, including after an async Workshop download.
        panelHeight?.Restore(); imageHeight?.Restore();
    }

    internal void Observe(CustomUIWindow handle, bool previewChanged)
    {
        if (!isActiveAndEnabled || handle == null || !handle.IsAlive) return;
        if (window != handle)
        {
            Restore();
            window = handle;
            previewPanel = handle.Get("preview_panel"); preview = handle.Get("preview");
            description = handle.Get("detail_description");
            if (previewPanel?.root == null || preview?.root == null || description?.root == null) { Restore(); return; }
            detailColumn = previewPanel.Rect.parent as RectTransform;
            scroll = description.root.GetComponentInParent<ScrollRect>(true);
            if (scroll == null || detailColumn == null) { Restore(); return; }
            var scrollLayout = scroll.GetComponent<LayoutElement>();
            if (scrollLayout == null) { Restore(); return; }
            scrollHeight = new HeightState(scrollLayout);
        }
        if (panelHeight == null || previewChanged)
        {
            var panelLayout = previewPanel!.root.GetComponent<LayoutElement>();
            var imageLayout = preview!.root.GetComponent<LayoutElement>();
            if (panelLayout == null || imageLayout == null) { Restore(); return; }
            panelHeight = new HeightState(panelLayout); imageHeight = new HeightState(imageLayout);
        }
        dirty = true;
    }

    float OtherHeight()
    {
        if (detailColumn == null || scroll == null || previewPanel == null) return 0;
        var group = detailColumn.GetComponent<VerticalLayoutGroup>();
        float height = group != null ? group.padding.vertical : 0;
        int count = 0;
        foreach (Transform child in detailColumn)
        {
            if (!child.gameObject.activeSelf) continue;
            count++;
            if (child == previewPanel.Rect || child == scroll.transform) continue;
            if (child is RectTransform rect) height += LayoutUtility.GetPreferredHeight(rect);
        }
        return height + (group != null ? group.spacing * Mathf.Max(0, count - 1) : 0);
    }

    void LateUpdate()
    {
        if (window == null) return;
        if (!window.IsAlive || !window.IsVisible || detailColumn == null ||
            !detailColumn.gameObject.activeInHierarchy || scroll == null) { Restore(); return; }
        var size = detailColumn.rect.size;
        float other = OtherHeight();
        string text = description?.text?.text ?? "";
        if (!dirty && Vector2.Distance(size, lastSize) < .5f && Mathf.Abs(other - lastOtherHeight) < .5f && text == lastText) return;
        Canvas.ForceUpdateCanvases();
        other = OtherHeight();
        float available = detailColumn.rect.height - other;
        if (available <= 0 || panelHeight == null || imageHeight == null || scrollHeight == null) return;
        float minimumScroll = Mathf.Min(180, Mathf.Max(48, available - 64));
        float cover = Mathf.Clamp(available - minimumScroll, 32, Mathf.Max(32, Mathf.Min(220, panelHeight.Preferred)));
        bool hasCover = previewPanel!.root.activeSelf;
        if (hasCover)
        {
            panelHeight.Layout.minHeight = panelHeight.Layout.preferredHeight = cover;
            panelHeight.Layout.flexibleHeight = 0;
            imageHeight.Layout.minHeight = imageHeight.Layout.preferredHeight = Mathf.Max(1, cover - 16);
            imageHeight.Layout.flexibleHeight = 0;
        }
        scrollHeight.Layout.minHeight = minimumScroll;
        scrollHeight.Layout.preferredHeight = -1;
        scrollHeight.Layout.flexibleHeight = 1;
        LayoutRebuilder.ForceRebuildLayoutImmediate(detailColumn);
        LayoutRebuilder.ForceRebuildLayoutImmediate(scroll.content);
        Canvas.ForceUpdateCanvases();
        lastSize = detailColumn.rect.size; lastOtherHeight = OtherHeight(); lastText = text; dirty = false;
    }
}

[HarmonyPatch(typeof(ModUIManager), "ShowMod")]
static class BestOfferModCardPatch
{
    static void Prefix() => WorkshopModMenuUI.Instance?.Restore();
    static void Postfix(LoadedMod mod, CustomUIWindow ___handle)
    {
        if (mod?.Manifest?.ID == BestOfferMod.WorkshopModId)
            WorkshopModMenuUI.Instance?.Observe(___handle, false);
    }
}

[HarmonyPatch(typeof(ModUIManager), "ApplyPreview")]
static class BestOfferModPreviewPatch
{
    static void Prefix(LoadedMod mod)
    {
        if (mod?.Manifest?.ID == BestOfferMod.WorkshopModId) WorkshopModMenuUI.Instance?.BeforePreview();
    }
    static void Postfix(LoadedMod mod, CustomUIWindow ___handle)
    {
        if (mod?.Manifest?.ID == BestOfferMod.WorkshopModId)
            WorkshopModMenuUI.Instance?.Observe(___handle, true);
    }
}
