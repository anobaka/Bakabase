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
  AiOutlineFolderOpen,
  AiOutlinePlayCircle,
} from "react-icons/ai";

import type { SteamApp } from "..";

const getSteamHeaderImage = (appId: number) =>
  `https://cdn.akamai.steamstatic.com/steam/apps/${appId}/header.jpg`;

interface SteamTableColumn {
  key: string;
  label: string;
  width?: number;
}

export default function SteamTable({
  apps, showCover, onLaunch, onOpenLocal, formatPlaytime, formatDate,
}: {
  apps: SteamApp[];
  showCover: boolean;
  onLaunch: (appId: number) => void;
  onOpenLocal: (installPath: string) => void;
  formatPlaytime: (minutes: number) => string;
  formatDate: (unixTs: number) => string;
}) {
  const { t } = useTranslation();

  const columns = useMemo<SteamTableColumn[]>(() => {
    const cols: SteamTableColumn[] = [];
    if (showCover) cols.push({ key: "cover", label: "", width: 240 });
    cols.push(
      { key: "appId", label: t("resourceSource.steam.label.appId") },
      { key: "name", label: t("resourceSource.steam.label.name") },
      { key: "playtime", label: t("resourceSource.steam.label.playtime") },
      { key: "lastPlayed", label: t("resourceSource.steam.label.lastPlayed") },
      { key: "installed", label: t("resourceSource.steam.label.installed") },
      { key: "account", label: t("resourceSource.steam.label.account") },
      { key: "actions", label: "", width: 120 },
    );
    return cols;
  }, [showCover, t]);

  const renderCell = (app: SteamApp, columnKey: string) => {
    switch (columnKey) {
      case "cover":
        return (
          <Image
            alt={app.name || String(app.appId)}
            className="object-contain"
            classNames={{ wrapper: "w-[220px] min-w-[220px]" }}
            radius="sm"
            src={getSteamHeaderImage(app.appId)}
          />
        );
      case "appId":
        return app.appId;
      case "name":
        return <span className="font-medium">{app.name || "-"}</span>;
      case "playtime":
        return formatPlaytime(app.playtimeForever);
      case "lastPlayed":
        return formatDate(app.rtimeLastPlayed);
      case "installed":
        return (
          <Chip
            color={app.isInstalled ? "success" : "default"}
            size="sm"
            variant="flat"
          >
            {app.isInstalled ? "Yes" : "No"}
          </Chip>
        );
      case "account":
        return app.account ? (
          <Chip size="sm" variant="flat">
            {app.account}
          </Chip>
        ) : "-";
      case "actions":
        return (
          <div className="flex gap-1">
            <Tooltip content={t("resourceSource.steam.action.launch")}>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={() => onLaunch(app.appId)}
              >
                <AiOutlinePlayCircle className="text-lg" />
              </Button>
            </Tooltip>
            {app.isInstalled && app.installPath && (
              <Tooltip content={t("resourceSource.steam.action.openLocal")}>
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => onOpenLocal(app.installPath!)}
                >
                  <AiOutlineFolderOpen className="text-lg" />
                </Button>
              </Tooltip>
            )}
          </div>
        );
      default:
        return null;
    }
  };

  return (
    <Table removeWrapper aria-label="Steam Apps" isStriped>
      <TableHeader columns={columns}>
        {(column) => (
          <TableColumn key={column.key} width={column.width}>
            {column.label}
          </TableColumn>
        )}
      </TableHeader>
      <TableBody
        emptyContent={t("resourceSource.empty")}
        items={apps}
      >
        {(app) => (
          <TableRow key={app.appId}>
            {(columnKey) => (
              <TableCell>{renderCell(app, columnKey as string)}</TableCell>
            )}
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}
