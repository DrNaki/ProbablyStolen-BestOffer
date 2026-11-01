# 最佳报价 / Best Offer 1.7.1

从 Nexus Best Offer 1.6.1 适配到《Probably Stolen》正式版原生 Mod 系统，并以新版交易平板重构界面。
智能选货、一键摆货、库存与分类规则、中英文界面沿用 Nexus 版本。
新增“改良种子”分类的官方图标；规则列表读取正式版分类表，共61项（含违禁品与赃物）。

订阅后重启游戏，在主菜单的“改装件 / Mods”中选择“最佳报价 / Best Offer”，
审核并启用，然后按游戏提示重启。无需安装 MelonLoader、.NET 6 或独立 Mod Manager。

进入店铺后按 F8，或点击交易平板的“报价配置”。设置想保留的商品，点击“一键摆货”
即可按买家需求和预算选择商品并摆上交易台；核对原版报价后，由玩家确认成交。
库存、分类、方案和交易设置四页保留。快捷键可在“交易设置”中更改。
F9 在报价面板打开时导出只读诊断。若自行用 dev.txt 启用了官方开发工具，请在
交易设置中改用其他快捷键。

配置仍位于游戏根目录 UserData/BestOffer-*.json，更新无需删除。试玩版配置可在退出
两个游戏后手动复制这些文件到正式版的 UserData。不同存档的库存与分类规则分别保存。
卸载时在游戏 Mods 菜单停用，并取消订阅；配置保留。

测试环境：Windows x64，正式版菜单 VERSION 100-REV6，Steam build 26337366。
游戏内部 Application.version 为 0.46D，manifest 使用该兼容版本号。

Native Workshop adaptation of Nexus Best Offer 1.6.1 for the full release.
Subscribe, restart, select Best Offer in the main-menu Mods screen, review and enable it,
then restart as requested. MelonLoader, .NET 6 and the separate Mod Manager are not required.

Press F8 or use Offer Settings on the trading tablet. Configure protected inventory,
then use one-click placement. Prices, budgets, restrictions and settlement stay native;
the player still confirms every sale. Change hotkeys in Trade Settings. F9 exports
read-only diagnostics while the offer panel is open. Rebind if you use the game's optional
developer tools, enabled only by a dev.txt file.

Settings remain in the full game's UserData/BestOffer-*.json. Preserve them when updating.
To migrate demo settings, close both games and copy these files into the full game's UserData.
Disable the mod and unsubscribe to remove it; personal settings remain.

Nexus: https://www.nexusmods.com/probablystolen/mods/723
Source: https://github.com/DrNaki/ProbablyStolen-BestOffer
Author: Naki & Codex


1.7.1：主菜单 Mods / 改装件页面的齿轮现可设置面板和诊断快捷键。输入 F8 或 Ctrl+F10 等组合，点击应用后立即生效，与店铺的交易设置双向同步。无效、Esc、纯修饰键和重复组合会保留旧设置。该模组的封面高度会为完整介绍及滚动区让出空间，权限信息保持完整。
