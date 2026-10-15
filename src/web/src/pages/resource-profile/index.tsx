"use client";

import type { ReactNode } from "react";
import type { BakabaseServiceModelsInputResourceProfileInputModel } from "@/sdk/Api";
import type { ResourceProfile, ProfileSource } from "./sourceProfiles";
import type { IProperty } from "@/components/Property/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { ResourceScope, RuleField } from "./scopeApi";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AppstoreOutlined,
  CheckOutlined,
  ClearOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  EyeOutlined,
  FileTextOutlined,
  FilterOutlined,
  InfoCircleOutlined,
  MoreOutlined,
  PlayCircleOutlined,
  PlusOutlined,
  ReloadOutlined,
  SearchOutlined,
  SettingOutlined,
  ThunderboltOutlined,
} from "@ant-design/icons";

import { profileSourceApi } from "./sourceProfiles";
import { resourceScopeApi, ruleFields } from "./scopeApi";
import { processingApi } from "./processingApi";
import ResourceProcessingTasks from "./components/ResourceProcessingTasks";
import RuleExecutionTargetSelect, {
  defaultExecutionTarget,
} from "./components/RuleExecutionTargetSelect";
import ResourceScopeEditorModal from "./components/ResourceScopeEditorModal";
import { createSourceFilterConfig } from "./sourceFilterConfig";
import ResourceProfileModal from "./components/ResourceProfileModal";
import ResourceProfileTestModal from "./components/ResourceProfileTestModal";
import DisplayNameTemplateEditorModal from "./components/DisplayNameTemplateEditorModal";
import EnhancementConfigPanel from "./components/EnhancementConfigPanel";
import PlayableFileSelectorModal from "./components/PlayableFileSelectorModal";
import PlayerSelectorModal from "./components/PlayerSelectorModal";
import PropertyPoolModal from "./components/PropertyPoolModal";
import DeleteEnhancementsModal from "./components/DeleteEnhancementsModal";
import { checkProfileResponse, hasProfileConditions, toProfileInputModel } from "./profileUtils";

