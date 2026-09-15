"use client";

import type { ReactNode } from "react";
import type {
  BakabaseServiceModelsViewResourceProfileViewModel as ResourceProfile,
  BakabaseServiceModelsInputResourceProfileInputModel,
} from "@/sdk/Api";
import type { IProperty } from "@/components/Property/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";

import { useEffect, useRef, useState } from "react";
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
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { HelpCenterButton } from "@/components/HelpCenter";
import { PropertyLabel } from "@/components/Property";
import { ResourceFilterController } from "@/components/ResourceFilter";
import { FilterDisplayMode, PropertyPool, ResourceTagLabel } from "@/sdk/constants";
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
  const [profiles, setProfiles] = useState<ResourceProfile[]>([]);
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
    setSelectedId((id) => (sorted.some((p) => p.id === id) ? id : sorted[0]?.id));
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
  }, []);

  const updateProfile = async (id: number, updates: Partial<ResourceProfile>) => {
    if (busyRef.current) throw new Error(t("resourceProfile.error.busy"));
    const current = profilesRef.current.find((profile) => profile.id === id);

    if (!current) throw new Error(t("resourceProfile.error.missing"));
    busyRef.current = true;
    setBusy(true);
    try {
      const merged = { ...current, ...updates };
      const response = await BApi.resourceProfile.updateResourceProfile(
        id,
        toProfileInputModel(merged) as BakabaseServiceModelsInputResourceProfileInputModel,
      );

      checkProfileResponse(response, t("resourceProfile.error.save"));
      // Apply only after the server accepts the complete update. A later modal
      // always reads this ref, including scope priorities added by other edits.
      acceptProfiles(profilesRef.current.map((profile) => (profile.id === id ? merged : profile)));
    } finally {
      busyRef.current = false;
      setBusy(false);
    }
  };
  const onCreated = (profile: ResourceProfile) => {
    acceptProfiles([...profilesRef.current, profile]);
    setSelectedId(profile.id);
    setKeyword("");
    setDraft({ id: profile.id, search: profile.search ?? { page: 1, pageSize: 100 } });
  };
  const create = () =>
    createPortal(ResourceProfileModal, {
      existingNames: profiles.map((p) => p.name),
      onSaved: onCreated,
    });
  const filtered = profiles.filter((profile) =>
    profile.name.toLocaleLowerCase().includes(keyword.trim().toLocaleLowerCase()),
  );
  const selected = profiles.find((profile) => profile.id === selectedId);
  const search = draft?.id === selected?.id ? draft?.search : selected?.search;
  const scopeEditing = !!draft && draft.id === selected?.id;
  const action = (label: string, onPress: () => void, needsMetadata = false) => (
    <Button
      isDisabled={busy || (needsMetadata && !metadataReady)}
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
      onSubmit: (options) => {
        const current = profilesRef.current.find((p) => p.id === profile.id)!;
        const refs = [...(current.propertyOptions?.properties ?? [])];
        const seen = new Set(refs.map((ref) => `${ref.pool}:${ref.id}`));

        for (const enhancer of options)
          for (const target of enhancer.targetOptions ?? []) {
            if (!target.propertyPool || !target.propertyId) continue;
            const key = `${target.propertyPool}:${target.propertyId}`;

            if (!seen.has(key)) {
              refs.push({ pool: target.propertyPool, id: target.propertyId });
              seen.add(key);
            }
          }

        return updateProfile(profile.id, {
          enhancerOptions: options.length ? { enhancers: options } : undefined,
          propertyOptions:
            refs.length > (current.propertyOptions?.properties?.length ?? 0)
              ? { ...current.propertyOptions, properties: refs }
              : current.propertyOptions,
        });
      },
    });
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
              void loadMetadata();
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
              void loadMetadata();
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
                        <ListboxItem key="clear" startContent={<ClearOutlined />}>
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
                        action(t("resourceProfile.action.editScope"), () =>
                          setDraft({
                            id: selected.id,
                            search: structuredClone(selected.search ?? { page: 1, pageSize: 100 }),
                          }),
                        )
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
                  <p className="mt-2 text-xs leading-relaxed text-default-500">
                    {t("resourceProfile.tip.scope")}
                  </p>
                  {(scopeEditing || hasProfileConditions(search)) && (
                    <div className="mt-4 space-y-3">
                      <div className={busy ? "pointer-events-none opacity-60" : ""}>
                        <ResourceFilterController
                          key={`${selected.id}-${scopeEditing}`}
                          defaultFilterDisplayMode={FilterDisplayMode.Simple}
                          filterLayout="horizontal"
                          group={
                            (search?.group ?? {
                              combinator: 1,
                              disabled: false,
                            }) as SearchFilterGroup
                          }
                          isReadonly={!scopeEditing}
                          onGroupChange={(group) =>
                            setDraft(
                              (current) =>
                                current && {
                                  ...current,
                                  search: { ...current.search, group } as typeof current.search,
                                },
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
                  <ConfigSection
                    action={action(
                      t("resourceProfile.action.configureName"),
                      () =>
                        createPortal(DisplayNameTemplateEditorModal, {
                          template: selected.nameTemplate,
                          properties,
                          onSubmit: (nameTemplate) =>
                            updateProfile(selected.id, { nameTemplate: nameTemplate || undefined }),
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
                  <ConfigSection
                    action={null}
                    description={t("resourceProfile.tip.playback")}
                    icon={<PlayCircleOutlined />}
                    title={t("resourceProfile.label.playback")}
                  >
                    <div className="grid gap-3 xl:grid-cols-2">
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
                                      ? options
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
                      </div>
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
                                  playerOptions: players.length ? { players } : undefined,
                                }),
                            }),
                          )}
                        </div>
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
                    </div>
                  </ConfigSection>
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
                </div>
              </CardBody>
            </Card>
          )}
        </div>
      )}
    </div>
  );
}
