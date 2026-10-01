import type { DestroyableProps } from "@/components/bakaui/types";
import type { CollectionMemoRange } from "../helpers";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { resolveRangeBoundary, toLocalDateTimeInput } from "../helpers";

import { Input, Modal } from "@/components/bakaui";

interface Props extends DestroyableProps {
  range?: CollectionMemoRange;
  targetName: string;
  onSave: (range: { startAt: string; endAt: string }) => Promise<void>;
}

const RangeEditor = ({ range, targetName, onSave, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [start, setStart] = useState(() => toLocalDateTimeInput(range?.startAt ?? Date.now()));
  const [end, setEnd] = useState(() => toLocalDateTimeInput(range?.endAt ?? Date.now()));
  const [error, setError] = useState<string>();

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: { children: t<string>("common.action.save") },
        cancelProps: { children: t<string>("common.action.cancel") },
      }}
      title={t<string>(
        range ? "collectionMemo.action.editRange" : "collectionMemo.action.addRange",
      )}
      onDestroyed={onDestroyed}
      onOk={async () => {
        const startAt = resolveRangeBoundary(start, range?.startAt);
        const endAt = resolveRangeBoundary(end, range?.endAt);
        const validationKey =
          !startAt || !endAt
            ? "collectionMemo.validation.date"
            : Date.parse(startAt) > Date.parse(endAt)
              ? "collectionMemo.validation.order"
              : Date.parse(endAt) > Date.now()
                ? "collectionMemo.validation.future"
                : undefined;

        if (validationKey || !startAt || !endAt) {
          const message = t<string>(validationKey ?? "collectionMemo.validation.date");

          setError(message);
          throw new Error(message);
        }

        setError(undefined);
        try {
          await onSave({ startAt, endAt });
        } catch (cause) {
          setError(t<string>("collectionMemo.error.save"));
          throw cause;
        }
      }}
    >
      <div className="flex flex-col gap-3">
        <div className="break-words font-medium">{targetName}</div>
        <Input
          isRequired
          label={t<string>("collectionMemo.range.start")}
          step="1"
          type="datetime-local"
          value={start}
          onValueChange={setStart}
        />
        <Input
          isRequired
          label={t<string>("collectionMemo.range.end")}
          step="1"
          type="datetime-local"
          value={end}
          onValueChange={setEnd}
        />
        <p className="text-sm text-default-500">{t<string>("collectionMemo.range.localTime")}</p>
        <p className="text-sm text-default-500">{t<string>("collectionMemo.range.pointHint")}</p>
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
