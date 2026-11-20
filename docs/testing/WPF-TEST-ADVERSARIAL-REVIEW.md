# WPF 测试稳定性对抗审查

本文档是高影响 WPF 测试基础设施改动的审查流程与记录模板；架构规则唯一真源仍是 [ARCHITECTURE-CONTRACT.md](../ARCHITECTURE-CONTRACT.md) 的 `WPF-TEST-*` 行。它不引入运行时脚本、在线模型调用或第二套架构规则。

## 触发条件

下列改动必须完成本审查：`WpfApplicationFixture`、`WpfStaTestHost`、test-project orchestration、global static hooks、timeout/retry、parallelism、ThreadPool、background-flight lifetime、structured result format。

## 审查记录

每次记录必须包含：

- 原始 claim，以及可以证伪该 claim 的 falsification condition。
- 已确认的 facts、仍待验证的 hypotheses 和 unknowns。
- 至少一个能够推翻方案的 counterexample。
- cross-test contamination；thread/queue/time 资源边界；cancellation/Dispose 的所有权与退出语义；evidence completeness。
- 决定：`ADOPT`、`REVISE` 或 `BLOCK`，以及采纳后实际 adopted changes。
- commit、RED/GREEN evidence path 和 reviewer type（self-review、independent context 或 external model）。

审查结论必须以 fail-closed 证据为依据：当退出码、TRX、summary、顺序、计数器、stack/trace 或明确的 capture-failure reason 不完整时，不得将改动判为通过。在线 LLM 可以作为 reviewer type，但不是确定性自动化门禁的运行时依赖。
