using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    // A large completed run records its paths many times (active, staged, per-step and
    // final files). Only this subset witnesses ownership of its private input snapshot.
    private sealed class CleanupEvidenceProjection(string step)
    {
        private const long MaxEvidenceBytes = 64L * 1024 * 1024;
        internal const int MaxTokenBytes = 1024 * 1024;
        private string rootField = "";
        private bool discardValue;
        private int discardedContainer = -1;

        private bool Select(string name, int depth)
        {
            if (depth == 1)
            {
                rootField = name.ToLowerInvariant();
                return rootField is "lineageversion" or "completed" or "pendingoperation" or "readonlyvolumeartifactid" or
                    "readonlyvolumemembers" or "storagepolicy" or "steppolicies" or "operations" or "inputmanifest" or
                    "boundaryresults" or "outputs" or "retainedfiles";
            }
            // Dictionary keys are case-sensitive in a file processing plan.
            if (depth == 2 && rootField == "boundaryresults") return name == step;
            if (depth == 2 && rootField == "outputs") return name == "download";
            if (depth == 3 && rootField == "boundaryresults")
                return name.Equals("directory", StringComparison.OrdinalIgnoreCase) || name.Equals("manifest", StringComparison.OrdinalIgnoreCase);
            if (depth == 3 && rootField == "operations") return name.Equals("renameFiles", StringComparison.OrdinalIgnoreCase);
            if (depth == 5 && rootField == "operations") return name.Equals("move", StringComparison.OrdinalIgnoreCase);
            if (depth == 4 && rootField == "outputs") return name.Equals("path", StringComparison.OrdinalIgnoreCase);
            return true;
        }

        internal void Write(ref Utf8JsonReader reader, Utf8JsonWriter writer)
        {
            var token = reader.TokenType;
            if (discardedContainer >= 0)
            {
                if (reader.CurrentDepth == discardedContainer && token is JsonTokenType.EndArray or JsonTokenType.EndObject)
                    discardedContainer = -1;
                return;
            }
            if (discardValue)
            {
                discardValue = false;
                if (token is JsonTokenType.StartObject or JsonTokenType.StartArray) discardedContainer = reader.CurrentDepth;
                return;
            }
            switch (token)
            {
                case JsonTokenType.PropertyName:
                    var name = reader.GetString()!;
                    if (Select(name, reader.CurrentDepth)) writer.WritePropertyName(name);
                    else discardValue = true;
                    break;
                case JsonTokenType.StartObject: writer.WriteStartObject(); break;
                case JsonTokenType.EndObject: writer.WriteEndObject(); break;
                case JsonTokenType.StartArray: writer.WriteStartArray(); break;
                case JsonTokenType.EndArray: writer.WriteEndArray(); break;
                case JsonTokenType.String: writer.WriteStringValue(reader.GetString()); break;
                case JsonTokenType.Number:
                    if (reader.HasValueSequence) writer.WriteRawValue(reader.ValueSequence.ToArray(), true);
                    else writer.WriteRawValue(reader.ValueSpan, true);
                    break;
                case JsonTokenType.Null: writer.WriteNullValue(); break;
                case JsonTokenType.True: writer.WriteBooleanValue(true); break;
                case JsonTokenType.False: writer.WriteBooleanValue(false); break;
                default: throw new JsonException();
            }
            if (writer.BytesCommitted + writer.BytesPending > MaxEvidenceBytes)
                throw new InvalidDataException("The private input evidence exceeds its inspection limit.");
        }
    }

    private static async Task<State> ReadCleanupEvidenceAsync(string directory, string step, CancellationToken ct)
    {
        var path = Path.Combine(directory, "state.json");
        ArtifactManifestFiles.RejectLinks(path);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        if (length > 1024L * 1024 * 1024) throw new InvalidDataException("The processing checkpoint exceeds its inspection limit.");
        var stamp = File.GetLastWriteTimeUtc(path);
        var pipe = PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 128 * 1024, leaveOpen: true));
        try
        {
            var projection = new CleanupEvidenceProjection(step);
            var output = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(output);
            var state = new JsonReaderState();
            while (true)
            {
                var read = await pipe.ReadAsync(ct);
                if (stream.Position > length) throw new InvalidDataException("The processing checkpoint changed during inspection.");
                var reader = new Utf8JsonReader(read.Buffer, read.IsCompleted, state);
                while (reader.Read())
                {
                    if (reader.HasValueSequence ? reader.ValueSequence.Length > CleanupEvidenceProjection.MaxTokenBytes
                        : reader.ValueSpan.Length > CleanupEvidenceProjection.MaxTokenBytes)
                        throw new InvalidDataException("A processing checkpoint token exceeds its inspection limit.");
                    projection.Write(ref reader, writer);
                }
                if (read.Buffer.Length - reader.BytesConsumed > CleanupEvidenceProjection.MaxTokenBytes)
                    throw new InvalidDataException("A processing checkpoint token exceeds its inspection limit.");
                state = reader.CurrentState;
                pipe.AdvanceTo(reader.Position, read.Buffer.End);
                if (read.IsCompleted) break;
            }
            writer.Flush();
            if (stream.Length != length || File.GetLastWriteTimeUtc(path) != stamp)
                throw new InvalidDataException("The processing checkpoint changed during inspection.");
            return JsonSerializer.Deserialize<State>(output.WrittenSpan, Json) ?? throw new InvalidDataException("Invalid processing checkpoint.");
        }
        finally { await pipe.CompleteAsync(); }
    }
}
