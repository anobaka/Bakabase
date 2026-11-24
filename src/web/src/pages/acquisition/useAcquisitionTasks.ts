import { useCallback, useEffect, useRef, useState } from "react";
import BApi from "@/sdk/BApi";
import { normalizeAcquisitionFilters } from "./acquisitionFilters";

type Query = Parameters<typeof BApi.acquisition.searchAcquisitionTasks>[0];
type Page = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.searchAcquisitionTasks>>["data"]
>;

/** Only the current page is loaded. Polls preserve rows; a superseded filter cannot supply batch IDs. */
export default function useAcquisitionTasks(query: Query, keywordPending = false) {
  const { attentionTag: _legacyTag, ...otherQuery } = query;
  const normalizedQuery = { ...otherQuery, ...normalizeAcquisitionFilters(query) };
  const key = JSON.stringify(normalizedQuery);
  // Each dictionary only applies to the filters the service counted. Paging and sorting affect none.
  const stageSearch = [query.stage ?? "all", query.keyword?.trim() ?? ""];
  const runtimeSelection = [query.capabilityId ?? "", query.executionState ?? ""];
  const runtimeScope = JSON.stringify([...stageSearch, query.downloadSource ?? ""]);
  const tagScope = JSON.stringify([runtimeScope, ...runtimeSelection]);
  const sourceScope = JSON.stringify([...stageSearch, ...runtimeSelection,
    normalizedQuery.attentionTags, normalizedQuery.facts]);
  const requestQuery = { key, runtimeScope, tagScope, sourceScope, query: normalizedQuery };
  const queryRef = useRef(requestQuery);
  queryRef.current = requestQuery;
  const sequence = useRef(0);
  const pending = useRef<AbortController>();
  const [snapshot, setSnapshot] = useState<{ requestQuery: typeof requestQuery; data: Page }>();
  const [failedKey, setFailedKey] = useState<string>();
  const failed = failedKey === key;
  const [loading, setLoading] = useState(true);
  const load = useCallback(async () => {
    const request = ++sequence.current;
    pending.current?.abort();
    const controller = new AbortController();
    pending.current = controller;
    const captured = queryRef.current;
    setLoading(true);
    try {
      const result = await BApi.acquisition.searchAcquisitionTasks(captured.query, {
        signal: controller.signal,
        sensitiveResponse: true,
        showErrorToast: false,
      });
      if (request !== sequence.current || controller.signal.aborted) return;
      if (result.code || !result.data) throw new Error("ACQUISITION_SEARCH_FAILED");
      setSnapshot({ requestQuery: captured, data: result.data });
      setFailedKey(undefined);
    } catch {
      if (request === sequence.current && !controller.signal.aborted) setFailedKey(captured.key);
    } finally {
      if (request === sequence.current && !controller.signal.aborted) setLoading(false);
    }
  }, []);
  useEffect(() => {
    void load();
    const timer = setInterval(() => void load(), 5000);
    return () => {
      sequence.current++;
      pending.current?.abort();
      clearInterval(timer);
    };
  }, [key, load]);
  const current = snapshot?.requestQuery.key === key;
  const confirmed = !failed && !keywordPending ? snapshot : undefined;
  const runtime = confirmed?.requestQuery.runtimeScope === runtimeScope ? confirmed.data : undefined;
  const tags = confirmed?.requestQuery.tagScope === tagScope ? confirmed.data : undefined;
  const sources = confirmed?.requestQuery.sourceScope === sourceScope ? confirmed.data : undefined;
  return {
    data: current ? snapshot.data : undefined,
    stageCounts: failed ? undefined : snapshot?.data.stageCounts,
    downloadSourceCounts: sources?.downloadSourceCounts,
    queueTaskCount: snapshot?.data.queueTaskCount ?? 0,
    attentionCounts: tags?.attentionCounts,
    factCounts: tags?.factCounts,
    runtimeCapabilityCounts: runtime?.runtimeCapabilityCounts,
    runtimeExecutionStateCounts: runtime?.runtimeExecutionStateCounts,
    // A selected capability can keep its provider label while its new scope loads.
    runtimeCapabilities: snapshot?.data.runtimeCapabilities,
    taskRuntimeFacets: current ? snapshot.data.taskRuntimeFacets : undefined,
    loading: loading || (!current && !failed),
    failed,
    refresh: load,
  };
}
