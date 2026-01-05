"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { BakabaseAbstractionsModelsDomainMediaLibraryPlayer } from "@/sdk/Api";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";
import { BsController, BsTerminal } from "react-icons/bs";
import { InfoCircleOutlined, CopyOutlined, CheckOutlined } from "@ant-design/icons";

import { Modal, Input, Button, Chip, Divider, Tooltip, Accordion, AccordionItem } from "@/components/bakaui";
import ExtensionsInput from "@/components/ExtensionsInput";
import PathAutocomplete from "@/components/PathAutocomplete";
import { splitPathIntoSegments } from "@/components/utils";

type MediaLibraryPlayer = BakabaseAbstractionsModelsDomainMediaLibraryPlayer;

type Props = {
  players?: MediaLibraryPlayer[];
  onSubmit?: (players: MediaLibraryPlayer[]) => any;
} & DestroyableProps;

type EditingPlayer = MediaLibraryPlayer & {
  pathType?: "folder" | "file";
  testFilePath?: string;
  testFilePathType?: "folder" | "file";
};

// Generate the actual command that would be executed
const generateCommand = (executablePath: string, commandTemplate: string, filePath: string): string => {
  const template = commandTemplate || "{0}";
  const args = template.replace(/\{0\}/g, `"${filePath}"`);
  return `"${executablePath}" ${args}`;
};

