"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Input,
  Chip,
  Button,
  Spinner,
  CircularProgress,
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
  AiOutlineScan,
} from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import { useDLsiteOptionsStore } from "@/stores/options";
import { DLsiteConfig } from "@/components/ThirdPartyConfig";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";

import type { DLsiteWork } from "./types";
import { SYNC_TASK_ID, DOWNLOAD_TASK_ID_PREFIX, SCAN_TASK_ID } from "./types";
import { DLsiteTable } from "./components/DLsiteTable";

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
    window.open(`https://www.dlsite.com/maniax/work/=/product_id/${workId}.html`, "_blank");
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
