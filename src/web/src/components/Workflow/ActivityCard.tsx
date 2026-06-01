import React, { useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined, EditOutlined } from "@ant-design/icons";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";

import { getWorkflowActivityUI } from "./Activities";
import { activityDisplayName } from "./displayNames";

import { Button, Chip, Switch } from "@/components/bakaui";
import DragHandle from "@/components/DragHandle";
import {
  WorkflowActivityCategory,
  WorkflowActivityCategoryLabel,
  WorkflowActivityErrorBehavior,
} from "@/sdk/constants";

export interface ActivityDraft {
  /** Stable id for React keys + drag-and-drop. Not persisted. */
  clientId: string;
  kind: string;
  configJson: string;
  onItemError: WorkflowActivityErrorBehavior;
}

interface Props {
  draft: ActivityDraft;
  /** Display name fallback from the server's descriptor when the frontend has no UI for this kind. */
  displayNameFallback?: string;
  /** True when the chain walk says this activity can't accept the type reaching it. */
  incompatible?: boolean;
  onChange: (next: ActivityDraft) => void;
  onDelete: () => void;
}

const CategoryColor: Record<
  WorkflowActivityCategory,
  "primary" | "warning" | "success" | "default"
> = {
  [WorkflowActivityCategory.Filter]: "warning",
  [WorkflowActivityCategory.Action]: "primary",
  [WorkflowActivityCategory.Transform]: "success",
};

const ActivityCard: React.FC<Props> = ({
  draft,
  displayNameFallback,
  incompatible,
  onChange,
  onDelete,
}) => {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: draft.clientId,
  });
  const sortableStyle = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  const ui = getWorkflowActivityUI(draft.kind);
  const config = ui ? ui.parseConfig(draft.configJson) : null;
  const ConfigForm = ui?.ConfigForm;
  const SummaryComponent = ui?.Summary;

  const category = ui?.category;
  const categoryColor = category != null ? CategoryColor[category] : "default";

  return (
    <div
      ref={setNodeRef}
      className={`border rounded-lg flex flex-col ${
        incompatible ? "border-danger" : "border-default-200"
      }`}
      style={sortableStyle}
    >
      <div className="flex items-center gap-2 p-2">
        {/* Drag handle — keyboard reordering works too via dnd-kit's KeyboardSensor. */}
        <DragHandle {...attributes} {...listeners} />

        <div className="flex-1 min-w-0 flex flex-col gap-1">
          <div className="flex items-center gap-2 min-w-0">
            {category != null && (
              <Chip color={categoryColor} size="sm" variant="flat">
                {WorkflowActivityCategoryLabel[category]}
              </Chip>
            )}
            <span className="font-medium truncate">
              {activityDisplayName(t, draft.kind, displayNameFallback)}
            </span>
          </div>
          {!expanded && SummaryComponent && config !== null && (
            <SummaryComponent config={config} />
          )}
          {!ui && (
            <span className="text-xs text-warning-600">
              {t<string>("workflow.activity.unknownKind", { kind: draft.kind })}
            </span>
          )}
          {incompatible && (
            <span className="text-xs text-danger">
              {t<string>("workflow.activity.incompatible")}
            </span>
          )}
        </div>

        <Button
          isIconOnly
          color="default"
          size="sm"
          variant="light"
          onPress={() => setExpanded((v) => !v)}
        >
          <EditOutlined className="text-lg" />
        </Button>
        <Button isIconOnly color="danger" size="sm" variant="light" onPress={onDelete}>
          <DeleteOutlined className="text-lg" />
        </Button>
      </div>

      {expanded && (
        <div className="border-t border-default-200 p-3 flex flex-col gap-3">
          {ConfigForm && config !== null ? (
            <ConfigForm
              value={config}
              onChange={(next) =>
                onChange({ ...draft, configJson: ui!.serializeConfig(next) })
              }
            />
          ) : (
            <p className="text-xs text-default-500">
              {t<string>("workflow.activity.unknownKind.noEditor")}
            </p>
          )}
          {/* Binary fail/skip → a single Switch. The label is self-explanatory; an explicit
              header + per-option description was overkill for a two-state toggle. */}
          <Switch
            isSelected={draft.onItemError === WorkflowActivityErrorBehavior.Skip}
            onValueChange={(on) =>
              onChange({
                ...draft,
                onItemError: on
                  ? WorkflowActivityErrorBehavior.Skip
                  : WorkflowActivityErrorBehavior.Fail,
              })
            }
          >
            {t<string>("workflow.activity.skipOnItemError")}
          </Switch>
        </div>
      )}
    </div>
  );
};

export default ActivityCard;
