"use client";

import type { BakabaseModulesPlayerAbstractionsModelsDomainBatchPlayCandidate as BatchPlayCandidate } from "@/sdk/Api";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineExport } from "react-icons/ai";

import {
  Button,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Tooltip,
  toast,
} from "@/components/bakaui";
import { useResourceApi, useResourceOwner } from "@/features/management/resourceContext";
import { BatchPlayFileSelectionMode } from "@/sdk/constants";

type Props = {
  playlistId: number;
};

/**
 * "Open the whole playlist with an external player" dropdown. Candidates are
 * fetched on open and annotated with how many of the playlist's files each
 * player can handle, so a mixed-type playlist shows e.g. "PotPlayer (12)" and
 * "foobar2000 (8)" instead of one misleading button.
 */
const BatchPlayButton = ({ playlistId }: Props) => {
  const BApi = useResourceApi();
  const owner = useResourceOwner();
  const { t } = useTranslation();

  const [candidates, setCandidates] = useState<BatchPlayCandidate[]>();
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string>();
  const requestVersion = useRef(0);
  const isManaged = !!owner && !owner.isLocal;
  useEffect(() => {
    ++requestVersion.current;
    setCandidates(undefined);
    setError(undefined);
    setLoading(false);

    return () => {
      ++requestVersion.current;
    };
  }, [BApi, playlistId]);
  const ownedResourceIds = async () => {
    const response = await BApi.playlist.getPlaylist(playlistId);
    if (response.code) throw new Error(response.message || String(response.code));
    if (!response.data) throw new Error(t("playlist.label.notFound"));
    const items = response.data.items ?? [];
    if (items.some((item) => !item.resourceId || !!item.file))
      throw new Error(t("management.playlist.fileItems"));
    return items.map((item) => item.resourceId!);
  };

  const loadCandidates = async () => {
    const version = ++requestVersion.current;
    setLoading(true);
    try {
      setError(undefined);
      const rsp = isManaged
        ? await BApi.player.getBatchPlayCandidates({ resourceIds: await ownedResourceIds() })
        : await BApi.player.getPlaylistBatchPlayCandidates(playlistId);

      if (rsp.code) throw new Error(rsp.message || String(rsp.code));
      if (version === requestVersion.current) setCandidates(rsp.data ?? []);
    } catch (failure) {
      if (version === requestVersion.current)
        setError(failure instanceof Error ? failure.message : String(failure));
    } finally {
      if (version === requestVersion.current) setLoading(false);
    }
  };

  const play = async (playerKey: string) => {
    if (!candidates?.some((candidate) => candidate.key === playerKey)) return;
    try {
      const rsp = isManaged
        ? await BApi.player.batchPlayResources({
            resourceIds: await ownedResourceIds(),
            playerKey,
            fileSelectionMode: BatchPlayFileSelectionMode.AllFiles,
          })
        : await BApi.player.batchPlayPlaylist(playlistId, { playerKey });

      if (rsp.code) throw new Error(rsp.message || String(rsp.code));
      if (!rsp.data) return;

      toast.success(
        t<string>("resource.contextMenu.batchPlay.success", {
          player: rsp.data.playerName,
          count: rsp.data.fileCount,
        }),
      );
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
    }
  };

  return (
    <Dropdown
      onOpenChange={(open) => {
        if (open && candidates === undefined && !loading) {
          loadCandidates();
        }
      }}
    >
      <DropdownTrigger>
        <Button isIconOnly color="secondary" size="sm" variant="light">
          <Tooltip content={t<string>("playlist.action.openWithPlayer")}>
            <AiOutlineExport className="text-lg" />
          </Tooltip>
        </Button>
      </DropdownTrigger>
      <DropdownMenu
        aria-label={t<string>("playlist.action.openWithPlayer")}
        onAction={(key) => (key === "retry" ? void loadCandidates() : void play(key as string))}
      >
        {error ? (
          [
            <DropdownItem
              key="error"
              isDisabled
              textValue={error}
              className="max-w-80 whitespace-normal"
            >
              {error}
            </DropdownItem>,
            <DropdownItem key="retry">{t("management.retry")}</DropdownItem>,
          ]
        ) : loading || candidates === undefined ? (
          <DropdownItem key="loading" isDisabled>
            {t<string>("resource.contextMenu.batchPlay.scanning")}
          </DropdownItem>
        ) : candidates.length === 0 ? (
          <DropdownItem key="empty" isDisabled>
            {t<string>("resource.contextMenu.batchPlay.noPlayers")}
          </DropdownItem>
        ) : (
          candidates.map((c) => (
            <DropdownItem
              key={c.key}
              description={c.executablePath}
              isDisabled={
                (isManaged ? (c.matchedResourceCount ?? 0) : (c.matchedFileCount ?? 0)) === 0
              }
            >
              {c.displayName}
              {c.capabilitiesAssumed ? t<string>("resource.contextMenu.batchPlay.untested") : ""}（
              {isManaged ? (c.matchedResourceCount ?? 0) : (c.matchedFileCount ?? 0)}）
            </DropdownItem>
          ))
        )}
      </DropdownMenu>
    </Dropdown>
  );
};

BatchPlayButton.displayName = "PlaylistBatchPlayButton";

export default BatchPlayButton;
