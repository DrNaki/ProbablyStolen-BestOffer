# 最佳报价 / Best Offer 1.7.3

从 Nexus Best Offer 1.6.1 适配到《Probably Stolen》正式版原生 Mod 系统，并以新版交易平板重构界面。
智能选货、一键摆货、库存与分类规则、中英文界面沿用 Nexus 版本。
新增“改良种子”分类的官方图标；规则列表读取正式版分类表，共61项（含违禁品与赃物）。

订阅后重启游戏，在主菜单的“改装件 / Mods”中选择“最佳报价 / Best Offer”，
审核并启用，然后按游戏提示重启。无需安装 MelonLoader、.NET 6 或独立 Mod Manager。

进入店铺后按 F8，或点击交易平板的“报价配置”。设置想保留的商品，点击“一键摆货”
即可按买家需求和预算选择商品并摆上交易台；核对原版报价后，由玩家确认成交。
库存、分类、方案和交易设置四页保留。四项快捷键可在游戏 Mods 页面的齿轮或
“交易设置”中更改，两处同步保存；留空或点击“清除”可禁用对应快捷键。

| 功能 | 默认快捷键 | 行为 |
| --- | --- | --- |
| 报价面板 | F8 | 打开/关闭最佳报价面板 |
| 只读诊断 | F9 | 最佳报价面板打开时导出诊断 |
| 一键摆货 | Ctrl+F8 | 计算最佳方案并摆上交易台，不自动成交 |
| 一键售卖 | Ctrl+F9 | 计算最佳方案并直接出售，无需再次确认 |

两项交易快捷键在买家可交易且原版交易平板已打开时生效，仍遵守库存保护、
分类规则、买家拒收和预算限制。修改快捷键、输入文本及打开暂停菜单时不会触发交易。
请避免与其他模组或 dev.txt 官方开发工具的快捷键重复。升级时若新默认键与原有
面板/诊断自定义键冲突，会禁用冲突的新快捷键，保留原有设置。

1.7.3 同时修复正式版机器输出识别：3D 打印机的完整武器及其他独立输出栏中的
成品由“产物”控制，电池、模块、芯片和原料由“机器”控制。水分收集器/蒸馏器
输出栏的空瓶仍作设备保留；没有独立成品边界的混合库存继续受“机器”开关控制。

配置仍位于游戏根目录 UserData/BestOffer-*.json，更新无需删除。试玩版配置可在退出
两个游戏后手动复制这些文件到正式版的 UserData。不同存档的库存与分类规则分别保存。
卸载时在游戏 Mods 菜单停用，并取消订阅；配置保留。

测试环境：Windows x64，正式版菜单 VERSION 100-REV6，Steam build 26337366。
游戏内部 Application.version 为 0.46D，manifest 使用该兼容版本号。

Native Workshop adaptation of Nexus Best Offer 1.6.1 for the full release.
Subscribe, restart, select Best Offer in the main-menu Mods screen, review and enable it,
then restart as requested. MelonLoader, .NET 6 and the separate Mod Manager are not required.

Press F8 or use Offer Settings on the trading tablet. Configure protected inventory,
then use one-click placement. Prices, budgets, restrictions and settlement stay native.
Change any of the four hotkeys in Trade Settings or the main-menu Mods gear; both stay
in sync. Clear a binding, or leave its native settings field empty, to disable it.

| Action | Default | Behavior |
| --- | --- | --- |
| Offer Settings | F8 | Toggle the Best Offer panel |
| Read-only diagnostics | F9 | Export diagnostics while the panel is open |
| Auto-fill | Ctrl+F8 | Find and place the best offer without selling |
| Quick Sell | Ctrl+F9 | Find and sell the best offer without another confirmation |

Trading hotkeys require a tradable buyer and the native trading tablet to be open.
Inventory protections, category rules, buyer restrictions and budget limits still apply.
They are ignored during rebinding, text entry and pause menus. Avoid conflicts with other
mods or the optional dev.txt developer tools. During migration, conflicting new defaults
are disabled while your existing panel/diagnostic bindings are preserved.

1.7.3 fixes full-release machine output recognition, including complete printed weapons.
Products controls finished output; Machines controls batteries, modules, chips and inputs.
Empty liquid-output bottles remain equipment. Mixed inventories without a distinct output
boundary remain under Machines.

Settings remain in the full game's UserData/BestOffer-*.json. Preserve them when updating.
To migrate demo settings, close both games and copy these files into the full game's UserData.
Disable the mod and unsubscribe to remove it; personal settings remain.

Nexus: https://www.nexusmods.com/probablystolen/mods/723
Source: https://github.com/DrNaki/ProbablyStolen-BestOffer
Author: Naki & Codex


1.7.1：主菜单 Mods / 改装件页面的齿轮现可设置面板和诊断快捷键。输入 F8 或 Ctrl+F10 等组合，点击应用后立即生效，与店铺的交易设置双向同步。无效、Esc、纯修饰键和重复组合会保留旧设置。该模组的封面高度会为完整介绍及滚动区让出空间，权限信息保持完整。

1.7.2：修复启动阶段过早读取语言的时序风险，改进缺少文本时的回退和语言切换后的名称缓存。交易与规则逻辑保留。
1.7.2: defer language detection until the game finishes localization; improve missing-text fallbacks and refresh cached names on language changes. Trading and rules are unchanged.
