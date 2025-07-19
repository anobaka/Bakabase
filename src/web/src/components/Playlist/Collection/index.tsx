"use client";

import React, { useCallback, useEffect, useRef, useState } from "react";
import * as dayjs from "dayjs";
import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";
import { MdAddCircle, MdPlayCircle, MdDelete } from "react-icons/md";
import { Card, CardBody, CardHeader } from "@heroui/react";

import styles from "./index.module.scss";

import { toast } from "@/components/bakaui";
import PlaylistDetail from "@/components/Playlist/Detail/index";
import { Popover, Button, Modal, Input } from "@/components/bakaui";
import { PlaylistItemType } from "@/sdk/constants";
import TimeRanger from "@/components/TimeRanger";
import BApi from "@/sdk/BApi";
import MediaPlayer from "@/components/MediaPlayer";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface IPlaylistItem {
  type: PlaylistItemType;
  resourceId?: number;
  file?: string;
  startTime?: string;
  endTime?: string;
}

class PlaylistItem implements IPlaylistItem {
  type: PlaylistItemType;
  endTime?: string;
  file?: string;
  resourceId?: number;
  startTime?: string;

  constructor(init?: Partial<IPlaylistItem>) {
    Object.assign(this, init);
  }

  static equals(a: IPlaylistItem, b: IPlaylistItem) {
    if (a.file && b.file && a.file == b.file) {
      return true;
    }

    return !!(
      !a.file &&
      !b.file &&
      a.resourceId &&
      b.resourceId &&
      a.resourceId == b.resourceId
    );
  }
}

interface IPropsItem {
  type: PlaylistItemType;
  resourceId?: number;
  file?: string;
  totalSeconds?: number;
  onTimeSelected?: (second: number) => void;
}

interface IPlaylist {
  id?: number;
  name?: string;
  items?: IPlaylistItem[];
  interval?: number;
  order?: number;
}

interface IProps {
  defaultNewItem?: IPropsItem;
  className?: string;
}

