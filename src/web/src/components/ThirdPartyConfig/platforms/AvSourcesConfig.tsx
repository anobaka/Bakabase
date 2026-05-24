"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import i18next from "i18next";
import {
  AiOutlineCheck,
  AiOutlineCopy,
  AiOutlinePlayCircle,
  AiOutlineReload,
  AiOutlineSetting,
} from "react-icons/ai";

import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Input,
  Modal,
  Spinner,
  Switch,
  Textarea,
  Tooltip,
  toast,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { ContentType } from "@/sdk/Api";
import { AvEnhancerTarget, AvSourceIds, avEnhancerTargets } from "@/sdk/constants";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const TEST_NUMBER_STORAGE_KEY = "avSources.testNumber";

interface AvSourceInfo {
  id: string;
  defaultBaseUrl?: string;
  defaultCookie?: string;
  resolvedBaseUrl?: string;
  resolvedCookie?: string;
  enabled: boolean;
}

interface AvSourceConfig {
  enabled?: boolean | null;
  baseUrl?: string | null;
  cookie?: string | null;
  userAgent?: string | null;
}

interface AvSourceTestDetail {
  number?: string | null;
  title?: string | null;
  originalTitle?: string | null;
  actor?: string | null;
  tag?: string | null;
  release?: string | null;
  year?: string | null;
  studio?: string | null;
  publisher?: string | null;
  series?: string | null;
  runtime?: string | null;
  director?: string | null;
  source?: string | null;
  coverUrl?: string | null;
  posterUrl?: string | null;
  website?: string | null;
  mosaic?: string | null;
  searchUrl?: string | null;
}

interface AvSourceHttpInteraction {
  method: string;
  url: string;
  requestHeaders?: Record<string, string>;
  requestBody?: string | null;
  requestContentType?: string | null;
  responseStatusCode?: number | null;
  responseReasonPhrase?: string | null;
  responseHeaders?: Record<string, string> | null;
  responseContentType?: string | null;
  responseContentLength?: number | null;
  error?: string | null;
  durationMs: number;
}

interface AvSourceTestResult {
  source: string;
  detail?: AvSourceTestDetail | null;
  error?: string | null;
  skipped?: boolean;
  durationMs: number;
  interactions?: AvSourceHttpInteraction[] | null;
}

type SourceState =
  | { phase: "idle" }
  | { phase: "loading" }
  | { phase: "done"; result: AvSourceTestResult };

// Per-target ordered list of source ids; missing entry means "use built-in default order".
export type PreferredSourcesByTarget = Partial<Record<AvEnhancerTarget, string[]>>;

// Map an AvEnhancerTarget enum value to the field on AvSourceTestDetail it represents.
const targetToDetailField: Partial<Record<AvEnhancerTarget, keyof AvSourceTestDetail>> = {
  [AvEnhancerTarget.Number]: "number",
  [AvEnhancerTarget.Title]: "title",
  [AvEnhancerTarget.OriginalTitle]: "originalTitle",
  [AvEnhancerTarget.Actor]: "actor",
  [AvEnhancerTarget.Introduction]: "outline",
  [AvEnhancerTarget.Tags]: "tag",
  [AvEnhancerTarget.Release]: "release",
  [AvEnhancerTarget.Year]: "year",
  [AvEnhancerTarget.Studio]: "studio",
  [AvEnhancerTarget.Publisher]: "publisher",
  [AvEnhancerTarget.Series]: "series",
  [AvEnhancerTarget.Runtime]: "runtime",
  [AvEnhancerTarget.Director]: "director",
  [AvEnhancerTarget.Source]: "source",
  [AvEnhancerTarget.Cover]: "coverUrl",
  [AvEnhancerTarget.Poster]: "posterUrl",
  [AvEnhancerTarget.Website]: "website",
  [AvEnhancerTarget.Mosaic]: "mosaic",
};

