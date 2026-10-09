using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Bakabase.Service.Components.ServerData;

public sealed record PathMappingRule(string SourcePrefix, string TargetPrefix);
public sealed record PathMappingProgress(string Phase, string File, string? Table = null,
    long ScannedTextValues = 0, long PathReferences = 0);
public sealed record PathMappingReference(string Path, string Kind, long ReferenceCount, string[] Locations);
public sealed record PathMappingScan(IReadOnlyList<PathMappingReference> Paths, long ScannedTextValues,
    int DatabaseCount, int ConfigurationFileCount);
public sealed record PathMappingExample(string SourcePath, string TargetPath, long ReferenceCount);
public sealed record PathMappingPreview(int MatchedPaths, long MatchedReferences, int UnmappedPaths,
    long UnmappedReferences, IReadOnlyList<PathMappingExample> Examples);
public sealed record PathMappingApplyResult(long ChangedValues, long ChangedReferences,
    int ChangedDatabases, int ChangedConfigurationFiles);

/// <summary>
/// Offline path references, never substring replacement. The caller owns directory locks,
/// source fingerprints and installation of the disposable stage. Scan cannot write to SQLite;
/// Apply commits each database atomically and replaces each configuration atomically. An error
/// makes the entire stage unsuitable for installation, even if earlier files were committed.
/// </summary>
public static class AppDataPathMapping
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "backups", "logs", "components", "cache", "caches", "attachments", "data", "temp", "thumbnails", "covers", "staging", ".git" };
    private static readonly HashSet<string> ConfigurationDirectories = new(StringComparer.OrdinalIgnoreCase)
    { "configs", "downloader", "federation", "remote-access" };
    private static readonly HashSet<string> DatabaseExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".db", ".sqlite", ".sqlite3" };
    private static readonly JsonLoadSettings JsonSettings = new() { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error };

    /// <summary>Absolute file names, ordered deterministically; no controls, backups or derived caches.</summary>
    public static IReadOnlyList<string> InputFiles(string directory) => EnumerateInputs(directory, true, default);

    private static IReadOnlyList<string> EnumerateInputs(string directory, bool requireCheckpoint, CancellationToken cancellation)
    {
        var root = Path.GetFullPath(directory);
        RejectLink(root);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The AppData directory does not exist.");
        var result = new List<string>();
        Visit(root, false);
        result.Sort(StringComparer.Ordinal);
        return result;
        void Visit(string folder, bool configuration)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                cancellation.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                if (name.StartsWith('.') || name.Equals("appdata-import-roots.json", StringComparison.OrdinalIgnoreCase)) continue;
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    if (ExcludedDirectories.Contains(name)) continue;
                    RejectLink(entry);
                    Visit(entry, configuration || folder == root && ConfigurationDirectories.Contains(name));
                }
                else if (DatabaseExtensions.Contains(Path.GetExtension(name)) ||
                         (folder == root || configuration) && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    RejectLink(entry);
                    if (requireCheckpoint && DatabaseExtensions.Contains(Path.GetExtension(entry))) RequireCheckpoint(entry);
                    result.Add(entry);
                }
            }
        }
    }

    public static PathMappingScan Scan(string directory, CancellationToken cancellationToken = default,
        Action<PathMappingProgress>? progress = null)
    {
        var worker = new Walker(directory, null, cancellationToken, progress);
        worker.Run(false);
        return Snapshot(worker);
    }

    private static PathMappingScan Snapshot(Walker worker) =>
        new(worker.References.Values.OrderBy(p => p.Path, StringComparer.Ordinal)
            .Select(p => new PathMappingReference(p.Path, p.Kind, p.Count, p.Locations.ToArray())).ToArray(),
            worker.TextValues, worker.Databases, worker.Configurations);

    public static IReadOnlyList<PathMappingRule> ValidateRules(IReadOnlyList<PathMappingRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (rules.Count > 256) throw new ArgumentException("Use at most 256 path mappings.");
        var normalized = new List<PathMappingRule>();
        var sources = new HashSet<string>(StringComparer.Ordinal);
        var targets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule == null || rule.SourcePrefix?.Length > 4096 || rule.TargetPrefix?.Length > 4096 ||
                !TryPath(rule.SourcePrefix, out var source, out _) ||
                !TryPath(rule.TargetPrefix, out var target, out _))
                throw new ArgumentException("Mapping prefixes must be absolute Windows, UNC or POSIX paths.");
            if (!sources.Add(Identity(source))) throw new ArgumentException("Duplicate mapping source prefix.");
            if (!targets.Add(Identity(target))) throw new ArgumentException("Mapping target prefixes collide.");
            normalized.Add(new PathMappingRule(source, target));
        }
        // Reject cycles between rules. A single rule may move into an ancestor or child:
        // mapping is one pass, and the caller must retry from a fresh, unmapped stage.
        var state = new int[normalized.Count];
        for (var i = 0; i < state.Length; i++) Visit(i);
        normalized.Sort((a, b) => b.SourcePrefix.Length.CompareTo(a.SourcePrefix.Length));
        return normalized;
        void Visit(int index)
        {
            if (state[index] == 1) throw new ArgumentException("Mapping rules contain a cycle or overlapping source and destination.");
            if (state[index] == 2) return;
            state[index] = 1;
            for (var next = 0; next < normalized.Count; next++)
                if (next != index && (Within(normalized[index].TargetPrefix, normalized[next].SourcePrefix) ||
                    Within(normalized[next].SourcePrefix, normalized[index].TargetPrefix))) Visit(next);
            state[index] = 2;
        }
    }

    /// <summary>Pure in-memory preview; rejects distinct source paths collapsing onto one destination.</summary>
    public static PathMappingPreview Preview(PathMappingScan scan, IReadOnlyList<PathMappingRule> rules)
    {
        var normalized = ValidateRules(rules);
        var destinations = new Dictionary<string, string>(StringComparer.Ordinal);
        var examples = new List<PathMappingExample>();
        var matched = 0;
        long matchedReferences = 0, unmappedReferences = 0;
        foreach (var reference in scan.Paths)
        {
            var mapped = Map(reference.Path, normalized);
            var identity = Identity(mapped);
            if (destinations.TryGetValue(identity, out var previous) && Identity(previous) != Identity(reference.Path))
                throw new InvalidDataException("Mapping would merge distinct stored paths into the same destination.");
            destinations[identity] = reference.Path;
            if (mapped != reference.Path)
            {
                matched++;
                matchedReferences += reference.ReferenceCount;
                if (examples.Count < 20) examples.Add(new(reference.Path, mapped, reference.ReferenceCount));
            }
            else unmappedReferences += reference.ReferenceCount;
        }
        return new(matched, matchedReferences, scan.Paths.Count - matched, unmappedReferences, examples);
    }

    public static PathMappingApplyResult Apply(string stagedDirectory, IReadOnlyList<PathMappingRule> rules,
        CancellationToken cancellationToken = default, Action<PathMappingProgress>? progress = null)
    {
        var normalized = ValidateRules(rules);
        // Detect collisions across every database/config before any mapped value is written.
        // This is the disposable copy, so recovery of its copied WAL is permitted.
        var preflight = new Walker(stagedDirectory, null, cancellationToken, progress);
        preflight.Run(false, allowJournalRecovery: true);
        Preview(Snapshot(preflight), normalized);
        var worker = new Walker(stagedDirectory, normalized, cancellationToken, progress);
        worker.Run(true);
        return new(worker.ChangedValues, worker.ChangedReferences, worker.ChangedDatabases, worker.ChangedConfigurations);
    }

    private static string Map(string path, IReadOnlyList<PathMappingRule> rules)
    {
        foreach (var rule in rules)
            if (Within(path, rule.SourcePrefix))
            {
                var suffix = path[rule.SourcePrefix.Length..].TrimStart('/');
                return suffix.Length == 0 ? rule.TargetPrefix : rule.TargetPrefix.TrimEnd('/') + "/" + suffix;
            }
        return path;
    }

    private static bool Within(string path, string prefix) => IsWindows(path) == IsWindows(prefix) &&
        (path.Equals(prefix, Comparison(prefix)) || path.StartsWith(prefix.EndsWith('/') ? prefix : prefix + "/", Comparison(prefix)));
    private static StringComparison Comparison(string path) => IsWindows(path) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static bool IsWindows(string path) => path.StartsWith("//", StringComparison.Ordinal) || path.Length >= 3 && path[1] == ':';
    private static string Identity(string path) => IsWindows(path) ? path.ToUpperInvariant() : path;

    private static bool TryPath(string? value, out string normalized, out string kind)
    {
        normalized = "";
        kind = "";
        if (string.IsNullOrEmpty(value) || value.Any(char.IsControl)) return false;
        string prefix;
        string remaining;
        if (value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] is '/' or '\\')
        {
            kind = "windows";
            prefix = char.ToUpperInvariant(value[0]) + ":/";
            remaining = value[3..].Replace('\\', '/');
        }
        else if (value.StartsWith("\\\\", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
        {
            var pieces = value.Replace('\\', '/')[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length < 2 || pieces[0] is "." or ".." or "?" || pieces[1] is "." or "..") return false;
            kind = "unc";
            prefix = "//" + pieces[0] + "/" + pieces[1];
            remaining = string.Join('/', pieces.Skip(2));
        }
        else if (value.StartsWith('/'))
        {
            kind = "posix";
            prefix = "/";
            remaining = value[1..]; // Backslashes are legal characters in POSIX file names.
        }
        else return false;
        var segments = new List<string>();
        foreach (var segment in remaining.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            if (segment == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); continue; }
            segments.Add(segment);
        }
        normalized = segments.Count == 0 ? prefix : prefix.TrimEnd('/') + "/" + string.Join('/', segments);
        return true;
    }

    private static void RequireCheckpoint(string file)
    {
        var wal = file + "-wal";
        if (File.Exists(wal))
        {
            RejectLink(wal);
            if (new FileInfo(wal).Length > 0)
                throw new InvalidDataException("Path preview requires stopped, checkpointed databases; a WAL still contains data.");
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Path mapping does not follow symbolic links.");
    }

    private sealed class Reference(string path, string kind)
    {
        public string Path { get; } = path;
        public string Kind { get; } = kind;
        public long Count;
        public HashSet<string> Locations { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Walker(string root, IReadOnlyList<PathMappingRule>? rules, CancellationToken cancellation,
        Action<PathMappingProgress>? report)
    {
        public Dictionary<string, Reference> References { get; } = new(StringComparer.Ordinal);
        public long TextValues, ChangedValues, ChangedReferences;
        public int Databases, Configurations, ChangedDatabases, ChangedConfigurations;
        private long _pathReferences;
        private readonly Stopwatch _reportTimer = Stopwatch.StartNew();
        private readonly Dictionary<string, string> _destinations = new(StringComparer.Ordinal);
        private string _file = "", _location = "";
        private string? _table;
        private bool _apply, _stage;

        public void Run(bool apply, bool allowJournalRecovery = false)
        {
            _apply = apply;
            _stage = apply || allowJournalRecovery;
            cancellation.ThrowIfCancellationRequested();
            foreach (var file in EnumerateInputs(root, !_stage, cancellation))
            {
                cancellation.ThrowIfCancellationRequested();
                _file = Path.GetRelativePath(root, file);
                _table = null;
                _location = _file;
                Report(true);
                if (DatabaseExtensions.Contains(Path.GetExtension(file))) { Database(file); Databases++; }
                else { Configuration(file); Configurations++; }
                Report(true);
            }
        }

        private void Report(bool force = false)
        {
            cancellation.ThrowIfCancellationRequested();
            if (force || _reportTimer.ElapsedMilliseconds >= 200)
            {
                report?.Invoke(new(_apply ? "mapping" : "scanning-paths", _file, _table, TextValues, _pathReferences));
                _reportTimer.Restart();
            }
        }

        private string PathValue(string value)
        {
            if (!TryPath(value, out var path, out var kind)) return value;
            _pathReferences++;
            if (rules == null)
            {
                var key = Identity(path);
                if (!References.TryGetValue(key, out var reference)) References.Add(key, reference = new(path, kind));
                reference.Count++;
                if (reference.Locations.Count < 8) reference.Locations.Add(_location);
                return value;
            }
            var mapped = Map(path, rules);
            var destination = Identity(mapped);
            if (_destinations.TryGetValue(destination, out var previous) && Identity(previous) != Identity(path))
                throw new InvalidDataException("Mapping would merge distinct stored paths into the same destination.");
            _destinations[destination] = path;
            if (mapped == path) return value; // Preserve spelling/format of unrelated references.
            ChangedReferences++;
            return mapped;
        }

        private static JToken Parse(string json)
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None, MaxDepth = 64 };
            var result = JToken.ReadFrom(reader, JsonSettings);
            if (reader.Read()) throw new JsonReaderException("Unexpected trailing JSON content.");
            return result;
        }

        private string Value(string value, int depth = 0)
        {
            if (depth > 40) throw new InvalidDataException("Nested path reference exceeds the supported depth.");
            var trimmed = value.AsSpan().TrimStart();
            if (trimmed.Length > 0 && trimmed[0] is '[' or '{' or '"')
            {
                JToken? token = null;
                try { token = Parse(value); }
                catch (JsonException) { /* Opaque strings are not searched for embedded paths. */ }
                if (token != null) return Token(token, depth + 1) ? token.ToString(Formatting.None) : value;
            }
            if (value.Contains('|'))
            {
                var paths = value.Split('|');
                if (paths.All(p => TryPath(p, out _, out _))) return string.Join('|', paths.Select(PathValue));
            }
            return PathValue(value);
        }

        private bool Token(JToken token, int depth = 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (depth > 40) throw new InvalidDataException("Nested path reference exceeds the supported depth.");
            var changed = false;
            if (token is JObject obj)
            {
                var typeToken = obj.GetValue("ValueType", StringComparison.OrdinalIgnoreCase);
                int? type = typeToken?.Type == JTokenType.Integer ? (int?)typeToken : null;
                var properties = obj.Properties().ToArray();
                var mappedNames = properties.Select(p => PathValue(p.Name)).ToArray();
                if (mappedNames.Distinct(StringComparer.Ordinal).Count() != mappedNames.Length)
                    throw new InvalidDataException("Mapping would merge JSON object keys.");
                for (var index = 0; index < properties.Length; index++)
                {
                    var property = properties[index];
                    var name = mappedNames[index];
                    if (property.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) && type.HasValue && property.Value.Type == JTokenType.String)
                    {
                        var original = (string)property.Value!;
                        var mapped = Standard(original, type.Value, depth + 1);
                        if (mapped != original) { property.Value = mapped; changed = true; }
                    }
                    else changed |= Token(property.Value, depth + 1);
                    if (name != property.Name) changed = true;
                }
                if (properties.Where((p, i) => p.Name != mappedNames[i]).Any())
                {
                    var replacements = properties.Select((p, i) => new JProperty(mappedNames[i], p.Value)).ToArray();
                    obj.RemoveAll();
                    obj.Add(replacements);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array) changed |= Token(item, depth + 1);
            }
            else if (token is JValue scalar && scalar.Type == JTokenType.String)
            {
                var original = (string)scalar!;
                var mapped = Value(original, depth + 1);
                if (mapped != original) { scalar.Value = mapped; changed = true; }
            }
            return changed;
        }

        private string Standard(string value, int type, int depth = 0)
        {
            // StandardValue is schema driven: commas in ordinary raw paths are not lists.
            if (type is not (2 or 4 or 8 or 9)) return Value(value, depth);
            // Historical rows sometimes contain JSON arrays instead of StandardValue escaping.
            if (value.AsSpan().TrimStart().StartsWith("[")) return Value(value, depth);
            var separator = type is 8 or 9 ? ';' : ',';
            var items = Split(value, separator);
            var mapped = items.Select(s => type is 8 or 9 ? Standard(s, 2, depth + 1) : Value(s, depth + 1)).ToArray();
            return items.SequenceEqual(mapped) ? value : Join(mapped, separator);
        }

        private void Configuration(string file)
        {
            var token = Parse(File.ReadAllText(file));
            TextValues++;
            var changed = Token(token);
            if (!changed || !_apply) return;
            cancellation.ThrowIfCancellationRequested();
            var temporary = file + ".mapping-" + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = File.GetUnixFileMode(file) & (UnixFileMode)0x1FF;
                using (var stream = new FileStream(temporary, options))
                {
                    // Creation mode is restricted by umask; restore only the source's access bits.
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, options.UnixCreateMode!.Value);
                    using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
                    writer.Write(token.ToString(Formatting.Indented));
                    writer.Flush();
                    stream.Flush(true);
                }
                cancellation.ThrowIfCancellationRequested();
                File.Move(temporary, file, true);
                ChangedValues++;
                ChangedConfigurations++;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private void Database(string file)
        {
            if (!_stage) RequireCheckpoint(file);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _stage ? file : new Uri(Path.GetFullPath(file)).AbsoluteUri + "?immutable=1",
                Mode = _stage ? SqliteOpenMode.ReadWrite : SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            using var transaction = _apply ? connection.BeginTransaction() : null;
            using var interrupt = cancellation.Register(() => SQLitePCL.raw.sqlite3_interrupt(connection.Handle));
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "PRAGMA table_list";
            var tables = new List<(string Name, bool WithoutRowId)>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    if (reader.GetString(0) == "main" && reader.GetString(2) == "table" && !reader.GetString(1).StartsWith("sqlite_", StringComparison.Ordinal))
                        tables.Add((reader.GetString(1), reader.GetInt32(4) != 0));
            var propertyTypes = PropertyTypes(connection, transaction, tables.Select(t => t.Name));
            var changesBefore = ChangedValues;
            foreach (var table in tables)
            {
                cancellation.ThrowIfCancellationRequested();
                _table = table.Name;
                Report(true);
                command.CommandText = "PRAGMA table_xinfo(" + Quote(table.Name) + ")";
                var columns = new List<(string Name, int Primary)>();
                using (var reader = command.ExecuteReader())
                    while (reader.Read())
                        if (reader.GetInt32(6) == 0) columns.Add((reader.GetString(1), reader.GetInt32(5)));
                var rowId = table.WithoutRowId ? null : new[] { "rowid", "_rowid_", "oid" }
                    .FirstOrDefault(n => columns.All(c => !c.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
                var keys = rowId != null ? new[] { rowId } : columns.Where(c => c.Primary > 0).OrderBy(c => c.Primary).Select(c => c.Name).ToArray();
                if (_apply && keys.Length == 0) throw new InvalidDataException("A SQLite table has no stable row identity for mapping.");
                var selected = keys.Concat(columns.Select(c => c.Name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                command.CommandText = "SELECT " + string.Join(',', selected.Select(Quote)) + " FROM " + Quote(table.Name) +
                                      (rowId != null ? " ORDER BY " + Quote(rowId) : "");
                var pending = new List<(Dictionary<string, object> Keys, Dictionary<string, string> Updates)>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                        for (var i = 0; i < selected.Length; i++) values[selected[i]] = reader.GetValue(i);
                        var updates = new Dictionary<string, string>(StringComparer.Ordinal);
                        foreach (var column in columns)
                        {
                            if (values[column.Name] is not string value) continue;
                            TextValues++;
                            _location = _file + ":" + table.Name + "." + column.Name;
                            var type = StandardType(table.Name, column.Name, values, propertyTypes);
                            var mapped = table.Name.Equals("PlayHistories", StringComparison.OrdinalIgnoreCase) && column.Name.Equals("Item", StringComparison.OrdinalIgnoreCase) && value.StartsWith("FileSystem:", StringComparison.Ordinal)
                                ? "FileSystem:" + PathValue(value[11..])
                                : type.HasValue ? Standard(value, type.Value) : Value(value);
                            if (mapped != value) updates.Add(column.Name, mapped);
                            Report();
                        }
                        if (!_apply || updates.Count == 0) continue;
                        var keyValues = keys.ToDictionary(k => k, k => values[k]);
                        if (rowId != null) Update(connection, transaction!, table.Name, keyValues, updates);
                        else pending.Add((keyValues, updates)); // A mapped WITHOUT ROWID key must not alter the live scan cursor.
                    }
                }
                foreach (var (keyValues, updates) in pending) Update(connection, transaction!, table.Name, keyValues, updates);
            }
            if (_apply)
            {
                _table = null;
                Report(true);
                command.CommandText = "PRAGMA integrity_check";
                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
                        throw new InvalidDataException("The mapped SQLite database failed integrity validation.");
                }
                cancellation.ThrowIfCancellationRequested();
                transaction!.Commit();
                command.Transaction = null;
                command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE)";
                command.ExecuteNonQuery();
                if (ChangedValues > changesBefore) ChangedDatabases++;
            }
        }

        private void Update(SqliteConnection connection, SqliteTransaction transaction, string table,
            Dictionary<string, object> keys, Dictionary<string, string> updates)
        {
            cancellation.ThrowIfCancellationRequested();
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            var index = 0;
            string Parameter(object value) { var name = "$p" + index++; command.Parameters.AddWithValue(name, value); return name; }
            command.CommandText = "UPDATE " + Quote(table) + " SET " + string.Join(',', updates.Select(p => Quote(p.Key) + "=" + Parameter(p.Value))) +
                                  " WHERE " + string.Join(" AND ", keys.Select(p => Quote(p.Key) + " IS " + Parameter(p.Value)));
            if (command.ExecuteNonQuery() != 1) throw new InvalidDataException("A mapped SQLite row could not be identified uniquely.");
            ChangedValues += updates.Count;
        }
    }

    private static Dictionary<long, long> PropertyTypes(SqliteConnection connection, SqliteTransaction? transaction, IEnumerable<string> tables)
    {
        var result = new Dictionary<long, long>();
        if (!tables.Contains("CustomProperties", StringComparer.OrdinalIgnoreCase)) return result;
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id,Type FROM CustomProperties";
        using var reader = command.ExecuteReader();
        while (reader.Read())
            if (reader.GetValue(0) is long id && reader.GetValue(1) is long type) result[id] = type;
        return result;
    }

    private static int? StandardType(string table, string column, Dictionary<string, object> values, Dictionary<long, long> propertyTypes)
    {
        if ((table.Equals("ResourceCaches", StringComparison.OrdinalIgnoreCase) && column is "CoverPaths" or "PlayableFilePaths") ||
            table.Equals("ReservedPropertyValues", StringComparison.OrdinalIgnoreCase) && column == "CoverPaths") return 2;
        if ((table.Equals("CustomPropertyValues", StringComparison.OrdinalIgnoreCase) || table.Equals("DataCardPropertyValues", StringComparison.OrdinalIgnoreCase)) && column.Equals("Value", StringComparison.OrdinalIgnoreCase))
        {
            if (values.TryGetValue("PropertyId", out var id) && id is long number && propertyTypes.TryGetValue(number, out var type))
                return type is 4 or 10 or 15 or 16 ? 2 : type == 9 ? 4 : 1;
        }
        if (table.Equals("Enhancements", StringComparison.OrdinalIgnoreCase) && column.Equals("Value", StringComparison.OrdinalIgnoreCase) &&
            values.TryGetValue("ValueType", out var standardType) && standardType is long numeric) return checked((int)numeric);
        return null;
    }

    private static string Quote(string name) => '"' + name.Replace("\"", "\"\"") + '"';
    private static string[] Split(string value, char separator)
    {
        var result = new List<string>();
        var part = new StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '\\' && i + 1 < value.Length && (value[i + 1] == separator || value[i + 1] == '\\')) part.Append(value[++i]);
            else if (ch == separator) { result.Add(part.ToString()); part.Clear(); }
            else part.Append(ch);
        }
        result.Add(part.ToString());
        return result.ToArray();
    }
    private static string Join(IEnumerable<string> values, char separator) => string.Join(separator,
        values.Select(v => v.Replace("\\", "\\\\").Replace(separator.ToString(), "\\" + separator)));
}
