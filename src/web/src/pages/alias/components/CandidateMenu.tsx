import { useState } from "react";
import { useTranslation } from "react-i18next";
import { DeleteOutlined, EditOutlined, MoreOutlined, ToTopOutlined } from "@ant-design/icons";

import { Button, Popover } from "@/components/bakaui";

export default function CandidateMenu({
  text,
  isDisabled,
  onPreferred,
  onRename,
  onDelete,
}: {
  text: string;
  isDisabled?: boolean;
  onPreferred: () => void;
  onRename: () => void;
  onDelete: () => void;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const action = (callback: () => void) => {
    setOpen(false);
    callback();
  };

  return (
    <Popover
      placement="bottom-start"
      trigger={
        <Button
          aria-label={t("alias.action.manageAlias", { text })}
          className="h-auto min-h-8 min-w-0 max-w-full gap-2 rounded-lg px-2.5 py-1 text-left"
          endContent={<MoreOutlined aria-hidden className="shrink-0 text-default-400" />}
          isDisabled={isDisabled}
          size="sm"
          variant="flat"
        >
          <span className="min-w-0 whitespace-normal break-words">{text}</span>
        </Button>
      }
      visible={open}
      onVisibleChange={setOpen}
    >
      <div className="flex max-w-xs flex-col gap-0.5 p-1">
        <p className="max-w-full break-words px-2 py-1 text-xs text-default-400">{text}</p>
        <Button
          className="justify-start"
          size="sm"
          startContent={<ToTopOutlined aria-hidden />}
          variant="light"
          onPress={() => action(onPreferred)}
        >
          {t("alias.action.setAsPreferred")}
        </Button>
        <Button
          className="justify-start"
          size="sm"
          startContent={<EditOutlined aria-hidden />}
          variant="light"
          onPress={() => action(onRename)}
        >
          {t("alias.action.rename")}
        </Button>
        <Button
          className="justify-start"
          color="danger"
          size="sm"
          startContent={<DeleteOutlined aria-hidden />}
          variant="light"
          onPress={() => action(onDelete)}
        >
          {t("common.action.delete")}
        </Button>
      </div>
    </Popover>
  );
}
