using System.IO.Pipes;
using System.Text;

namespace SmartGuard.Contracts;

public static class SingleInstanceActivation
{
  public static string PipeName(string component) => $"SmartGuard.{component}.Activate";

  public static bool TryNotifyExisting(string component, int timeoutMs = 500)
  {
    try
    {
      using var client = new NamedPipeClientStream(".", PipeName(component), PipeDirection.Out);
      client.Connect(timeoutMs);
      using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
      writer.WriteLine("activate");
      return true;
    }
    catch
    {
      return false;
    }
  }

  public static void RunActivationServer(string component, Action onActivate, CancellationToken cancel)
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
        _ = reader.ReadLine();
        onActivate();
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
