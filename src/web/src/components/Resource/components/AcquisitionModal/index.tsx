"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Resource } from "@/core/models/Resource";

import React from "react";
import { useTranslation } from "react-i18next";

import AcquisitionPanel from "../AcquisitionPanel";

import { Modal } from "@/components/bakaui";

interface Props extends DestroyableProps {
  resource: Resource;
  onChanged?: () => void;
}

/**
 * The card's entry point into "how do I get this". Same panel the detail modal shows, so the two
 * cannot drift apart.
 */
const AcquisitionModal: React.FC<Props> = ({ resource, onChanged, onDestroyed }) => {
  const { t } = useTranslation();

  return (
    <Modal
      defaultVisible
      footer={{ actions: ["cancel"] }}
      size="lg"
      title={t<string>("acquisition.modal.title", { name: resource.displayName ?? resource.id })}
      onDestroyed={onDestroyed}
    >
      <AcquisitionPanel resource={resource} onChanged={onChanged} />
    </Modal>
  );
};

AcquisitionModal.displayName = "AcquisitionModal";

export default AcquisitionModal;
