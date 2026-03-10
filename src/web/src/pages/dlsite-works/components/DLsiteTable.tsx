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
  Dropdown,
  DropdownTrigger,
  DropdownMenu,
  DropdownItem,
} from "@heroui/react";
import {
  AiOutlineFolderOpen,
  AiOutlineEye,
  AiOutlineEyeInvisible,
  AiOutlineDelete,
  AiOutlineMore,
} from "react-icons/ai";
import { FiExternalLink } from "react-icons/fi";
import { GoPackage } from "react-icons/go";

import type { DLsiteWork } from "../types";
import { DrmKeyCell } from "./DrmKeyCell";
import { DownloadButton } from "./DownloadButton";
import { LeWorkControls } from "./LeWorkControls";

interface DLsiteTableColumn {
  key: string;
  label: string;
  width?: number;
}

export function DLsiteTable({
  works, showCover, hasDownloadDir,
  onOpenPage, onOpenLocal, onLaunch,
  onToggleHidden, onDeleteLocal, onReExtract, onWorkUpdate, onSetWorksLocalPath,
  onToggleUseLocaleEmulator,
}: {
  works: DLsiteWork[];
  showCover: boolean;
  hasDownloadDir: boolean;
  onOpenPage: (workId: string) => void;
  onOpenLocal: (localPath: string) => void;
  onLaunch: (workId: string) => void;
  onToggleHidden: (workId: string, isHidden: boolean) => void;
  onDeleteLocal: (workId: string) => void;
  onReExtract: (workId: string) => void;
  onWorkUpdate: (workId: string, patch: Partial<DLsiteWork>) => void;
  onSetWorksLocalPath: (workId: string, localPath: string) => void;
  onToggleUseLocaleEmulator: (workId: string, useLocaleEmulator: boolean) => void;
}) {
  const { t } = useTranslation();

  const columns = useMemo<DLsiteTableColumn[]>(() => {
    const cols: DLsiteTableColumn[] = [];
    if (showCover) cols.push({ key: "cover", label: "", width: 160 });
    cols.push(
      { key: "workId", label: t("resourceSource.dlsite.label.workId") },
      { key: "title", label: t("resourceSource.dlsite.label.title") },
      { key: "salesDate", label: t("resourceSource.dlsite.label.salesDate") },
      { key: "purchasedAt", label: t("resourceSource.dlsite.label.purchasedAt") },
      { key: "resourceId", label: t("resourceSource.label.resourceId") },
      { key: "account", label: t("resourceSource.dlsite.label.account") },
      { key: "drmKey", label: t("resourceSource.dlsite.label.drmKey") },
      { key: "actions", label: "", width: 260 },
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
        return (
          <div>
            <span className="font-medium">{work.title || "-"}</span>
            <div className="flex items-center gap-2 mt-1">
              {work.circle && (
                <span className="text-xs text-default-400">{work.circle}</span>
              )}
              {work.workType && (
                <Chip className="text-[10px]" color="secondary" size="sm" variant="flat">
                  {work.workType}
                </Chip>
              )}
              <Tooltip content={t("resourceSource.dlsite.action.openPage")}>
                <Button
                  isIconOnly
                  className="min-w-5 w-5 h-5"
                  size="sm"
                  variant="light"
                  onPress={() => onOpenPage(work.workId)}
                >
                  <FiExternalLink className="text-sm" />
                </Button>
              </Tooltip>
            </div>
          </div>
        );
      case "salesDate":
        return work.salesDate
          ? new Date(work.salesDate).toLocaleDateString()
          : "-";
      case "purchasedAt":
        return work.purchasedAt
          ? new Date(work.purchasedAt).toLocaleDateString()
          : "-";
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
      case "actions": {
        return (
          <div className="flex gap-1 items-center">
            {work.isDownloaded && work.localPath && (
              <LeWorkControls
                workId={work.workId}
                useLocaleEmulator={work.useLocaleEmulator}
                onLaunch={onLaunch}
                onToggleUseLocaleEmulator={onToggleUseLocaleEmulator}
              />
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
            {work.isDownloaded && work.localPath && (
              <Dropdown>
                <DropdownTrigger>
                  <Button
                    isIconOnly
                    size="sm"
                    variant="light"
                  >
                    <AiOutlineMore className="text-lg" />
                  </Button>
                </DropdownTrigger>
                <DropdownMenu aria-label={t("resourceSource.dlsite.action.more")}>
                  <DropdownItem
                    key="reExtract"
                    startContent={<GoPackage />}
                    onPress={() => onReExtract(work.workId)}
                  >
                    {t("resourceSource.dlsite.action.reExtract")}
                  </DropdownItem>
                  <DropdownItem
                    key="deleteLocal"
                    className="text-danger"
                    color="danger"
                    startContent={<AiOutlineDelete />}
                    onPress={() => onDeleteLocal(work.workId)}
                  >
                    {t("resourceSource.dlsite.action.deleteLocal")}
                  </DropdownItem>
                </DropdownMenu>
              </Dropdown>
            )}
          </div>
        );
      }
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
