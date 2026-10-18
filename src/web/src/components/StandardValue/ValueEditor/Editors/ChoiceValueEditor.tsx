"use client";

import type { OptionDisplayProps } from "../../OptionDisplayProps";
import type { ValueEditorProps } from "../models";
import type { DestroyableProps } from "@/components/bakaui/types";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { SearchOutlined } from "@ant-design/icons";

import OrderableChoiceOptions from "../../ValueRenderer/Renderers/components/OrderableChoiceOptions";

import {
  useDisabledChoiceKeys,
  type DisabledChoiceKeysSource,
} from "@/hooks/useDisabledChoiceKeys";
import { Button, Input, Modal } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";

type Data = { value: string; label: string };

type ChoiceValueEditorProps = ValueEditorProps<string[] | undefined> &
  DestroyableProps &
  OptionDisplayProps & {
    disabledKeys?: ReadonlySet<string>;
    disabledKeysSource?: DisabledChoiceKeysSource;
    multiple: boolean;
    getDataSource: () => Promise<Data[] | undefined>;
  };

const log = buildLogger("ChoiceValueEditor");
const ChoiceValueEditor = (props: ChoiceValueEditorProps) => {
  const { t } = useTranslation();
  const {
    renderOptionExtra,
    optionsDescription,
    optionOrdering,
    disabledKeys: initialDisabledKeys,
    disabledKeysSource,
    multiple,
    getDataSource,
    value: propsValue,
    onValueChange,
  } = props;

  const disabledKeys = useDisabledChoiceKeys(disabledKeysSource, initialDisabledKeys);

  const [dataSource, setDataSource] = useState<Data[]>([]);
  const [keyword, setKeyword] = useState("");
  const [value, setValue] = useState<string[]>(propsValue ?? []);

  log(props);

  useEffect(() => {
    loadData();
  }, []);

  const loadData = async () => {
    const data = (await getDataSource()) ?? [];

    if (data.length > 0) {
      setDataSource(data);
    }
  };

  const visible = dataSource.filter(
    (choice) => !keyword || choice.label.toLocaleLowerCase().includes(keyword.toLocaleLowerCase()),
  );
  const renderOption = (d: Data) => (
    <Button
      key={d.value}
      color={value.includes(d.value) ? "primary" : "default"}
      isDisabled={disabledKeys?.has(d.value) && !value.includes(d.value)}
      size={"sm"}
      onClick={() => {
        if (disabledKeys?.has(d.value) && !value.includes(d.value)) return;

        if (multiple) {
          log("value", value, "select", d.value, "includes", value.includes(d.value));
          if (value.includes(d.value)) {
            setValue(value.filter((v) => v !== d.value));
          } else {
            setValue([...(value || []), d.value]);
          }
        } else {
          setValue(value.includes(d.value) ? [] : [d.value]);
        }
      }}
    >
      {d.label}
      {renderOptionExtra?.({ value: d.value, label: d.label })}
    </Button>
  );

  return (
    <Modal
      defaultVisible
      size={dataSource.length > 10 ? "xl" : "lg"}
      title={t<string>("Select data")}
      onOk={async () => {
        const validValues = value.filter((v) => dataSource.some((d) => d.value == v));

        onValueChange?.(
          validValues,
          validValues
            .map((v) => dataSource.find((x) => x.value == v)?.label)
            .filter((x) => x) as string[],
        );
      }}
    >
      <div>
        <Input
          placeholder={t<string>("common.placeholder.search")}
          size={"sm"}
          startContent={<SearchOutlined className={"text-small"} />}
          value={keyword}
          onValueChange={(v) => {
            setKeyword(v);
          }}
        />
      </div>
      {optionsDescription}
      {dataSource.length === 0 ? (
        <div>{t<string>("No choices available, please check your configurations")}</div>
      ) : optionOrdering ? (
        <OrderableChoiceOptions
          choices={visible}
          ordering={optionOrdering}
          renderOption={renderOption}
        />
      ) : (
        <div className="flex flex-wrap gap-1">{visible.map(renderOption)}</div>
      )}
    </Modal>
  );
};

ChoiceValueEditor.displayName = "ChoiceValueEditor";

export default ChoiceValueEditor;
