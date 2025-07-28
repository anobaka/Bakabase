"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { MediaLibraryPlayer } from "../models";

import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDelete, AiOutlinePlus } from "react-icons/ai";

import { Modal, Input, Button } from "@/components/bakaui";
import ExtensionsInput from "@/components/ExtensionsInput";
import PathAutocomplete from "@/components/PathAutocomplete";

type Props = {
  players?: MediaLibraryPlayer[];
  onSubmit?: (players: MediaLibraryPlayer[]) => any;
} & DestroyableProps;

type EditingPlayer = MediaLibraryPlayer & {
  pathType?: "folder" | "file";
};
const PlayerSelectorModal = ({ players: propPlayers, onSubmit }: Props) => {
  const { t } = useTranslation();

  const [players, setPlayers] = useState<EditingPlayer[]>(
    (propPlayers ?? []).map((p) => ({ ...p, pathType: "file" })),
  );

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
        player.executablePath.trim() !== "" && player.pathType == "file",
    );
  };

  const handleSubmit = () => {
    if (validatePlayers()) {
      onSubmit?.(players);
    }
  };

  const hasInvalidPlayers = players.some(
    (player) =>
      player.executablePath.trim() === "" || player.pathType != "file",
  );

  console.log(players);

  return (
    <Modal
      defaultVisible
      okProps={{
        isDisabled: hasInvalidPlayers,
      }}
      size={"xl"}
      onOk={handleSubmit}
    >
      <div className={"flex flex-col gap-4"}>
        <div className={"flex items-center justify-between"}>
          <h3 className={"text-lg font-semibold"}>
            {t<string>("Configure Players")}
          </h3>
          <Button
            color={"primary"}
            size={"sm"}
            startContent={<AiOutlinePlus />}
            onPress={addPlayer}
          >
            {t<string>("Add Player")}
          </Button>
        </div>

        {players.length === 0 ? (
          <div className={"text-center text-gray-500 py-8"}>
            {t<string>(
              "No players configured. Click 'Add Player' to get started.",
            )}
          </div>
        ) : (
          <div className={"flex flex-col gap-4"}>
            {players.map((player, index) => (
              <div
                key={index}
                className={"border rounded-lg p-4 flex flex-col gap-3"}
              >
                <div className={"flex items-center justify-between"}>
                  <h4 className={"font-medium"}>
                    {t<string>(`Player ${index + 1}`)}
                  </h4>
                  <Button
                    isIconOnly
                    color={"danger"}
                    size={"sm"}
                    variant={"light"}
                    onPress={() => removePlayer(index)}
                  >
                    <AiOutlineDelete className={"text-lg"} />
                  </Button>
                </div>

                <div className={"grid grid-cols-1 gap-3"}>
                  <PathAutocomplete
                    isRequired
                    errorMessage={
                      player.executablePath.trim() === ""
                        ? t<string>("Executable path is required")
                        : ""
                    }
                    isInvalid={player.executablePath.trim() === ""}
                    label={t<string>("Executable Path")}
                    pathType={"file"}
                    placeholder={t<string>("Path to the player executable")}
                    value={player.executablePath}
                    onChange={(value, type) =>
                      updatePlayer(index, {
                        executablePath: value,
                        pathType: type,
                      })
                    }
                  />

                  <Input
                    description={t<string>(
                      "Use {0} as placeholder for the file path",
                    )}
                    label={t<string>("Command")}
                    placeholder={"{0}"}
                    value={player.command}
                    onValueChange={(value) =>
                      updatePlayer(index, { command: value })
                    }
                  />

                  <ExtensionsInput
                    defaultValue={player.extensions}
                    label={t<string>("Supported file extensions")}
                    onValueChange={(extensions) =>
                      updatePlayer(index, { extensions })
                    }
                  />
                </div>
              </div>
            ))}
          </div>
        )}

        {hasInvalidPlayers && (
          <div className={"text-red-500 text-sm mt-2"}>
            {t<string>("Please fill in all executable paths before saving.")}
          </div>
        )}
      </div>
    </Modal>
  );
};

PlayerSelectorModal.displayName = "PlayerSelectorModal";

export default PlayerSelectorModal;
