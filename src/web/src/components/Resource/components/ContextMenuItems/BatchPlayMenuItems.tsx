"use client";

import type { BakabaseModulesPlayerAbstractionsModelsDomainBatchPlayCandidate as BatchPlayCandidate } from "@/sdk/Api";

import { MenuDivider, MenuItem, SubMenu } from "@szhsin/react-menu";
import { PlayCircleOutlined } from "@ant-design/icons";
import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import {
  readIncludeAllFiles,
  readLastPlayer,
  writeIncludeAllFiles,
  writeLastPlayer,
} from "./batchPlayStorage";

import { toast } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import BApi from "@/sdk/BApi";
import { BatchPlayFileSelectionMode } from "@/sdk/constants";

const log = buildLogger("BatchPlayMenuItems");

type Props = {
  selectedResourceIds: number[];
};

/**
 * "Play together" submenu for a multi-selection, listing every candidate
 * player (profile-configured first, then players discovered on the machine),
 * each annotated with how many of the selected resources it can open. The
 * last picked player is tagged instead of getting a duplicate top-level item.
 */
const BatchPlayMenuItems = ({ selectedResourceIds }: Props) => {
  const { t } = useTranslation();

  const [candidates, setCandidates] = useState<BatchPlayCandidate[]>();
  const [loading, setLoading] = useState(false);
  const [lastPlayer, setLastPlayer] = useState(() => readLastPlayer(localStorage));
  const [includeAllFiles, setIncludeAllFiles] = useState(() => readIncludeAllFiles(localStorage));

  // Candidates depend on the selection (profile players vary per resource);
  // the menu stays mounted across openings, so invalidate on change.
  useEffect(() => {
    setCandidates(undefined);
  }, [selectedResourceIds]);

  const loadCandidates = useCallback(async () => {
    setLoading(true);
    try {
      const rsp = await BApi.player.getBatchPlayCandidates({ resourceIds: selectedResourceIds });

      if (!rsp.code) {
        setCandidates(rsp.data ?? []);
      }
    } catch (e) {
      log("Failed to load batch play candidates", e);
    } finally {
      setLoading(false);
    }
  }, [selectedResourceIds]);

  const play = useCallback(
    async (playerKey: string) => {
      const rsp = await BApi.player.batchPlayResources({
        resourceIds: selectedResourceIds,
        playerKey,
        fileSelectionMode: includeAllFiles
          ? BatchPlayFileSelectionMode.AllFiles
          : BatchPlayFileSelectionMode.FirstFilePerResource,
      });

      if (rsp.code || !rsp.data) return;

      const result = rsp.data;
      const stored = { key: playerKey, name: result.playerName };

      writeLastPlayer(localStorage, stored);
      setLastPlayer(stored);

      toast.success(
        t<string>("resource.contextMenu.batchPlay.success", {
          player: result.playerName,
          count: result.fileCount,
        }),
      );
      if ((result.skippedResources?.length ?? 0) > 0) {
        toast.warning(
          t<string>("resource.contextMenu.batchPlay.skipped", {
            count: result.skippedResources.length,
          }),
        );
      }
    },
    [selectedResourceIds, includeAllFiles, t],
  );

  if (selectedResourceIds.length < 2) {
    return null;
  }

  return (
    <SubMenu
      label={
        <div className="flex items-center gap-2">
          <PlayCircleOutlined className="text-base" />
          {t<string>("resource.contextMenu.batchPlay.label", {
            count: selectedResourceIds.length,
          })}
        </div>
      }
      menuStyle={{ minWidth: "220px" }}
      onMenuChange={(e) => {
        if (e.open && candidates === undefined && !loading) {
          loadCandidates();
        }
      }}
    >
      {loading || candidates === undefined ? (
        <MenuItem disabled>{t<string>("resource.contextMenu.batchPlay.scanning")}</MenuItem>
      ) : candidates.length === 0 ? (
        <>
          <MenuItem disabled>{t<string>("resource.contextMenu.batchPlay.noPlayers")}</MenuItem>
          <MenuItem disabled className="text-xs opacity-60">
            {t<string>("resource.contextMenu.batchPlay.noPlayersHint")}
          </MenuItem>
        </>
      ) : (
        candidates.map((c) => {
          // A player that can open nothing in this selection (e.g. an
          // audio player over a pure video selection) stays visible but
          // disabled, so the user sees why it is not an option.
          const matched = c.matchedResourceCount ?? 0;

          return (
            <MenuItem
              key={c.key}
              disabled={matched === 0}
              title={c.executablePath}
              onClick={() => play(c.key)}
            >
              {c.displayName}
              {c.capabilitiesAssumed ? t<string>("resource.contextMenu.batchPlay.untested") : ""}
              {t<string>("resource.contextMenu.batchPlay.matchedCount", {
                matched,
                total: selectedResourceIds.length,
              })}
              {c.key === lastPlayer?.key
                ? t<string>("resource.contextMenu.batchPlay.lastUsed")
                : ""}
            </MenuItem>
          );
        })
      )}
      <MenuDivider />
      <MenuItem
        checked={includeAllFiles}
        type="checkbox"
        onClick={(e) => {
          e.keepOpen = true;
          const next = !includeAllFiles;

          setIncludeAllFiles(next);
          writeIncludeAllFiles(localStorage, next);
        }}
      >
        {t<string>("resource.contextMenu.batchPlay.includeAllFiles")}
      </MenuItem>
    </SubMenu>
  );
};

BatchPlayMenuItems.displayName = "BatchPlayMenuItems";

export default BatchPlayMenuItems;
