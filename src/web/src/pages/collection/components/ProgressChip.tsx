import type { CollectionModel } from "@/stores/collections";

import { useTranslation } from "react-i18next";

import { percent } from "../helpers";

import { Chip } from "@/components/bakaui";

const ProgressChip = ({ collection }: { collection: CollectionModel }) => {
  const { t } = useTranslation();
  const progress = collection.progress;
  const completion = percent(collection);
  const complete = !!progress && progress.total > 0 && progress.owned === progress.total;
  const emptyKey = !progress
    ? "collection.progress.unavailable"
    : progress.ignored > 0
      ? "collection.progress.allIgnored"
      : "collection.progress.empty";

  return (
    <Chip color={complete ? "success" : "default"} size="sm" variant="flat">
      {completion === undefined
        ? t<string>(emptyKey)
        : t<string>("collection.percentComplete", { percent: completion })}
    </Chip>
  );
};

export default ProgressChip;