const imageTargets = new Set<AvEnhancerTarget>([AvEnhancerTarget.Cover, AvEnhancerTarget.Poster]);

const cellValue = (
  target: AvEnhancerTarget,
  detail?: AvSourceTestDetail | null,
): string | undefined => {
  if (!detail) return undefined;
  const field = targetToDetailField[target];

  if (!field) return undefined;
  const v = detail[field];

  return v == null || v === "" ? undefined : String(v);
};

// Normalize API response: JSON object keys come back as strings — convert to numeric enum keys.
const parsePreferredFromApi = (
  raw: Record<string, string[]> | null | undefined,
): PreferredSourcesByTarget => {
  if (!raw) return {};
  const out: PreferredSourcesByTarget = {};

  for (const [k, v] of Object.entries(raw)) {
    const target = Number.parseInt(k, 10);

    if (!Number.isNaN(target) && Array.isArray(v)) {
      out[target as AvEnhancerTarget] = v;
    }
  }

  return out;
};

export const AvSourcesConfigPanel = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [sources, setSources] = useState<AvSourceInfo[]>([]);
  const [configs, setConfigs] = useState<Record<string, AvSourceConfig>>({});
  const [preferredSourcesByTarget, setPreferredSourcesByTarget] =
    useState<PreferredSourcesByTarget>({});
  const [number, setNumber] = useState(() => {
    if (typeof window === "undefined") return "";

    return localStorage.getItem(TEST_NUMBER_STORAGE_KEY) || "";
  });
  const [running, setRunning] = useState(false);
  const [results, setResults] = useState<Record<string, SourceState>>({});

  const loadSources = useCallback(async () => {
    const rsp = await BApi.request<{ data?: AvSourceInfo[] }>({
      path: "/av/sources",
      method: "GET",
      format: "json",
    });
    const fetched = rsp.data || [];
    // Honor AvSourceIds order (shipped via constants.ts), with stragglers appended.
    const order = new Map(AvSourceIds.map((id, idx) => [id, idx] as const));

    fetched.sort(
      (a, b) => (order.get(a.id) ?? 999) - (order.get(b.id) ?? 999) || a.id.localeCompare(b.id),
    );
    setSources(fetched);
  }, []);

  const loadConfig = useCallback(async () => {
    const rsp = await BApi.request<{
      data?: {
        sources?: Record<string, AvSourceConfig>;
        preferredSourcesByTarget?: Record<string, string[]>;
      };
    }>({
      path: "/options/av-sources",
      method: "GET",
      format: "json",
    });

    setConfigs(rsp.data?.sources || {});
    setPreferredSourcesByTarget(parsePreferredFromApi(rsp.data?.preferredSourcesByTarget));
  }, []);

  useEffect(() => {
    void loadSources();
    void loadConfig();
  }, [loadSources, loadConfig]);

  const saveConfig = async (id: string, patch: Partial<AvSourceConfig>) => {
    const next = { ...configs };
    const merged: AvSourceConfig = { ...(next[id] || {}), ...patch };

    next[id] = merged;
    setConfigs(next);
    await BApi.request({
      path: "/options/av-sources",
      method: "PATCH",
      body: { sources: next },
      type: ContentType.Json,
      format: "json",
    });
    await loadSources();
    toast.success(t("avSources.toast.saved", "Saved"));
  };

  const savePreferredSources = async (next: PreferredSourcesByTarget) => {
    setPreferredSourcesByTarget(next);
    await BApi.request({
      path: "/options/av-sources",
      method: "PATCH",
      body: { preferredSourcesByTarget: next },
      type: ContentType.Json,
      format: "json",
    });
  };

  const runAll = async () => {
    if (!number.trim()) {
      toast.danger(t("avSources.toast.numberRequired", "Please enter a number first"));

      return;
    }
    setRunning(true);
    const initial: Record<string, SourceState> = {};

    sources.forEach((s) => {
      initial[s.id] = { phase: "loading" };
    });
    setResults(initial);

    try {
      const rsp = await BApi.request<{ data?: AvSourceTestResult[] }>({
        path: "/av/test",
        method: "POST",
        body: { number: number.trim(), language: i18next.language },
        type: ContentType.Json,
        format: "json",
      });
      const next: Record<string, SourceState> = {};

      (rsp.data || []).forEach((r) => {
        next[r.source] = { phase: "done", result: r };
      });
      sources.forEach((s) => {
        if (!next[s.id]) {
          next[s.id] = {
            phase: "done",
            result: { source: s.id, durationMs: 0, error: "no response" },
          };
        }
      });
      setResults(next);
    } finally {
      setRunning(false);
    }
  };

  const runOne = async (id: string) => {
    if (!number.trim()) {
      toast.danger(t("avSources.toast.numberRequired", "Please enter a number first"));

      return;
    }
    setResults((prev) => ({ ...prev, [id]: { phase: "loading" } }));
    try {
      const rsp = await BApi.request<{ data?: AvSourceTestResult[] }>({
        path: "/av/test",
        method: "POST",
        body: { number: number.trim(), sources: [id], language: i18next.language },
        type: ContentType.Json,
        format: "json",
      });
      const result = rsp.data?.[0];

      if (result) {
        setResults((prev) => ({ ...prev, [id]: { phase: "done", result } }));
      } else {
        setResults((prev) => ({
          ...prev,
          [id]: { phase: "done", result: { source: id, durationMs: 0, error: "no response" } },
        }));
      }
    } catch (e: any) {
      setResults((prev) => ({
        ...prev,
        [id]: {
          phase: "done",
          result: { source: id, durationMs: 0, error: e?.message ?? String(e) },
        },
      }));
    }
  };

  const orderedSourceIds = useMemo(() => sources.map((s) => s.id), [sources]);

  // Per-target effective preferred list. Missing entry => use default order (all sources).
  const getEffectivePreferred = useCallback(
    (target: AvEnhancerTarget): string[] => {
      const explicit = preferredSourcesByTarget[target];

      if (explicit) return explicit;

      return orderedSourceIds;
    },
    [preferredSourcesByTarget, orderedSourceIds],
  );

  // Priority of a source within a target (1-based). 0 means "not selected".
  const priorityOf = useCallback(
    (target: AvEnhancerTarget, sourceId: string): number => {
      const list = getEffectivePreferred(target);
      const idx = list.indexOf(sourceId);

      return idx < 0 ? 0 : idx + 1;
    },
    [getEffectivePreferred],
  );

  const toggleCell = (target: AvEnhancerTarget, sourceId: string) => {
    const current = getEffectivePreferred(target);
    const isSelected = current.includes(sourceId);
    const nextList = isSelected ? current.filter((id) => id !== sourceId) : [...current, sourceId];

    void savePreferredSources({ ...preferredSourcesByTarget, [target]: nextList });
  };

  // Count of selected sources for a target (based on the effective preferred list).
  const selectedCountForTarget = useCallback(
    (target: AvEnhancerTarget): number => getEffectivePreferred(target).length,
    [getEffectivePreferred],
  );

  // Click the column header to flip between "all selected" and "all cleared".
  // - If every source has a priority for this target → set to [] (skip target).
  // - Otherwise → set to a full explicit list in display order.
  const toggleTargetColumn = (target: AvEnhancerTarget) => {
    const count = selectedCountForTarget(target);
    const allSelected = count === orderedSourceIds.length && count > 0;
    const nextList = allSelected ? [] : orderedSourceIds.slice();

    void savePreferredSources({ ...preferredSourcesByTarget, [target]: nextList });
  };

  const selectAllEverything = () => {
    const next: PreferredSourcesByTarget = {};

    for (const tgt of avEnhancerTargets) {
      next[tgt.value as AvEnhancerTarget] = orderedSourceIds.slice();
    }
    void savePreferredSources(next);
  };

  const resetToDefault = () => {
    void savePreferredSources({});
  };

  // Convert shift+vertical-wheel into horizontal scroll on the matrix container.
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = scrollRef.current;

    if (!el) return;
    const onWheel = (e: WheelEvent) => {
      if (e.shiftKey && e.deltaY !== 0) {
        e.preventDefault();
        el.scrollLeft += e.deltaY;
      }
    };

    el.addEventListener("wheel", onWheel, { passive: false });

    return () => el.removeEventListener("wheel", onWheel);
  }, []);

  const openSourceConfigModal = (s: AvSourceInfo) => {
    createPortal(SourceConfigModal, {
      source: s,
      config: configs[s.id] || {},
      onSave: async (patch: Partial<AvSourceConfig>) => {
        await saveConfig(s.id, patch);
      },
    });
  };

  return (
    <div className="flex flex-col gap-3 h-full">
      <div className="flex items-center gap-2 flex-wrap">
        <Input
          className="max-w-[280px]"
          placeholder={t("avSources.input.numberPlaceholder", "Enter a number, e.g. SSIS-001")}
          size="sm"
          value={number}
          onValueChange={(v) => {
            setNumber(v);
            localStorage.setItem(TEST_NUMBER_STORAGE_KEY, v);
          }}
        />
        <Button
          color="primary"
          isDisabled={!number.trim() || sources.length === 0}
          isLoading={running}
          size="sm"
          startContent={<AiOutlinePlayCircle />}
          onPress={runAll}
        >
          {t("avSources.button.testAll", "Test all sources")}
        </Button>
        <Button
          size="sm"
          startContent={<AiOutlineReload />}
          variant="flat"
          onPress={() => {
            void loadSources();
            void loadConfig();
          }}
        >
          {t("avSources.button.refresh", "Refresh")}
        </Button>
        <div className="mx-2 h-5 w-px bg-default-200" />
        <Button size="sm" variant="flat" onPress={selectAllEverything}>
          {t("avSources.button.selectAll", "Select all")}
        </Button>
        <Button size="sm" variant="flat" onPress={resetToDefault}>
          {t("avSources.button.resetDefault", "Reset to default")}
        </Button>
        <div className="ml-auto text-xs text-default-500">
          {t("avSources.helper.summary", "{{count}} sources discovered", { count: sources.length })}
        </div>
      </div>

      <div className="text-xs text-default-500">
        {t(
          "avSources.helper.matrixHint",
          "Click a target name to select-all / clear that column. Hold Shift while scrolling to move horizontally.",
        )}
      </div>

      <div ref={scrollRef} className="flex-1 overflow-auto border border-default-200 rounded-md">
        <table className="text-xs border-collapse min-w-full">
          <thead>
            <tr>
              <th className="sticky left-0 top-0 z-30 bg-default-100 px-3 py-2 text-left border-b border-r border-default-200 min-w-[220px]">
                {t("avSources.column.source", "Source")}
              </th>
              {avEnhancerTargets.map((tgt) => {
                const target = tgt.value as AvEnhancerTarget;
                const count = selectedCountForTarget(target);
                const total = orderedSourceIds.length;
                const allSelected = count === total && count > 0;
                const allCleared = count === 0;

                return (
                  <th
                    key={tgt.value}
                    className="sticky top-0 z-20 bg-default-100 px-2 py-2 text-left border-b border-r border-default-200 min-w-[140px] select-none cursor-pointer hover:bg-default-200"
                    onClick={() => toggleTargetColumn(target)}
                  >
                    <div className="flex items-center gap-1 justify-between">
                      <span className="font-medium">{tgt.label}</span>
                      <span
                        className={`text-[10px] tabular-nums ${
                          allSelected
                            ? "text-primary"
                            : allCleared
                              ? "text-default-400"
                              : "text-warning"
                        }`}
                      >
                        {count}/{total}
                      </span>
                    </div>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {sources.map((s) => {
              const state = results[s.id];
              const cfg = configs[s.id] || {};
              const disabledByConfig = cfg.enabled === false || !s.enabled;
              const rowError = state?.phase === "done" ? state.result.error : undefined;
              const rowSkipped = state?.phase === "done" ? state.result.skipped : false;
              const isLoading = state?.phase === "loading";
              const detail = state?.phase === "done" ? state.result.detail : undefined;

              return (
                <tr
                  key={s.id}
                  className={
                    disabledByConfig
                      ? "opacity-40"
                      : rowError
                        ? "bg-danger-50/40"
                        : rowSkipped
                          ? "bg-default-100/60"
                          : "hover:bg-default-50"
                  }
                >
                  <th className="sticky left-0 z-10 bg-background px-3 py-2 text-left border-b border-r border-default-200 align-top">
                    <SourceCell
                      config={cfg}
                      numberSet={!!number.trim()}
                      source={s}
                      state={state}
                      onConfigure={() => openSourceConfigModal(s)}
                      onTest={() => runOne(s.id)}
                    />
                  </th>
                  {avEnhancerTargets.map((tgt) => {
                    const target = tgt.value as AvEnhancerTarget;
                    const priority = priorityOf(target, s.id);
                    const checked = priority > 0;
                    const value = cellValue(target, detail);

                    return (
                      <td
                        key={tgt.value}
                        className="px-2 py-2 border-b border-r border-default-200 align-top"
                      >
                        <Cell
                          checked={checked}
                          interactive={!disabledByConfig}
                          isImage={imageTargets.has(target)}
                          loading={isLoading}
                          priority={priority}
                          rowError={rowError}
                          rowSkipped={rowSkipped}
                          target={target}
                          value={value}
                          onToggle={() => toggleCell(target, s.id)}
                        />
                      </td>
                    );
                  })}
                </tr>
              );
            })}
            {sources.length === 0 && (
              <tr>
                <td
                  className="px-3 py-6 text-center text-default-500"
                  colSpan={avEnhancerTargets.length + 1}
                >
                  {t("avSources.empty.noSources", "No sources available")}
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};

interface SourceCellProps {
  source: AvSourceInfo;
  config: AvSourceConfig;
  state?: SourceState;
  onTest: () => void;
  onConfigure: () => void;
  numberSet: boolean;
}

const SourceCell = ({ source, config, state, onTest, onConfigure, numberSet }: SourceCellProps) => {
  const { t } = useTranslation();
  const disabledByConfig = config.enabled === false || !source.enabled;
  const error = state?.phase === "done" ? state.result.error : undefined;
  const skipped = state?.phase === "done" ? state.result.skipped : false;
  const duration = state?.phase === "done" ? state.result.durationMs : undefined;
  const interactions = state?.phase === "done" ? (state.result.interactions ?? []) : [];

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-1.5 flex-wrap">
        <Tooltip content={t("avSources.button.configure", "Configure")}>
          <Button
            isIconOnly
            aria-label={t("avSources.button.configure", "Configure")}
            size="sm"
            variant="light"
            onPress={onConfigure}
          >
            <AiOutlineSetting className="text-lg" />
          </Button>
        </Tooltip>
        <Tooltip content={t("avSources.button.test", "Test")}>
          <Button
            isIconOnly
            aria-label={t("avSources.button.test", "Test")}
            isDisabled={!numberSet}
            isLoading={state?.phase === "loading"}
            size="sm"
            variant="light"
            onPress={onTest}
          >
            <AiOutlinePlayCircle className="text-lg" />
          </Button>
        </Tooltip>
        <span className="font-medium">{source.id}</span>
        {disabledByConfig && (
          <Chip color="default" size="sm" variant="flat">
            {t("avSources.chip.disabled", "disabled")}
          </Chip>
        )}
        {duration != null && <span className="text-[10px] text-default-500">{duration}ms</span>}
      </div>
      {error && (
        <Tooltip content={error}>
          <span className="text-[10px] text-danger truncate max-w-[200px]" title={error}>
            {error}
          </span>
        </Tooltip>
      )}
      {skipped && !error && (
        <span className="text-[10px] text-default-500">
          {t("avSources.result.skipped", "Skipped (disabled)")}
        </span>
      )}
      {interactions.length > 0 && (
        <Accordion isCompact>
          <AccordionItem
            key="reqs"
            aria-label={t("avSources.accordion.requests", "HTTP requests")}
            classNames={{ trigger: "py-0", title: "text-[11px] text-default-500" }}
            title={
              <span>
                {t("avSources.accordion.requests", "HTTP requests")}
                <span className="ml-1 text-default-400">({interactions.length})</span>
              </span>
            }
          >
            <div className="space-y-2 pt-1">
              {interactions.map((i, idx) => (
                <InteractionItem key={idx} i={i} index={idx} />
              ))}
            </div>
          </AccordionItem>
        </Accordion>
      )}
    </div>
  );
};

interface CellProps {
  target: AvEnhancerTarget;
  value?: string;
  isImage: boolean;
  checked: boolean;
  priority: number;
  interactive: boolean;
  loading: boolean;
  rowError?: string | null;
  rowSkipped?: boolean;
  onToggle: () => void;
}

const Cell = ({
  value,
  isImage,
  checked,
  priority,
  interactive,
  loading,
  rowError,
  rowSkipped,
  onToggle,
}: CellProps) => {
  return (
    <div className="flex flex-col gap-1">
      {interactive && (
        <button
          className={`self-start inline-flex items-center gap-1 rounded px-1.5 py-0.5 border text-[10px] transition-colors ${
            checked
              ? "border-primary text-primary bg-primary-50"
              : "border-default-300 text-default-500 hover:border-default-400"
          }`}
          type="button"
          onClick={onToggle}
        >
          {checked ? (
            <>
              <AiOutlineCheck className="text-[10px]" />
              <span>#{priority}</span>
            </>
          ) : (
            <span>·</span>
          )}
        </button>
      )}
      <CellBody
        isImage={isImage}
        loading={loading}
        rowError={rowError}
        rowSkipped={rowSkipped}
        value={value}
      />
    </div>
  );
};

interface CellBodyProps {
  loading: boolean;
  rowError?: string | null;
  rowSkipped?: boolean;
  value?: string;
  isImage: boolean;
}

const CellBody = ({ loading, rowError, rowSkipped, value, isImage }: CellBodyProps) => {
  if (loading) return <Spinner size="sm" />;
  if (rowError) return <span className="text-danger text-base leading-none">✕</span>;
  if (rowSkipped) return <span className="text-default-300 text-base leading-none">·</span>;
  if (!value) return <span className="text-default-300">—</span>;

  if (isImage) {
    return (
      <a className="block" href={value} rel="noreferrer" target="_blank">
        <img alt="" className="max-h-16 max-w-[120px] rounded object-contain" src={value} />
      </a>
    );
  }

  // Truncate long text with tooltip showing full content
  if (value.length > 32) {
    return (
      <Tooltip content={<div className="max-w-[400px] whitespace-pre-wrap break-all">{value}</div>}>
        <span className="break-all line-clamp-3">{value}</span>
      </Tooltip>
    );
  }

  return <span className="break-all">{value}</span>;
};

interface SourceConfigModalProps {
  source: AvSourceInfo;
  config: AvSourceConfig;
  onSave: (patch: Partial<AvSourceConfig>) => Promise<void>;
  onDestroyed?: () => void;
}

const SourceConfigModal = ({ source, config, onSave, onDestroyed }: SourceConfigModalProps) => {
  const { t } = useTranslation();
  const [enabled, setEnabled] = useState(config.enabled ?? true);
  const [baseUrl, setBaseUrl] = useState(config.baseUrl ?? "");
  const [cookie, setCookie] = useState(config.cookie ?? "");
  const [userAgent, setUserAgent] = useState(config.userAgent ?? "");

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel", "ok"] }}
      size="lg"
      title={t("avSources.modal.sourceConfig", "Source: {{id}}", { id: source.id })}
      onDestroyed={onDestroyed}
      onOk={async () => {
        await onSave({
          enabled,
          baseUrl: baseUrl || null,
          cookie: cookie || null,
          userAgent: userAgent || null,
        });
      }}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <span className="text-sm">{t("avSources.field.enabled", "Enabled")}</span>
          <Switch isSelected={enabled} size="sm" onValueChange={setEnabled} />
        </div>
        <Input
          label={t("avSources.field.baseUrl", "Base URL")}
          placeholder={source.defaultBaseUrl}
          size="sm"
          value={baseUrl}
          onValueChange={setBaseUrl}
        />
        <Textarea
          description={
            source.defaultCookie
              ? t("avSources.field.cookieDefaultHint", "Default applied: {{cookie}}", {
                  cookie: source.defaultCookie,
                })
              : undefined
          }
          label={t("avSources.field.cookie", "Cookie")}
          minRows={2}
          placeholder={
            source.defaultCookie || t("avSources.field.cookiePlaceholder", "name=value; ...")
          }
          size="sm"
          value={cookie}
          onValueChange={setCookie}
        />
        <Input
          label={t("avSources.field.userAgent", "User-Agent")}
          placeholder={t("avSources.field.userAgentPlaceholder", "Optional override")}
          size="sm"
          value={userAgent}
          onValueChange={setUserAgent}
        />
      </div>
    </Modal>
  );
};

const copyToClipboard = (text: string) => {
  if (typeof navigator !== "undefined" && navigator.clipboard) {
    void navigator.clipboard.writeText(text).then(
      () => toast.success("Copied"),
      () => toast.danger("Copy failed"),
    );
  }
};

const buildCurl = (i: AvSourceHttpInteraction) => {
  const parts: string[] = ["curl"];

  if (i.method && i.method.toUpperCase() !== "GET") parts.push(`-X ${i.method.toUpperCase()}`);
  for (const [k, v] of Object.entries(i.requestHeaders || {})) {
    parts.push(`-H '${k}: ${String(v).replace(/'/g, "'\\''")}'`);
  }
  if (i.requestBody) {
    parts.push(`--data-raw '${i.requestBody.replace(/'/g, "'\\''")}'`);
  }
  parts.push(`'${i.url}'`);

  return parts.join(" ");
};

const InteractionItem = ({ index, i }: { index: number; i: AvSourceHttpInteraction }) => {
  const { t } = useTranslation();
  const statusColor =
    i.error || (i.responseStatusCode && i.responseStatusCode >= 400)
      ? "danger"
      : i.responseStatusCode && i.responseStatusCode >= 300
        ? "warning"
        : "success";

  return (
    <div className="rounded-md border border-default-200 p-2 text-[11px] space-y-1">
      <div className="flex items-center gap-1 flex-wrap">
        <span className="text-default-400">#{index + 1}</span>
        <Chip size="sm" variant="flat">
          {i.method.toUpperCase()}
        </Chip>
        {i.responseStatusCode != null && (
          <Chip color={statusColor} size="sm" variant="flat">
            {i.responseStatusCode}
          </Chip>
        )}
        <span className="text-default-500">{i.durationMs}ms</span>
        <Button
          isIconOnly
          aria-label="curl"
          size="sm"
          variant="light"
          onPress={() => copyToClipboard(buildCurl(i))}
        >
          <AiOutlineCopy />
        </Button>
      </div>
      <a
        className="block break-all text-primary underline"
        href={i.url}
        rel="noreferrer"
        target="_blank"
      >
        {i.url}
      </a>
      {i.error && <div className="text-danger break-all">{i.error}</div>}
    </div>
  );
};

export default AvSourcesConfigPanel;
