"use client";

import type { ValueRendererProps } from "../models";

import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  FileAddOutlined,
  LoadingOutlined,
} from "@ant-design/icons";
import { Img } from "react-image";
import React from "react";
import { MdAttachFile } from "react-icons/md";

import envConfig from "@/config/env";
import NotSet from "@/components/StandardValue/ValueRenderer/Renderers/components/NotSet";
import { Button } from "@/components/bakaui";
import { splitPathIntoSegments } from "@/components/utils";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";

type AttachmentValueRendererProps = Omit<
  ValueRendererProps<string[]>,
  "variant"
> & {
  variant: ValueRendererProps<string[]>["variant"];
  size?: "sm" | "md" | "lg";
};
const AttachmentValueRenderer = ({
  value,
  variant,
  editor,
  size,
  ...props
}: AttachmentValueRendererProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const v = variant ?? "default";

  const editable = !!editor;

  switch (v) {
    case "default":
      return (
        <div className={"flex items-center gap-2 flex-wrap"}>
          {value?.map((v) => {
            const pathSegments = splitPathIntoSegments(v);

            return (
              <div
                className={"flex flex-col gap-1 max-w-[100px] relative group"}
              >
                <Img
                  alt={""}
                  loader={<LoadingOutlined className={"text-2xl"} />}
                  src={[
                    `${envConfig.apiEndpoint}/tool/thumbnail?path=${encodeURIComponent(v)}`,
                  ]}
                  style={{
                    maxWidth: 100,
                    maxHeight: 100,
                  }}
                  title={v}
                  unloader={<MdAttachFile className={"text-2xl"} />}
                />
                <Button
                  isIconOnly
                  color={'danger'}
                  style={{ transform: 'translate(50%, -50%)', zIndex: 1 }}
                  onClick={() => {
                    const newValue = value.filter(e => e != v);
                    editor?.onValueChange?.(newValue, newValue);
                  }}
                  size={size}
                  // variant={'light'}
                  className={'top-0 right-0 absolute hidden group-hover:block'}
                >
                  <DeleteOutlined className={"text-lg"} />
                </Button>
              </div>
            );
          })}
          {editable && (
            <div
              className={
                "flex items-center justify-center w-[80px] h-[80px] border-1 rounded"
              }
              style={{ borderColor: "var(--bakaui-overlap-background)" }}
            >
              <Button
                isIconOnly
                color={"primary"}
                size={size}
                variant={"light"}
                onClick={() => {
                  createPortal(FileSystemSelectorModal, {
                    targetType: "file",
                    onSelected: (entry) => {
                      const newValue = (value ?? []).concat([entry.path]);

                      editor?.onValueChange?.(newValue, newValue);
                    },
                  });
                }}
              >
                <FileAddOutlined className={"text-lg"} />
              </Button>
            </div>
          )}
        </div>
      );
    case "light":
      if (!value || value.length == 0) {
        return <NotSet />;
      } else {
        return <span className={"break-all"}>{value.join(",")}</span>;
      }
  }
};

AttachmentValueRenderer.displayName = "AttachmentValueRenderer";

export default AttachmentValueRenderer;
