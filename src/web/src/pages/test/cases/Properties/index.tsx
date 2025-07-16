"use client";

import ReactJson from "react-json-view";
import React, { useState } from "react";

import TestProperties from "./data.json";

import { propertyTypes } from "@/sdk/constants";
import ModalContent from "@/components/PropertyModal/components/ModalContent";

export default () => {
  const [properties, setProperties] = useState<any[]>(TestProperties);

  console.log(properties);

  return (
    <div className={"flex flex-col gap-2"}>
      {propertyTypes.map((pt) => {
        return (
          <div
            className={"flex items-center gap-1 p-1 border-b-1"}
            style={{ borderBottomColor: "rgba(255,255,255,0.2)" }}
          >
            <div className={"!w-[120px] !min-w-[120px]"}>{pt.label}</div>
            <div className={"!w-[480px] !min-w-[480px]"}>
              <ModalContent
                validValueTypes={[pt.value]}
                value={TestProperties[pt.value] ?? { type: pt.value }}
                onChange={(p) => {
                  // console.log(p);
                  properties[pt.value] = p;
                  setProperties([...properties]);
                }}
              />
            </div>
            <div>
              <ReactJson
                // displayDataTypes={false}
                // displayObjectSize={false}
                name={"json"}
                quotesOnKeys={false}
                src={properties[pt.value] ? properties[pt.value] : undefined}
                theme={"monokai"}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
};
