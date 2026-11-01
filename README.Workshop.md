# Best Offer 1.7.2 · Steam Workshop

以 Nexus 1.6.1 的功能为基础，适配《Probably Stolen》正式版原生 Mod 系统，按新版交易平板重构四页界面。报价求解、分组、库存保护、分类期限、风险规则和手动成交机制保持原有行为。

正式版新增 `MODIFIED_SEED`（改良种子），规则列表直接读取游戏分类表，并补上官方物品图标。当前共有61项规则分类（含违禁品、赃物）。已有 `type:*`、`risk:*` 键和按存档保存的结构不变。

## 构建

需要 .NET 8 SDK、Python，以及已安装的正式版游戏。运行：

```powershell
.\scripts\BuildWorkshop.ps1
```

该命令生成独立的 `src/BestOffer.Workshop/Generated` 平台适配源码，执行原报价穷举对照、快捷键及新增分类测试，再构建 .NET Standard 2.1 DLL。发布目录为 `workshop-release/1.7.2/content`，不覆盖 Nexus 的 `dist/BestOffer.dll`。`-GameDir` 可指定正式版安装目录。

新界面实现见 `src/BestOffer.Workshop/WorkshopTabletUI.cs`，使用正式版 `TabletStyle` 配色和 `TabletSkin.frame2/frameNotched` 边框。原生平板完整位图无9slice边界，未将其强行拉宽。原模态遮罩、输入保护、Esc 优先关闭分类选择器等交互继续保留。

## 安装与发布

本地测试时，退出游戏，将 content 文件夹的内容复制到正式版 `Mods/BestOffer/`。游戏主菜单 Mods 中审核并启用，再重启。工坊订阅版使用同一原生加载方式，不需要 MelonLoader、.NET 6 或独立 Mod Manager。

个人配置仍是游戏根目录的 `UserData/BestOffer-*.json`。从试玩版迁移时，退出两个游戏后复制这些文件。勿将个人配置和存档放进发布包。

复用 `tools/WorkshopPublisher` 操作已有条目 `3830638812`。发布配置及请求状态是作者本地文件，不随仓库提交。维护已有条目时应保留原状态文件并显式使用同一条目 ID，避免重复创建；配置示例见上传器 README。上传器复用当前拥有游戏的 Steam 登录会话，不读取或保存密码。具体命令见其 README。

游戏菜单版本为 **100-REV6 / build 26337366**，内部 `Application.version` 仍为 `0.46D`。manifest 的兼容字符串为 `0.46D (100-REV6)`，同时显示实测版本并满足游戏原生兼容性判断。

发布包只包含 BestOffer.dll、manifest.xml、README、第三方说明和预览图。仅声明 `files` 和 `reflection-external`；不附带游戏程序集、框架 DLL、加载器或其他 Mod。

1.7.1：主菜单 Mods / 改装件页面的齿轮现可设置面板和诊断快捷键。输入 F8 或 Ctrl+F10 等组合，点击应用后立即生效，与店铺的交易设置双向同步。无效、Esc、纯修饰键和重复组合会保留旧设置。该模组的封面高度会为完整介绍及滚动区让出空间，权限信息保持完整。

1.7.2：等待游戏本地化初始化完成后再读取语言，避免模组提前创建默认设置或同步等待资源；缺少翻译或格式化失败时使用游戏回退，切换语言后刷新名称缓存。正式版构建新增 109 项语言初始化及隔离回归检查。
