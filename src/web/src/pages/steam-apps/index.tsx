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

interface SteamApp {
  id: number;
  appId: number;
  name?: string;
  playtimeForever: number;
  rtimeLastPlayed: number;
  imgIconUrl?: string;
  hasCommunitVisibleStats: boolean;
  metadataJson?: string;
  metadataFetchedAt?: string;
  isInstalled: boolean;
  installPath?: string;
  resourceId?: number;
  createdAt: string;
  updatedAt: string;
}

export default function SteamAppsPage() {
  const { t } = useTranslation();
  const [apps, setApps] = useState<SteamApp[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");

  const loadApps = async () => {
    setLoading(true);
    try {
      const rsp = await BApi.steamApp.getAllSteamApps();
      if (!rsp.code) {
        setApps(rsp.data || []);
      }
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadApps();
  }, []);

  const filteredApps = useMemo(() => {
    if (!keyword.trim()) return apps;
    const kw = keyword.toLowerCase();
    return apps.filter(
      (a) =>
        a.name?.toLowerCase().includes(kw) ||
        String(a.appId).includes(kw),
    );
  }, [apps, keyword]);

  const handleDelete = async (appId: number) => {
    const rsp = await BApi.steamApp.deleteSteamApp(appId);
    if (!rsp.code) {
      toast.success(t("common.state.saved"));
      setApps((prev) => prev.filter((a) => a.appId !== appId));
    }
  };

  const formatPlaytime = (minutes: number) => {
    const hours = Math.round(minutes / 60 * 10) / 10;
    return t("resourceSource.steam.playtimeHours", { hours });
  };

  const formatDate = (unixTs: number) => {
    if (!unixTs) return "-";
    return new Date(unixTs * 1000).toLocaleDateString();
  };

  return (
    <div className="space-y-4">
      <div>
        <h1 className="text-2xl font-bold">
          {t("resourceSource.steam.title")}
        </h1>
        <p className="text-default-500 mt-1">
          {t("resourceSource.steam.description")}
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
          {filteredApps.length} / {apps.length}
        </Chip>
      </div>

      {loading ? (
        <div className="flex justify-center py-12">
          <Spinner size="lg" />
        </div>
      ) : (
        <Table aria-label="Steam Apps" isStriped>
          <TableHeader>
            <TableColumn>{t("resourceSource.steam.label.appId")}</TableColumn>
            <TableColumn>{t("resourceSource.steam.label.name")}</TableColumn>
            <TableColumn>{t("resourceSource.steam.label.playtime")}</TableColumn>
            <TableColumn>{t("resourceSource.steam.label.lastPlayed")}</TableColumn>
            <TableColumn>{t("resourceSource.steam.label.installed")}</TableColumn>
            <TableColumn>{t("resourceSource.label.resourceId")}</TableColumn>
            <TableColumn width={80}>{""}</TableColumn>
          </TableHeader>
          <TableBody
            emptyContent={t("resourceSource.empty")}
            items={filteredApps}
          >
            {(app) => (
              <TableRow key={app.appId}>
                <TableCell>{app.appId}</TableCell>
                <TableCell>
                  <span className="font-medium">{app.name || "-"}</span>
                </TableCell>
                <TableCell>{formatPlaytime(app.playtimeForever)}</TableCell>
                <TableCell>{formatDate(app.rtimeLastPlayed)}</TableCell>
                <TableCell>
                  <Chip
                    color={app.isInstalled ? "success" : "default"}
                    size="sm"
                    variant="flat"
                  >
                    {app.isInstalled ? "Yes" : "No"}
                  </Chip>
                </TableCell>
                <TableCell>
                  {app.resourceId ? (
                    <Chip color="primary" size="sm" variant="flat">
                      #{app.resourceId}
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
                    onPress={() => handleDelete(app.appId)}
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
