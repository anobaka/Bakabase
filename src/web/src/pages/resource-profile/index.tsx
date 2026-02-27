"use client";

import { useTranslation } from "react-i18next";
import { useEffect, useMemo, useRef, useState } from "react";
import { SearchOutlined, PlusOutlined, EditOutlined, DeleteOutlined, CopyOutlined, ExperimentOutlined, ClearOutlined, MoreOutlined, InfoCircleOutlined, QuestionCircleOutlined, CheckOutlined } from "@ant-design/icons";
import { BsController } from "react-icons/bs";

import BApi from "@/sdk/BApi";
import type {
  BakabaseServiceModelsViewResourceProfileViewModel,
  BakabaseAbstractionsModelsDomainEnhancerFullOptions,
  BakabaseAbstractionsModelsDomainResourceProfilePlayableFileOptions,
  BakabaseAbstractionsModelsDomainMediaLibraryPlayer,
  BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions,
} from "@/sdk/Api";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { IProperty } from "@/components/Property/models";
import { Button, Input, Table, TableHeader, TableBody, TableColumn, TableRow, TableCell, Chip, Tooltip, Popover, Listbox, ListboxItem, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceProfileModal from "./components/ResourceProfileModal";
import ResourceProfileTestModal from "./components/ResourceProfileTestModal";
import DisplayNameTemplateEditorModal from "./components/DisplayNameTemplateEditorModal";
import EnhancementConfigPanel from "./components/EnhancementConfigPanel";
import PlayableFileSelectorModal from "./components/PlayableFileSelectorModal";
import PlayerSelectorModal from "./components/PlayerSelectorModal";
import DeleteEnhancementsModal from "./components/DeleteEnhancementsModal";
import PropertyPoolModal from "./components/PropertyPoolModal";
import PropertySelector from "@/components/PropertySelector";
import { ResourceFilterController, toSearchInputModel } from "@/components/ResourceFilter";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import { getEnumKey } from "@/i18n";
import type { ResourceSearchInputModel } from "@/components/ResourceFilter/utils/toInputModel";
import { PropertyPool, resourceTags, builtinPropertyForDisplayNames, FilterDisplayMode } from "@/sdk/constants";
import { splitPathIntoSegments } from "@/components/utils";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";
import { EnhancerDescription } from "@/components/Chips/Terms";
import { ResourceProfileGuideModal, useResourceProfileGuide } from "./components/ResourceProfileGuide";

// Parse template and render with highlighted properties
const parseTemplateSegments = (
  template: string,
  validPropertyNames: Set<string>
): Array<{ type: "text" | "valid" | "invalid"; content: string }> => {
  const segments: Array<{ type: "text" | "valid" | "invalid"; content: string }> = [];
  const regex = /\{([^}]+)\}/g;
  let lastIndex = 0;
  let match;

  while ((match = regex.exec(template)) !== null) {
    // Add text before the match
    if (match.index > lastIndex) {
      segments.push({ type: "text", content: template.slice(lastIndex, match.index) });
    }
    // Check if property name is valid
    const propName = match[1];
    const isValid = validPropertyNames.has(propName);
    segments.push({ type: isValid ? "valid" : "invalid", content: propName });
    lastIndex = regex.lastIndex;
  }

  // Add remaining text
  if (lastIndex < template.length) {
    segments.push({ type: "text", content: template.slice(lastIndex) });
  }

  return segments;
};

type ResourceProfile = BakabaseServiceModelsViewResourceProfileViewModel;

/**
 * Convert ResourceProfile to API input model format
 * This ensures search.group.filters[].dbValue is serialized string
 *
 * IMPORTANT: We use explicit null for optional fields to ensure they are included in JSON.
 * If we use undefined, JSON.stringify will omit the field, and the backend will
 * interpret missing fields as null, potentially clearing existing data unexpectedly.
 */
const toProfileInputModel = (profile: Partial<ResourceProfile>) => {
  return {
    name: profile.name ?? "",
    search: profile.search ? toSearchInputModel(profile.search) : null,
    nameTemplate: profile.nameTemplate ?? null,
    enhancerOptions: profile.enhancerOptions ?? null,
    playableFileOptions: profile.playableFileOptions ?? null,
    playerOptions: profile.playerOptions ?? null,
    propertyOptions: profile.propertyOptions ?? null,
    priority: profile.priority ?? 0,
  };
};

