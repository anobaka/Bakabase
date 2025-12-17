"use client";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { PlusCircleOutlined } from "@ant-design/icons";
import { MdFlashlightOn, MdPlayCircle, MdSearch } from "react-icons/md";

import BApi from "@/sdk/BApi";
import {
  ComponentDescriptorAdditionalItem,
  ComponentDescriptorType,
  ComponentType,
  componentTypes,
} from "@/sdk/constants";
import "./index.scss";

import type { CustomComponentDetailDialogProps } from "./Detail";

import Detail from "@/pages/custom-component/Detail";
import ComponentDescriptorCard from "@/pages/custom-component/components/ComponentCard";
import { Alert, Button, Modal } from "@/components/bakaui";

const ComponentTypeIcons = {
  [ComponentType.Enhancer]: MdFlashlightOn,
  [ComponentType.Player]: MdPlayCircle,
  [ComponentType.PlayableFileSelector]: MdSearch,
};
const CustomComponentPage = () => {
  const { t } = useTranslation();
  const [allComponents, setAllComponents] = useState([]);

  const [selectedComponent, setSelectedComponent] =
    useState<CustomComponentDetailDialogProps>();

  const loadAllComponents = async () => {
    const rsp = await BApi.component.getComponentDescriptors({
      additionalItems: ComponentDescriptorAdditionalItem.AssociatedCategories,
    });

    setAllComponents(rsp.data);
  };

  useEffect(() => {
    loadAllComponents();
  }, []);

  const showDetail = (componentType, componentKey) => {
    setSelectedComponent({
      componentType,
      componentKey,
      onClosed: () => {
        setSelectedComponent(undefined);
      },
    });
  };

  return (
    <div className={"custom-component-page"}>
      <Modal
        defaultVisible
        footer={{ actions: ["cancel"] }}
        size={"lg"}
        title={t<string>("Custom components will be removed soon")}
      >
        <Alert
          color={"danger"}
          title={t<string>(
            "Please note that the custom components will be removed soon. Please use the new media library template.",
          )}
        />
      </Modal>
      {selectedComponent && (
        <Detail
          {...selectedComponent}
          onClosed={(hasChanges) => {
            setSelectedComponent(undefined);
            if (hasChanges) {
              loadAllComponents();
            }
          }}
        />
      )}
      {componentTypes
        .filter((x) => x.value != ComponentType.Enhancer)
        .map((ct) => {
          const components = allComponents.filter(
            (c) =>
              c.componentType == ct.value &&
              c.type == ComponentDescriptorType.Instance,
          );

          return (
            <div key={ct.value} className={"component-type"}>
              <div className="type-name">
                <div className="name flex items-center gap-1">
                  {React.createElement(ComponentTypeIcons[ct.value], {
                    className: "text-xl",
                  })}
                  {t<string>(ct.label)}
                </div>
                <Button
                  color={"primary"}
                  variant={"light"}
                  onClick={() => {
                    showDetail(ct.value);
                  }}
                >
                  <PlusCircleOutlined className={"text-base"} />
                  {t<string>("Add")}
                </Button>
              </div>
              {components.length > 0 ? (
                <div className="components">
                  {components.map((c) => {
                    return (
                      <ComponentDescriptorCard
                        key={c.id}
                        descriptor={c}
                        onDeleted={() => {
                          loadAllComponents();
                        }}
                      />
                    );
                  })}
                </div>
              ) : (
                <div className={"no-components"}>
                  {t<string>("Nothing here yet")}
                </div>
              )}
            </div>
          );
        })}
    </div>
  );
};

CustomComponentPage.displayName = "CustomComponentPage";

export default CustomComponentPage;
