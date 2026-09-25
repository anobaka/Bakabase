using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.DataSync.Abstractions;
using Bakabase.Modules.DataSync.Canonical;
using Bakabase.Modules.DataSync.Planning;
using Bakabase.Modules.DataSync.Refs;
using Bakabase.Modules.DataSync.Wire;
using static Bakabase.Modules.DataSync.Kinds.CustomProperties.CustomPropertyJson;

namespace Bakabase.Modules.DataSync.Kinds.CustomProperties;

public sealed partial class CustomPropertyCodec
{
    /// <summary>
    /// Error codes of a held entity (<see cref="CodecReadResult.Errors"/>, and <see cref="DataSyncPublishable.HeldDetail"/>
    /// when this device's own content would be held). Machine strings, never shown as they are.
    /// </summary>
    public static class HeldDetails
    {
        public const string TooManyChildren = "tooManyChildren";
        public const string Name = "name";
        public const string Type = "type";
        public const string Shape = "shape";
        public const string Setting = "settings";
    }

    /// <summary>Reasons of an <see cref="DataSyncWarningCode.OptionDropped"/> warning (v3.1 §6.3).</summary>
    public static class DropReasons
    {
        public const string Label = "label";
        public const string Uuid = "uuid";
        public const string DuplicateUuid = "duplicateUuid";
        public const string Color = "color";
        public const string Depth = "depth";
    }

    /// <summary>
    /// Peer validation (v3.1 §6.3, §3.3 here). Per-option problems drop the option — a node with its subtree — with
    /// one <see cref="DataSyncWarningCode.OptionDropped"/> each; entity-level problems hold the entity. Settings are
    /// normalized to exactly the type's, with defaults filled in; a <c>defaultValue</c> ref that does not resolve
    /// within the content is dropped (<see cref="DataSyncWarningCode.DefaultValueRefDropped"/>), so every published
    /// ref names an option of its own content. Unknown top-level members are returned verbatim in
    /// <see cref="CodecReadResult.Unknown"/>.
    /// </summary>
    protected override CodecReadResult ReadCore(JsonObject content, DataSyncLimits limits) =>
        new PeerReader(limits).Read(content);

    private sealed class PeerReader(DataSyncLimits limits)
    {
        private readonly List<DataSyncPlanWarning> _warnings = [];
        private readonly List<string> _errors = [];
        private readonly HashSet<string> _uuids = new(StringComparer.Ordinal);
        private bool _unknownEnumValue;
        private int _ignoredMembers;

        public CodecReadResult Read(JsonObject content)
        {
            JsonObject? unknown = null;
            foreach (var (member, value) in content)
            {
                if (ContentMembers.Contains(member)) continue;
                unknown ??= new JsonObject();
                unknown[member] = value?.DeepClone();
                _ignoredMembers++;
            }

            if (!TryString(content[CustomPropertyJson.Name], out var name) || name.Length < 1 ||
                name.Length > limits.MaxNameLength || !IsCleanText(name))
                _errors.Add(HeldDetails.Name);

            PropertyType type = default;
            if (!TryString(content[CustomPropertyJson.Type], out var typeName)) _errors.Add(HeldDetails.Type);
            else if (!CustomPropertyTypes.TryParse(typeName, out type)) _unknownEnumValue = true;
            if (_errors.Count > 0 || _unknownEnumValue) return Held();

            var isReference = CustomPropertyTypes.IsReference(type);
            var ignoreCase = ReadFlag(content, CustomPropertyJson.IgnoreCase, isReference);
            var childrenLocal = ReadFlag(content, CustomPropertyJson.ChildrenLocal, isReference);
            var settings = ReadSettings(type, content[CustomPropertyJson.Settings]);

            IReadOnlyList<CustomPropertyChoiceV1> choices = [];
            IReadOnlyList<CustomPropertyTagV1> tags = [];
            IReadOnlyList<CustomPropertyNodeV1> nodes = [];
            var ownList = childrenLocal ? null : CustomPropertyTypes.ChildListOf(type);
            foreach (var (member, list) in new[]
                     {
                         (CustomPropertyJson.Choices, CustomPropertyTypes.ChildList.Choices),
                         (CustomPropertyJson.Tags, CustomPropertyTypes.ChildList.Tags),
                         (CustomPropertyJson.Nodes, CustomPropertyTypes.ChildList.Nodes),
                     })
            {
                var node = content[member];
                if (node is null) continue;
                if (node is not JsonArray array)
                {
                    _errors.Add($"{HeldDetails.Shape}:{member}");
                    continue;
                }

                if (list != ownList)
                {
                    if (array.Count > 0) Ignored(member);
                    continue;
                }

                if (CountRaw(array) > limits.MaxOptionsPerProperty)
                {
                    _errors.Add(HeldDetails.TooManyChildren);
                    continue;
                }

                switch (list)
                {
                    case CustomPropertyTypes.ChildList.Choices:
                        choices = ReadChoices(array);
                        break;
                    case CustomPropertyTypes.ChildList.Tags:
                        tags = ReadTags(array);
                        break;
                    default:
                        nodes = ReadNodes(array, 1);
                        break;
                }
            }

            if (_errors.Count > 0 || _unknownEnumValue) return Held();

            var validated = new CustomPropertyContentV1
            {
                Name = name,
                Type = type,
                IgnoreCase = isReference ? ignoreCase : null,
                Settings = settings,
                Choices = choices,
                Tags = tags,
                Nodes = nodes,
                ChildrenLocal = childrenLocal,
            };
            validated = validated with { DefaultValue = ReadDefaultValue(validated, content[CustomPropertyJson.DefaultValue]) };
            if (_errors.Count > 0) return Held();

            if (_ignoredMembers > 0)
                _warnings.Add(Warning(DataSyncWarningCode.UnknownFieldsIgnored,
                    ("count", _ignoredMembers.ToString(CultureInfo.InvariantCulture))));
            return new CodecReadResult(validated, null, [], _warnings, unknown);
        }

