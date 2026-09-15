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

import { checkProfileResponse } from "../profileUtils";

import { useProfileModalSave } from "./useProfileModalSave";

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
    const options = [{ value: "all", label: t("resourceProfile.label.allEnhancers") }];

    enhancerOptions.forEach((opt: BakabaseAbstractionsModelsDomainEnhancerFullOptions) => {
      options.push({
        value: String(opt.enhancerId),
        label: getEnhancerName(opt.enhancerId!),
      });
    });

    return options;
  }, [enhancerOptions, enhancerDescriptors, t]);

  const editor = useProfileModalSave(async () => {
    const query = { deleteEmptyOnly };
    const response =
      selectedEnhancerId === "all"
        ? await BApi.resourceProfile.deleteEnhancementsByResourceProfile(profile.id, query)
        : await BApi.resourceProfile.deleteEnhancementsByResourceProfileAndEnhancer(
            profile.id,
            Number(selectedEnhancerId),
            query,
          );

    checkProfileResponse(response, t("resourceProfile.error.save"));
    await onDeleted?.();
  }, t("resourceProfile.editor.saveFailed"));

  return (
    <Modal
      footer={
        <div className="flex justify-end gap-2">
          <Button isDisabled={editor.saving} variant="light" onPress={editor.close}>
            {t<string>("common.action.cancel")}
          </Button>
          <Button
            color="danger"
            isLoading={editor.saving}
            startContent={<DeleteOutlined />}
            onPress={() => editor.save(undefined)}
          >
            {t<string>("common.action.delete")}
          </Button>
        </div>
      }
      hideCloseButton={editor.saving}
      isDismissable={!editor.saving}
      isKeyboardDismissDisabled={editor.saving}
      size="md"
      title={t<string>("resourceProfile.modal.deleteEnhancementsTitle")}
      visible={editor.visible}
      onClose={editor.close}
      onDestroyed={onDestroyed}
    >
      {editor.error && (
        <p className="rounded-lg bg-danger-50 p-3 text-sm text-danger" role="alert">
          {editor.error}
        </p>
      )}
      <div className="flex flex-col gap-4" {...{ inert: editor.saving ? "" : undefined }}>
        <div className="text-sm">
          {t("resourceProfile.tip.deleteEnhancementsForProfile")}:{" "}
          <Chip color="primary" size="sm" variant="flat">
            {profile.name}
          </Chip>
        </div>

        {enhancerOptions.length > 0 ? (
          <div>
            <label className="text-sm font-medium mb-2 block">
              {t("resourceProfile.label.selectEnhancer")}
            </label>
            <Select
              dataSource={selectDataSource}
              selectedKeys={[selectedEnhancerId]}
              onSelectionChange={(keys) => {
                const key = Array.from(keys)[0] as string;

                setSelectedEnhancerId(key);
              }}
            />
          </div>
        ) : (
          <div className="text-sm text-warning">
            {t("resourceProfile.tip.noEnhancersConfigured")}
          </div>
        )}

        <Checkbox isSelected={deleteEmptyOnly} onValueChange={setDeleteEmptyOnly}>
          <div className="flex flex-col">
            <span>{t("resourceProfile.label.deleteEmptyRecordsOnly")}</span>
            <span className="text-xs text-default-400">
              {t("resourceProfile.tip.deleteEmptyRecordsOnlyDescription")}
            </span>
          </div>
        </Checkbox>

        <div className="text-sm text-danger">{t("resourceProfile.warning.cannotBeUndone")}</div>
      </div>
    </Modal>
  );
};

DeleteEnhancementsModal.displayName = "DeleteEnhancementsModal";

export default DeleteEnhancementsModal;