const PlayerSelectorModal = ({ players: propPlayers, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();

  const [players, setPlayers] = useState<EditingPlayer[]>(
    (propPlayers ?? []).map((p) => ({ ...p, pathType: "file", testFilePath: "", testFilePathType: undefined }))
  );
  const [copiedIndex, setCopiedIndex] = useState<number | null>(null);

  const addPlayer = () => {
    setPlayers([
      ...players,
      {
        executablePath: "",
        command: "{0}",
        extensions: [],
      },
    ]);
  };

  const removePlayer = (index: number) => {
    const newPlayers = [...players];
    newPlayers.splice(index, 1);
    setPlayers(newPlayers);
  };

  const updatePlayer = (index: number, updates: Partial<EditingPlayer>) => {
    const newPlayers = [...players];
    newPlayers[index] = { ...newPlayers[index], ...updates };
    setPlayers(newPlayers);
  };

  const validatePlayers = () => {
    return players.every(
      (player) =>
        player.executablePath.trim() !== "" && player.pathType === "file"
    );
  };

  const handleSubmit = () => {
    if (validatePlayers()) {
      onSubmit?.(players.map(({ pathType, ...p }) => p));
    }
  };

  const hasInvalidPlayers = players.some(
    (player) =>
      player.executablePath.trim() === "" || player.pathType !== "file"
  );

  const getPlayerName = (path: string) => {
    if (!path) return t("resourceProfile.label.newPlayer");
    const segments = splitPathIntoSegments(path);
    return segments[segments.length - 1] || t("resourceProfile.label.newPlayer");
  };

  const copyCommand = async (index: number, command: string) => {
    try {
      await navigator.clipboard.writeText(command);
      setCopiedIndex(index);
      setTimeout(() => setCopiedIndex(null), 2000);
    } catch (err) {
      console.error("Failed to copy command:", err);
    }
  };

  return (
    <Modal
      defaultVisible
      okProps={{
        isDisabled: hasInvalidPlayers,
      }}
      size="2xl"
      title={t("resourceProfile.modal.configurePlayersTitle")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <div className="flex flex-col gap-4">
        {/* Header with add button */}
        <div className="flex items-center justify-between">
          <div className="text-sm text-default-500">
            {players.length > 0
              ? t("resourceProfile.status.playersConfigured", { count: players.length })
              : t("resourceProfile.status.noPlayersConfigured")}
          </div>
          <Button
            color="primary"
            size="sm"
            startContent={<AiOutlinePlus />}
            onPress={addPlayer}
          >
            {t("resourceProfile.action.addPlayer")}
          </Button>
        </div>

        {players.length === 0 ? (
          <div className="text-center py-12 bg-default-50 rounded-lg">
            <BsController className="text-4xl text-default-300 mx-auto mb-3" />
            <p className="text-default-400">
              {t("resourceProfile.empty.noPlayersConfiguredGetStarted")}
            </p>
          </div>
        ) : (
          <div className="max-h-[60vh] overflow-y-auto">
            <Accordion
              selectionMode="multiple"
              variant="splitted"
              defaultExpandedKeys={players.length === 1 ? ["0"] : []}
            >
              {players.map((player, index) => {
                const playerName = getPlayerName(player.executablePath);
                const isInvalid = player.executablePath.trim() === "" || player.pathType !== "file";

                return (
                  <AccordionItem
                    key={String(index)}
                    aria-label={playerName}
                    classNames={{
                      base: isInvalid ? "border-danger border-2" : "",
                    }}
                    startContent={
                      <BsController className={`text-lg ${isInvalid ? "text-danger" : "text-success"}`} />
                    }
                    title={
                      <div className="flex items-center gap-2">
                        <span className="font-medium">{playerName}</span>
                        {(player.extensions?.length ?? 0) > 0 && (
                          <Chip size="sm" variant="flat" color="default">
                            {player.extensions?.length} {t("resourceProfile.label.extensions")}
                          </Chip>
                        )}
                        {isInvalid && (
                          <Chip size="sm" variant="flat" color="danger">
                            {t("resourceProfile.label.invalid")}
                          </Chip>
                        )}
                      </div>
                    }
                  >
                    <div className="flex flex-col gap-4 pb-2">
                      {/* Delete button */}
                      <div className="flex justify-end">
                        <Button
                          color="danger"
                          size="sm"
                          variant="flat"
                          startContent={<AiOutlineDelete />}
                          onPress={() => removePlayer(index)}
                        >
                          {t("common.action.delete")}
                        </Button>
                      </div>

                      <PathAutocomplete
                        isRequired
                        errorMessage={
                          player.executablePath.trim() === ""
                            ? t("resourceProfile.error.executablePathRequired")
                            : player.pathType !== "file"
                            ? t("resourceProfile.error.selectFileNotFolder")
                            : ""
                        }
                        isInvalid={isInvalid}
                        label={t("resourceProfile.label.executablePath")}
                        pathType="file"
                        placeholder={t("resourceProfile.tip.pathToPlayerExecutable")}
                        value={player.executablePath}
                        onChange={(value, type) =>
                          updatePlayer(index, {
                            executablePath: value,
                            pathType: type,
                          })
                        }
                      />

                      <div>
                        <Input
                          label={t("resourceProfile.label.commandTemplate")}
                          placeholder="{0}"
                          value={player.command}
                          onValueChange={(value) =>
                            updatePlayer(index, { command: value })
                          }
                        />
                        <div className="text-xs text-default-400 mt-1">
                          <InfoCircleOutlined className="mr-1" />
                          {t("resourceProfile.tip.commandTemplatePlaceholder")}
                        </div>
                      </div>

                      <div>
                        <ExtensionsInput
                          defaultValue={player.extensions}
                          label={t("resourceProfile.label.supportedExtensions")}
                          onValueChange={(extensions) =>
                            updatePlayer(index, { extensions })
                          }
                          minRows={1}
                        />
                        <div className="text-xs text-default-400 mt-1">
                          <InfoCircleOutlined className="mr-1" />
                          {t("resourceProfile.tip.playerExtensionUsage")}
                        </div>
                      </div>

                      {/* Command Preview & Test Section */}
                      {player.executablePath.trim() && player.pathType === "file" && (
                        <>
                          <Divider />
                          <div className="flex flex-col gap-3">
                            <div className="flex items-center gap-2">
                              <BsTerminal className="text-lg text-primary" />
                              <span className="font-medium text-sm">{t("resourceProfile.label.commandPreviewTest")}</span>
                            </div>

                            <PathAutocomplete
                              label={t("resourceProfile.label.testFilePath")}
                              placeholder={t("resourceProfile.tip.enterFilePathPreview")}
                              value={player.testFilePath || ""}
                              onChange={(value, type) =>
                                updatePlayer(index, {
                                  testFilePath: value,
                                  testFilePathType: type,
                                })
                              }
                            />

                            {player.testFilePath && (
                              <div className="flex flex-col gap-2">
                                <div className="text-xs text-default-500">{t("resourceProfile.label.generatedCommand")}:</div>
                                <div className="flex items-start gap-2">
                                  <code className="flex-1 p-2 bg-default-100 rounded text-xs font-mono break-all">
                                    {generateCommand(
                                      player.executablePath,
                                      player.command || "{0}",
                                      player.testFilePath
                                    )}
                                  </code>
                                  <Tooltip content={copiedIndex === index ? t("resourceProfile.action.copied") : t("resourceProfile.action.copyToClipboard")}>
                                    <Button
                                      isIconOnly
                                      size="sm"
                                      variant="flat"
                                      color={copiedIndex === index ? "success" : "default"}
                                      onPress={() =>
                                        copyCommand(
                                          index,
                                          generateCommand(
                                            player.executablePath,
                                            player.command || "{0}",
                                            player.testFilePath || ""
                                          )
                                        )
                                      }
                                    >
                                      {copiedIndex === index ? (
                                        <CheckOutlined className="text-sm" />
                                      ) : (
                                        <CopyOutlined className="text-sm" />
                                      )}
                                    </Button>
                                  </Tooltip>
                                </div>
                                {player.testFilePathType === "folder" && (
                                  <div className="text-xs text-warning">
                                    <InfoCircleOutlined className="mr-1" />
                                    {t("resourceProfile.tip.testPathIsFolder")}
                                  </div>
                                )}
                              </div>
                            )}
                          </div>
                        </>
                      )}
                    </div>
                  </AccordionItem>
                );
              })}
            </Accordion>
          </div>
        )}

        {hasInvalidPlayers && (
          <div className="text-danger text-sm flex items-center gap-1">
            <InfoCircleOutlined />
            {t("resourceProfile.error.fillAllExecutablePaths")}
          </div>
        )}
      </div>
    </Modal>
  );
};

PlayerSelectorModal.displayName = "PlayerSelectorModal";

export default PlayerSelectorModal;
