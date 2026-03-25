"use client";

import { useTranslation } from "react-i18next";
import { useState } from "react";
import { Checkbox } from "@/components/bakaui";
import { ExclamationCircleOutlined } from "@ant-design/icons";

type Props = {
  count: number;
  onDeleteFilesChange?: (deleteFiles: boolean) => void;
};

const DeleteResourceConfirmContent = ({ count, onDeleteFilesChange }: Props) => {
  const { t } = useTranslation();
  const [deleteFiles, setDeleteFiles] = useState(false);

  return (
    <div>
      <div className={"font-bold"}>
        {t<string>("resource.contextMenu.confirmDeleteCount", { count })}
      </div>
      <div className={"flex items-start gap-2 mt-3 p-2 rounded bg-warning-50 text-warning-700 text-sm"}>
        <ExclamationCircleOutlined className={"text-base mt-0.5 shrink-0"} />
        <span>{t<string>("resource.contextMenu.deleteDataOnlyWarning")}</span>
      </div>
      <div className={"mt-3"}>
        <Checkbox
          size="sm"
          isSelected={deleteFiles}
          onValueChange={(v) => {
            setDeleteFiles(v);
            onDeleteFilesChange?.(v);
          }}
        >
          {t<string>("resource.contextMenu.deleteFilesOption")}
        </Checkbox>
      </div>
    </div>
  );
};

DeleteResourceConfirmContent.displayName = "DeleteResourceConfirmContent";

export default DeleteResourceConfirmContent;
