import { useRef, useState } from "react";
import type { DestroyableProps } from "@/components/bakaui/types";
import AcquisitionLocalBatchModal, { type LocalBatchSelection } from "./AcquisitionLocalBatchModal";
import AcquisitionAdaptiveProcessingModal from "./AcquisitionAdaptiveProcessingModal";
import { acquisitionActionLimits } from "../acquisitionSelection";
import AcquisitionRecoveryModal from "./AcquisitionRecoveryModal";
import AcquisitionContinuationJobsModal from "./AcquisitionContinuationJobsModal";

type Pane = "adaptive" | "automatic" | "localQueue" | "history" | "downloads";
/** One visible modal. Queue inspection never starts a task or carries an old acknowledgement. */
export default function AcquisitionContinueModal({ taskIds, scopeLabel, filteredAnalysis = false, onOpenTask, onChanged, onDestroyed }: DestroyableProps & {
  taskIds?: number[];
  scopeLabel?: string;
  filteredAnalysis?: boolean;
  onOpenTask?: (id: number) => void;
  onChanged?: () => void;
}) {
  const captured = useRef([...new Set((taskIds ?? []).filter(id => Number.isSafeInteger(id) && id > 0))]);
  const canAnalyze = captured.current.length > 0 && captured.current.length <= acquisitionActionLimits.adaptive;
  const generation = useRef(0);
  const selection = useRef<LocalBatchSelection>();
  const [navigation, setNavigation] = useState<{ pane: Pane; batchId?: string; generation: number }>({
    pane: captured.current.length ? (filteredAnalysis && canAnalyze ? "adaptive" : "automatic") : "localQueue", generation: 0,
  });
  const navigate = (pane: Pane, batchId?: string) => setNavigation({ pane, batchId, generation: ++generation.current });
  const destroyed = () => { if (navigation.generation === generation.current) onDestroyed?.(); };
  const shared = { onChanged, onDestroyed: destroyed };
  const back = captured.current.length ? () => navigate("automatic") : undefined;
  if (navigation.pane === "adaptive") return <AcquisitionAdaptiveProcessingModal key={navigation.generation} {...shared}
    taskIds={captured.current} scopeLabel={scopeLabel} onOpenTask={onOpenTask}
    onBackToSelection={back} />;
  if (navigation.pane === "automatic") return <AcquisitionLocalBatchModal key={navigation.generation} {...shared}
    automatic taskIds={captured.current} scopeLabel={scopeLabel}
    initialSelection={selection.current} onSelectionChange={value => { selection.current = value; }}
    onShowDownloads={() => navigate("downloads")}
    onShowAdaptive={canAnalyze ? () => navigate("adaptive") : undefined}
    onOpenQueue={queue => navigate(queue.kind === "localQueue" ? "localQueue" : "history", queue.batchId)} />;
  if (navigation.pane === "downloads") return <AcquisitionRecoveryModal key={navigation.generation} {...shared}
    taskIds={captured.current} scopeLabel={scopeLabel} initialScope="downloads" continuationScope="downloads"
    fixedScope onContinuationScopeChange={() => navigate("automatic")} />;
  if (navigation.pane === "history") return <AcquisitionContinuationJobsModal key={navigation.generation} {...shared}
    initialBatchId={navigation.batchId} onBackToSelection={back} onShowLocalQueues={() => navigate("localQueue")} />;
  return <AcquisitionLocalBatchModal key={navigation.generation} {...shared} initialBatchId={navigation.batchId}
    onBackToSelection={back} onShowContinuationHistory={() => navigate("history")} />;
}
