using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sic.Core.State;

/// <summary>
/// The folder list settings.json kept until November 8, 2026, under <c>Workplaces</c> (and under <c>Workspaces</c> before
/// November 7): the folders the user added, browsed from their own names in a panel of their own. Since then the list is
/// <see cref="AppPreferences.Folders"/> (docs/decisions/workspaces.md), and an older file's list is read once into it by
/// <see cref="Workspaces.AdoptFolders"/>. What stays here reads the old keys in every shape they took.
/// </summary>
public static class WorkplaceFolders
{
    /// <summary>The saved list as it was kept: normalized, without blanks, each folder once, first added first.</summary>
    public static List<string> Clean(IEnumerable<string>? folders) => PinnedFolders.Clean(folders);

    /// <summary>
    /// Reads the saved list, under Workplaces or an older file's Workspaces, whether it holds folder paths, as it did last, or
    /// the named groups of folders an older settings file kept (<c>{ "Id", "Name", "Folders" }</c>), which are read as their
    /// folders in order, the names dropped. Writes paths, should a list ever be written.
    /// </summary>
    public sealed class ListConverter : JsonConverter<List<string>>
    {
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Workplaces must be a list of folders.");
            var folders = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        folders.Add(reader.GetString()!);
                        break;
                    case JsonTokenType.StartObject:
                        using (var group = JsonDocument.ParseValue(ref reader))
                        {
                            foreach (var property in group.RootElement.EnumerateObject())
                                if (string.Equals(property.Name, "Folders", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Array)
                                    folders.AddRange(property.Value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!));
                        }
                        break;
                    default:
                        throw new JsonException("A workplace must be a folder path.");
                }
            }
            return folders;
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var folder in value) writer.WriteStringValue(folder);
            writer.WriteEndArray();
        }
    }
}
