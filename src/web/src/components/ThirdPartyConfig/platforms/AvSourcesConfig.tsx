"use client";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import i18next from "i18next";
import { AiOutlineCopy, AiOutlinePlayCircle, AiOutlineReload, AiOutlineSetting } from "react-icons/ai";

import {
  Accordion,
  AccordionItem,
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Divider,
  Input,
  Spinner,
  Switch,
  Textarea,
  toast,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { ContentType } from "@/sdk/Api";

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

export const AvSourcesConfigPanel = () => {
  const { t } = useTranslation();
  const [sources, setSources] = useState<AvSourceInfo[]>([]);
  const [configs, setConfigs] = useState<Record<string, AvSourceConfig>>({});
  const [number, setNumber] = useState(() => {
    if (typeof window === "undefined") return "";
    return localStorage.getItem(TEST_NUMBER_STORAGE_KEY) || "";
  });
  const [running, setRunning] = useState(false);
  const [savingId, setSavingId] = useState<string | null>(null);
  const [results, setResults] = useState<Record<string, SourceState>>({});

  const loadSources = async () => {
    const rsp = await BApi.request<{ data?: AvSourceInfo[] }>({
      path: "/av/sources",
      method: "GET",
      format: "json",
    });
    setSources((rsp.data || []).slice().sort((a, b) => a.id.localeCompare(b.id)));
  };

  const loadConfig = async () => {
    const rsp = await BApi.request<{ data?: { sources?: Record<string, AvSourceConfig> } }>({
      path: "/options/av-sources",
      method: "GET",
      format: "json",
    });
    setConfigs(rsp.data?.sources || {});
  };

  useEffect(() => {
    void loadSources();
    void loadConfig();
  }, []);

  const saveConfig = async (id: string, patch: Partial<AvSourceConfig>) => {
    const next = { ...configs };
    const merged: AvSourceConfig = { ...(next[id] || {}), ...patch };
    next[id] = merged;
    setConfigs(next);
    setSavingId(id);
    try {
      await BApi.request({
        path: "/options/av-sources",
        method: "PATCH",
        body: { sources: next },
        type: ContentType.Json,
        format: "json",
      });
      // Refresh resolved values so the UI reflects backend resolution.
      await loadSources();
      toast.success(t("avSources.toast.saved", "Saved"));
    } finally {
      setSavingId(null);
    }
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
          next[s.id] = { phase: "done", result: { source: s.id, durationMs: 0, error: "no response" } };
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
        [id]: { phase: "done", result: { source: id, durationMs: 0, error: e?.message ?? String(e) } },
      }));
    }
  };

  const sortedSources = useMemo(() => sources, [sources]);

  return (
    <div className="space-y-4">
      <Card>
        <CardHeader className="flex items-center justify-between gap-4 flex-wrap">
          <div className="flex items-center gap-2 flex-1 min-w-[280px]">
            <Input
              size="sm"
              placeholder={t("avSources.input.numberPlaceholder", "Enter a number, e.g. SSIS-001")}
              value={number}
              onValueChange={(v) => {
                setNumber(v);
                localStorage.setItem(TEST_NUMBER_STORAGE_KEY, v);
              }}
            />
            <Button
              color="primary"
              startContent={<AiOutlinePlayCircle />}
              onPress={runAll}
              isLoading={running}
              isDisabled={!number.trim() || sortedSources.length === 0}
            >
              {t("avSources.button.testAll", "Test all sources")}
            </Button>
            <Button
              variant="flat"
              startContent={<AiOutlineReload />}
              onPress={() => {
                void loadSources();
                void loadConfig();
              }}
            >
              {t("avSources.button.refresh", "Refresh")}
            </Button>
          </div>
          <div className="text-xs text-default-500">
            {t("avSources.helper.summary", "{{count}} sources discovered", { count: sortedSources.length })}
          </div>
        </CardHeader>
      </Card>

      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-3">
        {sortedSources.map((s) => {
          const state = results[s.id];
          const cfg = configs[s.id] || {};
          return (
            <Card key={s.id} shadow="sm">
              <CardHeader className="flex items-center justify-between gap-2">
                <div className="flex items-center gap-2">
                  <span className="font-medium">{s.id}</span>
                  {!s.enabled && (
                    <Chip size="sm" color="default" variant="flat">
                      {t("avSources.chip.disabled", "disabled")}
                    </Chip>
                  )}
                  {s.defaultCookie ? (
                    <Chip size="sm" color="success" variant="flat">
                      {t("avSources.chip.bypass", "auto bypass detection")}
                    </Chip>
                  ) : null}
                </div>
                <div className="flex items-center gap-1">
                  <Button
                    size="sm"
                    variant="flat"
                    isLoading={state?.phase === "loading"}
                    isDisabled={!number.trim()}
                    onPress={() => runOne(s.id)}
                    startContent={<AiOutlinePlayCircle />}
                  >
                    {t("avSources.button.test", "Test")}
                  </Button>
                </div>
              </CardHeader>
              <Divider />
              <CardBody className="space-y-3">
                <Accordion isCompact>
                  <AccordionItem
                    key="config"
                    aria-label={t("avSources.accordion.config", "Configuration")}
                    title={
                      <span className="flex items-center gap-2 text-sm">
                        <AiOutlineSetting /> {t("avSources.accordion.config", "Configuration")}
                      </span>
                    }
                  >
                    <div className="space-y-2 pt-2">
                      <div className="flex items-center justify-between">
                        <span className="text-xs">{t("avSources.field.enabled", "Enabled")}</span>
                        <Switch
                          size="sm"
                          isSelected={cfg.enabled ?? true}
                          isDisabled={savingId === s.id}
                          onValueChange={(v) => void saveConfig(s.id, { enabled: v })}
                        />
                      </div>
                      <Input
                        size="sm"
                        label={t("avSources.field.baseUrl", "Base URL")}
                        placeholder={s.defaultBaseUrl}
                        value={cfg.baseUrl ?? ""}
                        onValueChange={(v) => void saveConfig(s.id, { baseUrl: v || null })}
                      />
                      <Textarea
                        size="sm"
                        minRows={2}
                        label={t("avSources.field.cookie", "Cookie")}
                        placeholder={s.defaultCookie || t("avSources.field.cookiePlaceholder", "name=value; ...")}
                        description={
                          s.defaultCookie
                            ? t("avSources.field.cookieDefaultHint", "Default applied: {{cookie}}", {
                                cookie: s.defaultCookie,
                              })
                            : undefined
                        }
                        value={cfg.cookie ?? ""}
                        onValueChange={(v) => void saveConfig(s.id, { cookie: v || null })}
                      />
                      <Input
                        size="sm"
                        label={t("avSources.field.userAgent", "User-Agent")}
                        placeholder={t("avSources.field.userAgentPlaceholder", "Optional override")}
                        value={cfg.userAgent ?? ""}
                        onValueChange={(v) => void saveConfig(s.id, { userAgent: v || null })}
                      />
                    </div>
                  </AccordionItem>
                </Accordion>
                <ResultPanel state={state} />
                <InteractionsPanel state={state} />
              </CardBody>
            </Card>
          );
        })}
      </div>
    </div>
  );
};

