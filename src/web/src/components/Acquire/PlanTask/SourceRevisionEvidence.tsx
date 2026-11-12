import type { TFunction } from "i18next";

import { useTranslation } from "react-i18next";

import { Button, Tooltip } from "@/components/bakaui";

/** Producer evidence stays unchanged; only its presentation is translated. */
export function sourceRevisionEvidenceLabel(evidence: string, t: TFunction): string {
  if (evidence === "savedPlanMirror") return t("acquisition.sourceRevision.evidence.savedPlan");
  const post = /^postParser:([1-9]\d*):([1-9]\d*):group:(.+)$/u.exec(evidence);

  if (post)
    return t("acquisition.sourceRevision.evidence.postParser", {
      taskId: post[1],
      revision: post[2],
      groupId: post[3],
    });

  return t("acquisition.sourceRevision.evidence.other");
}

export default function SourceRevisionEvidence({ evidence }: { evidence: string }) {
  const { t } = useTranslation();

  return (
    <Tooltip
      content={<span className="max-w-lg whitespace-pre-wrap break-all">{evidence}</span>}
      delay={1000}
      placement="bottom"
    >
      <Button
        className="h-auto min-w-0 whitespace-pre-wrap break-words px-0 py-0 text-left text-xs text-default-500"
        size="sm"
        variant="light"
      >
        {sourceRevisionEvidenceLabel(evidence, t)}
      </Button>
    </Tooltip>
  );
}
