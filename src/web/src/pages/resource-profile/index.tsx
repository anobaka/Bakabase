"use client";

import { useTranslation } from "react-i18next";
import { useEffect, useMemo, useState } from "react";
import { SearchOutlined, PlusOutlined, EditOutlined, DeleteOutlined, CopyOutlined, ExperimentOutlined } from "@ant-design/icons";
import { BsController } from "react-icons/bs";

import BApi from "@/sdk/BApi";
import type {
  BakabaseAbstractionsModelsDomainResourceProfile,
  BakabaseAbstractionsModelsDomainResourceSearch,
  BakabaseAbstractionsModelsDomainEnhancerFullOptions,
  BakabaseAbstractionsModelsDomainResourceProfilePlayableFileOptions,
  BakabaseAbstractionsModelsDomainMediaLibraryPlayer,
} from "@/sdk/Api";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { IProperty } from "@/components/Property/models";
import { Button, Input, Table, TableHeader, TableBody, TableColumn, TableRow, TableCell, Chip, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceProfileModal from "./components/ResourceProfileModal";
import ResourceProfileTestModal from "./components/ResourceProfileTestModal";
import DisplayNameTemplateEditorModal from "./components/DisplayNameTemplateEditorModal";
import EnhancerSelectorModal from "./components/EnhancerSelectorModal";
import PlayableFileSelectorModal from "./components/PlayableFileSelectorModal";
import PlayerSelectorModal from "./components/PlayerSelectorModal";
import { FilterGroup, FilterProvider, createDefaultFilterConfig, toSearchInputModel, toFilterGroupInputModel } from "@/components/ResourceFilter";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { ResourceSearchInputModel } from "@/components/ResourceFilter/utils/toInputModel";
import { PropertyPool, resourceTags, builtinPropertyForDisplayNames } from "@/sdk/constants";
import { splitPathIntoSegments } from "@/components/utils";
import BriefEnhancer from "@/components/Chips/Enhancer/BriefEnhancer";

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

type ResourceProfile = BakabaseAbstractionsModelsDomainResourceProfile;

/**
 * Convert ResourceProfile to API input model format
 * This ensures search.group.filters[].dbValue is serialized string
 */
const toProfileInputModel = (profile: Partial<ResourceProfile>) => {
  return {
    name: profile.name ?? "",
    search: profile.search ? toSearchInputModel(profile.search) : undefined,
    nameTemplate: profile.nameTemplate,
    enhancerOptions: profile.enhancerOptions,
    playableFileOptions: profile.playableFileOptions,
    playerOptions: profile.playerOptions,
    priority: profile.priority ?? 0,
  };
};

const ResourceProfilePage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const filterConfig = useMemo(() => createDefaultFilterConfig(createPortal), [createPortal]);
  const [profiles, setProfiles] = useState<ResourceProfile[]>([]);
  const [keyword, setKeyword] = useState("");
  const [loading, setLoading] = useState(false);
  const [properties, setProperties] = useState<IProperty[]>([]);
  const [enhancerDescriptors, setEnhancerDescriptors] = useState<EnhancerDescriptor[]>([]);

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
    if (!window.confirm(t("Are you sure you want to delete this resource profile?"))) {
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

  const updateProfile = async (profile: ResourceProfile, updates: Partial<ResourceProfile>) => {
    try {
      const merged = { ...profile, ...updates };
      const inputModel = toProfileInputModel(merged);
      await BApi.resourceProfile.updateResourceProfile(profile.id, inputModel as any);
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
      t(`BuiltinPropertyForDisplayName.${v.label}`)
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
      createPortal(EnhancerSelectorModal, {
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
        <Tooltip content={t("Click to configure enhancers")}>
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

  const columns = [
    {
      key: "priority",
      label: t("Priority"),
      width: 80,
    },
    {
      key: "name",
      label: t("Name"),
      width: 150,
    },
    {
      key: "search",
      label: t("Search Criteria"),
      render: (profile: ResourceProfile) => {
        const group = profile.search?.group ?? { combinator: 1, disabled: false };
        return (
          <div className="flex flex-col gap-1">
            <FilterProvider config={filterConfig}>
              <FilterGroup
                isRoot
                group={group as SearchFilterGroup}
                onChange={(newGroup) => {
                  updateProfile(profile, {
                    search: {
                      ...profile.search,
                      group: newGroup as BakabaseAbstractionsModelsDomainResourceSearch["group"],
                    },
                  });
                }}
              />
            </FilterProvider>
            {profile.search?.tags && profile.search.tags.length > 0 && (
              <div className="flex flex-wrap gap-1">
                {profile.search.tags.map((tag) => (
                  <Chip key={tag} size="sm" variant="flat" color="warning">
                    {t(`ResourceTag.${resourceTags.find((rt) => rt.value === tag)?.label}`)}
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
      label: t("Name Template"),
      render: renderNameTemplate,
    },
    {
      key: "enhancers",
      label: t("Enhancers"),
      width: 130,
      render: renderEnhancers,
    },
    {
      key: "playableFiles",
      label: t("Playable Files"),
      width: 130,
      render: renderPlayableFiles,
    },
    {
      key: "players",
      label: t("Players"),
      width: 200,
      render: renderPlayers,
    },
    {
      key: "actions",
      label: t("Actions"),
      width: 150,
      render: (profile: ResourceProfile) => (
        <div className="flex gap-1">
          <Tooltip content={t("Test criteria")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => handleTest(profile)}
            >
              <ExperimentOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("Edit basic info")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => {
                createPortal(ResourceProfileModal, {
                  profile,
                  onSaved: loadProfiles,
                });
              }}
            >
              <EditOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("Duplicate")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => handleDuplicate(profile)}
            >
              <CopyOutlined />
            </Button>
          </Tooltip>
          <Tooltip content={t("Delete")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              color="danger"
              onPress={() => handleDelete(profile.id)}
            >
              <DeleteOutlined />
            </Button>
          </Tooltip>
        </div>
      ),
    },
  ];

  return (
    <div>
      <div className="flex items-center justify-between gap-2 mb-4">
        <div className="flex items-center gap-2">
          <Button
            color="primary"
            size="sm"
            startContent={<PlusOutlined />}
            onPress={() => {
              createPortal(ResourceProfileModal, {
                existingNames: profiles.map((p) => p.name),
                onSaved: loadProfiles,
              });
            }}
          >
            {t("Add Resource Profile")}
          </Button>
          <Input
            size="sm"
            placeholder={t("Search by name")}
            startContent={<SearchOutlined className="text-small" />}
            value={keyword}
            onValueChange={setKeyword}
            className="w-64"
          />
        </div>
        <div className="text-sm text-default-500">
          {t("Total")}: {filteredProfiles.length}
        </div>
      </div>

      <Table
        aria-label="Resource Profiles Table"
        isHeaderSticky
        removeWrapper
        classNames={{
          wrapper: "max-h-[calc(100vh-200px)]",
        }}
      >
        <TableHeader>
          {columns.map((column) => (
            <TableColumn
              key={column.key}
              width={column.width}
            >
              {column.label}
            </TableColumn>
          ))}
        </TableHeader>
        <TableBody isLoading={loading} emptyContent={t("No profiles found")}>
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
