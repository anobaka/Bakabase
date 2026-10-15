import type { ActivityFailure } from "../activityErrors";

import { useTranslation } from "react-i18next";

import { activityErrorText } from "../activityErrors";

/** Durable errors and request failures keep diagnostics available without presenting them as copy. */
export function ErrorExplanation({ message, detail }: { message: string; detail?: string | null }) {
  const { t } = useTranslation();

  return (
    <div className="space-y-1 break-words text-xs text-danger" data-testid="sync-error-explanation">
      <p>{message}</p>
      {detail && (
        <details className="text-default-500" data-testid="sync-error-technical">
          <summary className="cursor-pointer">{t("dataSync.error.technical")}</summary>
          <pre className="mt-1 max-h-32 overflow-auto whitespace-pre-wrap break-words">
            {detail}
          </pre>
        </details>
      )}
    </div>
  );
}

export default function ActivityErrorNotice(failure: ActivityFailure) {
  const { t } = useTranslation();

  if (!failure.errorCode && !failure.error) return null;

  return (
    <ErrorExplanation
      detail={[failure.errorCode, failure.error].filter(Boolean).join("\n")}
      message={activityErrorText(t, failure)}
    />
  );
}
