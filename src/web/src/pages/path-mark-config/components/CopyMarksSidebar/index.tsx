"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineLeft, AiOutlineRight, AiOutlineDelete } from "react-icons/ai";

import CandidateGroupCard from "./CandidateGroupCard";

import { Button, Badge } from "@/components/bakaui";
import { useCopyMarksStore } from "@/stores/copyMarks";

const CopyMarksSidebar = () => {
  const { t } = useTranslation();
  const {
    candidateGroups,
    selectedGroupId,
    sidebarCollapsed,
    selectGroup,
    removeGroup,
    toggleSidebar,
    clearAllGroups,
  } = useCopyMarksStore();

  // Hide completely if no groups and collapsed
  if (candidateGroups.length === 0 && sidebarCollapsed) {
    // Render an empty div to keep component mounted for state updates
    return <div className="hidden" />;
  }

  if (sidebarCollapsed) {
    return (
      <div className="flex-shrink-0 border-l border-default-200">
        <Button
          isIconOnly
          variant="light"
          className="h-full rounded-none px-2"
          onPress={toggleSidebar}
        >
          <Badge
            content={candidateGroups.length}
            color="primary"
            size="sm"
            isInvisible={candidateGroups.length === 0}
          >
            <AiOutlineLeft className="text-lg" />
          </Badge>
        </Button>
      </div>
    );
  }

  return (
    <div className="flex-shrink-0 w-[280px] border-l border-default-200 flex flex-col bg-background">
      {/* Header */}
      <div className="flex items-center justify-between p-3 border-b border-default-200">
        <h3 className="text-sm font-medium">{t("pathMarkConfig.label.copiedMarks")}</h3>
        <Button
          isIconOnly
          size="sm"
          variant="light"
          onPress={toggleSidebar}
        >
          <AiOutlineRight className="text-lg" />
        </Button>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-y-auto p-3 space-y-2">
        {candidateGroups.length === 0 ? (
          <div className="text-center text-default-400 py-8">
            <p className="text-sm">{t("pathMarkConfig.empty.noCopiedMarks")}</p>
            <p className="text-xs mt-1">{t("pathMarkConfig.tip.selectMarksToCopy")}</p>
          </div>
        ) : (
          candidateGroups.map((group) => (
            <CandidateGroupCard
              key={group.id}
              group={group}
              isSelected={selectedGroupId === group.id}
              onSelect={() => selectGroup(selectedGroupId === group.id ? null : group.id)}
              onRemove={() => removeGroup(group.id)}
            />
          ))
        )}
      </div>

      {/* Footer */}
      {candidateGroups.length > 0 && (
        <div className="p-3 border-t border-default-200">
          <Button
            size="sm"
            variant="flat"
            color="danger"
            startContent={<AiOutlineDelete />}
            className="w-full"
            onPress={clearAllGroups}
          >
            {t("common.action.clearAll")}
          </Button>
        </div>
      )}
    </div>
  );
};

CopyMarksSidebar.displayName = "CopyMarksSidebar";

export default CopyMarksSidebar;
