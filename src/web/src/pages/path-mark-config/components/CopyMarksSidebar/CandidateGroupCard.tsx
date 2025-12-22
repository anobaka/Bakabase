"use client";

import type { CandidateGroup } from "@/stores/copyMarks";

import { useCallback } from "react";
import { AiOutlineClose } from "react-icons/ai";

import PathMarkChip from "../PathMarkChip";

import { Card, CardBody, Button } from "@/components/bakaui";
import { useCopyMarksStore } from "@/stores/copyMarks";

interface CandidateGroupCardProps {
  group: CandidateGroup;
  isSelected: boolean;
  onSelect: () => void;
  onRemove: () => void;
}

const CandidateGroupCard = ({
  group,
  isSelected,
  onSelect,
  onRemove,
}: CandidateGroupCardProps) => {
  const { removeMarkFromGroup } = useCopyMarksStore();

  // Get display path (truncate if too long)
  const displayPath =
    group.sourcePath.length > 40
      ? `...${group.sourcePath.slice(-37)}`
      : group.sourcePath;

  // Sort marks by type for consistent display
  const sortedMarks = [...group.marks].sort((a, b) => (a.type ?? 0) - (b.type ?? 0));

  const handleRemoveMark = useCallback(
    (markId: number) => {
      removeMarkFromGroup(group.id, markId);
    },
    [group.id, removeMarkFromGroup],
  );

  return (
    <Card
      className={`cursor-pointer transition-all ${
        isSelected
          ? "border-2 border-primary shadow-md"
          : "border border-default-200 hover:border-default-400"
      }`}
      isPressable
      onPress={onSelect}
    >
      <CardBody className="p-3 gap-2">
        <div className="flex items-start justify-between gap-2">
          <div className="flex-1 min-w-0">
            <p
              className="text-xs text-default-500 truncate"
              title={group.sourcePath}
            >
              {displayPath}
            </p>
          </div>
          <Button
            isIconOnly
            size="sm"
            variant="light"
            className="text-default-400 hover:text-danger min-w-6 w-6 h-6"
            onPress={(e) => {
              onRemove();
            }}
          >
            <AiOutlineClose className="text-sm" />
          </Button>
        </div>

        {/* Display all marks using PathMarkChip - right click to remove */}
        <div className="flex items-center gap-1 flex-wrap">
          {sortedMarks.map((mark) => (
            <PathMarkChip
              key={mark.id}
              mark={mark}
              onContextMenu={mark.id !== undefined ? () => handleRemoveMark(mark.id!) : undefined}
            />
          ))}
        </div>
      </CardBody>
    </Card>
  );
};

CandidateGroupCard.displayName = "CandidateGroupCard";

export default CandidateGroupCard;
