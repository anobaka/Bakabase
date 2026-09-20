import type {
  FederatedAsset,
  FederatedResourceDetail,
  PlaybackSession,
  ResourceRef,
} from "../types";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";

import { federationResourceApi, localMediaUrl } from "../resourceApi";
import { FederationError, isAbort } from "../transport";
import { resourceKey, sameResource } from "../types";

import { buttonClass, ErrorNotice, panelClass, SourceBadge } from "./common";

import { PropertyValueScopeLabel } from "@/sdk/constants";

export function displayValue(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (Array.isArray(value)) return value.map(displayValue).join(", ");
  if (typeof value === "object") {
    return Object.entries(value)
      .map(([key, item]) => `${key}: ${displayValue(item)}`)
      .join(" · ");
  }

  return String(value);
}

/** The server already resolved property dictionaries and scopes. This renderer has no local property store. */
export default function ResourceDetail({
  resourceRef,
  localNodeId,
  localEpoch,
  onClose,
}: {
  resourceRef: ResourceRef;
  localNodeId: string;
  localEpoch: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const [detail, setDetail] = useState<FederatedResourceDetail>();
  const [error, setError] = useState<Error>();
  const [revision, setRevision] = useState(0);
  const [playing, setPlaying] = useState(false);
  const [playError, setPlayError] = useState<Error>();
  const [launched, setLaunched] = useState(false);
  const [media, setMedia] = useState<{
    asset: FederatedAsset;
    session: PlaybackSession;
    url: string;
  }>();
  const playbackRequest = useRef<AbortController>();
  const generation = useRef(0);
  const key = resourceKey(resourceRef);

  useEffect(() => {
    const current = ++generation.current;
    const controller = new AbortController();

    playbackRequest.current?.abort();
    setDetail(undefined);
    setError(undefined);
    setMedia(undefined);
    setPlayError(undefined);
    setLaunched(false);
    setPlaying(false);
    void federationResourceApi
      .detail(resourceRef, controller.signal)
      .then((response) => {
        if (current !== generation.current) return;
        const found = response.resources.find((resource) =>
          sameResource(resource.ref, resourceRef),
        );

        if (!found) throw new FederationError("ResourceGone", "", 404);
        setDetail(found);
      })
      .catch((cause) => {
        if (current === generation.current && !isAbort(cause)) setError(asError(cause));
      });

    return () => {
      generation.current += 1;
      controller.abort();
      playbackRequest.current?.abort();
    };
  }, [key, revision]);

  const play = async (asset: FederatedAsset, mode: "preview" | "player") => {
    const current = generation.current;

    playbackRequest.current?.abort();
    const controller = new AbortController();

    playbackRequest.current = controller;
    setPlaying(true);
    setPlayError(undefined);
    setMedia(undefined);
    setLaunched(false);
    try {
      const session = await federationResourceApi.playback(
        resourceRef,
        asset.assetId,
        mode,
        controller.signal,
      );

      if (current !== generation.current) return;
      if (mode === "player") {
        if (!session.launched) throw new FederationError("PlayerUnavailable", "", 409);
        setLaunched(true);
      } else {
        if (!session.url) throw new FederationError("AssetGone", "", 404);
        setMedia({ asset, session, url: localMediaUrl(session.url) });
      }
    } catch (cause) {
      if (current === generation.current && !isAbort(cause)) setPlayError(asError(cause));
    } finally {
      if (current === generation.current) setPlaying(false);
    }
  };

  const isLocal = resourceRef.nodeId === localNodeId && resourceRef.libraryEpoch === localEpoch;

  return (
    <section
      aria-label={t("federation.detail.title")}
      className={`${panelClass} min-w-0 space-y-4`}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-semibold uppercase tracking-wide text-default-500">
            {t("federation.readOnly")}
          </p>
          <h2 className="mt-1 break-words text-xl font-semibold">
            {detail?.displayName || `#${resourceRef.resourceId}`}
          </h2>
        </div>
        <button
          aria-label={t("federation.close")}
          className={buttonClass}
          type="button"
          onClick={onClose}
        >
          ×
        </button>
      </div>
      <ErrorNotice error={error} onRetry={() => setRevision((value) => value + 1)} />
      {!detail && !error && <p role="status">{t("federation.loading")}</p>}
      {detail && (
        <>
          <SourceBadge label={detail.ownerLabel || resourceRef.nodeId} local={isLocal} />
          {detail.fileName && (
            <p className="break-all text-sm text-default-500">{detail.fileName}</p>
          )}
          {detail.unavailableReason && (
            <p className="rounded-lg bg-warning/10 p-3 text-sm">{detail.unavailableReason}</p>
          )}
          <p className="text-sm text-default-500">{t("federation.detail.readOnly")}</p>
          {isLocal && (
            <Link
              className={buttonClass}
              to={`/resource?${new URLSearchParams({ inspect: String(resourceRef.resourceId), node: resourceRef.nodeId, epoch: resourceRef.libraryEpoch })}`}
            >
              {t("federation.manageLocal")}
            </Link>
          )}
          {detail.properties.length > 0 && (
            <dl className="divide-y divide-default-200">
              {detail.properties.map((property, index) => (
                <div key={`${property.label}:${property.scope}:${index}`} className="py-3">
                  <dt className="text-xs font-medium text-default-500">
                    {property.label}
                    {PropertyValueScopeLabel[
                      property.scope as keyof typeof PropertyValueScopeLabel
                    ] && (
                      <span className="ml-2 opacity-70">
                        {t(
                          `federation.propertyScope.${
                            PropertyValueScopeLabel[
                              property.scope as keyof typeof PropertyValueScopeLabel
                            ]
                          }`,
                        )}
                      </span>
                    )}
                  </dt>
                  <dd className="mt-1 whitespace-pre-wrap break-words text-sm">
                    {displayValue(property.value)}
                  </dd>
                </div>
              ))}
            </dl>
          )}
          {detail.collections.length > 0 && (
            <div>
              <h3 className="mb-2 text-sm font-medium">{t("federation.detail.collections")}</h3>
              <div className="flex flex-wrap gap-2">
                {detail.collections.map((collection, index) => (
                  <span
                    key={`${collection.name}:${index}`}
                    className="rounded-md bg-default-100 px-2 py-1 text-xs"
                  >
                    {collection.name}
                  </span>
                ))}
              </div>
            </div>
          )}
          {detail.externalIdentities.length > 0 && (
            <div>
              <h3 className="mb-2 text-sm font-medium">{t("federation.detail.identities")}</h3>
              {detail.externalIdentities.map((identity, index) => (
                <p
                  key={`${identity.provider}:${identity.externalId}:${index}`}
                  className="break-all text-sm text-default-500"
                >
                  {identity.provider} · {identity.externalId}
                </p>
              ))}
            </div>
          )}
          {detail.sources.length > 0 && (
            <div>
              <h3 className="mb-2 text-sm font-medium">{t("federation.sourceKinds")}</h3>
              {detail.sources.map((source, index) => (
                <p key={`${source.kind}:${index}`} className="break-all text-sm text-default-500">
                  {source.label || t(`federation.source.${source.kind}`)}
                  {source.url && <span className="ml-2">{source.url}</span>}
                </p>
              ))}
            </div>
          )}
          <div className="border-t border-default-200 pt-4">
            <h3 className="font-medium">{t("federation.detail.media")}</h3>
            <p className="mt-1 text-xs text-default-500">{t("federation.detail.mediaTip")}</p>
            {!detail.assets.length && (
              <p className="mt-3 text-sm text-default-500">{t("federation.detail.noMedia")}</p>
            )}
            <div className="mt-3 space-y-3">
              {detail.assets.map((asset) => (
                <div key={asset.assetId} className="rounded-lg bg-default-50 p-3">
                  <p className="mb-2 break-all text-sm">{asset.fileName}</p>
                  <div className="flex flex-wrap gap-2">
                    <button
                      className={buttonClass}
                      disabled={playing}
                      type="button"
                      onClick={() => void play(asset, "preview")}
                    >
                      {t("federation.preview")}
                    </button>
                    {asset.kind !== "image" && (
                      <button
                        className={buttonClass}
                        disabled={playing}
                        type="button"
                        onClick={() => void play(asset, "player")}
                      >
                        {t("federation.playHere")}
                      </button>
                    )}
                  </div>
                </div>
              ))}
            </div>
            {playing && (
              <p className="mt-3 text-sm" role="status">
                {t("federation.mediaPreparing")}
              </p>
            )}
            {launched && (
              <p className="mt-3 text-sm text-success" role="status">
                {t("federation.playerLaunched")}
              </p>
            )}
            <div className="mt-3">
              <ErrorNotice error={playError} />
            </div>
            {media && (
              <div className="mt-3 rounded-lg bg-black/5 p-2">
                {media.asset.kind === "image" ? (
                  // Images use short-lived local capability URLs, with no third-party image loader.
                  // eslint-disable-next-line @next/next/no-img-element
                  <img
                    alt={media.asset.fileName}
                    className="max-h-[60vh] w-full object-contain"
                    src={media.url}
                    onError={() => setPlayError(new FederationError("AssetGone", "", 404))}
                  />
                ) : media.asset.kind === "audio" ? (
                  <audio
                    controls
                    className="w-full"
                    src={media.url}
                    onError={() =>
                      setPlayError(new FederationError("MediaPlaybackFailed", "", 422))
                    }
                  >
                    <track kind="captions" />
                  </audio>
                ) : (
                  <video
                    controls
                    className="max-h-[60vh] w-full"
                    src={media.url}
                    onError={() =>
                      setPlayError(new FederationError("MediaPlaybackFailed", "", 422))
                    }
                  >
                    <track kind="captions" />
                  </video>
                )}
              </div>
            )}
          </div>
        </>
      )}
    </section>
  );
}

const asError = (error: unknown) => (error instanceof Error ? error : new Error(String(error)));
