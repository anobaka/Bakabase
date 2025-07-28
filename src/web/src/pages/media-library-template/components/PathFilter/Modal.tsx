"use client";

import type { PathFilter } from "@/pages/media-library-template/models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { BsFileEarmark, BsFolder } from "react-icons/bs";
import { useTranslation } from "react-i18next";
import { useState } from "react";
import { AiOutlineDelete } from "react-icons/ai";

import {
  Button,
  Divider,
  Input,
  Modal,
  Radio,
  RadioGroup,
  Select,
} from "@/components/bakaui";
import { PathFilterFsType } from "@/pages/media-library-template/models";
import {
  pathFilterFsTypes,
  PathPositioner,
  pathPositioners,
} from "@/sdk/constants";
import ExtensionsInput from "@/components/ExtensionsInput";
import ExtensionGroupSelect from "@/components/ExtensionGroupSelect";

type Props = {
  filter?: PathFilter;
  onSubmit?: (filter: PathFilter) => Promise<any>;
} & DestroyableProps;
const PathFilterModal = ({ filter: propsFilter, onSubmit, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [filter, setFilter] = useState<Partial<PathFilter>>(
    propsFilter ?? { positioner: PathPositioner.Layer },
  );

  const renderFsItem = (type: PathFilterFsType) => {
    switch (type) {
      case PathFilterFsType.File:
        return (
          <div className={"inline-flex items-center gap-1"}>
            <BsFileEarmark />
            {t<string>("File")}
          </div>
        );
      case PathFilterFsType.Directory:
        return (
          <div className={"inline-flex items-center gap-1"}>
            <BsFolder />
            {t<string>("Folder")}
          </div>
        );
    }
  };

  const renderPositioner = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return (
          <Select
            isRequired
            dataSource={[0, 1, 2, 3, 4, 5, 6, 7, 8, 9].map((l) => ({
              label: l,
              value: l.toString(),
            }))}
            description={t<string>("Layer 0 is directory of media library")}
            label={t<string>("Layer")}
            selectedKeys={
              filter.layer == undefined ? undefined : [filter.layer.toString()]
            }
            onSelectionChange={(keys) => {
              const layer = parseInt(Array.from(keys)[0] as string, 10);

              setFilter({
                ...filter,
                layer,
              });
            }}
          />
        );
      case PathPositioner.Regex:
        return (
          <Input
            isRequired
            label={t<string>("Regex")}
            placeholder={t<string>("Regex to match sub path")}
            value={filter.regex}
            onValueChange={(v) => {
              setFilter({
                ...filter,
                regex: v,
              });
            }}
          />
        );
      default:
        return t<string>("Not supported");
    }
  };

  const isValid = () => {
    switch (filter.positioner) {
      case PathPositioner.Layer:
        return filter.layer != undefined && filter.layer >= 0;
      case PathPositioner.Regex:
        return filter.regex != undefined && filter.regex.length > 0;
      default:
        return false;
    }
  };

  console.log(
    filter,
    pathFilterFsTypes.map((t) => ({
      label: renderFsItem(t.value),
      value: t.value.toString(),
      textValue: t.label,
    })),
  );

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok", "cancel"],
        okProps: {
          isDisabled: !isValid(),
        },
      }}
      size={"lg"}
      onDestroyed={onDestroyed}
      onOk={async () => await onSubmit?.(filter as PathFilter)}
    >
      <div className={"flex flex-col gap-2"}>
        <RadioGroup
          isRequired
          label={t<string>("Positioning")}
          orientation="horizontal"
          value={filter.positioner?.toString()}
          onValueChange={(v) => {
            const nv = parseInt(v, 10);

            if (filter.positioner != nv) {
              setFilter({
                positioner: nv,
              });
            }
          }}
        >
          {pathPositioners.map((p) => (
            <Radio value={p.value.toString()}>{t<string>(p.label)}</Radio>
          ))}
        </RadioGroup>
        {filter.positioner && (
          <>
            {renderPositioner()}
            <Divider className="my-4" />
            <Select
              dataSource={pathFilterFsTypes.map((t) => ({
                label: renderFsItem(t.value),
                value: t.value.toString(),
                textValue: t.label,
              }))}
              disallowEmptySelection={false}
              endContent={
                <Button
                  isIconOnly
                  color={"danger"}
                  size={"sm"}
                  variant={"light"}
                  onPress={() => {
                    setFilter({
                      ...filter,
                      fsType: undefined,
                    });
                  }}
                >
                  <AiOutlineDelete className={"text-base"} />
                </Button>
              }
              label={t<string>("Limit path type")}
              placeholder={t<string>("No limited")}
              renderValue={(items) => {
                return items.map((t) =>
                  renderFsItem(
                    parseInt((t.data as { value: string })!.value, 10),
                  ),
                );
              }}
              selectedKeys={filter.fsType ? [filter.fsType.toString()] : []}
              selectionMode={"single"}
              onSelectionChange={(keys) => {
                const fsType: PathFilterFsType = parseInt(
                  Array.from(keys)[0] as string,
                  10,
                );

                setFilter({
                  ...filter,
                  fsType,
                });
              }}
            />
            {(filter.fsType == undefined ||
              filter.fsType == PathFilterFsType.File) && (
              <>
                <ExtensionGroupSelect
                  value={filter.extensionGroupIds}
                  onSelectionChange={(ids) => {
                    setFilter({
                      ...filter,
                      extensionGroupIds: ids,
                    });
                  }}
                />
                <ExtensionsInput
                  defaultValue={filter.extensions}
                  label={t<string>("Limit file extensions")}
                  onValueChange={(v) => {
                    setFilter({
                      ...filter,
                      extensions: v,
                    });
                  }}
                />
              </>
            )}
          </>
        )}
        {/* <pre>{JSON.stringify(filter)}</pre> */}
      </div>
    </Modal>
  );
};

PathFilterModal.displayName = "PathFilterModal";

export default PathFilterModal;
