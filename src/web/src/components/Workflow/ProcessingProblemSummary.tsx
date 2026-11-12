/** Keep the concrete file observation visible; render external/model text as plain text. */
export default function ProcessingProblemSummary({ message, summary }: { message: string; summary?: string }) {
  return (
    <div className="min-w-0 space-y-1">
      <p className="whitespace-pre-wrap break-words text-sm text-warning-700">{message}</p>
      {summary && summary.trim() !== message.trim() && (
        <p className="whitespace-pre-wrap break-words text-xs text-default-500">{summary}</p>
      )}
    </div>
  );
}
