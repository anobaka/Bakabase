"use client";

import type { ErrorListProps, FieldErrorProps, UiSchema } from "@rjsf/utils";
import type { TitleFieldProps } from "@rjsf/utils";
import type { FieldProps } from "@rjsf/utils";
import type { RegistryFieldsType } from "@rjsf/utils";
import type { FormValidation } from "@rjsf/utils";

import Form from "@rjsf/core";
import React, { useEffect, useReducer, useRef } from "react";
import { customizeValidator } from "@rjsf/validator-ajv8";
import AjvDraft04 from "ajv-draft-04";
import localizer from "ajv-i18n";
import i18n from "i18next";
import "./index.scss";

import { uuidv4 } from "@/components/utils";
import { MdHelp } from "react-icons/md";
import { Tooltip } from "@/components/bakaui";

function TitleFieldTemplate(props: TitleFieldProps) {
  return;
}
function ErrorListTemplate(props: ErrorListProps) {
  return;
}

function transformErrors(errors, uiSchema) {
  return errors.map((error) => {
    console.log(error);

    return error;
  });
}

function FieldErrorTemplate(props: FieldErrorProps) {
  const { errors = [] } = props;

  if (errors.length > 0) {
    return (
      <div className={"item error"}>
        <div className="label" />
        <div className="value">{errors}</div>
      </div>
    );
  }

  return;
}

// function CustomFieldTemplate(props: FieldTemplateProps) {
//   const { id, classNames, style, label, help, required, description, errors, children } = props;
//   return (
//     <div className={classNames} style={style}>
//       <label htmlFor={id}>
//         {label}
//         {required ? '*' : null}
//       </label>
//       {description}
//       {children}
//       {errors}
//       {help}
//     </div>
//   );
// }

const validator = customizeValidator(
  { AjvClass: AjvDraft04 },
  localizer[i18n.language == "cn" ? "zh" : "en"],
);

export interface BRjsfProperty {
  Component: any;
  componentProps?: {};
  tip?: any;
}
export interface BRjsfProps {
  value?: any;
  defaultValue?: any;
  schema: any;
  properties?: { [index: string]: BRjsfProperty };
  customValidate?: (
    formData,
    errors: FormValidation,
    uiSchema: UiSchema,
  ) => any;
  className?: string;
  onChange: (data: any) => any;
}

const buildField = (
  fieldProps: FieldProps,
  bRjsfProps: BRjsfProperty,
  formRef: any,
) => {
  console.log("[RenderingField]", fieldProps, bRjsfProps, fieldProps.formData);

  // console.log('Building field', fieldProps.name);
  return (
    <div className={"item"}>
      <div className="label">
        {i18n.t<string>(fieldProps.name)}
        {bRjsfProps.tip && (
          <Tooltip content={bRjsfProps.tip} style={{ maxWidth: "unset" }}>
            <MdHelp />
          </Tooltip>
        )}
      </div>
      <div className="value">
        <bRjsfProps.Component
          // value={fieldProps.formData}
          defaultValue={fieldProps.formData}
          onChange={(v) => {
            // debugger;
            console.log("OnChange", fieldProps.name, v);
            fieldProps.onChange(v);
            // console.log(fieldProps.formData);
          }}
          {...(bRjsfProps.componentProps || {})}
        />
      </div>
    </div>
  );
};

const BRjsf = (props: BRjsfProps, ref) => {
  const {
    value: propsValue,
    defaultValue: propsDefaultValue,
    schema,
    properties = {},
    customValidate,
    className,
    onChange,
  } = props;

  const [, forceUpdate] = useReducer((x) => x + 1, 0);
  const fieldsRef = useRef<RegistryFieldsType>();
  const uiSchemaRef = useRef<UiSchema>();

  useEffect(() => {
    console.log("[BRjsf]Initialized", props);
  }, []);

  useEffect(() => {
    fieldsRef.current = {};
    uiSchemaRef.current = {
      "ui:submitButtonOptions": {
        norender: true,
      },
    };
    Object.keys(properties || {}).forEach((k) => {
      const componentKey = uuidv4();

      fieldsRef.current[componentKey] = (fieldProps: FieldProps) =>
        buildField(fieldProps, properties[k]);
      uiSchemaRef.current[k] = {
        "ui:field": componentKey,
      };
    }, {});
    forceUpdate();

    console.log("Properties changed");
  }, [properties]);

  return (
    <Form
      ref={ref}
      liveValidate
      className={`b-rjsf ${className || ''}`}
      fields={fieldsRef.current}
      formData={propsValue ?? propsDefaultValue}
      schema={schema}
      transformErrors={transformErrors}
      uiSchema={uiSchemaRef.current}
      validator={validator}
      onChange={v => {
        console.log(props, v);
        if (onChange) {
          onChange(v.formData);
        }
      }}
      customValidate={customValidate}
      // widgets={widgets}
      templates={{ TitleFieldTemplate, ErrorListTemplate, FieldErrorTemplate }}
    />
  );
};

export default React.forwardRef(BRjsf);
