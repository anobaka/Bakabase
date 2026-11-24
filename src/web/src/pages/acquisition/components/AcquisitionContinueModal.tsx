import { useRef, useState } from "react";
import type { DestroyableProps } from "@/components/bakaui/types";
import AcquisitionLocalBatchModal, { type LocalBatchSelection } from "./AcquisitionLocalBatchModal";
import AcquisitionRecoveryModal from "./AcquisitionRecoveryModal";
import AcquisitionContinuationJobsModal from "./AcquisitionContinuationJobsModal";

type Pane = "automatic" | "localQueue" | "history" | "downloads" | "pausedOriginal";
/** One visible modal. Queue inspection never starts a task or carries an old acknowledgement. */
export default function AcquisitionContinueModal({
  taskIds,
  scopeLabel,
  onOpenTask,
  onChanged,
  onDestroyed,
}: DestroyableProps & {
  taskIds?: number[];
  scopeLabel?: string;
  filteredAnalysis?: boolean;
  onOpenTask?: (id: number) => void;
  onChanged?: () => void;
}) {
  const captured = useRef([
    ...new Set((taskIds ?? []).filter((id) => Number.isSafeInteger(id) && id > 0)),
  ]);
  const generation = useRef(0);
  const selection = useRef<LocalBatchSelection>();
  // Continuing the saved plan is always the safe default. Re-analysis is an
  // explicit choice inside that flow, never an implicit consequence of a
  // filtered selection.
  const [navigation, setNavigation] = useState<{
    pane: Pane;
    batchId?: string;
    taskId?: number;
    generation: number;
  }>({
    pane: captured.current.length ? "automatic" : "localQueue",
    generation: 0,
  });
  const navigate = (pane: Pane, batchId?: string) =>
    setNavigation({ pane, batchId, generation: ++generation.current });
  const destroyed = () => {
    if (navigation.generation === generation.current) onDestroyed?.();
  };
  const shared = { onChanged, onDestroyed: destroyed };
  const back = captured.current.length ? () => navigate("automatic") : undefined;
  if (navigation.pane === "automatic")
    return (
      <AcquisitionLocalBatchModal
        key={navigation.generation}
        {...shared}
        automatic
        taskIds={captured.current}
        scopeLabel={scopeLabel}
        onOpenTask={onOpenTask}
        initialSelection={selection.current}
        onSelectionChange={(value) => {
          selection.current = value;
        }}
        onInspectPausedOriginal={(taskId) =>
          setNavigation({ pane: "pausedOriginal", taskId, generation: ++generation.current })
        }
        onShowDownloads={() => navigate("downloads")}
        onOpenQueue={(queue) =>
          navigate(queue.kind === "localQueue" ? "localQueue" : "history", queue.batchId)
        }
      />
    );
  if (navigation.pane === "pausedOriginal" && navigation.taskId != null)
    return (
      <AcquisitionLocalBatchModal
        key={navigation.generation}
        {...shared}
        automatic
        taskIds={[navigation.taskId]}
        onBackToSelection={back}
        onOpenTask={onOpenTask}
      />
    );
  if (navigation.pane === "downloads")
    return (
      <AcquisitionRecoveryModal
        key={navigation.generation}
        {...shared}
        taskIds={captured.current}
        scopeLabel={scopeLabel}
        initialScope="downloads"
        continuationScope="downloads"
        fixedScope
        onContinuationScopeChange={() => navigate("automatic")}
      />
    );
  if (navigation.pane === "history")
    return (
      <AcquisitionContinuationJobsModal
        key={navigation.generation}
        {...shared}
        initialBatchId={navigation.batchId}
        onBackToSelection={back}
        onShowLocalQueues={() => navigate("localQueue")}
      />
    );
  return (
    <AcquisitionLocalBatchModal
      key={navigation.generation}
      {...shared}
      initialBatchId={navigation.batchId}
      onBackToSelection={back}
      onShowContinuationHistory={() => navigate("history")}
    />
  );
}
