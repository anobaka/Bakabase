"use client";

import type { HelpTarget, HelpTopicId } from "./types";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { getHelpTopic, helpTopics } from "./topics";

import { Button, Modal } from "@/components/bakaui";

export interface HelpCenterModalProps extends HelpTarget {
  visible: boolean;
  onClose: () => void;
  /** First-run mode: opened automatically for new users, closes via a single primary action. */
  firstRun?: boolean;
}

/**
 * The help center host. Content is rendered by topic components so the same
 * content can later be hosted by a standalone page as well as this modal.
 */
const HelpCenterModal = ({
  visible,
  onClose,
  topic,
  section,
  concept,
  firstRun,
}: HelpCenterModalProps) => {
  const { t } = useTranslation();
  const initialTopicId = topic ?? helpTopics[0]!.id;
  const [activeTopicId, setActiveTopicId] = useState<HelpTopicId>(initialTopicId);

  const activeTopic = getHelpTopic(activeTopicId);
  const { Content } = activeTopic;
  const showTopicNav = helpTopics.length > 1;

  return (
    <Modal
      footer={
        firstRun ? (
          <div className="flex justify-end w-full">
            <Button color="primary" onPress={onClose}>
              {t("helpCenter.action.getStarted")}
            </Button>
          </div>
        ) : (
          false
        )
      }
      isDismissable={!firstRun}
      size="5xl"
      title={
        <div className="flex items-center gap-2">
          {activeTopic.icon}
          <span>
            {t("helpCenter.title")} · {t(activeTopic.titleKey)}
          </span>
        </div>
      }
      visible={visible}
      onClose={onClose}
    >
      <div className="flex gap-3 min-h-0">
        {showTopicNav && (
          <div className="flex flex-col gap-1 w-40 shrink-0">
            {helpTopics.map((topicDef) => (
              <Button
                key={topicDef.id}
                className="justify-start"
                color={topicDef.id === activeTopicId ? "primary" : "default"}
                size="sm"
                startContent={topicDef.icon}
                variant={topicDef.id === activeTopicId ? "flat" : "light"}
                onPress={() => setActiveTopicId(topicDef.id)}
              >
                {t(topicDef.titleKey)}
              </Button>
            ))}
          </div>
        )}
        <div className="flex-1 min-w-0 max-h-[70vh] overflow-y-auto overflow-x-hidden pr-1 pb-2">
          <Content
            concept={activeTopicId === initialTopicId ? concept : undefined}
            firstRun={firstRun}
            section={activeTopicId === initialTopicId ? section : undefined}
          />
        </div>
      </div>
    </Modal>
  );
};

HelpCenterModal.displayName = "HelpCenterModal";

export default HelpCenterModal;