        private CodecReadResult Held() => _errors.Count > 0
            ? new CodecReadResult(null, DataSyncHeldReason.Invalid, _errors.Distinct().ToArray(), _warnings)
            : new CodecReadResult(null, DataSyncHeldReason.UnknownEnumValue, [], _warnings);

        /// <summary>IgnoreCase or childrenLocal: false when absent; only reference types keep it.</summary>
        private bool ReadFlag(JsonObject content, string member, bool isReference)
        {
            var node = content[member];
            if (node is null) return false;
            if (!TryBool(node, out var value))
            {
                _errors.Add($"{HeldDetails.Shape}:{member}");
                return false;
            }

            if (isReference) return value;
            if (value) Ignored(member);
            return false;
        }

        private CustomPropertySettingsV1? ReadSettings(PropertyType type, JsonNode? node)
        {
            var settings = CustomPropertyTypes.DefaultSettings(type);
            if (node is null) return settings;
            if (node is not JsonObject json)
            {
                _errors.Add($"{HeldDetails.Shape}:{CustomPropertyJson.Settings}");
                return settings;
            }

            var own = CustomPropertyTypes.SettingNames(type);
            foreach (var (member, value) in json)
            {
                if (!SettingMembers.Contains(member))
                {
                    _ignoredMembers++;
                    continue;
                }

                if (!own.Contains(member))
                {
                    Ignored($"{CustomPropertyJson.Settings}.{member}");
                    continue;
                }

                if (value is null) continue;   // a null setting reads as its default
                var error = $"{HeldDetails.Setting}.{member}";
                switch (member)
                {
                    case Precision:
                        if (TryInt(value, out var precision) && precision >= limits.MinPrecision &&
                            precision <= limits.MaxPrecision) settings = settings! with { Precision = precision };
                        else _errors.Add(error);
                        break;
                    case MaxValue:
                        if (TryInt(value, out var maxValue) && maxValue >= limits.MinRatingMaxValue &&
                            maxValue <= limits.MaxRatingMaxValue) settings = settings! with { MaxValue = maxValue };
                        else _errors.Add(error);
                        break;
                    case ShowProgressBar:
                        if (TryBool(value, out var showProgressBar)) settings = settings! with { ShowProgressBar = showProgressBar };
                        else _errors.Add(error);
                        break;
                    case ValueIsSingleton:
                        if (TryBool(value, out var valueIsSingleton)) settings = settings! with { ValueIsSingleton = valueIsSingleton };
                        else _errors.Add(error);
                        break;
                    case Layout:
                        if (!TryString(value, out var layout)) _errors.Add(error);
                        else if (CustomPropertyAttachmentLayouts.All.Contains(layout)) settings = settings! with { Layout = layout };
                        else _unknownEnumValue = true;
                        break;
                }
            }

            return settings;
        }

