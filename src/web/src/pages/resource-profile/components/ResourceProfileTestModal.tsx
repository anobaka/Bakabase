"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainResourceProfile } from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { LoadingOutlined, FileOutlined } from "@ant-design/icons";

import { Modal, Input, Chip, Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type ResourceProfile = BakabaseAbstractionsModelsDomainResourceProfile;

interface Resource {
  id: number;
  path: string;
  name?: string;
  displayName?: string;
}

type Props = {
  profile: ResourceProfile;
} & DestroyableProps;

const ResourceProfileTestModal = ({ profile, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [matchingResources, setMatchingResources] = useState<Resource[]>([]);
  const [matchCount, setMatchCount] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [keyword, setKeyword] = useState("");
  const [limit, setLimit] = useState(50);

  useEffect(() => {
    const loadMatchingResources = async () => {
      setLoading(true);
      try {
        // Use resource search API to test the profile's search criteria
        const searchParams = {
          group: profile.search?.group,
          tags: profile.search?.tags,
          pageSize: limit,
          page: 1,
        };

        const rsp = await BApi.resource.searchResources(searchParams as any);
        const resources = (rsp.data || []) as Resource[];
        setMatchingResources(resources);
        setMatchCount(rsp.totalCount ?? resources.length);
      } catch (e) {
        console.error("Failed to test criteria", e);
      } finally {
        setLoading(false);
      }
    };

    loadMatchingResources();
  }, [profile, limit]);

  const filteredResources = matchingResources.filter(
    (r) =>
      keyword === "" ||
      r.path?.toLowerCase().includes(keyword.toLowerCase()) ||
      r.name?.toLowerCase().includes(keyword.toLowerCase()) || 
      r.displayName?.toLowerCase().includes(keyword.toLowerCase())
  );

  return (
    <Modal
      defaultVisible
      size="3xl"
      title={
        <div className="flex items-center gap-2">
          {t("Test Search Criteria")}
          <Chip size="sm" variant="flat" color="primary">
            {profile.name}
          </Chip>
        </div>
      }
      footer={false}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-4">
        <div className="flex items-center justify-between">
          <div className="text-sm">
            {matchCount !== null && (
              <span>
                {t("Total matching resources")}: <strong>{matchCount}</strong>
              </span>
            )}
          </div>
          <div className="flex items-center gap-2">
            <span className="text-sm text-default-500">{t("Show limit")}:</span>
            <Button
              size="sm"
              variant={limit === 50 ? "solid" : "flat"}
              onPress={() => setLimit(50)}
            >
              50
            </Button>
            <Button
              size="sm"
              variant={limit === 100 ? "solid" : "flat"}
              onPress={() => setLimit(100)}
            >
              100
            </Button>
            <Button
              size="sm"
              variant={limit === 200 ? "solid" : "flat"}
              onPress={() => setLimit(200)}
            >
              200
            </Button>
          </div>
        </div>

        <Input
          size="sm"
          placeholder={t("Filter results...")}
          value={keyword}
          onValueChange={setKeyword}
          className="max-w-sm"
        />

        {loading ? (
          <div className="flex items-center justify-center py-8">
            <LoadingOutlined className="text-2xl mr-2" />
            <span>{t("Testing criteria...")}</span>
          </div>
        ) : filteredResources.length === 0 ? (
          <div className="text-center text-default-400 py-8">
            {keyword ? t("No resources match your filter") : t("No resources matched by this criteria")}
          </div>
        ) : (
          <div className="max-h-[400px] overflow-y-auto border-divider border rounded-lg">
            {filteredResources.map((resource) => (
              <div
                key={resource.id}
                className="flex items-center gap-2 px-3 py-2 border-b border-b-divider last:border-b-0 hover:bg-default-100"
              >
                <FileOutlined className="text-default-400" />
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-medium truncate" title={resource.name}>
                    {resource.displayName || t("Unnamed")}
                  </div>
                  <div className="text-xs text-default-400 font-mono truncate" title={resource.path}>
                    {resource.path}
                  </div>
                </div>
                <Chip size="sm" variant="flat">
                  #{resource.id}
                </Chip>
              </div>
            ))}
          </div>
        )}

        {!loading && matchCount !== null && matchCount > limit && (
          <div className="text-center text-sm text-default-400">
            {t("Showing {{shown}} of {{total}} matching resources", {
              shown: Math.min(limit, matchCount),
              total: matchCount,
            })}
          </div>
        )}
      </div>
    </Modal>
  );
};

ResourceProfileTestModal.displayName = "ResourceProfileTestModal";

export default ResourceProfileTestModal;
