import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";
import { captureFilteredSelection, validSelectionSnapshot } from "../acquisitionSelection";
import { localBatchSelectionLimit } from "./acquisitionLocalBatchApi";

export default function AcquisitionSelectionButtons({
  pageIds, filteredIds, totalCount, truncated, disabled, otherPagesSelected, onSelect,
}: {
  pageIds: number[];
  filteredIds: number[];
  totalCount: number;
  truncated: boolean;
  disabled: boolean;
  otherPagesSelected: boolean;
  onSelect: (ids: number[]) => void;
}) {
  const { t } = useTranslation();
  const valid = validSelectionSnapshot(filteredIds);
  const selectFiltered = (first?: number) => {
    const ids = captureFilteredSelection(filteredIds, truncated, first);
    if (!disabled && ids) onSelect(ids);
  };
  return (
    <>
      <Button
        className="h-auto min-h-8 min-w-0 max-w-full whitespace-normal"
        size="sm"
        variant="flat"
        isDisabled={disabled || truncated || !valid}
        onPress={() => selectFiltered()}
      >
        {t("acquisition.selection.allFiltered", { count: totalCount })}
      </Button>
      {filteredIds.length > localBatchSelectionLimit && (
        <Button
          className="h-auto min-h-8 min-w-0 max-w-full whitespace-normal"
          size="sm"
          variant="flat"
          isDisabled={disabled || !valid}
          onPress={() => selectFiltered(localBatchSelectionLimit)}
        >
          {t("acquisition.selection.firstByOrder", { count: localBatchSelectionLimit })}
        </Button>
      )}
      {otherPagesSelected && (
        <Button
          className="h-auto min-h-8 min-w-0 max-w-full whitespace-normal"
          size="sm"
          variant="flat"
          isDisabled={disabled || !pageIds.length}
          onPress={() => onSelect([...pageIds])}
        >
          {t("acquisition.selection.onlyPage", { count: pageIds.length })}
        </Button>
      )}
    </>
  );
}
