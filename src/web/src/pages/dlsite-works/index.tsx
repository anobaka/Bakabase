"use client";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Input,
  Chip,
  Button,
  Spinner,
  Progress,
  Card,
  CardBody,
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

const DLSITE_WORK_URL = "https://www.dlsite.com/maniax/work/=/product_id/";

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

  const handleOpenDLsitePage = (workId: string) => {
    window.open(`${DLSITE_WORK_URL}${workId}.html`, "_blank");
  };

  const handleOpenLocal = async (localPath: string) => {
    await BApi.tool.openFileOrDirectory({ path: localPath, openInDirectory: false });
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
          ) : filteredWorks.length === 0 ? (
            <div className="flex justify-center py-12 text-default-400">
              {t("resourceSource.empty")}
            </div>
          ) : (
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 xl:grid-cols-6 gap-4">
              {filteredWorks.map((work) => (
                <Card
                  key={work.workId}
                  className="group"
                  isPressable
                  onPress={() => handleOpenDLsitePage(work.workId)}
                >
                  <CardBody className="p-0 overflow-hidden">
                    <div className="relative bg-default-100">
                      {work.coverUrl ? (
                        <Image
                          alt={work.title || work.workId}
                          className="w-full h-full object-contain"
                          classNames={{ wrapper: "w-full h-full !max-w-full" }}
                          radius="none"
                          src={work.coverUrl}
                        />
                      ) : (
                        <div className="w-full h-full flex items-center justify-center text-default-300 text-4xl font-bold">
                          {work.workId.slice(0, 2)}
                        </div>
                      )}

                      {/* Overlay with actions - visible on hover */}
                      <div className="absolute inset-0 bg-black/60 opacity-0 group-hover:opacity-100 transition-opacity flex flex-col justify-end p-2 gap-1">
                        <div className="flex gap-1">
                          <Tooltip content={t("resourceSource.dlsite.action.openPage")}>
                            <Button
                              className="flex-1"
                              color="primary"
                              size="sm"
                              variant="flat"
                              onPress={(e) => {
                                e.stopPropagation?.();
                                handleOpenDLsitePage(work.workId);
                              }}
                            >
                              <AiOutlineLink />
                            </Button>
                          </Tooltip>
                          <Tooltip
                            content={
                              work.localPath
                                ? t("resourceSource.dlsite.action.openLocal")
                                : t("resourceSource.dlsite.noLocalPath")
                            }
                          >
                            <Button
                              className="flex-1"
                              color="success"
                              isDisabled={!work.localPath}
                              size="sm"
                              variant="flat"
                              onPress={(e) => {
                                e.stopPropagation?.();
                                if (work.localPath) {
                                  handleOpenLocal(work.localPath);
                                }
                              }}
                            >
                              <AiOutlineFolderOpen />
                            </Button>
                          </Tooltip>
                          <Tooltip content={t("resourceSource.action.delete")}>
                            <Button
                              color="danger"
                              isIconOnly
                              size="sm"
                              variant="flat"
                              onPress={(e) => {
                                e.stopPropagation?.();
                                handleDelete(work.workId);
                              }}
                            >
                              <AiOutlineDelete />
                            </Button>
                          </Tooltip>
                        </div>
                      </div>
                    </div>

                    <div className="p-2 space-y-1">
                      <p className="text-sm font-medium line-clamp-2 leading-tight" title={work.title}>
                        {work.title || work.workId}
                      </p>
                      <p className="text-xs text-default-400 truncate">
                        {work.circle || work.workId}
                      </p>
                      <div className="flex gap-1 flex-wrap">
                        {work.workType && (
                          <Chip className="text-[10px]" color="secondary" size="sm" variant="flat">
                            {work.workType}
                          </Chip>
                        )}
                        {work.isPurchased && (
                          <Chip className="text-[10px]" color="success" size="sm" variant="flat">
                            {t("resourceSource.dlsite.label.purchased")}
                          </Chip>
                        )}
                      </div>
                    </div>
                  </CardBody>
                </Card>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
