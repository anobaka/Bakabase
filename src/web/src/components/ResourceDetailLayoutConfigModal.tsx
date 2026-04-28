"use client";

import type { DetailLayoutConfig } from "@/components/ResourceDetailLayoutEditor";
import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Modal, toast } from "@/components/bakaui";
import {
  ResourceDetailLayoutEditor,
  normalizeLayoutConfig,
  renderSamplePlaceholder,
} from "@/components/ResourceDetailLayoutEditor";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";

type Props = DestroyableProps;

const LayoutConfigModal = ({ onDestroyed }: Props) => {
  const { t } = useTranslation();
  const uiOptionsStore = useUiOptionsStore();
  const savedLayout = uiOptionsStore.data?.resourceDetailLayout;

  const initialConfig = useMemo<DetailLayoutConfig>(
    () => normalizeLayoutConfig(savedLayout),
    // Freeze the initial config at open time. Subsequent store updates (from
    // optimistic saves or signalr pushes) shouldn't clobber the user's draft.

    [],
  );

  const [config, setConfig] = useState<DetailLayoutConfig>(initialConfig);
  const [saving, setSaving] = useState(false);
  const [resetting, setResetting] = useState(false);
  const [visible, setVisible] = useState(true);

  const handleSave = async () => {
    setSaving(true);
    try {
      const rsp = await BApi.options.patchUiOptions({
        resourceDetailLayout: config as any,
      });

      if (!rsp.code) {
        toast.success(t<string>("common.success.saved"));
        setVisible(false);
      }
    } finally {
      setSaving(false);
    }
  };

  const handleReset = async () => {
    setResetting(true);
    try {
      const rsp = await BApi.options.resetResourceDetailLayout();

      if (!rsp.code) {
        toast.success(t<string>("resource.detailLayout.resetToast"));
        setVisible(false);
      }
    } finally {
      setResetting(false);
    }
  };

  return (
    <Modal
      footer={
        <div className="flex items-center w-full gap-2">
          <Button color="danger" isLoading={resetting} variant="light" onPress={handleReset}>
            {t<string>("resource.detailLayout.reset")}
          </Button>
          <div className="flex-1" />
          <Button variant="light" onPress={() => setVisible(false)}>
            {t<string>("common.action.cancel")}
          </Button>
          <Button color="primary" isLoading={saving} onPress={handleSave}>
            {t<string>("common.action.save")}
          </Button>
        </div>
      }
      size="7xl"
      title={t<string>("resource.detailLayout.title")}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={onDestroyed}
    >
      <div className="text-xs text-default-500 mb-2">
        {t<string>("resource.detailLayout.description")}
      </div>
      <ResourceDetailLayoutEditor
        configDescription={t<string>("resource.detailLayout.sampleDataNote")}
        defaultValue={initialConfig}
        renderSection={renderSamplePlaceholder}
        value={config}
        onChange={setConfig}
      />
    </Modal>
  );
};

LayoutConfigModal.displayName = "ResourceDetailLayoutConfigModal";

export default LayoutConfigModal;
