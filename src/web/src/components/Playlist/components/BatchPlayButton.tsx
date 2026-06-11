"use client";

import type { BakabaseModulesPlayerAbstractionsModelsDomainBatchPlayCandidate as BatchPlayCandidate } from "@/sdk/Api";

import { useState } from "react";
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
import BApi from "@/sdk/BApi";

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
  const { t } = useTranslation();

  const [candidates, setCandidates] = useState<BatchPlayCandidate[]>();
  const [loading, setLoading] = useState(false);

  const loadCandidates = async () => {
    setLoading(true);
    try {
      const rsp = await BApi.player.getPlaylistBatchPlayCandidates(playlistId);

      if (!rsp.code) {
        setCandidates(rsp.data ?? []);
      }
    } finally {
      setLoading(false);
    }
  };

  const play = async (playerKey: string) => {
    const rsp = await BApi.player.batchPlayPlaylist(playlistId, { playerKey });

    if (rsp.code || !rsp.data) return;

    toast.success(
      t<string>("resource.contextMenu.batchPlay.success", {
        player: rsp.data.playerName,
        count: rsp.data.fileCount,
      }),
    );
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
        onAction={(key) => play(key as string)}
      >
        {loading || candidates === undefined ? (
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
              isDisabled={(c.matchedFileCount ?? 0) === 0}
            >
              {c.displayName}
              {c.capabilitiesAssumed ? t<string>("resource.contextMenu.batchPlay.untested") : ""}（
              {c.matchedFileCount ?? 0}）
            </DropdownItem>
          ))
        )}
      </DropdownMenu>
    </Dropdown>
  );
};

BatchPlayButton.displayName = "PlaylistBatchPlayButton";

export default BatchPlayButton;
