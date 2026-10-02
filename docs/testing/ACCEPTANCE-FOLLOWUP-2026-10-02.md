# 验收跟进与外部计划来源（2026-10-02）

## 外部来源：已定位进程

只读采集Windows System中Microsoft-Windows-UserModePowerService事件12，限定本地15:01:30–15:04:00和17:05:00–17:06:15两个窗口，各最多100条。原始XML、RecordId、UTC及消息保存在`.work/guard-rules/external-plan-origin-20261002.json`，没有更改电脑管家服务或电源设置。

事件明确记录`C:\Program Files\HONOR\PCManager\HnPerformanceCenter.exe`、PID2264，将高性能GUID改为`961cc777-2547-4f9d-8174-7d86181b8a7a`。RecordId10952、10957、10961、10964、10975、10978分别对应本地15:01:51.726、15:02:08.704、15:02:27.522、15:02:40.439、15:03:01.653、15:03:06.737；与powercfg纠偏交错。RecordId11063在17:05:38.604再次记录同进程的相同改动，11065在17:05:46.222记录powercfg改回高性能。

这两段系统事件与v3实际计划/状态不一致窗口吻合，直接确认荣耀电脑管家性能组件是这两次覆盖的调用进程；不再仅凭overlay GUID猜测来源。当前文件Authenticode状态Valid、签名主体Honor Device Co., Ltd.，仅作当前文件身份补充，不证明历史运行文件字节未变。内部触发条件、用户是否点击性能模式、是否自动策略仍未知；不把同GUID到同GUID的WUDFHost事件当成高性能被替换的证据。

微软[电源滑块文档](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/customize-power-slider)登记该GUID为Better Battery overlay；此身份不改变系统事件里明确记录的调用进程，也不证明其内部触发规则。现有最终发布复核见[修复报告](PLAN-PUBLICATION-FIX-2026-10-02.md)。八小时重验按用户要求延后，旧v3仍BLOCKED。

## 当前验收台账

| 项目 | 当前状态 | 新鲜证据或缺口 |
| --- | --- | --- |
| 自动完整门禁 | Passed | 最新2026-10-02 20:53:36；Pester156/.NET1763/Tray2/Installer9，全部0失败0跳过；删除开发工具后的新门禁及1.5.9安装包见[构建记录](PACKAGE-BUILD-2026-10-02.md)，历史回执保留 |
| S0原生通知/合盖唤醒 | 已有通过记录 | [S0原生验收](MODERN-STANDBY-NATIVE-ACCEPTANCE-2026-10-01.md)，不能外推深度低功耗 |
| S4、300/900阈值、额外60分钟 | Passed（既有候选） | [S4与空闲验收](S4-IDLE-SOAK-ACCEPTANCE-2026-10-01.md) |
| 电池合盖/开盖鼠标唤醒 | Passed（既有候选） | [电池验收](BATTERY-LID-MOUSE-ACCEPTANCE-2026-10-01.md) |
| 最终发布修复真实纠偏短测 | Passed | 60.503秒，4样本同PID/AC且计划一致；环境恢复已独立复核 |
| 前台规则完整真实UI链 | BLOCKED | 当前computer-use内核初始化报找不到指定路径os error3，重试及reset后仍失败；未获得窗口或截图，未操作真实设置 |
| UNC添加、128项上限提示、键盘/焦点 | NOT RUN（真实UI） | 单元/窗口测试不替代实际UI路径 |
| 100%/200%DPI、高对比度、减少动画 | NOT RUN | 需要可操作桌面和显示矩阵 |
| 整机节能测量 | NOT RUN | 无功率计证据，CPU指标不等于能耗 |
| 修复后连续八小时 | 延后/NOT RUN | 用户明确延后；未启动新会话 |
| 候选部署生产安装根 | NOT RUN | 尚未部署 |

旧进度中的Tray/Installer UAC阻塞已被新完整门禁解除；前台规则桌面链仍缺证据，不能将所有旧BLOCKED一起改成Passed。此表是现有验收报告的索引，不复制生产配置/运行状态真源。

## 桌面补测恢复路径

修复或恢复computer-use可用性后，先准备隔离候选及恢复快照，再实际走通添加→命中升档→降档→离开保留→不纠偏→返回重决策→手动高性能到期回归，核对托盘/设置文案和配置往返。UNC、上限提示、键盘和显示矩阵分项记录，不缩短默认300/900秒阈值。当前没有启动候选，也没有让用户在生产配置上盲测。
