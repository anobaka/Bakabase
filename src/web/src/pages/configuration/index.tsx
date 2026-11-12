"use client";

import type { ReactNode } from "react";
import type { BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo } from "@/sdk/Api";
import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import {
  AiOutlineSearch,
  AiOutlineAppstore,
  AiOutlineControl,
  AiOutlineDatabase,
  AiOutlineGlobal,
  AiOutlineInfoCircle,
  AiOutlineSafetyCertificate,
  AiOutlineTool,
} from "react-icons/ai";
import { useSearchParams } from "react-router-dom";

import DependencySummary from "./components/Dependency";
import StorageSettings from "./components/StorageSettings";
import AppInfo from "./components/AppInfo";
import ContactUs from "./components/ContactUs";
import Functional from "./components/Functional";
import Backup from "./components/VersionBackups";
import Others from "./components/Others";
import Development from "./components/Development";
import RemoteAccess from "./components/RemoteAccess";
import { normalizeQuery, SettingsSearchResults } from "./components/SettingsSection";
import DependenciesPage from "@/pages/dependencies";
import { Button, Input, Select } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import "./index.scss";

export const configurationGroups = [
  { id: "general", Icon: AiOutlineControl },
  { id: "storage", Icon: AiOutlineDatabase },
  { id: "network", Icon: AiOutlineGlobal },
  { id: "dependency", Icon: AiOutlineAppstore },
  { id: "remote-access", Icon: AiOutlineSafetyCertificate },
  { id: "advanced", Icon: AiOutlineTool },
  { id: "about", Icon: AiOutlineInfoCircle },
] as const;
type GroupId = (typeof configurationGroups)[number]["id"];
const isGroup = (value: string | null): value is GroupId =>
  configurationGroups.some((group) => group.id === value);
const lightGroups = configurationGroups.filter((group) => group.id !== "dependency");

function GroupResult({
  id,
  matched,
  selected,
  searching,
  title,
  onMatch,
  onOpen,
  children,
}: {
  id: GroupId;
  matched: boolean;
  selected: boolean;
  searching: boolean;
  title: string;
  onMatch: (id: GroupId, matched: boolean) => void;
  onOpen: (id: GroupId) => void;
  children: ReactNode;
}) {
  const { t } = useTranslation();
  useEffect(() => {
    onMatch(id, matched);
  }, [id, matched, onMatch]);
  return (
    <section
      hidden={searching ? !matched : !selected}
      aria-label={title}
      data-settings-group={id}
      className="min-w-0 space-y-3"
    >
      {searching && matched && (
        <div className="flex flex-wrap items-center justify-between gap-2 pt-2">
          <h2 className="text-sm font-semibold text-default-600">{title}</h2>
          <Button
            size="sm"
            variant="light"
            onPress={() => onOpen(id)}
            aria-label={t("configuration.workspace.openGroup", { group: title })}
          >
            {t("configuration.workspace.openGroup", { group: title })}
          </Button>
        </div>
      )}
      <div className="min-w-0 space-y-3">{children}</div>
    </section>
  );
}

