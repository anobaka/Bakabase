"use client";

import React, { useEffect, useRef, useState } from "react";
import "./index.scss";
import { useUpdateEffect } from "react-use";
import { useTranslation } from "react-i18next";

import BApi from "@/sdk/BApi";
import ComponentCard from "@/pages/custom-component/components/ComponentCard";

import type { ComponentType } from "@/sdk/constants";

import {
  ComponentDescriptorAdditionalItem,
  ComponentDescriptorType,
} from "@/sdk/constants";
import { MdAddCircle } from "react-icons/md";
import ComponentDetail from "@/pages/custom-component/Detail";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

export default ({
  componentType,
  value: propsValue = undefined,
  onChange,
  maxCount = 1,
}: {
  componentType: ComponentType;
  value?: string[];
  onChange?: (v: string[]) => void;
  maxCount?: number;
}) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [allCurrentTypeComponents, setAllCurrentTypeComponents] = useState([]);
  const [value, setValue] = useState(propsValue ?? []);
  const adjustValue = (v) =>
    v?.filter((a) => allCurrentTypeComponents.some((b) => b.id == a));
  const adjustedPropsValueRef = useRef();

  const loadAllComponents = async () => {
    const rsp = await BApi.component.getComponentDescriptors({
      type: componentType,
      additionalItems: ComponentDescriptorAdditionalItem.AssociatedCategories,
    });

    console.log(
      rsp.data.filter((b) => b.type != ComponentDescriptorType.Configurable),
    );
    setAllCurrentTypeComponents(
      rsp.data.filter((b) => b.type != ComponentDescriptorType.Configurable),
    );
  };

  useUpdateEffect(() => {
    if (adjustedPropsValueRef.current != value) {
      console.log(
        "[BasicCategoryComponent]OnChange",
        adjustedPropsValueRef.current,
        value,
      );
      onChange && onChange(value);
    }
  }, [value]);

  useUpdateEffect(() => {
    const av = adjustValue(propsValue);

    adjustedPropsValueRef.current = av;
    setValue(av);
    console.log(
      "[BasicCategoryComponent]On props value change",
      adjustedPropsValueRef.current,
      value,
    );
  }, [propsValue]);

  useEffect(() => {
    loadAllComponents();
  }, []);

  console.log("BasicCategoryComponentSelector", allCurrentTypeComponents);

  return (
    <div className={"basic-category-component-selector"}>
      {allCurrentTypeComponents?.map((c, i) => {
        return (
          <ComponentCard
            descriptor={c}
            selected={value.indexOf(c.id) > -1}
            onClick={() => {
              const newValue = value?.slice() ?? [];
              const idx = newValue.indexOf(c.id);

              if (idx > -1) {
                newValue.splice(idx, 1);
              } else {
                // console.log(JSON.parse(JSON.stringify(newValue)), newValue.length, maxCount);
                newValue.splice(0, newValue.length - maxCount + 1);
                newValue.push(c.id);
              }
              setValue(adjustValue(newValue));
            }}
            onDeleted={() => {
              loadAllComponents();
            }}
          />
        );
      })}
      <div
        className="add"
        onClick={() => {
          createPortal(ComponentDetail, {
            componentType: componentType,
            onClosed: (hasChanges) => {
              if (hasChanges) {
                loadAllComponents();
              }
            },
          });
        }}
      >
        <MdAddCircle />
        {t<string>("Add")}
      </div>
    </div>
  );
};
