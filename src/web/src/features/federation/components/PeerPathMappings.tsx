import type { PathMapping, Peer } from "../types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { buttonClass, ErrorNotice, fieldClass, primaryClass } from "./common";
import MappingTree from "./MappingTree";
import {
  readMappingDraft,
  writeMappingDraft,
  clearMappingDraft,
  draftSharingChanged,
} from "./mappingDrafts";

import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

/**
 * Where a device's shared storage roots are on this device, for playing and opening folders
 * here. Shown wherever this device can read that device: its card on the devices page, and
 * its panel on the device map.
 *
 * Saving sends the mappings it started from as well, so a change made meanwhile elsewhere is
 * refused rather than overwritten; replacing an existing mapping is asked about first.
 */
type Props = {
  peer: Peer;
  busy: boolean;
  onSave: (mappings: PathMapping[], expectedMappings: PathMapping[]) => Promise<boolean>;
  className?: string;
};
export default function PeerPathMappings(props: Props) {
  return <PeerPathMappingsEditor key={props.peer.nodeId} {...props} />;
}

function PeerPathMappingsEditor({
  peer,
  busy,
  onSave,
  className = "border-t border-default-200 pt-3",
}: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [mappings, setMappings] = useState(peer.pathMappings);
  const [dirty, setDirty] = useState(false);
  const [storedDraft, setStoredDraft] = useState(() => readMappingDraft(peer.nodeId));
  const [draftStorageFailed, setDraftStorageFailed] = useState(false);
  const [saveError, setSaveError] = useState<Error>();
  const [opened, setOpened] = useState(false);
  const [selectionNotice, setSelectionNotice] = useState<string>();

  useEffect(() => {
    if (!dirty) return;
    try {
      writeMappingDraft(peer, mappings);
      setDraftStorageFailed(false);
    } catch {
      setDraftStorageFailed(true);
    }
  }, [mappings, dirty, peer.nodeId, peer.outboundGrant?.grantId, peer.outboundGrant?.revision]);
  const [mappingReview, setMappingReview] = useState<{
    proposed: PathMapping[];
    baseline: string;
  }>();
  const [roots, setRoots] = useState<{ sourceRootId: string; name: string }[]>();
  const mounted = useRef(true);

  useEffect(() => {
    mounted.current = true;

    return () => {
      mounted.current = false;
    };
  }, []);

  useEffect(() => {
    if (!dirty) setMappings(peer.pathMappings);
  }, [peer.pathMappings, dirty]);
  const update = (index: number, patch: Partial<PathMapping>) => {
    setMappingReview(undefined);
    setDirty(true);
    setMappings((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  };

  const mappingKey = (rows: PathMapping[]) =>
    JSON.stringify([...rows].sort((a, b) => a.sourceRootId.localeCompare(b.sourceRootId)));
  const conflicts = (proposed: PathMapping[]) =>
    peer.pathMappings.filter(
      (existing) =>
        proposed.find((row) => row.sourceRootId === existing.sourceRootId)?.localPath !==
        existing.localPath,
    );
  const persistMappings = async (proposed: PathMapping[]) => {
    setSaveError(undefined);
    try {
      if (!(await onSave(proposed, peer.pathMappings)) || !mounted.current) return;
      setMappingReview(undefined);
      setMappings(proposed);
      setDirty(false);
      setStoredDraft(undefined);
      try {
        clearMappingDraft(peer.nodeId);
      } catch {
        setDraftStorageFailed(true);
      }
    } catch (error) {
      if (mounted.current) setSaveError(error instanceof Error ? error : new Error(String(error)));
    }
  };
  const reviewMappings = () => {
    const proposed = mappings.map((mapping) => ({
      sourceRootId: mapping.sourceRootId.trim(),
      localPath: mapping.localPath.trim(),
    }));

    if (conflicts(proposed).length)
      setMappingReview({ proposed, baseline: mappingKey(peer.pathMappings) });
    else void persistMappings(proposed);
  };
  const applyReview = (keepExisting: boolean) => {
    if (!mappingReview) return;
    // A refreshed status invalidates the previous decision; show the latest conflicts first.
    if (mappingReview.baseline !== mappingKey(peer.pathMappings)) {
      setMappingReview({ ...mappingReview, baseline: mappingKey(peer.pathMappings) });

      return;
    }
    const preserved = keepExisting ? conflicts(mappingReview.proposed) : [];
    const proposed = [
      ...mappingReview.proposed.filter(
        (row) => !preserved.some((existing) => existing.sourceRootId === row.sourceRootId),
      ),
      ...preserved,
    ];

    void persistMappings(proposed);
  };

  return (
    <details
      className={className}
      onToggle={(event) => {
        setOpened(event.currentTarget.open);
      }}
    >
      <summary className="cursor-pointer text-sm font-medium">
        {t("federation.mappings.title")}
      </summary>
      <p className="mt-2 text-xs text-default-500">{t("federation.mappings.tip")}</p>
      <ErrorNotice error={saveError} />
      {storedDraft && !dirty && (
        <div className="mt-2 rounded-lg bg-default-100 p-2 text-xs">
          <p>
            {t(
              draftSharingChanged(storedDraft, peer)
                ? "federation.mappings.draft.changed"
                : "federation.mappings.draft.found",
            )}
          </p>
          <div className="mt-1 flex gap-2">
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() => {
                setMappings(storedDraft.mappings);
                setDirty(true);
              }}
            >
              {t("federation.mappings.draft.restore")}
            </button>
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() => {
                try {
                  clearMappingDraft(peer.nodeId);
                  setStoredDraft(undefined);
                } catch {
                  setDraftStorageFailed(true);
                }
              }}
            >
              {t("federation.mappings.draft.discard")}
            </button>
          </div>
        </div>
      )}
      {dirty && (
        <div className="mt-2 flex flex-wrap items-center gap-2 text-xs">
          <p role="status">
            {t(
              draftStorageFailed
                ? "federation.mappings.draft.unavailable"
                : "federation.mappings.draft.saved",
            )}
          </p>
          <button
            className="text-primary"
            disabled={busy}
            type="button"
            onClick={() => {
              setMappingReview(undefined);
              setMappings(peer.pathMappings);
              setDirty(false);
              setStoredDraft(undefined);
              try {
                clearMappingDraft(peer.nodeId);
              } catch {
                setDraftStorageFailed(true);
              }
            }}
          >
            {t("federation.mappings.draft.discard")}
          </button>
        </div>
      )}
      <MappingTree
        active={opened}
        busy={busy}
        mappings={mappings}
        nodeId={peer.nodeId}
        onNodes={(nodes) =>
          setRoots((previous) => [
            ...new Map(
              [...(previous ?? []), ...nodes].map((node) => [node.sourceRootId, node]),
            ).values(),
          ])
        }
        onSelect={(node, label) => {
          setRoots((previous) => [
            ...new Map(
              [...(previous ?? []), { sourceRootId: node.sourceRootId, name: label }].map(
                (item) => [item.sourceRootId, item],
              ),
            ).values(),
          ]);
          if (mappings.some((mapping) => mapping.sourceRootId === node.sourceRootId)) {
            setSelectionNotice(t("federation.mappings.tree.alreadyMapped"));

            return;
          }
          setMappingReview(undefined);
          setDirty(true);
          setMappings((previous) => [
            ...previous,
            { sourceRootId: node.sourceRootId, localPath: "" },
          ]);
          setSelectionNotice(t("federation.mappings.tree.added"));
        }}
      />
      {selectionNotice && (
        <p className="mt-2 text-xs" role="status">
          {selectionNotice}
        </p>
      )}
      <div className="mt-3 space-y-2">
        {mappings.map((mapping, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <div className="space-y-1 text-xs">
              <span>{t("federation.mappings.root")}</span>
              <select
                aria-label={t("federation.mappings.root")}
                className={fieldClass}
                disabled={busy}
                value={mapping.sourceRootId}
                onChange={(event) => update(index, { sourceRootId: event.target.value })}
              >
                <option value="">{t("federation.mappings.selectRoot")}</option>
                {mapping.sourceRootId &&
                  !roots?.some((root) => root.sourceRootId === mapping.sourceRootId) && (
                    <option value={mapping.sourceRootId}>
                      {t("federation.mappings.unavailableRoot")}
                    </option>
                  )}
                {roots?.map((root) => (
                  <option key={root.sourceRootId} value={root.sourceRootId}>
                    {root.name}
                  </option>
                ))}
              </select>
              <details>
                <summary className="cursor-pointer text-default-500">
                  {t("federation.mappings.manual")}
                </summary>
                <input
                  aria-label={t("federation.mappings.rootId")}
                  className={fieldClass}
                  disabled={busy}
                  value={mapping.sourceRootId}
                  onChange={(event) => update(index, { sourceRootId: event.target.value })}
                />
              </details>
            </div>
            <div className="space-y-1 text-xs">
              <span>{t("federation.mappings.local")}</span>
              <div className="flex items-center gap-1">
                <input
                  aria-label={t("federation.mappings.local")}
                  className={fieldClass}
                  disabled={busy}
                  placeholder="/Volumes/Media"
                  value={mapping.localPath}
                  onChange={(event) => update(index, { localPath: event.target.value })}
                />
                <button
                  aria-label={t("federation.mappings.chooseLocal")}
                  className={`${buttonClass} shrink-0 !px-2`}
                  disabled={busy}
                  type="button"
                  onClick={() =>
                    createPortal(FileSystemSelectorModal, {
                      targetType: "folder",
                      startPath: mapping.localPath || undefined,
                      defaultSelectedPath: mapping.localPath || undefined,
                      onSelected: (entry) => {
                        if (mounted.current) update(index, { localPath: entry.path });
                      },
                    })
                  }
                >
                  {t("federation.mappings.chooseLocal")}
                </button>
              </div>
            </div>
            <button
              aria-label={t("federation.mappings.remove")}
              className={`${buttonClass} self-end`}
              disabled={busy}
              type="button"
              onClick={() => {
                setMappingReview(undefined);
                setDirty(true);
                setMappings((rows) => rows.filter((_, i) => i !== index));
              }}
            >
              ×
            </button>
          </div>
        ))}
      </div>
      <div className="mt-3 flex gap-2">
        <button
          className={buttonClass}
          disabled={busy}
          type="button"
          onClick={() => {
            setMappingReview(undefined);
            setDirty(true);
            setMappings((rows) => [...rows, { sourceRootId: "", localPath: "" }]);
          }}
        >
          {t("federation.mappings.add")}
        </button>
        <button
          className={primaryClass}
          disabled={
            busy ||
            !dirty ||
            mappings.some((mapping) => !mapping.sourceRootId.trim() || !mapping.localPath.trim())
          }
          type="button"
          onClick={reviewMappings}
        >
          {t("federation.save")}
        </button>
      </div>
      {mappingReview && (
        <section
          aria-label={t("federation.mappings.conflictTitle")}
          className="mt-3 space-y-3 rounded-lg border border-warning/40 bg-warning/5 p-3"
          role="alertdialog"
        >
          <h4 className="font-medium">{t("federation.mappings.conflictTitle")}</h4>
          <p className="text-sm">{t("federation.mappings.conflictTip")}</p>
          {mappingReview.baseline !== mappingKey(peer.pathMappings) && (
            <p className="text-sm text-warning" role="status">
              {t("federation.mappings.changedDuringReview")}
            </p>
          )}
          <ul className="space-y-2 text-xs">
            {conflicts(mappingReview.proposed).map((existing) => (
              <li key={existing.sourceRootId} className="space-y-1 break-all">
                <p className="font-medium">
                  {roots?.find((root) => root.sourceRootId === existing.sourceRootId)?.name ??
                    t("federation.mappings.unavailableRoot")}
                </p>
                <p>
                  {existing.localPath} →{" "}
                  {mappingReview.proposed.find((row) => row.sourceRootId === existing.sourceRootId)
                    ?.localPath ?? t("federation.mappings.removed")}
                </p>
              </li>
            ))}
          </ul>
          <div className="flex flex-wrap gap-2">
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() => applyReview(true)}
            >
              {t("federation.mappings.keep")}
            </button>
            <button
              className={primaryClass}
              disabled={busy}
              type="button"
              onClick={() => applyReview(false)}
            >
              {t("federation.mappings.replace")}
            </button>
            <button
              className={buttonClass}
              disabled={busy}
              type="button"
              onClick={() => setMappingReview(undefined)}
            >
              {t("federation.cancel")}
            </button>
          </div>
        </section>
      )}
    </details>
  );
}
