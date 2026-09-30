import type { MoveExcludedResource } from "./types";

import { getKnownMoveReasonCode, useMoveReasonText, useMoveText } from "./messages";

import { Button, Modal, Select, Spinner } from "@/components/bakaui";
import {
  cancelMoveDraft,
  hideUnknownMoveDraft,
  draftMovableIds,
  setMoveDraftPolicy,
  submitMoveDraft,
  useResourceMovePanelStore,
} from "@/stores/resourceMovePanel";
import { PathMarkType } from "@/sdk/constants";

export default function MoveConfirmation() {
  const draft = useResourceMovePanelStore((s) => s.draft);
  const text = useMoveText();
  const reasonText = useMoveReasonText();

  if (!draft) return null;
  const ids = draftMovableIds(draft);
  const items = draft.preview?.items ?? [];
  const blocked = !!draft.preview?.duplicateDestinationPaths?.length;
  const irreversibleSubmit = !!draft.request;
  const loading = draft.phase === "preview" || draft.phase === "submitting";
  const movableItems = items.filter((item) => ids.includes(item.resourceId));
  const excludedById = new Map<number, MoveExcludedResource>(
    (draft.preview?.excludedResources ?? []).map((resource) => [resource.resourceId, resource]),
  );

  // Older servers may return only skipped IDs or item reasons. Keep those exclusions visible too.
  for (const resource of draft.preview ? draft.payload.resources : []) {
    if (ids.includes(resource.id) || excludedById.has(resource.id)) continue;
    const item = items.find((item) => item.resourceId === resource.id);
    const parent = items.find((item) =>
      item.coveredResources?.some((child) => child.resourceId === resource.id),
    );
    const parentExclusion = parent ? excludedById.get(parent.resourceId) : undefined;

    excludedById.set(resource.id, {
      resourceId: resource.id,
      displayName: resource.displayName,
      path: resource.path ?? item?.sourcePath,
      reasonCode:
        item?.unavailableReason ??
        (item?.destInsideSource ? "destinationInsideSource" : undefined) ??
        parentExclusion?.reasonCode ??
        parent?.unavailableReason ??
        (draft.preview?.skippedResourceIds?.includes(resource.id) ? "noLocalFiles" : "unavailable"),
      blockingResourceIds: parentExclusion?.blockingResourceIds,
    });
  }
  const excluded = [...excludedById.values()];
  const resourceLabel = (id: number, path?: string | null, displayName?: string | null) => {
    const resource = draft.payload.resources.find((resource) => resource.id === id);
    const coveredPath = items
      .flatMap((item) => item.coveredResources ?? [])
      .find((child) => child.resourceId === id)?.path;

    return (
      displayName ||
      resource?.displayName ||
      (path || resource?.path || coveredPath)?.split(/[\\/]/).filter(Boolean).at(-1) ||
      `${text("resource")} #${id}`
    );
  };

  return (
    <Modal
      visible
      classNames={{ base: "max-w-2xl max-h-[85vh]", body: "overflow-y-auto" }}
      footer={false}
      hideCloseButton={irreversibleSubmit && draft.phase !== "unknown"}
      isDismissable={!irreversibleSubmit || draft.phase === "unknown"}
      isKeyboardDismissDisabled={irreversibleSubmit && draft.phase !== "unknown"}
      title={text("confirm")}
      onClose={draft.phase === "unknown" ? hideUnknownMoveDraft : cancelMoveDraft}
    >
      <div data-resource-move-panel className="move-panel-confirm">
        <p>{text("physical")}</p>
        <strong>{text("path")}</strong>
        <code className="move-panel-full-path">{draft.destination.path}</code>
        <p>
          {text("count")}: {draft.payload.resources.length} · {text("topLevel")}:{" "}
          {movableItems.length}
        </p>
        {loading && (
          <div className="flex items-center gap-2">
            <Spinner size="sm" />
            {text(draft.phase === "preview" ? "prepare" : "submitting")}
          </div>
        )}
        {draft.phase === "unknown" && (
          <div className="move-panel-notice" role="status">
            <strong>{text("unknown")}</strong>
            <p>{text("unknownHelp")}</p>
          </div>
        )}
        {(draft.contextError || draft.error) && (
          <div className="move-panel-error" role="alert">
            {draft.contextError
              ? reasonText(draft.contextError)
              : getKnownMoveReasonCode(draft.error)
                ? reasonText(draft.error)
                : draft.error}
          </div>
        )}
        {blocked && (
          <div className="move-panel-error">
            {text("conflict")}
            <ul>
              {draft.preview?.duplicateDestinationPaths?.map((path) => <li key={path}>{path}</li>)}
            </ul>
          </div>
        )}
        <div className="move-panel-preview-list">
          {movableItems.map((item) => (
            <article key={item.resourceId} className="move-panel-preview-item">
              <strong>{resourceLabel(item.resourceId, item.sourcePath)}</strong>
              <code>{item.sourcePath}</code>
              <code>→ {item.destPath}</code>
              {item.destConflict && (
                <span className="move-panel-badge warning">{text("conflict")}</span>
              )}
              {!!item.coveredResources?.length && (
                <details>
                  <summary>
                    {text("affected")}: {item.coveredResources.length}
                  </summary>
                  {item.coveredResources.map((c) => (
                    <code key={c.resourceId}>{c.path}</code>
                  ))}
                </details>
              )}
              <div className="move-panel-badges">
                {item.effects?.map((effect) => (
                  <span
                    key={effect.markId}
                    className="move-panel-badge"
                    style={{ opacity: effect.willApply ? 1 : 0.6 }}
                  >
                    {effect.type === PathMarkType.MediaLibrary
                      ? effect.mediaLibraryName ||
                        (effect.isDynamic ? "Dynamic media library" : "Media library")
                      : `${effect.propertyName ?? "Property"}${effect.fixedValue ? ` = ${effect.fixedValue}` : ""}`}
                    {effect.isDynamic ? " ∼" : ""}
                  </span>
                ))}
              </div>
            </article>
          ))}
        </div>
        {draft.preview && !ids.length && (
          <p className="move-panel-error" role="alert">
            {text("noMovableResources")}
          </p>
        )}
        {!!excluded.length && (
          <details open className="move-panel-notice">
            <summary>
              {text("excluded")}: {excluded.length}
            </summary>
            {excluded.map((resource) => {
              const path =
                resource.path ??
                draft.payload.resources.find((r) => r.id === resource.resourceId)?.path;

              return (
                <article key={resource.resourceId} className="move-panel-preview-item">
                  <strong>{resourceLabel(resource.resourceId, path, resource.displayName)}</strong>
                  {path && <code>{path}</code>}
                  <p>{reasonText(resource.reasonCode)}</p>
                  {!!resource.blockingResourceIds?.length && (
                    <div>
                      {text("blockingResources")} ({resource.blockingResourceIds.length}):{" "}
                      {resource.blockingResourceIds.map((id) => resourceLabel(id)).join(", ")}
                    </div>
                  )}
                </article>
              );
            })}
          </details>
        )}
        <Select
          disallowEmptySelection
          dataSource={["inherit", "ask", "overwrite"].map((value) => ({
            value,
            label: text(value as typeof draft.conflictPolicy),
          }))}
          isDisabled={irreversibleSubmit}
          label={text("policy")}
          labelPlacement="outside"
          selectedKeys={[draft.conflictPolicy]}
          size="sm"
          onSelectionChange={(keys) => {
            const value = Array.from(keys)[0];

            if (value === "inherit" || value === "ask" || value === "overwrite")
              setMoveDraftPolicy(value);
          }}
        />
        <div className="move-panel-actions">
          {draft.phase === "unknown" && (
            <Button size="sm" type="button" variant="flat" onPress={hideUnknownMoveDraft}>
              {text("hideForNow")}
            </Button>
          )}
          {!irreversibleSubmit && (
            <Button size="sm" type="button" variant="flat" onPress={cancelMoveDraft}>
              {text("cancel")}
            </Button>
          )}
          <Button
            color="primary"
            isDisabled={
              loading ||
              !ids.length ||
              blocked ||
              !!draft.contextError ||
              (!!draft.error && draft.phase !== "unknown")
            }
            isLoading={draft.phase === "submitting"}
            size="sm"
            type="button"
            onPress={() => void submitMoveDraft()}
          >
            {draft.phase === "unknown"
              ? text("retrySubmission")
              : `${text("confirmMove")} ${ids.length}`}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
