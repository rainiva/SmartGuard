# Phase 2.1 Task Contract — 结构化日志

**状态：** 已完成（2026-06-16）  
**父规划：** [`MIGRATION.md`](MIGRATION.md) § Phase 2  
**制定日期：** 2026-06-16  
**前置：** Phase 1 已收尾（C# 引擎为主路径）

---

## Execution Summary

| 项 | 内容 |
|----|------|
| **Task** | 为 C# 引擎日志增加结构化级别标签（INFO / WARN / ERROR / HEART），统一行格式，保持 LogViewer 与现有滚动策略可用 |
| **Mode** | `STRICT` |
| **Skill chain** | TDD → daily-development → verification-before-completion |
| **Slice** | **仅 Phase 2.1**（不含按日归档 2.2、电源事件 2.3） |
| **Est. touch** | 4–6 个 C# 文件，1 个 xUnit 文件，0 个配置字段变更 |

---

## 一、Goal（目标）

将 `SmartGuard.Engine` 写入 `SmartGuard.log` 的每一行，从「时间戳 + 自由文本」升级为「时间戳 + 级别 + 消息」，使：

1. 日志查看器、文本搜索、后续 Phase 2.2 按日归档能按级别过滤或高亮；
2. 消除消息体内重复的 `WARN:` / `ERROR:` 前缀歧义；
3. **不改变** `SmartGuard.config.json` 字段、不改变 `status.json` IPC、不引入第三方日志库。

---

## 二、Non-Goals（本切片不做）

| 项 | 归属 |
|----|------|
| 按日归档 `{LogFile}.{yyyyMMdd}.bak`、7 天清理 | Phase 2.2 |
| `SystemEvents.PowerModeChanged` 事件驱动 | Phase 2.3 |
| PowerShell 回退引擎 `SmartGuard.Core.ps1` 日志格式对齐 | 可选延后；本切片 **仅 C#** |
| NLog / Serilog / Windows Event Log | 永久不做（见 MIGRATION D-非目标） |
| LogViewer 按级别着色、过滤 UI | 可选增强；本切片只保证**能原样显示新行** |
| 新增配置项 `LogLevel` / `MinLevel` | 本切片不做；四级全写 |

---

## 三、现状审计

### 3.1 当前行格式（C#）

```
yyyy-MM-dd HH:mm:ss - {message}
```

示例（`GuardianLoop.WriteLog`）：

```
2026-06-16 16:00:13 - SmartGuard Engine 启动。日志：D:\Project\SmartGuard\SmartGuard.log
2026-06-16 16:00:13 - WARN: 亮度写回未完全匹配，已重试 3 次
2026-06-16 16:00:13 - ERROR: ...
2026-06-16 16:00:13 - [监控中] 活跃 | 计划正常 | 高性能 | 电量90% 插电
```

### 3.2 写入路径

| 组件 | 职责 |
|------|------|
| `FileLogger.cs` | 滚动（`.old`）、`WriteLine` |
| `GuardianLoop.WriteLog` | 去重指纹、拼行、调 `FileLogger` |
| `Program.cs` | 单实例冲突写 `startup.log`（**本切片不改**） |

### 3.3 消费方（须保持可用）

| 消费方 | 依赖 | 风险 |
|--------|------|------|
| `Presentation.LogViewer.ps1` | 按行追加读文本 | 低：纯文本仍可读 |
| 托盘「打开日志」 | 同上 | 低 |
| 用户 `grep` / `Select-String` | 可能搜 `WARN:`、`ERROR:` | 中：见 §五兼容策略 |
| Pester `Invoke-LogRotationIfNeeded` | PS 侧格式 | 无影响（C# 独立） |

---

## 四、技术决策（冻结）

| ID | 决策 | 选定值 | 理由 |
|----|------|--------|------|
| L1 | 级别枚举 | `Info`, `Warn`, `Error`, `Heart` | 与 MIGRATION Phase 2 一致 |
| L2 | 行格式 | `{ts} [{LEVEL}] {message}` | LEVEL 固定 4–5 字符大写 |
| L3 | 时间戳 | `yyyy-MM-dd HH:mm:ss` | 与现网一致 |
| L4 | 编码 | UTF-8 无 BOM | 与 `FileLogger` 现行为一致 |
| L5 | 滚动策略 | 仍用 `LogMaxBytes` → `.old` | 2.2 再改按日归档 |
| L6 | 格式化逻辑 | 独立 `LogLineFormatter`（可单测） | 对齐 `BatteryStatusInterpreter` 模式 |
| L7 | API 入口 | `FileLogger.Write(level, path, message, maxBytes)` | `WriteLine` 保留或标记内部 |
| L8 | 去重指纹 | 仍基于**消息体**（不含级别） | 避免同级别重复刷屏 |
| L9 | Fallback 行 | `[WARN] [LOG-FALLBACK] {message}` | 主日志写失败时 |

