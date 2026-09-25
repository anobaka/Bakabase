import type { PathMapping, Peer } from "../types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { federationPeerApi } from "../peerApi";

import { buttonClass, ErrorNotice, fieldClass, primaryClass } from "./common";

/**
 * Where a device's shared storage roots are on this device, for playing and opening folders
 * here. Shown wherever this device can read that device: its card on the devices page, and
 * its panel on the device map.
 *
 * Saving sends the mappings it started from as well, so a change made meanwhile elsewhere is
 * refused rather than overwritten; replacing an existing mapping is asked about first.
 */
export default function PeerPathMappings({
  peer,
  busy,
  onSave,
  className = "border-t border-default-200 pt-3",
}: {
  peer: Peer;
  busy: boolean;
  onSave: (mappings: PathMapping[], expectedMappings: PathMapping[]) => Promise<boolean>;
  className?: string;
}) {
  const { t } = useTranslation();
  const [mappings, setMappings] = useState(peer.pathMappings);
  const [dirty, setDirty] = useState(false);
  const [mappingReview, setMappingReview] = useState<{
    proposed: PathMapping[];
    baseline: string;
  }>();
  const [roots, setRoots] = useState<{ sourceRootId: string; name: string }[]>();
  const [rootsError, setRootsError] = useState<Error>();
  const [loadingRoots, setLoadingRoots] = useState(false);
  const rootRequest = useRef<AbortController>();

  useEffect(() => () => rootRequest.current?.abort(), []);
  const loadRoots = async () => {
    rootRequest.current?.abort();
    const request = new AbortController();

    rootRequest.current = request;
    setLoadingRoots(true);
    setRootsError(undefined);
    try {
      const result = await federationPeerApi.mappingRoots(peer.nodeId, request.signal);

      if (!request.signal.aborted) setRoots(result);
    } catch (cause) {
      if (!request.signal.aborted)
        setRootsError(cause instanceof Error ? cause : new Error(String(cause)));
    } finally {
      if (!request.signal.aborted) setLoadingRoots(false);
    }
  };

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
    if (await onSave(proposed, peer.pathMappings)) {
      setMappingReview(undefined);
      setMappings(proposed);
      setDirty(false);
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
        if (event.currentTarget.open && !roots && !loadingRoots) void loadRoots();
      }}
    >
      <summary className="cursor-pointer text-sm font-medium">
        {t("federation.mappings.title")}
      </summary>
      <p className="mt-2 text-xs text-default-500">{t("federation.mappings.tip")}</p>
      <ErrorNotice error={rootsError} onRetry={() => void loadRoots()} />
      {loadingRoots && (
        <p className="mt-2 text-xs" role="status">
          {t("federation.loading")}
        </p>
      )}
      <div className="mt-3 space-y-2">
        {mappings.map((mapping, index) => (
          <div key={index} className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <label className="space-y-1 text-xs">
              <span>{t("federation.mappings.root")}</span>
              <select
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
            </label>
            <label className="space-y-1 text-xs">
              <span>{t("federation.mappings.local")}</span>
              <input
                className={fieldClass}
                disabled={busy}
                placeholder="/Volumes/Media"
                value={mapping.localPath}
                onChange={(event) => update(index, { localPath: event.target.value })}
              />
            </label>
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
