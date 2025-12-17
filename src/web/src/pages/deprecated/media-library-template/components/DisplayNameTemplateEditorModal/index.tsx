"use client";

import type { Wrapper } from "@/core/models/Text/Wrapper.ts";
import type { DestroyableProps } from "@/components/bakaui/types.ts";
import type { IProperty } from "@/components/Property/models.ts";

import React, { useCallback, useEffect, useRef, useState } from "react";
import { Trans, useTranslation } from "react-i18next";
import { renderToString } from "react-dom/server";
import ContentEditable from "react-contenteditable";
import { useUpdate } from "react-use";
import { InfoCircleOutlined } from "@ant-design/icons";

import { extractResourceDisplayNameTemplate } from "./helpers.ts";

import { Button, Chip, Code, Modal } from "@/components/bakaui";
import BApi from "@/sdk/BApi.tsx";
import { builtinPropertyForDisplayNames, SpecialTextType } from "@/sdk/constants.ts";
import {
  type ResourceDisplayNameTemplateSegment,
  ResourceDisplayNameTemplateSegmentType,
} from "@/core/models/Category/ResourceDisplayNameTemplate.ts";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider.tsx";

type Props = DestroyableProps & {
  template?: string;
  properties: Omit<IProperty, "bizValueType" | "dbValueType">[];
  onSubmit?: (template: string) => any;
};

const renderDisplayNameSegment = (p: ResourceDisplayNameTemplateSegment) => {
  switch (p.type) {
    case ResourceDisplayNameTemplateSegmentType.Text:
      return <span>{p.text}</span>;
    case ResourceDisplayNameTemplateSegmentType.Wrapper:
      return (
        <span
          // className={'font-bold'}
          style={{ color: "var(--bakaui-secondary)" }}
        >
          {p.text}
        </span>
      );
    case ResourceDisplayNameTemplateSegmentType.Property:
      return <span style={{ color: "var(--bakaui-primary)" }}>{p.text}</span>;
  }
};
const DisplayNameTemplateEditorModal = ({ template, properties, onSubmit, ...props }: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();
  const [visible, setVisible] = useState(true);

  const [wrappers, setWrappers] = useState<Wrapper[]>([]);

  const [variables, setVariables] = useState<string[]>([]);

  // const propertiesRef = useRef<IProperty[]>([]);
  const [templateHtml, setTemplateHtml] = useState<string>("");
  const templateRef = useRef("");

  const init = useCallback(async () => {
    const tr = await BApi.specialText.getAllSpecialTexts();
    const texts = tr.data?.[SpecialTextType.Wrapper] || [];
    const wrappers = texts.map((text) => {
      return {
        left: text.value1!,
        right: text.value2!,
      };
    });

    setWrappers(wrappers);

    const builtinPropertyNames = builtinPropertyForDisplayNames.map((v) =>
      t<string>(`BuiltinPropertyForDisplayName.${v.label}`),
    );
    const customPropertyNames = properties?.map((cp) => cp.name!) ?? [];

    setVariables(builtinPropertyNames.concat(customPropertyNames));

    templateRef.current = template ?? "";
    setTemplateHtml(template ? buildTemplateHtml(template) : "");
  }, []);

  useEffect(() => {
    init();
  }, []);

  const close = () => {
    setVisible(false);
  };

  const buildTemplateHtml = (template: string) => {
    const parts = extractResourceDisplayNameTemplate(template, variables, wrappers);
    const components = parts.map((p) => renderDisplayNameSegment(p));
    const html = renderToString(<>{components}</>);

    console.log(parts, html);

    return html;
  };

  return (
    <Modal
      defaultVisible
      size={"xl"}
      title={t<string>("Edit display name template for resources")}
      onClose={close}
      onDestroyed={props.onDestroyed}
      onOk={() => onSubmit?.(templateRef.current)}
    >
      <div>
        <div>
          <div className={"flex flex-wrap"}>
            <InfoCircleOutlined className={"text-base"} />
            &nbsp;
            {t<string>(
              "You can use any combination of text and following properties in template, and you can add more properties in category configuration.",
            )}
          </div>
          <div className={"flex flex-wrap items-center"}>
            <InfoCircleOutlined className={"text-base"} />
            &nbsp;
            <Trans
              i18nKey={"category.displayNameTemplate.propertyExample"}
              values={{
                // samplePropertyName: propertiesRef.current[0]?.name ?? t<string>('Name'),
                samplePropertyName: variables?.[0] ?? t<string>("Name"),
              }}
            >
              To add a property value as a variable in the template, you can use the following
              format: <Code>{"{name of property}"}</Code>. For example,{" "}
              <Code>{"{samplePropertyName}"}</Code> will be replaced with the value of the property
              named <Code>sampleName</Code>
            </Trans>
          </div>
          <div className={"flex flex-wrap"}>
            <InfoCircleOutlined className={"text-base"} />
            &nbsp;
            {t<string>(
              "Be careful if you have multiple properties with same name, only a random one will be replaced.",
            )}
          </div>
        </div>
        <div className={"flex flex-wrap gap-1 mt-2"}>
          {variables?.map((p) => (
            <Button
              key={p}
              size={"sm"}
              onClick={() => {
                templateRef.current += `{${p}}`;
                setTemplateHtml(buildTemplateHtml(templateRef.current));
              }}
            >
              {p}
            </Button>
          ))}
        </div>
      </div>
      <div>
        <div className={"flex flex-wrap"}>
          <InfoCircleOutlined className={"text-base"} />
          &nbsp;
          {t<string>(
            "You can safely use any of following text wrappers to wrap the properties, and wrappers surrounding the property with empty value will be removed automatically.",
          )}
          {t<string>("You can check and set the wrappers in special text configuration.")}
        </div>
        <div className={"flex flex-wrap gap-1 mt-2"}>
          {wrappers.map((w) => (
            <Chip key={w.left} size={"sm"}>
              {w.left}
              &emsp;
              {w.right}
            </Chip>
          ))}
        </div>
      </div>
      <div className={"flex flex-wrap"}>
        <InfoCircleOutlined className={"text-base"} />
        &nbsp;
        {t<string>(
          "If you leave the template with empty value, the file name will be the display name.",
        )}
      </div>
      <div>
        <div>{t<string>("Display name template")}</div>
        <ContentEditable
          key={"0"}
          autoFocus
          className={"border-1 rounded mt-1 p-2"}
          html={templateHtml}
          tagName={"pre"}
          onChange={(v) => {
            // console.log('changes', v, v.target.value, v.currentTarget.textContent);
            templateRef.current = v.currentTarget.textContent || "";
            setTemplateHtml(buildTemplateHtml(templateRef.current));
          }}
        />
      </div>
    </Modal>
  );
};

DisplayNameTemplateEditorModal.displayName = "DisplayNameTemplateEditorModal";

export default DisplayNameTemplateEditorModal;
