"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { HistoryOutlined, SearchOutlined } from "@ant-design/icons";

import { buildLogger } from "@/components/utils";
import BApi from "@/sdk/BApi";
import { useResourceOptionsStore } from "@/stores/options";
import { Button, Chip, Divider, Input, Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type Props = {
  onSelect: (id: number) => Promise<any> | any;
  confirmation?: boolean;
} & DestroyableProps;

type Library = {
  id: number;
  name: string;
};

type Category = {
  id: number;
  name: string;
  libraries: Library[];
};

const log = buildLogger("MediaLibrarySelectorV2");
const MediaLibrarySelectorV2 = (props: Props) => {
  const { t } = useTranslation();
  const { onSelect, confirmation } = props;
  const { createPortal } = useBakabaseContext();

  const [visible, setVisible] = useState(true);
  const [categories, setCategories] = useState<Category[]>([]);
  const [keyword, setKeyword] = useState<string>();
  const resourceOptions = useResourceOptionsStore((state) => state.data);

  const init = async () => {
    const mlv2s = (await BApi.mediaLibraryV2.getAllMediaLibraryV2()).data ?? [];
    const data: Category[] = [];

    if (mlv2s.length > 0) {
      data.push({
        id: 0,
        name: t<string>("Media Libraries"),
        libraries: mlv2s.map((ml) => ({
          id: ml.id,
          name: ml.name,
        })),
      });
    }
    setCategories(data);
  };

  useEffect(() => {
    init();
  }, []);

  const recentlySelectedIds =
    resourceOptions?.idsOfMediaLibraryRecentlyMovedTo || [];

  const lowerCasedKeyword = keyword?.toLowerCase() ?? "";
  const filteredCategories =
    keyword != undefined && keyword?.length > 0
      ? categories
          .map((a) => {
            if (a.name.toLowerCase().indexOf(lowerCasedKeyword) > -1) {
              return a;
            } else {
              const filteredLibraries = (a.libraries || []).filter((b) => {
                return b.name.toLowerCase().includes(lowerCasedKeyword);
              });

              return {
                ...a,
                libraries: filteredLibraries,
              };
            }
          })
          .filter((c) => c.libraries.length > 0)
      : categories;

  return (
    <Modal
      footer={false}
      size={"lg"}
      title={t<string>("Select a media library")}
      visible={visible}
      onClose={() => {
        setVisible(false);
      }}
      onDestroyed={props.onDestroyed}
    >
      <div>
        <div>
          <Input
            placeholder={t<string>("Quick filter")}
            size={"sm"}
            startContent={<SearchOutlined className={"text-base"} />}
            value={keyword}
            onValueChange={(v) => setKeyword(v)}
          />
        </div>
        <div className={"py-2"}>
          {filteredCategories.map((c, ic) => {
            return (
              <>
                <div
                  className={"grid gap-12 items-center"}
                  style={{ gridTemplateColumns: "30% auto" }}
                >
                  <div>{c.name}</div>
                  <div>
                    {c.libraries.map((l, il) => {
                      const selectedRecently = recentlySelectedIds.includes(
                        l.id,
                      );

                      return (
                        <Button
                          color={selectedRecently ? "success" : "primary"}
                          size={"sm"}
                          variant={"light"}
                          onClick={() => {
                            if (confirmation) {
                              createPortal(Modal, {
                                defaultVisible: true,
                                title: t<string>(
                                  "Are you sure to select this media library?",
                                ),
                                children: (
                                  <div className={"flex items-center gap-2"}>
                                    <Chip radius={"sm"}>{l.name}</Chip>
                                  </div>
                                ),
                                onOk: async () => {
                                  onSelect?.(l.id);
                                  setVisible(false);
                                },
                              });
                            } else {
                              onSelect?.(l.id);
                              setVisible(false);
                            }
                          }}
                        >
                          {l.name}
                          {selectedRecently && (
                            <HistoryOutlined className={"text-sm"} />
                          )}
                        </Button>
                      );
                    })}
                  </div>
                </div>
                {ic != filteredCategories.length - 1 && (
                  <Divider orientation={"horizontal"} />
                )}
              </>
            );
          })}
        </div>
      </div>
    </Modal>
  );
};

MediaLibrarySelectorV2.displayName = "MediaLibrarySelectorV2";

export default MediaLibrarySelectorV2;
