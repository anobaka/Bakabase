import type { FsFileNameOpConfig, FileNameModifierOperationModel } from "./types";

import React from "react";
import { useTranslation } from "react-i18next";
import { AiOutlinePlusCircle } from "react-icons/ai";

import OperationCard from "@/components/FileNameModifier/OperationCard";
import { Button } from "@/components/bakaui";
import {
  FileNameModifierCaseType,
  FileNameModifierFileNameTarget,
  FileNameModifierOperationType,
  FileNameModifierPosition,
} from "@/sdk/constants";

interface Props {
  value: FsFileNameOpConfig;
  onChange: (v: FsFileNameOpConfig) => void;
}

const newOperation = (): FileNameModifierOperationModel => ({
  target: FileNameModifierFileNameTarget.FileNameWithoutExtension,
  operation: FileNameModifierOperationType.Replace,
  position: FileNameModifierPosition.Start,
  positionIndex: 0,
  targetText: "",
  text: "",
  deleteCount: 0,
  deleteStartPosition: 0,
  caseType: FileNameModifierCaseType.TitleCase,
  alphabetStartChar: "a",
  alphabetCount: 1,
  replaceEntire: false,
  regex: false,
});

/**
 * The same OperationCard the file-name-modifier page uses — one editor for one rule engine
 * (owner decision on component reuse, capability map §9·决定 2 applies here too).
 */
const ConfigForm: React.FC<Props> = ({ value, onChange }) => {
  const { t } = useTranslation();
  const ops = value.operations ?? [];

  const setOps = (operations: FileNameModifierOperationModel[]) => onChange({ ...value, operations });

  return (
    <div className="flex flex-col gap-2">
      {ops.map((op, i) => (
        <OperationCard
          key={i}
          index={i}
          operation={op}
          onChange={(next) => setOps(ops.map((o, oi) => (oi === i ? next : o)))}
          onCopy={() => setOps([...ops.slice(0, i + 1), { ...op }, ...ops.slice(i + 1)])}
          onDelete={() => setOps(ops.filter((_, oi) => oi !== i))}
        />
      ))}
      <Button
        size="sm"
        startContent={<AiOutlinePlusCircle />}
        variant="flat"
        onPress={() => setOps([...ops, newOperation()])}
      >
        {t<string>("workflow.activity.fsFileNameOp.addOperation")}
      </Button>
    </div>
  );
};

export default ConfigForm;
