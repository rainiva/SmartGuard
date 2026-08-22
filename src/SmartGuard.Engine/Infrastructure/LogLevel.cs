namespace SmartGuard.Engine.Infrastructure;

// 顺序即严重度（LogLevelFilter 依赖 >= 比较做最低级别过滤，禁止重排）；
// Heart 为独立心跳通道，不参与级别过滤（由 HeartbeatSchedule 调度治理），必须保持末位。
public enum LogLevel
{
  Trace,
  Debug,
  Info,
  Warn,
  Error,
  Critical,
  Heart,
}
