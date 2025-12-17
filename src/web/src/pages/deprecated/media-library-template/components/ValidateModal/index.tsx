"use client";

import type { DestroyableProps } from "@/components/bakaui/types.ts";

import React, { useCallback } from "react";
import { useTranslation } from "react-i18next";
import moment from "moment";

import { Button, Chip, Input, Modal, Spinner } from "@/components/bakaui";
import envConfig from "@/config/env.ts";

type TraceContext = { key: string; value?: string | null };
type ValidationTrace = {
  level: number;
  time: string;
  topic: string;
  contexts?: TraceContext[];
};

type Props = DestroyableProps & {
  templateId: number;
};

const levelColorMap: Record<
  number,
  "default" | "primary" | "secondary" | "warning" | "danger" | "success"
> = {
  0: "default", // Trace
  1: "default", // Debug
  2: "default", // Information
  3: "warning", // Warning
  4: "danger", // Error
  5: "danger", // Critical
  6: "default", // None
};

const ValidateModal = ({ templateId, ...props }: Props) => {
  const { t } = useTranslation();
  const [visible, setVisible] = React.useState(true);
  // const [rootPath, setRootPath] = React.useState("Z:\\Movie");
  const [rootPath, setRootPath] = React.useState<string>();
  const [limit, setLimit] = React.useState<number | undefined>(3);
  const [keyword, setKeyword] = React.useState<string>("");
  const [running, setRunning] = React.useState(false);
  const [traces, setTraces] = React.useState<ValidationTrace[]>([]);
  const abortRef = React.useRef<AbortController | null>(null);

  const close = useCallback(() => {
    stop();
    setVisible(false);
  }, []);

  const start = async () => {
    if (!rootPath) return;
    setRunning(true);
    setTraces([]);
    const controller = new AbortController();

    abortRef.current = controller;

    try {
      const url = `${envConfig.apiEndpoint}/media-library-template/${templateId}/validate`;
      const resp = await fetch(url, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          rootPath,
          limitResourcesCount: limit,
          resourceKeyword: keyword || undefined,
        }),
        signal: controller.signal,
      });

      if (!resp.ok || !resp.body) {
        throw new Error(`Request failed: ${resp.status}`);
      }

      const reader = resp.body.getReader();
      const decoder = new TextDecoder();
      let buffer = "";

      while (true) {
        const { value, done } = await reader.read();

        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        let idx;

        while ((idx = buffer.indexOf("\n")) >= 0) {
          const line = buffer.slice(0, idx).trim();

          buffer = buffer.slice(idx + 1);
          if (!line) continue;
          try {
            const obj = JSON.parse(line) as ValidationTrace;

            setTraces((prev) => [...prev, obj]);
          } catch {
            // ignore broken line
          }
        }
      }
      if (buffer.trim().length > 0) {
        try {
          const obj = JSON.parse(buffer.trim()) as ValidationTrace;

          setTraces((prev) => [...prev, obj]);
        } catch {}
      }
    } catch (e) {
      // silently end on abort or error
    } finally {
      setRunning(false);
      abortRef.current = null;
    }
  };

  const stop = () => {
    abortRef.current?.abort();
    abortRef.current = null;
    setRunning(false);
  };

  return (
    <Modal
      className={"max-w-[80vw]"}
      footer={
        <div className="flex items-center gap-2">
          {!running ? (
            <Button color="primary" isDisabled={!rootPath} onPress={start}>
              {t<string>("Start")}
            </Button>
          ) : (
            <Button color="danger" variant="light" onPress={stop}>
              {t<string>("Stop")}
            </Button>
          )}
          <Button onPress={close}>{t<string>("Close")}</Button>
        </div>
      }
      size={"xl"}
      title={t<string>("Validate media library template")}
      visible={visible}
      onClose={close}
      onDestroyed={props.onDestroyed}
    >
      <div className="flex flex-col gap-3">
        <div className="grid gap-2" style={{ gridTemplateColumns: "160px 1fr" }}>
          <div className="self-center text-sm opacity-80">{t<string>("Root path")}</div>
          <Input
            placeholder={t<string>("Pick a directory or file path")}
            value={rootPath}
            onValueChange={setRootPath}
          />
          <div className="self-center text-sm opacity-80">{t<string>("Limit resources count")}</div>
          <Input
            type="number"
            value={(limit ?? 0).toString()}
            onValueChange={(v) => setLimit(v ? parseInt(v, 10) : undefined)}
          />
          <div className="self-center text-sm opacity-80">{t<string>("Resource keyword")}</div>
          <Input placeholder={t<string>("Optional")} value={keyword} onValueChange={setKeyword} />
        </div>

        <div className="h-[420px] overflow-auto rounded border-small p-2">
          <div className="grid gap-2" style={{ gridTemplateColumns: "auto 1fr" }}>
            {traces.map((tr, idx) => (
              <React.Fragment key={idx}>
                <div className="flex items-center gap-1">
                  <Chip color={"default"} size="sm" variant={"flat"}>
                    {moment(tr.time).format("HH:mm:ss.SSS")}
                  </Chip>
                  <Chip color={levelColorMap[tr.level] || "default"} size="sm" variant={"flat"}>
                    {tr.topic}
                  </Chip>
                </div>
                {(tr.contexts && tr.contexts.length > 0 && (
                  <div
                    className="grid gap-x-4 items-center text-xs"
                    style={{ gridTemplateColumns: "auto 1fr" }}
                  >
                    {tr.contexts.map((c, i) => (
                      <>
                        <div key={`${idx}-k-${i}`} className="opacity-70 break-all text-right">
                          {c.key}
                        </div>
                        <div key={`${idx}-v-${i}`} className={"break-all"}>
                          {c.value != undefined && JSON.stringify(c.value)}
                        </div>
                      </>
                    ))}
                  </div>
                )) || <div />}
              </React.Fragment>
            ))}
            {running && <Spinner color="default" label={t<string>("Validating...")} size="sm" />}
          </div>
        </div>
      </div>
    </Modal>
  );
};

ValidateModal.displayName = "ValidateModal";

export default ValidateModal;
