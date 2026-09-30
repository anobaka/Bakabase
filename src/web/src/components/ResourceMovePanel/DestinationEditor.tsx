import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { MoveDestination } from "./types";

import { useEffect, useMemo, useState } from "react";

import { useMoveText } from "./messages";

import { Modal } from "@/components/bakaui";
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
      title={text(destination ? "edit" : "add")}
      onClose={onClose}
    >
      <div data-resource-move-panel className="move-panel-editor">
        <label>
          {text("name")}
          <input value={name} onChange={(e) => setName(e.target.value)} />
        </label>
        <label>
          {text("path")}
          <input
            placeholder="/media/library"
            value={path}
            onChange={(e) => setPath(e.target.value)}
          />
        </label>
        <label>
          {text("scope")}
          <select value={scope} onChange={(e) => setScope(e.target.value as "global" | "tab")}>
            <option value="global">{text("global")}</option>
            {(tabId || destination?.tabId) && <option value="tab">{text("local")}</option>}
          </select>
        </label>
        <div className="move-panel-folder-picker">
          {roots && (
            <FileExplorer
              expandable
              capabilities={["select", "enter-directory"]}
              filter={filter}
              keyboard={false}
              rootPath={destination?.path}
              rootPaths={roots.length ? roots : undefined}
              selectable="single"
              onSelected={(entries) => {
                if (entries[0]) setPath(entries[0].path);
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
          <button disabled={saving} type="button" onClick={onClose}>
            {text("cancel")}
          </button>
          <button
            className="primary"
            disabled={!path.trim() || saving}
            type="button"
            onClick={() => void save()}
          >
            {text("save")}
          </button>
        </div>
      </div>
    </Modal>
  );
}
