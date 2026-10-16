import type { ManagedServer, ManagedServerPathMapping } from "../types";
import type { ManagedMappingTreeLoader } from "./ManagedMappingTree";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { notifyPathMappingsSaved } from "../managedPathMappingTarget";

import { buttonClass, ErrorNotice, fieldClass, primaryClass } from "./common";
import ManagedMappingTree from "./ManagedMappingTree";
import {
  clearMappingDraft,
  readMappingDraft,
  sameMappings,
  writeMappingDraft,
} from "./managedMappingDrafts";

import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

/**
 * Where a managed server's library folders are on this computer — what its relay uses to play
 * files and open folders here. Shown on the server's card on the devices page and in its
 * panel on the device map.
 */
export type ManagedPathMappingSnapshot = Pick<
  ManagedServer,
  "serverId" | "name" | "pairedAt" | "pathMappings"
>;

export type ManagedPathMappingEditorProps = {
  server: ManagedPathMappingSnapshot;
  busy: boolean;
  onSave: (
    mappings: ManagedServerPathMapping[],
    expectedMappings?: ManagedServerPathMapping[],
  ) => Promise<boolean>;
  className?: string;
  /** Expanded forms (including dialogs) use the same editor and draft as device management. */
  expanded?: boolean;
  loadTree?: ManagedMappingTreeLoader;
  /** Undefined uses the host folder selector; false disables it for a remote host. */
  chooseLocal?: false | ((path: string, onSelected: (path: string) => void) => void);
  targetKind?: "manager" | "relay";
};
export default function ManagedServerPathMappings(props: ManagedPathMappingEditorProps) {
  return (
    <ManagedMappingEditor key={`${props.server.serverId}:${props.server.pairedAt}`} {...props} />
  );
}
export function ManagedMappingEditor({
  server,
  busy,
  onSave,
  className = "border-t border-default-200 pt-3",
  expanded = false,
  loadTree,
  chooseLocal,
  targetKind = "manager",
}: ManagedPathMappingEditorProps) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [mappings, setMappings] = useState(server.pathMappings);
  const [baseline, setBaseline] = useState(server.pathMappings);
  const [dirty, setDirty] = useState(false);
  const [storedDraft, setStoredDraft] = useState(() => readMappingDraft(server.serverId));
  const [storageFailed, setStorageFailed] = useState(false);
  const [opened, setOpened] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<Error>();
  const [notice, setNotice] = useState<string>();
  const [review, setReview] = useState<ManagedServerPathMapping[]>();
  const mounted = useRef(true);
  const disabled = busy || saving;

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
    };
  }, []);
  useEffect(() => {
    if (!dirty) {
      setMappings(server.pathMappings);
      setBaseline(server.pathMappings);
    }
  }, [server.pathMappings, dirty]);
  useEffect(() => {
    if (!dirty) return;
    try {
      writeMappingDraft(server, mappings, baseline);
      setStorageFailed(false);
    } catch {
      setStorageFailed(true);
    }
  }, [mappings, baseline, dirty, server.serverId, server.pairedAt]);
  const edit = (next: (rows: ManagedServerPathMapping[]) => ManagedServerPathMapping[]) => {
    setReview(undefined);
    setError(undefined);
    setDirty(true);
    setMappings(next);
  };
  const clearDraft = () => {
    try {
      clearMappingDraft(server.serverId);
      setStorageFailed(false);
    } catch {
      setStorageFailed(true);
    }
    setStoredDraft(undefined);
  };
  const discard = () => {
    setReview(undefined);
    setError(undefined);
    setNotice(undefined);
    setMappings(server.pathMappings);
    setBaseline(server.pathMappings);
    setDirty(false);
    clearDraft();
  };
  const persist = async (expected: ManagedServerPathMapping[]) => {
    const proposed = mappings.map((row) => ({
      serverPath: row.serverPath.trim(),
      localPath: row.localPath.trim(),
    }));

    setSaving(true);
    setError(undefined);
    try {
      if ((await onSave(proposed, expected)) && mounted.current) {
        setMappings(proposed);
        setBaseline(proposed);
        setDirty(false);
        setReview(undefined);
        clearDraft();
        notifyPathMappingsSaved({ serverId: server.serverId, kind: targetKind });
      }
    } catch (failure) {
      if (mounted.current)
        setError(failure instanceof Error ? failure : new Error(String(failure)));
    } finally {
      if (mounted.current) setSaving(false);
    }
  };
  const save = () => {
    if (!sameMappings(baseline, server.pathMappings)) {
      setReview(server.pathMappings);

      return;
    }
    void persist(baseline);
  };
  const replaceCurrent = () => {
    if (!review) return;
    if (!sameMappings(review, server.pathMappings)) {
      setReview(server.pathMappings);

      return;
    }
    void persist(review);
  };
  const draftChanged =
    storedDraft &&
    (storedDraft.pairedAt !== server.pairedAt ||
      !sameMappings(storedDraft.baseMappings, server.pathMappings));

  const body = (
    <>
      <p className="mt-2 text-xs text-default-500">{t("federation.servers.mappings.tip")}</p>
      <ErrorNotice error={error} />
      {storedDraft && !dirty && (
        <div className="mt-2 rounded-lg bg-default-100 p-2 text-xs">
          <p>
            {t(
              draftChanged
                ? "federation.managedMappings.draft.changed"
                : "federation.managedMappings.draft.found",
            )}
          </p>
          <div className="mt-1 flex gap-2">
            <button
              className={buttonClass}
              disabled={disabled}
              type="button"
              onClick={() => {
                setMappings(storedDraft.mappings);
                setBaseline(storedDraft.baseMappings);
                setDirty(true);
              }}
            >
              {t("federation.managedMappings.draft.restore")}
            </button>
            <button className={buttonClass} disabled={disabled} type="button" onClick={clearDraft}>
              {t("federation.managedMappings.draft.discard")}
            </button>
          </div>
        </div>
      )}
      {dirty && (
        <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
          <p role="status">
            {t(
              storageFailed
                ? "federation.managedMappings.draft.unavailable"
                : "federation.managedMappings.draft.saved",
            )}
          </p>
          <button className="text-primary" disabled={disabled} type="button" onClick={discard}>
            {t("federation.managedMappings.draft.discard")}
          </button>
        </div>
      )}
      <ManagedMappingTree
        active={expanded || opened}
        busy={disabled}
        loadPage={loadTree}
        mappings={mappings}
        serverId={server.serverId}
        onSelect={(node) => {
          if (mappings.some((row) => row.serverPath === node.serverPath)) {
            setNotice(t("federation.managedMappings.tree.alreadyMapped"));

            return;
          }
          edit((rows) => [...rows, { serverPath: node.serverPath, localPath: "" }]);
          setNotice(t("federation.managedMappings.tree.added"));
        }}
      />
      {notice && (
        <p className="mt-2 text-xs" role="status">
          {notice}
        </p>
      )}
      <div className="mt-3 space-y-2">
        {mappings.map((mapping, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <label className="space-y-1 text-xs">
              <span>{t("federation.servers.mappings.server")}</span>
              <input
                className={fieldClass}
                disabled={disabled}
                placeholder={"D:\\Media"}
                value={mapping.serverPath}
                onChange={(event) =>
                  edit((rows) =>
                    rows.map((row, i) =>
                      i === index ? { ...row, serverPath: event.target.value } : row,
                    ),
                  )
                }
              />
            </label>
            <label className="space-y-1 text-xs">
              <span>{t("federation.servers.mappings.local")}</span>
              <input
                className={fieldClass}
                disabled={disabled}
                placeholder="/Volumes/Media"
                value={mapping.localPath}
                onChange={(event) =>
                  edit((rows) =>
                    rows.map((row, i) =>
                      i === index ? { ...row, localPath: event.target.value } : row,
                    ),
                  )
                }
              />
            </label>
            <div className="flex gap-1 self-end">
              {chooseLocal !== false && (
                <button
                  aria-label={t("federation.managedMappings.chooseLocal")}
                  className={buttonClass}
                  disabled={disabled}
                  type="button"
                  onClick={() => {
                    const selected = (path: string) => {
                      if (mounted.current)
                        edit((rows) =>
                          rows.map((row, i) => (i === index ? { ...row, localPath: path } : row)),
                        );
                    };

                    if (chooseLocal) chooseLocal(mapping.localPath, selected);
                    else
                      createPortal(FileSystemSelectorModal, {
                        targetType: "folder",
                        startPath: mapping.localPath || undefined,
                        defaultSelectedPath: mapping.localPath || undefined,
                        onSelected: (entry) => selected(entry.path),
                      });
                  }}
                >
                  {t("federation.managedMappings.chooseLocal")}
                </button>
              )}
              <button
                aria-label={t("federation.servers.mappings.remove")}
                className={`${buttonClass} self-end`}
                disabled={disabled}
                type="button"
                onClick={() => edit((rows) => rows.filter((_, i) => i !== index))}
              >
                ×
              </button>
            </div>
          </div>
        ))}
      </div>
      <div className="mt-3 flex gap-2">
        <button
          className={buttonClass}
          disabled={disabled}
          type="button"
          onClick={() => edit((rows) => [...rows, { serverPath: "", localPath: "" }])}
        >
          {t("federation.servers.mappings.add")}
        </button>
        <button
          className={primaryClass}
          disabled={
            disabled ||
            !dirty ||
            mappings.some((row) => !row.serverPath.trim() || !row.localPath.trim())
          }
          type="button"
          onClick={() => void save()}
        >
          {t("federation.servers.mappings.save")}
        </button>
      </div>
      {review && (
        <section
          className="mt-3 space-y-2 rounded-lg border border-warning/40 bg-warning/5 p-3 text-sm"
          role="alert"
        >
          <p>{t("federation.managedMappings.conflict")}</p>
          {!sameMappings(review, server.pathMappings) && (
            <p>{t("federation.managedMappings.conflictAgain")}</p>
          )}
          <p className="font-medium">{t("federation.managedMappings.current")}</p>
          <ul className="max-h-40 overflow-auto text-xs">
            {server.pathMappings.map((row, index) => (
              <li key={index} className="break-all">
                {row.serverPath} → {row.localPath}
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap gap-2">
            <button
              className={primaryClass}
              disabled={disabled}
              type="button"
              onClick={replaceCurrent}
            >
              {t("federation.managedMappings.replace")}
            </button>
            <button className={buttonClass} disabled={disabled} type="button" onClick={discard}>
              {t("federation.managedMappings.useCurrent")}
            </button>
            <button
              className={buttonClass}
              disabled={disabled}
              type="button"
              onClick={() => setReview(undefined)}
            >
              {t("federation.cancel")}
            </button>
          </div>
        </section>
      )}
    </>
  );

  return expanded ? (
    <div className={className}>{body}</div>
  ) : (
    <details className={className} onToggle={(event) => setOpened(event.currentTarget.open)}>
      <summary className="cursor-pointer text-sm font-medium">
        {t("federation.servers.mappings.title")}
        {server.pathMappings.length > 0 && (
          <span className="ml-2 text-xs text-default-500">{server.pathMappings.length}</span>
        )}
      </summary>
      {body}
    </details>
  );
}
