"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
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
  AiOutlineCopy,
  AiOutlineKey,
  AiOutlineDelete,
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
  account?: string;
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

interface DLsiteTableColumn {
  key: string;
  label: string;
  width?: number;
}

/** Standalone DRM key cell - manages its own fetch/reveal state to bypass Table memoization */
function DrmKeyCell({ work, onWorkUpdate }: {
  work: DLsiteWork;
  onWorkUpdate: (workId: string, patch: Partial<DLsiteWork>) => void;
}) {
  const { t } = useTranslation();
  const [isFetching, setIsFetching] = useState(false);
  const [isRevealed, setIsRevealed] = useState(false);

  const handleFetch = async () => {
    setIsFetching(true);
    try {
      const rsp = await BApi.dlsiteWork.getDLsiteWorkDrmKey(work.workId);
      if (!rsp.code) {
        onWorkUpdate(work.workId, { drmKey: rsp.data ?? "" });
      }
    } finally {
      setIsFetching(false);
    }
  };

  const handleCopy = async (key: string) => {
    await navigator.clipboard.writeText(key);
    toast.success(t("resourceSource.dlsite.drmKey.copied"));
  };

  if (work.drmKey === undefined || work.drmKey === null) {
    return (
      <Button
        className="text-xs"
        isLoading={isFetching}
        size="sm"
        startContent={!isFetching ? <AiOutlineKey className="text-lg" /> : undefined}
        variant="light"
        onPress={handleFetch}
      >
        {t("resourceSource.dlsite.drmKey.fetch")}
      </Button>
    );
  }
  if (work.drmKey === "") {
    return (
      <span className="text-xs text-default-400">
        {t("resourceSource.dlsite.drmKey.none")}
      </span>
    );
  }
  return (
    <div className="flex items-center gap-1">
      <span className="text-xs font-mono">
        {isRevealed ? work.drmKey : "******"}
      </span>
      <Tooltip content={isRevealed ? t("resourceSource.dlsite.action.hide") : t("resourceSource.dlsite.action.unhide")}>
        <Button
          isIconOnly
          size="sm"
          variant="light"
          onPress={() => setIsRevealed((v) => !v)}
        >
          {isRevealed
            ? <AiOutlineEyeInvisible className="text-lg" />
            : <AiOutlineEye className="text-lg" />}
        </Button>
      </Tooltip>
      <Tooltip content={t("resourceSource.dlsite.drmKey.copy")}>
        <Button
          isIconOnly
          size="sm"
          variant="light"
          onPress={() => handleCopy(work.drmKey!)}
        >
          <AiOutlineCopy className="text-lg" />
        </Button>
      </Tooltip>
    </div>
  );
}

/** Standalone download button - subscribes to BTask store directly to bypass Table memoization */
function DownloadButton({ work, hasDownloadDir, onSetWorksLocalPath }: {
  work: DLsiteWork;
  hasDownloadDir: boolean;
  onSetWorksLocalPath: (workId: string, localPath: string) => void;
}) {
  const { t } = useTranslation();
  const downloadTask = useBTasksStore((s) => s.tasks.find((task) => task.id === `${DOWNLOAD_TASK_ID_PREFIX}${work.workId}`));
  const [isStarting, setIsStarting] = useState(false);

  // Clear optimistic state once BTask arrives
  useEffect(() => {
    if (isStarting && downloadTask) {
      setIsStarting(false);
    }
  }, [isStarting, downloadTask]);

  const isDownloading = isStarting || downloadTask?.status === BTaskStatus.Running || downloadTask?.status === BTaskStatus.NotStarted;

  const handleDownload = async () => {
    setIsStarting(true);
    try {
      const rsp = await BApi.dlsiteWork.downloadDLsiteWork(work.workId);
      if (rsp.code) {
        setIsStarting(false);
      } else if (rsp.data) {
        onSetWorksLocalPath(work.workId, rsp.data as string);
      }
    } catch {
      setIsStarting(false);
    }
  };

  const handleStop = async () => {
    const taskId = `${DOWNLOAD_TASK_ID_PREFIX}${work.workId}`;
    await BApi.backgroundTask.stopBackgroundTask(taskId);
  };

  if (isDownloading) {
    return (
      <Tooltip content={
        <div className="text-center">
          <div>{downloadTask?.process || t("resourceSource.dlsite.action.downloading")}</div>
          <div className="text-xs mt-1">{t("resourceSource.dlsite.action.stopDownload")}</div>
        </div>
      }>
        <div
          className="relative w-8 h-8 flex items-center justify-center cursor-pointer group"
          onClick={handleStop}
        >
          <CircularProgress
            className="group-hover:hidden"
            color="primary"
            showValueLabel
            size="sm"
            value={downloadTask?.percentage ?? 0}
          />
          <AiOutlineStop className="text-lg text-danger hidden group-hover:block" />
        </div>
      </Tooltip>
    );
  }

  if (work.isDownloaded) return null;

  return (
    <Tooltip content={hasDownloadDir ? t("resourceSource.dlsite.action.download") : t("resourceSource.dlsite.action.setDownloadDir")}>
      <span>
        <Button
          color="warning"
          isDisabled={!hasDownloadDir}
          isIconOnly
          size="sm"
          variant="light"
          onPress={handleDownload}
        >
          <AiOutlineDownload className="text-lg" />
        </Button>
      </span>
    </Tooltip>
  );
}

