import type { PostAvailability, PostParserTask } from "@/core/models/PostParserTask";

import { useTranslation } from "react-i18next";
import { Popover, PopoverContent, PopoverTrigger } from "@heroui/react";
import { AiOutlineQuestionCircle } from "react-icons/ai";

import { Button } from "@/components/bakaui";

export const contentText = (html?: string) => {
  if (!html) return "";
  const document = new DOMParser().parseFromString(html, "text/html");

  document.querySelectorAll("script,style").forEach((element) => element.remove());
  document.querySelectorAll("br").forEach((element) => element.replaceWith("\n"));
  document.querySelectorAll("p,div,li").forEach((element) => element.append("\n"));

  return document.body.textContent?.trim() ?? "";
};

export function EvidencePopover({
  reason,
  evidence,
  label,
}: {
  reason?: string | null;
  evidence?: string[];
  label?: string;
}) {
  const { t } = useTranslation();
  const details = evidence?.filter(Boolean) ?? [];

  if (!reason && !details.length) return null;

  return (
    <Popover placement="bottom-start">
      <PopoverTrigger>
        <Button
          isIconOnly
          aria-label={label ?? t("postParser.label.evidence")}
          className="h-6 min-w-6 w-6 shrink-0 text-default-500"
          size="sm"
          variant="light"
        >
          <AiOutlineQuestionCircle aria-hidden className="text-sm" />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="max-w-sm items-start gap-2 p-3 text-xs">
        {reason && <p className="whitespace-pre-wrap break-words">{reason}</p>}
        {details.length > 0 && (
          <ul className="list-disc space-y-1 pl-4">
            {details.map((text, index) => (
              <li key={index} className="whitespace-pre-wrap break-words">
                {text}
              </li>
            ))}
          </ul>
        )}
      </PopoverContent>
    </Popover>
  );
}

export function AvailabilityDetails({
  value,
  compact = true,
}: {
  value?: PostAvailability | null;
  compact?: boolean;
}) {
  const { t } = useTranslation();

  if (!value) return null;

  return (
    <div className="space-y-1 text-xs">
      <div className="flex flex-wrap items-center gap-1">
        <span className={value.status === "expired" ? "text-warning-600" : "text-default-500"}>
          {t(`postParser.availability.${value.status}`, { defaultValue: value.status })}
        </span>
        {compact && (
          <EvidencePopover
            evidence={value.evidence}
            label={t("postParser.label.availabilityDetails")}
            reason={value.reason}
          />
        )}
      </div>
      {!compact && (
        <>
          {value.reason && <p className="text-default-500">{value.reason}</p>}
          {!!value.evidence?.length && (
            <ul className="list-disc space-y-1 pl-4">
              {value.evidence.map((text, index) => (
                <li key={index}>{text}</li>
              ))}
            </ul>
          )}
        </>
      )}
    </div>
  );
}

export default function PostDetails({ task }: { task: PostParserTask }) {
  const { t } = useTranslation();
  const snapshot = task.contentSnapshot;
  const comments = snapshot?.comments?.length
    ? snapshot.comments
    : snapshot?.commentHtmlList?.map((html, index) => ({
        html,
        floor: String(index + 1),
        author: undefined,
      }));

  return (
    <div className="max-h-[65vh] space-y-4 overflow-auto text-sm">
      {snapshot?.capturedAt && (
        <p className="text-xs text-default-500">
          {t("postParser.snapshot.capturedAt", {
            time: new Date(snapshot.capturedAt).toLocaleString(),
          })}
        </p>
      )}
      <p className="text-xs text-default-500">{t("postParser.snapshot.scope")}</p>
      <AvailabilityDetails compact={false} value={task.availability} />
      <pre className="whitespace-pre-wrap break-words font-sans">
        {contentText(snapshot?.mainHtml) || task.text || task.content}
      </pre>
      {comments?.map((comment, index) => (
        <section key={index} className="space-y-2 border-t border-default-200 pt-3">
          <p className="text-xs font-medium text-default-500">
            {t("postParser.snapshot.floor", { floor: comment.floor ?? index + 1 })} {comment.author}
          </p>
          <pre className="whitespace-pre-wrap break-words font-sans">
            {contentText(comment.html)}
          </pre>
        </section>
      ))}
    </div>
  );
}
