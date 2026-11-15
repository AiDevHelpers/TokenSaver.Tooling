using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

using TokenSaver.Vs.Extension.Configuration;

namespace TokenSaver.Vs.Extension.Options;

/// <summary>
/// Advanced MCP settings: verbose logging, context helper and token economy switches, endpoint strategy, and the
/// endpoint list. Stored in the MCP server's appsettings.json (see <see cref="McpSettingsFile"/>). The server reads
/// them at startup, so a restart applies changes.
/// </summary>
public partial class TokenSaverOptionsControl
{
    private const string EndpointSeparator = "|";

    internal void LoadAdvanced(JsonObject root)
    {
        McpVerboseLoggingCheckBox.IsChecked = McpSettingsFile.Get(root, "VerboseLogging")?.GetValue<bool>() ?? false;
        McpAutoInterceptCheckBox.IsChecked = GetNested(root, "ContextHelper:AutoIntercept")?.GetValue<bool>() ?? true;
        McpAutoWarmCheckBox.IsChecked = GetNested(root, "ContextHelper:AutoWarmOnStartup")?.GetValue<bool>() ?? true;
        McpEnableDistillCheckBox.IsChecked = GetNested(root, "TokenEconomy:EnableDistill")?.GetValue<bool>() ?? true;
        McpEnableTerseCheckBox.IsChecked = GetNested(root, "TokenEconomy:EnableTerse")?.GetValue<bool>() ?? true;
        McpEnableSavingsCheckBox.IsChecked = GetNested(root, "TokenEconomy:EnableSavingsTracking")?.GetValue<bool>() ?? true;
        McpCtxSizeLimitTextBox.Text = (GetNested(root, "ContextHelper:ContextSizeLimit")?.GetValue<int>() ?? 120_000)
            .ToString(CultureInfo.InvariantCulture);

        // Names of TokenSaver.Common.Core EndpointSelectStrategy. The extension doesn't reference that assembly.
        McpEndpointStrategyComboBox.ItemsSource = new[] { "Failover", "RoundRobin", "Explicit" };
        McpEndpointStrategyComboBox.SelectedItem = McpSettingsFile.Get(root, "EndpointSelectStrategy")?.GetValue<string>() ?? "Failover";

        McpEndpointsTextBox.Text = FormatEndpoints(McpSettingsFile.Get(root, "Endpoints") as JsonArray);
    }

    /// <summary>Writes the advanced values into <paramref name="root"/>. Returns false with an error on bad input.</summary>
    internal bool TryApplyAdvanced(JsonObject root, out string? error)
    {
        if (!int.TryParse(McpCtxSizeLimitTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ctxLimit))
        {
            error = "Context size limit must be a whole number.";
            return false;
        }

        if (!TryParseEndpoints(McpEndpointsTextBox.Text, out var endpoints, out error))
        {
            return false;
        }

        McpSettingsFile.Set(root, "VerboseLogging", JsonValue.Create(McpVerboseLoggingCheckBox.IsChecked == true));
        McpSettingsFile.SetPath(root, "ContextHelper:AutoIntercept", JsonValue.Create(McpAutoInterceptCheckBox.IsChecked == true));
        McpSettingsFile.SetPath(root, "ContextHelper:AutoWarmOnStartup", JsonValue.Create(McpAutoWarmCheckBox.IsChecked == true));
        McpSettingsFile.SetPath(root, "ContextHelper:ContextSizeLimit", JsonValue.Create(ctxLimit));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableDistill", JsonValue.Create(McpEnableDistillCheckBox.IsChecked == true));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableTerse", JsonValue.Create(McpEnableTerseCheckBox.IsChecked == true));
        McpSettingsFile.SetPath(root, "TokenEconomy:EnableSavingsTracking", JsonValue.Create(McpEnableSavingsCheckBox.IsChecked == true));
        McpSettingsFile.Set(root, "EndpointSelectStrategy",
            JsonValue.Create(McpEndpointStrategyComboBox.SelectedItem as string ?? "Failover"));
        McpSettingsFile.Set(root, "Endpoints", endpoints);
        error = null;
        return true;
    }

    private static JsonNode? GetNested(JsonObject root, string path) => McpSettingsFile.GetPath(root, path);

    private static string FormatEndpoints(JsonArray? endpoints)
    {
        if (endpoints is null)
        {
            return "";
        }

        var lines = new List<string>();
        foreach (var node in endpoints)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            var models = (item["Models"] as JsonArray)?.Select(m => m?.GetValue<string>() ?? "") ?? Enumerable.Empty<string>();
            lines.Add(string.Join(EndpointSeparator,
                item["Name"]?.GetValue<string>() ?? "",
                item["Url"]?.GetValue<string>() ?? "",
                (item["Primary"]?.GetValue<bool>() ?? false).ToString(CultureInfo.InvariantCulture),
                string.Join(",", models)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    // One endpoint per line: name|url|primary(true|false)|model1,model2. Blank lines are skipped.
    private static bool TryParseEndpoints(string text, out JsonArray endpoints, out string? error)
    {
        endpoints = new JsonArray();
        var primaryCount = 0;
        var lineNumber = 0;
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(EndpointSeparator);
            if (parts.Length != 4 || parts[0].Trim().Length == 0 || parts[1].Trim().Length == 0)
            {
                error = $"Endpoint line {lineNumber} must be name|url|primary|models.";
                return false;
            }

            if (!bool.TryParse(parts[2].Trim(), out var primary))
            {
                error = $"Endpoint line {lineNumber}: primary must be true or false.";
                return false;
            }

            if (primary)
            {
                primaryCount++;
            }

            var models = new JsonArray();
            foreach (var model in parts[3].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                models.Add(model);
            }

            endpoints.Add(new JsonObject
            {
                ["Name"] = parts[0].Trim(),
                ["Url"] = parts[1].Trim(),
                ["Primary"] = primary,
                ["Models"] = models,
            });
        }

        // OllamaEndpointConfig docs: when endpoints are listed, at least one must have Primary set to true.
        if (endpoints.Count > 0 && primaryCount == 0)
        {
            error = "At least one endpoint line must have primary set to true.";
            return false;
        }

        error = null;
        return true;
    }
}
