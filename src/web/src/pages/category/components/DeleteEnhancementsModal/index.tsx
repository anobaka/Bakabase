"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import { QuestionCircleOutlined } from "@ant-design/icons";
import React from "react";
import { useTranslation } from "react-i18next";

import { Checkbox, Modal, Tooltip } from "@/components/bakaui";

type Props = {
  title: string;
  onOk: (deleteEmptyOnly: boolean) => Promise<any>;
} & DestroyableProps;

export default ({ title, onOk, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [deleteEmptyOnly, setDeleteEmptyOnly] = React.useState(false);

  return (
    <Modal
      defaultVisible
      title={title}
      onDestroyed={onDestroyed}
      onOk={async () => await onOk(deleteEmptyOnly)}
    >
      <div className={"flex flex-col gap-4"}>
        <div className={"flex items-center gap-1"}>
          <Checkbox size={"sm"} onValueChange={(v) => setDeleteEmptyOnly(v)}>
            {t<string>("Delete empty records only")}
          </Checkbox>
          <Tooltip
            className={"max-w-[600px]"}
            color={"secondary"}
            content={
              <div>
                <div>
                  {t<string>(
                    "When the enhancement is successfully executed but no data available for enhancement is retrieved, an empty enhancement record is generated.",
                  )}
                </div>
                <div>
                  {t<string>(
                    "Enhancement will only be executed when there are no existing enhancement records, so in some cases, you may only need to delete the empty enhancement records.",
                  )}
                </div>
              </div>
            }
          >
            <QuestionCircleOutlined className={"text-base"} />
          </Tooltip>
        </div>
        <div>
          {t<string>(
            "This operation cannot be undone. Would you like to proceed?",
          )}
        </div>
      </div>
    </Modal>
  );
};