### 4.1 目标行格式

```
2026-06-16 16:00:13 [INFO] SmartGuard Engine 启动。日志：D:\Project\SmartGuard\SmartGuard.log
2026-06-16 16:00:13 [WARN] 亮度写回未完全匹配，已重试 3 次
2026-06-16 16:00:13 [ERROR] powercfg 返回非零退出码
2026-06-16 16:00:13 [HEART] 活跃 | 计划正常 | 高性能 | 电量90% 插电 | 已暂停
```

说明：

- `[HEART]` 消息体**去掉**原 `[监控中]` 前缀，避免双括号；语义由级别表达。
- `INIT:` / `EXTERNAL:` / `状态:` 等**保留在消息体内**，级别见 §4.2。

---

## 五、级别映射表（冻结）

| 场景 | 级别 | 消息体（实施后示例） |
|------|------|----------------------|
| 引擎启动 | `INFO` | `SmartGuard Engine 启动。日志：{path}` |
| 首次初始化各步骤 | `INFO` | `INIT: 开始首次初始化...`（保留 INIT 前缀） |
| 空闲档位变化（非切换） | `INFO` | `状态: 活跃 (空闲0秒) \| 计划正常 \| ...` |
| 计划切换 + 亮度锁定 | `INFO` | `状态: 活跃 \| 计划切换(切前同步) + 亮度锁定: ...` |
| 心跳 | `HEART` | `活跃 \| 计划正常 \| 高性能 \| 电量90% 插电`（无 `[监控中]`） |
| 外部计划变更 | `WARN` | `EXTERNAL: 计划被外部改为 高性能 ({guid}) \| 下轮纠偏` |
| 亮度写回未匹配 | `WARN` | `亮度写回未完全匹配，已重试 {n} 次`（去掉 `WARN:`） |
| 主循环异常 | `ERROR` | `{ex.Message}`（去掉 `ERROR:`） |
| Fallback 写入 | `WARN` | `[LOG-FALLBACK] {原消息}` |

### 5.1 兼容策略（搜索 / 习惯）

- 新日志：**不再**在消息体以 `WARN:` / `ERROR:` 开头。
- 文档与 README 补充：搜索警告用 `[WARN]`，错误用 `[ERROR]`。
- 旧日志文件混合存在时，LogViewer 无需迁移。

---

## 六、文件级改动清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `src/.../Infrastructure/LogLevel.cs` | **新增** | 枚举 |
| `src/.../Infrastructure/LogLineFormatter.cs` | **新增** | `Format(timestamp, level, message)` |
| `src/.../Infrastructure/FileLogger.cs` | **修改** | 增加 `Write(LogLevel, ...)`；`RotateIfNeeded` 不变 |
| `src/.../Worker/GuardianLoop.cs` | **修改** | `WriteLog(LogLevel, string)`；按映射表改调用点（约 12 处） |
| `tests/.../LogLineFormatterTests.cs` | **新增** | 格式与级别标签 |
| `tests/.../FileLoggerTests.cs` | **扩展** | 写入含 `[INFO]` 行可读回 |
| `docs/MIGRATION.md` | **更新** | Phase 2.1 状态、功能表 |
| `lib/SmartGuard.Core.ps1` | **不改** | 回退路径保持旧格式 |

**预估调用点（GuardianLoop）：**

1. 启动 ×1 → INFO  
2. INIT ×5 → INFO  
3. EXTERNAL ×1 → WARN  
4. 计划切换 ×2 → INFO  
5. 亮度 WARN ×1 → WARN  
6. 档位变化 ×1 → INFO  
7. 心跳 ×1 → HEART  
8. catch ERROR ×1 → ERROR  
9. LOG-FALLBACK ×1 → WARN  

---

## 七、TDD 与测试计划

### 7.1 Red（必须先失败）

`LogLineFormatterTests.cs`：

```csharp
[Theory]
[InlineData(LogLevel.Info,   "[INFO]")]
[InlineData(LogLevel.Warn,   "[WARN]")]
[InlineData(LogLevel.Error, "[ERROR]")]
[InlineData(LogLevel.Heart, "[HEART]")]
void Format_includes_level_tag(LogLevel level, string tag);

[Fact]
void Format_uses_fixed_timestamp_pattern();

[Fact]
void Format_does_not_duplicate_warn_prefix_in_message();
```

`FileLoggerTests.cs`：

```csharp
[Fact]
void Write_persists_formatted_line_with_level();
```