function DLsiteTable({
  works, showCover, hasDownloadDir,
  onOpenPage, onOpenLocal, onLaunch,
  onToggleHidden, onDeleteLocal, onWorkUpdate, onSetWorksLocalPath,
}: {
  works: DLsiteWork[];
  showCover: boolean;
  hasDownloadDir: boolean;
  onOpenPage: (workId: string) => void;
  onOpenLocal: (localPath: string) => void;
  onLaunch: (workId: string) => void;
  onToggleHidden: (workId: string, isHidden: boolean) => void;
  onDeleteLocal: (workId: string) => void;
  onWorkUpdate: (workId: string, patch: Partial<DLsiteWork>) => void;
  onSetWorksLocalPath: (workId: string, localPath: string) => void;
}) {
  const { t } = useTranslation();

  const columns = useMemo<DLsiteTableColumn[]>(() => {
    const cols: DLsiteTableColumn[] = [];
    if (showCover) cols.push({ key: "cover", label: "", width: 160 });
    cols.push(
      { key: "workId", label: t("resourceSource.dlsite.label.workId") },
      { key: "title", label: t("resourceSource.dlsite.label.title") },
      { key: "circle", label: t("resourceSource.dlsite.label.circle") },
      { key: "workType", label: t("resourceSource.dlsite.label.workType") },
      { key: "resourceId", label: t("resourceSource.label.resourceId") },
      { key: "account", label: t("resourceSource.dlsite.label.account") },
      { key: "drmKey", label: t("resourceSource.dlsite.label.drmKey") },
      { key: "actions", label: "", width: 200 },
    );
    return cols;
  }, [showCover, t]);

  const renderCell = (work: DLsiteWork, columnKey: string) => {
    switch (columnKey) {
      case "cover":
        return work.coverUrl ? (
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
        );
      case "workId":
        return work.workId;
      case "title":
        return <span className="font-medium">{work.title || "-"}</span>;
      case "circle":
        return work.circle || "-";
      case "workType":
        return work.workType ? (
          <Chip className="text-[10px]" color="secondary" size="sm" variant="flat">
            {work.workType}
          </Chip>
        ) : "-";
      case "resourceId":
        return work.resourceId ? (
          <Chip color="primary" size="sm" variant="flat">
            #{work.resourceId}
          </Chip>
        ) : "-";
      case "account":
        return work.account ? (
          <Chip size="sm" variant="flat">
            {work.account}
          </Chip>
        ) : "-";
      case "drmKey":
        return <DrmKeyCell work={work} onWorkUpdate={onWorkUpdate} />;
      case "actions":
        return (
          <div className="flex gap-1">
            <Tooltip content={t("resourceSource.dlsite.action.openPage")}>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={() => onOpenPage(work.workId)}
              >
                <FiExternalLink className="text-lg" />
              </Button>
            </Tooltip>
            {work.isDownloaded && work.localPath && (
              <Tooltip content={t("resourceSource.dlsite.action.launch")}>
                <Button
                  color="success"
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => onLaunch(work.workId)}
                >
                  <AiOutlinePlayCircle className="text-lg" />
                </Button>
              </Tooltip>
            )}
            {work.localPath && (
              <Tooltip content={t("resourceSource.dlsite.action.openLocal")}>
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => onOpenLocal(work.localPath!)}
                >
                  <AiOutlineFolderOpen className="text-lg" />
                </Button>
              </Tooltip>
            )}
            {work.isDownloaded && work.localPath && (
              <Tooltip content={t("resourceSource.dlsite.action.deleteLocal")}>
                <Button
                  color="danger"
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => onDeleteLocal(work.workId)}
                >
                  <AiOutlineDelete className="text-lg" />
                </Button>
              </Tooltip>
            )}
            <DownloadButton work={work} hasDownloadDir={hasDownloadDir} onSetWorksLocalPath={onSetWorksLocalPath} />
            <Tooltip content={work.isHidden ? t("resourceSource.dlsite.action.unhide") : t("resourceSource.dlsite.action.hide")}>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={() => onToggleHidden(work.workId, !work.isHidden)}
              >
                {work.isHidden
                  ? <AiOutlineEye className="text-lg" />
                  : <AiOutlineEyeInvisible className="text-lg" />}
              </Button>
            </Tooltip>
          </div>
        );
      default:
        return null;
    }
  };

  return (
    <Table
      removeWrapper
      aria-label="DLsite Works"
      isStriped
    >
      <TableHeader columns={columns}>
        {(column) => (
          <TableColumn key={column.key} width={column.width}>
            {column.label}
          </TableColumn>
        )}
      </TableHeader>
      <TableBody
        emptyContent={t("resourceSource.empty")}
        items={works}
      >
        {(work) => (
          <TableRow key={work.workId}>
            {(columnKey) => (
              <TableCell>{renderCell(work, columnKey as string)}</TableCell>
            )}
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}

export default function DLsiteWorksPage() {
  const { t } = useTranslation();
  const [works, setWorks] = useState<DLsiteWork[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [configOpen, setConfigOpen] = useState(false);
  const [showHidden, setShowHidden] = useState(true);
  const dlsiteOptions = useDLsiteOptionsStore((s) => s.data);
  const patchOptions = useDLsiteOptionsStore((s) => s.patch);
  const showCover = dlsiteOptions?.showCover ?? false;
  const dlsiteOptionsInitialized = useDLsiteOptionsStore((s) => s.initialized);
  const syncTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SYNC_TASK_ID));
  const scanTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SCAN_TASK_ID));
  const allTasks = useBTasksStore((s) => s.tasks);
  const isSyncing = syncTask?.status === BTaskStatus.Running;
  const isScanning = scanTask?.status === BTaskStatus.Running;
  const prevSyncStatusRef = useRef(syncTask?.status);
  const prevScanStatusRef = useRef(scanTask?.status);

  const isConfigured = !dlsiteOptionsInitialized || (dlsiteOptions?.accounts?.length ?? 0) > 0;
  const downloadDir = dlsiteOptions?.defaultPath;
  const hasDownloadDir = !dlsiteOptionsInitialized || !!downloadDir;
  const scanFolders = dlsiteOptions?.scanFolders || [];
  const hasScanFolders = scanFolders.length > 0;

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

  const handleLaunch = async (workId: string) => {
    const rsp = await BApi.dlsiteWork.launchDLsiteWork(workId);
    if (rsp.code) {
      toast.danger(rsp.message!);
    }
  };

  const handleWorkUpdate = useCallback((workId: string, patch: Partial<DLsiteWork>) => {
    setWorks((prev) => prev.map((w) => w.workId === workId ? { ...w, ...patch } : w));
  }, []);

  const handleSetWorksLocalPath = useCallback((workId: string, localPath: string) => {
    setWorks((prev) => prev.map((w) => w.workId === workId ? { ...w, localPath } : w));
  }, []);

  const handleDeleteLocal = async (workId: string) => {
    if (!confirm(t("resourceSource.dlsite.action.deleteLocalConfirm"))) return;
    const rsp = await BApi.dlsiteWork.deleteDLsiteWorkLocalFiles(workId);
    if (!rsp.code) {
      setWorks((prev) => prev.map((w) => w.workId === workId ? { ...w, isDownloaded: false, localPath: undefined } : w));
      toast.success(t("common.state.saved"));
    }
  };

  const handleToggleHidden = async (workId: string, isHidden: boolean) => {
    const rsp = await BApi.dlsiteWork.setDLsiteWorkHidden(workId, isHidden);
    if (!rsp.code) {
      setWorks((prev) => prev.map((w) => w.workId === workId ? { ...w, isHidden } : w));
    }
  };

  return (
    <div className="space-y-2">
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
            <div className="flex items-center gap-4">
              <Switch
                isSelected={showCover}
                size="sm"
                onValueChange={(v) => patchOptions({ showCover: v })}
              >
                <span className="text-sm text-default-500 whitespace-nowrap">
                  {t("resourceSource.action.showCover")}
                </span>
              </Switch>
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
          </div>

          {loading ? (
            <div className="flex justify-center py-12">
              <Spinner size="lg" />
            </div>
          ) : (
            <DLsiteTable
              works={filteredWorks}
              showCover={showCover}
              hasDownloadDir={hasDownloadDir}
              onOpenPage={handleOpenDLsitePage}
              onOpenLocal={handleOpenLocal}
              onLaunch={handleLaunch}
              onToggleHidden={handleToggleHidden}
              onDeleteLocal={handleDeleteLocal}
              onWorkUpdate={handleWorkUpdate}
              onSetWorksLocalPath={handleSetWorksLocalPath}
            />
          )}
        </>
      )}
    </div>
  );
}
