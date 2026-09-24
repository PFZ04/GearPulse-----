using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GearPulse;

public sealed record WidgetSettings(
    string IconStyle = "line",
    string Size = "medium",
    int BackgroundOpacity = 92,
    int ContentOpacity = 100,
    string? Monitor = null,
    string Corner = "bottom-right",
    bool ShowWiredHeadsets = true,
    bool ShowBluetoothHeadsets = true,
    bool HideUnreadableInformation = false)
{
    public double Scale => Size switch { "small" => .8, "large" => 1.25, _ => 1 };

    public WidgetSettings Normalized() => this with
    {
        IconStyle = IconStyle is "line" or "silhouette" ? IconStyle : "line",
        Size = Size is "small" or "medium" or "large" ? Size : "medium",
        BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 100),
        ContentOpacity = Math.Clamp(ContentOpacity, 0, 100),
        Monitor = string.IsNullOrWhiteSpace(Monitor) ? null : Monitor,
        Corner = Corner is "top-left" or "top-right" or "bottom-left" or "bottom-right" ? Corner : "bottom-right"
    };

    public static WidgetSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (!root.TryGetProperty("appearance", out var value) || value.ValueKind != JsonValueKind.Object) return new();
            static string? String(JsonElement item, string name) => item.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
            static int Number(JsonElement item, string name, int fallback) => item.TryGetProperty(name, out var field) &&
                field.ValueKind == JsonValueKind.Number && field.TryGetInt32(out var result) ? result : fallback;
            static bool Boolean(JsonElement item, string name) => !item.TryGetProperty(name, out var field) ||
                field.ValueKind != JsonValueKind.False;
            static bool Enabled(JsonElement item, string name) => item.TryGetProperty(name, out var field) &&
                field.ValueKind == JsonValueKind.True;
            return new WidgetSettings(String(value, "iconStyle") ?? "line", String(value, "size") ?? "medium",
                Number(value, "backgroundOpacity", 92), Number(value, "contentOpacity", 100),
                String(value, "monitor"), String(value, "corner") ?? "bottom-right",
                Boolean(value, "showWiredHeadsets"), Boolean(value, "showBluetoothHeadsets"),
                Enabled(value, "hideUnreadableInformation")).Normalized();
        }
        catch (Exception error)
        {
            AppLog.Write("Could not read appearance settings", error);
            return new();
        }
    }

    public bool Save(string path) => SettingsJson.Update(path, root =>
    {
        var value = Normalized();
        root["appearance"] = new JsonObject
        {
            ["iconStyle"] = value.IconStyle,
            ["size"] = value.Size,
            ["backgroundOpacity"] = value.BackgroundOpacity,
            ["contentOpacity"] = value.ContentOpacity,
            ["monitor"] = value.Monitor,
            ["corner"] = value.Corner,
            ["showWiredHeadsets"] = value.ShowWiredHeadsets,
            ["showBluetoothHeadsets"] = value.ShowBluetoothHeadsets,
            ["hideUnreadableInformation"] = value.HideUnreadableInformation
        };
    });
}

internal static class SettingsJson
{
    public static bool Update(string path, Action<JsonObject> update)
    {
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            JsonObject root;
            try { root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new() : new(); }
            catch (JsonException) { root = new(); }
            update(root);
            File.WriteAllText(temporary, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
            return true;
        }
        catch (Exception error)
        {
            AppLog.Write("Could not save settings", error);
            return false;
        }
        finally { try { File.Delete(temporary); } catch { } }
    }
}
