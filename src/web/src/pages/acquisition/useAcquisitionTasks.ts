import { useCallback, useEffect, useRef, useState } from "react";
import BApi from "@/sdk/BApi";
import { normalizeAcquisitionFilters } from "./acquisitionFilters";

type Query = Parameters<typeof BApi.acquisition.searchAcquisitionTasks>[0];
type Page = NonNullable<
  Awaited<ReturnType<typeof BApi.acquisition.searchAcquisitionTasks>>["data"]
>;

/** Only the current page is loaded. Polls preserve rows; a superseded filter cannot supply batch IDs. */
export default function useAcquisitionTasks(query: Query) {
  const { attentionTag: _legacyTag, ...otherQuery } = query;
  const normalizedQuery = { ...otherQuery, ...normalizeAcquisitionFilters(query) };
  const key = JSON.stringify(normalizedQuery);
  // Facet counts intentionally ignore the AND selection, pagination and ordering.
  // Keep them available while the user adds another condition to a pending request.
  const facetKey = JSON.stringify([query.keyword ?? "", query.stage ?? "all", query.downloadSource ?? ""]);
  const queryRef = useRef({ key, facetKey, query: normalizedQuery });
  queryRef.current = { key, facetKey, query: normalizedQuery };
  const sequence = useRef(0);
  const pending = useRef<AbortController>();
  const [snapshot, setSnapshot] = useState<{ key: string; facetKey: string; data: Page }>();
  const [failed, setFailed] = useState(false);
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
      setSnapshot({ key: captured.key, facetKey: captured.facetKey, data: result.data });
      setFailed(false);
    } catch {
      if (request === sequence.current && !controller.signal.aborted) setFailed(true);
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
  const current = snapshot?.key === key;
  return {
    data: current ? snapshot.data : undefined,
    stageCounts: snapshot?.data.stageCounts,
    queueTaskCount: snapshot?.data.queueTaskCount ?? 0,
    attentionCounts: snapshot?.facetKey === facetKey ? snapshot.data.attentionCounts : undefined,
    factCounts: snapshot?.facetKey === facetKey ? snapshot.data.factCounts : undefined,
    runtimeCapabilityCounts: snapshot?.facetKey === facetKey ? snapshot.data.runtimeCapabilityCounts : undefined,
    runtimeExecutionStateCounts: snapshot?.facetKey === facetKey ? snapshot.data.runtimeExecutionStateCounts : undefined,
    runtimeCapabilities: snapshot?.facetKey === facetKey ? snapshot.data.runtimeCapabilities : undefined,
    taskRuntimeFacets: current ? snapshot.data.taskRuntimeFacets : undefined,
    loading: loading || (!current && !failed),
    failed,
    refresh: load,
  };
}
