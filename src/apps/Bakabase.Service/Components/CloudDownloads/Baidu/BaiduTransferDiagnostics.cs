using System;
using System.Collections.Generic;
using System.Text.Json;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Baidu;

internal static class BaiduTransferDiagnostics
{
    internal static bool IsTerminal(string? code) => code is
        "BAIDU_TRANSFER_FILE_COUNT_LIMIT" or "BAIDU_TRANSFER_NAME_CONFLICT" or "BAIDU_TRANSFER_ITEMS_REJECTED";

    internal static bool IsKnown(string? code) => IsTerminal(code) || code is
        "BAIDU_TRANSFER_COUNT_LIMIT_CONFIRM_TARGET" or "BAIDU_TRANSFER_CONFLICT_CONFIRM_TARGET" or
        "BAIDU_TRANSFER_PROVIDER_UNCONFIRMED" or "BAIDU_TRANSFER_PARTIAL_OR_PENDING" or
        "BAIDU_TRANSFER_OUTPUT_UNCONFIRMED" or "BAIDU_TRANSFER_RESPONSE_UNAVAILABLE" or "BAIDU_TRANSFER_RESPONSE_INVALID";

    // Accept only the bridge's bounded numeric response evidence. Unknown provider text and
    // identities must never be copied from a receipt or child process into a workflow payload.
    internal static CloudTransferDiagnostic? Read(JsonElement json, string? code)
    {
        if (!IsKnown(code) || json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty("code", out var name) || name.ValueKind != JsonValueKind.String || name.GetString() != code)
            return null;

        bool Number(string key, int min, int max, out int? result)
        {
            result = null;
            if (!json.TryGetProperty(key, out var value)) return true;
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var n) || n < min || n > max) return false;
            result = n;
            return true;
        }

        if (!Number("errno", -999999, 999999, out var errno) ||
            !Number("requested", 1, 100000000, out var requested) ||
            !Number("rejected", 1, 100000000, out var rejected) ||
            !Number("fileCount", 1, 100000000, out var count) || !Number("fileLimit", 1, 100000000, out var limit) ||
            rejected.GetValueOrDefault() > requested.GetValueOrDefault()) return null;
        var items = new List<int>();
        if (json.TryGetProperty("itemErrnos", out var codes))
        {
            if (codes.ValueKind != JsonValueKind.Array || codes.GetArrayLength() > 8) return null;
            foreach (var item in codes.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Number || !item.TryGetInt32(out var n) || n is < -999999 or > 999999 ||
                    items.Count > 0 && n <= items[^1]) return null;
                items.Add(n);
            }
        }
        var noAsync = json.TryGetProperty("noAsyncTask", out var asyncValue) && asyncValue.ValueKind == JsonValueKind.True;
        if (code is "BAIDU_TRANSFER_RESPONSE_UNAVAILABLE" or "BAIDU_TRANSFER_RESPONSE_INVALID")
            return errno == null && items.Count == 0 && requested == null && rejected == null && count == null && limit == null && !noAsync
                ? new(code, null, [], null, null) : null;
        if (errno == null || code == "BAIDU_TRANSFER_OUTPUT_UNCONFIRMED" && (errno != 0 || requested == null)) return null;
        if (IsTerminal(code) && (!noAsync || requested == null || rejected != requested || errno == 0 || items.Count == 0 || items.Contains(0))) return null;
        if (code is "BAIDU_TRANSFER_COUNT_LIMIT_CONFIRM_TARGET" or "BAIDU_TRANSFER_FILE_COUNT_LIMIT")
            if (errno != 12 || limit == null || count == null || count <= limit) return null;
        if (code is "BAIDU_TRANSFER_NAME_CONFLICT" or "BAIDU_TRANSFER_CONFLICT_CONFIRM_TARGET")
            if (errno != 12 || items is not [-30]) return null;
        return new(code!, errno, items.ToArray(), count, limit);
    }
}
