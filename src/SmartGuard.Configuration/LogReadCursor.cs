namespace SmartGuard.Configuration;

public sealed record LogReadCursor(LogSourceIdentity Source, long ByteOffset, string? EventId);
