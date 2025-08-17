"use client";

import { Button, Modal, Input, Chip } from "@/components/bakaui";
import { toast } from "@/components/bakaui";

import React, { useCallback, useRef, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import "./index.scss";
import i18n from "i18next";

import BApi from "@/sdk/BApi";
import { PasswordSearchOrder } from "@/sdk/constants";
import PasswordSelector from "@/components/PasswordSelector";
import { Tooltip } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

const QuickPasswordCount = 5;

interface IPassword {
  text: string;
  usedTimes: number;
  lastUsedAt: string;
}

interface Props {
  trigger: any;
  entry: {
    path: string;
  };
  passwords?: string[];
}

const loadTopNPasswords = async (
  type: PasswordSearchOrder,
): Promise<IPassword[]> => {
  const rsp = await BApi.password.searchPasswords({
    // @ts-ignore
    order: type,
    pageSize: QuickPasswordCount,
  });

  // @ts-ignore
  return rsp.data || [];
};
const DecompressBalloon = (props: Props) => {
  const { trigger, entry, passwords = [] } = props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const customPasswordRef = useRef<string>();

  const [recentPasswords, setRecentPasswords] = useState<IPassword[]>([]);
  const [frequentlyUsedPasswords, setFrequentlyUsedPasswords] = useState<
    IPassword[]
  >([]);

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
    setFrequentlyUsedPasswords(
      await loadTopNPasswords(PasswordSearchOrder.Frequency),
    );
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
      let label;
      let passwords: IPassword[];

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
          <div className={"secondary"}>
            <div className="tip">
              {t<string>(
                `Alternatively, you can choose a password from ${label} passwords:`,
              )}
              {passwords.length === 5 && (
                <Button
                  className={"show-more"}
                  size={"sm"}
                  variant={"light"}
                  color={"primary"}
                  onClick={openPasswordSelector}
                >
                  {t<string>("Show more")}
                </Button>
              )}
            </div>
            <div className="passwords">
              {passwords.map((p) => {
                return (
                  <Chip
                    key={p.text}
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
                    {/* | {p.lastUsedAt} */}
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
      autoFocus={false}
      className="fp-te-db"
      content={
        <div>
          <div className="common-tip">
            {t<string>(
              "Contents will be decompressed to the same directory as the compressed file.",
            )}
          </div>
          <div className="password-guides">
            {passwords.length > 0 && (
              <div className={"default"}>
                <Trans
                  i18nKey={"fp.te.db.defaultPassword"}
                  values={{
                    password: passwords[0],
                  }}
                >
                  By default, we will use{" "}
                  <Button variant={"light"} color={"primary"} size={"sm"}>
                    password
                  </Button>{" "}
                  as password.
                </Trans>
              </div>
            )}
            {passwords.length > 1 && (
              <div className={"secondary"}>
                <div className="tip">
                  {t<string>(
                    "Alternatively, you can choose a password from the following candidates:",
                  )}
                </div>
                <div className="passwords">
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

            <div className={"secondary"}>
              <div className="tip">
                {t<string>("Or you can use a custom password:")}
              </div>
              <Input
                isClearable
                placeholder={i18n.t<string>("Password")}
                size={"sm"}
                style={{ width: "100%" }}
                onValueChange={(v) => {
                  customPasswordRef.current = v;
                }}
                onKeyDown={(e) => {
                  e.stopPropagation();
                }}
                endContent={
                  <Button
                    size={"sm"}
                    variant={"flat"}
                    onClick={() => {
                      if (customPasswordRef.current) {
                        decompress(entry.path, customPasswordRef.current);
                      } else {
                        toast.danger(
                          i18n.t<string>("Password can not be empty"),
                        );
                      }
                    }}
                  >
                    {t<string>("Use custom password to decompress")}
                  </Button>
                }
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