export default ({ defaultNewItem, className }: IProps) => {
  const [playlists, setPlaylists] = useState<IPlaylist[]>([]);
  const newItemRef = useRef<IPlaylistItem | undefined>(
    defaultNewItem
      ? {
          ...defaultNewItem,
        }
      : undefined,
  );

  const forceUpdate = useUpdate();

  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  useEffect(() => {
    loadPlaylists();
  }, []);

  const loadPlaylists = useCallback(() => {
    BApi.playlist.getAllPlaylists().then((a) => {
      // @ts-ignore
      setPlaylists(a.data || []);
    });
  }, []);

  const saveItemToPlaylist = useCallback(
    (item: IPlaylistItem, playlist: IPlaylist) => {
      const items = playlist.items?.slice() || [];

      items.push(item);

      return BApi.playlist
        .putPlaylist({
          ...playlist,
          // @ts-ignore
          items,
        })
        .then((a) => {
          if (!a.code) {
            playlist.items = items;
            forceUpdate();
            toast.success(t<string>("Add successfully"));

            return a;
          }
          throw new Error(a.message!);
        });
    },
    [],
  );

  const setDurationTime = useCallback(
    (
      currentValue: number[],
      newValue: number[] | undefined,
      key: "startTime" | "endTime",
    ) => {
      if (newValue) {
        const idx = key == "startTime" ? 0 : 1;
        const value = newValue[idx];

        if (value != undefined && currentValue[idx] != value) {
          currentValue[idx] = value;
          newItemRef.current![key] = dayjs
            .duration(value * 1000)
            .format("HH:mm:ss");
          if (defaultNewItem?.onTimeSelected) {
            defaultNewItem.onTimeSelected(value);
          }
        }
      }
    },
    [],
  );

  const showDurationSelector = useCallback(
    (item: IPlaylistItem, playlist: IPlaylist) => {
      // console.log(playerElement, playerElement?.currentTime);
      const totalSeconds = Math.floor(defaultNewItem!.totalSeconds || 0);

      if (totalSeconds == 0) {
        return toast.error(t<string>("Can not get duration of a media file"));
      }
      const duration = [0, totalSeconds];

      createPortal(Modal, {
        defaultVisible: true,
        title: t<string>("Select duration"),
        children: (
          <div className={"playlist-item-duration-selector"}>
            <TimeRanger
              duration={dayjs.duration(totalSeconds * 1000)}
              onProcess={(seconds) => {
                setDurationTime(duration, seconds, "startTime");
                setDurationTime(duration, seconds, "endTime");
              }}
            />
          </div>
        ),
        onOk: () => saveItemToPlaylist(item, playlist),
      });
    },
    [],
  );

  const addNewItem = useCallback((newItem: IPlaylistItem, playlist: any) => {
    if (newItem) {
      switch (newItem.type) {
        case PlaylistItemType.Resource:
        case PlaylistItemType.Image:
          saveItemToPlaylist(newItem, playlist);
          break;
        case PlaylistItemType.Video:
        case PlaylistItemType.Audio:
          showDurationSelector(newItem, playlist);
          break;
      }
    }
  }, []);

  const renderAddButton = useCallback(
    (newItem: IPlaylistItem | undefined, playlist: IPlaylist) => {
      if (newItem) {
        const added = playlist.items?.find((a) =>
          PlaylistItem.equals(a, newItem),
        );
        const btn = (
          <Button
            size={"small"}
            type={"normal"}
            onClick={() => {
              addNewItem(newItem, playlist);
            }}
          >
            <MdAddCircle size={"small"} />
            {t<string>(added ? "Added" : "Add it here")}
          </Button>
        );

        if (added) {
          return (
            <Popover
              content={t<string>(
                "You can add it more than once, and if you want to remove it from playlist, you should click the corresponding playlist first.",
              )}
              trigger={btn}
              triggerType={"hover"}
            />
          );
        }

        return btn;
      }

      return;
    },
    [],
  );

  return (
    <div className={`${styles.playlistCollection} ${className}`}>
      <div>
        {playlists.map((pl, i) => {
          return (
            <Card key={i} className={styles.item}>
              <CardHeader>
                <Button
                  text
                  type={"primary"}
                  onPress={() => {
                    const dialog = createPortal(Modal, {
                      defaultVisible: true,
                      title: t<string>("Details of {{name}}", {
                        name: pl.name,
                      }),
                      size: "lg",
                      children: (
                        <PlaylistDetail
                          id={pl.id}
                          onChange={(p) => {
                            Object.assign(pl, p);
                            console.log(playlists, pl, p);
                            setPlaylists([...playlists]);
                          }}
                        />
                      ),
                      onOk: () =>
                        BApi.playlist.putPlaylist(pl).then((a) => {
                          if (!a.code) {
                            dialog.destroy();

                            return a;
                          }
                          throw new Error(a.message!);
                        }),
                    });
                  }}
                >
                  {pl.name}({(pl.items || []).length})
                </Button>
              </CardHeader>
              <CardBody>
                <div className={styles.extra}>
                  {renderAddButton(newItemRef.current, pl)}
                  {pl.items?.length > 0 && (
                    <Button
                      size={"small"}
                      onPress={() => {
                        BApi.playlist.getPlaylistFiles(pl.id).then((a) => {
                          if (a.data) {
                            if (a.data.length > 0) {
                              const files: {
                                path: string;
                                startTime?: string;
                                endTime?: string;
                              }[] = [];
                              // console.log(a.data, pl, pl.items, pl.items.length);

                              for (let x = 0; x < pl.items.length; x++) {
                                const item = pl.items[x];
                                const currentItemFiles = a.data[x];

                                if (currentItemFiles) {
                                  for (const file of a.data[x]) {
                                    files.push({
                                      path: file,
                                      startTime: item.startTime,
                                      endTime: item.endTime,
                                    });
                                  }
                                }
                              }
                              const mpProps = {
                                files,
                                interval: pl.interval,
                                autoPlay: true,
                                style: {
                                  zIndex: 1001,
                                },
                              };

                              MediaPlayer.show(mpProps);
                            } else {
                              toast.danger(t<string>("No playable contents"));
                            }
                          }
                        });
                      }}
                    >
                      <MdPlayCircle size={"small"} />
                      {t<string>("Play")}
                    </Button>
                  )}
                  <Button
                    warning
                    size={"small"}
                    onClick={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("Sure to delete?"),
                        children: t<string>(
                          "Are you sure you want to delete this playlist?",
                        ),
                        onOk: () =>
                          BApi.playlist.deletePlaylist(pl.id).then((a) => {
                            if (!a.code) {
                              loadPlaylists();

                              return a;
                            }
                            throw new Error(a.message!);
                          }),
                      });
                    }}
                  >
                    <MdDelete size={"small"} />
                    {t<string>("Delete")}
                  </Button>
                </div>
              </CardBody>
            </Card>
          );
        })}
      </div>
      <div className={styles.opt}>
        <Button
          color={"default"}
          size={"sm"}
          onPress={() => {
            let name: string | undefined;

            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("Creating playlist"),
              children: (
                <Input
                  onValueChange={(v) => {
                    name = v;
                  }}
                />
              ),
              onOk: async () => {
                if (!name) {
                  toast.danger(t<string>("Name can not be empty"));
                  throw new Error("Name can not be empty");
                }
                const a = await BApi.playlist.addPlaylist({
                  name,
                });

                if (!a.code) {
                  loadPlaylists();
                }
              },
            });
          }}
        >
          {t<string>("Create")}
        </Button>
      </div>
    </div>
  );
};
