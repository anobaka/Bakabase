"use client";

import React, { useEffect, useState } from "react";

import { Popover } from "@/components/bakaui";

import "./index.scss";
import i18n from "i18next";
const ComponentCard = ({
  name,
  description,
  extra,
  selected: s,
  defaultSelected: ds,
  className,
  onSelect,
  disabled,
  tip,
  ...otherProps
}) => {
  const [selected, setSelected] = useState(ds);

  useEffect(() => {
    if (s !== undefined) {
      setSelected(s);
    }
  }, [s]);

  const coreComponent = (
    <div
      className={`${className} component-card ${selected ? "selected" : ""} ${disabled ? "disabled" : ""}`}
      {...otherProps}
      onClick={() => {
        if (disabled) {
          return;
        }
        if (s == undefined) {
          setSelected(!selected);
        }
        if (onSelect) {
          onSelect(!selected);
        }
      }}
    >
      <div className="select-cover">
        {/* <Icon type="select" size={'large'} /> */}
      </div>
      <div className="name">{name}</div>
      {description && (
        <pre className="description">{i18n.t<string>(description)}</pre>
      )}
      {extra}
    </div>
  );

  return tip ? (
    <Popover content={i18n.t<string>(tip)}>{coreComponent}</Popover>
  ) : (
    coreComponent
  );
};

ComponentCard.displayName = "ComponentCard";

export default ComponentCard;
