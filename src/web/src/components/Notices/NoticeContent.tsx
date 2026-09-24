"use client";

import type { ReactNode } from "react";
import type { NoticeDefinition } from "./registry";

import { useTranslation } from "react-i18next";
import { AiOutlineArrowRight } from "react-icons/ai";

import { Button, Chip } from "@/components/bakaui";

export interface NoticeContentProps {
  notice: NoticeDefinition;
  /** Whether it has been read, where this page knows; nothing is shown otherwise. */
  read?: boolean;
  /** Takes the notice's action. Without it the action is not offered here. */
  onAct?: () => void;
  /** More controls, beside the action. */
  extra?: ReactNode;
}

/** One notice: shared by the startup dialog and the help center's list. */
const NoticeContent = ({ notice, read, onAct, extra }: NoticeContentProps) => {
  const { t } = useTranslation();
  const Icon = notice.icon;
  const titleId = `notice-${notice.id}-title`;
  const showAction = !!(notice.action && onAct);

  return (
    <article aria-labelledby={titleId} className="flex gap-4" data-notice-id={notice.id}>
      <span
        aria-hidden
        className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary"
      >
        <Icon className="text-xl" />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-2">
        <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
          <span>{t("notices.introducedIn", { version: notice.introducedIn })}</span>
          {read != undefined && (
            <Chip color={read ? "default" : "primary"} size="sm" variant="flat">
              {t(read ? "notices.state.read" : "notices.state.unread")}
            </Chip>
          )}
        </div>
        <h3 className="text-base font-semibold leading-snug" id={titleId}>
          {t(notice.titleKey)}
        </h3>
        <p className="text-sm leading-relaxed text-default-600">{t(notice.bodyKey)}</p>
        {notice.pointKeys && notice.pointKeys.length > 0 && (
          <ul className="flex list-disc flex-col gap-1.5 pl-5 text-sm leading-relaxed text-default-600 marker:text-default-400">
            {notice.pointKeys.map((key) => (
              <li key={key}>{t(key)}</li>
            ))}
          </ul>
        )}
        {(showAction || extra) && (
          <div className="flex flex-wrap items-center gap-2 pt-1">
            {showAction && (
              <Button
                color="primary"
                endContent={<AiOutlineArrowRight className="text-sm" />}
                size="sm"
                variant="flat"
                onPress={onAct}
              >
                {t(notice.action!.labelKey)}
              </Button>
            )}
            {extra}
          </div>
        )}
      </div>
    </article>
  );
};

NoticeContent.displayName = "NoticeContent";

export default NoticeContent;
