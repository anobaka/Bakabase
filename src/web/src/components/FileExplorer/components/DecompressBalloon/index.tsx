"use client";

import { Button, Modal, Input, Chip } from "@/components/bakaui";
import { toast } from "@/components/bakaui";

import React, { useCallback, useRef, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import i18n from "i18next";

import BApi from "@/sdk/BApi";
import type {
  BakabaseInsideWorldModelsConstantsAosPasswordSearchOrder,
  BakabaseInsideWorldModelsModelsEntitiesPassword,
} from "@/sdk/Api";
import { PasswordSearchOrder } from "@/sdk/constants";
import PasswordSelector from "@/components/PasswordSelector";
import { Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const QuickPasswordCount = 5;

type Password = BakabaseInsideWorldModelsModelsEntitiesPassword;

interface Props {
  trigger: React.ReactElement;
  entry: {
    path: string;
  };
  passwords?: string[];
}

const loadTopNPasswords = async (type: PasswordSearchOrder): Promise<Password[]> => {
  const rsp = await BApi.password.searchPasswords({
    order: type as BakabaseInsideWorldModelsConstantsAosPasswordSearchOrder,
    pageSize: QuickPasswordCount,
  });

  return rsp.data || [];
};
const DecompressBalloon = (props: Props) => {
  const { trigger, entry, passwords = [] } = props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const customPasswordRef = useRef<string>();

  const [recentPasswords, setRecentPasswords] = useState<Password[]>([]);
  const [frequentlyUsedPasswords, setFrequentlyUsedPasswords] = useState<Password[]>([]);

  const decompress = useCallback((path: string, password?: string) => {
    // Notification.open({
    //   title: t<string>('Start decompressing'),
    //   type: 'success',
    //   // content:
    //   //   "This is the content of the notification. This is the content of the notification. This is the content of the notification.",
    //   // type
    // });
    toast.default(t<string>("Start decompressing"));

    return BApi.file.decompressFiles({
      paths: [path],
      password,
    });
  }, []);

  const onBalloonVisible = useCallback(async () => {
    setRecentPasswords(await loadTopNPasswords(PasswordSearchOrder.Latest));
    setFrequentlyUsedPasswords(await loadTopNPasswords(PasswordSearchOrder.Frequency));
  }, []);

  const openPasswordSelector = useCallback(() => {
    PasswordSelector.show({
      onSelect: (password: string) => {
        decompress(entry.path, password);
      },
    });
  }, []);

  const renderTopNPasswords = useCallback(
    (type: PasswordSearchOrder) => {
      let label: string;
      let passwords: Password[];

      switch (type) {
        case PasswordSearchOrder.Latest:
          label = "recently used";
          passwords = recentPasswords;
          break;
        case PasswordSearchOrder.Frequency:
          label = "frequently used";
          passwords = frequentlyUsedPasswords;
          break;
      }
      if (passwords.length > 0) {
        return (
          <div className="rounded p-2.5 shadow-[rgba(0,0,0,0.05)_0px_0px_0px_1px,rgb(209,213,219)_0px_0px_0px_1px_inset]">
            <div className="mb-1.5">
              {t<string>(`Alternatively, you can choose a password from ${label} passwords:`)}
              {passwords.length === 5 && (
                <Button
                  color={"primary"}
                  size={"sm"}
                  variant={"light"}
                  onClick={openPasswordSelector}
                >
                  {t<string>("Show more")}
                </Button>
              )}
            </div>
            <div className="flex flex-wrap gap-1.5">
              {passwords.map((p) => {
                return (
                  <Chip
                    key={p.text}
                    className="cursor-pointer"
                    size={"sm"}
                    onClick={() => {
                      decompress(entry.path, p.text);
                    }}
                    onClose={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("Delete password from history?"),
                        children: t<string>(
                          "Are you sure you want to delete this password from history?",
                        ),
                        onOk: () => BApi.password.deletePassword(p.text),
                      });
                    }}
                  >
                    {p.text}
                  </Chip>
                );
              })}
            </div>
          </div>
        );
      }

      return;
    },
    [recentPasswords, frequentlyUsedPasswords],
  );

  return (
    <Tooltip
      content={
        <div>
          <div className="my-1.5">
            {t<string>(
              "Contents will be decompressed to the same directory as the compressed file.",
            )}
          </div>
          <div className="flex flex-col gap-2.5">
            {passwords.length > 0 && (
              <div>
                <Trans
                  i18nKey={"fp.te.db.defaultPassword"}
                  values={{
                    password: passwords[0],
                  }}
                >
                  By default, we will use&nbsp;
                  <Button color={"primary"} size={"sm"} variant={"light"}>
                    password
                  </Button>
                  &nbsp; as password.
                </Trans>
              </div>
            )}
            {passwords.length > 1 && (
              <div className="rounded p-2.5 shadow-[rgba(0,0,0,0.05)_0px_0px_0px_1px,rgb(209,213,219)_0px_0px_0px_1px_inset]">
                <div className="mb-1.5">
                  {t<string>(
                    "Alternatively, you can choose a password from the following candidates:",
                  )}
                </div>
                <div className="flex flex-wrap gap-1.5">
                  {passwords.slice(1).map((password: string) => (
                    <Button
                      key={password}
                      size={"sm"}
                      variant={"flat"}
                      onClick={() => {
                        decompress(entry.path, password);
                      }}
                    >
                      {password}
                    </Button>
                  ))}
                </div>
              </div>
            )}

            {renderTopNPasswords(PasswordSearchOrder.Latest)}
            {renderTopNPasswords(PasswordSearchOrder.Frequency)}

            <div className="rounded p-2.5 shadow-[rgba(0,0,0,0.05)_0px_0px_0px_1px,rgb(209,213,219)_0px_0px_0px_1px_inset]">
              <div className="mb-1.5">{t<string>("Or you can use a custom password:")}</div>
              <Input
                isClearable
                endContent={
                  <Button
                    size={"sm"}
                    variant={"flat"}
                    onClick={() => {
                      if (customPasswordRef.current) {
                        decompress(entry.path, customPasswordRef.current);
                      } else {
                        toast.danger(i18n.t<string>("Password can not be empty"));
                      }
                    }}
                  >
                    {t<string>("Use custom password to decompress")}
                  </Button>
                }
                placeholder={i18n.t<string>("Password")}
                size={"sm"}
                style={{ width: "100%" }}
                onKeyDown={(e) => {
                  e.cancelable = true;
                }}
                onMouseDown={(e) => {
                  e.stopPropagation();
                }}
                onClick={(e) => {
                  e.stopPropagation();
                }}
                onDoubleClick={(e) => {
                  e.stopPropagation();
                }}
                onValueChange={(v) => {
                  customPasswordRef.current = v;
                }}
              />
            </div>
          </div>
        </div>
      }
      delay={500}
      placement={"left"}
      onClick={() => {}}
      onOpenChange={(v) => {
        if (v) {
          onBalloonVisible();
        }
      }}
    >
      {React.cloneElement(trigger, {
        onContextMenu: () => {
          // e.stopPropagation();
          // e.preventDefault();
          // setVisible(true);
        },
        onClick: () => {
          // console.log(e);
          // e.stopPropagation();
          // e.preventDefault();
          if (trigger.props.onClick) {
            trigger.props.onClick();
          }
          decompress(entry.path);
        },
      })}
    </Tooltip>
  );
};

DecompressBalloon.displayName = "DecompressBalloon";

export default DecompressBalloon;
