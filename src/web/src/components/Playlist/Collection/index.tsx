"use client";

import type { components } from "@/sdk/BApi2";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Divider } from "@heroui/react";
import {
  AiOutlineDelete,
  AiOutlinePlayCircle,
  AiOutlinePlusCircle,
} from "react-icons/ai";

import { toast } from "@/components/bakaui";
import PlaylistDetail from "@/components/Playlist/Detail/index";
import { Button, Modal, Input } from "@/components/bakaui";
import { PlaylistItemType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import MediaPlayer from "@/components/MediaPlayer";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type PlayList =
  components["schemas"]["Bakabase.InsideWorld.Business.Components.PlayList.Models.Domain.PlayList"];

type Props = {
  addingResourceId?: number;
};
const Collection = ({ addingResourceId }: Props) => {
  const [playlists, setPlaylists] = useState<PlayList[]>([]);

  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  useEffect(() => {
    loadPlaylists();
  }, []);

  const loadPlaylists = useCallback(() => {
    BApi.playlist.getAllPlaylists().then((a) => {
      setPlaylists(a.data || []);
    });
  }, []);

  return (
    <div className="w-full">
      {playlists.length === 0 ? (
        <div className="flex flex-col items-center justify-center py-8 text-center">
          <div className="text-default-400 text-lg mb-2">
            {t<string>("No playlists found")}
          </div>
          <div className="text-default-300 text-sm">
            {t<string>("Create your first playlist to get started")}
          </div>
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          {playlists.map((pl, i) => (
            <>
              <div key={pl.id || i}>
                <div className="flex items-center gap-2 justify-between hover:bg-default-50 rounded-medium transition-colors">
                  <div>
                    <Button
                      color="primary"
                      size="sm"
                      variant="light"
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
                              onChange={(p: PlayList) => {
                                Object.assign(pl, p);
                                console.log(playlists, pl, p);
                                setPlaylists([...playlists]);
                              }}
                            />
                          ),
                          onOk: () =>
                            BApi.playlist.putPlaylist(pl.id, pl).then((a) => {
                              if (!a.code) {
                                dialog.destroy();

                                return a;
                              }
                              throw new Error(a.message!);
                            }),
                        });
                      }}
                    >
                      {pl.name} ({(pl.items || []).length})
                    </Button>
                  </div>

                  <div className="flex gap-0.5 items-center">
                    {addingResourceId && (
                      <Button
                        size="sm"
                        variant="bordered"
                        onPress={() => {
                          pl.items ??= [];
                          pl.items.push({
                            resourceId: addingResourceId,
                            type: PlaylistItemType.Resource,
                          });
                          BApi.playlist.putPlaylist(pl.id, pl).then(() => {
                            loadPlaylists();
                          });
                        }}
                      >
                        <AiOutlinePlusCircle className="text-lg" />
                        {t<string>("Add it here")}
                      </Button>
                    )}

                    {pl.items && pl.items.length > 0 && (
                      <Button
                        isIconOnly
                        color="success"
                        size="sm"
                        variant="light"
                        onPress={() => {
                          BApi.playlist.getPlaylistFiles(pl.id).then((a) => {
                            if (a.data) {
                              if (a.data.length > 0) {
                                const files: {
                                  path: string;
                                  startTime?: string;
                                  endTime?: string;
                                }[] = [];

                                for (let x = 0; x < pl.items!.length; x++) {
                                  const item = pl.items![x];
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
                        <AiOutlinePlayCircle className="text-lg" />
                      </Button>
                    )}

                    <Button
                      isIconOnly
                      color="danger"
                      size="sm"
                      variant="light"
                      onPress={() => {
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
                      <AiOutlineDelete className="text-lg" />
                    </Button>
                  </div>
                </div>
              </div>
              {i < playlists.length - 1 && <Divider />}
            </>
          ))}
        </div>
      )}
      <Divider className="mt-1" />
      <div className="pt-4">
        <Button
          color="primary"
          size="sm"
          variant="solid"
          onPress={() => {
            let name: string | undefined;

            createPortal(Modal, {
              defaultVisible: true,
              title: t<string>("Creating playlist"),
              children: (
                <Input
                  label={t<string>("Playlist Name")}
                  placeholder={t<string>("Enter playlist name")}
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

Collection.displayName = "Collection";

export default Collection;
