"use client";

import { useCallback, useEffect, useState } from "react";

import { Badge, Button, Modal, Radio } from "@/components/bakaui";
import { createPortalOfComponent } from "@/components/utils";
import BApi from "@/sdk/BApi";
import { PasswordSearchOrder } from "@/sdk/constants";

import "./index.scss";
import { useTranslation } from "react-i18next";
import { MdDelete } from "react-icons/md";

import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface IProps {
  onSelect: (password: string) => any;
  afterClose?: () => any;
}

function PasswordSelector(props: IProps) {
  const { onSelect, afterClose } = props;
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [passwords, setPasswords] = useState<
    { text: string; usedTimes: number; lastUsedAt: string }[]
  >([]);
  const [visible, setVisible] = useState(true);
  const [order, setOrder] = useState(PasswordSearchOrder.Latest);

  const initialize = useCallback(async () => {
    const rsp = await BApi.password.getAllPasswords();

    // @ts-ignore
    setPasswords(rsp.data || []);
  }, []);

  useEffect(() => {
    initialize();
  }, []);

  const close = useCallback(() => {
    setVisible(false);
  }, []);

  const renderPasswords = useCallback(() => {
    const filtered = passwords.sort((a, b) =>
      order === PasswordSearchOrder.Latest
        ? b.lastUsedAt.localeCompare(a.lastUsedAt)
        : b.usedTimes - a.usedTimes,
    );

    return filtered.map((p) => {
      return (
        <div
          className="password"
          onClick={() => {
            onSelect(p.text);
            close();
          }}
        >
          <div className="top">
            <div className="text">{p.text}</div>
            <Button
              isIconOnly
              color={"danger"}
              size={"sm"}
              variant={"light"}
              onPress={() => {
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
              <MdDelete className={"text-base"} />
            </Button>
          </div>
          <div className="info">
            <div className="last-used-at">{p.lastUsedAt}</div>
            <Badge count={p.usedTimes} />
            {/* <div className="used-times">{p.usedTimes}</div> */}
          </div>
        </div>
      );
    });
  }, [passwords, order]);

  return (
    <Modal
      v2
      afterClose={afterClose}
      className={"password-selector-dialog"}
      closeMode={["close", "mask", "esc"]}
      footerActions={["cancel"]}
      title={t<string>("Passwords")}
      visible={visible}
      width={"auto"}
      onCancel={close}
      onClose={close}
    >
      <div className="orders">
        <div className="title">{t<string>("Order")}</div>
        <div className="content">
          <Radio.Group
            dataSource={[
              {
                label: t<string>("Used recently"),
                value: PasswordSearchOrder.Latest,
              },
              {
                label: t<string>("Used frequently"),
                value: PasswordSearchOrder.Frequency,
              },
            ]}
            value={order}
            onChange={(v) => {
              // @ts-ignore
              setOrder(v);
            }}
          />
        </div>
      </div>
      <div className="passwords">
        <div className="title">{t<string>("Passwords")}</div>
        <div className="content">{renderPasswords()}</div>
      </div>
    </Modal>
  );
}

PasswordSelector.show = (props) =>
  createPortalOfComponent(PasswordSelector, props);

export default PasswordSelector;
