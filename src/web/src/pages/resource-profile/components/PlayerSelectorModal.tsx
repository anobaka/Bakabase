"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainMediaLibraryPlayer } from "@/sdk/Api";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineCheck, AiOutlineCopy, AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";
import { BsController, BsTerminal } from "react-icons/bs";

import { normalizeProfileExtensions, useProfileModalSave } from "./useProfileModalSave";

import { Accordion, AccordionItem, Button, Chip, Input, Modal } from "@/components/bakaui";
import ExtensionsInput from "@/components/ExtensionsInput";
import PathAutocomplete from "@/components/PathAutocomplete";
import { splitPathIntoSegments } from "@/components/utils";

type MediaLibraryPlayer = BakabaseAbstractionsModelsDomainMediaLibraryPlayer;
type Props = {
  players?: MediaLibraryPlayer[];
  onSubmit?: (players: MediaLibraryPlayer[]) => unknown | Promise<unknown>;
} & DestroyableProps;
type EditingPlayer = MediaLibraryPlayer & {
  editingId: string;
  pathType?: "folder" | "file";
  testFilePath: string;
  testFilePathType?: "folder" | "file";
};

/** Mirrors the local-file player's quoting rules; this is a preview, never a launch operation. */
export const generatePlayerCommand = (
  executablePath: string,
  command: string,
  filePath: string,
) => {
  const escapedFile = filePath.replace(/"/g, '\\"');
  const args = (command || "{0}").replace(/(["']?)\{\d+\}(["']?)/g, (_, prefix, suffix) =>
    (prefix === '"' && suffix === '"') || (prefix === "'" && suffix === "'")
      ? `${prefix}${escapedFile}${suffix}`
      : `"${escapedFile}"`,
  );

  return `"${executablePath}" ${args}`;
};

const PlayerSelectorModal = ({ players: propPlayers, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const editor = useProfileModalSave(onSubmit, t<string>("resourceProfile.editor.saveFailed"));
  const nextId = useRef(propPlayers?.length ?? 0);
  const [players, setPlayers] = useState<EditingPlayer[]>(() =>
    (propPlayers ?? []).map((player, index) => ({
      ...player,
      extensions: normalizeProfileExtensions(player.extensions),
      editingId: `player-${index}`,
      pathType: "file",
      testFilePath: "",
    })),
  );
  const [expanded, setExpanded] = useState<Set<string>>(
    () => new Set(propPlayers?.length === 1 ? ["player-0"] : []),
  );
  const [copiedId, setCopiedId] = useState<string>();
  const [copyError, setCopyError] = useState<string>();
  const copyTimer = useRef<ReturnType<typeof setTimeout>>();

  useEffect(
    () => () => {
      if (copyTimer.current) clearTimeout(copyTimer.current);
    },
    [],
  );

  const addPlayer = () => {
    const editingId = `player-${nextId.current++}`;

    setPlayers((current) => [
      ...current,
      { editingId, executablePath: "", command: "{0}", extensions: [], testFilePath: "" },
    ]);
    setExpanded((current) => new Set([...current, editingId]));
  };
  const updatePlayer = (editingId: string, updates: Partial<EditingPlayer>) =>
    setPlayers((current) =>
      current.map((player) =>
        player.editingId === editingId ? { ...player, ...updates } : player,
      ),
    );
  const removePlayer = (editingId: string) => {
    setPlayers((current) => current.filter((player) => player.editingId !== editingId));
    setExpanded((current) => new Set([...current].filter((id) => id !== editingId)));
  };
  const hasInvalidPlayers = players.some(
    (player) => !player.executablePath.trim() || player.pathType !== "file",
  );
  const savePlayers = () => {
    if (hasInvalidPlayers) return;

    // Explicit projection keeps preview paths and editing IDs out of the persisted configuration.
    return editor.save(
      players.map((player) => ({
        executablePath: player.executablePath,
        command: player.command || "{0}",
        extensions: normalizeProfileExtensions(player.extensions),
      })),
    );
  };
  const copyCommand = async (player: EditingPlayer) => {
    setCopyError(undefined);
    try {
      await navigator.clipboard.writeText(
        generatePlayerCommand(player.executablePath, player.command, player.testFilePath),
      );
      setCopiedId(player.editingId);
      if (copyTimer.current) clearTimeout(copyTimer.current);
      copyTimer.current = setTimeout(() => setCopiedId(undefined), 2000);
    } catch {
      setCopyError(t<string>("resourceProfile.players.copyFailed"));
    }
  };

  return (
    <Modal
      classNames={{ base: "max-w-3xl", body: "gap-5", footer: "border-t border-default-200/60" }}
      footer={
        <div className="flex w-full items-center justify-between gap-3">
          <span className="text-xs text-default-500">
            {t<string>("resourceProfile.status.playersConfigured", { count: players.length })}
          </span>
          <div className="flex gap-2">
            <Button isDisabled={editor.saving} variant="light" onPress={editor.close}>
              {t<string>("common.action.cancel")}
            </Button>
            <Button
              color="primary"
              isDisabled={hasInvalidPlayers}
              isLoading={editor.saving}
              onPress={savePlayers}
            >
              {t<string>("common.action.save")}
            </Button>
          </div>
        </div>
      }
      hideCloseButton={editor.saving}
      isDismissable={!editor.saving}
      isKeyboardDismissDisabled={editor.saving}
      size="3xl"
      title={t<string>("resourceProfile.modal.configurePlayersTitle")}
      visible={editor.visible}
      onClose={editor.close}
      onDestroyed={onDestroyed}
    >
      <div className="space-y-2">
        <p className="text-sm leading-6 text-default-600">
          {t<string>("resourceProfile.players.description")}
        </p>
        <p className="text-xs leading-5 text-default-500">
          {t<string>("resourceProfile.players.deviceHint")}
        </p>
      </div>
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-xs leading-5 text-default-500">
          {t<string>("resourceProfile.players.selectionOrder")}
        </p>
        <Button
          color="primary"
          isDisabled={editor.saving}
          size="sm"
          startContent={<AiOutlinePlus />}
          variant="flat"
          onPress={addPlayer}
        >
          {t<string>("resourceProfile.action.addPlayer")}
        </Button>
      </div>
      {players.length === 0 ? (
        <div className="flex flex-col items-center gap-2 rounded-xl bg-default-50 px-6 py-8 text-center">
          <BsController aria-hidden className="mb-1 text-3xl text-default-400" />
          <p className="text-sm font-medium">{t<string>("resourceProfile.players.emptyTitle")}</p>
          <p className="max-w-md text-xs leading-5 text-default-500">
            {t<string>("resourceProfile.players.emptyDescription")}
          </p>
        </div>
      ) : (
        <Accordion
          className="px-0"
          expandedKeys={expanded}
          itemClasses={{
            base: "border-b border-default-200/60 last:border-b-0",
            trigger: "py-3",
            content: "pb-5 pt-1",
          }}
          selectionMode="multiple"
          showDivider={false}
          variant="light"
          onSelectionChange={(keys) =>
            setExpanded(
              keys === "all"
                ? new Set(players.map((player) => player.editingId))
                : new Set([...keys].map(String)),
            )
          }
        >
          {players.map((player) => {
            const segments = splitPathIntoSegments(player.executablePath);
            const playerName =
              segments[segments.length - 1] || t<string>("resourceProfile.label.newPlayer");
            const invalid = !player.executablePath.trim() || player.pathType !== "file";
            const extensions = normalizeProfileExtensions(player.extensions);

            return (
              <AccordionItem
                key={player.editingId}
                aria-label={playerName}
                startContent={<BsController aria-hidden className="text-xl text-default-500" />}
                subtitle={
                  extensions.length
                    ? extensions.join(" · ")
                    : t<string>("resourceProfile.players.defaultForOtherFiles")
                }
                title={
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="break-all text-sm font-medium">{playerName}</span>
                    {invalid && (
                      <Chip color="warning" size="sm" variant="flat">
                        {t<string>("resourceProfile.players.incomplete")}
                      </Chip>
                    )}
                  </div>
                }
              >
                <fieldset
                  className="m-0 flex min-w-0 flex-col gap-4 border-0 p-0"
                  disabled={editor.saving}
                >
                  <PathAutocomplete
                    isRequired
                    description={t<string>("resourceProfile.players.pathHint")}
                    errorMessage={
                      player.pathType === "folder"
                        ? t<string>("resourceProfile.error.selectFileNotFolder")
                        : t<string>("resourceProfile.players.selectKnownFile")
                    }
                    isDisabled={editor.saving}
                    isInvalid={!!player.executablePath && invalid}
                    label={t<string>("resourceProfile.label.executablePath")}
                    pathType="file"
                    placeholder={t<string>("resourceProfile.tip.pathToPlayerExecutable")}
                    value={player.executablePath}
                    onChange={(executablePath, pathType) =>
                      updatePlayer(player.editingId, { executablePath, pathType })
                    }
                  />
                  <div className="space-y-1.5">
                    <ExtensionsInput
                      defaultValue={player.extensions}
                      label={t<string>("resourceProfile.players.openTheseFiles")}
                      minRows={1}
                      onValueChange={(extensions) => updatePlayer(player.editingId, { extensions })}
                    />
                    <p className="text-xs leading-5 text-default-500">
                      {t<string>("resourceProfile.players.extensionsHint")}
                    </p>
                  </div>
                  <details
                    className="group"
                    open={player.command !== "{0}" && !!player.command ? true : undefined}
                  >
                    <summary className="flex cursor-pointer list-none items-center gap-2 py-1 text-sm font-medium text-default-600">
                      <BsTerminal aria-hidden />
                      {t<string>("resourceProfile.players.advancedTitle")}
                      <span aria-hidden className="ml-auto text-default-400 group-open:rotate-90">
                        ›
                      </span>
                    </summary>
                    <div className="mt-3 flex flex-col gap-3">
                      <Input
                        description={t<string>("resourceProfile.tip.commandTemplatePlaceholder")}
                        isDisabled={editor.saving}
                        label={t<string>("resourceProfile.label.commandTemplate")}
                        placeholder="{0}"
                        value={player.command}
                        onValueChange={(command) => updatePlayer(player.editingId, { command })}
                      />
                      <p className="text-xs leading-5 text-default-500">
                        {t<string>("resourceProfile.players.previewHint")}
                      </p>
                      <PathAutocomplete
                        isDisabled={editor.saving}
                        label={t<string>("resourceProfile.label.testFilePath")}
                        pathType="both"
                        placeholder={t<string>("resourceProfile.tip.enterFilePathPreview")}
                        value={player.testFilePath}
                        onChange={(testFilePath, testFilePathType) =>
                          updatePlayer(player.editingId, { testFilePath, testFilePathType })
                        }
                      />
                      {player.testFilePath && !invalid && (
                        <div className="flex items-start gap-2 rounded-lg bg-default-100 p-3">
                          <code className="min-w-0 flex-1 break-all text-xs leading-5">
                            {generatePlayerCommand(
                              player.executablePath,
                              player.command,
                              player.testFilePath,
                            )}
                          </code>
                          <Button
                            isIconOnly
                            aria-label={t<string>(
                              copiedId === player.editingId
                                ? "resourceProfile.action.copied"
                                : "resourceProfile.action.copyToClipboard",
                            )}
                            color={copiedId === player.editingId ? "success" : "default"}
                            size="sm"
                            variant="light"
                            onPress={() => copyCommand(player)}
                          >
                            {copiedId === player.editingId ? <AiOutlineCheck /> : <AiOutlineCopy />}
                          </Button>
                        </div>
                      )}
                      {player.testFilePathType === "folder" && (
                        <p className="text-xs text-warning-700">
                          {t<string>("resourceProfile.tip.testPathIsFolder")}
                        </p>
                      )}
                    </div>
                  </details>
                  <div className="flex justify-end">
                    <Button
                      aria-label={t<string>("resourceProfile.players.removeNamed", {
                        name: playerName,
                      })}
                      color="danger"
                      isDisabled={editor.saving}
                      size="sm"
                      startContent={<AiOutlineDelete />}
                      variant="light"
                      onPress={() => removePlayer(player.editingId)}
                    >
                      {t<string>("resourceProfile.players.remove")}
                    </Button>
                  </div>
                </fieldset>
              </AccordionItem>
            );
          })}
        </Accordion>
      )}
      {hasInvalidPlayers && (
        <p className="text-xs text-default-500">
          {t<string>("resourceProfile.players.completeBeforeSaving")}
        </p>
      )}
      {copyError && (
        <p className="text-sm text-danger" role="alert">
          {copyError}
        </p>
      )}
      {editor.error && (
        <p className="rounded-lg bg-danger/10 px-3 py-2 text-sm text-danger" role="alert">
          {editor.error}
        </p>
      )}
    </Modal>
  );
};

PlayerSelectorModal.displayName = "PlayerSelectorModal";
export default PlayerSelectorModal;
