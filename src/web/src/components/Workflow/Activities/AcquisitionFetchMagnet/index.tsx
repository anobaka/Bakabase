"use client";

import type { WorkflowActivityUI } from "../types";

import React from "react";
import { useTranslation } from "react-i18next";

import { Input, Select } from "@/components/bakaui";
import { WorkflowActivityCategory } from "@/sdk/constants";

/** Mirrors the backend FetchMagnetStep.Handler enum. */
enum Handler {
  Builtin = 0,
  Aria2 = 1,
  SystemDefault = 2,
}

/** Mirrors the backend FetchMagnetStep.Config record. */
interface Config {
  handler: Handler;
  rpcUrl: string;
  secret?: string;
  timeoutMinutes: number;
  pollSeconds: number;
}

const DEFAULT: Config = {
  handler: Handler.Builtin,
  rpcUrl: "http://127.0.0.1:6800/jsonrpc",
  timeoutMinutes: 240,
  pollSeconds: 5,
};

const ConfigForm: React.FC<{ value: Config; onChange: (v: Config) => void }> = ({
  value,
  onChange,
}) => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-3">
      <Select
        disallowEmptySelection
        dataSource={[
          {
            value: String(Handler.Builtin),
            label: t<string>("workflow.acquisition.fetchMagnet.handler.builtin"),
            textValue: t<string>("workflow.acquisition.fetchMagnet.handler.builtin"),
          },
          {
            value: String(Handler.Aria2),
            label: t<string>("workflow.acquisition.fetchMagnet.handler.aria2"),
            textValue: t<string>("workflow.acquisition.fetchMagnet.handler.aria2"),
          },
          {
            value: String(Handler.SystemDefault),
            label: t<string>("workflow.acquisition.fetchMagnet.handler.system"),
            textValue: t<string>("workflow.acquisition.fetchMagnet.handler.system"),
          },
        ]}
        description={t<string>(
          value.handler === Handler.SystemDefault
            ? "workflow.acquisition.fetchMagnet.handler.system.description"
            : value.handler === Handler.Aria2
              ? "workflow.acquisition.fetchMagnet.handler.aria2.description"
              : "workflow.acquisition.fetchMagnet.handler.builtin.description",
        )}
        label={t<string>("workflow.acquisition.fetchMagnet.handler.label")}
        selectedKeys={[String(value.handler ?? Handler.Builtin)]}
        size="sm"
        onSelectionChange={(keys) => {
          const raw = Array.from(keys)[0];

          if (raw != null) onChange({ ...value, handler: Number(raw) as Handler });
        }}
      />

      {value.handler === Handler.Aria2 && (
        <>
          <Input
            label={t<string>("workflow.acquisition.fetchMagnet.rpcUrl.label")}
            size="sm"
            value={value.rpcUrl ?? DEFAULT.rpcUrl}
            onValueChange={(rpcUrl) => onChange({ ...value, rpcUrl })}
          />
          <Input
            description={t<string>("workflow.acquisition.fetchMagnet.secret.description")}
            label={t<string>("workflow.acquisition.fetchMagnet.secret.label")}
            size="sm"
            type="password"
            value={value.secret ?? ""}
            onValueChange={(secret) => onChange({ ...value, secret })}
          />
          <Input
            label={t<string>("workflow.acquisition.fetchMagnet.pollSeconds.label")}
            max={60}
            min={1}
            size="sm"
            type="number"
            value={String(value.pollSeconds)}
            onValueChange={(raw) => {
              const next = Number(raw);

              if (Number.isFinite(next))
                onChange({ ...value, pollSeconds: Math.min(60, Math.max(1, next)) });
            }}
          />
        </>
      )}
      {value.handler !== Handler.SystemDefault && (
        <Input
          description={t<string>("workflow.acquisition.fetchMagnet.timeout.description")}
          label={t<string>("workflow.acquisition.fetchMagnet.timeout.label")}
          max={43200}
          min={1}
          size="sm"
          type="number"
          value={String(value.timeoutMinutes ?? DEFAULT.timeoutMinutes)}
          onValueChange={(v) => {
            const n = Number(v);

            if (Number.isFinite(n))
              onChange({ ...value, timeoutMinutes: Math.min(43200, Math.max(1, n)) });
          }}
        />
      )}
    </div>
  );
};

const Summary: React.FC<{ config: Config }> = ({ config }) => {
  const { t } = useTranslation();

  return (
    <span className="text-xs text-default-500">
      {t<string>(
        config.handler === Handler.SystemDefault
          ? "workflow.acquisition.fetchMagnet.handler.system"
          : config.handler === Handler.Aria2
            ? "workflow.acquisition.fetchMagnet.handler.aria2"
            : "workflow.acquisition.fetchMagnet.handler.builtin",
      )}
    </span>
  );
};

export const AcquisitionFetchMagnetUI: WorkflowActivityUI<Config> = {
  kind: "acquisition.fetchMagnet",
  displayNameKey: "workflow.acquisition.step.fetchMagnet",
  category: WorkflowActivityCategory.Action,
  defaultConfig: () => ({ ...DEFAULT }),
  parseConfig: (json) => {
    if (!json) return { ...DEFAULT };
    try {
      return { ...DEFAULT, ...(JSON.parse(json) as Partial<Config>) };
    } catch {
      return { ...DEFAULT };
    }
  },
  serializeConfig: (config) => JSON.stringify(config),
  // An aria2 endpoint that is not there is a runtime failure with a clear message; an empty one is
  // a mistake the editor can catch.
  isValid: (config) =>
    [Handler.Builtin, Handler.Aria2, Handler.SystemDefault].includes(config.handler) &&
    (config.handler === Handler.SystemDefault ||
      (config.timeoutMinutes >= 1 && config.timeoutMinutes <= 43200)) &&
    (config.handler !== Handler.Aria2 ||
      ((config.rpcUrl?.trim().length ?? 0) > 0 &&
        config.pollSeconds >= 1 &&
        config.pollSeconds <= 60)),
  ConfigForm,
  Summary,
};
