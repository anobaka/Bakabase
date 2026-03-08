"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Table,
  TableHeader,
  TableColumn,
  TableBody,
  TableRow,
  TableCell,
  Input,
  Chip,
  Button,
  Spinner,
  Progress,
} from "@heroui/react";
import {
  AiOutlineSearch,
  AiOutlineDelete,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineStop,
  AiOutlineReload,
} from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import { useDLsiteOptionsStore } from "@/stores/options";
import { DLsiteConfig } from "@/components/ThirdPartyConfig";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";

interface DLsiteWork {
  id: number;
  workId: string;
  title?: string;
  circle?: string;
  workType?: string;
  metadataJson?: string;
  metadataFetchedAt?: string;
  drmKey?: string;
  isPurchased: boolean;
  isDownloaded: boolean;
  localPath?: string;
  resourceId?: number;
  createdAt: string;
  updatedAt: string;
}

const SYNC_TASK_ID = "SyncDLsite";

export default function DLsiteWorksPage() {
  const { t } = useTranslation();
  const [works, setWorks] = useState<DLsiteWork[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [configOpen, setConfigOpen] = useState(false);
  const dlsiteOptions = useDLsiteOptionsStore((s) => s.data);
  const syncTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SYNC_TASK_ID));
  const isSyncing = syncTask?.status === BTaskStatus.Running;
  const prevSyncStatusRef = useRef(syncTask?.status);

  const isConfigured = (dlsiteOptions?.accounts?.length ?? 0) > 0;

  const loadWorks = async () => {
    setLoading(true);
    try {
      const rsp = await BApi.dlsiteWork.getAllDLsiteWorks();
      if (!rsp.code) {
        setWorks(rsp.data || []);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadWorks();
  }, []);

  useEffect(() => {
    if (prevSyncStatusRef.current === BTaskStatus.Running && syncTask?.status === BTaskStatus.Completed) {
      loadWorks();
    }
    prevSyncStatusRef.current = syncTask?.status;
  }, [syncTask?.status]);

  const handleSync = async () => {
    await BApi.dlsiteWork.syncDLsiteWorks();
  };

  const handleStopSync = async () => {
    await BApi.backgroundTask.stopBackgroundTask(SYNC_TASK_ID);
  };

  const filteredWorks = useMemo(() => {
    if (!keyword.trim()) return works;
    const kw = keyword.toLowerCase();
    return works.filter(
      (w) =>
        w.title?.toLowerCase().includes(kw) ||
        w.workId.toLowerCase().includes(kw) ||
        w.circle?.toLowerCase().includes(kw),
    );
  }, [works, keyword]);

  const handleDelete = async (workId: string) => {
    const rsp = await BApi.dlsiteWork.deleteDLsiteWork(workId);
    if (!rsp.code) {
      toast.success(t("common.state.saved"));
      setWorks((prev) => prev.filter((w) => w.workId !== workId));
    }
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">
            {t("resourceSource.dlsite.title")}
          </h1>
          <p className="text-default-500 mt-1">
            {t("resourceSource.dlsite.description")}
          </p>
        </div>
        <div className="flex gap-2 items-center">
          {isSyncing ? (
            <div className="flex items-center gap-2">
              <Progress
                className="w-32"
                color="primary"
                size="sm"
                value={syncTask?.percentage ?? 0}
              />
              <Button
                color="danger"
                size="sm"
                startContent={<AiOutlineStop />}
                variant="flat"
                onPress={handleStopSync}
              >
                {t("resourceSource.action.stopSync")}
              </Button>
            </div>
          ) : (
            <Button
              isDisabled={!isConfigured}
              size="sm"
              startContent={<AiOutlineSync />}
              variant="flat"
              onPress={handleSync}
            >
              {t("resourceSource.action.sync")}
            </Button>
          )}
          <Button
            size="sm"
            startContent={<AiOutlineReload />}
            variant="flat"
            onPress={loadWorks}
          >
            {t("resourceSource.action.refresh")}
          </Button>
          <Button
            size="sm"
            startContent={<AiOutlineSetting />}
            variant="flat"
            onPress={() => setConfigOpen(true)}
          >
            {t("resourceSource.action.configure")}
          </Button>
        </div>
      </div>

      <DLsiteConfig isOpen={configOpen} onClose={() => setConfigOpen(false)} />

      {!isConfigured && works.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 gap-4 text-default-500">
          <p className="text-lg font-medium">{t("resourceSource.notConfigured.title")}</p>
          <p>{t("resourceSource.notConfigured.description", { platform: "DLsite" })}</p>
          <Button
            color="primary"
            size="sm"
            onPress={() => setConfigOpen(true)}
          >
            {t("resourceSource.notConfigured.goToConfigure")}
          </Button>
        </div>
      )}

      {(isConfigured || works.length > 0) && (
        <>
          <div className="flex items-center gap-4">
            <Input
              className="max-w-sm"
              placeholder={t("resourceSource.filter.keyword")}
              size="sm"
              startContent={<AiOutlineSearch />}
              value={keyword}
              onValueChange={setKeyword}
            />
            <Chip size="sm" variant="flat">
              {filteredWorks.length} / {works.length}
            </Chip>
          </div>

          {loading ? (
            <div className="flex justify-center py-12">
              <Spinner size="lg" />
            </div>
          ) : (
            <Table removeWrapper aria-label="DLsite Works" isStriped>
              <TableHeader>
                <TableColumn>{t("resourceSource.dlsite.label.workId")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.title")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.circle")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.workType")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.purchased")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.downloaded")}</TableColumn>
                <TableColumn>{t("resourceSource.label.resourceId")}</TableColumn>
                <TableColumn width={80}>{""}</TableColumn>
              </TableHeader>
              <TableBody
                emptyContent={t("resourceSource.empty")}
                items={filteredWorks}
              >
                {(work) => (
                  <TableRow key={work.workId}>
                    <TableCell>
                      <Chip size="sm" variant="flat">
                        {work.workId}
                      </Chip>
                    </TableCell>
                    <TableCell>
                      <span className="font-medium">{work.title || "-"}</span>
                    </TableCell>
                    <TableCell>{work.circle || "-"}</TableCell>
                    <TableCell>
                      {work.workType ? (
                        <Chip color="secondary" size="sm" variant="flat">
                          {work.workType}
                        </Chip>
                      ) : (
                        "-"
                      )}
                    </TableCell>
                    <TableCell>
                      <Chip
                        color={work.isPurchased ? "success" : "default"}
                        size="sm"
                        variant="flat"
                      >
                        {work.isPurchased ? "Yes" : "No"}
                      </Chip>
                    </TableCell>
                    <TableCell>
                      <Chip
                        color={work.isDownloaded ? "success" : "default"}
                        size="sm"
                        variant="flat"
                      >
                        {work.isDownloaded ? "Yes" : "No"}
                      </Chip>
                    </TableCell>
                    <TableCell>
                      {work.resourceId ? (
                        <Chip color="primary" size="sm" variant="flat">
                          #{work.resourceId}
                        </Chip>
                      ) : (
                        "-"
                      )}
                    </TableCell>
                    <TableCell>
                      <Button
                        color="danger"
                        isIconOnly
                        size="sm"
                        variant="light"
                        onPress={() => handleDelete(work.workId)}
                      >
                        <AiOutlineDelete />
                      </Button>
                    </TableCell>
                  </TableRow>
                )}
              </TableBody>
            </Table>
          )}
        </>
      )}
    </div>
  );
}
