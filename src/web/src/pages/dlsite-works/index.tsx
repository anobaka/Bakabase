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

interface DLsiteWork {
  id: number;
  workId: string;
  title?: string;
  circle?: string;
  workType?: string;
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

export default function DLsiteWorksPage() {
  const { t } = useTranslation();
  const [works, setWorks] = useState<DLsiteWork[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");

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

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">
          {t("resourceSource.dlsite.title")}
        </h1>
        <p className="text-default-500 mt-1">
          {t("resourceSource.dlsite.description")}
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
          {filteredWorks.length} / {works.length}
        </Chip>
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : (
        <Table aria-label="DLsite Works" isStriped>
          <TableHeader>
            <TableColumn>{t("resourceSource.dlsite.label.workId")}</TableColumn>
            <TableColumn>{t("resourceSource.dlsite.label.title")}</TableColumn>
            <TableColumn>{t("resourceSource.dlsite.label.circle")}</TableColumn>
            <TableColumn>{t("resourceSource.dlsite.label.workType")}</TableColumn>
            <TableColumn>{t("resourceSource.dlsite.label.purchased")}</TableColumn>
            <TableColumn>{t("resourceSource.dlsite.label.downloaded")}</TableColumn>
            <TableColumn>{t("resourceSource.label.resourceId")}</TableColumn>
            <TableColumn width={80}>{""}</TableColumn>
          </TableHeader>
          <TableBody
            emptyContent={t("resourceSource.empty")}
            items={filteredWorks}
          >
            {(work) => (
              <TableRow key={work.workId}>
                <TableCell>
                  <Chip size="sm" variant="flat">
                    {work.workId}
                  </Chip>
                </TableCell>
                <TableCell>
                  <span className="font-medium">{work.title || "-"}</span>
                </TableCell>
                <TableCell>{work.circle || "-"}</TableCell>
                <TableCell>
                  {work.workType ? (
                    <Chip color="secondary" size="sm" variant="flat">
                      {work.workType}
                    </Chip>
                  ) : (
                    "-"
                  )}
                </TableCell>
                <TableCell>
                  <Chip
                    color={work.isPurchased ? "success" : "default"}
                    size="sm"
                    variant="flat"
                  >
                    {work.isPurchased ? "Yes" : "No"}
                  </Chip>
                </TableCell>
                <TableCell>
                  <Chip
                    color={work.isDownloaded ? "success" : "default"}
                    size="sm"
                    variant="flat"
                  >
                    {work.isDownloaded ? "Yes" : "No"}
                  </Chip>
                </TableCell>
                <TableCell>
                  {work.resourceId ? (
                    <Chip color="primary" size="sm" variant="flat">
                      #{work.resourceId}
                    </Chip>
                  ) : (
                    "-"
                  )}
                </TableCell>
                <TableCell>
                  <Button
                    color="danger"
                    isIconOnly
                    size="sm"
                    variant="light"
                    onPress={() => handleDelete(work.workId)}
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
