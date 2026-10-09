import type { BakabaseServiceModelsViewResourceUsageViewModel } from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { LuActivity, LuCpu, LuHardDrive, LuMemoryStick } from "react-icons/lu";

import { formatUsageBytes } from "./formatUsage";

import { Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";

export default function ResourceUsage({ collapsed }: { collapsed?: boolean }) {
  const { t } = useTranslation();
  const enabled = useUiOptionsStore((s) => s.initialized && s.data.showResourceUsage !== false);
  const [usage, setUsage] = useState<BakabaseServiceModelsViewResourceUsageViewModel>();
  const [unavailable, setUnavailable] = useState(false);

  useEffect(() => {
    if (!enabled) return;
    let disposed = false;
    let pending = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let controller: AbortController | undefined;
    let deadline: ReturnType<typeof setTimeout> | undefined;

    const refresh = async () => {
      if (disposed || document.hidden || pending) return;
      pending = true;
      controller = new AbortController();
      deadline = setTimeout(() => controller?.abort(), 10000);
      try {
        const response = await BApi.app.getResourceUsage({
          signal: controller.signal,
          showErrorToast: false,
        });

        if (disposed) return;
        if (response.code || !response.data) throw new Error("Resource usage unavailable");
        setUsage(response.data);
        setUnavailable(false);
      } catch {
        if (!disposed) setUnavailable(true);
      } finally {
        clearTimeout(deadline);
        pending = false;
        if (!disposed && !document.hidden) timer = setTimeout(() => void refresh(), 5000);
      }
    };
    const onVisibilityChange = () => {
      clearTimeout(timer);
      if (!document.hidden) void refresh();
    };

    void refresh();
    document.addEventListener("visibilitychange", onVisibilityChange);

    return () => {
      disposed = true;
      controller?.abort();
      clearTimeout(deadline);
      clearTimeout(timer);
      document.removeEventListener("visibilitychange", onVisibilityChange);
    };
  }, [enabled]);

  if (!enabled) return null;

  const directoryStatus = usage?.dataDirectoryUnavailable
    ? t("resourceUsage.directoryUnavailable")
    : usage?.dataDirectoryPartial
      ? t("resourceUsage.directoryPartial")
      : usage?.dataDirectoryScanning
        ? t("resourceUsage.calculating")
        : t("resourceUsage.directoryHint");
  const metrics = [
    {
      label: "CPU",
      Icon: LuCpu,
      value: unavailable || usage?.cpuPercent == null ? "—" : `${usage.cpuPercent.toFixed(1)}%`,
      hint: t("resourceUsage.cpuHint"),
    },
    {
      label: t("resourceUsage.memory"),
      Icon: LuMemoryStick,
      value: unavailable ? "—" : formatUsageBytes(usage?.memoryBytes),
      hint: t("resourceUsage.memoryHint"),
    },
    {
      label: t("resourceUsage.data"),
      Icon: LuHardDrive,
      value: unavailable ? "—" : formatUsageBytes(usage?.dataDirectoryBytes),
      hint: `${directoryStatus}${usage?.dataDirectoryUpdatedAt ? ` · ${new Date(usage.dataDirectoryUpdatedAt).toLocaleTimeString()}` : ""}`,
    },
  ];
  const title = unavailable ? t("resourceUsage.unavailable") : t("resourceUsage.title");
  const details = (
    <div className="max-w-64 space-y-2 p-1 text-xs">
      <div className="font-medium">{title}</div>
      {metrics.map(({ label, value, hint }) => (
        <div key={label}>
          <div className="flex justify-between gap-5">
            <span>{label}</span>
            <span className="tabular-nums">{value}</span>
          </div>
          <div className="mt-0.5 text-foreground-400">{hint}</div>
        </div>
      ))}
    </div>
  );

  if (collapsed)
    return (
      <Tooltip content={details} placement="right">
        <button
          aria-label={title}
          className="mx-auto my-2 rounded-lg p-2 text-foreground-400 outline-none focus-visible:ring-2 focus-visible:ring-primary"
          type="button"
        >
          <LuActivity aria-hidden size={18} />
        </button>
      </Tooltip>
    );

  return (
    <section
      aria-label={t<string>("resourceUsage.title")}
      className="mx-3 mb-2 shrink-0 rounded-xl border border-default-200/50 bg-default-100/40 px-3 py-2"
    >
      <div className="mb-1.5 flex items-center gap-1.5 text-[10px] font-medium text-foreground-400">
        <LuActivity aria-hidden size={12} />
        {title}
      </div>
      <div className="space-y-1">
        {metrics.map(({ label, value, Icon, hint }) => (
          <Tooltip key={label} content={hint} placement="right">
            <button
              className="flex w-full items-center gap-2 rounded text-xs outline-none focus-visible:ring-2 focus-visible:ring-primary"
              tabIndex={0}
              type="button"
            >
              <Icon aria-hidden className="text-foreground-400" size={13} />
              <span className="text-foreground-500">{label}</span>
              <span className="ml-auto tabular-nums text-foreground-700">{value}</span>
            </button>
          </Tooltip>
        ))}
      </div>
    </section>
  );
}
