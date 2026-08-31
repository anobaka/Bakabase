"use client";

import type { HelpTarget, HelpTopicId } from "./types";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { helpTopics } from "./topics";

import { Button, Modal } from "@/components/bakaui";

export interface HelpCenterModalProps extends HelpTarget {
  visible: boolean;
  onClose: () => void;
  /** First-run mode: opened automatically for new users, closes via a single primary action. */
  firstRun?: boolean;
}

interface ActiveEntry {
  topicId: HelpTopicId;
  /** Undefined = the topic's overview entry is selected. */
  conceptId?: string;
}

/**
 * The help center host: a left navigation of topics and their concepts, and a
 * right pane with the selected entry's content. Content is rendered by topic
 * components so the same content can later be hosted by a standalone page.
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
  const [active, setActive] = useState<ActiveEntry>({
    topicId: initialTopicId,
    conceptId: concept,
  });

  const activeTopic = helpTopics.find((item) => item.id === active.topicId) ?? helpTopics[0]!;
  const { Content, ConceptContent } = activeTopic;

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
      size="6xl"
      title={
        <div className="flex items-center gap-2">
          <AiOutlineQuestionCircle className="text-lg" />
          <span>{t("helpCenter.title")}</span>
        </div>
      }
      visible={visible}
      onClose={onClose}
    >
      <div className="flex gap-3 min-h-0">
        {/* Left navigation: one overview entry per topic + its concept entries */}
        <div className="flex flex-col gap-0.5 w-44 shrink-0 max-h-[72vh] overflow-y-auto pr-1">
          {helpTopics.map((topicDef) => {
            const isOverviewActive =
              topicDef.id === active.topicId && active.conceptId == undefined;

            return (
              <div key={topicDef.id} className="flex flex-col gap-0.5">
                <Button
                  className="justify-start"
                  color={isOverviewActive ? "primary" : "default"}
                  size="sm"
                  startContent={topicDef.icon}
                  variant={isOverviewActive ? "flat" : "light"}
                  onPress={() => setActive({ topicId: topicDef.id })}
                >
                  {t(topicDef.titleKey)}
                </Button>

                {topicDef.concepts && topicDef.concepts.length > 0 && (
                  <>
                    {topicDef.conceptGroupLabelKey && (
                      <div className="px-2 pt-2 pb-0.5 text-xs text-default-400">
                        {t(topicDef.conceptGroupLabelKey)}
                      </div>
                    )}
                    {topicDef.concepts.map((item) => {
                      const isActive =
                        topicDef.id === active.topicId && active.conceptId === item.id;

                      return (
                        <Button
                          key={item.id}
                          className={`justify-start pl-5 h-7 min-h-7 ${
                            isActive ? "" : "text-default-600"
                          }`}
                          color={isActive ? "primary" : "default"}
                          size="sm"
                          variant={isActive ? "flat" : "light"}
                          onPress={() => setActive({ topicId: topicDef.id, conceptId: item.id })}
                        >
                          {t(item.labelKey)}
                        </Button>
                      );
                    })}
                  </>
                )}
              </div>
            );
          })}
        </div>

        {/* Right pane */}
        <div className="flex-1 min-w-0 max-h-[72vh] overflow-y-auto overflow-x-hidden pr-1 pb-2 border-l border-default-100 pl-3">
          {active.conceptId != undefined && ConceptContent ? (
            <ConceptContent conceptId={active.conceptId} />
          ) : (
            <Content
              firstRun={firstRun}
              section={active.topicId === initialTopicId ? section : undefined}
            />
          )}
        </div>
      </div>
    </Modal>
  );
};

HelpCenterModal.displayName = "HelpCenterModal";

export default HelpCenterModal;
