"use client";

import { Modal, ModalBody, ModalContent, ModalFooter, ModalHeader } from "@heroui/react";

export interface ThirdPartyConfigModalProps {
  isOpen?: boolean;
  onClose?: () => void;
  title: React.ReactNode;
  children: React.ReactNode;
  /** @default "5xl" */
  size?: "2xl" | "5xl";
  footer?: React.ReactNode;
}

/**
 * Modal shell for third-party config: use with a *ConfigPanel as children (same panel as on the third-party settings page).
 */
export default function ThirdPartyConfigModal({
  isOpen = true,
  onClose,
  title,
  children,
  size = "5xl",
  footer,
}: ThirdPartyConfigModalProps) {
  return (
    <Modal
      isOpen={isOpen}
      scrollBehavior="inside"
      shouldBlockScroll={false}
      size={size}
      onClose={onClose}
    >
      <ModalContent>
        <ModalHeader>{title}</ModalHeader>
        <ModalBody className="pb-6">{children}</ModalBody>
        {footer != null ? <ModalFooter>{footer}</ModalFooter> : null}
      </ModalContent>
    </Modal>
  );
}
