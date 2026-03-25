"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Input,
  Chip,
  Button,
  Spinner,
  Progress,
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
} from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import { useExHentaiOptionsStore } from "@/stores/options";
import { ExHentaiConfig } from "@/components/ThirdPartyConfig";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";
import ExHentaiTable from "./components/ExHentaiTable";

export interface ExHentaiGallery {
  id: number;
  galleryId: number;
  galleryToken: string;
  title?: string;
  titleJpn?: string;
  category?: string;
  coverUrl?: string;
  metadataJson?: string;
  metadataFetchedAt?: string;
  isDownloaded: boolean;
  localPath?: string;
  resourceId?: number;
  account?: string;
  createdAt: string;
  updatedAt: string;
}

const SYNC_TASK_ID = "SyncExHentai";
const PAGE_SIZE_OPTIONS = [20, 50];

export default function ExHentaiGalleriesPage() {
  const { t } = useTranslation();
  const [galleries, setGalleries] = useState<ExHentaiGallery[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [searchKeyword, setSearchKeyword] = useState("");
  const { createPortal } = useBakabaseContext();
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [totalCount, setTotalCount] = useState(0);
  const exhentaiOptions = useExHentaiOptionsStore((s) => s.data);
  const patchOptions = useExHentaiOptionsStore((s) => s.patch);
  const showCover = exhentaiOptions?.showCover ?? false;
  const syncTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SYNC_TASK_ID));
  const isSyncing = syncTask?.status === BTaskStatus.Running;
  const prevSyncStatusRef = useRef(syncTask?.status);

  const isConfigured = (exhentaiOptions?.accounts?.length ?? 0) > 0;

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  const loadGalleries = useCallback(async (pageNum: number, ps: number, kw: string) => {
    setLoading(true);
    try {
      const rsp = await BApi.exhentaiGallery.getAllExHentaiGalleries({
        keyword: kw || undefined,
        pageIndex: pageNum,
        pageSize: ps,
      });
      if (!rsp.code) {
        setGalleries(rsp.data || []);
        setTotalCount(rsp.totalCount ?? 0);
      }
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadGalleries(page, pageSize, searchKeyword);
  }, [page, pageSize, searchKeyword]);

  useEffect(() => {
    if (prevSyncStatusRef.current === BTaskStatus.Running && syncTask?.status === BTaskStatus.Completed) {
      loadGalleries(page, pageSize, searchKeyword);
    }
    prevSyncStatusRef.current = syncTask?.status;
  }, [syncTask?.status]);

  const [showSyncConfirm, setShowSyncConfirm] = useState(false);
  const [refetchMetadata, setRefetchMetadata] = useState(false);

  const handleSync = async () => {
    setShowSyncConfirm(true);
  };

  const handleSyncConfirmed = async () => {
    setShowSyncConfirm(false);
    await BApi.exhentaiGallery.syncExHentaiGalleries({ refetchMetadata });
    setRefetchMetadata(false);
  };

  const handleStopSync = async () => {
    await BApi.backgroundTask.stopBackgroundTask(SYNC_TASK_ID);
  };

  const handleSearch = () => {
    setPage(1);
    setSearchKeyword(keyword);
  };

  const handleOpenLocal = async (localPath: string) => {
    await BApi.tool.openFileOrDirectory({ path: localPath, openInDirectory: false });
  };

  const handleDeleteLocal = async (galleryId: number, galleryToken: string) => {
    if (!confirm(t("resourceSource.exhentai.action.deleteLocalConfirm"))) return;
    const rsp = await BApi.exhentaiGallery.deleteExHentaiGalleryLocalFiles(galleryId, galleryToken);
    if (!rsp.code) {
      setGalleries((prev) => prev.map((g) =>
        g.galleryId === galleryId && g.galleryToken === galleryToken
          ? { ...g, isDownloaded: false, localPath: undefined }
          : g,
      ));
      toast.success(t("common.state.saved"));
    }
  };

  const handleDelete = async (id: number) => {
    const rsp = await BApi.exhentaiGallery.deleteExHentaiGallery(id);
    if (!rsp.code) {
      toast.success(t("common.state.saved"));
      loadGalleries(page, pageSize, searchKeyword);
    }
  };

  const categoryColorMap: Record<string, "primary" | "secondary" | "success" | "warning" | "danger" | "default"> = {
    "Doujinshi": "primary",
    "Manga": "secondary",
    "Artist CG": "success",
    "Game CG": "warning",
    "Non-H": "default",
    "Cosplay": "danger",
  };

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">
            {t("resourceSource.exhentai.title")}
          </h1>
          <p className="text-default-500 mt-1">
            {t("resourceSource.exhentai.description")}
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
            onPress={() => loadGalleries(page, pageSize, searchKeyword)}
          >
            {t("resourceSource.action.refresh")}
          </Button>
          <Button
            size="sm"
            startContent={<AiOutlineSetting />}
            variant="flat"
            onPress={() => createPortal(ExHentaiConfig, {})}
          >
            {t("resourceSource.action.configure")}
          </Button>
        </div>
      </div>




      {!isConfigured && galleries.length === 0 && !loading && (
        <div className="flex flex-col items-center justify-center py-16 gap-4 text-default-500">
          <p className="text-lg font-medium">{t("resourceSource.notConfigured.title")}</p>
          <p>{t("resourceSource.notConfigured.description", { platform: "ExHentai" })}</p>
          <Button
            color="primary"
            size="sm"
            onPress={() => createPortal(ExHentaiConfig, {})}
          >
            {t("resourceSource.notConfigured.goToConfigure")}
          </Button>
        </div>
      )}

      {(isConfigured || galleries.length > 0 || loading) && (
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
            <ExHentaiTable
              galleries={galleries}
              showCover={showCover}
              categoryColorMap={categoryColorMap}
              onDelete={handleDelete}
              onOpenLocal={handleOpenLocal}
              onDeleteLocal={handleDeleteLocal}
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
            <Checkbox
              isSelected={refetchMetadata}
              onValueChange={setRefetchMetadata}
            >
              {t("resourceSource.confirm.sync.refetchMetadata")}
            </Checkbox>
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
