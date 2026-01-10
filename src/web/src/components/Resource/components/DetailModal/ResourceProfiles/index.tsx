"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { ProfileOutlined, RightOutlined } from "@ant-design/icons";
import { useNavigate } from "react-router-dom";

import type { BakabaseServiceModelsViewResourceProfileViewModel } from "@/sdk/Api";

import { Chip, Spinner, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

interface Props {
  resourceId: number;
  onClose?: () => void;
  /** Compact mode: only show chips without label */
  compact?: boolean;
}

const ResourceProfiles = ({ resourceId, onClose, compact = false }: Props) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [profiles, setProfiles] = useState<
    BakabaseServiceModelsViewResourceProfileViewModel[]
  >([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadProfiles = async () => {
      setLoading(true);
      try {
        // @ts-ignore - API will be available after SDK regeneration
        const response = await BApi.resourceProfile.getMatchingProfilesForResource(resourceId);
        setProfiles(response.data || []);
      } catch (error) {
        console.error("Failed to load matching profiles:", error);
        setProfiles([]);
      } finally {
        setLoading(false);
      }
    };

    loadProfiles();
  }, [resourceId]);

  const handleNavigateToProfile = (profileId: number) => {
    onClose?.();
    navigate(`/resource-profile?highlight=${profileId}`);
  };

  if (loading) {
    return (
      <div className="flex items-center gap-2">
        {!compact && (
          <div className="flex items-center gap-1 text-sm text-default-500 shrink-0">
            <ProfileOutlined />
            <span>{t("resource.label.resourceProfiles")}</span>
          </div>
        )}
        <Spinner size="sm" />
      </div>
    );
  }

  // In compact mode with no profiles, don't render anything
  if (compact && profiles.length === 0) {
    return null;
  }

  return (
    <div className="flex items-start gap-2">
      {!compact && (
        <div className="flex items-center gap-1 text-sm text-default-500 shrink-0 pt-0.5">
          <ProfileOutlined />
          <span>{t("resource.label.resourceProfiles")}</span>
        </div>
      )}
      <div className="flex flex-wrap items-center gap-1 flex-1">
        {profiles.length === 0 ? (
          <span className="text-sm text-default-400">
            {t("common.label.none")}
          </span>
        ) : (
          profiles.map((profile) => (
            <Tooltip
              key={profile.id}
              content={t("resource.tip.clickToViewEditProfile")}
            >
              <Chip
                className="cursor-pointer"
                color="primary"
                endContent={<RightOutlined className="text-xs" />}
                size="sm"
                variant="flat"
                onClick={() => handleNavigateToProfile(profile.id!)}
              >
                {profile.name}
              </Chip>
            </Tooltip>
          ))
        )}
      </div>
    </div>
  );
};

ResourceProfiles.displayName = "ResourceProfiles";

export default ResourceProfiles;
