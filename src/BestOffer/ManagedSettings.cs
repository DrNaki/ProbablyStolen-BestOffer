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
            "点击交易 iPad 的齿轮「报价配置」或按 F8，设置库存规则、分类禁卖、报价方案与交易选项。违禁品和赃物可分别允许需求例外；等待买家时也可提前配置。设置自动保存。管理器负责启用/禁用。",
            "Open Quote Configuration on the trade tablet (default F8) to configure inventory rules, category restrictions, trade plans and options. Contraband and stolen goods have separate explicit-demand exceptions. Configure rules before a buyer arrives. Settings save automatically. Use the manager to enable or disable the mod.");
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
