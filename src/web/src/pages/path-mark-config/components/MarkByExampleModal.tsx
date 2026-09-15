"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { MarkByExampleCandidate } from "../utils/markByExample";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";

import { markByExampleCandidates, preferredCandidate } from "../utils/markByExample";

import { Modal, Radio, RadioGroup } from "@/components/bakaui";
import { PathFilterFsType, PathMatchMode } from "@/sdk/constants";

type Props = DestroyableProps & {
  /** The folder the user pointed at — one they already have, of the kind they want marked. */
  samplePath: string;
  /** Whether a folder already carries marks, used to guess which ancestor they meant. */
  pathHasMarks: (path: string) => boolean;
  onConfirm: (rootPath: string, configJson: string) => Promise<void> | void;
};

/**
 * "Everything at this level is a resource."
 *
 * A layer mark is written relative to the folder it sits on, so using one means working out which
 * number describes the folders you actually care about — and getting it wrong makes resources of
 * the wrong things, which looks like it worked until somebody counts. Pointing at one example
 * instead is the same statement without the arithmetic.
 */
const MarkByExampleModal = ({ samplePath, pathHasMarks, onConfirm, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const candidates = useMemo(
    () => markByExampleCandidates(samplePath, pathHasMarks),
    [samplePath, pathHasMarks],
  );

  const [chosen, setChosen] = useState<MarkByExampleCandidate | undefined>(() =>
    preferredCandidate(candidates),
  );

  const submit = async () => {
    if (!chosen) return;

    await onConfirm(
      chosen.rootPath,
      JSON.stringify({
        matchMode: PathMatchMode.Layer,
        layer: chosen.layer,
        // Marking by example is always about folders: the example the user pointed at is one.
        fsTypeFilter: PathFilterFsType.Directory,
      }),
    );
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t<string>("pathMarkConfig.markByExample.confirm"),
          isDisabled: !chosen,
        },
      }}
      size="lg"
      title={t<string>("pathMarkConfig.markByExample.title")}
      onDestroyed={onDestroyed}
      onOk={submit}
    >
      <div className="flex flex-col gap-3">
        <div className="text-sm text-default-500">
          {t<string>("pathMarkConfig.markByExample.description", { path: samplePath })}
        </div>

        {candidates.length === 0 ? (
          <div className="text-sm text-default-400">
            {t<string>("pathMarkConfig.markByExample.noAncestors")}
          </div>
        ) : (
          <RadioGroup
            label={t<string>("pathMarkConfig.markByExample.root.label")}
            value={chosen?.rootPath ?? ""}
            onValueChange={(rootPath) => setChosen(candidates.find((c) => c.rootPath === rootPath))}
          >
            {candidates.map((candidate) => (
              <Radio
                key={candidate.rootPath}
                description={t<string>("pathMarkConfig.markByExample.layer", {
                  layer: candidate.layer,
                })}
                value={candidate.rootPath}
              >
                <span className="break-all">
                  {candidate.rootPath}
                  {candidate.hasMarks && (
                    <span className="ml-2 text-xs text-default-400">
                      {t<string>("pathMarkConfig.markByExample.alreadyMarked")}
                    </span>
                  )}
                </span>
              </Radio>
            ))}
          </RadioGroup>
        )}
      </div>
    </Modal>
  );
};

MarkByExampleModal.displayName = "MarkByExampleModal";

export default MarkByExampleModal;
