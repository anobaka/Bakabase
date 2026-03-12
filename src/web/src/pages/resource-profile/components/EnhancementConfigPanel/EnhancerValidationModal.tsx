"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainEnhancerFullOptions } from "@/sdk/Api";
import type { ResourceSelectorValue } from "@/components/ResourceSelectorModal";

import React, { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { ExperimentOutlined } from "@ant-design/icons";

import { Button, Modal, Spinner, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceSelectorModal from "@/components/ResourceSelectorModal";
import ResourceEnhancementsModal from "@/components/Resource/components/ResourceEnhancementsModal";

type Props = {
  enhancerOptions: BakabaseAbstractionsModelsDomainEnhancerFullOptions[];
} & DestroyableProps;

const EnhancerValidationModal: React.FC<Props> = ({
  enhancerOptions,
  onDestroyed,
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [selectedResource, setSelectedResource] = useState<ResourceSelectorValue | null>(null);
  const [validating, setValidating] = useState(false);

  const handleSelectResource = useCallback(() => {
    createPortal(ResourceSelectorModal, {
      multiple: false,
      defaultSelectedIds: selectedResource ? [selectedResource.id] : [],
      onConfirm: (resources: ResourceSelectorValue[]) => {
        if (resources.length > 0) {
          setSelectedResource(resources[0]);
        }
      },
    });
  }, [createPortal, selectedResource]);

  const handleValidate = useCallback(async () => {
    if (!selectedResource) return;

    setValidating(true);
    try {
      const response = await BApi.resource.validateEnhancerConfiguration(
        selectedResource.id,
        enhancerOptions,
      );
      if (response.code === 0) {
        // Open ResourceEnhancementsModal to show the results
        createPortal(ResourceEnhancementsModal, {
          resourceId: selectedResource.id,
        });
      }
    } catch (err) {
      console.error("Validation failed:", err);
      toast.error(t<string>("enhancementConfig.validation.failed"));
    } finally {
      setValidating(false);
    }
  }, [selectedResource, enhancerOptions, createPortal, t]);

  return (
    <Modal
      defaultVisible
      size="lg"
      title={
        <div className="flex items-center gap-2">
          <ExperimentOutlined />
          {t<string>("enhancementConfig.validation.title")}
        </div>
      }
      onDestroyed={onDestroyed}
      footer={(
        <div className="flex justify-between items-center w-full">
          <div />
          <div className="flex gap-2">
            <Button
              color="default"
              variant="light"
              onPress={() => onDestroyed?.()}
            >
              {t<string>("common.action.close")}
            </Button>
            <Button
              color="primary"
              isDisabled={!selectedResource || validating}
              isLoading={validating}
              startContent={!validating ? <ExperimentOutlined /> : undefined}
              onPress={handleValidate}
            >
              {t<string>("enhancementConfig.validation.validate")}
            </Button>
          </div>
        </div>
      )}
    >
      <div className="flex flex-col gap-4">
        <div className="text-sm text-default-500">
          {t<string>("enhancementConfig.validation.description")}
        </div>

        {/* Resource selector */}
        <div className="flex items-center gap-3">
          {selectedResource && (
            <div className="flex items-center gap-2 px-3 py-2 bg-default-100 rounded-lg">
              <span className="text-sm font-medium">{selectedResource.displayName}</span>
            </div>
          )}
          <Button
            color={selectedResource ? "default" : "primary"}
            variant={selectedResource ? "bordered" : "solid"}
            onPress={handleSelectResource}
          >
            {t<string>(selectedResource
              ? "enhancementConfig.validation.changeResource"
              : "enhancementConfig.validation.selectResource"
            )}
          </Button>
        </div>

        {/* Loading state */}
        {validating && (
          <div className="flex items-center justify-center gap-2 py-4">
            <Spinner size="sm" />
            <span className="text-sm text-default-500">
              {t<string>("enhancementConfig.validation.running")}
            </span>
          </div>
        )}
      </div>
    </Modal>
  );
};

export default EnhancerValidationModal;
