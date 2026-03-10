import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import {
  Table,
  TableHeader,
  TableColumn,
  TableBody,
  TableRow,
  TableCell,
  Chip,
  Button,
  Image,
  Tooltip,
} from "@heroui/react";
import {
  AiOutlineDelete,
  AiOutlineFolderOpen,
} from "react-icons/ai";

import type { ExHentaiGallery } from "..";

interface ExHentaiTableColumn {
  key: string;
  label: string;
  width?: number;
}

export default function ExHentaiTable({
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
            {gallery.localPath && (
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
            )}
            {gallery.isDownloaded && gallery.localPath && (
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
