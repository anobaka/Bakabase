"use client";

import "./index.scss";
import type { BakabaseAbstractionsModelsDomainComponentDescriptor } from "@/sdk/Api";

import React, { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Input, Textarea } from "@/components/bakaui";
import { toast } from "@/components/bakaui";
import {
  ComponentDescriptorAdditionalItem,
  ComponentDescriptorType,
  ComponentType,
} from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import ComponentDescriptorCard from "@/pages/custom-component/components/ComponentCard";
import ComponentOptionsRenderPage from "@/pages/custom-component/Detail/ComponentOptions/ComponentOptionsRender";

export interface CustomComponentDetailDialogProps {
  componentType?: ComponentType;
  componentKey?: string | number;
  onClosed?: (hasChanges: boolean) => void;
  onDestroyed?: () => void;
}

function ComponentDetailPage(props: CustomComponentDetailDialogProps) {
  const {
    componentKey,
    componentType,
    onClosed = () => {},
    onDestroyed,
  } = props;

  const { t } = useTranslation();
  const [descriptor, setDescriptor] = useState<
    BakabaseAbstractionsModelsDomainComponentDescriptor | undefined
  >();
  const [baseDescriptors, setBaseDescriptors] = useState<
    BakabaseAbstractionsModelsDomainComponentDescriptor[]
  >([]);
  const [baseDescriptor, setBaseDescriptor] = useState<
    BakabaseAbstractionsModelsDomainComponentDescriptor | undefined
  >(undefined);
  const [visible, setVisible] = useState(true);
  const optionsFormRef = useRef<any>();

  const init = async () => {
    let d: BakabaseAbstractionsModelsDomainComponentDescriptor | undefined;

    if (componentKey !== undefined) {
      const rsp = await BApi.component.getComponentDescriptorByKey({
        key: componentKey.toString(),
        additionalItems: ComponentDescriptorAdditionalItem.AssociatedCategories,
      });

      d = rsp.data;
    } else {
      d = {
        componentType,
      } as BakabaseAbstractionsModelsDomainComponentDescriptor;
    }
    setDescriptor(d);

    if (d?.componentType) {
      const dsRsp = await BApi.component.getComponentDescriptors({
        type: d.componentType,
      });
      const baseDs =
        dsRsp.data?.filter(
          (a) => a.type == ComponentDescriptorType.Configurable,
        ) ?? [];

      setBaseDescriptors(baseDs);

      if (baseDs.length > 0) {
        const bd =
          baseDs.find((a) => a.id == d?.id) ??
          baseDs.find((a) => a.componentType == d?.componentType);

        setBaseDescriptor(bd);
      }
    }
  };

  useEffect(() => {
    init();
    console.log("[CustomComponentDetail]Initialized", props);
  }, []);

  const optionsJsonSchema =
    baseDescriptor?.optionsJsonSchema &&
    JSON.parse(baseDescriptor.optionsJsonSchema);

  console.log(descriptor, baseDescriptor);

  const closeWithChangesRef = useRef(false);

  const close = (closeWithChanges: boolean) => {
    console.log("close?");
    closeWithChangesRef.current = closeWithChanges;
    setVisible(false);
  };

  return (
    <Modal
      defaultVisible
      size="xl"
      title={
        componentType
          ? t<string>(ComponentType[componentType])
          : t<string>("Component Detail")
      }
      visible={visible}
      onClose={() => close(false)}
      onDestroyed={onDestroyed}
      onOk={() =>
        new Promise<void>((resolve, reject) => {
          if (optionsFormRef.current?.validateForm()) {
            if (!(descriptor?.name?.length > 0)) {
              toast.danger(
                t<string>("{{key}} is not set", {
                  key: t<string>("Name"),
                }),
              );
              reject();

              return;
            }

            if (!baseDescriptor) {
              toast.danger(t<string>("Please select a component type"));
              reject();

              return;
            }

            const data = {
              name: descriptor.name,
              componentAssemblyQualifiedTypeName:
                baseDescriptor.assemblyQualifiedTypeName,
              description: descriptor?.description,
              json: descriptor?.optionsJson,
            };

            const invoke =
              descriptor?.optionsId && descriptor.optionsId > 0
                ? BApi.componentOptions.putComponentOptions
                : BApi.componentOptions.addComponentOptions;
            const args =
              descriptor?.optionsId && descriptor.optionsId > 0
                ? [descriptor.optionsId, data]
                : [data];

            console.log(args);

            invoke(...args)
              .then((a) => {
                if (a.code) {
                  reject();
                } else {
                  resolve();
                  close(true);
                }
              })
              .catch((e) => {
                reject(e);
              });
          } else {
            reject();
          }
        })
      }
    >
      <div className="space-y-4">
        <div>
          <div className="label">{t<string>("Type")}</div>
          <div className="value">
            <div className="base-components">
              {baseDescriptors.map((c) => {
                return (
                  <ComponentDescriptorCard
                    key={c.id}
                    descriptor={c}
                    selected={c == baseDescriptor}
                    onClick={() => {
                      setBaseDescriptor(c);
                    }}
                  />
                );
              })}
            </div>
          </div>
        </div>

        <div>
          <div className="label">{t<string>("Name")}</div>
          <div className="value">
            <Input
              value={descriptor?.name}
              onValueChange={(v) => {
                setDescriptor({
                  ...descriptor,
                  name: v,
                });
              }}
            />
          </div>
        </div>

        <div>
          <div className="label">{t<string>("Description")}</div>
          <div className="value">
            <Textarea
              placeholder={
                baseDescriptor?.description
                  ? t<string>(baseDescriptor?.description)
                  : undefined
              }
              value={descriptor?.description}
              onValueChange={(v) => {
                setDescriptor({
                  ...(descriptor || {}),
                  description: v,
                });
              }}
            />
          </div>
        </div>

        {optionsJsonSchema && (
          <div>
            <div className="label">{t<string>("Configuration")}</div>
            <div className="value">
              <ComponentOptionsRenderPage
                ref={optionsFormRef}
                defaultValue={
                  descriptor?.optionsJson
                    ? JSON.parse(descriptor?.optionsJson)
                    : undefined
                }
                schema={optionsJsonSchema}
                onChange={(v) => {
                  console.log("Options changed", v);
                  setDescriptor({
                    ...descriptor,
                    optionsJson: JSON.stringify(v),
                  });
                }}
              />
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}

export default ComponentDetailPage;
