"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Input, Modal, Textarea, toast } from "@/components/bakaui";
import { planTaskApi } from "@/components/Acquire/PlanTask/api";

interface Props extends DestroyableProps {
  onStarted?: (taskId: number) => void;
}

/** Saving intent does not search, create a library resource, or start execution. */
export default function StartAcquisitionModal({ onStarted, onDestroyed }: Props) {
  const { t } = useTranslation();
  const [title, setTitle] = useState("");
  const [urls, setUrls] = useState("");
  return (
    <Modal
      defaultVisible
      size="2xl"
      title={t("acquisition.draft.createTitle")}
      onDestroyed={onDestroyed}
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t("acquisition.draft.save"),
          isDisabled: !title.trim() && !urls.trim(),
        },
      }}
      onOk={async () => {
        if (!title.trim() && !urls.trim()) throw new Error(t("acquisition.draft.required"));
        const result = await planTaskApi.createDraft({
          title: title.trim() || undefined,
          sources: urls
            .split(/\r?\n/)
            .map((url) => url.trim())
            .filter(Boolean)
            .map((url, index) => ({ id: `source-${index + 1}`, kind: "auto", url })),
        });
        toast.success(t("acquisition.draft.saved"));
        onStarted?.(result.task.id);
      }}
    >
      <div className="space-y-4">
        <p className="text-sm leading-relaxed text-default-500">
          {t("acquisition.draft.createHint")}
        </p>
        <Input
          autoFocus
          label={t("acquisition.plan.titleLabel")}
          value={title}
          maxLength={512}
          onValueChange={setTitle}
        />
        <Textarea
          label={t("acquisition.draft.urls")}
          description={t("acquisition.draft.urlsHint")}
          value={urls}
          onValueChange={setUrls}
        />
      </div>
    </Modal>
  );
}
