"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { components } from "@/sdk/BApi2";

import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheckCircle, AiOutlineCloseCircle, AiOutlineSwap } from "react-icons/ai";

import { Modal, Textarea, toast } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

type PlaceholderResult =
  components["schemas"]["Bakabase.Service.Models.View.ResourcePlaceholderResultViewModel"];

interface Props extends DestroyableProps {
  /** Called once anything was created, so the grid can refresh. */
  onCreated?: () => void;
}

/**
 * "I am missing these." One per line — a name, a work id, a store page or a link someone shared;
 * the server decides which each line is, so the user never has to classify them.
 *
 * The modal stays open after submitting to show what happened to each line: created, matched
 * against something already tracked, or refused. Closing on success would hide exactly the part
 * worth reading.
 */
const CreatePlaceholderResourcesModal: React.FC<Props> = ({ onCreated, onDestroyed }) => {
  const { t } = useTranslation();
  const [text, setText] = useState("");
  const [results, setResults] = useState<PlaceholderResult[] | undefined>();
  const [submitting, setSubmitting] = useState(false);

  const lines = text
    .split("\n")
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  const submit = async () => {
    if (lines.length === 0) return;
    setSubmitting(true);
    try {
      const rsp = await BApi.resource.createPlaceholderResources({
        items: lines.map((title) => ({ title })),
        acquireImmediately: false,
      });

      if (rsp.code) {
        toast.danger(rsp.message ?? t<string>("resource.unmaterialized.error.failed"));

        return;
      }

      const data = rsp.data ?? [];

      setResults(data);
      if (data.some((r) => r.resourceId != null)) {
        onCreated?.();
      }
    } finally {
      setSubmitting(false);
    }
  };

  const renderResult = (result: PlaceholderResult) => {
    const line = lines[result.index] ?? "";

    const [icon, tone, label] = result.error
      ? [<AiOutlineCloseCircle key="e" />, "text-danger", result.error]
      : result.created
        ? [
            <AiOutlineCheckCircle key="c" />,
            "text-success",
            t<string>("resource.unmaterialized.result.created"),
          ]
        : [
            <AiOutlineSwap key="m" />,
            "text-warning",
            t<string>("resource.unmaterialized.result.matched"),
          ];

    return (
      <div key={result.index} className="flex items-start gap-2 text-xs">
        <span className={`mt-0.5 ${tone}`}>{icon}</span>
        <div className="min-w-0 grow">
          <div className="break-all">{result.name ?? line}</div>
          <div className="opacity-60 break-all">{line}</div>
        </div>
        <span className={`shrink-0 ${tone}`}>{label}</span>
      </div>
    );
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t<string>("resource.unmaterialized.action.create", { count: lines.length }),
          isDisabled: lines.length === 0,
          isLoading: submitting,
        },
      }}
      size="lg"
      title={t<string>("resource.unmaterialized.title")}
      onDestroyed={onDestroyed}
      onOk={submit}
    >
      <div className="flex flex-col gap-3">
        <Textarea
          description={t<string>("resource.unmaterialized.input.description")}
          label={t<string>("resource.unmaterialized.input.label")}
          minRows={6}
          placeholder={t<string>("resource.unmaterialized.input.placeholder")}
          value={text}
          onValueChange={(v) => {
            setText(v);
            setResults(undefined);
          }}
        />

        {results && (
          <div className="flex flex-col gap-1 max-h-[240px] overflow-y-auto">
            <div className="text-sm font-medium">
              {t<string>("resource.unmaterialized.result.title")}
            </div>
            {results.map(renderResult)}
          </div>
        )}
      </div>
    </Modal>
  );
};

CreatePlaceholderResourcesModal.displayName = "CreatePlaceholderResourcesModal";

export default CreatePlaceholderResourcesModal;
