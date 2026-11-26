namespace Sic.Core.State;

public sealed class JsonStateStore<T>(string path) where T : class
{
    public string FilePath { get; } = Path.GetFullPath(path);
    private static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = true, PropertyNameCaseInsensitive = true,
        ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true
    };

    public T Load(Func<T> createDefault)
    {
        if (!File.Exists(FilePath)) return createDefault();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath), Options)
                ?? throw new InvalidDataException("The state file contains null: " + FilePath);
        }
        catch (System.Text.Json.JsonException exception)
        {
            throw new InvalidDataException("The saved state is invalid and has been preserved: " + FilePath, exception);
        }
    }

    public void Save(T value) => Write(Serialize(value));

    /// <summary>The file's text for <paramref name="value"/>, taken on the thread that owns the value so <see cref="Write"/> can run on another.</summary>
    public byte[] Serialize(T value) => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(value, Options);

    /// <summary>Writes the serialized text to the file atomically: a temporary file beside it, flushed to disk, then moved into place.</summary>
    public void Write(byte[] serialized)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(serialized);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