export default function ConfigurationPage() {
  const { t } = useTranslation();
  const [params, setParams] = useSearchParams();
  const requested = params.get("tab");
  const active: GroupId = isGroup(requested)
    ? requested
    : !requested && params.has("component")
      ? "dependency"
      : "general";
  const [visited, setVisited] = useState<Set<GroupId>>(() => new Set([active]));
  const [appInfo, setAppInfo] = useState<
    Partial<BakabaseInfrastructuresComponentsAppModelsResponseModelsAppInfo>
  >({});
  const [keyword, setKeyword] = useState("");
  const query = useMemo(() => normalizeQuery(keyword), [keyword]);
  const searching = !!query;
  const [matches, setMatches] = useState<Partial<Record<GroupId, boolean>>>({});
  const reportMatch = useCallback(
    (id: GroupId, matched: boolean) =>
      setMatches((previous) =>
        previous[id] === matched ? previous : { ...previous, [id]: matched },
      ),
    [],
  );
  const clearSearch = useCallback(() => setKeyword(""), []);

  useEffect(() => {
    void BApi.app.getAppInfo().then((response) => setAppInfo(response.data ?? {}));
  }, []);
  useEffect(() => {
    setVisited((previous) => {
      const additions = searching ? lightGroups.map((group) => group.id) : [active];
      return additions.every((id) => previous.has(id))
        ? previous
        : new Set([...previous, ...additions]);
    });
  }, [active, searching]);
  // Navigation from search results and browser history restores the destination group.
  useEffect(() => {
    setKeyword("");
  }, [params.toString()]);
  useEffect(() => {
    if ((requested && !isGroup(requested)) || (!requested && params.has("component"))) {
      const next = new URLSearchParams(params);
      next.set("tab", active);
      if (active !== "dependency") {
        next.delete("component");
        next.delete("q");
      }
      setParams(next, { replace: true });
    }
  }, [requested, active, params, setParams]);

  const selectGroup = (id: GroupId) => {
    const next = new URLSearchParams(params);
    next.set("tab", id);
    if (id !== "dependency" || searching) {
      next.delete("component");
      next.delete("q");
    }
    setKeyword("");
    setParams(next);
  };
  const applyPatches = <T,>(
    api: (patches: T) => Promise<{ code?: number }>,
    patches: T,
    success?: (response: { code?: number }) => void,
  ) => {
    void api(patches).then((response) => {
      if (!response.code) {
        toast.success(t("common.success.saved"));
        success?.(response);
      }
    });
  };
  const contents: Record<GroupId, ReactNode> = {
    general: <Functional applyPatches={applyPatches} query={query} />,
    storage: (
      <>
        <StorageSettings appInfo={appInfo} query={query} />
        <Backup query={query} />
      </>
    ),
    network: <Others group="network" applyPatches={applyPatches} query={query} />,
    dependency: <DependencySummary query={query} onOpen={clearSearch} />,
    "remote-access": <RemoteAccess query={query} />,
    advanced: (
      <>
        <Others group="advanced" applyPatches={applyPatches} query={query} />
        <Development query={query} />
      </>
    ),
    about: (
      <>
        <AppInfo appInfo={appInfo} applyPatches={applyPatches} query={query} />
        <ContactUs query={query} />
      </>
    ),
  };
  const activeTitle = t(`configuration.workspace.group.${active}`);
  const anyMatched = configurationGroups.some((group) => matches[group.id]);
  return (
    <div className="configuration-page flex min-w-0 flex-col gap-4">
      <header className="flex min-w-0 flex-wrap items-start justify-between gap-3">
        <h1 className="pt-1 text-xl font-semibold">{t("configuration.workspace.title")}</h1>
        <Input
          isClearable
          className="w-full md:max-w-[360px]"
          aria-label={t("configuration.search.placeholder")}
          placeholder={t("configuration.search.placeholder")}
          description={t("configuration.workspace.searchScope")}
          size="sm"
          startContent={<AiOutlineSearch aria-hidden />}
          value={keyword}
          onValueChange={setKeyword}
        />
      </header>
      <div className="grid min-w-0 grid-cols-1 items-start gap-4 md:grid-cols-[160px_minmax(0,1fr)]">
        <nav
          aria-label={t("configuration.workspace.navigation")}
          className="sticky top-4 hidden min-w-0 flex-col gap-1 md:flex"
        >
          {configurationGroups.map(({ id, Icon }) => (
            <Button
              key={id}
              className="h-auto min-h-10 w-full justify-start whitespace-normal px-3 py-2 text-left"
              size="sm"
              variant={active === id ? "flat" : "light"}
              color={active === id ? "primary" : "default"}
              aria-current={active === id ? "page" : undefined}
              startContent={<Icon className="shrink-0" aria-hidden />}
              onPress={() => selectGroup(id)}
            >
              {t(`configuration.workspace.group.${id}`)}
            </Button>
          ))}
        </nav>
        <Select
          className="min-w-0 md:hidden"
          aria-label={t("configuration.workspace.navigation")}
          label={t("configuration.workspace.navigation")}
          selectedKeys={[active]}
          disallowEmptySelection
          dataSource={configurationGroups.map(({ id }) => ({
            value: id,
            label: t(`configuration.workspace.group.${id}`),
          }))}
          onSelectionChange={(keys) => {
            const id = String(Array.from(keys)[0]);
            if (isGroup(id)) selectGroup(id);
          }}
        />
        <main className="min-w-0 space-y-4">
          <div className="space-y-1">
            <h2 className="text-base font-semibold">
              {searching ? t("configuration.workspace.searchResults") : activeTitle}
            </h2>
            {!searching && active !== "dependency" && (
              <p className="text-xs leading-relaxed text-default-500">
                {t(`configuration.workspace.description.${active}`)}
              </p>
            )}
          </div>
          {configurationGroups.map(
            ({ id }) =>
              (id === "dependency" ? searching : visited.has(id) || active === id || searching) && (
                <SettingsSearchResults key={id}>
                  {(matched) => (
                    <GroupResult
                      id={id}
                      matched={matched}
                      selected={active === id}
                      searching={searching}
                      title={t(`configuration.workspace.group.${id}`)}
                      onMatch={reportMatch}
                      onOpen={selectGroup}
                    >
                      {contents[id]}
                    </GroupResult>
                  )}
                </SettingsSearchResults>
              ),
          )}
          {(visited.has("dependency") || active === "dependency") && (
            <section
              hidden={searching || active !== "dependency"}
              aria-label={t("configuration.workspace.group.dependency")}
              data-settings-group="dependency"
            >
              <DependenciesPage embedded active={!searching && active === "dependency"} />
            </section>
          )}
          {searching && !anyMatched && (
            <p role="status" className="py-6 text-sm text-default-500">
              {t("configuration.search.noResults", { keyword })}
            </p>
          )}
        </main>
      </div>
    </div>
  );
}
