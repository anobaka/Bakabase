import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Autocomplete, AutocompleteItem } from "@/components/bakaui";
import type { PlanTask } from "@/components/Acquire/PlanTask/models";
import { acquisitionStageLabel } from "@/components/Acquire/taskStage";
import BApi from "@/sdk/BApi";

export default function AcquisitionTaskSearch({
  value,
  onValueChange,
  onSearch,
  onOpenTask,
}: {
  value: string;
  onValueChange: (value: string) => void;
  onSearch: () => void;
  onOpenTask: (task: PlanTask) => void;
}) {
  const { t } = useTranslation();
  const [candidate, setCandidate] = useState<PlanTask>();
  const [suggestionsDismissed, setSuggestionsDismissed] = useState(false);
  const navigatedCandidate = useRef(false);
  const idText = /^#(\d+)$/.exec(value.trim())?.[1];
  const id = idText ? Number(idText) : undefined;
  const candidateId = id && Number.isSafeInteger(id) && id <= 2147483647 ? id : undefined;

  useEffect(() => {
    setCandidate(undefined);
    if (!candidateId) return;
    let current = true;
    const timer = window.setTimeout(async () => {
      try {
        const result = await BApi.acquisition.getAcquisition(candidateId, {
          sensitiveResponse: true,
        });
        if (current && result && !result.code && result.data?.id === candidateId)
          setCandidate(result.data);
      } catch {
        // A failed optional suggestion must not prevent ordinary text search.
      }
    }, 250);
    return () => {
      current = false;
      window.clearTimeout(timer);
    };
  }, [candidateId]);

  const matchingCandidate =
    !suggestionsDismissed && candidate?.id === candidateId ? candidate : undefined;
  return (
    <div
      className="w-full sm:w-52 sm:shrink-0"
      onKeyDownCapture={(event) => {
        if (event.nativeEvent.isComposing) return;
        if (event.key === "ArrowDown" || event.key === "ArrowUp") navigatedCandidate.current = true;
        if (event.key === "Enter" && !navigatedCandidate.current) {
          // HeroUI can highlight an asynchronously arriving option. Plain Enter
          // must still search; only deliberate option navigation selects it.
          event.preventDefault();
          event.stopPropagation();
          setSuggestionsDismissed(true);
          onSearch();
        }
      }}
    >
      <Autocomplete
        className="w-full"
        size="sm"
        aria-label={t("acquisition.tasks.search")}
        placeholder={t("acquisition.tasks.search")}
        allowsCustomValue
        menuTrigger="input"
        disableAnimation
        selectedKey={null}
        inputValue={value}
        items={matchingCandidate ? [matchingCandidate] : []}
        onInputChange={(text) => {
          navigatedCandidate.current = false;
          setSuggestionsDismissed(false);
          onValueChange(text);
        }}
        onClose={() => {
          navigatedCandidate.current = false;
        }}
        onKeyDown={(event) => {
          if (event.key === "Enter" && !event.nativeEvent.isComposing) onSearch();
        }}
        onSelectionChange={(key) => {
          if (matchingCandidate && String(key) === String(matchingCandidate.id))
            onOpenTask(matchingCandidate);
        }}
        isClearable
        onClear={() => onValueChange("")}
        selectorButtonProps={{ className: "hidden" }}
        popoverProps={{ placement: "bottom-start", classNames: { content: "w-80 max-w-[90vw]" } }}
        listboxProps={{
          autoFocus: false,
          bottomContent: matchingCandidate ? (
            <span className="px-2 pb-1 text-xs text-default-500">
              {t("acquisition.tasks.searchCandidateHint")}
            </span>
          ) : undefined,
        }}
      >
        {(task) => (
          <AutocompleteItem
            key={task.id}
            textValue={value}
            description={`${acquisitionStageLabel(t, task)} · ${t("acquisition.tasks.openSearchCandidate")}`}
            classNames={{ title: "whitespace-normal break-words" }}
          >
            #{task.id} · {task.title || task.resourceName || `#${task.id}`}
          </AutocompleteItem>
        )}
      </Autocomplete>
    </div>
  );
}
