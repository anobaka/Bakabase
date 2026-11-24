import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineBranches, AiOutlineDownload, AiOutlineRobot } from "react-icons/ai";
import { Button, Tooltip } from "@/components/bakaui";
import { acquisitionFilterCount } from "../acquisitionFilters";

type RuntimeFacet = {
  capabilityId?: string;
  capabilityName?: string;
  capabilityNameKey?: string;
};

function runtimeChoices(counts: Record<string, number> | null | undefined, ids: Iterable<string>, selected?: string) {
  const choices = new Set([...Object.keys(counts ?? {}), ...ids]);
  if (selected) choices.add(selected);
  return [...choices].map(id => ({ id, count: acquisitionFilterCount(counts, id) }))
    .filter(choice => choice.count !== 0 || choice.id === selected);
}

function iconFor(id: string) {
  if (id.startsWith("ai.")) return <AiOutlineRobot aria-hidden />;
  if (id === "download" || id.startsWith("download.")) return <AiOutlineDownload aria-hidden />;
  return <AiOutlineBranches aria-hidden />;
}

export default function AcquisitionRuntimeCapabilityFilters({
  capabilityCounts,
  executionStateCounts,
  capabilities,
  executionStates = [],
  facets,
  capabilityId,
  executionState,
  onCapabilityChange,
  onExecutionStateChange,
  disabled = false,
}: {
  capabilityCounts?: Record<string, number> | null;
  executionStateCounts?: Record<string, number> | null;
  capabilities?: RuntimeFacet[] | null;
  executionStates?: string[];
  facets?: Record<string, RuntimeFacet> | null;
  capabilityId?: string;
  executionState?: string;
  onCapabilityChange: (value?: string) => void;
  onExecutionStateChange: (value?: string) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const labels = useMemo(() => {
    const result = new Map<string, RuntimeFacet>();
    (capabilities ?? []).forEach((facet) => {
      if (facet.capabilityId && !result.has(facet.capabilityId)) result.set(facet.capabilityId, facet);
    });
    Object.values(facets ?? {}).forEach((facet) => {
      if (facet.capabilityId && !result.has(facet.capabilityId)) result.set(facet.capabilityId, facet);
    });
    return result;
  }, [capabilities, facets]);
  const capabilityChoices = runtimeChoices(capabilityCounts, labels.keys(), capabilityId);
  const states = runtimeChoices(executionStateCounts, executionStates, executionState);
  if (!capabilityChoices.length && !states.length) return null;

  const capabilityLabel = (id: string) => {
    const facet = labels.get(id);
    return facet?.capabilityNameKey
      ? t(facet.capabilityNameKey, { defaultValue: facet.capabilityName || id })
      : facet?.capabilityName || id;
  };

  return (
    <div role="group" aria-label={t("acquisition.runtime.filters.title")} className="flex flex-wrap items-center gap-1">
      <span className="mr-1 text-xs text-default-500">{t("acquisition.runtime.filters.title")}</span>
      {capabilityChoices.map(({ id, count }) => {
        const selected = capabilityId === id;
        const label = capabilityLabel(id);
        return (
          <Tooltip key={`capability:${id}`} content={label} delay={1000} placement="bottom">
            <Button
              aria-label={`${label} ${count ?? "—"}`}
              aria-pressed={selected}
              className="h-7 min-w-0 gap-1.5 px-2 text-xs"
              size="sm"
              variant={selected ? "flat" : "light"}
              color={selected ? "primary" : "default"}
              startContent={iconFor(id)}
              isDisabled={!selected && (disabled || count == null)}
              onPress={() => onCapabilityChange(selected ? undefined : id)}
            >
              <span>{label}</span>
              <span className="tabular-nums">{count ?? "—"}</span>
            </Button>
          </Tooltip>
        );
      })}
      {states.map(({ id: state, count }) => {
        const selected = executionState === state;
        const fallback = state.length ? `${state[0].toUpperCase()}${state.slice(1)}` : state;
        const key = `acquisition.runtime.executionState.${state}`;
        const translated = t(key, { defaultValue: fallback });
        // Keep a useful label when a host embeds the component before loading its locale bundle.
        const label = translated === key ? fallback : translated;
        return (
          <Tooltip key={`state:${state}`} content={label} delay={1000} placement="bottom">
            <Button
              aria-label={`${label} ${count ?? "—"}`}
              aria-pressed={selected}
              className="h-7 min-w-0 gap-1.5 px-2 text-xs"
              size="sm"
              variant={selected ? "flat" : "light"}
              color={selected ? "primary" : "default"}
              isDisabled={!selected && (disabled || count == null)}
              onPress={() => onExecutionStateChange(selected ? undefined : state)}
            >
              <span>{label}</span>
              <span className="tabular-nums">{count ?? "—"}</span>
            </Button>
          </Tooltip>
        );
      })}
      {(capabilityId || executionState) && (
        <Button
          size="sm"
          variant="light"
          className="h-7 min-w-0 px-2 text-xs"
          onPress={() => {
            onCapabilityChange(undefined);
            onExecutionStateChange(undefined);
          }}
        >
          {t("acquisition.runtime.filters.clear")}
        </Button>
      )}
    </div>
  );
}
