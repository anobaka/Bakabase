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
  Image,
  Tooltip,
} from "@heroui/react";
import {
  AiOutlineSearch,
  AiOutlineDelete,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineStop,
  AiOutlineReload,
  AiOutlineLink,
  AiOutlineFolderOpen,
  AiOutlineDownload,
  AiOutlinePlayCircle,
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
  coverUrl?: string;
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
const DOWNLOAD_TASK_ID_PREFIX = "DownloadDLsite_";

const DLSITE_WORK_URL = "https://www.dlsite.com/maniax/work/=/product_id/";

export default function DLsiteWorksPage() {
  const { t } = useTranslation();
  const [works, setWorks] = useState<DLsiteWork[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [configOpen, setConfigOpen] = useState(false);
  const dlsiteOptions = useDLsiteOptionsStore((s) => s.data);
  const syncTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SYNC_TASK_ID));
  const allTasks = useBTasksStore((s) => s.tasks);
  const isSyncing = syncTask?.status === BTaskStatus.Running;
  const prevSyncStatusRef = useRef(syncTask?.status);

  const isConfigured = (dlsiteOptions?.accounts?.length ?? 0) > 0;

  const getDownloadTask = (workId: string) => {
    return allTasks.find((t) => t.id === `${DOWNLOAD_TASK_ID_PREFIX}${workId}`);
  };

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

  // Reload works when any download task completes
  useEffect(() => {
    const downloadTasks = allTasks.filter((t) => t.id?.startsWith(DOWNLOAD_TASK_ID_PREFIX));
    const justCompleted = downloadTasks.some((t) => t.status === BTaskStatus.Completed);
    if (justCompleted) {
      loadWorks();
    }
  }, [allTasks]);

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

  const handleOpenDLsitePage = (workId: string) => {
    window.open(`${DLSITE_WORK_URL}${workId}.html`, "_blank");
  };

  const handleOpenLocal = async (localPath: string) => {
    await BApi.tool.openFileOrDirectory({ path: localPath, openInDirectory: false });
  };

  const handleDownload = async (workId: string) => {
    const rsp = await BApi.dlsiteWork.downloadDLsiteWork(workId);
    if (!rsp.code) {
      toast.success(t("resourceSource.dlsite.action.downloading"));
    }
  };

  const handleLaunch = async (workId: string) => {
    const rsp = await BApi.dlsiteWork.launchDLsiteWork(workId);
    if (rsp.code) {
      toast.error(rsp.message);
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
                <TableColumn width={60}>{""}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.workId")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.title")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.circle")}</TableColumn>
                <TableColumn>{t("resourceSource.dlsite.label.workType")}</TableColumn>
                <TableColumn>{t("resourceSource.label.resourceId")}</TableColumn>
                <TableColumn width={200}>{""}</TableColumn>
              </TableHeader>
              <TableBody
                emptyContent={t("resourceSource.empty")}
                items={filteredWorks}
              >
                {(work) => {
                  const downloadTask = getDownloadTask(work.workId);
                  const isDownloading = downloadTask?.status === BTaskStatus.Running;

                  return (
                    <TableRow key={work.workId}>
                      <TableCell>
                        {work.coverUrl ? (
                          <Image
                            alt={work.title || work.workId}
                            className="object-contain"
                            classNames={{ wrapper: "w-10 h-10 min-w-10" }}
                            radius="sm"
                            src={work.coverUrl}
                          />
                        ) : (
                          <div className="w-10 h-10 flex items-center justify-center bg-default-100 rounded-sm text-default-300 text-xs font-bold">
                            {work.workId.slice(0, 2)}
                          </div>
                        )}
                      </TableCell>
                      <TableCell>{work.workId}</TableCell>
                      <TableCell>
                        <span className="font-medium">{work.title || "-"}</span>
                      </TableCell>
                      <TableCell>{work.circle || "-"}</TableCell>
                      <TableCell>
                        {work.workType ? (
                          <Chip className="text-[10px]" color="secondary" size="sm" variant="flat">
                            {work.workType}
                          </Chip>
                        ) : "-"}
                      </TableCell>
                      <TableCell>
                        {work.resourceId ? (
                          <Chip color="primary" size="sm" variant="flat">
                            #{work.resourceId}
                          </Chip>
                        ) : "-"}
                      </TableCell>
                      <TableCell>
                        {isDownloading ? (
                          <div className="flex items-center gap-2">
                            <Progress
                              className="w-24"
                              color="primary"
                              size="sm"
                              value={downloadTask?.percentage ?? 0}
                            />
                            <span className="text-xs text-default-400 truncate max-w-[80px]">
                              {downloadTask?.process || t("resourceSource.dlsite.action.downloading")}
                            </span>
                          </div>
                        ) : (
                          <div className="flex gap-1">
                            <Tooltip content={t("resourceSource.dlsite.action.openPage")}>
                              <Button
                                isIconOnly
                                size="sm"
                                variant="light"
                                onPress={() => handleOpenDLsitePage(work.workId)}
                              >
                                <AiOutlineLink />
                              </Button>
                            </Tooltip>
                            {work.isDownloaded && work.localPath ? (
                              <>
                                <Tooltip content={t("resourceSource.dlsite.action.launch")}>
                                  <Button
                                    color="success"
                                    isIconOnly
                                    size="sm"
                                    variant="light"
                                    onPress={() => handleLaunch(work.workId)}
                                  >
                                    <AiOutlinePlayCircle />
                                  </Button>
                                </Tooltip>
                                <Tooltip content={t("resourceSource.dlsite.action.openLocal")}>
                                  <Button
                                    isIconOnly
                                    size="sm"
                                    variant="light"
                                    onPress={() => handleOpenLocal(work.localPath!)}
                                  >
                                    <AiOutlineFolderOpen />
                                  </Button>
                                </Tooltip>
                              </>
                            ) : (
                              <Tooltip content={t("resourceSource.dlsite.action.download")}>
                                <Button
                                  color="warning"
                                  isIconOnly
                                  size="sm"
                                  variant="light"
                                  onPress={() => handleDownload(work.workId)}
                                >
                                  <AiOutlineDownload />
                                </Button>
                              </Tooltip>
                            )}
                            <Tooltip content={t("resourceSource.action.delete")}>
                              <Button
                                color="danger"
                                isIconOnly
                                size="sm"
                                variant="light"
                                onPress={() => handleDelete(work.workId)}
                              >
                                <AiOutlineDelete />
                              </Button>
                            </Tooltip>
                          </div>
                        )}
                      </TableCell>
                    </TableRow>
                  );
                }}
              </TableBody>
            </Table>
          )}
        </>
      )}
    </div>
  );
}
