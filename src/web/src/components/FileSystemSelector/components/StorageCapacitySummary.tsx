import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineReload } from "react-icons/ai";
import { Button, Progress, Spinner, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type Capacity = { status: string; availableBytes?: number; totalBytes?: number; volumePath?: string };
type Snapshot = { path: string; loading: boolean; data?: Capacity };
const gib = 1024 ** 3;
const tib = 1024 ** 4;

export function formatStorageCapacity(bytes: number, locale?: string) {
  const unit = bytes >= tib ? "TiB" : "GiB";
  return `${new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(bytes / (unit === "TiB" ? tib : gib))} ${unit}`;
}

/** Reads the selected Bakabase path's volume, never the browser device's storage. */
export default function StorageCapacitySummary({ path, emptyHint }: { path?: string | null; emptyHint?: string }) {
  const { t, i18n } = useTranslation();
  const selectedPath = path?.trim() ?? "";
  const requestId = useRef(0);
  const [refresh, setRefresh] = useState(0);
  const [snapshot, setSnapshot] = useState<Snapshot>();
  useEffect(() => {
    const request = ++requestId.current;
    if (!selectedPath) { setSnapshot(undefined); return; }
    const controller = new AbortController();
    let active = true;
    let timedOut = false;
    let deadline: ReturnType<typeof setTimeout> | undefined;
    setSnapshot({ path: selectedPath, loading: true });
    const timer = setTimeout(async () => {
      const publish = (data?: Capacity) => {
        if (active && !timedOut && request === requestId.current)
          setSnapshot({ path: selectedPath, loading: false, data });
      };
      deadline = setTimeout(() => { publish(); timedOut = true; controller.abort(); }, 5000);
      try {
        const result = await BApi.file.getPathStorageCapacity({ path: selectedPath }, {
          showErrorToast: false, signal: controller.signal,
        });
        publish(!result.code && result.data ? result.data : undefined);
      } catch { publish(); }
      finally { clearTimeout(deadline); }
    }, 300);
    return () => {
      active = false; requestId.current++; clearTimeout(timer); clearTimeout(deadline); controller.abort();
    };
  }, [selectedPath, refresh]);

  if (!selectedPath)
    return <p className="text-xs text-default-500">{emptyHint ?? t("fileSystemSelector.capacity.unknown")}</p>;
  const current = snapshot?.path === selectedPath ? snapshot : undefined;
  const data = current?.data;
  const available = data?.availableBytes;
  const total = data?.totalBytes;
  const valid = data?.status === "available" && Number.isFinite(available) && Number.isFinite(total) &&
    available! >= 0 && total! > 0 && available! <= total!;
  const percent = valid ? available! / total! * 100 : undefined;
  const loading = !current || current.loading;
  const text = loading ? t("fileSystemSelector.capacity.loading") : valid
    ? t("fileSystemSelector.capacity.summary", {
        available: formatStorageCapacity(available!, i18n?.language), total: formatStorageCapacity(total!, i18n?.language),
        percent: new Intl.NumberFormat(i18n?.language, { maximumFractionDigits: 1 }).format(percent!),
      })
    : t(data?.status === "unavailable" ? "fileSystemSelector.capacity.unavailable" : "fileSystemSelector.capacity.unknown");
  return <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-1 text-xs text-default-500" aria-live="polite">
    <Tooltip content={t("fileSystemSelector.capacity.scope")} placement="bottom" delay={1000}>
      <span className="inline-flex min-w-0 items-center gap-1.5">
        {loading && <Spinner size="sm" aria-label={t("fileSystemSelector.capacity.loading")} />}
        <span className={data?.status === "unavailable" ? "text-warning" : undefined}>{text}</span>
      </span>
    </Tooltip>
    {valid && <Progress aria-label={t("fileSystemSelector.capacity.remaining")} size="sm" value={percent}
      color="default" className="w-20" />}
    <Button isIconOnly size="sm" variant="light" className="h-6 min-h-6 w-6 min-w-6" isDisabled={loading}
      aria-label={t("fileSystemSelector.capacity.refresh")} onPress={() => setRefresh(value => value + 1)}>
      <AiOutlineReload aria-hidden />
    </Button>
  </div>;
}
