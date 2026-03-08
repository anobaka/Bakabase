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
  CircularProgress,
  Progress,
  Image,
  Tooltip,
  Switch,
} from "@heroui/react";
import {
  AiOutlineSearch,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineStop,
  AiOutlineReload,
  AiOutlineFolderOpen,
  AiOutlineDownload,
  AiOutlinePlayCircle,
  AiOutlineScan,
  AiOutlineEye,
  AiOutlineEyeInvisible,
} from "react-icons/ai";
import { FiExternalLink } from "react-icons/fi";

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
  isHidden: boolean;
  localPath?: string;
  resourceId?: number;
  createdAt: string;
  updatedAt: string;
}

const SYNC_TASK_ID = "SyncDLsite";
const DOWNLOAD_TASK_ID_PREFIX = "DownloadDLsite_";
const SCAN_TASK_ID = "ScanDLsiteFolder";

const DLSITE_WORK_URL = "https://www.dlsite.com/maniax/work/=/product_id/";

export default function DLsiteWorksPage() {
  const { t } = useTranslation();
  const [works, setWorks] = useState<DLsiteWork[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [configOpen, setConfigOpen] = useState(false);
  const [showHidden, setShowHidden] = useState(true);
  const dlsiteOptions = useDLsiteOptionsStore((s) => s.data);
  const syncTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SYNC_TASK_ID));
  const scanTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SCAN_TASK_ID));
  const allTasks = useBTasksStore((s) => s.tasks);
  const isSyncing = syncTask?.status === BTaskStatus.Running;
  const isScanning = scanTask?.status === BTaskStatus.Running;
  const prevSyncStatusRef = useRef(syncTask?.status);
  const prevScanStatusRef = useRef(scanTask?.status);

  const isConfigured = (dlsiteOptions?.accounts?.length ?? 0) > 0;
  const downloadDir = dlsiteOptions?.defaultPath;
  const hasDownloadDir = !!downloadDir;
  const scanFolders = dlsiteOptions?.scanFolders || [];
  const hasScanFolders = scanFolders.length > 0;

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

  useEffect(() => {
    if (prevScanStatusRef.current === BTaskStatus.Running && scanTask?.status === BTaskStatus.Completed) {
      loadWorks();
    }
    prevScanStatusRef.current = scanTask?.status;
  }, [scanTask?.status]);

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

  const handleScanFolders = async () => {
    if (!hasScanFolders) {
      setConfigOpen(true);
      return;
    }
    const rsp = await BApi.dlsiteWork.scanDLsiteFolders();
    if (!rsp.code) {
      toast.success(t("resourceSource.dlsite.action.scanning"));
    }
  };

  const filteredWorks = useMemo(() => {
    let result = works;
    if (!showHidden) {
      result = result.filter((w) => !w.isHidden);
    }
    if (keyword.trim()) {
      const kw = keyword.toLowerCase();
      result = result.filter(
        (w) =>
          w.title?.toLowerCase().includes(kw) ||
          w.workId.toLowerCase().includes(kw) ||
          w.circle?.toLowerCase().includes(kw),
      );
    }
    return result;
  }, [works, keyword, showHidden]);

  const hiddenCount = useMemo(() => works.filter((w) => w.isHidden).length, [works]);

  const handleOpenDLsitePage = (workId: string) => {
    window.open(`${DLSITE_WORK_URL}${workId}.html`, "_blank");
  };

  const handleOpenLocal = async (localPath: string) => {
    await BApi.tool.openFileOrDirectory({ path: localPath, openInDirectory: false });
  };

  const handleOpenDownloadDir = async () => {
    if (downloadDir) {
      await BApi.tool.openFileOrDirectory({ path: downloadDir, openInDirectory: false });
    }
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

  const handleToggleHidden = async (workId: string, isHidden: boolean) => {
    const rsp = await BApi.dlsiteWork.setDLsiteWorkHidden(workId, isHidden);
    if (!rsp.code) {
      setWorks((prev) => prev.map((w) => w.workId === workId ? { ...w, isHidden } : w));
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
              <CircularProgress
                color="primary"
                showValueLabel
                size="sm"
                value={syncTask?.percentage ?? 0}
              />
              <Button
                color="danger"
                size="sm"
                startContent={<AiOutlineStop className="text-lg" />}
                variant="flat"
                onPress={handleStopSync}
              >
                {t("resourceSource.action.stopSync")}
              </Button>
            </div>
          ) : (
            <Tooltip content={!isConfigured ? t("resourceSource.notConfigured.title") : undefined}>
              <span>
                <Button
                  isDisabled={!isConfigured}
                  size="sm"
                  startContent={<AiOutlineSync className="text-lg" />}
                  variant="flat"
                  onPress={handleSync}
                >
                  {t("resourceSource.action.sync")}
                </Button>
              </span>
            </Tooltip>
          )}
          {isScanning ? (
            <div className="flex items-center gap-2">
              <CircularProgress
                color="secondary"
                showValueLabel
                size="sm"
                value={scanTask?.percentage ?? 0}
              />
              <span className="text-xs text-default-400">
                {t("resourceSource.dlsite.action.scanning")}
              </span>
            </div>
          ) : (
            <Button
              size="sm"
              startContent={<AiOutlineScan className="text-lg" />}
              variant="flat"
              onPress={handleScanFolders}
            >
              {t("resourceSource.dlsite.action.scanFolder")}
            </Button>
          )}
          {hasDownloadDir ? (
            <Button
              size="sm"
              startContent={<AiOutlineFolderOpen className="text-lg" />}
              variant="flat"
              onPress={handleOpenDownloadDir}
            >
              {t("resourceSource.dlsite.action.openDownloadDir")}
            </Button>
          ) : (
            <Button
              color="warning"
              size="sm"
              startContent={<AiOutlineFolderOpen className="text-lg" />}
              variant="flat"
              onPress={() => setConfigOpen(true)}
            >
              {t("resourceSource.dlsite.action.setDownloadDir")}
            </Button>
          )}
          <Button
            size="sm"
            startContent={<AiOutlineReload className="text-lg" />}
            variant="flat"
            onPress={loadWorks}
          >
            {t("resourceSource.action.refresh")}
          </Button>
          <Button
            size="sm"
            startContent={<AiOutlineSetting className="text-lg" />}
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
          <div className="flex items-center justify-between">
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
            <Switch
              isSelected={showHidden}
              size="sm"
              onValueChange={setShowHidden}
            >
              <span className="text-sm text-default-500 whitespace-nowrap">
                {t("resourceSource.dlsite.action.showHidden", { count: hiddenCount })}
              </span>
            </Switch>
          </div>

          {loading ? (
            <div className="flex justify-center py-12">
              <Spinner size="lg" />
            </div>
          ) : (
            <Table
              removeWrapper
              aria-label="DLsite Works"
              classNames={{
                // tr: "h-[200px]",
                // td: "align-middle",
              }}
              isStriped
            >
              <TableHeader>
                <TableColumn width={160}>{""}</TableColumn>
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
                            classNames={{ wrapper: "w-[140px] min-w-[140px]" }}
                            radius="sm"
                            src={work.coverUrl}
                          />
                        ) : (
                          <div className="w-[140px] flex items-center justify-center bg-default-100 rounded-sm text-default-300 text-lg font-bold">
                            {work.workId}
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
                          <div className="flex flex-col gap-1">
                            <Progress
                              className="w-full"
                              color="primary"
                              size="sm"
                              value={downloadTask?.percentage ?? 0}
                            />
                            <span className="text-xs text-default-400 truncate">
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
                                <FiExternalLink className="text-lg" />
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
                                    <AiOutlinePlayCircle className="text-lg" />
                                  </Button>
                                </Tooltip>
                                <Tooltip content={t("resourceSource.dlsite.action.openLocal")}>
                                  <Button
                                    isIconOnly
                                    size="sm"
                                    variant="light"
                                    onPress={() => handleOpenLocal(work.localPath!)}
                                  >
                                    <AiOutlineFolderOpen className="text-lg" />
                                  </Button>
                                </Tooltip>
                              </>
                            ) : (
                              <Tooltip content={hasDownloadDir ? t("resourceSource.dlsite.action.download") : t("resourceSource.dlsite.action.setDownloadDir")}>
                                <span>
                                  <Button
                                    color="warning"
                                    isDisabled={!hasDownloadDir}
                                    isIconOnly
                                    size="sm"
                                    variant="light"
                                    onPress={() => handleDownload(work.workId)}
                                  >
                                    <AiOutlineDownload className="text-lg" />
                                  </Button>
                                </span>
                              </Tooltip>
                            )}
                            <Tooltip content={work.isHidden ? t("resourceSource.dlsite.action.unhide") : t("resourceSource.dlsite.action.hide")}>
                              <Button
                                isIconOnly
                                size="sm"
                                variant="light"
                                onPress={() => handleToggleHidden(work.workId, !work.isHidden)}
                              >
                                {work.isHidden
                                  ? <AiOutlineEye className="text-lg" />
                                  : <AiOutlineEyeInvisible className="text-lg" />}
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
