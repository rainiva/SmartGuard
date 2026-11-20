# 安装包构建与验证（2026-10-02）

## 范围

按用户要求移除已放弃的开发工具代码、专属测试、入口脚本、规划/实施文档、架构登记、缓存和专属审查副本，清除活动文档链接。产品逻辑及原始电源验收证据保留；用户已有`installer/version.txt`为1.5.9，构建不递增、不提交该文件。

影响检查：唯一开发工具入口与Owner整体删除，无迁移后双入口；产品配置/状态/日志格式、时间和运行策略不变；无新增队列、线程或权限。恢复/升级风险限于新安装包的既有打包与安装链，用现有集成测试和任务快照核对；不部署到生产安装根。删除范围精确核对绝对路径，保留其他未跟踪文件。旧历史门禁不重写或冒充新验证。

## 删除验证

不存在性RED保存`retirement-red.json`，7个工具目标路径仍存在，退出1；删除后GREEN为目标与活动引用均空，`retirement-green.json`。证据位于`.work/test-results/`。架构回归327通过0失败0跳过，`package-removal-architecture-green.trx`。专属bin/obj及临时脚本/测试产物已删除，未清理全仓缓存或无关诊断文件。

## 构建

经现有`SmartGuard.Packaging`的installer入口，Release重新publish产品、校验staging并调用仓库已有ISCC；参数`--skip-version-bump`，构建退出0。Inno[官方编译器说明](https://jrsoftware.org/ishelp/topic_compilercmdline.htm)定义成功退出码0；未修改编译器或安装协议。

输出：`dist/SmartGuard-Setup-1.5.9-x64.exe`，66,020,734字节，最后写入UTC2026-10-02T12:43:39.9974172Z。SHA256：`6E0367CA443AA43538722C5A2714E19EF37C8FDFAD3C904CBE77A61274C5AEEF`。staging35个文件，未含已移除工具，版本文件字节仍为1.5.9；原始构建回执`.work/test-results/package-build-result.json`。

构建保留既有警告：LogTailReader.cs:95/103的CS1998、SettingsPolicyCoordinator.cs:89的CS8602；没有按范围外警告修改产品代码，也不声称零警告构建。

## 最终门禁

用户确认UAC后，`powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Run-Tests.ps1`于本地20:53:36完成，命令和summary退出码均0。Pester156/.NET1763/Tray Integration2/Installer Integration9，四层全部0失败0跳过、无未运行层；删除两项专属架构测试后.NET计数从1765回到1763，非新增跳过。

权威summary：`.work/test-results/run-tests-20261002T124832496Z-6065ef2b53ca4a898d283863f9e30035.summary.json`。包装`.work/guard-rules/package-build-gates.result.json`的Before/After原任务配置及运行态精确一致，Error为空；最终复核`.work/test-results/package-final-review.json`确认安装包SHA256与构建后相同、版本文件仍为原1.5.9。新包经过实际安装器集成路径，不等同于已部署用户生产安装或完成全部桌面验收；八小时重验按用户要求延后，真实前台UI链仍缺证据。
