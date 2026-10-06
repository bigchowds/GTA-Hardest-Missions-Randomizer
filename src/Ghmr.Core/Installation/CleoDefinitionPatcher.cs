using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ghmr.Core.Installation;

public static class CleoDefinitionPatcher
{
    public const string ExtensionName = "ghmr";

    private static readonly HashSet<string> RequiredCommands = new(StringComparer.Ordinal)
    {
        "READ_STRING_FROM_INI_FILE",
        "WRITE_STRING_TO_INI_FILE"
    };

    public static bool Apply(string definitionsPath, string extensionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionsPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionPath);

        JsonObject root = ReadObject(definitionsPath, "CLEO definition");
        JsonObject desired = ReadObject(extensionPath, "GHMR CLEO extension");
        ValidateExtension(desired);

        JsonArray extensions = root["extensions"] as JsonArray
            ?? throw new InvalidDataException(
                "The CLEO definition does not contain an extensions array.");

        int existingIndex = FindExtensionIndex(extensions);
        if (existingIndex >= 0 && JsonNode.DeepEquals(extensions[existingIndex], desired))
        {
            return false;
        }

        if (existingIndex >= 0)
        {
            extensions[existingIndex] = desired.DeepClone();
        }
        else
        {
            extensions.Add(desired.DeepClone());
        }

        WriteAtomically(definitionsPath, root);
        return true;
    }

    public static bool Remove(string definitionsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionsPath);

        JsonObject root = ReadObject(definitionsPath, "CLEO definition");
        JsonArray extensions = root["extensions"] as JsonArray
            ?? throw new InvalidDataException(
                "The CLEO definition does not contain an extensions array.");
        int existingIndex = FindExtensionIndex(extensions);
        if (existingIndex < 0)
        {
            return false;
        }

        extensions.RemoveAt(existingIndex);
        WriteAtomically(definitionsPath, root);
        return true;
    }

    private static JsonObject ReadObject(string path, string description)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"{description} file was not found.", path);
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new InvalidDataException($"{description} root must be an object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{description} is not valid JSON.", exception);
        }
    }

    private static void ValidateExtension(JsonObject extension)
    {
        if (!string.Equals(
                extension["name"]?.GetValue<string>(),
                ExtensionName,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The CLEO extension name must be ghmr.");
        }

        JsonArray commands = extension["commands"] as JsonArray
            ?? throw new InvalidDataException("The GHMR CLEO extension has no commands array.");
        HashSet<string> names = commands
            .OfType<JsonObject>()
            .Select(command => command["name"]?.GetValue<string>() ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        if (!names.SetEquals(RequiredCommands))
        {
            throw new InvalidDataException(
                "The GHMR CLEO extension command set is incomplete or unexpected.");
        }
    }

    private static int FindExtensionIndex(JsonArray extensions)
    {
        for (int index = 0; index < extensions.Count; index++)
        {
            if (extensions[index] is JsonObject extension &&
                string.Equals(
                    extension["name"]?.GetValue<string>(),
                    ExtensionName,
                    StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static void WriteAtomically(string path, JsonObject root)
    {
        string directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException("Definition path has no parent directory.");
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(
                root,
                new JsonSerializerOptions { WriteIndented = true });
            using (FileStream stream = new(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(json);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
