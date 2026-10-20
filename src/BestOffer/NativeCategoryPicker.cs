using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod
{
    readonly Dictionary<string, Sprite?> categoryIconSprites = new(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, Sprite>> categoryIconAtlases = new(StringComparer.Ordinal);
    readonly List<Sprite> fittedCategorySprites = new();

    Sprite? CategorySprite(string category)
    {
        if (categoryIconSprites.TryGetValue(category, out var cached)) return cached;
        if (!CategoryIconCatalog.Icons.TryGetValue(category, out var icon)) return null;
        if (!categoryIconAtlases.TryGetValue(icon.Atlas, out var atlas))
        {
            atlas = new(StringComparer.Ordinal);
            foreach (var sprite in Resources.LoadAll<Sprite>(icon.Atlas)) atlas[sprite.name] = sprite;
            categoryIconAtlases[icon.Atlas] = atlas;
        }
        atlas.TryGetValue(icon.Sprite, out var result);
        if (result != null) result = FitCategorySprite(result);
        categoryIconSprites[category] = result;
        if (result == null) LoggerInstance.Warning($"未找到分类图标：{category} / {icon.Atlas} / {icon.Sprite}");
        return result;
    }

    Sprite FitCategorySprite(Sprite source)
    {
        // Native inventory sprites have unequal transparent margins. Fit the
        // visible art, so preserveAspect centres the object rather than its cell.
        try
        {
            if (!source.texture.isReadable) return source;
            var rect = source.rect;
            int width = (int)rect.width, height = (int)rect.height;
            var pixels = source.texture.GetPixels((int)rect.x, (int)rect.y, width, height);
            int left = width, bottom = height, right = -1, top = -1;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * width + x].a <= .1f) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y);
            }
            if (right < left || (left == 0 && bottom == 0 && right == width - 1 && top == height - 1)) return source;
            var fitted = Sprite.Create(source.texture, new Rect(rect.x + left, rect.y + bottom, right - left + 1, top - bottom + 1),
                new Vector2(.5f, .5f), source.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            fitted.name = source.name; fittedCategorySprites.Add(fitted);
            return fitted;
        }
        catch (Exception e)
        {
            LoggerInstance.Warning($"分类图标对齐失败，保留原图：{source.name} / {e.Message}");
            return source;
        }
    }

    void MakeCategoryIcon(Transform parent, float x, float y, float size, string category)
    {
        var icon = RectObject("CategoryIcon:" + category, parent, x, y, size, size).AddComponent<Image>();
        // uGUI preserves the aspect ratio around the RectTransform pivot.
        // A top-left pivot would top-align wide icons and left-align tall ones.
        var rect = icon.GetComponent<RectTransform>();
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x + size / 2, -y - size / 2);
        icon.sprite = CategorySprite(category); icon.preserveAspect = true;
        icon.color = Color.white; icon.raycastTarget = false;
        if (icon.sprite == null) icon.enabled = false;
    }

    static Color CategoryColor(int mode) => mode == 1 ? Green : mode == 2 ? Rust : Muted;

    void BuildCategoryRulesPage(Transform root)
    {
        MakeText(root, 48, 188, 1024, 26, "分类规则优先于库存规则；多个分类冲突时禁卖优先。", 14, Ink);
        if (CurrentCategoryScope() == null)
        { MakeText(root, 48, 260, 1024, 60, "请进入店铺后设置分类规则。", 18, Muted); return; }
        int day = CategoryClock().Day;
        var all = NativeCategoryPolicy.Categories();
        var risks = all.Where(c => c.HasDemandException).ToArray();
        for (int n = 0; n < risks.Length; n++) BuildRiskCategory(root, 48 + n * 524, 224, risks[n], day);

        MakeText(root, 48, 316, 700, 25, "商品分类", 18, Ink);
        var selected = all.FirstOrDefault(c => !c.HasDemandException && c.Id == selectedCategoryId);
        int mode = selected == null ? 0 : GetCategoryRule(selected.Id).EffectiveMode(day);
        var selector = MakeButton(root, 48, 347, 1024, 58, selected?.Name ?? "选择商品分类", () =>
        { categoryPickerOpen = !categoryPickerOpen; }, CategoryColor(mode), !busy);
        selector.gameObject.name = "CategorySelector";
        var label = selector.GetComponentInChildren<TMP_Text>();
        label.alignment = TextAlignmentOptions.Left; label.fontSize = label.fontSizeMax = 18;
        var textRect = label.GetComponent<RectTransform>();
        textRect.anchoredPosition = new Vector2(selected == null ? 20 : 70, -2); textRect.sizeDelta = new Vector2(850, 54);
        if (selected != null) MakeCategoryIcon(selector.transform, 16, 9, 40, selected.Id);
        MakeText(selector.transform, 970, 12, 40, 34, categoryPickerOpen ? "▲" : "▼", 18, Paper, TextAlignmentOptions.Center);

        if (selected == null)
            MakeText(root, 72, 444, 976, 100, "从上方选择需要设置的商品分类。", 18, Muted, TextAlignmentOptions.Center);
        else BuildSelectedCategory(root, selected, day);
    }

    void BuildRiskCategory(Transform root, float x, float y, TradeCategory category, int day)
    {
        MakeBox(root, x, y, 500, 82, Line);
        MakeBox(root, x + 1, y + 1, 498, 80, new Color(.88f, .81f, .60f));
        var rule = GetCategoryRule(category.Id);
        int mode = rule.EffectiveMode(day);
        bool exception = mode == 2 && rule.AllowDemandException;
        MakeText(root, x + 16, y + 27, 216, 28, category.Name, 19, mode == 0 ? Ink : CategoryColor(mode));
        MakeButton(root, x + 248, y + 25, 110, 32, mode == 1 ? "✓ 必卖" : "必卖", () => ToggleCategoryMode(category, 1), mode == 1 ? Green : Muted, !busy);
        var forbidden = MakeButton(root, x + 370, y + 25, 114, 32, exception ? "✓ 需求例外" : mode == 2 ? "✓ 禁卖" : "禁卖",
            () => CycleCategoryForbidden(category), mode == 2 ? Rust : Muted, !busy);
        AddSettingTooltip(forbidden, root, "点击切换：关闭 → 禁卖 → 需求例外 → 关闭。\n需求例外：仅放行买家明确所需商品，仍遵循库存与拒收规则。", x + 16, y + 60, 468, 45);
    }

    void BuildSelectedCategory(Transform root, TradeCategory category, int day)
    {
        var rule = GetCategoryRule(category.Id);
        int mode = rule.EffectiveMode(day);
        var editor = MakeBox(root, 48, 425, 1024, 135, Line);
        editor.name = "CategoryRuleEditor";
        MakeBox(root, 49, 426, 1022, 133, new Color(.88f, .81f, .60f));
        MakeText(root, 72, 446, 180, 26, "售卖规则", 16, Ink);
        MakeButton(root, 280, 440, 136, 34, mode == 1 ? "✓ 必卖" : "必卖", () => ToggleCategoryMode(category, 1), mode == 1 ? Green : Muted, !busy);
        MakeButton(root, 432, 440, 136, 34, mode == 2 ? "✓ 禁卖" : "禁卖", () => ToggleCategoryMode(category, 2), mode == 2 ? Rust : Muted, !busy);
        MakeText(root, 600, 446, 448, 26, mode == 0 ? rule.IsExpired(day) ? "已到期 · 恢复库存规则" : "遵循库存规则" :
            mode == 1 ? "整类必卖（含新增商品）" : "整类禁卖（含新增商品）", 13, Muted);
        MakeBox(root, 72, 490, 976, 1, Line);
        MakeText(root, 72, 514, 180, 26, "生效期限", 16, Ink);
        var period = MakeButton(root, 280, 508, 136, 34, rule.Limited ? "✓ 启用" : "启用",
            () => ToggleCategoryPeriod(category), rule.Limited ? Green : Muted, !busy);
        period.gameObject.name = "CategoryPeriod";
        var expiry = MakeText(root, rule.Limited ? 672 : 440, 514, rule.Limited ? 376 : 608, 26,
            CategoryRuleLogic.PeriodDescription(rule, day), 13, Muted);
        if (rule.Limited)
        {
            int days = CategoryRuleLogic.PeriodDaysForEditing(rule, day);
            MakeButton(root, 440, 508, 30, 34, "−", () => ChangeCategoryPeriodDays(category, -1), Muted, !busy && days > 1);
            MakeCategoryDayField(root, 478, 508, 82, 34, category, rule, expiry);
            MakeButton(root, 568, 508, 30, 34, "+", () => ChangeCategoryPeriodDays(category, 1), Muted, !busy && days < CategoryRuleLogic.MaxDurationDays);
            MakeText(root, 610, 514, 50, 26, "天", 14, Ink);
        }
        AddSettingTooltip(period, root, "剩余天数含今天，修改后从今天重新计时。\n到期恢复库存规则，并在夜间报告末尾提醒。", 280, 546, 760, 45);
    }

    void SelectCategory(string id)
    {
        if (!NativeCategoryPolicy.Categories().Any(c => !c.HasDemandException && c.Id == id)) return;
        selectedCategoryId = id; categoryPickerOpen = false;
    }

    void BuildCategoryPicker(Transform root)
    {
        var outside = MakeButton(root, 0, 0, NativeWidth, CategoryPageHeight, "", () => { categoryPickerOpen = false; }, new Color(.09f, .06f, .08f, .48f));
        outside.gameObject.name = "CategoryPickerOutside";
        var outsideImage = outside.GetComponent<Image>(); outsideImage.sprite = null; outsideImage.type = Image.Type.Simple;
        outside.transition = Selectable.Transition.None;
        MakeBox(root, 48, 184, 1024, 444, Line);
        MakeBox(root, 50, 186, 1020, 440, Paper);
        MakeText(root, 64, 194, 400, 28, "选择商品分类", 20, Ink);
        string[] states = { "未设置", "必卖", "禁卖" };
        for (int n = 0; n < states.Length; n++)
        {
            float x = 638 + n * 114;
            MakeBox(root, x, 201, 14, 14, CategoryColor(n));
            MakeText(root, x + 22, 195, 88, 26, states[n], 13, Ink);
        }
        MakeButton(root, 1016, 194, 40, 28, "×", () => { categoryPickerOpen = false; }, Muted);

        var categories = NativeCategoryPolicy.Categories().Where(c => !c.HasDemandException).ToArray();
        int day = CategoryClock().Day;
        const int columns = 6;
        var viewport = RectObject("CategoryGrid", root, 64, 234, 994, 388);
        viewport.AddComponent<RectMask2D>();
        float height = Math.Max(388, (categories.Length + columns - 1) / columns * 39 - 3);
        var content = RectObject("CategoryGridContent", viewport.transform, 0, 0, 994, height);
        for (int n = 0; n < categories.Length; n++)
        {
            var category = categories[n];
            float x = n % columns * 167, y = n / columns * 39;
            int mode = GetCategoryRule(category.Id).EffectiveMode(day);
            if (selectedCategoryId == category.Id) MakeBox(content.transform, x, y, 159, 36, new Color(.99f, .94f, .77f));
            var tile = MakeButton(content.transform, x + 2, y + 2, 155, 32, category.Name, () => SelectCategory(category.Id), CategoryColor(mode), !busy);
            tile.gameObject.name = "CategoryTile:" + category.Id;
            var image = tile.GetComponent<Image>(); image.sprite = null; image.type = Image.Type.Simple;
            var label = tile.GetComponentInChildren<TMP_Text>();
            label.alignment = TextAlignmentOptions.Left; label.enableWordWrapping = false;
            var rect = label.GetComponent<RectTransform>(); rect.anchoredPosition = new Vector2(38, -2); rect.sizeDelta = new Vector2(112, 28);
            MakeCategoryIcon(tile.transform, 4, 2, 28, category.Id);
        }
        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>(); scroll.content = content.GetComponent<RectTransform>();
        scroll.horizontal = false; scroll.vertical = height > 388; scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30; scroll.verticalNormalizedPosition = categoryPickerScroll;
        scroll.onValueChanged.AddListener((UnityAction<Vector2>)(position => categoryPickerScroll = position.y));
    }
}
