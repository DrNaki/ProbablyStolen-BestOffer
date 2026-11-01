# WorkshopPublisher

针对 Best Offer 的独立发布 CLI，使用当前已登录的 Steam 客户端。不读取或保存密码。固定 Steam App ID 为 `4348910`，仅接受 manifest ID 为 `naki.bestoffer`、程序集为 `BestOffer.dll` 的内容包。

构建：

```powershell
dotnet build .\tools\WorkshopPublisher\WorkshopPublisher.csproj -c Release
```

运行入口为 `tools/WorkshopPublisher/bin/Release/net8.0/WorkshopPublisher.dll`。运行时从已安装的正式版加载 Steamworks.NET 和 `steam_api64.dll`，不会把这些 DLL 打包进 Mod。可用 `--game-dir` 或配置中的 GameDirectory 指向另一安装位置。

只读检查：

```powershell
dotnet <entry.dll> probe
dotnet <entry.dll> details --id <workshop-id>
```

不带参数只显示帮助，不执行任何 Steam 写操作。

## 发布配置

新建 UTF-8 JSON 文件，所有路径建议使用绝对路径：

```json
{
  "GameDirectory": "E:\\Steam\\steamapps\\common\\Probably Stolen",
  "ContentFolder": "C:\\Projects\\BestOffer\\workshop-release\\1.7.1\\content",
  "PreviewFile": "C:\\Projects\\BestOffer\\workshop-release\\1.7.1\\content\\preview.png",
  "AdditionalPreviewFiles": [
    "C:\\Projects\\BestOffer\\media\\v1.7.1\\01_中文_库存规则.jpg",
    "C:\\Projects\\BestOffer\\media\\v1.7.1\\02_中文_分类规则.jpg"
  ],
  "Title": "最佳报价 / Best Offer - Smart Trading Assistant",
  "DescriptionFile": "C:\\Projects\\BestOffer\\workshop-release\\1.7.1\\description.bbcode.txt",
  "Language": "schinese",
  "ChangeNote": "Steam Workshop release of Best Offer 1.7.1.",
  "Tags": ["Mod", "Quality of Life", "UI"],
  "StateFile": "C:\\Projects\\BestOffer\\workshop-release\\1.7.1\\workshop-state.json",
  "TimeoutSeconds": 300
}
```

预览支持 Steam 接受的 JPG/PNG，小于 1 MiB。描述及更新说明最多 7999 UTF-8 字节，标题最多 128 字节，超限直接报错，不截断。状态文件必须在内容文件夹之外。

`Language` 指定本次标题和描述的语言版本：`schinese` 为简体中文，`english` 为英文。当前中文为主的中英双语介绍使用 `schinese`；旧配置省略时仍兼容 Steam 的英文默认值。上传前显式设置语言，避免中文介绍被写入英文版本。只读复核可用 `details --id <id> --language schinese`。这是工坊文案语言，游戏内界面仍跟随游戏设置。[Steamworks 语言接口](https://partner.steamgames.com/doc/api/ISteamUGC#SetItemUpdateLanguage)

`AdditionalPreviewFiles` 可省略。填写时，每张图片都需为存在的 PNG/JPG/JPEG、非空且小于1MiB，文件名不能重名。只想先发布本体时省略此字段或使用空数组，避免尚未准备好的素材导致验证失败。

明确执行首次创建：

```powershell
dotnet <entry.dll> create-private --config <config.json>
```

CLI 在 CreateItem 请求前保存 `CreationAttemptedUtc`，收到回调后立即保存返回 ID，再设置标题、说明、内容、预览、标签和 Private 可见度并提交。用过的状态文件拒绝再次创建；发生超时或未知结果时先查看账号现有工坊条目，不要新建状态文件盲目重试。

更新同一条目或公开：

```powershell
dotnet <entry.dll> update --config <config.json> --id <id> --visibility private
dotnet <entry.dll> publish --config <config.json> --id <id>
```

update 默认 Private；publish 明确设置 Public。两者都要求显式 ID，且必须与已保存状态中的 ID 一致。create/submit 的完整回调结果、IO failure、`NeedsLegalAgreement`、工坊链接和阶段均写入状态文件并输出。协议需在 Steam 网页处理；工具不会代为接受法律协议。发布后应运行 details，并用 Steam 页面验证可见度、说明及文件大小。

## 仅追加或更新图库图片

```powershell
dotnet <entry.dll> add-previews --config <config.json> --id <id>
```

这个命令只对 `AdditionalPreviewFiles` 调用 AddItemPreviewFile / UpdateItemPreviewFile，再提交更新。不会创建条目，也不设置可见度、内容包、主封面、标题、说明或标签，因此不会把私密条目变为公开。此命令只要求配置中的 GameDirectory（可使用默认值）、StateFile、AdditionalPreviewFiles 与 TimeoutSeconds，不要求 ContentFolder/DescriptionFile/PreviewFile。

create-private、update、publish 同样会处理已配置的额外图片。每次先读取服务器最新图库索引及原文件名，再核对本地SHA256与状态记录：已上传且摘要相同的图跳过；同文件名的新内容更新已有索引；尚未存在的文件名才追加。同一配置中内容相同的两份文件也只上传一份，因此先 add-previews 再 publish 不会重复添加图库。

提交前会持久保存 `PendingPreviewOperation` 的操作token、文件名、摘要和索引，成功回调后写入 `AdditionalPreviews` 与 `LastCompletedPreviewOperationToken`。超时、IO失败或失败回调保留待确认操作并拒绝再次追加，防止不确定情况下重复建图；先运行只读 `details` 检查服务器图库及保存的操作，再由操作者核对并解决状态，不要删除记录盲目重试。details 输出 AdditionalPreviews（索引、原文件名、URL/视频ID、类型），可用于发布复核。

SDK初始化、拥有权与只读详情回调已实测成功。发布和图库命令都需要明确执行，运行前应核对配置、条目ID与准备上传的文件。

API依据：[Steamworks ISteamUGC](https://partner.steamgames.com/doc/api/ISteamUGC#AddItemPreviewFile)。
