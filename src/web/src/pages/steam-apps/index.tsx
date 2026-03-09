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
} from "@heroui/react";
import {
  AiOutlineSearch,
  AiOutlineDelete,
  AiOutlineSetting,
  AiOutlineSync,
  AiOutlineStop,
  AiOutlineReload,
} from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { toast } from "@/components/bakaui";
import { useSteamOptionsStore } from "@/stores/options";
import { SteamConfig } from "@/components/ThirdPartyConfig";
import { useBTasksStore } from "@/stores/bTasks";
import { BTaskStatus } from "@/sdk/constants";

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

const SYNC_TASK_ID = "SyncSteam";

const getSteamHeaderImage = (appId: number) =>
  `https://cdn.akamai.steamstatic.com/steam/apps/${appId}/header.jpg`;

export default function SteamAppsPage() {
  const { t } = useTranslation();
  const [apps, setApps] = useState<SteamApp[]>([]);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [configOpen, setConfigOpen] = useState(false);
  const steamOptions = useSteamOptionsStore((s) => s.data);
  const patchOptions = useSteamOptionsStore((s) => s.patch);
  const showCover = steamOptions?.showCover ?? false;
  const syncTask = useBTasksStore((s) => s.tasks.find((t) => t.id === SYNC_TASK_ID));
  const isSyncing = syncTask?.status === BTaskStatus.Running;
  const prevSyncStatusRef = useRef(syncTask?.status);

  const isConfigured = (steamOptions?.accounts?.length ?? 0) > 0;

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

  useEffect(() => {
    if (prevSyncStatusRef.current === BTaskStatus.Running && syncTask?.status === BTaskStatus.Completed) {
      loadApps();
    }
    prevSyncStatusRef.current = syncTask?.status;
  }, [syncTask?.status]);

  const handleSync = async () => {
    await BApi.steamApp.syncSteamApps();
  };

  const handleStopSync = async () => {
    await BApi.backgroundTask.stopBackgroundTask(SYNC_TASK_ID);
  };

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
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold">
            {t("resourceSource.steam.title")}
          </h1>
          <p className="text-default-500 mt-1">
            {t("resourceSource.steam.description")}
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
            onPress={loadApps}
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

      <SteamConfig isOpen={configOpen} onClose={() => setConfigOpen(false)} />

      {!isConfigured && apps.length === 0 && (
        <div className="flex flex-col items-center justify-center py-16 gap-4 text-default-500">
          <p className="text-lg font-medium">{t("resourceSource.notConfigured.title")}</p>
          <p>{t("resourceSource.notConfigured.description", { platform: "Steam" })}</p>
          <Button
            color="primary"
            size="sm"
            onPress={() => setConfigOpen(true)}
          >
            {t("resourceSource.notConfigured.goToConfigure")}
          </Button>
        </div>
      )}

      {(isConfigured || apps.length > 0) && (
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
                {filteredApps.length} / {apps.length}
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
            <Table key={String(showCover)} removeWrapper aria-label="Steam Apps" isStriped>
              <TableHeader>
                {[
                  showCover && <TableColumn key="cover" width={240}>{""}</TableColumn>,
                  <TableColumn key="appId">{t("resourceSource.steam.label.appId")}</TableColumn>,
                  <TableColumn key="name">{t("resourceSource.steam.label.name")}</TableColumn>,
                  <TableColumn key="playtime">{t("resourceSource.steam.label.playtime")}</TableColumn>,
                  <TableColumn key="lastPlayed">{t("resourceSource.steam.label.lastPlayed")}</TableColumn>,
                  <TableColumn key="installed">{t("resourceSource.steam.label.installed")}</TableColumn>,
                  <TableColumn key="resourceId">{t("resourceSource.label.resourceId")}</TableColumn>,
                  <TableColumn key="actions" width={80}>{""}</TableColumn>,
                ].filter(Boolean)}
              </TableHeader>
              <TableBody
                emptyContent={t("resourceSource.empty")}
                items={filteredApps}
              >
                {(app) => (
                  <TableRow key={app.appId}>
                    {showCover && (
                      <TableCell>
                        <Image
                          alt={app.name || String(app.appId)}
                          className="object-contain"
                          classNames={{ wrapper: "w-[220px] min-w-[220px]" }}
                          radius="sm"
                          src={getSteamHeaderImage(app.appId)}
                        />
                      </TableCell>
                    )}
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
        </>
      )}
    </div>
  );
}
