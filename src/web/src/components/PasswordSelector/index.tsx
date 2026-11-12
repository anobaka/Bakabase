"use client";

import type { BakabaseAbstractionsModelsDbPasswordDbModel as SavedPassword } from "@/sdk/Api";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { MdDelete } from "react-icons/md";

import { Button, Modal, Radio, RadioGroup, Spinner, Tooltip } from "@/components/bakaui";
import { useResourceOwnerScope } from "@/features/management/resourceContext";
import BApi from "@/sdk/BApi";

interface Props {
  onSelect: (password: string) => void;
  onDestroyed?: () => void;
  selectionOnly?: boolean;
}

function PasswordSelector({ onSelect, onDestroyed, selectionOnly }: Props) {
  const { t } = useTranslation();
  const owner = useResourceOwnerScope();
  const device = owner?.key;
  const canRead = !owner || owner.source.isLocal;
  const [result, setResult] = useState<{ device?: string; values: SavedPassword[] | null }>();
  const [order, setOrder] = useState("latest");
  const [retry, setRetry] = useState(0);
  const [deleting, setDeleting] = useState<SavedPassword>();
  const [deleteFailed, setDeleteFailed] = useState(false);
  const [visible, setVisible] = useState(true);
  const close = () => {
    setVisible(false);
    onDestroyed?.();
  };

  useEffect(() => {
    if (!canRead) return;
    const request = new AbortController();
    setResult(undefined);
    BApi.password
      .getAllPasswords({ signal: request.signal })
      .then((response) => {
        if (!request.signal.aborted)
          setResult({ device, values: response.code === 0 ? (response.data ?? []) : null });
      })
      .catch(() => {
        if (!request.signal.aborted) setResult({ device, values: null });
      });
    return () => request.abort();
  }, [canRead, device, retry]);

  const passwords = result?.device === device ? result?.values : undefined;
  const sorted =
    passwords &&
    [...passwords].sort((a, b) =>
      order === "latest" ? b.lastUsedAt.localeCompare(a.lastUsedAt) : b.usedTimes - a.usedTimes,
    );

  return (
    <Modal
      visible={visible}
      onClose={close}
      size="lg"
      footer={false}
      title={t("passwordSelector.title")}
    >
      {!canRead || passwords === null ? (
        <div className="flex items-center gap-2">
          <span role="status">{t("passwordSelector.unavailable")}</span>
          {canRead && (
            <Button size="sm" onPress={() => setRetry((n) => n + 1)}>
              {t("common.action.retry")}
            </Button>
          )}
        </div>
      ) : !sorted ? (
        <Spinner size="sm" aria-label={t("common.state.loading")} />
      ) : !sorted.length ? (
        <p className="text-sm text-default-500">{t("passwordSelector.empty")}</p>
      ) : (
        <>
          <RadioGroup
            size="sm"
            label={t("passwordSelector.order")}
            orientation="horizontal"
            value={order}
            onValueChange={setOrder}
          >
            <Radio value="latest">{t("passwordSelector.latest")}</Radio>
            <Radio value="frequent">{t("passwordSelector.frequent")}</Radio>
          </RadioGroup>
          <ul className="flex min-w-0 flex-wrap items-start gap-1.5">
            {sorted.map((item) => (
              <li className="flex min-w-0 max-w-full items-center" key={item.text}>
                <Tooltip content={`${t("passwordSelector.latest")} · ${item.lastUsedAt} · ${t("passwordSelector.uses", { count: item.usedTimes })}`}
                  delay={650} placement="bottom" classNames={{ base: "pointer-events-none", content: "pointer-events-none" }}>
                  <Button
                    type="button"
                    size="sm"
                    variant="flat"
                    className="h-auto min-h-8 min-w-0 max-w-full whitespace-normal px-2.5 py-1 text-start"
                    onPress={() => {
                      onSelect(item.text);
                      close();
                    }}
                  >
                    <span className="min-w-0 whitespace-pre-wrap [overflow-wrap:anywhere]">{item.text}</span>
                  </Button>
                </Tooltip>
                {!selectionOnly && (
                  <Button
                    type="button"
                    isIconOnly
                    size="sm"
                    variant="light"
                    color="danger"
                    aria-label={t("passwordSelector.remove")}
                    onPress={() => {
                      setDeleteFailed(false);
                      setDeleting(item);
                    }}
                  >
                    <MdDelete aria-hidden />
                  </Button>
                )}
              </li>
            ))}
          </ul>
        </>
      )}
      {deleting && canRead && !selectionOnly && (
        <Modal
          visible
          onClose={() => setDeleting(undefined)}
          title={t("passwordSelector.removeConfirm")}
          okProps={{ color: "danger" }}
          onOk={async () => {
            try {
              const response = await BApi.password.deletePassword(deleting.text);
              if (response.code !== 0) throw new Error();
              setDeleting(undefined);
              setRetry((n) => n + 1);
            } catch {
              setDeleteFailed(true);
              throw new Error(t("passwordSelector.deleteFailed"));
            }
          }}
        >
          <span className="break-all">{deleting.text}</span>
          {deleteFailed && (
            <span role="alert" className="text-danger">
              {t("passwordSelector.deleteFailed")}
            </span>
          )}
        </Modal>
      )}
    </Modal>
  );
}

export default PasswordSelector;
