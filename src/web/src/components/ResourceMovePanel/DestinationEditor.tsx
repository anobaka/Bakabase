import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { MoveDestination } from "./types";

import { useEffect, useMemo, useState } from "react";

import { useMoveText } from "./messages";

import { Button, Checkbox, Input, Modal } from "@/components/bakaui";
import { FileExplorer } from "@/components/FileExplorer";
import BApi from "@/sdk/BApi";
import { IwFsType } from "@/sdk/constants";
import {
  normalizedMovePath,
  updateMovePanelOptions,
  useResourceMovePanelStore,
} from "@/stores/resourceMovePanel";

export default function DestinationEditor({
  destination,
  onClose,
}: {
  destination?: MoveDestination;
  onClose: () => void;
}) {
  const text = useMoveText();
  const tabId = useResourceMovePanelStore((s) =>
    s.explicitPayload ? s.explicitPayload.sourceTabId : s.context.tabId,
  );
  const [path, setPath] = useState(destination?.path ?? "");
  const [name, setName] = useState(destination?.name ?? "");
  const [scope, setScope] = useState<"global" | "tab">(
    destination?.scope ?? (tabId ? "tab" : "global"),
  );
  const [roots, setRoots] = useState<string[]>();
  const [error, setError] = useState<string>();
  const [saving, setSaving] = useState(false);
  const filter = useMemo(() => ({ custom: (e: Entry) => e.isDirectoryOrDrive }), []);

  useEffect(() => {
    void BApi.pathMark
      .getAllPathMarkPaths()
      .then((r) => setRoots(r.data ?? []))
      .catch(() => setRoots([]));
  }, []);
  const save = async () => {
    if (saving) return;
    setSaving(true);
    setError(undefined);
    try {
      const normalized = normalizedMovePath(path.trim());
      const rsp = await BApi.file.getIwFsEntry({ path: normalized }, { showErrorToast: false });

      if (rsp.code || !rsp.data || ![IwFsType.Directory, IwFsType.Drive].includes(rsp.data.type))
        throw new Error(rsp.message || text("folderRequired"));
      const ownerTab = scope === "tab" ? (destination?.tabId ?? tabId) : undefined;

      if (scope === "tab" && !ownerTab) throw new Error(text("scopeHint"));
      await updateMovePanelOptions((options) => {
        const duplicate = options.destinations.find(
          (d) =>
            !d.isDeleted &&
            d.id !== destination?.id &&
            d.scope === scope &&
            d.tabId === ownerTab &&
            normalizedMovePath(d.path) === normalized,
        );

        if (duplicate) {
          // Promoting a local bookmark to a global one merges the duplicate configuration.
          if (destination?.scope === "tab" && scope === "global")
            return {
              ...options,
              destinations: options.destinations.map((d) =>
                d.id === destination.id ? { ...d, isDeleted: true } : d,
              ),
            };
          throw new Error(text("duplicate"));
        }
        const updated: MoveDestination = {
          id: destination?.id ?? crypto.randomUUID(),
          name: name.trim() || undefined,
          path: rsp.data!.path || normalized,
          scope,
          tabId: ownerTab,
          order:
            destination?.order ??
            Math.max(
              -1,
              ...options.destinations
                .filter((d) => d.scope === scope && d.tabId === ownerTab)
                .map((d) => d.order),
            ) + 1,
          isDeleted: false,
        };

        return {
          ...options,
          destinations: destination
            ? options.destinations.map((d) => (d.id === destination.id ? updated : d))
            : [...options.destinations, updated],
        };
      });
      onClose();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setSaving(false);
    }
  };

  return (
    <Modal
      visible
      classNames={{ base: "max-w-4xl w-[92vw] h-[82vh]", body: "min-h-0 overflow-hidden" }}
      footer={false}
      hideCloseButton={saving}
      isDismissable={!saving}
      isKeyboardDismissDisabled={saving}
      title={text(destination ? "edit" : "add")}
      onClose={onClose}
    >
      <div data-resource-move-panel className="move-panel-editor">
        <Input
          isDisabled={saving}
          label={text("name")}
          size="sm"
          value={name}
          onValueChange={setName}
        />
        <Input
          isDisabled={saving}
          label={text("path")}
          placeholder="/media/library"
          size="sm"
          value={path}
          onValueChange={setPath}
        />
        <Checkbox
          isDisabled={saving || (!tabId && !destination?.tabId)}
          isSelected={scope === "global"}
          size="sm"
          onValueChange={(isGlobal) => setScope(isGlobal ? "global" : "tab")}
        >
          {text("globalDestination")}
        </Checkbox>
        {!tabId && !destination?.tabId && <p className="move-panel-muted">{text("scopeHint")}</p>}
        <div className="move-panel-folder-picker">
          {roots && (
            <FileExplorer
              expandable
              capabilities={saving ? [] : ["select", "enter-directory"]}
              filter={filter}
              keyboard={false}
              rootPath={destination?.path}
              rootPaths={roots.length ? roots : undefined}
              selectable="single"
              onSelected={(entries) => {
                if (!saving && entries[0]) setPath(entries[0].path);
              }}
            />
          )}
        </div>
        {error && (
          <div className="move-panel-error" role="alert">
            {error}
          </div>
        )}
        <div className="move-panel-actions">
          <Button isDisabled={saving} size="sm" variant="light" onPress={onClose}>
            {text("cancel")}
          </Button>
          <Button
            color="primary"
            isDisabled={!path.trim() || saving}
            isLoading={saving}
            size="sm"
            onPress={() => void save()}
          >
            {text("save")}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
