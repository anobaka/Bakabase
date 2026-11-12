import { createContext, useContext, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import { downloadProblemLabel } from "@/components/CloudDownload/problemLabels";

/** Only the acquisition task that owns a waiting run can supply its linked post. */
export const ResumePostSourceContext = createContext(false);

export const ResumeActivityContext = createContext<string | undefined>(undefined);
export const useResumeActivityKind = () => useContext(ResumeActivityContext);

export function ResumeGuidance({
  why,
  action,
  next,
}: {
  why: string;
  action: string;
  next: string;
}) {
  const { t } = useTranslation();
  return (
    <dl className="space-y-3 rounded-xl border border-warning-200 bg-warning-50/40 p-4 text-sm">
      {(
        [
          ["why", why],
          ["action", action],
          ["next", next],
        ] as const
      ).map(([label, text]) => (
        <div key={label} className="space-y-1">
          <dt className="font-medium">{t(`workflow.resume.guide.${label}`)}</dt>
          <dd className="break-words leading-relaxed text-default-600">{text}</dd>
        </div>
      ))}
    </dl>
  );
}

export function ResumeDetails({ children, title }: { children: ReactNode; title?: string }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  return (
    <div className="min-w-0 space-y-2">
      <Button
        size="sm"
        variant="light"
        className="h-auto max-w-full whitespace-normal px-0 py-1 text-left"
        aria-expanded={open}
        onPress={() => setOpen((value) => !value)}
      >
        {title || t("workflow.resume.guide.details")}
      </Button>
      {open && (
        <div className="min-w-0 space-y-1 rounded-lg bg-default-100 p-3 text-xs text-default-500">
          {children}
        </div>
      )}
    </div>
  );
}

/** Unknown exception stacks stay out of the form; only stable codes appear in technical details. */
export function ResumeProblem({ problem, fallback }: { problem: string; fallback: string }) {
  const { t } = useTranslation();
  const localized = downloadProblemLabel(problem, t);
  const summary =
    localized !== problem && !localized.startsWith("cloudDownload.") ? localized : fallback;
  const codes = [...new Set(problem.match(/\b[A-Z][A-Z0-9_]{2,}\b/g) ?? [])].filter((code) => {
    const key = `cloudDownload.problemCode.${code}`;
    const label = t(key, { defaultValue: "" });
    return label && label !== key;
  });
  return (
    <div className="space-y-1">
      <p className="break-words text-sm text-warning-700">{summary}</p>
      {codes.length > 0 && (
        <ResumeDetails>
          <p className="break-all font-mono">{codes.join(" · ")}</p>
        </ResumeDetails>
      )}
    </div>
  );
}
