import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Chip, NumberInput, Switch } from "@/components/bakaui";
import type { BaiduAccount } from "./BaiduNetdiskConfig";

type Settings = NonNullable<BaiduAccount["downloadOptions"]>;

export default function BaiduDownloadSettings({
  account,
  busy,
  onSave,
  onRefresh,
}: {
  account: BaiduAccount;
  busy: boolean;
  onSave: (settings: Settings) => void;
  onRefresh: () => void;
}) {
  const { t } = useTranslation();
  const savedTasks = account.downloadOptions?.maxConcurrentTasks ?? 1;
  const savedConnections = account.downloadOptions?.connectionsPerFile ?? 1;
  const savedConfirmation = account.downloadOptions?.userConfirmedSvip ?? false;
  const [tasks, setTasks] = useState(savedTasks);
  const [connections, setConnections] = useState(savedConnections);
  const [confirmed, setConfirmed] = useState(savedConfirmation);
  useEffect(() => {
    setTasks(savedTasks);
    setConnections(savedConnections);
    setConfirmed(savedConfirmation);
  }, [account.id, savedTasks, savedConnections, savedConfirmation]);
  const membership = account.membership;
  const kind = membership?.kind ?? "unknown";
  const valid =
    Number.isInteger(tasks) &&
    tasks >= 1 &&
    tasks <= 5 &&
    Number.isInteger(connections) &&
    connections >= 1 &&
    connections <= 20;
  const dirty =
    tasks !== savedTasks || connections !== savedConnections || confirmed !== savedConfirmation;
  const disabled = busy || !account.isLoggedIn;

  return (
    <section
      aria-label={t("cloudDownload.baidu.concurrency.title")}
      className="space-y-3 border-t border-default-200 pt-3"
    >
      <div className="flex flex-wrap items-center gap-2 text-xs">
        <span className="font-medium">{t("cloudDownload.baidu.membership.label")}</span>
        <Chip
          size="sm"
          variant="flat"
          color={kind === "svip" && !membership?.failureCode ? "success" : "default"}
        >
          {t(`cloudDownload.baidu.membership.${kind}`)}
        </Chip>
        {membership?.checkedAt && (
          <span className="text-default-500">
            {t("cloudDownload.baidu.membership.checked", {
              time: new Date(membership.checkedAt).toLocaleString(),
            })}
          </span>
        )}
        <Button size="sm" variant="light" isDisabled={disabled} onPress={onRefresh}>
          {t("cloudDownload.baidu.membership.refresh")}
        </Button>
        {membership?.failureCode && (
          <span role="status" className="text-warning">
            {t("cloudDownload.baidu.membership.failed")}
          </span>
        )}
      </div>
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <NumberInput
          size="sm"
          label={t("cloudDownload.baidu.concurrency.tasks")}
          description={t("cloudDownload.baidu.concurrency.tasksHint")}
          minValue={1}
          maxValue={5}
          step={1}
          value={tasks}
          onValueChange={setTasks}
          isDisabled={disabled}
        />
        <NumberInput
          size="sm"
          label={t("cloudDownload.baidu.concurrency.connections")}
          description={t("cloudDownload.baidu.concurrency.connectionsHint")}
          minValue={1}
          maxValue={20}
          step={1}
          value={connections}
          onValueChange={setConnections}
          isDisabled={disabled}
        />
      </div>
      <Switch size="sm" isSelected={confirmed} onValueChange={setConfirmed} isDisabled={disabled}>
        {t("cloudDownload.baidu.concurrency.confirmSvip")}
      </Switch>
      <p className="text-xs text-default-500">
        {t("cloudDownload.baidu.concurrency.membershipHint")}
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          size="sm"
          color="primary"
          isDisabled={disabled || !valid || !dirty}
          onPress={() =>
            onSave({
              maxConcurrentTasks: tasks,
              connectionsPerFile: connections,
              userConfirmedSvip: confirmed,
            })
          }
        >
          {t("cloudDownload.baidu.concurrency.save")}
        </Button>
        <span className="text-xs text-default-500">
          {t("cloudDownload.baidu.concurrency.effective", {
            tasks: account.effectiveMaxConcurrentTasks ?? 1,
            connections: account.effectiveConnectionsPerFile ?? 1,
          })}
        </span>
      </div>
      <p className="text-xs text-default-500">{t("cloudDownload.baidu.concurrency.storageHint")}</p>
    </section>
  );
}
