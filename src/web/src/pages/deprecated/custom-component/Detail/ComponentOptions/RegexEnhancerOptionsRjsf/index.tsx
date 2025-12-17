"use client";

import type { FormValidation } from "@rjsf/utils";
import type { BRjsfProps } from "@/components/BRjsf";

import React, {
  useEffect,
  useImperativeHandle,
  useReducer,
  useRef,
  useState,
} from "react";
import i18n from "i18next";
import { useUpdateEffect } from "react-use";

import { Button, Input, Select, Table } from "@/components/bakaui";
import BRjsf from "@/components/BRjsf";
import { reservedProperties, ReservedProperty } from "@/sdk/constants";
import { findCapturingGroupsInRegex } from "@/components/utils";

import "./index.scss";

const typeDataSources = [
  {
    label: "Reserved property",
    value: 1,
  },
  {
    label: "Custom property",
    value: -1,
  },
].map((a) => ({
  ...a,
  label: i18n.t<string>(a.label).toString(),
}));

const translatedReservedResourceProperties = reservedProperties.map((a) => ({
  ...a,
  label: i18n.t<string>(a.label).toString(),
}));
const RegexEnhancerOptionsRjsfPage = React.forwardRef(
  function RegexEnhancerOptionsRjsf(bRjsfProps: BRjsfProps, ref) {
    const defaultBRjsfValue = bRjsfProps.defaultValue || {};

    const regexRef = useRef(defaultBRjsfValue.regex);
    const groupsComponentRef = useRef();
    const regexCapturingGroupsRef = useRef<string[]>(
      findCapturingGroupsInRegex(defaultBRjsfValue.regex),
    );

    console.log(bRjsfProps, translatedReservedResourceProperties);

    useEffect(() => {
      console.log("[RegexEnhancerOptionsRjsf]Initialized");
    }, []);

    const propertiesRef = useRef({
      regex: {
        Component: (props) => {
          return (
            <Input
              {...props}
              onChange={(v) => {
                console.log(props);
                regexRef.current = v;
                const groups = findCapturingGroupsInRegex(v).sort((a, b) =>
                  a.localeCompare(b),
                );

                if (
                  groups.length != regexCapturingGroupsRef.current.length ||
                  groups.some(
                    (ng, i) => ng != regexCapturingGroupsRef.current[i],
                  )
                ) {
                  regexCapturingGroupsRef.current = groups;
                  groupsComponentRef.current?.forceUpdate();
                }
                if (props.onChange) {
                  props.onChange(v);
                }
              }}
            />
          );
        },
        componentProps: {
          size: "small",
        },
      },
      groups: {
        Component: React.forwardRef((props, ref) => {
          const [, forceUpdate] = useReducer((x) => x + 1, 0);
          const [value, setValue] = useState<any[]>(
            (props.defaultValue &&
              Object.prototype.toString.call(props.defaultValue) ===
                "[object Array]" &&
              props.defaultValue) ||
              [],
          );

          const isValid = (v) => {
            if (
              v &&
              v.every(
                (t) =>
                  t.name &&
                  t.isReserved != undefined &&
                  t.key != undefined &&
                  t.key.length > 0,
              )
            ) {
              return true;
            }

            return false;
          };

          useUpdateEffect(() => {
            console.log(value, isValid(value), props);
            if (isValid(value)) {
              if (props.onChange) {
                props.onChange(value);
              }
            }
          }, [value]);

          useImperativeHandle(ref, () => {
            return {
              forceUpdate: () => {
                console.log("force updated");
                forceUpdate();
              },
            };
          }, []);

          // useEffect(() => {
          //   setValue(convertPropsValue(props.value));
          // }, [props.value]);

          const definedGroupsInRegex =
            (regexRef.current &&
              regexRef.current
                .match(/\(\?<(\w+)>/g)
                ?.map((match) => match.slice(3, -1))) ||
            [];

          console.log("[RegexEnhancerOptionsRjsfGroups]rendering", value);

          return (
            <div className={"regex-groups"} style={{ width: "100%" }}>
              <Table dataSource={value} size={"small"}>
                <Table.Column
                  cell={(c, i, r) => {
                    return (
                      <div>
                        <Select
                          dataSource={definedGroupsInRegex.map((a) => ({
                            label: a,
                            value: a,
                          }))}
                          size={"small"}
                          value={c}
                          onChange={(v) => {
                            r.name = v;
                            setValue([...value]);
                          }}
                        />
                        {regexCapturingGroupsRef.current?.indexOf(c) == -1 && (
                          <div className={"error"}>
                            {i18n.t<string>("Invalid capturing group")}
                          </div>
                        )}
                      </div>
                    );
                  }}
                  dataIndex={"name"}
                  title={i18n.t<string>("Group name")}
                />
                <Table.Column
                  cell={(c, i, r) => {
                    console.log("asdsdasasdasasda", c);

                    return (
                      <div>
                        <Select
                          dataSource={typeDataSources}
                          size={"small"}
                          value={c == undefined ? undefined : c ? 1 : -1}
                          onChange={(v) => {
                            r.isReserved = v > 0;
                            setValue([...value]);
                          }}
                        />
                        {c == undefined && (
                          <div className={"error"}>
                            {i18n.t<string>("Type is not set")}
                          </div>
                        )}
                      </div>
                    );
                  }}
                  dataIndex={"isReserved"}
                  title={i18n.t<string>("Type")}
                />
                <Table.Column
                  cell={(c, i, r) => {
                    const error = c == undefined || c.length == 0;

                    return (
                      <div>
                        {r.isReserved ? (
                          <Select
                            dataSource={translatedReservedResourceProperties}
                            size={"small"}
                            value={c ? ReservedProperty[c] : undefined}
                            onChange={(v) => {
                              r.key = ReservedProperty[v];
                              setValue([...value]);
                            }}
                          />
                        ) : (
                          <Input
                            trim
                            size={"small"}
                            style={{ width: "auto" }}
                            value={c}
                            onChange={(v) => {
                              r.key = v;
                              setValue([...value]);
                            }}
                          />
                        )}
                        {error && (
                          <div className={"error"}>
                            {i18n.t<string>("Property is not set")}
                          </div>
                        )}
                      </div>
                    );
                  }}
                  dataIndex={"key"}
                  title={i18n.t<string>("Property")}
                />
                <Table.Column
                  cell={(_, i, r) => {
                    return (
                      <MdDelete
                        className={"text-base cursor-pointer"}
                        onClick={() => {
                          value.splice(i, 1);
                          setValue([...value]);
                        }}
                      />
                    );
                  }}
                  title={i18n.t<string>("Operations")}
                />
              </Table>
              <Button
                size={"small"}
                style={{ marginTop: 5 }}
                type={"normal"}
                onClick={() => {
                  const newValue = value || [];

                  newValue.push({});
                  setValue([...newValue]);
                }}
              >
                {i18n.t<string>("Add")}
              </Button>
            </div>
          );
        }),
        componentProps: {
          size: "small",
          ref: groupsComponentRef,
        },
      },
    });

    return (
      <BRjsf
        className={"regex-enhancer-options-rjsf"}
        {...bRjsfProps}
        ref={ref}
        customValidate={(formData, errors: FormValidation, uiSchema) => {
          console.log(formData);
          if (formData.groups) {
            if (!(formData.groups?.length > 0)) {
              errors.groups.addError(i18n.t<string>("Invalid configuration"));
            } else {
              const invalidGroups = formData.groups.filter(
                (a) => regexCapturingGroupsRef.current?.indexOf(a.name) == -1,
              );

              if (invalidGroups.length > 0) {
                errors.groups.addError(
                  i18n.t<string>(
                    "Multiple invalid capturing group names are found: {{names}}",
                    { names: invalidGroups.map((a) => a.name).join(",") },
                  ),
                );
              } else {
                if (
                  invalidGroups.some((d) => {
                    if (
                      d.isReserved == undefined ||
                      d.key == undefined ||
                      d.key.length == 0 ||
                      d.name == undefined ||
                      d.name.length == 0
                    ) {
                      errors.groups.addError(i18n.t<string>("Invalid group"));
                    }
                  })
                ) {
                }
              }
            }
          }

          if (formData.regex) {
            if (regexCapturingGroupsRef.current.length == 0) {
              errors.regex.addError(
                i18n.t<string>("No capturing groups are found"),
              );
            }
          }

          return errors;
        }}
        defaultValue={defaultBRjsfValue}
        properties={propertiesRef.current}
      />
    );
  },
);

RegexEnhancerOptionsRjsfPage.displayName = "RegexEnhancerOptionsRjsfPage";

export default RegexEnhancerOptionsRjsfPage;
