"use client";

import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type { DestroyableProps } from "@/components/bakaui/types";
import type {
  BakabaseAbstractionsModelsDomainEnhancerFullOptions,
  BakabaseServiceModelsViewResourceProfileViewModel,
} from "@/sdk/Api";

import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined } from "@ant-design/icons";

import { Button, Checkbox, Modal, Select, Chip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type ResourceProfile = BakabaseServiceModelsViewResourceProfileViewModel;

type Props = {
  profile: ResourceProfile;
  onDeleted?: () => any;
} & DestroyableProps;

const DeleteEnhancementsModal = ({ profile, onDeleted, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [enhancerDescriptors, setEnhancerDescriptors] = useState<EnhancerDescriptor[]>([]);
  const [selectedEnhancerId, setSelectedEnhancerId] = useState<string>("all");
  const [deleteEmptyOnly, setDeleteEmptyOnly] = useState(false);
  const [isDeleting, setIsDeleting] = useState(false);

  const enhancerOptions = profile.enhancerOptions?.enhancers ?? [];

  useEffect(() => {
    BApi.enhancer.getAllEnhancerDescriptors().then((r) => {
      setEnhancerDescriptors((r.data || []) as EnhancerDescriptor[]);
    });
  }, []);

  const getEnhancerName = (enhancerId: number): string => {
    const enhancer = enhancerDescriptors.find((e) => e.id === enhancerId);
    return enhancer?.name ?? `Enhancer ${enhancerId}`;
  };

  const selectDataSource = useMemo(() => {
    const options = [
      { value: "all", label: t("All enhancers") },
    ];

    enhancerOptions.forEach((opt: BakabaseAbstractionsModelsDomainEnhancerFullOptions) => {
      options.push({
        value: String(opt.enhancerId),
        label: getEnhancerName(opt.enhancerId!),
      });
    });

    return options;
  }, [enhancerOptions, enhancerDescriptors, t]);

  const handleDelete = async () => {
    setIsDeleting(true);
    try {
      // Temporary: Use fetch directly until SDK is regenerated
      const baseUrl = "";  // Uses relative URL which works with proxy
      if (selectedEnhancerId === "all") {
        // Delete all enhancements for this profile
        await fetch(
          `${baseUrl}/resource-profile/${profile.id}/enhancement?deleteEmptyOnly=${deleteEmptyOnly}`,
          { method: "DELETE" }
        );
      } else {
        // Delete enhancements for specific enhancer
        await fetch(
          `${baseUrl}/resource-profile/${profile.id}/enhancer/${selectedEnhancerId}/enhancement?deleteEmptyOnly=${deleteEmptyOnly}`,
          { method: "DELETE" }
        );
      }
      onDeleted?.();
      onDestroyed?.();
    } catch (e) {
      console.error("Failed to delete enhancements", e);
    } finally {
      setIsDeleting(false);
    }
  };

  return (
    <Modal
      defaultVisible
      size="md"
      title={t<string>("Delete Enhancements")}
      onDestroyed={onDestroyed}
      footer={(
        <div className="flex justify-end gap-2">
          <Button variant="light" onPress={() => onDestroyed?.()}>
            {t<string>("Cancel")}
          </Button>
          <Button
            color="danger"
            isLoading={isDeleting}
            startContent={<DeleteOutlined />}
            onPress={handleDelete}
          >
            {t<string>("Delete")}
          </Button>
        </div>
      )}
    >
      <div className="flex flex-col gap-4">
        <div className="text-sm">
          {t("Delete enhancements for resources matching profile")}:{" "}
          <Chip color="primary" size="sm" variant="flat">{profile.name}</Chip>
        </div>

        {enhancerOptions.length > 0 ? (
          <div>
            <label className="text-sm font-medium mb-2 block">{t("Select enhancer")}</label>
            <Select
              selectedKeys={[selectedEnhancerId]}
              dataSource={selectDataSource}
              onSelectionChange={(keys) => {
                const key = Array.from(keys)[0] as string;
                setSelectedEnhancerId(key);
              }}
            />
          </div>
        ) : (
          <div className="text-sm text-warning">
            {t("This profile has no enhancers configured. All enhancements for matching resources will be deleted.")}
          </div>
        )}

        <Checkbox
          isSelected={deleteEmptyOnly}
          onValueChange={setDeleteEmptyOnly}
        >
          <div className="flex flex-col">
            <span>{t("Delete empty records only")}</span>
            <span className="text-xs text-default-400">
              {t("Only delete enhancement records that have no actual enhancement data")}
            </span>
          </div>
        </Checkbox>

        <div className="text-sm text-danger">
          {t("Warning: This action cannot be undone.")}
        </div>
      </div>
    </Modal>
  );
};

DeleteEnhancementsModal.displayName = "DeleteEnhancementsModal";

export default DeleteEnhancementsModal;
