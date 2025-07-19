"use client";

import "./index.scss";
import type { DialogProps } from "@alifd/next/types/dialog";
import type { BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor } from "@/sdk/Api";

import React, { useEffect, useRef, useState } from "react";
import { Modal, Input } from "@/components/bakaui";
import i18n from "i18next";
import ReactDOM from "react-dom/client";

import { toast } from "@/components/bakaui";
import {
  ComponentDescriptorAdditionalItem,
  ComponentDescriptorType,
  ComponentType,
} from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import ComponentDescriptorCard from "@/pages/custom-component/components/ComponentCard";
import ComponentOptionsRender from "@/pages/custom-component/Detail/ComponentOptions/ComponentOptionsRender";
import { uuidv4 } from "@/components/utils";

export interface CustomComponentDetailDialogProps extends DialogProps {
  componentType?: ComponentType;
  componentKey?: string | number;
  onClosed?: (hasChanges) => void;
}

function ComponentDetail(props: CustomComponentDetailDialogProps) {
  const {
    componentKey,
    componentType,
    onClosed = () => {},
    afterClose,
    ...otherProps
  } = props;

  const [descriptor, setDescriptor] = useState<
    | BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor
    | undefined
  >();
  const [baseDescriptors, setBaseDescriptors] = useState<
    BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor[]
  >([]);
  const [baseDescriptor, setBaseDescriptor] =
    useState<BakabaseInsideWorldBusinessComponentsResourceComponentsComponentDescriptor>(
      undefined,
    );
  const [visible, setVisible] = useState(true);
  const optionsFormRef = useRef();

  const init = async () => {
    let d;

    if (componentKey !== undefined) {
      const rsp = await BApi.component.getComponentDescriptorByKey({
        key: componentKey.toString(),
        additionalItems: ComponentDescriptorAdditionalItem.AssociatedCategories,
      });

      d = rsp.data;
    } else {
      d = {
        componentType,
      };
    }
    setDescriptor(d);

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
        baseDs.find((a) => a.id == d.componentKey) ??
        baseDs.find((a) => a.componentType == d.componentType);

      setBaseDescriptor(bd);
    }
  };

  useEffect(() => {
    init();
    console.log("[CustomComponentDetail]Initialized", props, otherProps);
  }, []);

  const optionsJsonSchema =
    baseDescriptor?.optionsJsonSchema &&
    JSON.parse(baseDescriptor.optionsJsonSchema);

  console.log(descriptor, baseDescriptor);

  const closeWithChangesRef = useRef(false);

  const close = (closeWithChanges) => {
    console.log("close?");
    closeWithChangesRef.current = closeWithChanges;
    setVisible(false);
  };

  return (
    <Modal
      centered
      v2
      afterClose={() => {
        console.log("closed");
        if (onClosed) {
          onClosed(closeWithChangesRef.current);
        }
        if (afterClose) {
          afterClose();
        }
      }}
      className={"custom-component-detail-dialog"}
      closeMode={["close", "esc", "mask"]}
      title={i18n.t<string>(ComponentType[componentType])}
      visible={visible}
      width={"auto"}
      onCancel={() => close(false)}
      onClose={() => close(false)}
      onOk={() =>
        new Promise((resolve, reject) => {
          if (optionsFormRef.current.validateForm()) {
            if (!(descriptor.name?.length > 0)) {
              return toast.error(
                i18n.t<string>("{{key}} is not set", {
                  key: i18n.t<string>("Name"),
                }),
              );
            }

            const data = {
              name: descriptor.name,
              componentAssemblyQualifiedTypeName:
                baseDescriptor.assemblyQualifiedTypeName,
              description: descriptor?.description,
              json: descriptor?.optionsJson,
            };

            const invoke =
              descriptor?.optionsId > 0
                ? BApi.componentOptions.putComponentOptions
                : BApi.componentOptions.addComponentOptions;
            const args =
              descriptor?.optionsId > 0
                ? [descriptor?.optionsId, data]
                : [data];

            console.log(args);

            invoke(...args)
              .then((a) => {
                if (a.code) {
                  reject();
                } else {
                  resolve(a);
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
      {...(otherProps || {})}
    >
      <div className="label">{i18n.t<string>("Type")}</div>
      <div className="value ">
        <div className="base-components">
          {baseDescriptors.map((c) => {
            return (
              <ComponentDescriptorCard
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
      <div className="label">{i18n.t<string>("Name")}</div>
      <div className="value">
        <Input
          value={descriptor?.name}
          onChange={(v) => {
            setDescriptor({
              ...descriptor,
              name: v,
            });
          }}
        />
      </div>
      <div className="label">{i18n.t<string>("Description")}</div>
      <div className="value">
        <Input.TextArea
          placeholder={
            baseDescriptor?.description
              ? i18n.t<string>(baseDescriptor?.description)
              : undefined
          }
          value={descriptor?.description}
          onChange={(v) => {
            setDescriptor({
              ...(descriptor || {}),
              description: v,
            });
          }}
        />
      </div>
      {optionsJsonSchema && (
        <>
          <div className="label">{i18n.t<string>("Configuration")}</div>
          <div className="value">
            <ComponentOptionsRender
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
        </>
      )}
    </Modal>
  );
}

ComponentDetail.show = (props) => {
  const { key = `component-detail-${uuidv4()}` } = props;
  const node = document.createElement("div");

  document.body.appendChild(node);

  const root = ReactDOM.createRoot(node);

  const unmount = () => {
    console.log(key);
    root.unmount();
    node.parentElement.removeChild(node);
  };

  root.render(
    // <React.StrictMode>
    <ComponentDetail {...props} afterClose={unmount} />,
    // </React.StrictMode>,
  );

  return {
    key,
    close: unmount,
  };
};

export default ComponentDetail;
