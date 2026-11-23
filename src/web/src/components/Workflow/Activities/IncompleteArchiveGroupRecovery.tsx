import { useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";
import { humanFileSize } from "@/components/utils";
import ResumeActions from "../ResumeActions";

interface ArchiveGroup {
  token: string;
  memberCount: number;
  totalBytes: number;
  completedGroupCount: number;
  members: string[];
}

interface GroupPrompt {
  targetId: string;
  artifactId: string;
  fingerprint: string;
  knownStepId: string;
  allowedActions: string[];
  archiveGroupInspected?: boolean;
  incompleteArchiveGroup?: unknown;
}

const present = (value: unknown): value is string => typeof value === "string" && !!value.trim();
const count = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0;

export function isArchiveGroupWait(value: unknown): value is GroupPrompt {
  if (!value || typeof value !== "object") return false;
  const p = value as Record<string, unknown>;
  return p.reason === "MissingVolumes" && present(p.targetId) && present(p.artifactId) &&
    present(p.fingerprint) && present(p.knownStepId) && Array.isArray(p.allowedActions) &&
    (p.allowedActions.length === 0 || p.allowedActions.includes("retainIncompleteArchiveGroup"));
}

function verifiedGroup(value: unknown): value is ArchiveGroup {
  if (!value || typeof value !== "object") return false;
  const g = value as Record<string, unknown>;
  return present(g.token) && count(g.completedGroupCount) && count(g.totalBytes) && count(g.memberCount) &&
    Array.isArray(g.members) && g.members.length > 0 && g.members.length <= Math.min(g.memberCount, 8) &&
    g.members.every(present);
}

export default function IncompleteArchiveGroupRecovery({ prompt, submitting, onSubmit }: {
  prompt: GroupPrompt;
  submitting: boolean;
  onSubmit: (signalJson: string) => void;
}) {
  const { t } = useTranslation();
  const sending = useRef(false);
  const [pending, setPending] = useState(false);
  const group = verifiedGroup(prompt.incompleteArchiveGroup) ? prompt.incompleteArchiveGroup : undefined;
  const canInspect = prompt.incompleteArchiveGroup == null && !prompt.archiveGroupInspected && prompt.allowedActions.length === 0;
  const canRetain = group && prompt.allowedActions.includes("retainIncompleteArchiveGroup");
  const submit = async () => {
    if (submitting || sending.current || (!canInspect && !canRetain)) return;
    sending.current = true;
    setPending(true);
    try {
      await onSubmit(JSON.stringify({
        targetId: prompt.targetId, artifactId: prompt.artifactId, fingerprint: prompt.fingerprint,
        stepId: prompt.knownStepId,
        action: canRetain ? "retainIncompleteArchiveGroup" : "inspectIncompleteArchiveGroup",
        ...(canRetain && { archiveGroupToken: group.token }),
      }));
    } finally {
      sending.current = false;
      setPending(false);
    }
  };

  return <div className="space-y-3 text-sm">
    <p className="font-medium text-warning-700">{t("workflow.waitReason.MissingVolumes")}</p>
    {group && <div className="space-y-1">
      <p className="font-medium">{t("workflow.fileRecovery.incompleteGroup.files", {
        files: group.memberCount, size: humanFileSize(group.totalBytes),
      })}</p>
      <ul className="list-disc break-all ps-5">
        {group.members.map(name => <li key={name}>{name}</li>)}
      </ul>
      {group.members.length < group.memberCount && <p className="text-xs text-default-500">
        {t("workflow.fileRecovery.incompleteGroup.more", { files: group.memberCount - group.members.length })}
      </p>}
      <p>{t("workflow.fileRecovery.incompleteGroup.completed", { groups: group.completedGroupCount })}</p>
    </div>}
    <p>{t(canRetain ? "workflow.fileRecovery.incompleteGroup.retainEffect" : canInspect
      ? "workflow.fileRecovery.incompleteGroup.inspectEffect" : "workflow.fileRecovery.incompleteGroup.unavailable")}</p>
    {(canInspect || canRetain) && <ResumeActions busy={submitting || pending}>
      <Button color="primary" isDisabled={submitting || pending} isLoading={submitting || pending} onPress={submit}>
        {t(canRetain ? "workflow.fileRecovery.incompleteGroup.retain" : "workflow.fileRecovery.incompleteGroup.inspect")}
      </Button>
    </ResumeActions>}
  </div>;
}
