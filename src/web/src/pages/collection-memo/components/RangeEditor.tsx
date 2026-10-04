import type { DestroyableProps } from "@/components/bakaui/types";
import type { CollectionMemoRange, CollectionMemoRangeInput } from "../helpers";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { getCollectionMemoRangeUrl, getTimestampTicks, resolveRangeBoundary } from "../helpers";

import { Input, Modal, Textarea } from "@/components/bakaui";
import { formatDateTimeInput } from "@/components/bakaui/components/Date/dateTimeInput";

interface Props extends DestroyableProps {
  range?: CollectionMemoRange;
  targetName: string;
  globalStartAt: string;
  onSave: (range: CollectionMemoRangeInput) => Promise<void>;
}

const dateTimeFormat: Intl.DateTimeFormatOptions = {
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  hourCycle: "h23",
};

const RangeEditor = ({ range, targetName, globalStartAt, onSave, onDestroyed }: Props) => {
  const { t, i18n } = useTranslation();
  const [start, setStart] = useState(() =>
    range?.startAt ? formatDateTimeInput(range.startAt) : "",
  );
  const [end, setEnd] = useState(() =>
    formatDateTimeInput(range?.endAt ?? new Date(Date.now()).toISOString()),
  );
  const [edited, setEdited] = useState({ start: false, end: false });
  const [composing, setComposing] = useState({ start: false, end: false });
  const [error, setError] = useState<string>();
  const [url, setUrl] = useState(range?.url ?? "");
  const [note, setNote] = useState(range?.note ?? "");
  const [urlEdited, setUrlEdited] = useState(false);
  const validUrl = getCollectionMemoRangeUrl(url);
  const invalidUrl = !!url.trim() && !validUrl;
  const inherited = !start.trim();
  const startAt = inherited
    ? globalStartAt
    : resolveRangeBoundary(start, range?.startAt ?? undefined);
  const endAt = resolveRangeBoundary(end, range?.endAt);
  const locale =
    i18n.language === "cn" ? "zh-CN" : i18n.language === "en" ? "en-US" : i18n.language;
  const formatter = useMemo(() => new Intl.DateTimeFormat(locale, dateTimeFormat), [locale]);
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const renderPreview = (resolved?: string) => {
    if (!resolved) return t<string>("collectionMemo.range.parseHint");
    const date = new Date(resolved);
    const useUtc = date.getFullYear() < 1 || date.getFullYear() > 9999;
    const previewFormatter = useUtc
      ? new Intl.DateTimeFormat(locale, { ...dateTimeFormat, timeZone: "UTC" })
      : formatter;
    const fraction = /\.(\d+)(?:Z|[+-]\d{2}:?\d{2})$/i.exec(resolved)?.[1];
    const local = previewFormatter
      .formatToParts(date)
      .map((part) =>
        part.type === "second" && fraction ? `${part.value}.${fraction}` : part.value,
      )
      .join("");
    const offset = useUtc ? 0 : date.getTimezoneOffset();
    const absoluteOffset = Math.abs(offset);
    const offsetText = `UTC${offset <= 0 ? "+" : "-"}${String(Math.floor(absoluteOffset / 60)).padStart(2, "0")}:${String(absoluteOffset % 60).padStart(2, "0")}`;

    return (
      <span>
        {t<string>("collectionMemo.range.parsed")}{" "}
        <time dateTime={resolved}>
          {local} {offsetText}
        </time>
      </span>
    );
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("common.action.save"), isDisabled: invalidUrl },
        cancelProps: { children: t<string>("common.action.cancel") },
      }}
      title={t<string>(
        range ? "collectionMemo.action.editRange" : "collectionMemo.action.addRange",
      )}
      onDestroyed={onDestroyed}
      onOk={async () => {
        setEdited({ start: true, end: true });
        setUrlEdited(true);
        const startTicks = startAt ? getTimestampTicks(startAt) : undefined;
        const endTicks = endAt ? getTimestampTicks(endAt) : undefined;
        const validationKey =
          !startAt || !endAt || startTicks === undefined || endTicks === undefined
            ? "collectionMemo.validation.date"
            : startTicks > endTicks
              ? inherited
                ? "collectionMemo.validation.inheritedOrder"
                : "collectionMemo.validation.order"
              : endTicks > BigInt(Date.now()) * 10_000n
                ? "collectionMemo.validation.future"
                : invalidUrl
                  ? "collectionMemo.validation.url"
                  : undefined;

        if (validationKey || !startAt || !endAt) {
          const message = t<string>(validationKey ?? "collectionMemo.validation.date");

          setError(validationKey === "collectionMemo.validation.url" ? undefined : message);
          throw new Error(message);
        }

        setError(undefined);
        try {
          await onSave({
            startAt: inherited ? null : startAt,
            endAt,
            ...(validUrl ? { url: validUrl } : {}),
            ...(note.trim() ? { note: note.trim() } : {}),
          });
        } catch (cause) {
          setError(t<string>("collectionMemo.error.save"));
          throw cause;
        }
      }}
    >
      <div className="flex flex-col gap-3">
        <div className="break-words font-medium">{targetName}</div>
        <Input
          autoComplete="off"
          description={
            <span className="flex flex-col gap-1">
              {inherited && <span>{t<string>("collectionMemo.range.inherited")}</span>}
              {renderPreview(startAt)}
            </span>
          }
          errorMessage={
            edited.start && !composing.start && !startAt
              ? t<string>("collectionMemo.validation.date")
              : undefined
          }
          isInvalid={edited.start && !composing.start && !startAt}
          label={t<string>("collectionMemo.range.start")}
          placeholder={t<string>("collectionMemo.range.inheritPlaceholder")}
          spellCheck="false"
          type="text"
          value={start}
          onCompositionEnd={() => setComposing((previous) => ({ ...previous, start: false }))}
          onCompositionStart={() => setComposing((previous) => ({ ...previous, start: true }))}
          onValueChange={(value) => {
            setStart(value);
            setEdited((previous) => ({ ...previous, start: true }));
            setError(undefined);
          }}
        />
        <Input
          isRequired
          autoComplete="off"
          description={renderPreview(endAt)}
          errorMessage={
            edited.end && !composing.end && !endAt
              ? t<string>("collectionMemo.validation.date")
              : undefined
          }
          isInvalid={edited.end && !composing.end && !endAt}
          label={t<string>("collectionMemo.range.end")}
          placeholder={t<string>("collectionMemo.range.placeholder")}
          spellCheck="false"
          type="text"
          value={end}
          onCompositionEnd={() => setComposing((previous) => ({ ...previous, end: false }))}
          onCompositionStart={() => setComposing((previous) => ({ ...previous, end: true }))}
          onValueChange={(value) => {
            setEnd(value);
            setEdited((previous) => ({ ...previous, end: true }));
            setError(undefined);
          }}
        />
        <div className="space-y-1 text-xs text-default-500">
          <p>{t<string>("collectionMemo.range.inheritHint")}</p>
          <p>{t<string>("collectionMemo.range.formatsHelp")}</p>
          <p>
            {t<string>("collectionMemo.range.localTime", { zone })}{" "}
            {t<string>("collectionMemo.range.dateOnlyHint")}
          </p>
        </div>
        <p className="text-sm text-default-500">{t<string>("collectionMemo.range.pointHint")}</p>
        <Input
          description={t<string>("collectionMemo.range.urlHint")}
          errorMessage={
            urlEdited && invalidUrl ? t<string>("collectionMemo.validation.url") : undefined
          }
          isInvalid={urlEdited && invalidUrl}
          label={t<string>("collectionMemo.range.url")}
          placeholder="https://"
          type="url"
          value={url}
          onValueChange={(value) => {
            setUrl(value);
            setUrlEdited(true);
            setError(undefined);
          }}
        />
        <Textarea
          label={t<string>("collectionMemo.range.note")}
          minRows={2}
          value={note}
          onValueChange={setNote}
        />
        {error && (
          <p className="text-sm text-danger" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
};

export default RangeEditor;