        private IReadOnlyList<CustomPropertyChoiceV1> ReadChoices(JsonArray array)
        {
            var choices = new List<CustomPropertyChoiceV1>();
            foreach (var element in array)
            {
                if (!ReadCommon(element, Label, ChoiceMembers, 0, out _, out var uuid, out var label, out var color))
                    continue;
                choices.Add(new CustomPropertyChoiceV1(uuid, label, color));
            }

            return choices;
        }

        private IReadOnlyList<CustomPropertyTagV1> ReadTags(JsonArray array)
        {
            var tags = new List<CustomPropertyTagV1>();
            foreach (var element in array)
            {
                if (!ReadCommon(element, TagName, TagMembers, 0, out var json, out var uuid, out var name, out var color))
                    continue;
                string? group = null;
                var groupNode = json[Group];
                if (groupNode is not null && (!TryString(groupNode, out group) || !IsLabel(group, allowEmpty: true)))
                {
                    Drop(uuid, DropReasons.Label, 0);
                    continue;
                }

                tags.Add(new CustomPropertyTagV1(uuid, group, name, color));
            }

            return tags;
        }

        private IReadOnlyList<CustomPropertyNodeV1> ReadNodes(JsonArray array, int level)
        {
            var nodes = new List<CustomPropertyNodeV1>();
            foreach (var element in array)
            {
                var childrenNode = (element as JsonObject)?[Children];
                if (childrenNode is not null and not JsonArray)
                {
                    _errors.Add($"{HeldDetails.Shape}:{Children}");
                    continue;
                }

                var children = childrenNode as JsonArray;
                var descendants = children is null ? 0 : CountRaw(children);
                if (level > limits.MaxMultilevelDepth)
                {
                    Drop(ReadableUuid(element), DropReasons.Depth, descendants);
                    continue;
                }

                if (!ReadCommon(element, Label, NodeMembers, descendants, out _, out var uuid, out var label, out var color))
                    continue;
                nodes.Add(new CustomPropertyNodeV1(uuid, label, color)
                {
                    Children = children is null ? [] : ReadNodes(children, level + 1),
                });
            }

            return nodes;
        }

        /// <summary>The members every option has: uuid (unique within the property), a label or name, a colour.</summary>
        private bool ReadCommon(JsonNode? element, string labelMember, IReadOnlySet<string> members, int descendants,
            out JsonObject json, out string uuid, out string label, out string? color)
        {
            json = null!;
            uuid = null!;
            label = null!;
            color = null;
            if (element is not JsonObject obj)
            {
                Drop(null, DropReasons.Uuid, descendants);
                return false;
            }

            json = obj;
            foreach (var (member, _) in obj)
            {
                if (!members.Contains(member)) _ignoredMembers++;
            }

            if (!TryString(obj[CustomPropertyJson.Uuid], out uuid) || !IsUuid(uuid))
            {
                Drop(ReadableUuid(obj), DropReasons.Uuid, descendants);
                return false;
            }

            if (_uuids.Contains(uuid))
            {
                Drop(uuid, DropReasons.DuplicateUuid, descendants);
                return false;
            }

            if (!TryString(obj[labelMember], out label) || !IsLabel(label, allowEmpty: false))
            {
                Drop(uuid, DropReasons.Label, descendants);
                return false;
            }

            var colorNode = obj[CustomPropertyJson.Color];
            if (colorNode is not null)
            {
                if (!TryString(colorNode, out var colorText) || colorText.Length > limits.MaxColorLength ||
                    !IsCleanText(colorText))
                {
                    Drop(uuid, DropReasons.Color, descendants);
                    return false;
                }

                color = colorText.Length == 0 ? null : colorText;
            }

            _uuids.Add(uuid);
            return true;
        }

