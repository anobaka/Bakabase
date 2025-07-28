"use client";

import type { IChoice } from "../../../Property/models";

import React, { useEffect, useRef, useState } from "react";
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { useTranslation } from "react-i18next";
import { AutoSizer, List } from "react-virtualized";
import { MdSort, MdAddCircle } from "react-icons/md";

import { SortableChoice } from "./components/SortableChoice";

import { uuidv4 } from "@/components/utils";
import { Button, Chip, Popover, Textarea } from "@/components/bakaui";

interface IProps {
  choices?: IChoice[];
  onChange?: (choices: IChoice[]) => void;
  className?: string;
  checkUsage?: (value: string) => Promise<number>;
}

const lineHeight = 35;

export default function ChoiceList({
  choices: propsChoices,
  onChange,
  className,
  checkUsage,
}: IProps) {
  const { t } = useTranslation();
  const [choices, setChoices] = useState<IChoice[]>(propsChoices || []);
  const [editInBulkPopupVisible, setEditInBulkPopupVisible] = useState(false);
  const [editInBulkText, setEditInBulkText] = useState("");
  const [bulkEditSummaries, setBulkEditSummaries] = useState<string[]>([]);
  const calculateBulkEditSummaryTimeoutRef = useRef<any>();

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    }),
  );

  const virtualListRef = useRef<any>();

  useEffect(() => {
    onChange?.(choices);
  }, [choices]);

  const buildChoicesFromBulkText = (text: string): IChoice[] => {
    return text
      .split("\n")
      .map<IChoice | null>((c) => {
        const str = c.trim();

        if (str.length == 0) {
          return null;
        }

        const t = choices.find((x) => x.label == str);

        if (t) {
          return t;
        }

        return {
          label: str,
          value: uuidv4(),
        };
      })
      .filter((x) => x != null) as IChoice[];
  };

  const calculateBulkEditSummary = (text: string) => {
    const ctxChoices = buildChoicesFromBulkText(text);
    const addedChoicesCount = ctxChoices.filter(
      (x) => !choices.includes(x),
    ).length;
    const sameChoicesCount = ctxChoices.filter((x) =>
      choices.includes(x),
    ).length;

    const deletedChoicesCount = choices.length - sameChoicesCount;

    if (deletedChoicesCount > 0 || addedChoicesCount > 0) {
      const tips = [
        deletedChoicesCount > 0
          ? t<string>("{{count}} data will be deleted", {
              count: deletedChoicesCount,
            })
          : "",
        addedChoicesCount > 0
          ? t<string>("{{count}} data will be added", {
              count: addedChoicesCount,
            })
          : "",
      ];

      setBulkEditSummaries(tips.filter((t) => t));
    } else {
      setBulkEditSummaries([]);
    }
  };

  const addChoice = () => {
    const newChoices = [...choices, { label: "", value: uuidv4() }];

    setChoices(newChoices);
    setTimeout(() => {
      virtualListRef.current?.scrollToRow(newChoices.length - 1);
    }, 100);
    // console.log(`scroll to ${newChoices.length}`, virtualListRef.current?.scrollToRow);
  };

  return (
    <div className={className}>
      <div className="flex justify-between items-center">
        <Button
          size={"sm"}
          variant={"light"}
          onClick={() => {
            choices.sort((a, b) =>
              (a.label || "").localeCompare(b.label || ""),
            );
            setChoices([...choices]);
          }}
        >
          <MdSort className={"text-base"} />
          {t<string>("Sort by alphabet")}
        </Button>
      </div>
      <div className="mt-2 mb-2 flex flex-col gap-1">
        <DndContext
          collisionDetection={closestCenter}
          sensors={sensors}
          onDragEnd={handleDragEnd}
        >
          <SortableContext
            items={choices?.map((c) => ({ ...c, id: c.value }))}
            strategy={verticalListSortingStrategy}
          >
            <div style={{ height: Math.min(choices.length, 6) * lineHeight }}>
              <AutoSizer>
                {({ width, height }) => (
                  <List
                    ref={virtualListRef}
                    // className={styles.List}
                    height={height}
                    rowCount={choices.length}
                    rowHeight={lineHeight}
                    rowRenderer={(ctx) => {
                      const { key, index, style } = ctx;
                      const sc = choices[index]!;

                      return (
                        <SortableChoice
                          key={sc.value}
                          choice={sc}
                          style={style}
                          onChange={t => {
                            choices[index] = t;
                            setChoices([...choices]);
                          }}
                          onEnterKeyDown={() => {
                            addChoice();
                          }}
                          onRemove={t => {
                            choices.splice(index, 1);
                            setChoices([...choices]);
                          }}
                          checkUsage={checkUsage}
                          // key={sc.value}
                          id={sc.value}
                        />
                      );
                    }}
                    width={width}
                  />
                )}
              </AutoSizer>
            </div>
            {/* {choices?.map((sc, index) => ( */}
            {/*   <SortableChoice */}
            {/*     key={sc.value} */}
            {/*     id={sc.value} */}
            {/*     choice={sc} */}
            {/*     onRemove={t => { */}
            {/*       choices.splice(index, 1); */}
            {/*       setChoices([...choices]); */}
            {/*     }} */}
            {/*     onChange={t => { */}
            {/*       choices[index] = t; */}
            {/*       setChoices([...choices]); */}
            {/*     }} */}
            {/*   /> */}
            {/* ))} */}
          </SortableContext>
        </DndContext>
      </div>
      <div className="flex items-center justify-between">
        <Button
          size={"sm"}
          onClick={() => {
            addChoice();
          }}
        >
          <MdAddCircle className={"text-base"} />
          {t<string>("Add a choice")}
        </Button>
        <Popover
          placement={"right"}
          size={"lg"}
          style={{ zIndex: 100 }}
          trigger={
            <Button size={"sm"} variant={"light"}>
              {t<string>("Add or delete in bulk")}
            </Button>
          }
          visible={editInBulkPopupVisible}
          onVisibleChange={(v) => {
            if (v) {
              const text = choices.map((t) => t.label).join("\n");

              setEditInBulkText(text);
            }
            setEditInBulkPopupVisible(v);
          }}
        >
          <div className={"flex flex-col gap-2 m-2 "}>
            <div className="text-base">
              {t<string>("Add or delete choices in bulk")}
            </div>
            <div className={"text-sm opacity-70"}>
              <div>
                {t<string>("Choices will be separated by line breaks.")}
              </div>
              <div>
                {t<string>(
                  "Once you click the submit button, new choices will be added to the list, and missing choices will be deleted.",
                )}
              </div>
              <div>
                {t<string>(
                  "Be cautions: once you modify the text in one line, it will be treated as a new choice, and the original choice will be deleted.",
                )}
              </div>
            </div>
            {bulkEditSummaries.length > 0 && (
              <div className={"flex items-center gap-2 text-sm"}>
                {bulkEditSummaries.map((s) => (
                  <Chip color={"success"} size={"sm"} variant={"light"}>
                    {s}
                  </Chip>
                ))}
              </div>
            )}
            <Textarea
              onValueChange={v => {
                setEditInBulkText(v);
                calculateBulkEditSummaryTimeoutRef.current && clearTimeout(calculateBulkEditSummaryTimeoutRef.current);
                calculateBulkEditSummaryTimeoutRef.current = setTimeout(() => {
                  calculateBulkEditSummary(v);
                  calculateBulkEditSummaryTimeoutRef.current = undefined;
                }, 1000);
              }}
              value={editInBulkText}
              // onValueChange={v => setEditInBulkText(v)}
              maxRows={16}
            />
            <div className="flex justify-end items-center">
              <Button
                size={"sm"}
                variant={"light"}
                onClick={() => {
                  setEditInBulkPopupVisible(false);
                }}
              >
                {t<string>("Cancel")}
              </Button>
              <Button
                color={"primary"}
                isLoading={!!calculateBulkEditSummaryTimeoutRef.current}
                size={"sm"}
                onClick={() => {
                  setChoices(buildChoicesFromBulkText(editInBulkText));
                  setEditInBulkPopupVisible(false);
                }}
              >
                {t<string>("Submit")}
              </Button>
            </div>
          </div>
        </Popover>
      </div>
    </div>
  );

  function handleDragEnd(event) {
    const { active, over } = event;

    if (active.value !== over.value) {
      setChoices((items) => {
        const oldIndex = items.indexOf(active.value);
        const newIndex = items.indexOf(over.value);

        return arrayMove(items, oldIndex, newIndex);
      });
    }
  }
}
