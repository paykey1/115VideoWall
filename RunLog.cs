using System.Text;
using System.Text.Json;

namespace VideoWall115;

internal sealed class RunLog : IDisposable
{
    private readonly StreamWriter? _writer;
    public string? DirectoryPath { get; }

    public RunLog(string data)
    {
        try
        {
            var directory = Path.Combine(data, "logs");
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.jsonl");
            _writer = new StreamWriter(new FileStream(file, FileMode.CreateNew, FileAccess.Write,
                FileShare.ReadWrite), new UTF8Encoding(false)) { AutoFlush = true };
            DirectoryPath = directory;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Browser startup remains available if diagnostics cannot be written.
        }
    }

    public void Write(string name, object? details = null)
    {
        try
        {
            _writer?.WriteLine(JsonSerializer.Serialize(new { time = DateTimeOffset.Now, name, details }));
        }
        catch (IOException)
        {
            // Diagnostics are best effort.
        }
    }

    public void Dispose() => _writer?.Dispose();
}
