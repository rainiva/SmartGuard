namespace SmartGuard.Configuration;

// 受控关闭状态机：Starting → Running → Flushing（停止接收、排空队列）→ Stopped。
public enum LogPipelineState
{
  Starting,
  Running,
  Flushing,
  Stopped,
}
