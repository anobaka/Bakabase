"use client";

import React from "react";

import { Button } from "@/components/bakaui";

import "./index.scss";
import { Link } from "react-router-dom";
import { useTranslation } from "react-i18next";
const Title = ({
  title,
  titleAfter = undefined,
  buttons = [],
  right = undefined,
}) => {
  const { t } = useTranslation();

  return (
    <div className={"iw-title"}>
      <div className="left">
        <div className="title">{t<string>(title)}</div>
        {titleAfter && <div className={"title-after"}>{titleAfter}</div>}
        {buttons && (
          <div className="buttons">
            {buttons.map((b) => (
              <div key={b.key} className={"button"}>
                <img alt="" className={"icon"} src={b.icon} />
                {b.type == "link" ? (
                  <Link to={b.to}>{t<string>(b.text)}</Link>
                ) : (
                  <Button text type={"primary"} onClick={b.onClick}>
                    {t<string>(b.text)}
                  </Button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
      <div className="right">{right}</div>
    </div>
  );
};

Title.displayName = "Title";

export default Title;