const ResourceProfilePage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const { showGuide, completeGuide, resetGuide } = useResourceProfileGuide();
  const [profiles, setProfiles] = useState<ResourceProfile[]>([]);
  const [keyword, setKeyword] = useState("");
  const [loading, setLoading] = useState(true);
  const [properties, setProperties] = useState<IProperty[]>([]);
  const [enhancerDescriptors, setEnhancerDescriptors] = useState<EnhancerDescriptor[]>([]);
  const [editingProfileIds, setEditingProfileIds] = useState<Set<number>>(new Set());
  // Store pending filter group changes while editing (profileId -> pending group)
  const [pendingFilterGroups, setPendingFilterGroups] = useState<Map<number, SearchFilterGroup>>(new Map());

  // Use ref to always access the latest profiles in callbacks (avoids stale closure issues)
  const profilesRef = useRef<ResourceProfile[]>(profiles);
  profilesRef.current = profiles;

  const loadProfiles = async () => {
    setLoading(true);
    try {
      const rsp = await BApi.resourceProfile.getAllResourceProfiles();
      setProfiles(((rsp.data || []) as ResourceProfile[]).sort((a, b) => b.priority - a.priority));
    } catch (e) {
      console.error("Failed to load resource profiles", e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadProfiles();
    BApi.property.getPropertiesByPool(PropertyPool.All).then((r) => {
      setProperties((r.data || []) as IProperty[]);
    });
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      setEnhancerDescriptors((r.data || []) as EnhancerDescriptor[]);
    });
  }, []);

  const filteredProfiles = profiles.filter(
    (p) =>
      keyword === "" ||
      p.name?.toLowerCase().includes(keyword.toLowerCase())
  );

  const handleDelete = async (id: number) => {
    if (!window.confirm(t("resourceProfile.confirm.delete"))) {
      return;
    }
    try {
      await BApi.resourceProfile.deleteResourceProfile(id);
      loadProfiles();
    } catch (e) {
      console.error("Failed to delete resource profile", e);
    }
  };

  const handleDuplicate = async (profile: ResourceProfile) => {
    try {
      const inputModel = toProfileInputModel({
        ...profile,
        name: `${profile.name} (Copy)`,
      });
      await BApi.resourceProfile.addResourceProfile(inputModel as any);
      loadProfiles();
    } catch (e) {
      console.error("Failed to duplicate resource profile", e);
    }
  };

  const handleTest = (profile: ResourceProfile) => {
    createPortal(ResourceProfileTestModal, {
      profile,
    });
  };

  const updateProfile = async (profileOrId: ResourceProfile | number, updates: Partial<ResourceProfile>) => {
    try {
      const id = typeof profileOrId === 'number' ? profileOrId : profileOrId.id;
      // Always get the latest profile from ref to avoid stale closure issues
      const currentProfile = profilesRef.current.find(p => p.id === id);
      if (!currentProfile) {
        console.error("Profile not found", id);
        return;
      }
      const merged = { ...currentProfile, ...updates };
      const inputModel = toProfileInputModel(merged);
      await BApi.resourceProfile.updateResourceProfile(id, inputModel as any);
      loadProfiles();
    } catch (e) {
      console.error("Failed to update resource profile", e);
    }
  };

  const renderNameTemplate = (profile: ResourceProfile) => {
    const hasTemplate = !!profile.nameTemplate;

    const openModal = () => {
      createPortal(DisplayNameTemplateEditorModal, {
        template: profile.nameTemplate,
        properties,
        onSubmit: (template: string) => {
          updateProfile(profile, { nameTemplate: template || undefined });
        },
      });
    };

    if (!hasTemplate) {
      return (
        <Button
          size="sm"
          variant="light"
          className="min-w-0 px-2 h-auto py-1"
          onPress={openModal}
        >
          <span className="text-default-400">-</span>
          <EditOutlined className="ml-1 text-xs opacity-50 flex-shrink-0" />
        </Button>
      );
    }

    // Build valid property names set
    const builtinNames = builtinPropertyForDisplayNames.map((v) =>
      t(getEnumKey('BuiltinPropertyForDisplayName', v.label))
    );
    const customNames = properties.map((p) => p.name!);
    const validPropertyNames = new Set([...builtinNames, ...customNames]);

    // Parse template into segments
    const segments = parseTemplateSegments(profile.nameTemplate!, validPropertyNames);

    return (
      <Button
        size="sm"
        variant="light"
        className="min-w-0 px-2 h-auto py-1 flex-wrap"
        onPress={openModal}
      >
        <div className="text-sm text-left flex flex-wrap items-center gap-0.5">
          {segments.map((seg, idx) => {
            if (seg.type === "text") {
              return <span key={idx}>{seg.content}</span>;
            }
            if (seg.type === "valid") {
              return (
                <Chip key={idx} size="sm" color="primary" variant="flat" className="h-5 px-1 text-xs">
                  {seg.content}
                </Chip>
              );
            }
            // invalid
            return (
              <Chip key={idx} size="sm" color="danger" variant="flat" className="h-5 px-1 text-xs">
                {seg.content}
              </Chip>
            );
          })}
        </div>
        <EditOutlined className="ml-1 text-xs opacity-50 flex-shrink-0" />
      </Button>
    );
  };

  const renderEnhancers = (profile: ResourceProfile) => {
    const enhancerOptions = profile.enhancerOptions?.enhancers ?? [];
    const hasEnhancers = enhancerOptions.length > 0;

    const openEnhancerModal = () => {
      createPortal(EnhancementConfigPanel, {
        enhancerOptions: enhancerOptions,
        onSubmit: (options: BakabaseAbstractionsModelsDomainEnhancerFullOptions[]) => {
          updateProfile(profile, {
            enhancerOptions: options.length > 0 ? { enhancers: options } : undefined,
          });
        },
      });
    };

    if (!hasEnhancers) {
      return (
        <Tooltip content={t("resourceProfile.tip.configureEnhancers")}>
          <Button
            size="sm"
            variant="light"
            className="min-w-0"
            onPress={openEnhancerModal}
          >
            <span className="text-default-400">-</span>
            <EditOutlined className="ml-1 text-xs opacity-50" />
          </Button>
        </Tooltip>
      );
    }

    // Get enhancer descriptors for the selected enhancers
    const selectedEnhancers = enhancerOptions
      .map((opt: BakabaseAbstractionsModelsDomainEnhancerFullOptions) => enhancerDescriptors.find((e: EnhancerDescriptor) => e.id === opt.enhancerId))
      .filter((e): e is EnhancerDescriptor => e != null);

    return (
      <div className="flex flex-wrap gap-1">
        {selectedEnhancers.map((enhancer: EnhancerDescriptor) => (
          <Button
            key={enhancer.id}
            size="sm"
            variant="light"
            className="min-w-0 h-auto py-1 px-2"
            onPress={openEnhancerModal}
          >
            <BriefEnhancer enhancer={enhancer} />
          </Button>
        ))}
      </div>
    );
  };

  const renderPlayableFiles = (profile: ResourceProfile) => {
    const options = profile.playableFileOptions;
    const hasOptions = !!(options?.extensions?.length || options?.fileNamePattern);

    const openModal = () => {
      createPortal(PlayableFileSelectorModal, {
        options: profile.playableFileOptions,
        onSubmit: (opts: BakabaseAbstractionsModelsDomainResourceProfilePlayableFileOptions) => {
          const hasOpts = !!(opts.extensions?.length || opts.fileNamePattern);
          updateProfile(profile, {
            playableFileOptions: hasOpts ? opts : undefined,
          });
        },
      });
    };

    if (!hasOptions) {
      return (
        <Button
          size="sm"
          variant="light"
          className="min-w-0"
          onPress={openModal}
        >
          <span className="text-default-400">-</span>
          <EditOutlined className="ml-1 text-xs opacity-50" />
        </Button>
      );
    }

    return (
      <div className="flex flex-wrap gap-1 items-center">
        {/* Extensions */}
        {options?.extensions?.map((ext: string) => (
          <Chip
            key={ext}
            size="sm"
            color="secondary"
            variant="flat"
            className="cursor-pointer h-5 text-xs"
            onClick={openModal}
          >
            {ext}
          </Chip>
        ))}
        {/* File name pattern */}
        {options?.fileNamePattern && (
          <Chip
            size="sm"
            color="warning"
            variant="flat"
            className="cursor-pointer h-5 text-xs"
            onClick={openModal}
          >
            {options.fileNamePattern}
          </Chip>
        )}
        <Button
          isIconOnly
          size="sm"
          variant="light"
          className="min-w-0 w-6 h-6"
          onPress={openModal}
        >
          <EditOutlined className="text-xs opacity-50" />
        </Button>
      </div>
    );
  };

  const renderPlayers = (profile: ResourceProfile) => {
    const players = profile.playerOptions?.players ?? [];
    const hasPlayers = players.length > 0;

    const openModal = () => {
      createPortal(PlayerSelectorModal, {
        players: players,
        onSubmit: async (newPlayers: BakabaseAbstractionsModelsDomainMediaLibraryPlayer[]) => {
          updateProfile(profile, {
            playerOptions: newPlayers.length > 0 ? { players: newPlayers } : undefined,
          });
        },
      });
    };

    if (!hasPlayers) {
      return (
        <Button
          size="sm"
          variant="light"
          className="min-w-0"
          onPress={openModal}
        >
          <span className="text-default-400">-</span>
          <EditOutlined className="ml-1 text-xs opacity-50" />
        </Button>
      );
    }

    return (
      <div className="flex flex-wrap gap-1 items-center">
        {players.map((player: BakabaseAbstractionsModelsDomainMediaLibraryPlayer, index: number) => {
          const executablePathSegments = splitPathIntoSegments(player.executablePath);
          const playerName = executablePathSegments[executablePathSegments.length - 1];
          const extCount = player.extensions?.length ?? 0;

          return (
            <Tooltip
              key={index}
              content={
                <div className="flex flex-col gap-1">
                  <div className="font-medium">{playerName}</div>
                  {extCount > 0 && (
                    <div className="flex flex-wrap gap-1">
                      {player.extensions?.map((ext: string) => (
                        <Chip key={ext} size="sm" variant="flat">
                          {ext}
                        </Chip>
                      ))}
                    </div>
                  )}
                </div>
              }
            >
              <Chip
                size="sm"
                color="success"
                variant="flat"
                className="cursor-pointer h-5 text-xs"
                onClick={openModal}
                startContent={<BsController className="text-xs" />}
              >
                {playerName}
                {extCount > 0 && <span className="ml-1 opacity-60">({extCount})</span>}
              </Chip>
            </Tooltip>
          );
        })}
        <Button
          isIconOnly
          size="sm"
          variant="light"
          className="min-w-0 w-6 h-6"
          onPress={openModal}
        >
          <EditOutlined className="text-xs opacity-50" />
        </Button>
      </div>
    );
  };

  const renderProperties = (profile: ResourceProfile) => {
    const propertyRefs = profile.propertyOptions?.properties ?? [];
    const hasProperties = propertyRefs.length > 0;

    const openModal = () => {
      createPortal(PropertyPoolModal, {
        propertyOptions: profile.propertyOptions,
        allProperties: properties,
        enhancerOptions: profile.enhancerOptions?.enhancers ?? [],
        enhancerDescriptors,
        onSubmit: (newPropertyOptions: BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions | undefined) => {
          updateProfile(profile, {
            propertyOptions: newPropertyOptions,
          });
        },
      });
    };

    if (!hasProperties) {
      return (
        <Button
          size="sm"
          variant="light"
          className="min-w-0"
          onPress={openModal}
        >
          <span className="text-default-400">-</span>
          <EditOutlined className="ml-1 text-xs opacity-50" />
        </Button>
      );
    }

    // Find property objects for the references
    const selectedProperties = propertyRefs
      .map((ref) => properties.find((p) => p.pool === ref.pool && p.id === ref.id))
      .filter((p): p is NonNullable<typeof p> => p != null);

    return (
      <div className="flex flex-wrap gap-1 items-center">
        {selectedProperties.map((property) => (
          <Chip
            key={`${property.pool}-${property.id}`}
            size="sm"
            color="secondary"
            variant="flat"
            className="cursor-pointer h-5 text-xs"
            onClick={openModal}
          >
            {property.name}
          </Chip>
        ))}
        <Button
          isIconOnly
          size="sm"
          variant="light"
          className="min-w-0 w-6 h-6"
          onPress={openModal}
        >
          <EditOutlined className="text-xs opacity-50" />
        </Button>
      </div>
    );
  };

  const columns = [
    {
      key: "priority",
      label: t("resourceProfile.label.priority"),
      description: t("resourceProfile.tip.priority"),
      width: 80,
    },
    {
      key: "name",
      label: t("resourceProfile.label.name"),
      description: t("resourceProfile.tip.name"),
      width: 150,
    },
    {
      key: "search",
      label: t("resourceProfile.label.searchCriteria"),
      description: t("resourceProfile.tip.searchCriteria"),
      render: (profile: ResourceProfile) => {
        const savedGroup = profile.search?.group ?? { combinator: 1, disabled: false };
        const isEditing = editingProfileIds.has(profile.id!);
        // Use pending group if editing, otherwise use saved group
        const group = isEditing && pendingFilterGroups.has(profile.id!)
          ? pendingFilterGroups.get(profile.id!)!
          : savedGroup;

        return (
          <div className="flex flex-col gap-1">
            <div className="flex items-start gap-2">
              <div className="flex-1">
                <ResourceFilterController
                  group={group as SearchFilterGroup}
                  onGroupChange={(newGroup) => {
                    // Store changes locally while editing
                    setPendingFilterGroups((prev) => {
                      const next = new Map(prev);
                      next.set(profile.id!, newGroup);
                      return next;
                    });
                  }}
                  defaultFilterDisplayMode={FilterDisplayMode.Simple}
                  filterLayout="horizontal"
                  isReadonly={!isEditing}
                  autoCreateMediaLibraryFilter
                />
              </div>
              <Tooltip content={isEditing ? t("common.action.save") : t("common.action.edit")}>
                <Button
                  isIconOnly
                  size="sm"
                  variant="light"
                  color={isEditing ? "success" : "default"}
                  onPress={() => {
                    if (isEditing) {
                      // Save pending changes when exiting edit mode
                      const pendingGroup = pendingFilterGroups.get(profile.id!);
                      if (pendingGroup) {
                        updateProfile(profile, {
                          search: {
                            ...profile.search,
                            group: pendingGroup,
                          } as typeof profile.search,
                        });
                        // Clear pending changes
                        setPendingFilterGroups((prev) => {
                          const next = new Map(prev);
                          next.delete(profile.id!);
                          return next;
                        });
                      }
                      setEditingProfileIds((prev) => {
                        const next = new Set(prev);
                        next.delete(profile.id!);
                        return next;
                      });
                    } else {
                      // Enter edit mode
                      setEditingProfileIds((prev) => {
                        const next = new Set(prev);
                        next.add(profile.id!);
                        return next;
                      });
                    }
                  }}
                >
                  {isEditing ? <CheckOutlined /> : <EditOutlined />}
                </Button>
              </Tooltip>
            </div>
            {profile.search?.tags && profile.search.tags.length > 0 && (
              <div className="flex flex-wrap gap-1">
                {profile.search.tags.map((tag) => (
                  <Chip key={tag} size="sm" variant="flat" color="warning">
                    {t(getEnumKey('ResourceTag', resourceTags.find((rt) => rt.value === tag)?.label!))}
                  </Chip>
                ))}
              </div>
            )}
          </div>
        );
      },
    },
    {
      key: "nameTemplate",
      label: t("resourceProfile.label.nameTemplate"),
      description: t("resourceProfile.tip.nameTemplate"),
      render: renderNameTemplate,
    },
    {
      key: "enhancers",
      label: t("resourceProfile.label.enhancers"),
      description: <EnhancerDescription />,
      width: 130,
      render: renderEnhancers,
    },
    {
      key: "playableFiles",
      label: t("resourceProfile.label.playableFiles"),
      description: t("resourceProfile.tip.playableFiles"),
      width: 130,
      render: renderPlayableFiles,
    },
    {
      key: "players",
      label: t("resourceProfile.label.players"),
      description: t("resourceProfile.tip.players"),
      width: 200,
      render: renderPlayers,
    },
    {
      key: "properties",
      label: t("resourceProfile.label.properties"),
      description: t("resourceProfile.tip.properties"),
      width: 200,
      render: renderProperties,
    },
    {
      key: "actions",
      label: t("resourceProfile.label.actions"),
      width: 180,
      render: (profile: ResourceProfile) => (
        <div className="flex gap-1">
          <Tooltip content={t("resourceProfile.action.testCriteria")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => handleTest(profile)}
            >
              <ExperimentOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("resourceProfile.action.editBasicInfo")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => {
                createPortal(ResourceProfileModal, {
                  profile,
                  onSaved: loadProfiles,
                  onUpdate: updateProfile,
                });
              }}
            >
              <EditOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("resourceProfile.action.duplicate")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => handleDuplicate(profile)}
            >
              <CopyOutlined />
            </Button>
          </Tooltip>
          <Popover
            placement="bottom-end"
            trigger={
              <Button isIconOnly size="sm" variant="light">
                <MoreOutlined />
              </Button>
            }
          >
            <Listbox
              aria-label="More actions"
              onAction={(key) => {
                switch (key) {
                  case "delete-enhancements":
                    createPortal(DeleteEnhancementsModal, {
                      profile,
                    });
                    break;
                  case "delete":
                    handleDelete(profile.id);
                    break;
                }
              }}
            >
              <ListboxItem
                key="delete-enhancements"
                startContent={<ClearOutlined />}
                color="warning"
              >
                {t("resourceProfile.action.deleteEnhancements")}
              </ListboxItem>
              <ListboxItem
                key="delete"
                startContent={<DeleteOutlined />}
                color="danger"
                className="text-danger"
              >
                {t("common.action.delete")}
              </ListboxItem>
            </Listbox>
          </Popover>
        </div>
      ),
    },
  ];

  return (
    <div className="p-2">
      <ResourceProfileGuideModal
        visible={showGuide}
        onComplete={completeGuide}
      />
      <div className="flex items-center justify-between gap-2 mb-4">
        <div className="flex items-center gap-2">
          <Button
            color="primary"
            size="sm"
            startContent={<PlusOutlined />}
            className="tour-add-profile"
            onPress={() => {
              createPortal(ResourceProfileModal, {
                existingNames: profiles.map((p) => p.name),
                onSaved: loadProfiles,
              });
            }}
          >
            {t("resourceProfile.action.addProfile")}
          </Button>
          <Tooltip content={t("resourceProfileGuide.viewAgain")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={resetGuide}
            >
              <QuestionCircleOutlined className="text-base" />
            </Button>
          </Tooltip>
          <Input
            size="sm"
            placeholder={t("resourceProfile.action.searchByName")}
            startContent={<SearchOutlined className="text-small" />}
            value={keyword}
            onValueChange={setKeyword}
            className="w-64"
          />
        </div>
        <div className="text-sm text-default-500">
          {t("resourceProfile.label.total")}: {filteredProfiles.length}
        </div>
      </div>

      <Table
        aria-label="Resource Profiles Table"
        isHeaderSticky
        removeWrapper
        className="tour-profile-table"
        classNames={{
          wrapper: "max-h-[calc(100vh-200px)]",
        }}
      >
        <TableHeader>
          {columns.map((column) => (
            <TableColumn
              key={column.key}
              width={column.width}
              data-tour={column.description ? `col-${column.key}` : undefined}
            >
              <div className="flex items-center gap-1">
                {column.label}
                {column.description && (
                  <Tooltip content={column.description}>
                    <InfoCircleOutlined className="text-xs opacity-50 cursor-help" />
                  </Tooltip>
                )}
              </div>
            </TableColumn>
          ))}
        </TableHeader>
        <TableBody isLoading={loading} loadingContent={<Spinner label={t("resourceProfile.status.loading")} />} emptyContent={t("resourceProfile.empty.noProfiles")}>
          {filteredProfiles.map((profile) => (
            <TableRow key={profile.id}>
              {columns.map((column) => (
                <TableCell key={column.key}>
                  {column.render ? column.render(profile) : String(profile[column.key as keyof ResourceProfile] ?? '')}
                </TableCell>
              ))}
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
};

ResourceProfilePage.displayName = "ResourceProfilePage";

export default ResourceProfilePage;
