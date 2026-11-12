import { useTranslation } from "react-i18next";
import { downloadProblemSummary } from "./problemLabels";

/** Add an actionable summary to an existing diagnostic without rewriting its historical text. */
export default function DownloadProblemMessage({
  message,
  className,
}: {
  message: string;
  className?: string;
}) {
  const { t } = useTranslation();
  const summary = downloadProblemSummary(message, t);
  return (
    <div className={className}>
      {summary && summary !== message && <p>{summary}</p>}
      <p>{message}</p>
    </div>
  );
}
