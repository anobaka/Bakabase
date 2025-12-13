"use client";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { SearchOutlined, PlusOutlined, EditOutlined, DeleteOutlined, CopyOutlined, ExperimentOutlined } from "@ant-design/icons";

import BApi from "@/sdk/BApi";
import { Button, Input, Table, TableHeader, TableBody, TableColumn, TableRow, TableCell, Chip, Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceProfileModal from "./components/ResourceProfileModal";
import ResourceProfileTestModal from "./components/ResourceProfileTestModal";

interface SearchCriteria {
  mediaLibraryIds?: number[];
  propertyFilters?: PropertyFilter[];
  pathPattern?: string;
  tagFilter?: any;
}

interface PropertyFilter {
  pool: number;
  propertyId: number;
  operation: number;
  value?: any;
}

interface ResourceProfile {
  id: number;
  name: string;
  searchCriteria: SearchCriteria;
  nameTemplate?: string;
  enhancerSettings?: any;
  playableFileSettings?: any;
  playerSettings?: any;
  priority: number;
  createDt?: string;
  updateDt?: string;
}

const ResourceProfilePage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [profiles, setProfiles] = useState<ResourceProfile[]>([]);
  const [keyword, setKeyword] = useState("");
  const [loading, setLoading] = useState(false);

  const loadProfiles = async () => {
    setLoading(true);
    try {
      // @ts-ignore - API will be available after SDK regeneration
      const rsp = await BApi.resourceProfile.getAllResourceProfiles();
      // @ts-ignore
      setProfiles((rsp.data || []).sort((a, b) => b.priority - a.priority));
    } catch (e) {
      console.error("Failed to load resource profiles", e);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadProfiles();
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
      // @ts-ignore
      await BApi.resourceProfile.deleteResourceProfile(id);
      loadProfiles();
    } catch (e) {
      console.error("Failed to delete resource profile", e);
    }
  };

  const handleDuplicate = async (profile: ResourceProfile) => {
    try {
      const newProfile = {
        ...profile,
        id: undefined,
        name: `${profile.name} (Copy)`,
      };
      // @ts-ignore
      await BApi.resourceProfile.addResourceProfile(newProfile);
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

  const getCriteriaDescription = (criteria: SearchCriteria): string => {
    const parts: string[] = [];
    if (criteria.mediaLibraryIds?.length) {
      parts.push(`${criteria.mediaLibraryIds.length} ${t("libraries")}`);
    }
    if (criteria.propertyFilters?.length) {
      parts.push(`${criteria.propertyFilters.length} ${t("filters")}`);
    }
    if (criteria.pathPattern) {
      parts.push(t("path pattern"));
    }
    if (criteria.tagFilter) {
      parts.push(t("tag filter"));
    }
    return parts.length > 0 ? parts.join(", ") : t("No criteria");
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
      width: 200,
    },
    {
      key: "searchCriteria",
      label: t("Search Criteria"),
      render: (profile: ResourceProfile) => (
        <span className="text-sm text-default-500">
          {getCriteriaDescription(profile.searchCriteria)}
        </span>
      ),
    },
    {
      key: "nameTemplate",
      label: t("Name Template"),
      width: 150,
      render: (profile: ResourceProfile) => (
        <span className="text-sm font-mono">
          {profile.nameTemplate || "-"}
        </span>
      ),
    },
    {
      key: "settings",
      label: t("Settings"),
      width: 180,
      render: (profile: ResourceProfile) => (
        <div className="flex gap-1 flex-wrap">
          {profile.enhancerSettings && (
            <Chip size="sm" variant="flat" color="primary">
              {t("Enhancers")}
            </Chip>
          )}
          {profile.playableFileSettings && (
            <Chip size="sm" variant="flat" color="secondary">
              {t("Playable")}
            </Chip>
          )}
          {profile.playerSettings && (
            <Chip size="sm" variant="flat" color="success">
              {t("Players")}
            </Chip>
          )}
        </div>
      ),
    },
    {
      key: "actions",
      label: t("Actions"),
      width: 180,
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
          <Tooltip content={t("Edit")}>
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
