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
            "点击交易 iPad 的齿轮「报价配置」或按 F8，设置库存与包装内容规则、风险禁卖、当天行情和货源。机器与产物默认保留，包装默认整盒整瓶出售。需求例外不覆盖买家拒收；明确拒收违禁品的买家也不摆售赃物。设置自动保存。管理器负责启用/禁用。",
            "Open Quote Configuration on the trade tablet (default F8) to configure inventory and package-content rules, risk restrictions, today's market effects and stock sources. Machine contents and products are opt-in; retail packages stay whole by default. Contraband and stolen goods have separate explicit-demand exceptions. Settings save automatically. Use the manager to enable or disable the mod.");
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
