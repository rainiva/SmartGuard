# 底层亮度查询修复与归因（2026-11-05）

## 结论与证据边界

本次实际恢复了Windows亮度读取，不是关闭探测或降低日志级别。确认失败链的公共组件是微软WDM提供程序`WMIProv`（`C:\Windows\System32\wbem\wmiprov.dll`，版本`10.0.26100.3624`），涉及其宿主/激活缓存运行状态。没有证据证明Intel、Honor或GameViewer某一个显示驱动本身损坏；不能将存在虚拟驱动直接作为因果结论。

恢复前，普通用户和管理员直接CIM查询均复现`0x8004100A`。亮度、监视器标识、ACPI温度、内核提供程序信息四类查询都失败，电池CIM查询成功；类结构存在。四类失败均经`WMIProv`，不是SmartGuard内部超时或亮度类缺失。

管理员诊断将故障提供程序映射到旧`WmiPrvSE.exe`宿主7432，启动时间2026-10-28 16:59:21（Asia/Shanghai）；DLL微软签名有效。它与Windows的`InvProv`共用宿主。原宿主句柄约6316–6323，本机`HandlesPerHost`为4096，但没有检出相应5612事件。高句柄数只是异常线索，不足以证明最初诱因就是资源泄漏。

## 干预与失败记录

所有操作使用明确实例/进程身份检查，UAC由用户确认；没有禁用或更新显示驱动，没有修改用户规则，没有重建WMI仓库，也没有增加定时重启系统组件的逻辑。范围、影响矩阵及原始诊断保存在`.work/brightness-root-cause-20261105/`。

1. `admin-baseline.json`：保留四类查询失败、微软DLL签名、宿主模块和提供程序映射，作为RED证据。
2. 尝试`MSFT_Providers.UnLoad`定向卸载。一个实例卸载超时，之后为`0x8004108A`（provider disabled）。这是本次操作引入的临时停用状态，不能与原始`0x8004100A`混淆；未计修复成功。原始结果为`provider-reset.result.json`。
3. 回收经PID、启动时间、路径、模块及共享提供程序校验的旧宿主7432；旧进程退出确认。仅回收宿主未清除WMI主服务中的停用缓存，查询仍失败。证据为`host-reset.result.json`。
4. 通过`MSFT_Providers.Load`尝试重新激活，两次均返回无效参数，均未计成功，结果分别保留为`provider-load.result.json`、`provider-load-v2.result.json`。不得把“命令进程退出0”等同于提供程序恢复。
5. 按微软文档的注册更新恢复路径，备份`__Win32Provider.Name='WMIProv'`，重新提交原有`HostingModel`值。前后全部注册字段逐项序列化比较相等，未改变权限、承载模型或注册字段值。此操作刷新激活状态，新的`WMIProv`在宿主27908中工作。结果为`registration-before.json`及`registration-refresh.result.json`。

`UnLoad`不会自动恢复激活；先前将后续查询视为自动恢复的预期不正确。本次已显式恢复激活并通过四类真实查询确认，没有遗留停用状态。旧宿主已回收且未采集转储，无法再从其内部调用栈还原最初触发链；因此归因限定在已证实的公共组件及运行状态，不宣称查明具体驱动缺陷或永久消除复发。

## GREEN与实际应用恢复

- 08:22:48前后四类直接CIM查询全部恢复：亮度1项、监视器标识1项、ACPI温度1项、内核提供程序信息118项。亮度实际值为0%，不是失败时的-1；本次未执行亮度写入。
- 已安装Engine内部命令返回`SGWMI1|brightness|0`。未重启Engine，原PID32676在08:23:02自行写出`NativeProbeRecovered`（退出0、清理确认true），状态文件和随后心跳均为亮度0%。
- 原WMI主服务继续以PID5356运行，CIMWin32宿主15304未回收。显示驱动仍为Intel `32.0.101.8509`、IddDesk `25.0.0.4`、GameViewer `15.6.5.199`、Honor Virtual `1.0.23.3`，状态均OK。
- 配置SHA256仍为`AB173E29F11E80FF4569CD8BDB22DB9EEE2618B0C0B8768A84B993E1F5374FAF`。
- 连续301.7秒完成31次真实CIM亮度查询，失败0，均为合法0–100值，最长241ms；对应Engine亮度均有效、日志健康均healthy；新宿主句柄194–199。证据为`stability-probes.json`。这只能证明本次短时恢复稳定，长时间、休眠/唤醒和重新启动组合未验证。
- 最终显式等待并重定向子进程标准流后，确认已安装探测返回`SGWMI1|brightness|0`、退出0、stderr为空（`final-child.stdout.txt`、`final-child.stderr.txt`）。先前直接shell调用GUI程序未可靠捕获输出/退出码，未将该次验收命令失败算作应用失败或通过。

本次没有生产源码或安装包变更，验证是Windows组件修复前后对照与实际已安装应用恢复；不把此前的全量代码测试称为本次新跑门禁。

## 依据

- [微软WDM Provider说明](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wdm-provider)：硬件驱动WDM数据经root/WMI提供。
- [提供程序配置与排障类](https://learn.microsoft.com/en-us/windows/win32/wmisdk/provider-configuration-and-troubleshooting-classes)。
- [UnLoad方法语义与恢复路径](https://learn.microsoft.com/en-us/previous-versions/windows/desktop/wmisystemprov/unload-method-in-class-msft-providers)：停用后可经Load、注册更新或服务重启恢复。
- [WMI宿主配额排查](https://learn.microsoft.com/en-us/troubleshoot/windows-server/system-management-components/scenario-guide-troubleshoot-wmiprvse-quota-exceeded-issues)：不能未经调查就扩大配额或断言泄漏原因。