import {
  Button,
  Card,
  CardBody,
  Chip,
  Input,
  Listbox,
  ListboxItem,
  Popover,
  Spinner,
  Select,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { HelpCenterButton } from "@/components/HelpCenter";
import { PropertyLabel } from "@/components/Property";
import { ResourceFilterController } from "@/components/ResourceFilter";
import { FilterDisplayMode, PropertyPool, ResourceTagLabel } from "@/sdk/constants";
import { notifyResourceProfilesChanged } from "@/features/playback/profileInvalidation";
import BApi from "@/sdk/BApi";
import ConfirmModal from "@/components/ConfirmModal";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";

function ConfigSection({
  title,
  description,
  icon,
  action,
  children,
}: {
  title: string;
  description: string;
  icon: ReactNode;
  action: ReactNode;
  children?: ReactNode;
}) {
  return (
    <section className="min-w-0 py-5 first:pt-0 last:pb-0">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 flex-1 gap-3">
          <span className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-lg bg-default-100 text-default-500">
            {icon}
          </span>
          <div className="min-w-0">
            <h3 className="text-sm font-semibold">{title}</h3>
            <p className="mt-1 text-xs leading-relaxed text-default-500">{description}</p>
          </div>
        </div>
        {action}
      </div>
      {children && <div className="mt-3 lg:ml-11">{children}</div>}
    </section>
  );
}

export default function ResourceProfilePage() {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [sources, setSources] = useState<ProfileSource[]>([]);
  const [sourceError, setSourceError] = useState(false);
  const [sourceRevision, setSourceRevision] = useState(0);
  const loadSources = () => {
    setSourceError(false);
    void profileSourceApi
      .list()
      .then(setSources)
      .catch(() => setSourceError(true));
  };
  const defaultSourceId =
    new URLSearchParams(window.location.hash.split("?")[1] ?? "").get("sourceDeviceId") ??
    undefined;
  const [profiles, setProfiles] = useState<ResourceProfile[]>([]);
  const [scopes, setScopes] = useState<ResourceScope[]>([]);
  const [showProcessingTasks, setShowProcessingTasks] = useState(false);
  const [ruleField, setRuleField] = useState<RuleField | "all">("all");
  const showRule = (field: RuleField) => ruleField === "all" || ruleField === field;
  const profilesRef = useRef(profiles);
  const [selectedId, setSelectedId] = useState<number>();
  const [menuOpen, setMenuOpen] = useState(false);
  const [keyword, setKeyword] = useState("");
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const busyRef = useRef(false);
  const [error, setError] = useState("");
  const [properties, setProperties] = useState<IProperty[]>([]);
  const [enhancerDescriptors, setEnhancerDescriptors] = useState<EnhancerDescriptor[]>([]);
  const [metadataReady, setMetadataReady] = useState(false);
  const [draft, setDraft] = useState<{
    id: number;
    search: NonNullable<ResourceProfile["search"]>;
  }>();

  const acceptProfiles = (data: ResourceProfile[]) => {
    const sorted = [...data].sort((a, b) => b.priority - a.priority);

    profilesRef.current = sorted;
    setProfiles(sorted);
    setSelectedId((id) =>
      sorted.some((p) => p.id === id)
        ? id
        : (sorted.find((p) => defaultSourceId && p.sourceDeviceId === defaultSourceId) ?? sorted[0])
            ?.id,
    );
  };
  const loadProfiles = async () => {
    setLoading(true);
    setError("");
    try {
      const response = await BApi.resourceProfile.getAllResourceProfiles();

      checkProfileResponse(response, t("resourceProfile.error.load"));
      acceptProfiles(response.data ?? []);
    } catch (e) {
      setError(e instanceof Error ? e.message : t("resourceProfile.error.load"));
    } finally {
      setLoading(false);
    }
  };
  const loadScopes = async () => {
    try {
      const result = await resourceScopeApi.list();

      if (!Array.isArray(result)) throw new Error(t("resourceProfile.error.load"));
      setScopes(result);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t("resourceProfile.error.load"));
    }
  };
  const loadMetadata = async () => {
    try {
      const [propertyResponse, enhancerResponse] = await Promise.all([
        BApi.property.getPropertiesByPool(PropertyPool.All),
        BApi.enhancer.getAllEnhancerDescriptors(),
      ]);

      checkProfileResponse(propertyResponse, t("resourceProfile.error.load"));
      checkProfileResponse(enhancerResponse, t("resourceProfile.error.load"));
      setProperties((propertyResponse.data ?? []) as IProperty[]);
      setEnhancerDescriptors((enhancerResponse.data ?? []) as EnhancerDescriptor[]);
      setMetadataReady(true);
    } catch (e) {
      setError(e instanceof Error ? e.message : t("resourceProfile.error.load"));
    }
  };

  useEffect(() => {
    void loadProfiles();
    void loadMetadata();
    void loadScopes();
    loadSources();
  }, []);

  const updateProfile = async (id: number, updates: Partial<ResourceProfile>) => {
    if (busyRef.current) throw new Error(t("resourceProfile.error.busy"));
    const current = profilesRef.current.find((profile) => profile.id === id);

    if (!current) throw new Error(t("resourceProfile.error.missing"));
    busyRef.current = true;
    setBusy(true);
    try {
      const fields = Object.keys(updates);

      if (fields.length === 1 && fields[0] in ruleFields) {
        const field = fields[0] as RuleField;
        const saved = await resourceScopeApi.rule(current, field, updates[field]);

        notifyResourceProfilesChanged();
        acceptProfiles(profilesRef.current.map((profile) => (profile.id === id ? saved : profile)));

        return;
      }
      if (fields.length === 1 && fields[0] === "search" && current.resourceScopeId) {
        const scope = scopes.find((item) => item.id === current.resourceScopeId);

        if (!scope) throw new Error(t("resourceProfile.scope.loadFailed"));
        await resourceScopeApi.update({ ...scope, search: updates.search });
        notifyResourceProfilesChanged();
        await Promise.all([loadProfiles(), loadScopes()]);

        return;
      }
      const merged = { ...current, ...updates };
      const response = await BApi.resourceProfile.updateResourceProfile(
        id,
        toProfileInputModel(merged) as BakabaseServiceModelsInputResourceProfileInputModel,
      );

      checkProfileResponse(response, t("resourceProfile.error.save"));
      notifyResourceProfilesChanged();
      // Apply only after the server accepts the complete update. A later modal
      // always reads this ref, including scope priorities added by other edits.
      const saved = (response as { data?: ResourceProfile }).data ?? merged;

      acceptProfiles(profilesRef.current.map((profile) => (profile.id === id ? saved : profile)));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };
  const onCreated = (profile: ResourceProfile) => {
    notifyResourceProfilesChanged();
    acceptProfiles([...profilesRef.current, profile]);
    setSelectedId(profile.id);
    setKeyword("");
    if (!profile.sourceDeviceId)
      setDraft({ id: profile.id, search: profile.search ?? { page: 1, pageSize: 100 } });
  };
  const create = () =>
    createPortal(ResourceProfileModal, {
      existingNames: profiles.map((p) => p.name),
      sources,
      defaultSourceDeviceId: defaultSourceId,
      onSaved: onCreated,
    });
  const filtered = profiles.filter((profile) =>
    profile.name.toLocaleLowerCase().includes(keyword.trim().toLocaleLowerCase()),
  );
  const selected = profiles.find((profile) => profile.id === selectedId);
  const [remoteMetadata, setRemoteMetadata] = useState<{
    key: string;
    properties: IProperty[];
    search: ResourceProfile["search"];
    error?: boolean;
  }>();
  const metadataKey = selected?.sourceDeviceId
    ? `${selected.id}:${selected.sourceDeviceId}:${selected.sourceLibraryEpoch ?? ""}:${sourceRevision}:${JSON.stringify(selected.search)}`
    : "";

  useEffect(() => {
    if (!selected?.sourceDeviceId) return;
    const controller = new AbortController();
    const profile = selected;

    setRemoteMetadata(undefined);
    void Promise.all([
      profileSourceApi.properties(profile, controller.signal),
      profileSourceApi.resolve(profile, profile.search, controller.signal),
    ])
      .then(([properties, search]) => {
        if (!controller.signal.aborted) setRemoteMetadata({ key: metadataKey, properties, search });
      })
      .catch(() => {
        if (!controller.signal.aborted)
          setRemoteMetadata({
            key: metadataKey,
            properties: [],
            search: profile.search,
            error: true,
          });
      });

    return () => controller.abort();
  }, [metadataKey]);
  const canProcessSource =
    !selected?.sourceDeviceId ||
    sources.some(
      (source) =>
        source.kind === "managed" &&
        source.deviceId === selected.sourceDeviceId &&
        source.libraryEpoch === selected.sourceLibraryEpoch,
    );
  const sourceMetadataReady =
    !selected?.sourceDeviceId || (remoteMetadata?.key === metadataKey && !remoteMetadata.error);
  const sourceFilterConfig = useMemo(
    () =>
      selected?.sourceDeviceId && remoteMetadata?.key === metadataKey && !remoteMetadata.error
        ? createSourceFilterConfig(selected, remoteMetadata.properties, createPortal)
        : undefined,
    [selected?.sourceDeviceId, metadataKey, remoteMetadata, createPortal],
  );
  const resolvedSearch = selected?.sourceDeviceId
    ? remoteMetadata?.key === metadataKey
      ? remoteMetadata.search
      : selected.search
    : selected?.search;
  const search = draft?.id === selected?.id ? draft?.search : resolvedSearch;
  const scopeEditing = !!draft && draft.id === selected?.id;
  const action = (label: string, onPress: () => void, needsMetadata = false) => (
    <Button
      isDisabled={busy || (needsMetadata && (!metadataReady || !sourceMetadataReady))}
      size="sm"
      startContent={<EditOutlined />}
      variant="flat"
      onPress={onPress}
    >
      {label}
    </Button>
  );
  const fallback = (
    <p className="text-xs text-default-400">{t("resourceProfile.status.inherit")}</p>
  );

  const openProperties = (profile: ResourceProfile) =>
    createPortal(PropertyPoolModal, {
      propertyOptions: profile.propertyOptions,
      allProperties: properties,
      enhancerOptions: profile.enhancerOptions?.enhancers ?? [],
      enhancerDescriptors,
      onSubmit: (propertyOptions) => updateProfile(profile.id, { propertyOptions }),
    });
  const openEnhancers = (profile: ResourceProfile) =>
    createPortal(EnhancementConfigPanel, {
      enhancerOptions: profile.enhancerOptions?.enhancers ?? [],
      propertyCatalog: profile.sourceDeviceId ? remoteMetadata?.properties : undefined,
      onSubmit: (options) =>
        updateProfile(profile.id, {
          enhancerOptions: options.length
            ? {
                enhancers: options,
                executionTarget:
                  profile.enhancerOptions?.executionTarget ?? defaultExecutionTarget(profile),
              }
            : undefined,
        }),
    });
  const bindScope = async (profile: ResourceProfile, scopeId: number | null) => {
    if (busyRef.current) throw new Error(t("resourceProfile.error.busy"));
    busyRef.current = true;
    setBusy(true);
    try {
      const saved = await resourceScopeApi.bind(profile, scopeId);

      acceptProfiles(profilesRef.current.map((item) => (item.id === profile.id ? saved : item)));
      notifyResourceProfilesChanged();
      await loadScopes();
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };
  const editReusableScope = (scope: ResourceScope) =>
    createPortal(ResourceScopeEditorModal, {
      scope,
      profile: { ...scope, priority: 0, createdAt: "", updatedAt: "" },
      onSaved: async () => {
        notifyResourceProfilesChanged();
        await Promise.all([loadScopes(), loadProfiles()]);
      },
    });
  const startProcessing = async (profile: ResourceProfile, kind: 0 | 1) => {
    if (busyRef.current) return;
    busyRef.current = true;
    setBusy(true);
    try {
      await processingApi.start(profile.id, kind);
      setShowProcessingTasks(true);
    } catch (failure) {
      setError(
        failure instanceof Error ? failure.message : t("resourceProfile.processing.startFailed"),
      );
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };
  const duplicate = async (profile: ResourceProfile) => {
    setBusy(true);
    try {
      const response = await BApi.resourceProfile.addResourceProfile(
        toProfileInputModel({
          ...profile,
          name: t("resourceProfile.label.copyName", { name: profile.name }),
        }) as BakabaseServiceModelsInputResourceProfileInputModel,
      );

      checkProfileResponse(response, t("resourceProfile.error.save"));
      if (response.data) {
        notifyResourceProfilesChanged();
        acceptProfiles([...profilesRef.current, response.data]);
        setSelectedId(response.data.id);
        setKeyword("");
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : t("resourceProfile.error.save"));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="mx-auto flex w-full max-w-[1500px] flex-col gap-5 p-4 md:p-6">
      <header className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <h1 className="text-xl font-semibold">{t("menu.resourceProfile")}</h1>
            <HelpCenterButton topic="resourceProfile" />
          </div>
          <p className="mt-1 max-w-3xl text-sm leading-relaxed text-default-500">
            {t("resourceProfile.page.description")}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <Button
            isIconOnly
            aria-label={t("common.action.refresh")}
            isDisabled={busy || !!draft}
            isLoading={loading}
            variant="light"
            onPress={() => {
              void loadProfiles();
              void loadScopes();
              void loadMetadata();
              loadSources();
              setSourceRevision((value) => value + 1);
            }}
          >
            <ReloadOutlined />
          </Button>
          <Button
            color="primary"
            isDisabled={busy || !!draft}
            startContent={<PlusOutlined />}
            onPress={create}
          >
            {t("resourceProfile.action.addProfile")}
          </Button>
        </div>
      </header>
      {sourceError && (
        <p className="text-sm text-warning-700" role="status">
          {t("resourceProfile.source.listUnavailable")}
        </p>
      )}
      {error && (
        <div
          className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-danger-50 px-4 py-3 text-sm text-danger"
          role="alert"
        >
          {error}
          <Button
            size="sm"
            variant="light"
            onPress={() => {
              void loadProfiles();
              void loadScopes();
              void loadMetadata();
              loadSources();
              setSourceRevision((value) => value + 1);
            }}
          >
            {t("resourceProfile.action.retry")}
          </Button>
        </div>
      )}
      <details className="rounded-xl bg-primary-50/50 px-4 py-3 text-sm">
        <summary className="cursor-pointer text-primary">
          <InfoCircleOutlined className="mr-2" />
          {t("resourceProfile.page.howItWorks")}
        </summary>
        <div className="mt-3 space-y-2 text-xs leading-relaxed text-default-600">
          <p>{t("resourceProfile.tip.priority")}</p>
          <p>{t("resourceProfile.tip.enhancerMerge")}</p>
        </div>
      </details>
      <div className="flex flex-wrap items-start gap-3">
        <Select
          disallowEmptySelection
          className="max-w-xs"
          dataSource={[
            { value: "all", label: t("resourceProfile.rule.all") },
            ...Object.keys(ruleFields).map((field) => ({
              value: field,
              label: t(`resourceProfile.rule.${field}`),
            })),
          ]}
          isDisabled={busy || !!draft}
          label={t("resourceProfile.rule.type")}
          selectedKeys={[ruleField]}
          onSelectionChange={(keys) => {
            const value = [...keys][0];

            if (value) setRuleField(value as RuleField | "all");
          }}
        />
        <p className="flex-1 text-xs text-default-500 pt-2">
          {t("resourceProfile.rule.independentHint")}
        </p>
      </div>
      <details
        className="rounded-xl border border-default-200 p-3"
        open={showProcessingTasks}
        onToggle={(event) => setShowProcessingTasks(event.currentTarget.open)}
      >
        <summary className="cursor-pointer text-sm font-medium">
          {t("resourceProfile.processing.title")}
        </summary>
        <div className="mt-3">
          <ResourceProcessingTasks sources={sources} />
        </div>
      </details>
      {scopes.length > 0 && (
        <details className="rounded-xl border border-divider p-3">
          <summary className="cursor-pointer text-sm font-medium">
            {t("resourceProfile.scope.library")}
          </summary>
          <div className="mt-3 space-y-2">
            {scopes.map((scope) => (
              <div key={scope.id} className="flex items-center gap-2 rounded-lg bg-default-50 p-2">
                <div className="min-w-0 flex-1">
                  <p className="truncate text-sm">{scope.name}</p>
                  <p className="text-xs text-default-500">
                    {t("resourceProfile.scope.references", { count: scope.ruleCount })} ·{" "}
                    {scope.sourceDeviceId
                      ? (sources.find((source) => source.deviceId === scope.sourceDeviceId)?.name ??
                        scope.sourceDeviceId)
                      : t("resourceProfile.source.local")}
                  </p>
                </div>
                <Button
                  isDisabled={busy || !!draft}
                  size="sm"
                  variant="light"
                  onPress={() => editReusableScope(scope)}
                >
                  {t("common.action.edit")}
                </Button>
                <Button
                  color="danger"
                  isDisabled={busy || !!draft || scope.ruleCount > 0}
                  size="sm"
                  title={t("resourceProfile.scope.deleteHint")}
                  variant="light"
                  onPress={() =>
                    createPortal(ConfirmModal, {
                      title: t("common.action.delete"),
                      message: t("resourceProfile.scope.deleteConfirm", { name: scope.name }),
                      destructive: true,
                      onConfirm: async () => {
                        await resourceScopeApi.remove(scope);
                        await loadScopes();
                      },
                    })
                  }
                >
                  {t("common.action.delete")}
                </Button>
              </div>
            ))}
          </div>
        </details>
      )}
      {loading && !profiles.length ? (
        <div className="flex justify-center py-20">
          <Spinner />
        </div>
      ) : !profiles.length && !error ? (
        <Card className="border border-divider" shadow="none">
          <CardBody className="items-center gap-3 py-14 text-center">
            <SettingOutlined className="text-3xl text-primary" />
            <h2 className="font-semibold">{t("resourceProfile.empty.title")}</h2>
            <p className="max-w-lg text-sm text-default-500">
              {t("resourceProfile.empty.description")}
            </p>
            <Button color="primary" startContent={<PlusOutlined />} onPress={create}>
              {t("resourceProfile.action.addProfile")}
            </Button>
          </CardBody>
        </Card>
      ) : (
        <div className="flex min-w-0 flex-col items-start gap-5 lg:flex-row">
          <aside className="w-full shrink-0 lg:sticky lg:top-4 lg:w-64">
            <div className="mb-3 flex items-center justify-between px-1">
              <h2 className="text-sm font-medium">{t("resourceProfile.label.profiles")}</h2>
              <span className="text-xs text-default-400">{profiles.length}</span>
            </div>
            <Input
              isClearable
              aria-label={t("resourceProfile.input.search")}
              placeholder={t("resourceProfile.input.search")}
              size="sm"
              startContent={<SearchOutlined className="text-default-400" />}
              value={keyword}
              onClear={() => setKeyword("")}
              onValueChange={setKeyword}
            />
            <div
              aria-label={t("resourceProfile.label.profiles")}
              className="mt-3 flex max-h-64 flex-col gap-2 overflow-y-auto lg:max-h-[calc(100vh-280px)]"
            >
              {filtered.map((profile) => (
                <button
                  key={profile.id}
                  aria-pressed={profile.id === selectedId}
                  className={`w-full rounded-xl px-4 py-3 text-left transition-colors focus-visible:outline-primary disabled:opacity-50 ${profile.id === selectedId ? "bg-primary-50 text-primary ring-1 ring-inset ring-primary-200" : "bg-content1 hover:bg-default-100"}`}
                  disabled={busy || (!!draft && draft.id !== profile.id)}
                  type="button"
                  onClick={() => setSelectedId(profile.id)}
                >
                  <div className="break-words text-sm font-medium">{profile.name}</div>
                  <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-default-500">
                    <span className="w-full">
                      {profile.sourceDeviceId
                        ? (sources.find((source) => source.deviceId === profile.sourceDeviceId)
                            ?.name ?? profile.sourceDeviceId)
                        : t("resourceProfile.source.local")}
                    </span>
                    <span>
                      {t("resourceProfile.label.priority")} {profile.priority}
                    </span>
                    <span>
                      {t(
                        hasProfileConditions(profile.search)
                          ? "resourceProfile.status.filtered"
                          : "resourceProfile.status.allResources",
                      )}
                    </span>
                  </div>
                </button>
              ))}
              {!filtered.length && (
                <p className="p-4 text-center text-sm text-default-400">
                  {t("resourceProfile.empty.noProfilesFound")}
                </p>
              )}
            </div>
          </aside>
          {selected && (
            <Card className="min-w-0 w-full flex-1 border border-divider" shadow="none">
              <CardBody className="gap-5 p-4 md:p-6">
                <div className="flex flex-wrap items-start justify-between gap-3 border-b border-divider pb-5">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <h2 className="break-words text-lg font-semibold">{selected.name}</h2>
                      <Chip size="sm" variant="flat">
                        {t("resourceProfile.label.priority")} {selected.priority}
                      </Chip>
                    </div>
                    <p className="mt-2 text-xs text-default-500">
                      {t("resourceProfile.page.selectedHint")}
                    </p>
                  </div>
                  <div className="flex items-center gap-1">
                    {action(t("resourceProfile.action.editBasicInfo"), () =>
                      createPortal(ResourceProfileModal, {
                        profile: selected,
                        sources,
                        onUpdate: updateProfile,
                      }),
                    )}
                    <Popover
                      placement="bottom-end"
                      trigger={
                        <Button
                          isIconOnly
                          aria-label={t("resourceProfile.label.more")}
                          isDisabled={busy || !!draft}
                          size="sm"
                          variant="light"
                        >
                          <MoreOutlined />
                        </Button>
                      }
                      visible={menuOpen}
                      onVisibleChange={setMenuOpen}
                    >
                      <Listbox
                        aria-label={t("resourceProfile.label.more")}
                        onAction={(key) => {
                          setMenuOpen(false);
                          if (key === "copy") void duplicate(selected);
                          if (key === "clear")
                            createPortal(DeleteEnhancementsModal, { profile: selected });
                          if (key === "delete")
                            createPortal(ConfirmModal, {
                              title: t("common.action.delete"),
                              message: t("resourceProfile.confirm.delete"),
                              destructive: true,
                              onConfirm: async () => {
                                const response = await BApi.resourceProfile.deleteResourceProfile(
                                  selected.id,
                                );

                                checkProfileResponse(response, t("resourceProfile.error.save"));
                                notifyResourceProfilesChanged();
                                acceptProfiles(
                                  profilesRef.current.filter((p) => p.id !== selected.id),
                                );
                              },
                            });
                        }}
                      >
                        <ListboxItem key="copy" startContent={<CopyOutlined />}>
                          {t("resourceProfile.action.duplicate")}
                        </ListboxItem>
                        <ListboxItem
                          key="clear"
                          isDisabled={!!selected.sourceDeviceId}
                          startContent={<ClearOutlined />}
                        >
                          {t("resourceProfile.action.deleteEnhancements")}
                        </ListboxItem>
                        <ListboxItem
                          key="delete"
                          className="text-danger"
                          color="danger"
                          startContent={<DeleteOutlined />}
                        >
                          {t("common.action.delete")}
                        </ListboxItem>
                      </Listbox>
                    </Popover>
                  </div>
                </div>
                {selected.sourceDeviceId && (
                  <div className="rounded-xl bg-primary/5 p-3 text-sm">
                    <p className="font-medium">
                      {t("resourceProfile.source.forDevice", {
                        name:
                          sources.find((source) => source.deviceId === selected.sourceDeviceId)
                            ?.name ?? selected.sourceDeviceId,
                      })}
                    </p>
                    <p className="mt-1 text-xs leading-5 text-default-500">
                      {t("resourceProfile.source.localOnlyHint")}
                    </p>
                  </div>
                )}
                <section className="rounded-xl bg-default-50 p-4">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div className="flex items-center gap-2">
                      <FilterOutlined className="text-primary" />
                      <h3 className="text-sm font-semibold">
                        {t("resourceProfile.label.appliesTo")}
                      </h3>
                      <Chip
                        color={hasProfileConditions(search) ? "primary" : "default"}
                        size="sm"
                        variant="flat"
                      >
                        {t(
                          hasProfileConditions(search)
                            ? "resourceProfile.status.filtered"
                            : "resourceProfile.status.allResources",
                        )}
                      </Chip>
                    </div>
                    <div className="flex flex-wrap items-center gap-2">
                      <Button
                        isDisabled={!sourceMetadataReady}
                        size="sm"
                        startContent={<EyeOutlined />}
                        variant="light"
                        onPress={() =>
                          createPortal(ResourceProfileTestModal, {
                            profile: { ...selected, search },
                            isDraft: scopeEditing,
                          })
                        }
                      >
                        {t("resourceProfile.action.testCriteria")}
                      </Button>
                      {!scopeEditing ? (
                        <Button
                          isDisabled={busy || !sourceMetadataReady}
                          size="sm"
                          startContent={<EditOutlined />}
                          variant="flat"
                          onPress={() =>
                            setDraft({
                              id: selected.id,
                              search: structuredClone(resolvedSearch ?? { page: 1, pageSize: 100 }),
                            })
                          }
                        >
                          {t("resourceProfile.action.editScope")}
                        </Button>
                      ) : (
                        <>
                          <Button
                            isDisabled={busy}
                            size="sm"
                            variant="light"
                            onPress={() => setDraft(undefined)}
                          >
                            {t("common.action.cancel")}
                          </Button>
                          <Button
                            color="primary"
                            isLoading={busy}
                            size="sm"
                            startContent={<CheckOutlined />}
                            onPress={async () => {
                              try {
                                await updateProfile(selected.id, { search: draft!.search });
                                setDraft(undefined);
                                setError("");
                              } catch (e) {
                                setError(
                                  e instanceof Error ? e.message : t("resourceProfile.error.save"),
                                );
                              }
                            }}
                          >
                            {t("common.action.save")}
                          </Button>
                        </>
                      )}
                    </div>
                  </div>
                  <div className="mt-3 flex flex-wrap items-center gap-2">
                    <Select
                      disallowEmptySelection
                      className="max-w-xs"
                      dataSource={[
                        { value: "inline", label: t("resourceProfile.scope.inline") },
                        ...scopes
                          .filter(
                            (scope) =>
                              (scope.sourceDeviceId ?? null) ===
                                (selected.sourceDeviceId ?? null) &&
                              (scope.sourceLibraryEpoch ?? null) ===
                                (selected.sourceLibraryEpoch ?? null),
                          )
                          .map((scope) => ({ value: String(scope.id), label: scope.name })),
                      ]}
                      isDisabled={busy || scopeEditing}
                      label={t("resourceProfile.scope.reuse")}
                      selectedKeys={[
                        selected.resourceScopeId ? String(selected.resourceScopeId) : "inline",
                      ]}
                      size="sm"
                      onSelectionChange={(keys) => {
                        const value = [...keys][0];

                        if (value)
                          void bindScope(selected, value === "inline" ? null : Number(value)).catch(
                            (failure) => setError(failure.message),
                          );
                      }}
                    />
                    <Button
                      isDisabled={busy || scopeEditing || !sourceMetadataReady}
                      size="sm"
                      variant="flat"
                      onPress={() =>
                        createPortal(ResourceScopeEditorModal, {
                          profile: { ...selected, search },
                          onSaved: async (id: number) => {
                            try {
                              const current = profilesRef.current.find(
                                (profile) => profile.id === selected.id,
                              );

                              if (!current) throw new Error(t("resourceProfile.error.missing"));
                              await bindScope(current, id);
                            } finally {
                              await loadScopes();
                            }
                          },
                        })
                      }
                    >
                      {t("resourceProfile.scope.saveAs")}
                    </Button>
                    {selected.resourceScopeId && (
                      <p className="w-full text-xs text-primary">
                        {t("resourceProfile.scope.sharedHint", {
                          count:
                            scopes.find((scope) => scope.id === selected.resourceScopeId)
                              ?.ruleCount ?? 0,
                        })}
                      </p>
                    )}
                  </div>
                  <p className="mt-2 text-xs leading-relaxed text-default-500">
                    {t("resourceProfile.tip.scope")}
                  </p>
                  {!sourceMetadataReady && (
                    <p className="mt-3 text-xs text-default-500" role="status">
                      {t(
                        remoteMetadata?.error
                          ? "resourceProfile.source.unavailable"
                          : "resourceProfile.source.loading",
                      )}
                    </p>
                  )}
                  {sourceMetadataReady && (scopeEditing || hasProfileConditions(search)) && (
                    <div className="mt-4 space-y-3">
                      <div className={busy ? "pointer-events-none opacity-60" : ""}>
                        <ResourceFilterController
                          key={`${selected.id}-${scopeEditing}`}
                          config={sourceFilterConfig}
                          defaultFilterDisplayMode={FilterDisplayMode.Simple}
                          filterLayout="horizontal"
                          group={
                            (search?.group ?? {
                              combinator: 1,
                              disabled: false,
                            }) as SearchFilterGroup
                          }
                          isReadonly={!scopeEditing}
                          showKeyword={false}
                          showRecentFilters={!selected.sourceDeviceId}
                          tags={search?.tags}
                          onGroupChange={(group) =>
                            setDraft(
                              (current) =>
                                current && {
                                  ...current,
                                  search: { ...current.search, group } as typeof current.search,
                                },
                            )
                          }
                          onTagsChange={(tags) =>
                            setDraft(
                              (current) =>
                                current && { ...current, search: { ...current.search, tags } },
                            )
                          }
                        />
                      </div>
                      {!!search?.tags?.length && (
                        <div className="flex flex-wrap gap-2">
                          {search.tags.map((tag) => (
                            <Chip
                              key={tag}
                              size="sm"
                              variant="flat"
                              onClose={
                                scopeEditing
                                  ? () =>
                                      setDraft(
                                        (current) =>
                                          current && {
                                            ...current,
                                            search: {
                                              ...current.search,
                                              tags: current.search.tags?.filter(
                                                (value) => value !== tag,
                                              ),
                                            },
                                          },
                                      )
                                  : undefined
                              }
                            >
                              {t(`ResourceTag.${ResourceTagLabel[tag]}`)}
                            </Chip>
                          ))}
                        </div>
                      )}
                    </div>
                  )}
                </section>
                <div className="divide-y divide-divider">
                  {!selected.sourceDeviceId && showRule("propertyOptions") && (
                    <ConfigSection
                      action={action(
                        t("resourceProfile.action.configureProperties"),
                        () => openProperties(selected),
                        true,
                      )}
                      description={t("resourceProfile.tip.properties")}
                      icon={<AppstoreOutlined />}
                      title={t("resourceProfile.label.properties")}
                    >
                      {selected.propertyOptions?.properties?.length ? (
                        <div className="flex flex-wrap gap-2">
                          {selected.propertyOptions.properties.slice(0, 12).map((ref) => {
                            const property = properties.find(
                              (p) => p.pool === ref.pool && p.id === ref.id,
                            );

                            return (
                              <span
                                key={`${ref.pool}:${ref.id}`}
                                className="max-w-full rounded-lg bg-default-100 px-2.5 py-1.5 text-xs"
                              >
                                {property ? (
                                  <PropertyLabel property={property} />
                                ) : (
                                  t("resourceProfile.propertyPool.unknownProperty", { id: ref.id })
                                )}
                              </span>
                            );
                          })}
                        </div>
                      ) : selected.propertyOptions ? (
                        <p className="text-xs text-default-400">
                          {t("resourceProfile.status.emptyProperties")}
                        </p>
                      ) : (
                        fallback
                      )}
                      {(selected.propertyOptions?.properties?.length ?? 0) > 12 && (
                        <Button
                          className="mt-2"
                          isDisabled={!metadataReady || busy}
                          size="sm"
                          variant="light"
                          onPress={() => openProperties(selected)}
                        >
                          {t("resourceProfile.action.viewAllProperties", {
                            count: selected.propertyOptions!.properties!.length,
                          })}
                        </Button>
                      )}
                    </ConfigSection>
                  )}
                  {!selected.sourceDeviceId && showRule("nameTemplate") && (
                    <ConfigSection
                      action={action(
                        t("resourceProfile.action.configureName"),
                        () =>
                          createPortal(DisplayNameTemplateEditorModal, {
                            template: selected.nameTemplate,
                            properties,
                            onSubmit: (nameTemplate) =>
                              updateProfile(selected.id, {
                                nameTemplate: nameTemplate || undefined,
                              }),
                          }),
                        true,
                      )}
                      description={t("resourceProfile.tip.nameTemplate")}
                      icon={<FileTextOutlined />}
                      title={t("resourceProfile.label.nameTemplate")}
                    >
                      {selected.nameTemplate ? (
                        <code className="block whitespace-pre-wrap break-words rounded-lg bg-default-100 px-3 py-2 text-sm">
                          {selected.nameTemplate}
                        </code>
                      ) : (
                        fallback
                      )}
                    </ConfigSection>
                  )}
                  {(showRule("playerOptions") ||
                    showRule("playableFileOptions") ||
                    showRule("openingMode")) && (
                    <ConfigSection
                      action={null}
                      description={t("resourceProfile.tip.playback")}
                      icon={<PlayCircleOutlined />}
                      title={t("resourceProfile.label.playback")}
                    >
                      {selected.sourceDeviceId && showRule("openingMode") && (
                        <Select
                          disallowEmptySelection
                          className="mb-3 max-w-md"
                          dataSource={["inherit", "browser", "native", "ask"].map((mode) => ({
                            value: mode,
                            label: t(`resourceProfile.source.mode.${mode}`),
                          }))}
                          description={t("resourceProfile.source.openingHint")}
                          isDisabled={busy}
                          label={t("resourceProfile.source.openingMode")}
                          selectedKeys={[selected.openingMode ?? "inherit"]}
                          onSelectionChange={(keys) => {
                            const mode = [...keys][0];

                            if (mode)
                              void updateProfile(selected.id, {
                                openingMode:
                                  mode === "inherit"
                                    ? null
                                    : (mode as ResourceProfile["openingMode"]),
                              }).catch((e) => setError(e.message));
                          }}
                        />
                      )}
                      <div
                        className={`grid gap-3 ${!selected.sourceDeviceId ? "xl:grid-cols-2" : ""}`}
                      >
                        {canProcessSource && showRule("playableFileOptions") && (
                          <div className="rounded-xl bg-default-50 p-3">
                            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                              <h4 className="text-xs font-medium">
                                {t("resourceProfile.label.playableFiles")}
                              </h4>
                              {action(t("common.action.configure"), () =>
                                createPortal(PlayableFileSelectorModal, {
                                  options: selected.playableFileOptions,
                                  onSubmit: (options) =>
                                    updateProfile(selected.id, {
                                      playableFileOptions:
                                        options.extensions?.length || options.fileNamePattern
                                          ? {
                                              ...options,
                                              executionTarget:
                                                selected.playableFileOptions?.executionTarget ??
                                                defaultExecutionTarget(selected),
                                            }
                                          : undefined,
                                    }),
                                }),
                              )}
                            </div>
                            {selected.playableFileOptions ? (
                              <div className="flex flex-wrap gap-1.5">
                                {selected.playableFileOptions.extensions?.map((extension) => (
                                  <Chip key={extension} size="sm" variant="flat">
                                    {extension}
                                  </Chip>
                                ))}
                                {selected.playableFileOptions.fileNamePattern && (
                                  <code className="w-full break-all text-xs text-default-500">
                                    {selected.playableFileOptions.fileNamePattern}
                                  </code>
                                )}
                              </div>
                            ) : (
                              fallback
                            )}
                            {selected.playableFileOptions && (
                              <Button
                                className="mt-3"
                                color="primary"
                                isDisabled={busy || !!draft}
                                size="sm"
                                variant="flat"
                                onPress={() => void startProcessing(selected, 0)}
                              >
                                {t("resourceProfile.processing.run")}
                              </Button>
                            )}
                            {selected.playableFileOptions && (
                              <RuleExecutionTargetSelect
                                disabled={busy}
                                sources={sources}
                                value={selected.playableFileOptions.executionTarget}
                                onChange={(executionTarget) =>
                                  void updateProfile(selected.id, {
                                    playableFileOptions: {
                                      ...selected.playableFileOptions,
                                      executionTarget,
                                    },
                                  }).catch((error) => setError(error.message))
                                }
                              />
                            )}
                          </div>
                        )}
                        {showRule("playerOptions") && (
                          <div className="rounded-xl bg-default-50 p-3">
                            <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
                              <h4 className="text-xs font-medium">
                                {t("resourceProfile.label.players")}
                              </h4>
                              {action(t("common.action.configure"), () =>
                                createPortal(PlayerSelectorModal, {
                                  players: selected.playerOptions?.players ?? [],
                                  onSubmit: (players) =>
                                    updateProfile(selected.id, {
                                      playerOptions:
                                        players.length || selected.sourceDeviceId
                                          ? { players }
                                          : undefined,
                                    }),
                                }),
                              )}
                            </div>
                            {selected.sourceDeviceId && (
                              <div className="mb-3 flex flex-wrap gap-2">
                                <Button
                                  isDisabled={busy}
                                  size="sm"
                                  variant="light"
                                  onPress={() =>
                                    void updateProfile(selected.id, {
                                      playerOptions: { players: [] },
                                      openingMode: "native",
                                    }).catch((e) => setError(e.message))
                                  }
                                >
                                  {t("resourceProfile.source.systemDefault")}
                                </Button>
                                <Button
                                  isDisabled={busy || !selected.playerOptions}
                                  size="sm"
                                  variant="light"
                                  onPress={() =>
                                    void updateProfile(selected.id, {
                                      playerOptions: undefined,
                                    }).catch((e) => setError(e.message))
                                  }
                                >
                                  {t("resourceProfile.source.inheritPlayer")}
                                </Button>
                              </div>
                            )}
                            {selected.playerOptions?.players?.length ? (
                              <div className="space-y-2">
                                {selected.playerOptions.players.map((player, index) => (
                                  <div
                                    key={index}
                                    className="break-all text-xs"
                                    title={player.executablePath}
                                  >
                                    {player.executablePath?.split(/[\\/]/).pop()}
                                    <span className="ml-2 text-default-400">
                                      {player.extensions?.join(", ") ||
                                        t("resourceProfile.status.allExtensions")}
                                    </span>
                                  </div>
                                ))}
                              </div>
                            ) : (
                              <p className="text-xs text-default-400">
                                {t(
                                  selected.playerOptions
                                    ? "resourceProfile.status.emptyPlayers"
                                    : "resourceProfile.status.defaultPlayer",
                                )}
                              </p>
                            )}
                          </div>
                        )}
                      </div>
                    </ConfigSection>
                  )}
                  {canProcessSource && showRule("enhancerOptions") && (
                    <ConfigSection
                      action={action(
                        t("resourceProfile.action.configureEnhancers"),
                        () => openEnhancers(selected),
                        true,
                      )}
                      description={t("resourceProfile.tip.enhancers")}
                      icon={<ThunderboltOutlined />}
                      title={t("resourceProfile.label.enhancers")}
                    >
                      {!!selected.enhancerOptions?.enhancers?.length && (
                        <Button
                          className="mb-3"
                          color="primary"
                          isDisabled={busy || !!draft || !sourceMetadataReady}
                          size="sm"
                          variant="flat"
                          onPress={() => void startProcessing(selected, 1)}
                        >
                          {t("resourceProfile.processing.run")}
                        </Button>
                      )}
                      {selected.enhancerOptions && (
                        <RuleExecutionTargetSelect
                          disabled={busy}
                          sources={sources}
                          value={selected.enhancerOptions.executionTarget}
                          onChange={(executionTarget) =>
                            void updateProfile(selected.id, {
                              enhancerOptions: { ...selected.enhancerOptions, executionTarget },
                            }).catch((error) => setError(error.message))
                          }
                        />
                      )}
                      {selected.enhancerOptions?.enhancers?.length ? (
                        <div className="flex flex-wrap gap-2">
                          {selected.enhancerOptions.enhancers.map((option) => {
                            const descriptor = enhancerDescriptors.find(
                              (e) => e.id === option.enhancerId,
                            );

                            return (
                              <div
                                key={option.enhancerId}
                                className="rounded-lg bg-default-100 px-3 py-2"
                              >
                                {descriptor ? (
                                  <BriefEnhancer enhancer={descriptor} />
                                ) : (
                                  <span>#{option.enhancerId}</span>
                                )}
                              </div>
                            );
                          })}
                        </div>
                      ) : (
                        fallback
                      )}
                    </ConfigSection>
                  )}
                </div>
              </CardBody>
            </Card>
          )}
        </div>
      )}
    </div>
  );
}