const ResultPanel = ({ state }: { state?: SourceState }) => {
  const { t } = useTranslation();
  if (!state || state.phase === "idle") {
    return <div className="text-xs text-default-400">{t("avSources.result.idle", "Not tested yet")}</div>;
  }
  if (state.phase === "loading") {
    return (
      <div className="flex items-center gap-2 text-xs text-default-500">
        <Spinner size="sm" /> {t("avSources.result.loading", "Querying...")}
      </div>
    );
  }
  const r = state.result;
  if (r.skipped) {
    return <div className="text-xs text-default-400">{t("avSources.result.skipped", "Skipped (disabled)")}</div>;
  }
  if (r.error) {
    return (
      <div className="rounded-md bg-danger-50 p-2 text-xs text-danger">
        <div>{t("avSources.result.error", "Error")} ({r.durationMs}ms)</div>
        <div className="mt-1 break-all">{r.error}</div>
      </div>
    );
  }
  if (!r.detail) {
    return (
      <div className="rounded-md bg-warning-50 p-2 text-xs text-warning">
        {t("avSources.result.empty", "No data ({{ms}}ms)", { ms: r.durationMs })}
      </div>
    );
  }
  const d = r.detail;
  return (
    <div className="space-y-1 text-xs">
      <div className="flex items-center gap-2">
        <Chip size="sm" color="success" variant="flat">
          {t("avSources.result.ok", "OK")}
        </Chip>
        <span className="text-default-500">{r.durationMs}ms</span>
      </div>
      {d.coverUrl ? (
        // eslint-disable-next-line @next/next/no-img-element
        <img src={d.coverUrl} alt="cover" className="max-h-32 rounded-md object-contain" />
      ) : null}
      <Field label={t("avSources.field.number", "Number")} value={d.number} />
      <Field label={t("avSources.field.title", "Title")} value={d.title} />
      <Field label={t("avSources.field.actor", "Actor")} value={d.actor} />
      <Field label={t("avSources.field.tag", "Tag")} value={d.tag} />
      <Field label={t("avSources.field.studio", "Studio")} value={d.studio} />
      <Field label={t("avSources.field.publisher", "Publisher")} value={d.publisher} />
      <Field label={t("avSources.field.release", "Release")} value={d.release} />
      <Field label={t("avSources.field.runtime", "Runtime")} value={d.runtime} />
      {d.website ? (
        <a className="text-primary underline break-all" href={d.website} target="_blank" rel="noreferrer">
          {d.website}
        </a>
      ) : null}
    </div>
  );
};

