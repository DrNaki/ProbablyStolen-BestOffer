using System.Globalization;
using DeepSpaceModSupport;
using UnityEngine;

namespace DeepSpaceBestOffer;

public sealed partial class BestOfferMod
{
    string panelHotkey = "F8", auditHotkey = "F9";
    float toolbarX = -1, toolbarY = 18;
    bool managerWasVisible;

    void InitializeLocalSettings()
    {
        ManagerIntegration.DetachLegacyCategory("BestOffer");
        LocalModDetails.Register("最佳报价 / Best Offer", "最佳报价", "Best Offer",
            "点击最佳报价按钮或按面板快捷键（默认 F8），直接设置货源范围、清货上限、必卖禁卖与交易方案。配置自动保存。入口位于原生交易 iPad 内；管理器负责启用/禁用。",
            "Open Best Offer (default F8) to configure quote sources, clearance limits, item rules and trade plans. Settings save automatically. Open it from the native trade tablet. Use the manager to enable or disable the mod.");
    }
    void RefreshManagerVisibility()
    {
        if (!ManagerIntegration.Visible) return;
        managerWasVisible = true;
        if (IsOpen) Close();
    }
    void CaptureLocalHotkey(Event evt)
    {
        if (evt.keyCode == KeyCode.Escape) { captureHotkey = 0; return; }
        if (!ManagerIntegration.CaptureFunctionHotkey(evt, out string binding)) return;
        if (binding == (captureHotkey == 1 ? auditHotkey : panelHotkey))
        { status = "面板与诊断快捷键不能相同，请重新选择。"; return; }
        if (captureHotkey == 1) panelHotkey = binding; else auditHotkey = binding;
        captureHotkey = 0; SaveSettings(); SuppressTransitionInput();
        status = "快捷键已保存。F5/F6 用于第三方 Mod，请避免与其他 Mod 的快捷键重复。";
    }
}
