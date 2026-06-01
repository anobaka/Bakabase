import type { DescriptorWithFit } from "./activityFit";
import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { getWorkflowActivityUI } from "./Activities";
import { activityDisplayName } from "./displayNames";
import GroupLabel from "./GroupLabel";

import { Chip, Input, Modal, Tooltip } from "@/components/bakaui";
import { WorkflowActivityCategoryLabel } from "@/sdk/constants";

interface Props extends DestroyableProps {
  /** Pre-classified at the picker's open time. Direct fit first; bridge after. */
  addable: DescriptorWithFit[];
  onPick: (entry: DescriptorWithFit) => void;
}

const ActivityPicker: React.FC<Props> = ({ addable, onPick, onDestroyed }) => {
  const { t } = useTranslation();
  const [query, setQuery] = useState("");
  // The Modal is self-controlled so we can close it on pick. Keeping the picker open after
  // a click is tempting for batch-adds, but its `addable` prop is a snapshot — the chain
  // walker's compatibility set changes once the chosen activity lands in the chain, so the
  // list would silently go stale. Closing forces the editor to reopen a fresh picker that
  // reflects the new chain tail.
  const [open, setOpen] = useState(true);

  // Focus the search input on mount programmatically rather than via the `autoFocus` JSX
  // prop — the latter trips jsx-a11y/no-autofocus, while focusing imperatively after the
  // modal is mounted is semantically equivalent here (the picker is a transient overlay
  // the user explicitly invoked, so stealing focus is desirable).
  const searchInputRef = useRef<HTMLInputElement>(null);
  useEffect(() => {
    searchInputRef.current?.focus();
  }, []);

  const pickable = useMemo(
    () => addable.filter((e) => !!getWorkflowActivityUI(e.descriptor.kind)),
    [addable],
  );

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return pickable;
    return pickable.filter(
      (e) =>
        // Also match against the localized name so non-English searches work.
        activityDisplayName(t, e.descriptor.kind, e.descriptor.displayName)
          .toLowerCase()
          .includes(q) ||
        e.descriptor.displayName.toLowerCase().includes(q) ||
        e.descriptor.kind.toLowerCase().includes(q) ||
        e.descriptor.group.toLowerCase().includes(q),
    );
  }, [pickable, query, t]);

  // Group by `group`; direct fits float above bridge fits inside each section so the
  // happy-path picks stay at the top.
  const grouped = useMemo(() => {
    const m = new Map<string, DescriptorWithFit[]>();
    for (const e of filtered) {
      const list = m.get(e.descriptor.group) ?? [];
      list.push(e);
      m.set(e.descriptor.group, list);
    }
    for (const list of m.values()) {
      list.sort((a, b) => (a.fit === b.fit ? 0 : a.fit === "direct" ? -1 : 1));
    }
    return Array.from(m.entries());
  }, [filtered]);

  const handlePick = (entry: DescriptorWithFit) => {
    onPick(entry);
    setOpen(false);
  };

  return (
    <Modal
      footer={false}
      size="2xl"
      title={t<string>("workflow.activity.picker.title")}
      visible={open}
      onClose={() => setOpen(false)}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-3">
        <Input
          ref={searchInputRef}
          placeholder={t<string>("workflow.activity.picker.searchPlaceholder")}
          size="sm"
          value={query}
          onValueChange={setQuery}
        />
        {grouped.length === 0 ? (
          <div className="text-center text-default-500 py-6">
            {t<string>("workflow.activity.picker.empty")}
          </div>
        ) : (
          <div className="flex flex-col gap-3 max-h-[60vh] overflow-auto">
            {grouped.map(([group, items]) => (
              <div key={group} className="flex flex-col gap-1">
                <div className="text-xs font-medium px-1">
                  <GroupLabel group={group} />
                </div>
                <div className="flex flex-col gap-0.5">
                  {items.map((entry) => (
                    <button
                      key={entry.descriptor.kind}
                      className="text-left px-3 py-2 rounded-md hover:bg-default-100 transition-colors flex items-center gap-2"
                      type="button"
                      onClick={() => handlePick(entry)}
                    >
                      <Chip size="sm" variant="flat">
                        {WorkflowActivityCategoryLabel[entry.descriptor.category]}
                      </Chip>
                      <span className="text-sm flex-1">
                        {activityDisplayName(t, entry.descriptor.kind, entry.descriptor.displayName)}
                      </span>
                      {entry.fit === "bridge" && (
                        <Tooltip
                          content={t<string>("workflow.activity.picker.needsBridge.description")}
                        >
                          <Chip color="warning" size="sm" variant="flat">
                            {t<string>("workflow.activity.picker.needsBridge")}
                          </Chip>
                        </Tooltip>
                      )}
                    </button>
                  ))}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </Modal>
  );
};

export default ActivityPicker;
