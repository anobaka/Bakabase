using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

// Retention decisions need the frozen plan and policy. File disposition, nested input
// metadata, and stage-move evidence are verified independently by their respective guards.
internal sealed record FileArtifactRetentionMetadata(string? PlanJson, ArtifactStoragePolicy? StoragePolicy)
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

    internal static bool TryRead(string json, out FileArtifactRetentionMetadata? metadata)
    {
        try
        {
            if (json.Length < 1024 * 1024)
                metadata = JsonSerializer.Deserialize<FileArtifactRetentionMetadata>(json, JsonSerializerOptions.Web);
            else metadata = ReadLarge(json);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or EncoderFallbackException or InvalidOperationException)
        {
            metadata = null;
            return false;
        }
    }

    // Keep the strict JSON grammar and dictionary semantics of the small-document path.
    // Reading individual tokens prevents an unknown object, or a large escaped ancestor
    // metadata string inside it, from forcing a contiguous document-sized read buffer.
    private static FileArtifactRetentionMetadata? ReadLarge(string text)
    {
        using var input = new Utf8Chunks(text);
        var state = new JsonReaderState();
        var next = "";
        var sawRoot = false;
        var isNull = false;
        string? plan = null;
        ArtifactStoragePolicy? policy = null;
        long needed = 1;
        do
        {
            var bytes = input.Read(needed);
            var reader = new Utf8JsonReader(bytes, input.AtEnd, state);
            for (;;)
            {
                var before = reader;
                if (!reader.Read()) break;
                if (reader.CurrentDepth == 0)
                {
                    if (!sawRoot && reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.Null)) throw new JsonException();
                    if (!sawRoot) isNull = reader.TokenType == JsonTokenType.Null;
                    sawRoot = true;
                }
                if (reader.CurrentDepth != 1) continue;
                if (reader.TokenType == JsonTokenType.PropertyName)
                {
                    // Even a fully escaped ASCII policy key needs at most six bytes per character.
                    var length = reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length;
                    next = length <= "storagePolicy".Length * 6 ? reader.GetString()! : "";
                }
                else if (next.Equals("planJson", StringComparison.OrdinalIgnoreCase))
                {
                    if (reader.TokenType is not (JsonTokenType.String or JsonTokenType.Null)) throw new JsonException();
                    plan = reader.GetString();
                    next = "";
                }
                else if (next.Equals("storagePolicy", StringComparison.OrdinalIgnoreCase))
                {
                    if (!JsonDocument.TryParseValue(ref reader, out var value))
                    {
                        reader = before;
                        break;
                    }
                    using (value) policy = value.RootElement.Deserialize<ArtifactStoragePolicy>(JsonSerializerOptions.Web);
                    next = "";
                }
            }
            var remaining = bytes.Length - reader.BytesConsumed;
            state = reader.CurrentState;
            input.Consume(reader.BytesConsumed);
            if (!input.AtEnd && remaining > 32 * 1024 && reader.TokenType == JsonTokenType.PropertyName &&
                !next.Equals("planJson", StringComparison.OrdinalIgnoreCase) &&
                !next.Equals("storagePolicy", StringComparison.OrdinalIgnoreCase) && input.TrySkipString())
            {
                // An ignored string is a single value regardless of its length. Its contents
                // have been checked in pages; advance the structural reader by that one token.
                var equivalent = new Utf8JsonReader("\"\""u8, false, state);
                if (!equivalent.Read() || equivalent.TokenType != JsonTokenType.String) throw new JsonException();
                state = equivalent.CurrentState;
                needed = 1;
                continue;
            }
            if (input.AtEnd)
            {
                if (!sawRoot || remaining != 0) throw new JsonException();
                return isNull ? null : new(plan, policy);
            }
            // A string can cross many chunks. Retry it after geometric growth instead
            // of rescanning its entire prefix for each input page.
            needed = Math.Max(1, remaining * 2);
        } while (true);
    }

    private sealed class Utf8Chunks(string text) : IDisposable
    {
        private sealed class Page : ReadOnlySequenceSegment<byte>
        {
            internal readonly byte[] Bytes;
            internal Page(byte[] bytes, int length, long start)
            {
                Bytes = bytes;
                Memory = bytes.AsMemory(0, length);
                RunningIndex = start;
            }
            internal Page? Following => (Page?)Next;
            internal void Link(Page page) => Next = page;
        }

        private const int CharsPerPage = 16 * 1024;
        private int character, firstOffset;
        private long buffered;
        private Page? first, last;
        internal bool AtEnd => character == text.Length;

        internal ReadOnlySequence<byte> Read(long minimum)
        {
            while (buffered < minimum && !AtEnd)
            {
                var count = Math.Min(CharsPerPage, text.Length - character);
                if (character + count < text.Length && char.IsHighSurrogate(text[character + count - 1])) count--;
                var characters = text.AsSpan(character, count);
                var buffer = ArrayPool<byte>.Shared.Rent(StrictUtf8.GetByteCount(characters));
                int written;
                try { written = StrictUtf8.GetBytes(characters, buffer); }
                catch { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); throw; }
                var page = new Page(buffer, written, last == null ? 0 : last.RunningIndex + last.Memory.Length);
                if (last == null) first = page;
                else last.Link(page);
                last = page;
                buffered += written;
                character += count;
            }
            return first == null ? ReadOnlySequence<byte>.Empty : new(first, firstOffset, last!, last!.Memory.Length);
        }

        // Utf8JsonReader resumes between tokens, so a single escaped metadata string
        // can otherwise pin many pages. The encoder has already validated raw Unicode.
        // Validate the JSON string's controls and escape grammar without decoding its value.
        internal bool TrySkipString()
        {
            var opened = false;
            var escaped = false;
            var hexDigits = 0;
            while (true)
            {
                var bytes = Read(1);
                long consumed = 0;
                foreach (var segment in bytes)
                foreach (var value in segment.Span)
                {
                    consumed++;
                    if (!opened)
                    {
                        if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') continue;
                        if (value != (byte)'"') return false;
                        opened = true;
                    }
                    else if (hexDigits > 0)
                    {
                        if (!char.IsAsciiHexDigit((char)value)) throw new JsonException();
                        hexDigits--;
                    }
                    else if (escaped)
                    {
                        if (value == (byte)'u') hexDigits = 4;
                        else if (value is not ((byte)'"' or (byte)'\\' or (byte)'/' or (byte)'b' or (byte)'f' or
                            (byte)'n' or (byte)'r' or (byte)'t')) throw new JsonException();
                        escaped = false;
                    }
                    else if (value == (byte)'\\') escaped = true;
                    else if (value == (byte)'"')
                    {
                        Consume(consumed);
                        return true;
                    }
                    else if (value < 0x20) throw new JsonException();
                }
                if (!opened) return false;
                Consume(consumed);
                if (AtEnd) throw new JsonException();
            }
        }

        internal void Consume(long count)
        {
            buffered -= count;
            while (first != null && count >= first.Memory.Length - firstOffset)
            {
                count -= first.Memory.Length - firstOffset;
                var page = first;
                first = page.Following;
                ArrayPool<byte>.Shared.Return(page.Bytes, clearArray: true);
                firstOffset = 0;
            }
            firstOffset += (int)count;
            if (first == null) last = null;
        }

        public void Dispose() => Consume(buffered);
    }

    internal FileProcessingPlan? ReadPlan()
    {
        if (string.IsNullOrWhiteSpace(PlanJson)) return null;
        try { return JsonSerializer.Deserialize<FileProcessingPlan>(PlanJson, JsonSerializerOptions.Web); }
        catch (JsonException) { return null; }
    }
}