        private IReadOnlyList<OptionRef> ReadDefaultValue(CustomPropertyContentV1 validated, JsonNode? node)
        {
            if (node is null) return [];
            if (node is not JsonArray array)
            {
                _errors.Add($"{HeldDetails.Shape}:{CustomPropertyJson.DefaultValue}");
                return [];
            }

            if (array.Count == 0) return [];
            if (validated.ChildrenLocal || !CustomPropertyTypes.HasDefaultValue(validated.Type))
            {
                Ignored(CustomPropertyJson.DefaultValue);
                return [];
            }

            var refs = new List<OptionRef>();
            var resolvedUuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in array)
            {
                if (!OptionRef.TryRead(element, out var optionRef) || !IsValidRef(validated.Type, optionRef!))
                {
                    DroppedRef(ReadableUuid(element));
                    continue;
                }

                // Every ref must name an option of this content, and one option only once.
                var resolved = CustomPropertyRefs.Resolve(validated, optionRef!);
                if (resolved is null || !resolvedUuids.Add(resolved.Uuid) ||
                    (validated.Type == PropertyType.SingleChoice && refs.Count > 0))
                {
                    DroppedRef(optionRef!.Uuid);
                    continue;
                }

                refs.Add(optionRef!);
            }

            return refs;
        }

        private bool IsValidRef(PropertyType type, OptionRef optionRef)
        {
            if (!IsUuid(optionRef.Uuid)) return false;
            return type switch
            {
                PropertyType.SingleChoice or PropertyType.MultipleChoice =>
                    optionRef.Label is { } label && IsLabel(label, allowEmpty: false),
                PropertyType.Multilevel => optionRef.Path is { Count: > 0 } path &&
                                           path.Count <= limits.MaxMultilevelDepth &&
                                           path.All(l => IsLabel(l, allowEmpty: false)),
                _ => false,
            };
        }

        private bool IsUuid(string uuid) =>
            uuid.Length >= 1 && uuid.Length <= limits.MaxUuidLength && uuid != "*" && IsCleanText(uuid) &&
            !uuid.Any(char.IsControl);

        private bool IsLabel(string label, bool allowEmpty) =>
            (allowEmpty || label.Length > 0) && label.Length <= limits.MaxLabelLength && IsCleanText(label);

        private string? ReadableUuid(JsonNode? element) =>
            element is JsonObject obj && TryString(obj[CustomPropertyJson.Uuid], out var uuid) &&
            uuid.Length <= limits.MaxUuidLength && IsCleanText(uuid) && !uuid.Any(char.IsControl)
                ? uuid
                : null;

        private void Drop(string? uuid, string reason, int descendants)
        {
            var args = new Dictionary<string, string>
            {
                ["reason"] = reason,
                ["descendants"] = descendants.ToString(CultureInfo.InvariantCulture),
            };
            if (!string.IsNullOrEmpty(uuid)) args["uuid"] = uuid;
            _warnings.Add(new DataSyncPlanWarning(DataSyncWarningCode.OptionDropped, null, args));
        }

        private void DroppedRef(string? uuid) =>
            _warnings.Add(uuid is null
                ? new DataSyncPlanWarning(DataSyncWarningCode.DefaultValueRefDropped, null, null)
                : Warning(DataSyncWarningCode.DefaultValueRefDropped, ("uuid", uuid)));

        private void Ignored(string setting) =>
            _warnings.Add(Warning(DataSyncWarningCode.SettingsIgnoredForType, ("setting", setting)));

        /// <summary>Every element of an options array, multilevel descendants included, valid or not.</summary>
        private static int CountRaw(JsonArray array)
        {
            var count = 0;
            foreach (var element in array)
            {
                count++;
                if (element is JsonObject { } obj && obj[Children] is JsonArray children) count += CountRaw(children);
            }

            return count;
        }

        private static bool TryBool(JsonNode node, out bool value)
        {
            value = false;
            if (node is not JsonValue v) return false;
            switch (v.GetValueKind())
            {
                case JsonValueKind.True:
                    value = true;
                    return true;
                case JsonValueKind.False:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryInt(JsonNode node, out int value)
        {
            value = 0;
            if (node is not JsonValue v || v.GetValueKind() != JsonValueKind.Number ||
                !JsonNumbers.TryGetInt64(v, out var l) || l is < int.MinValue or > int.MaxValue) return false;
            value = (int)l;
            return true;
        }
    }

    /// <summary>No U+0000 and no unpaired surrogate: what may reach the canonicalizer from a peer (v3.1 §3.1).</summary>
    internal static bool IsCleanText(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\0') return false;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return false;
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    internal static DataSyncPlanWarning Warning(DataSyncWarningCode code, params (string Key, string Value)[] args) =>
        new(code, null, args.ToDictionary(a => a.Key, a => a.Value));
}
