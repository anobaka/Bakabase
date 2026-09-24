"use client";

import type { HelpTopicContentProps } from "@/components/HelpCenter/types";
import type { NoticeDefinition } from "./registry";

import { useEffect, useMemo } from "react";
import { useTranslation } from "react-i18next";

import NoticeContent from "./NoticeContent";
import { useNoticeStore, useNoticeViewer, useReadNoticeIds } from "./noticeStore";
import { audienceOf, notices } from "./registry";

import { Button } from "@/components/bakaui";
import { TopicHeadline } from "@/components/HelpCenter/components/TopicBlocks";

/** Newest first. */
export const noticesByRecency = (registry: NoticeDefinition[]) =>
  [...registry].sort((a, b) => b.order - a.order);

/**
 * Every notice shipped with the app, read or not — where to find one again after "Got it".
 *
 * Read state, "Got it" and actions appear only for a viewer the notice is for; elsewhere —
 * a window showing another server, a browser that may only read — the text is all there is.
 */
const NoticesTopic = ({ onNavigate, onOpenTopic }: HelpTopicContentProps) => {
  const { t } = useTranslation();
  const viewer = useNoticeViewer();
  const known = useNoticeStore((store) => store.state != undefined);
  const load = useNoticeStore((store) => store.load);
  const markRead = useNoticeStore((store) => store.markRead);
  const readIds = useReadNoticeIds();
  const read = useMemo(() => new Set(readIds), [readIds]);

  // Also retries a load that failed at startup. That never reopens the startup dialog: the
  // gate had its turn (`startupDone`), so what is learned here only shows here.
  useEffect(() => {
    if (viewer) void load(viewer);
  }, [viewer, load]);

  const act = (notice: NoticeDefinition) => {
    const action = notice.action;

    void markRead([notice.id]);
    if (action?.kind === "help") onOpenTopic?.({ topic: action.topic, section: action.section });
    if (action?.kind === "route") onNavigate?.(action.route);
  };

  return (
    <div className="flex flex-col gap-4">
      <TopicHeadline introKey="notices.topic.intro" titleKey="notices.topic.headline" />
      {viewer === null && <p className="text-xs text-default-500">{t("notices.topic.readOnly")}</p>}
      <ul className="flex flex-col gap-3">
        {noticesByRecency(notices).map((notice) => {
          const forViewer = !!viewer && audienceOf(notice).includes(viewer);
          const isRead = forViewer && known ? read.has(notice.id) : undefined;

          return (
            <li key={notice.id} className="rounded-xl border border-default-200 bg-content1 p-4">
              <NoticeContent
                extra={
                  isRead === false ? (
                    <Button size="sm" variant="light" onPress={() => void markRead([notice.id])}>
                      {t("notices.action.gotIt")}
                    </Button>
                  ) : undefined
                }
                notice={notice}
                read={isRead}
                onAct={forViewer ? () => act(notice) : undefined}
              />
            </li>
          );
        })}
      </ul>
    </div>
  );
};

NoticesTopic.displayName = "NoticesTopic";

export default NoticesTopic;
