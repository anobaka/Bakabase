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
  Switch,
  Tooltip,
} from "@heroui/react";
import {
  AiOutlineSearch,
  AiOutlineDelete,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineStop,
  AiOutlineReload,
  AiOutlineFolderOpen,
} from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import { useExHentaiOptionsStore } from "@/stores/options";
import { ExHentaiConfig } from "@/components/ThirdPartyConfig";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";

interface ExHentaiGallery {
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
  createdAt: string;
  updatedAt: string;
}

const SYNC_TASK_ID = "SyncExHentai";

interface ExHentaiTableColumn {
  key: string;
  label: string;
  width?: number;
}

function ExHentaiTable({
  galleries, showCover, categoryColorMap, onDelete, onOpenLocal, onDeleteLocal,
}: {
  galleries: ExHentaiGallery[];
  showCover: boolean;
  categoryColorMap: Record<string, "primary" | "secondary" | "success" | "warning" | "danger" | "default">;
  onDelete: (id: number) => void;
  onOpenLocal: (localPath: string) => void;
  onDeleteLocal: (galleryId: number, galleryToken: string) => void;
}) {
  const { t } = useTranslation();

  const columns = useMemo<ExHentaiTableColumn[]>(() => {
    const cols: ExHentaiTableColumn[] = [];
    if (showCover) cols.push({ key: "cover", label: "", width: 160 });
    cols.push(
      { key: "galleryId", label: t("resourceSource.exhentai.label.galleryId") },
      { key: "title", label: t("resourceSource.exhentai.label.title") },
      { key: "category", label: t("resourceSource.exhentai.label.category") },
      { key: "downloaded", label: t("resourceSource.exhentai.label.downloaded") },
      { key: "resourceId", label: t("resourceSource.label.resourceId") },
      { key: "createdAt", label: t("resourceSource.label.createdAt") },
      { key: "actions", label: "", width: 120 },
    );
    return cols;
  }, [showCover, t]);

  const renderCell = (gallery: ExHentaiGallery, columnKey: string) => {
    switch (columnKey) {
      case "cover":
        return gallery.coverUrl ? (
          <Image
            alt={gallery.title || String(gallery.galleryId)}
            className="object-contain"
            classNames={{ wrapper: "w-[140px] min-w-[140px]" }}
            radius="sm"
            src={gallery.coverUrl}
          />
        ) : (
          <div className="w-[140px] flex items-center justify-center bg-default-100 rounded-sm text-default-300 text-xs">
            {gallery.galleryId}
          </div>
        );
      case "galleryId":
        return (
          <span className="text-sm text-default-500">
            {gallery.galleryId}
          </span>
        );
      case "title":
        return (
          <div>
            <span className="font-medium">
              {gallery.title || gallery.titleJpn || "-"}
            </span>
            {gallery.titleJpn && gallery.title && (
              <div className="text-xs text-default-400 mt-0.5">
                {gallery.titleJpn}
              </div>
            )}
          </div>
        );
      case "category":
        return gallery.category ? (
          <Chip
            color={categoryColorMap[gallery.category] || "default"}
            size="sm"
            variant="flat"
          >
            {gallery.category}
          </Chip>
        ) : "-";
      case "downloaded":
        return (
          <Chip
            color={gallery.isDownloaded ? "success" : "default"}
            size="sm"
            variant="flat"
          >
            {gallery.isDownloaded ? "Yes" : "No"}
          </Chip>
        );
      case "resourceId":
        return gallery.resourceId ? (
          <Chip color="primary" size="sm" variant="flat">
            #{gallery.resourceId}
          </Chip>
        ) : "-";
      case "createdAt":
        return (
          <span className="text-sm">
            {new Date(gallery.createdAt).toLocaleDateString()}
          </span>
        );
      case "actions":
        return (
          <div className="flex gap-1">
            {gallery.isDownloaded && gallery.localPath && (
              <>
                <Tooltip content={t("resourceSource.exhentai.action.openLocal")}>
                  <Button
                    isIconOnly
                    size="sm"
                    variant="light"
                    onPress={() => onOpenLocal(gallery.localPath!)}
                  >
                    <AiOutlineFolderOpen className="text-lg" />
                  </Button>
                </Tooltip>
                <Tooltip content={t("resourceSource.exhentai.action.deleteLocal")}>
                  <Button
                    color="danger"
                    isIconOnly
                    size="sm"
                    variant="light"
                    onPress={() => onDeleteLocal(gallery.galleryId, gallery.galleryToken)}
                  >
                    <AiOutlineDelete className="text-lg" />
                  </Button>
                </Tooltip>
              </>
            )}
            <Button
              color="danger"
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => onDelete(gallery.id)}
            >
              <AiOutlineDelete />
            </Button>
          </div>
        );
      default:
        return null;
    }
  };

  return (
    <Table removeWrapper aria-label="ExHentai Galleries" isStriped>
      <TableHeader columns={columns}>
        {(column) => (
          <TableColumn key={column.key} width={column.width}>
            {column.label}
          </TableColumn>
        )}
      </TableHeader>
      <TableBody
        emptyContent={t("resourceSource.empty")}
        items={galleries}
      >
        {(gallery) => (
          <TableRow key={gallery.id}>
            {(columnKey) => (
              <TableCell>{renderCell(gallery, columnKey as string)}</TableCell>
            )}
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}

export default function ExHentaiGalleriesPage() {
  const { t } = useTranslation();
  const [galleries, setGalleries] = useState<ExHentaiGallery[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [configOpen, setConfigOpen] = useState(false);
  const exhentaiOptions = useExHentaiOptionsStore((s) => s.data);
  const patchOptions = useExHentaiOptionsStore((s) => s.patch);
  const showCover = exhentaiOptions?.showCover ?? false;
  const syncTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SYNC_TASK_ID));
  const isSyncing = syncTask?.status === BTaskStatus.Running;
  const prevSyncStatusRef = useRef(syncTask?.status);

  const isConfigured = (exhentaiOptions?.accounts?.length ?? 0) > 0;

  const loadGalleries = async () => {
    setLoading(true);
    try {
      const rsp = await BApi.exhentaiGallery.getAllExHentaiGalleries();
      if (!rsp.code) {
        setGalleries(rsp.data || []);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadGalleries();
  }, []);

  useEffect(() => {
    if (prevSyncStatusRef.current === BTaskStatus.Running && syncTask?.status === BTaskStatus.Completed) {
      loadGalleries();
    }
    prevSyncStatusRef.current = syncTask?.status;
  }, [syncTask?.status]);

  const handleSync = async () => {
    await BApi.exhentaiGallery.syncExHentaiGalleries();
  };

  const handleStopSync = async () => {
    await BApi.backgroundTask.stopBackgroundTask(SYNC_TASK_ID);
  };

  const filteredGalleries = useMemo(() => {
    if (!keyword.trim()) return galleries;
    const kw = keyword.toLowerCase();
    return galleries.filter(
      (g) =>
        g.title?.toLowerCase().includes(kw) ||
        g.titleJpn?.toLowerCase().includes(kw) ||
        String(g.galleryId).includes(kw) ||
        g.category?.toLowerCase().includes(kw),
    );
  }, [galleries, keyword]);

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
      setGalleries((prev) => prev.filter((g) => g.id !== id));
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
            onPress={loadGalleries}
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

      <ExHentaiConfig isOpen={configOpen} onClose={() => setConfigOpen(false)} />

      {!isConfigured && galleries.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 gap-4 text-default-500">
          <p className="text-lg font-medium">{t("resourceSource.notConfigured.title")}</p>
          <p>{t("resourceSource.notConfigured.description", { platform: "ExHentai" })}</p>
          <Button
            color="primary"
            size="sm"
            onPress={() => setConfigOpen(true)}
          >
            {t("resourceSource.notConfigured.goToConfigure")}
          </Button>
        </div>
      )}

      {(isConfigured || galleries.length > 0) && (
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
                {filteredGalleries.length} / {galleries.length}
              </Chip>
            </div>
            <Switch
              isSelected={showCover}
              size="sm"
              onValueChange={(v) => patchOptions({ showCover: v })}
            >
              <span className="text-sm text-default-500 whitespace-nowrap">
                {t("resourceSource.action.showCover")}
              </span>
            </Switch>
          </div>

          {loading ? (
            <div className="flex justify-center py-12">
              <Spinner size="lg" />
            </div>
          ) : (
            <ExHentaiTable
              galleries={filteredGalleries}
              showCover={showCover}
              categoryColorMap={categoryColorMap}
              onDelete={handleDelete}
              onOpenLocal={handleOpenLocal}
              onDeleteLocal={handleDeleteLocal}
            />
          )}
        </>
      )}
    </div>
  );
}
