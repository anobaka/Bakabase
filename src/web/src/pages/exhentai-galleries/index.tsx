"use client";

import { useEffect, useMemo, useState } from "react";
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
} from "@heroui/react";
import { AiOutlineSearch, AiOutlineDelete } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";

interface ExHentaiGallery {
  id: number;
  galleryId: number;
  galleryToken: string;
  title?: string;
  titleJpn?: string;
  category?: string;
  metadataJson?: string;
  metadataFetchedAt?: string;
  isDownloaded: boolean;
  localPath?: string;
  resourceId?: number;
  createdAt: string;
  updatedAt: string;
}

export default function ExHentaiGalleriesPage() {
  const { t } = useTranslation();
  const [galleries, setGalleries] = useState<ExHentaiGallery[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");

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
      <div>
        <h1 className="text-2xl font-bold">
          {t("resourceSource.exhentai.title")}
        </h1>
        <p className="text-default-500 mt-1">
          {t("resourceSource.exhentai.description")}
        </p>
      </div>

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

      {loading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : (
        <Table aria-label="ExHentai Galleries" isStriped>
          <TableHeader>
            <TableColumn>{t("resourceSource.exhentai.label.galleryId")}</TableColumn>
            <TableColumn>{t("resourceSource.exhentai.label.title")}</TableColumn>
            <TableColumn>{t("resourceSource.exhentai.label.category")}</TableColumn>
            <TableColumn>{t("resourceSource.exhentai.label.downloaded")}</TableColumn>
            <TableColumn>{t("resourceSource.label.resourceId")}</TableColumn>
            <TableColumn>{t("resourceSource.label.createdAt")}</TableColumn>
            <TableColumn width={80}>{""}</TableColumn>
          </TableHeader>
          <TableBody
            emptyContent={t("resourceSource.empty")}
            items={filteredGalleries}
          >
            {(gallery) => (
              <TableRow key={gallery.id}>
                <TableCell>
                  <span className="text-sm text-default-500">
                    {gallery.galleryId}
                  </span>
                </TableCell>
                <TableCell>
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
                </TableCell>
                <TableCell>
                  {gallery.category ? (
                    <Chip
                      color={categoryColorMap[gallery.category] || "default"}
                      size="sm"
                      variant="flat"
                    >
                      {gallery.category}
                    </Chip>
                  ) : (
                    "-"
                  )}
                </TableCell>
                <TableCell>
                  <Chip
                    color={gallery.isDownloaded ? "success" : "default"}
                    size="sm"
                    variant="flat"
                  >
                    {gallery.isDownloaded ? "Yes" : "No"}
                  </Chip>
                </TableCell>
                <TableCell>
                  {gallery.resourceId ? (
                    <Chip color="primary" size="sm" variant="flat">
                      #{gallery.resourceId}
                    </Chip>
                  ) : (
                    "-"
                  )}
                </TableCell>
                <TableCell>
                  <span className="text-sm">
                    {new Date(gallery.createdAt).toLocaleDateString()}
                  </span>
                </TableCell>
                <TableCell>
                  <Button
                    color="danger"
                    isIconOnly
                    size="sm"
                    variant="light"
                    onPress={() => handleDelete(gallery.id)}
                  >
                    <AiOutlineDelete />
                  </Button>
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      )}
    </div>
  );
}
