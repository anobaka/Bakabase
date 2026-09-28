# 整理 ExHentai 同名画廊的混合目录

`Split-MixedGallery.ps1` 适用于多个画廊的图片已经混在**同一个目录**，且图片文件名类似 `Page 10_ _147302483_p9.png` 的情况。脚本需要 Windows PowerShell 5.1 或 PowerShell 7。

它只检查源目录的第一层图片，默认匹配文件名末尾的 `_8~12位数字_p页码`，按数字分组。比如源目录 `D:\ExHentai\[Misc] 无题` 中匹配 `147302483` 的图片，会移到旁边的 `D:\ExHentai\[Misc] 无题 [local-147302483]`。不匹配的文件留在原目录。

**这个数字只是从页面标题推断的本地标记，并非 ExHentai gallery ID。** 它不能用于还原 URL，也不能保证每组恰好对应一个画廊。用户提供的样例中，标记组数就多于 URL 数。请先检查预览结果，并抽查每组图片内容，再决定是否执行。脚本不会创建 Bakabase 的 `.bakabase-exhentai-gallery.json` 归属标记；后续下载器会把这些无标记的非空目录视为来源不明。

## 使用

先暂停 Bakabase 对该目录的下载任务及其他写入程序，完成预览、抽查和移动后再恢复。

在仓库根目录打开 PowerShell。默认是只读预览，不创建目录，也不移动文件：

```powershell
.\scripts\exhentai\Split-MixedGallery.ps1 -SourceDirectory 'D:\ExHentai\[Misc] 无题'
```

可以把逐文件计划保存为 CSV，便于筛选和核对：

```powershell
.\scripts\exhentai\Split-MixedGallery.ps1 -SourceDirectory 'D:\ExHentai\[Misc] 无题' |
  Export-Csv -LiteralPath 'D:\ExHentai\split-plan.csv' -NoTypeInformation -Encoding UTF8
```

核对后执行移动：

```powershell
.\scripts\exhentai\Split-MixedGallery.ps1 -SourceDirectory 'D:\ExHentai\[Misc] 无题' -Apply
```

`-Apply -WhatIf` 也会预览，不会移动文件。脚本接受 `-MarkerPattern`，用于调整文件名识别规则；正则表达式必须包含名为 `Marker` 和 `Page` 的捕获组。例如要接受 7~12 位数字：

```powershell
.\scripts\exhentai\Split-MixedGallery.ps1 -SourceDirectory 'D:\ExHentai\[Misc] 无题' `
  -MarkerPattern '_(?<Marker>\d{7,12})_p(?<Page>\d+)$'
```

## 保护规则

- 只移动严格匹配的图片文件；其他文件、子目录、符号链接都不移动。
- 同一标记下出现重复的 `_p页码`，或目标目录里有其他标记、未知文件、子目录、同页图片时，`-Apply` 会在移动任何文件前停止。
- 不覆盖目标文件，不删除原目录。途中如因权限或磁盘错误中断，可重新运行；脚本会核查已经移动到目标目录、标记和页码均不冲突的文件。
