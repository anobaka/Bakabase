"use client";

import React, { useEffect } from "react";
import { Progress } from "@/components/bakaui";
import { usePrevious } from "react-use";
import { SyncOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import SynchronizationConfirmModal from "../SynchronizationConfirmModal";

import { BackgroundTaskStatus, BTaskStatus } from "@/sdk/constants";
import "./index.scss";
import { MdSync } from 'react-icons/md';
import { useBTasksStore } from "@/models/bTasks";
import { Button, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const testData = {
  status: BackgroundTaskStatus.Running,
  percentage: 90,
  message: "1232312312312",
  currentProcess: 123212312311,
};

type TaskInfo = {
  status: BackgroundTaskStatus;
  percentage: number;
  message?: string;
  currentProcess?: string;
};

type Props = {
  onComplete?: () => void;
};

export default ({ onComplete }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const backgroundTasks = useBTasksStore((state) => state.tasks);
  const sortedTasks = backgroundTasks
    .slice()
    .sort((a, b) => b.startedAt?.localeCompare(a.startedAt));
  const taskInfo = sortedTasks.find(
    (t) => t.name == "MediaLibraryService:Sync",
  );

  const prevTaskInfo = usePrevious(taskInfo);

  useEffect(() => {
    if (
      taskInfo?.status == BTaskStatus.Completed &&
      prevTaskInfo?.status != BTaskStatus.Completed
    ) {
      onComplete && onComplete();
    }
  }, [taskInfo]);

  useEffect(() => {}, []);

  const isSyncing = taskInfo?.status == BTaskStatus.Running;
  const failed = taskInfo?.status == BTaskStatus.Error;
  const isComplete = taskInfo?.status == BTaskStatus.Completed;

  return (
    <div className={"media-library-synchronization"}>
      <div className="main">
        <div className="top">
          {isSyncing ? (
            <div className="process">
              {t<string>(taskInfo?.process)}({taskInfo?.percentage}%)
            </div>
          ) : (
            <Button
              color={"secondary"}
              size={"small"}
              onClick={() => {
                createPortal(SynchronizationConfirmModal, {
                  onOk: async () =>
                    await BApi.mediaLibrary.startSyncMediaLibrary(),
                });
              }}
            >
              <SyncOutlined className={"text-base"} />
              {t<string>("Sync all media libraries")}
            </Button>
          )}
          {(failed || isComplete) && (
            <div className="status">
              {failed && (
                <Tooltip content={<pre>{taskInfo?.error}</pre>}>
                  <div className={"failed"}>
                    {t<string>("Failed")}
                    &nbsp;
                    <MdSync className={"text-base"} />
                  </div>
                </Tooltip>
              )}
              {isComplete && (
                <div className={"complete"}>
                  <MdSync className={"text-base"} />
                  &nbsp;
                  {t<string>("Complete")}
                </div>
              )}
            </div>
          )}
          <div className="opt" />
        </div>
        <div className="bottom">
          <div className="status">
            {isSyncing && (
              <div className={"syncing"}>
                <Progress
                  progressive
                  backgroundColor={"#d8d8d8"}
                  percent={taskInfo?.percentage}
                  size="small"
                  textRender={() => ""}
                />
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
