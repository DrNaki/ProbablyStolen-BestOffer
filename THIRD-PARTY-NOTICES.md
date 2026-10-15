# 第三方组件说明

本源码仓库和 Nexus 轻量发布包不附带加载器二进制。构建时需另行取得 MelonLoader 0.7.3 的官方 Windows x64 发行文件，放入 `tools/melonloader/v0.7.3/`。本地完整离线打包模式可附带这些文件；它们不属于《深空当铺》的游戏程序文件。

MelonLoader 随包许可为 Apache License 2.0；其 `NOTICE.txt` 标注版权人为 `Copyright 2020 - 2022 Lava Gang`。完整离线包保留以下原始文件，请以原文为准：

- `tools/melonloader/v0.7.3/MelonLoader/Documentation/LICENSE.md`
- `tools/melonloader/v0.7.3/MelonLoader/Documentation/NOTICE.txt`
- `tools/melonloader/v0.7.3/MelonLoader/Documentation/README.md`

本说明仅转述加载器官方包的许可与通知，不为其他组件另行指定许可证。使用完整离线打包模式时，官方包附带的说明、署名和许可内容均保留。

`tools/melonloader/v0.7.3-files.json` 记录官方原包来源、原始 ZIP 的 SHA256，以及逐文件 SHA256。发布脚本仅从该清单选择并验证加载器文件，避免把运行游戏后产生的互操作程序集、缓存或日志混入发布包。
