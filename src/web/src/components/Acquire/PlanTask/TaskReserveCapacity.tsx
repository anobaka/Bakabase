import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineReload } from "react-icons/ai";
import { Button, Spinner } from "@/components/bakaui";
import { humanFileSize } from "@/components/utils";
import { storageOverrideApi } from "./storageOverrideApi";

export default function TaskReserveCapacity({ taskId, workflowRunId, minimumBytes }: {
  taskId: number; workflowRunId: number; minimumBytes: number;
}) {
  const { t } = useTranslation();
  const [volumes, setVolumes] = useState<Awaited<ReturnType<typeof storageOverrideApi.capacity>>>();
  const [revision, refresh] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    setVolumes(undefined);
    storageOverrideApi.capacity(taskId, workflowRunId, controller.signal)
      .then(data => { if (!controller.signal.aborted) setVolumes(data); })
      .catch(() => { if (!controller.signal.aborted) setVolumes([]); });
    return () => controller.abort();
  }, [taskId, workflowRunId, revision]);

  return <section className="space-y-1 text-xs text-default-600" aria-label={t("acquisition.reserve.capacity.title")}>
    <div className="flex flex-wrap items-center gap-1">
      <span>{t("acquisition.reserve.capacity.title")}</span>
      <Button isIconOnly size="sm" variant="light" className="size-6 min-w-6"
        aria-label={t("acquisition.reserve.capacity.refresh")} isDisabled={!volumes}
        onPress={() => refresh(value => value + 1)}>
        {volumes ? <AiOutlineReload aria-hidden /> : <Spinner size="sm" />}
      </Button>
    </div>
    {volumes?.length === 0 && <p>{t("acquisition.reserve.capacity.unknown")}</p>}
    {volumes?.map(volume => {
      const estimate = volume.estimatedAdditionalBytes;
      const shortage = Math.max(0, minimumBytes + (estimate ?? 0) - volume.availableBytes);
      return <div key={volume.volumeId} className="space-y-1">
        {volumes.length > 1 && <p className="break-all text-default-500">{volume.paths[0]}</p>}
        <p>{t("acquisition.reserve.capacity.current", { available: humanFileSize(volume.availableBytes),
          minimum: humanFileSize(minimumBytes) })}{estimate != null && <> · {t("acquisition.reserve.capacity.estimate",
          { size: humanFileSize(estimate) })}</>}</p>
        {shortage > 0 && <p className="text-warning-700">{t(estimate == null ? "acquisition.reserve.capacity.reserveShortfall"
          : "acquisition.reserve.capacity.estimatedShortfall", { size: humanFileSize(shortage) })}</p>}
      </div>;
    })}
    {volumes?.some(volume => volume.estimateIncomplete) && <p className="text-default-500">{t("acquisition.reserve.capacity.estimateHint")}</p>}
  </section>;
}
