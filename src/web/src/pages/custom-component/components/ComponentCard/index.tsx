"use client";

import "./index.scss";
import type { DOMAttributes } from "react";
import type { BakabaseInsideWorldModelsModelsDtosComponentDescriptor } from "@/sdk/Api";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { MdEdit, MdDelete } from "react-icons/md";
import { MdCheckCircle } from "react-icons/md";
import { Button } from "@heroui/react";

import { ComponentDescriptorType, ComponentType } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import ComponentDetailPage from "@/pages/custom-component/Detail";
import { extractEnhancerTargetDescription } from "@/components/utils";
import SimpleLabel from "@/components/SimpleLabel";
import { Modal } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

interface DescriptorCardProps extends DOMAttributes<unknown> {
  descriptor: BakabaseInsideWorldModelsModelsDtosComponentDescriptor;
  selected?: boolean;
  onDeleted?: () => void;
}

const TypeLabelProps = {
  [ComponentDescriptorType.Fixed]: {
    status: "default",
    label: "Reserved",
  },
  [ComponentDescriptorType.Instance]: {
    status: "info",
    label: "Custom",
  },
};
const ComponentCard = (props: DescriptorCardProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const {
    descriptor: propsDescriptor,
    selected,
    onDeleted,
    ...otherProps
  } = props;

  const [descriptor, setDescriptor] = useState(propsDescriptor);
  const labelProps = TypeLabelProps[descriptor.type];
  const domRef = useRef();

  useEffect(() => {
    setDescriptor(propsDescriptor);
  }, [propsDescriptor]);

  console.log("Rendering", props);

  const renderExtra = () => {
    switch (descriptor.componentType) {
      case ComponentType.Enhancer: {
        if (descriptor.targets?.length > 0) {
          const descriptions = descriptor.targets.map((t) =>
            extractEnhancerTargetDescription(t),
          );
          const descriptionGroups = descriptions.reduce<
            { type: string; keys: string[] }[]
          >((s, t) => {
            let g = s.find((g) => g.type == t.type);

            if (g == undefined) {
              g = {
                type: t.type,
                keys: [],
              };
              s.push(g);
            }
            g.keys.push(t.key);

            return s;
          }, []);

          return (
            <div className={"target-groups"}>
              {descriptionGroups.map((t, i) => {
                return (
                  <div key={t.type} className={"group"}>
                    {t.type}
                    {t.keys.map((k) => (
                      <SimpleLabel key={k} status={"default"}>
                        {k}
                      </SimpleLabel>
                    ))}
                  </div>
                );
              })}
            </div>
          );
        }

        return;
      }
      default:
        return;
    }
  };

  const categories = descriptor?.associatedCategories ?? [];

  return (
    <div
      className={`component-card ${selected ? "selected" : ""}`}
      {...otherProps}
      ref={domRef}
    >
      {selected && <MdCheckCircle className={"selected-icon text-xl"} />}
      {descriptor.type == ComponentDescriptorType.Instance && (
        <div className={"top-right-operations"}>
          <Button
            isIconOnly
            color={"default"}
            size={"sm"}
            variant={"light"}
            onPress={(e) => {
              // e.preventDefault();
              // e.stopPropagation();
              createPortal(ComponentDetailPage, {
                componentType: descriptor.componentType,
                componentKey: descriptor.id,
                onClosed: (hasChanges) => {
                  if (hasChanges) {
                    BApi.component
                      .getComponentDescriptorByKey({
                        key: descriptor.optionsId,
                      })
                      .then((a) => {
                        setDescriptor(a.data);
                      });
                  }
                },
              });
            }}
          >
            <MdEdit className={"text-base"} />
          </Button>
          <Button
            isIconOnly
            color={"danger"}
            size={"sm"}
            variant={"light"}
            onPress={(e) => {
              // e.preventDefault();
              // e.stopPropagation();
              createPortal(Modal, {
                defaultVisible: true,
                title: t<string>("Sure to delete?"),
                children: t<string>(
                  "Are you sure you want to delete this component?",
                ),
                onOk: () =>
                  BApi.componentOptions
                    .removeComponentOptions(descriptor.optionsId)
                    .then((a) => {
                      if (!a.code) {
                        if (onDeleted) {
                          onDeleted();
                        }
                      }
                    }),
              });
            }}
          >
            <MdDelete className={"text-base"} />
          </Button>
        </div>
      )}
      <div className="top">
        <div className="name">
          {labelProps && (
            <>
              <SimpleLabel status={labelProps.status}>
                {t<string>(labelProps.label)}
              </SimpleLabel>
              &nbsp;
            </>
          )}
          {t<string>(descriptor.name)}
        </div>
        {descriptor.description && (
          <div className="description">{t<string>(descriptor.description)}</div>
        )}
        {renderExtra()}
      </div>
      <div className="bottom">
        {categories.length > 0 && (
          <div className="categories">
            <div className="label">
              {t<string>("Applied to {{count}} categories", {
                count: categories.length,
              })}
            </div>
          </div>
        )}
        <div className="versions">
          <SimpleLabel
            className={`version ${descriptor.version?.length > 0 ? "" : "empty"}`}
            status={"default"}
            title={`${t<string>("Version")}:${t<string>("May be different in incoming versions of app")}`}
          >
            {descriptor.version}
          </SimpleLabel>
          <SimpleLabel
            className={`version ${descriptor.dataVersion?.length > 0 ? "" : "empty"}`}
            title={`${t<string>("Data version")}:${t<string>("May be different after configuration change")}`}
          >
            {descriptor.dataVersion}
          </SimpleLabel>
        </div>
      </div>
    </div>
  );
};

ComponentCard.displayName = "ComponentCard";

export default ComponentCard;
