import type { DestroyableProps } from "@/components/bakaui/types";
import type { CollectionMemoSettings } from "../helpers";

import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { getTimestampTicks, resolveRangeBoundary } from "../helpers";

import { Input, Modal, Radio, RadioGroup } from "@/components/bakaui";
import { formatDateTimeInput } from "@/components/bakaui/components/Date/dateTimeInput";

interface Props extends DestroyableProps {
  settings: CollectionMemoSettings;
  /** The earliest inherited end limits the latest valid global start. */
  latestInheritedEndAt?: string;
  onSave: (settings: CollectionMemoSettings) => Promise<void>;
}

const SettingsEditor = ({ settings, latestInheritedEndAt, onSave, onDestroyed }: Props) => {
  const { t, i18n } = useTranslation();
  const [start, setStart] = useState(() => formatDateTimeInput(settings.startAt));
  const [reverse, setReverse] = useState(settings.reverse);
  const [edited, setEdited] = useState(false);
  const [composing, setComposing] = useState(false);
  const [error, setError] = useState<string>();
  const startAt = resolveRangeBoundary(start, settings.startAt);
  const locale =
    i18n.language === "cn" ? "zh-CN" : i18n.language === "en" ? "en-US" : i18n.language;
  const formatter = useMemo(
    () =>
      new Intl.DateTimeFormat(locale, {
        year: "numeric",
        month: "2-digit",
        day: "2-digit",
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit",
        hourCycle: "h23",
      }),
    [locale],
  );
  const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const renderPreview = () => {
    if (!startAt) return t<string>("collectionMemo.range.parseHint");
    const date = new Date(startAt);
    const useUtc = date.getFullYear() < 1 || date.getFullYear() > 9999;
    const previewFormatter = useUtc
      ? new Intl.DateTimeFormat(locale, {
          year: "numeric",
          month: "2-digit",
          day: "2-digit",
          hour: "2-digit",
          minute: "2-digit",
          second: "2-digit",
          hourCycle: "h23",
          timeZone: "UTC",
        })
      : formatter;
    const fraction = /\.(\d+)(?:Z|[+-]\d{2}:?\d{2})$/i.exec(startAt)?.[1];
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
        <time dateTime={startAt}>
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
        okProps: { children: t<string>("common.action.save") },
        cancelProps: { children: t<string>("common.action.cancel") },
      }}
      title={t<string>("collectionMemo.settings.title")}
      onDestroyed={onDestroyed}
      onOk={async () => {
        setEdited(true);
        const startTicks = startAt ? getTimestampTicks(startAt) : undefined;
        const inheritedEndTicks = latestInheritedEndAt
          ? getTimestampTicks(latestInheritedEndAt)
          : undefined;
        const validationKey =
          startTicks === undefined
            ? "collectionMemo.validation.date"
            : startTicks > BigInt(Date.now()) * 10_000n
              ? "collectionMemo.settings.future"
              : inheritedEndTicks !== undefined && startTicks > inheritedEndTicks
                ? "collectionMemo.settings.inheritedOrder"
                : undefined;

        if (validationKey || !startAt) {
          const message = t<string>(validationKey ?? "collectionMemo.validation.date");

          setError(message);
          throw new Error(message);
        }

        setError(undefined);
        try {
          await onSave({ startAt, reverse });
        } catch (cause) {
          setError(t<string>("collectionMemo.error.save"));
          throw cause;
        }
      }}
    >
      <div className="flex flex-col gap-3">
        <p className="text-sm text-default-500">
          {t<string>("collectionMemo.settings.description")}
        </p>
        <Input
          isRequired
          autoComplete="off"
          description={renderPreview()}
          errorMessage={
            edited && !composing && !startAt
              ? t<string>("collectionMemo.validation.date")
              : undefined
          }
          isInvalid={edited && !composing && !startAt}
          label={t<string>("collectionMemo.settings.start")}
          placeholder={t<string>("collectionMemo.range.placeholder")}
          spellCheck="false"
          type="text"
          value={start}
          onCompositionEnd={() => setComposing(false)}
          onCompositionStart={() => setComposing(true)}
          onValueChange={(value) => {
            setStart(value);
            setEdited(true);
            setError(undefined);
          }}
        />
        <div className="space-y-1 text-xs text-default-500">
          <p>{t<string>("collectionMemo.range.formatsHelp")}</p>
          <p>
            {t<string>("collectionMemo.range.localTime", { zone })}{" "}
            {t<string>("collectionMemo.range.dateOnlyHint")}
          </p>
        </div>
        <RadioGroup
          label={t<string>("collectionMemo.settings.direction")}
          value={reverse ? "reverse" : "forward"}
          onValueChange={(value) => {
            setReverse(value === "reverse");
            setError(undefined);
          }}
        >
          <Radio value="reverse">{t<string>("collectionMemo.settings.reverse")}</Radio>
          <Radio value="forward">{t<string>("collectionMemo.settings.forward")}</Radio>
        </RadioGroup>
        <p className="text-xs text-default-500">
          {t<string>("collectionMemo.settings.inheritHint")}
        </p>
        {error && (
          <p className="text-sm text-danger" role="alert">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
};

export default SettingsEditor;
