import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Textarea } from "@/components/bakaui";
import ProcessingChoiceGroup from "../../ProcessingChoiceGroup";
import ResumeActions from "../../ResumeActions";
import ProcessingPlanField, { isCompleteProcessingPlan } from "../../ProcessingPlanField";

type Policy = "instructions" | "keep" | "autoDetect";
type Choice = Policy | "ai";

function inspectPlan(json: string) {
  try { return JSON.parse(json); } catch { return null; }
}

function manualInstructions(saved?: string) {
  if (!saved) return JSON.stringify({ requirement: "required", steps: [], evidence: [] });
  const parsed = inspectPlan(saved);
  if (parsed && Array.isArray(parsed.steps) && ["notRequired", "unknown"].includes(parsed.requirement)) {
    return JSON.stringify({ ...parsed, requirement: "required" });
  }
  return saved;
}

/** One choice maps to one of the server-authorized answers for this wait. */
export default function TaskFilePlanDecision({ targetId, saved, policies, adaptive, pathConflict, submitting, onSubmit }: {
  targetId?: string;
  saved?: string;
  policies?: Policy[];
  adaptive: boolean;
  pathConflict: boolean;
  submitting: boolean;
  onSubmit: (signal: string) => void;
}) {
  const { t } = useTranslation();
  const choices = [
    { value: "instructions" as const, label: t("acquisition.processingChoice.manual"), icon: "manual" as const },
    { value: "keep" as const, label: t("acquisition.processingChoice.keep"), icon: "keep" as const },
    { value: "autoDetect" as const, label: t("cloudDownload.processing.policy.autoDetect"), icon: "detect" as const },
    { value: "ai" as const, label: t("acquisition.processingChoice.ai"), icon: "ai" as const },
  ].filter(({ value }) => {
    if (pathConflict) return value === "instructions" && (!policies || policies.includes(value));
    if (value === "ai") return adaptive;
    return policies ? policies.includes(value) : value !== "autoDetect";
  });
  const [choice, setChoice] = useState<Choice | undefined>(() => {
    if (choices.length === 1) return choices[0].value;
    if (adaptive && choices.some(o => o.value === "ai")) return "ai";
    if (inspectPlan(saved ?? "")?.requirement === "required" && choices.some(o => o.value === "instructions"))
      return "instructions";
    return undefined;
  });
  const [plan, setPlan] = useState(() => manualInstructions(saved));
  const [note, setNote] = useState("");
  const manualReady = choice === "instructions" && isCompleteProcessingPlan(plan) &&
    inspectPlan(plan)?.requirement === "required" && (!pathConflict || plan !== saved);
  const ready = !!targetId && choices.some(o => o.value === choice) &&
    (choice === "instructions" ? manualReady : choice === "ai" ? !!note.trim() : true);
  return (
    <div className="space-y-3">
      <ProcessingChoiceGroup label={t("workflow.processing.editor.requirement")} value={choice}
        choices={choices} onChange={setChoice} isDisabled={submitting} isRequired />
      {choice === "instructions" && (
        <ProcessingPlanField stepsOnly value={plan} onChange={setPlan} isDisabled={submitting} />
      )}
      {choice === "ai" && (
        <Textarea label={t("acquisition.continue.aiNote")} minRows={2} maxLength={16384}
          isRequired value={note} onValueChange={setNote} isDisabled={submitting} />
      )}
      <ResumeActions busy={submitting}>
        <Button size="sm" color="primary" isLoading={submitting} isDisabled={!ready || submitting}
          onPress={() => {
            if (!ready || submitting) return;
            const answer = choice === "ai" ? { userContext: note.trim() } :
              choice === "instructions" ? { extractionPlanJson: plan } : { archivePolicy: choice };
            onSubmit(JSON.stringify({ targetId, ...answer }));
          }}>
          {t("cloudDownload.processing.continue")}
        </Button>
      </ResumeActions>
    </div>
  );
}
