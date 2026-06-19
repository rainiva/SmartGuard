using System.IO.Pipes;
using System.Text;

namespace SmartGuard.Contracts;

public static class SingleInstanceActivation
{
  public static string PipeName(string component) => $"SmartGuard.{component}.Activate";

  public static bool TryNotifyExisting(string component, int timeoutMs = 500)
  {
    return TryNotifyExisting(component, string.Empty, timeoutMs);
  }

  public static bool TryNotifyExisting(string component, string argument, int timeoutMs = 500)
  {
    try
    {
      using var client = new NamedPipeClientStream(".", PipeName(component), PipeDirection.Out);
      client.Connect(timeoutMs);
      using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
      writer.WriteLine(argument);
      return true;
    }
    catch
    {
      return false;
    }
  }

  public static void RunActivationServer(string component, Action onActivate, CancellationToken cancel)
  {
    RunActivationServer(component, _ => onActivate(), cancel);
  }

  public static void RunActivationServer(string component, Action<string> onActivate, CancellationToken cancel)
  {
    while (!cancel.IsCancellationRequested)
    {
      try
      {
        using var server = new NamedPipeServerStream(
          PipeName(component),
          PipeDirection.In,
          1,
          PipeTransmissionMode.Byte,
          PipeOptions.Asynchronous);
        var wait = server.WaitForConnectionAsync(cancel);
        wait.GetAwaiter().GetResult();
        using var reader = new StreamReader(server, Encoding.UTF8);
        var argument = reader.ReadLine() ?? string.Empty;
        onActivate(argument);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch
      {
        if (cancel.IsCancellationRequested)
          break;
        Thread.Sleep(50);
      }
    }
  }
}
