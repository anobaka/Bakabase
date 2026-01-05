"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useMemo } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineSnippets } from "react-icons/ai";

import { getNewMarks, getExistingMarksCount } from "../utils/markComparison";

import { Button, Tooltip } from "@/components/bakaui";
import { useCopyMarksStore } from "@/stores/copyMarks";

interface PasteMarksButtonProps {
  targetPath: string;
  existingMarks: BakabaseAbstractionsModelsDomainPathMark[];
  onPaste: (marks: BakabaseAbstractionsModelsDomainPathMark[]) => void;
}

const PasteMarksButton = ({
  targetPath,
  existingMarks,
  onPaste,
}: PasteMarksButtonProps) => {
  const { t } = useTranslation();
  const { candidateGroups, selectedGroupId } = useCopyMarksStore();

  const selectedGroup = useMemo(
    () => candidateGroups.find((g) => g.id === selectedGroupId),
    [candidateGroups, selectedGroupId],
  );

  const { newMarks, existingCount, totalCount } = useMemo(() => {
    if (!selectedGroup) {
      return { newMarks: [], existingCount: 0, totalCount: 0 };
    }

    const sourceMarks = selectedGroup.marks;
    const newMarksResult = getNewMarks(sourceMarks, existingMarks);
    const existingCountResult = getExistingMarksCount(sourceMarks, existingMarks);

    return {
      newMarks: newMarksResult,
      existingCount: existingCountResult,
      totalCount: sourceMarks.length,
    };
  }, [selectedGroup, existingMarks]);

  // Don't render if no group selected or no marks to paste
  if (!selectedGroup || newMarks.length === 0) {
    return null;
  }

  // Don't render if pasting to the same path
  if (selectedGroup.sourcePath === targetPath) {
    return null;
  }

  const handlePaste = () => {
    onPaste(newMarks);
  };

  const button = (
    <Button
      size="sm"
      color="warning"
      variant="flat"
      startContent={<AiOutlineSnippets />}
      onPress={handlePaste}
    >
      {t("pathMarkConfig.action.pasteMarks", { count: newMarks.length })}
    </Button>
  );

  // Show tooltip if some marks already exist
  if (existingCount > 0) {
    return (
      <Tooltip
        content={t("pathMarkConfig.tip.marksAlreadyExist", { count: existingCount })}
      >
        {button}
      </Tooltip>
    );
  }

  return button;
};

PasteMarksButton.displayName = "PasteMarksButton";

export default PasteMarksButton;
