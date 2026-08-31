"use client";

import type { BakabaseAbstractionsModelsDbResourceMoveRecordDbModel } from "@/sdk/Api";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineArrowRight } from "react-icons/ai";
import {
  ClearOutlined,
  DeleteOutlined,
  QuestionCircleOutlined,
  RedoOutlined,
  ReloadOutlined,
} from "@ant-design/icons";
import moment from "moment";

import { Button, Chip, Spinner, Tooltip, toast } from "@/components/bakaui";
import { BTaskType, ResourceMoveRecordStatus } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import BApi from "@/sdk/BApi";

type MoveRecord = BakabaseAbstractionsModelsDbResourceMoveRecordDbModel;

const StatusChipColors: Record<
  ResourceMoveRecordStatus,
  "default" | "primary" | "success" | "danger" | "warning"
> = {
  [ResourceMoveRecordStatus.Pending]: "default",
  [ResourceMoveRecordStatus.Moving]: "primary",
  [ResourceMoveRecordStatus.Succeeded]: "success",
  [ResourceMoveRecordStatus.Failed]: "danger",
  [ResourceMoveRecordStatus.Cancelled]: "warning",
  [ResourceMoveRecordStatus.Interrupted]: "warning",
};

const retryableStatuses = new Set([
  ResourceMoveRecordStatus.Failed,
  ResourceMoveRecordStatus.Cancelled,
  ResourceMoveRecordStatus.Interrupted,
]);

const activeStatuses = new Set([ResourceMoveRecordStatus.Pending, ResourceMoveRecordStatus.Moving]);

/**
 * Durable move records with per-record retry. The list re-fetches when any MoveResources
 * task changes status (id:status fingerprint), not on percentage ticks, so a running move
 * doesn't hammer the API every 500ms push.
 */
const MoveRecordsTab = () => {
  const { t } = useTranslation();
  const [records, setRecords] = useState<MoveRecord[]>();
  const [working, setWorking] = useState(false);

  const moveTasksFingerprint = useBTasksStore((s) =>
    s.tasks
      .filter((x) => x.type === BTaskType.MoveResources)
      .map((x) => `${x.id}:${x.status}`)
      .sort()
      .join("|"),
  );

  const load = useCallback(async () => {
    const rsp = await BApi.resourceMove.getResourceMoveRecords();

    setRecords(rsp.data ?? []);
  }, []);

  useEffect(() => {
    load();
  }, [load, moveTasksFingerprint]);

  const retry = async (record: MoveRecord) => {
    if (working) return;
    setWorking(true);
    try {
      const rsp = await BApi.resourceMove.retryResourceMoveRecord(record.id);

      if (rsp.code) {
        toast.danger(rsp.message ?? "Failed");
      } else {
        toast.success(t<string>("resourceMove.records.retryScheduled"));
        await load();
      }
    } finally {
      setWorking(false);
    }
  };

  const deleteRecord = async (record: MoveRecord) => {
    const rsp = await BApi.resourceMove.deleteResourceMoveRecord(record.id);

    if (!rsp.code) {
      await load();
    }
  };

  const clearInactive = async () => {
    const rsp = await BApi.resourceMove.deleteInactiveResourceMoveRecords();

    if (!rsp.code) {
      await load();
    }
  };

  if (records == undefined) {
    return (
      <div className="flex justify-center py-6">
        <Spinner size="sm" />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex flex-col gap-1 max-h-[600px] overflow-auto">
        {records.length === 0 ? (
          <div className="text-sm text-default-500 text-center py-6">
            {t<string>("resourceMove.records.empty")}
          </div>
        ) : (
          records.map((record) => (
            <div
              key={record.id}
              className="border border-default-200 rounded-lg p-2 text-xs flex flex-col gap-1"
            >
              <div className="flex items-center gap-2">
                <Chip color={StatusChipColors[record.status]} size="sm" variant="flat">
                  {t<string>(
                    `enum.resourceMoveRecordStatus.${ResourceMoveRecordStatus[record.status]
                      .charAt(0)
                      .toLowerCase()}${ResourceMoveRecordStatus[record.status].slice(1)}`,
                  )}
                </Chip>
                {record.error && (
                  <Tooltip
                    content={<pre className="text-xs whitespace-pre-wrap">{record.error}</pre>}
                  >
                    <QuestionCircleOutlined className="text-danger" />
                  </Tooltip>
                )}
                <span className="text-default-400">
                  {moment(record.createdAt).format("YYYY-MM-DD HH:mm")}
                </span>
                <div className="grow" />
                {retryableStatuses.has(record.status) && (
                  <Tooltip content={t<string>("resourceMove.records.retry")}>
                    <Button
                      isIconOnly
                      className="min-w-6 w-6 h-6"
                      isDisabled={working}
                      size="sm"
                      variant="light"
                      onPress={() => retry(record)}
                    >
                      <RedoOutlined className="text-sm" />
                    </Button>
                  </Tooltip>
                )}
                {!activeStatuses.has(record.status) && (
                  <Button
                    isIconOnly
                    className="min-w-6 w-6 h-6"
                    color="danger"
                    size="sm"
                    variant="light"
                    onPress={() => deleteRecord(record)}
                  >
                    <DeleteOutlined className="text-sm" />
                  </Button>
                )}
              </div>
              <div
                className="font-mono text-danger line-through truncate"
                title={record.sourcePath}
              >
                {record.sourcePath}
              </div>
              <div className="flex items-center gap-1 min-w-0">
                <AiOutlineArrowRight className="text-default-400 flex-shrink-0" />
                <div className="font-mono text-success truncate" title={record.destPath}>
                  {record.destPath}
                </div>
              </div>
            </div>
          ))
        )}
      </div>
      <div className="flex items-center gap-2">
        <Button size="sm" variant="ghost" onPress={load}>
          <ReloadOutlined className="text-base" />
          {t<string>("common.action.refresh")}
        </Button>
        {records.some((r) => !activeStatuses.has(r.status)) && (
          <Button size="sm" variant="ghost" onPress={clearInactive}>
            <ClearOutlined className="text-base" />
            {t<string>("resourceMove.records.clearInactive")}
          </Button>
        )}
      </div>
    </div>
  );
};

MoveRecordsTab.displayName = "MoveRecordsTab";

export default MoveRecordsTab;
