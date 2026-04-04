"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Input,
  Chip,
  Button,
  Spinner,
  CircularProgress,
  Tooltip,
  Switch,
  Pagination,
  Select,
  SelectItem,
  Modal,
  ModalContent,
  ModalHeader,
  ModalBody,
  ModalFooter,
  Checkbox,
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
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";

import type { DLsiteWork } from "./types";
import { SYNC_TASK_ID, DOWNLOAD_TASK_ID_PREFIX, EXTRACT_TASK_ID_PREFIX, SCAN_TASK_ID } from "./types";
import { DLsiteTable } from "./components/DLsiteTable";


const PAGE_SIZE_OPTIONS = [20, 50];

export default function DLsiteWorksPage() {
  const { t } = useTranslation();
  const [works, setWorks] = useState<DLsiteWork[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [searchKeyword, setSearchKeyword] = useState("");
  const { createPortal } = useBakabaseContext();
  const [showHidden, setShowHidden] = useState(false);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalCount, setTotalCount] = useState(0);
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

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  const loadWorks = useCallback(async (pageNum: number, ps: number, kw: string, sh: boolean) => {
    setLoading(true);
    try {
      const rsp = await BApi.dlsiteWork.getAllDLsiteWorks({
        keyword: kw || undefined,
        showHidden: sh,
        pageIndex: pageNum,
        pageSize: ps,
      });
      if (!rsp.code) {
        setWorks(rsp.data || []);
        setTotalCount(rsp.totalCount ?? 0);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadWorks(page, pageSize, searchKeyword, showHidden);
  }, [page, pageSize, searchKeyword, showHidden]);

  useEffect(() => {
    if (prevSyncStatusRef.current === BTaskStatus.Running && syncTask?.status === BTaskStatus.Completed) {
      loadWorks(page, pageSize, searchKeyword, showHidden);
    }
    prevSyncStatusRef.current = syncTask?.status;
  }, [syncTask?.status]);

  useEffect(() => {
    if (prevScanStatusRef.current === BTaskStatus.Running && scanTask?.status === BTaskStatus.Completed) {
      loadWorks(page, pageSize, searchKeyword, showHidden);
    }
    prevScanStatusRef.current = scanTask?.status;
  }, [scanTask?.status]);

  useEffect(() => {
    const downloadTasks = allTasks.filter((t) => t.id?.startsWith(DOWNLOAD_TASK_ID_PREFIX));
    const extractTasks = allTasks.filter((t) => t.id?.startsWith(EXTRACT_TASK_ID_PREFIX));
    const justCompleted = [...downloadTasks, ...extractTasks].some((t) => t.status === BTaskStatus.Completed);
    if (justCompleted) {
      loadWorks(page, pageSize, searchKeyword, showHidden);
    }
  }, [allTasks]);

  const [showSyncConfirm, setShowSyncConfirm] = useState(false);
  const [refetchMetadata, setRefetchMetadata] = useState(false);

  const handleSync = async () => {
    setShowSyncConfirm(true);
  };

  const handleSyncConfirmed = async () => {
    setShowSyncConfirm(false);
    await BApi.dlsiteWork.syncDLsiteWorks({ refetchMetadata });
    setRefetchMetadata(false);
  };

  const handleStopSync = async () => {
    await BApi.backgroundTask.stopBackgroundTask(SYNC_TASK_ID);
  };

  const handleScanFolders = async () => {
    if (!hasScanFolders) {
      createPortal(DLsiteConfig, {});
      return;
    }
    const rsp = await BApi.dlsiteWork.scanDLsiteFolders();
    if (!rsp.code) {
      toast.success(t("resourceSource.dlsite.action.scanning"));
    }
  };

  const handleSearch = () => {
    setPage(1);
    setSearchKeyword(keyword);
  };

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

  const handleReExtract = async (workId: string) => {
    const rsp = await BApi.dlsiteWork.extractDLsiteWork(workId);
    if (!rsp.code) {
      toast.success(t("resourceSource.dlsite.action.reExtract"));
    }
  };

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

  const handleToggleUseLocaleEmulator = async (workId: string, useLocaleEmulator: boolean) => {
    const rsp = await BApi.dlsiteWork.setDLsiteWorkUseLocaleEmulator(workId, useLocaleEmulator);
    if (!rsp.code) {
      setWorks((prev) => prev.map((w) => w.workId === workId ? { ...w, useLocaleEmulator } : w));
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
          <Button
            isDisabled={!hasDownloadDir}
            size="sm"
            startContent={<AiOutlineFolderOpen className="text-lg" />}
            variant="flat"
            onPress={handleOpenDownloadDir}
          >
            {t("resourceSource.dlsite.action.openDownloadDir")}
          </Button>
          <Button
            size="sm"
            startContent={<AiOutlineReload className="text-lg" />}
            variant="flat"
            onPress={() => loadWorks(page, pageSize, searchKeyword, showHidden)}
          >
            {t("resourceSource.action.refresh")}
          </Button>
          <Button
            size="sm"
            startContent={<AiOutlineSetting className="text-lg" />}
            variant="flat"
            onPress={() => createPortal(DLsiteConfig, {})}
          >
            {t("resourceSource.action.configure")}
          </Button>
        </div>
      </div>




      {!isConfigured && works.length === 0 && !loading && (
        <div className="flex flex-col items-center justify-center py-16 gap-4 text-default-500">
          <p className="text-lg font-medium">{t("resourceSource.notConfigured.title")}</p>
          <p>{t("resourceSource.notConfigured.description", { platform: "DLsite" })}</p>
          <Button
            color="primary"
            size="sm"
            onPress={() => createPortal(DLsiteConfig, {})}
          >
            {t("resourceSource.notConfigured.goToConfigure")}
          </Button>
        </div>
      )}

      {(isConfigured || works.length > 0 || loading) && (
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
                onKeyDown={(e) => e.key === "Enter" && handleSearch()}
              />
              <Chip size="sm" variant="flat">
                {t("resourceSource.pagination.total", { total: totalCount })}
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
                onValueChange={(v) => {
                  setShowHidden(v);
                  setPage(1);
                }}
              >
                <span className="text-sm text-default-500 whitespace-nowrap">
                  {t("resourceSource.dlsite.action.showHidden")}
                </span>
              </Switch>
              <div className="flex items-center gap-2">
                <span className="text-sm text-default-500 whitespace-nowrap">{t("resourceSource.pagination.pageSize")}</span>
                <Select
                  size="sm"
                  className="w-20"
                  selectedKeys={[String(pageSize)]}
                  onSelectionChange={(keys) => {
                    const val = Number(Array.from(keys)[0]);
                    if (val) {
                      setPageSize(val);
                      setPage(1);
                    }
                  }}
                >
                  {PAGE_SIZE_OPTIONS.map((s) => (
                    <SelectItem key={String(s)}>{String(s)}</SelectItem>
                  ))}
                </Select>
              </div>
            </div>
          </div>

          {loading ? (
            <div className="flex justify-center py-12">
              <Spinner size="lg" />
            </div>
          ) : (
            <DLsiteTable
              works={works}
              showCover={showCover}
              hasDownloadDir={hasDownloadDir}
              onOpenPage={handleOpenDLsitePage}
              onOpenLocal={handleOpenLocal}
              onLaunch={handleLaunch}
              onToggleHidden={handleToggleHidden}
              onDeleteLocal={handleDeleteLocal}
              onReExtract={handleReExtract}
              onWorkUpdate={handleWorkUpdate}
              onSetWorksLocalPath={handleSetWorksLocalPath}
              onToggleUseLocaleEmulator={handleToggleUseLocaleEmulator}
            />
          )}

          {totalPages > 1 && (
            <div className="flex justify-center">
              <Pagination
                page={page}
                total={totalPages}
                onChange={setPage}
              />
            </div>
          )}
        </>
      )}
      <Modal isOpen={showSyncConfirm} onClose={() => setShowSyncConfirm(false)}>
        <ModalContent>
          <ModalHeader>{t("resourceSource.confirm.sync.title")}</ModalHeader>
          <ModalBody>
            <p>{t("resourceSource.confirm.sync.description")}</p>
            <div>
              <Checkbox
                isSelected={refetchMetadata}
                onValueChange={setRefetchMetadata}
              >
                {t("resourceSource.confirm.sync.refetchMetadata")}
              </Checkbox>
              <p className="text-xs text-default-400 ml-7 mt-1">
                {t("resourceSource.confirm.sync.refetchMetadata.description")}
              </p>
            </div>
          </ModalBody>
          <ModalFooter>
            <Button variant="light" onPress={() => setShowSyncConfirm(false)}>
              {t("common.action.cancel")}
            </Button>
            <Button color="primary" onPress={handleSyncConfirmed}>
              {t("resourceSource.action.sync")}
            </Button>
          </ModalFooter>
        </ModalContent>
      </Modal>
    </div>
  );
}
