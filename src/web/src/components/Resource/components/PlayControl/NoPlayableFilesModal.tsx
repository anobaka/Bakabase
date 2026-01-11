"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseServiceModelsViewResourceProfileViewModel } from "@/sdk/Api";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { InfoCircleOutlined, RightOutlined, SettingOutlined } from "@ant-design/icons";

import { Button, Chip, Modal, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type Props = {
  resourceId: number;
} & DestroyableProps;

const NoPlayableFilesModal = ({ resourceId, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [profiles, setProfiles] = useState<BakabaseServiceModelsViewResourceProfileViewModel[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const loadProfiles = async () => {
      setLoading(true);
      try {
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

  const handleNavigateToProfile = (profileId?: number) => {
    onDestroyed?.();
    if (profileId) {
      navigate(`/resource-profile?highlight=${profileId}`);
    } else {
      navigate("/resource-profile");
    }
  };

  return (
    <Modal
      defaultVisible
      size="lg"
      title={t("resource.playControl.noPlayableFiles.title")}
      onDestroyed={onDestroyed}
      footer={{ actions: ["ok"] }}
    >
      <div className="flex flex-col gap-6">
        <div>
          <div className="text-sm mb-2">
            {t("resource.playControl.noPlayableFiles.description")}
          </div>
        </div>

        <div>
          <div className="font-medium mb-2">{t("resource.playControl.noPlayableFiles.tips.title")}</div>
          <ul className="list-disc pl-5 text-sm space-y-1">
            <li>{t("resource.playControl.noPlayableFiles.tips.noFile")}</li>
            <li>{t("resource.playControl.noPlayableFiles.tips.extension")}</li>
          </ul>
        </div>

        <div>
          <div className="font-medium mb-2">{t("resource.playControl.noPlayableFiles.action.title")}</div>
          <div className="text-sm mb-3">
            <InfoCircleOutlined className="mr-1" />
            {t("resource.playControl.noPlayableFiles.action.description")}
          </div>

          {loading ? (
            <div className="flex items-center gap-2">
              <Spinner size="sm" />
              <span className="text-sm text-default-500">{t("common.state.loading")}</span>
            </div>
          ) : profiles.length > 0 ? (
            <div className="flex flex-col gap-2">
              <div className="text-sm text-default-500 mb-1">
                {t("resource.playControl.noPlayableFiles.matchingProfiles")}
              </div>
              <div className="flex flex-wrap gap-2">
                {profiles.map((profile) => (
                  <Chip
                    key={profile.id}
                    className="cursor-pointer"
                    color="primary"
                    endContent={<RightOutlined className="text-xs" />}
                    size="sm"
                    variant="flat"
                    onClick={() => handleNavigateToProfile(profile.id)}
                  >
                    {profile.name}
                  </Chip>
                ))}
              </div>
            </div>
          ) : (
            <div className="text-sm text-default-500">
              {t("resource.playControl.noPlayableFiles.noMatchingProfiles")}
            </div>
          )}

          <div className="mt-4">
            <Button
              color="primary"
              size="sm"
              variant="flat"
              startContent={<SettingOutlined />}
              onPress={() => handleNavigateToProfile()}
            >
              {t("resource.playControl.noPlayableFiles.action.settings")}
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  );
};

NoPlayableFilesModal.displayName = "NoPlayableFilesModal";

export default NoPlayableFilesModal;
