"use client";

import React, { useEffect, useState } from "react";
import "./index.scss";
import i18n from "i18next";
import axios from "axios";

import { NumberInput } from "@/components/bakaui";
import ColorPicker from "@/components/ColorPicker";
import envConfig from "@/config/env";
import { getFileNameWithoutExtension } from "@/components/utils";

const ParagraphPadding = 10;
const TitleFontSize = 30;
const SectionTitleFontSize = 20;

interface ITextReaderProps {
  file?: string;
  style?: any;
  onLoad?: any;
  className?: string;
}
const TextReader = (props: ITextReaderProps) => {
  const { file, style = {}, onLoad, className } = props;

  const [previewData, setPreviewData] = useState<any>();
  // const [columns, setColumns] = useState(1);
  const [fontSize, setFontSize] = useState(14);
  const [background, setBackground] = useState("white");
  const [color, setColor] = useState("black");
  const [colorPickerVisible, setColorPickerVisible] = useState(false);
  // const [page, setPage] = useState(0);

  useEffect(() => {
    if (file) {
      axios
        .request({
          url: `${envConfig.apiEndpoint}/file/play?fullname=${encodeURIComponent(file)}`,
        })
        .then((a) => {
          if (a.status == 200) {
            const text = a.data;
            const pd = {
              title: getFileNameWithoutExtension(file),
              paragraphs: undefined,
            };
            const paragraphs = text
              ?.split("\n")
              .map((x) => x.trim())
              .filter((b) => b?.length > 0);

            if (paragraphs?.length > 0) {
              pd.paragraphs = paragraphs;
            }
            setPreviewData(pd);
          }
        });
    } else {
      onLoad();
    }
  }, []);

  return (
    <div
      className={`text-reader ${className || ""}`}
      style={{ ...style, background, color }}
      onLoad={onLoad}
    >
      <div className="configurations">
        <div className="label">{i18n.t<string>("Font size")}</div>
        <div className="value">
          <NumberInput value={fontSize} onChange={(v) => setFontSize(v)} />
        </div>
        <div className="label">{i18n.t<string>("Font color")}</div>
        <div className="value">
          <ColorPicker color={color} onChange={(v) => setColor(v)} />
        </div>
        <div className="label">{i18n.t<string>("Background color")}</div>
        <div className="value">
          <ColorPicker color={background} onChange={(v) => setBackground(v)} />
        </div>
      </div>
      <div className={"content"}>
        {previewData?.title && <h1>{previewData.title}</h1>}
        {previewData?.sections?.map((s, i) => {
          return (
            <section className={`s-${i}`}>
              {s.title && <h2>{s.title}</h2>}

              {s.paragraphs?.map((p, j) => {
                return (
                  <p className={`p-${j}`} style={{ fontSize }}>
                    {p}
                  </p>
                );
              })}
            </section>
          );
        }) || (
          <div className={"no-content"}>{i18n.t<string>("No content")}</div>
        )}
      </div>
    </div>
  );
};

TextReader.displayName = "TextReader";

export default TextReader;