可选（不强制本切片）：

- Pester 静态断言：`GuardianLoop.cs` 不含裸 `WriteLog(config, $"WARN:` 字符串。

### 7.2 Green

最小实现 → `Run-Tests.ps1` 全绿。

### 7.3 Refactor

- 确认 `WriteLine` 仅被 `Write` 内部调用，或删除公开 `WriteLine`。
- 不抽取过度抽象（无 `ILogger` 接口）。

---

## 八、验收标准（Definition of Done）

| # | 验收项 | 验证方式 |
|---|--------|----------|
| V1 | 引擎启动后首行含 `[INFO]` | 读 `SmartGuard.log` 尾部 |
| V2 | 心跳行含 `[HEART]` 且不含 `[监控中]` | 等 `HeartbeatIntervalMin` 或临时改配置 |
| V3 | 人为触发异常（如删 config 写权限）产生 `[ERROR]` | 集成观察 |
| V4 | 亮度重试失败路径产生 `[WARN]` | 单测 + 代码审查 |
| V5 | LogViewer 正常打开、尾随刷新 | 手动点托盘「打开日志」 |
| V6 | `RotateIfNeeded` 行为不变 | 现有 `FileLoggerTests` 仍绿 |
| V7 | xUnit 新增 ≥4 项，全套件绿 | `Run-Tests.ps1` |
| V8 | 无 `config.json` 字段变更 | diff 审查 |

---

## 九、风险与缓解

| 风险 | 缓解 |
|------|------|
| 用户脚本搜 `WARN:` 失效 | README / MIGRATION 注明新格式；旧日志仍可读 |
| HEART 与 INFO 消息体相似难区分 | 级别标签 + 去掉 `[监控中]` |
| 去重指纹误伤 | 指纹仍用 message；不同级别同一文案极罕见 |
| Phase 2.2 按日归档与 `.old` 双轨 | 2.1 不动滚动；2.2 Task Contract 再统一 |

---

## 十、回滚

1. `git revert` Phase 2.1 提交；
2. 或临时 checkout Phase 1 的 `FileLogger.cs` + `GuardianLoop.cs`；
3. 已写入的新格式日志**无需**回滚文件内容；
4. 重新 `Publish-Engine.ps1` + 重启 Guardian 任务。

---

## 十一、实施后文档更新

- [ ] `MIGRATION.md` 功能表「结构化日志级别」→ **已完成**
- [ ] `MIGRATION.md` Phase 2 表 Phase 2.1 勾选
- [ ] `README.md` 日志格式一小节（可选一行示例）
- [ ] 变更记录追加 2026-06-16 Phase 2.1

---

## 十二、与 Phase 2.2 / 2.3 边界

```
Phase 2.1 结构化级别     ← 本 Contract
    ↓
Phase 2.2 按日归档       ← 依赖 2.1 行格式稳定；可解析 [LEVEL]
    ↓
Phase 2.3 电源事件       ← 与日志正交；新增 INFO 行「电源事件: 插电」
```

**Phase 2.3 预占位（不实施）：** 电源事件日志建议 `INFO` 级别，消息 `电源事件: {Online|Offline}，立即重新评估`。

---

## 附录 A：参考实现草图（非最终代码）

```csharp
public enum LogLevel { Info, Warn, Error, Heart }

public static class LogLineFormatter
{
  public static string Format(DateTime timestamp, LogLevel level, string message)
  {
    var tag = level switch
    {
      LogLevel.Info => "INFO",
      LogLevel.Warn => "WARN",
      LogLevel.Error => "ERROR",
      LogLevel.Heart => "HEART",
      _ => "INFO",
    };
    return $"{timestamp:yyyy-MM-dd HH:mm:ss} [{tag}] {message}";
  }
}
```

```csharp
// GuardianLoop
private void WriteLog(GuardConfig config, LogLevel level, string message)
{
  var fp = message.Trim().ToLowerInvariant();
  if (!_tickLogFingerprints.Add(fp)) return;
  try
  {
    FileLogger.Write(level, config.LogFile, message, config.LogMaxBytes);
  }
  catch
  {
    FileLogger.Write(LogLevel.Warn, fallbackLogPath,
      $"[LOG-FALLBACK] {message}", long.MaxValue);
  }
}
```

---

## 附录 B：实施顺序（建议单次 PR）

1. `LogLevel` + `LogLineFormatter` + 单测（Red → Green）  
2. `FileLogger.Write` + 扩展单测  
3. `GuardianLoop` 调用点替换  
4. 现场验收 V1–V5  
5. 更新 `MIGRATION.md`  
6. `Run-Tests.ps1` 贴通过输出归档  