const Field = ({ label, value }: { label: string; value?: string | null }) => {
  if (!value) return null;
  return (
    <div className="flex gap-2">
      <span className="shrink-0 text-default-500">{label}:</span>
      <span className="break-all">{value}</span>
    </div>
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

const InteractionsPanel = ({ state }: { state?: SourceState }) => {
  const { t } = useTranslation();
  if (!state || state.phase !== "done") return null;
  const interactions = state.result.interactions;
  if (!interactions || interactions.length === 0) return null;
  return (
    <Accordion isCompact>
      <AccordionItem
        key="requests"
        aria-label={t("avSources.accordion.requests", "HTTP requests")}
        title={
          <span className="text-sm">
            {t("avSources.accordion.requests", "HTTP requests")}
            <span className="ml-1 text-default-400">({interactions.length})</span>
          </span>
        }
      >
        <div className="space-y-3 pt-1">
          {interactions.map((i, idx) => (
            <InteractionItem key={idx} index={idx} i={i} />
          ))}
        </div>
      </AccordionItem>
    </Accordion>
  );
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
    <div className="rounded-md border border-default-200 p-2 text-xs space-y-2">
      <div className="flex items-center gap-2 flex-wrap">
        <span className="text-default-400">#{index + 1}</span>
        <Chip size="sm" variant="flat">
          {i.method.toUpperCase()}
        </Chip>
        {i.responseStatusCode != null && (
          <Chip size="sm" color={statusColor} variant="flat">
            {i.responseStatusCode} {i.responseReasonPhrase}
          </Chip>
        )}
        {i.error && (
          <Chip size="sm" color="danger" variant="flat">
            {t("avSources.interaction.failed", "failed")}
          </Chip>
        )}
        <span className="text-default-500">{i.durationMs}ms</span>
        <Button
          size="sm"
          variant="light"
          startContent={<AiOutlineCopy />}
          onPress={() => copyToClipboard(buildCurl(i))}
        >
          curl
        </Button>
      </div>
      <a
        href={i.url}
        target="_blank"
        rel="noreferrer"
        className="block break-all text-primary underline"
      >
        {i.url}
      </a>
      {i.requestHeaders && Object.keys(i.requestHeaders).length > 0 && (
        <div>
          <div className="text-default-500 mb-0.5">{t("avSources.interaction.headers", "Headers")}</div>
          <div className="rounded bg-default-100 p-2 font-mono whitespace-pre-wrap break-all">
            {Object.entries(i.requestHeaders)
              .map(([k, v]) => `${k}: ${v}`)
              .join("\n")}
          </div>
        </div>
      )}
      {i.requestBody && (
        <div>
          <div className="text-default-500 mb-0.5">
            {t("avSources.interaction.body", "Body")}
            {i.requestContentType ? <span className="ml-1 text-default-400">({i.requestContentType})</span> : null}
          </div>
          <div className="rounded bg-default-100 p-2 font-mono whitespace-pre-wrap break-all">{i.requestBody}</div>
        </div>
      )}
      {i.error && <div className="text-danger break-all">{i.error}</div>}
    </div>
  );
};

export default AvSourcesConfigPanel;
