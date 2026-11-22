using System.Text.Json;

namespace VideoWall115;

internal sealed class LocalJsonFile<T>(string data, string name, RunLog log) where T : class
{
    private readonly string _file = Path.Combine(data, name + ".json");

    internal T? Read()
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(_file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (ex is not FileNotFoundException and not DirectoryNotFoundException)
                log.Write("local_data_read_failed", new { name, type = ex.GetType().Name });
            return null;
        }
    }

    internal bool TryWrite(T value)
    {
        var temp = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(data);
            File.WriteAllText(temp, JsonSerializer.Serialize(value));
            File.Move(temp, _file, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Write("local_data_write_failed", new { name, type = ex.GetType().Name });
            return false;
        }
        finally
        {
            try { File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
