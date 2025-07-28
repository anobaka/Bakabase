"use client";

import type { Tag } from "../../../Property/models";

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

import { SortableTag } from "./components/SortableTag";

import { uuidv4 } from "@/components/utils";
import { Button, Chip, Popover, Textarea } from "@/components/bakaui";

interface IProps {
  tags?: Tag[];
  onChange?: (tags: Tag[]) => void;
  className?: string;
  checkUsage?: (value: string) => Promise<number>;
}

const LineHeight = 35;
const GroupAndNameSeparator = ":";

export default function TagList({
  tags: propsTags,
  onChange,
  className,
  checkUsage,
}: IProps) {
  const { t } = useTranslation();
  const [tags, setTags] = useState<Tag[]>(propsTags || []);
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
    onChange?.(tags);
  }, [tags]);

  const buildTagsFromBulkText = (text: string): Tag[] => {
    return text
      .split("\n")
      .map<Tag | null>((c) => {
        const str = c.trim();

        if (str.length == 0) {
          return null;
        }
        const segments = str.split(GroupAndNameSeparator);
        let name = "";
        let group: string | undefined;

        if (segments.length == 1) {
          name = segments[0]!;
        } else {
          group = segments[0];
          name = segments[1]!;
        }

        const t = tags.find((x) => x.name == name && x.group == group);

        if (t) {
          return t;
        }

        return {
          group,
          name,
          value: uuidv4(),
        };
      })
      .filter((x) => x != null) as Tag[];
  };

  const calculateBulkEditSummary = (text: string) => {
    const ctxTags = buildTagsFromBulkText(text);
    const addedTagsCount = ctxTags.filter((x) => !tags.includes(x)).length;
    const sameTagsCount = ctxTags.filter((x) => tags.includes(x)).length;

    const deletedTagsCount = tags.length - sameTagsCount;

    if (deletedTagsCount > 0 || addedTagsCount > 0) {
      const tips = [
        deletedTagsCount > 0
          ? t<string>("{{count}} data will be deleted", {
              count: deletedTagsCount,
            })
          : "",
        addedTagsCount > 0
          ? t<string>("{{count}} data will be added", { count: addedTagsCount })
          : "",
      ];

      setBulkEditSummaries(tips.filter((t) => t));
    } else {
      setBulkEditSummaries([]);
    }
  };

  return (
    <div className={className}>
      <div className="flex justify-between items-center">
        <Button
          size={"sm"}
          variant={"light"}
          onPress={() => {
            tags.sort((a, b) => {
              const sa = a.group ? `${a.group}:${b.name}` : b.name;
              const sb = b.group ? `${b.group}:${b.name}` : b.name;

              return (sa ?? "").localeCompare(sb ?? "");
            });
            setTags([...tags]);
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
            items={tags?.map((c) => ({
              ...c,
              id: c.value,
            }))}
            strategy={verticalListSortingStrategy}
          >
            <div style={{ height: Math.min(tags.length, 6) * LineHeight }}>
              <AutoSizer>
                {({ width, height }) => (
                  <List
                    ref={virtualListRef}
                    // className={styles.List}
                    height={height}
                    // height={Math.min(tags.length, 6) * 30}
                    rowCount={tags.length}
                    rowHeight={LineHeight}
                    rowRenderer={(ctx) => {
                      const { key, index, style } = ctx;
                      const sc = tags[index]!;

                      return (
                        <SortableTag
                          key={sc.value}
                          checkUsage={checkUsage}
                          id={sc.value}
                          style={style}
                          tag={sc}
                          onChange={(t) => {
                            tags[index] = t;
                            setTags([...tags]);
                          }}
                          onRemove={(t) => {
                            tags.splice(index, 1);
                            setTags([...tags]);
                          }}
                        />
                      );
                    }}
                    width={width}
                  />
                )}
              </AutoSizer>
            </div>
            {/* {tags?.map((sc, index) => ( */}
            {/*   <SortableTag */}
            {/*     key={sc.value} */}
            {/*     id={sc.value} */}
            {/*     tag={sc} */}
            {/*     onRemove={t => { */}
            {/*       tags.splice(index, 1); */}
            {/*       setTags([...tags]); */}
            {/*     }} */}
            {/*     onChange={t => { */}
            {/*       tags[index] = t; */}
            {/*       setTags([...tags]); */}
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
            const newTags = [...tags, { value: uuidv4() }];

            setTags(newTags);
            setTimeout(() => {
              virtualListRef.current?.scrollToRow(newTags.length - 1);
            }, 100);
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
              const text = tags
                .map((t) => {
                  let s = "";

                  if (t.group != undefined && t.group.length > 0) {
                    s += t.group + GroupAndNameSeparator;
                  }
                  s += t.name;

                  return s;
                })
                .join("\n");

              setEditInBulkText(text);
            }
            setEditInBulkPopupVisible(v);
          }}
        >
          <div className={"flex flex-col gap-2 m-2 "}>
            <div className="text-base">
              {t<string>("Add or delete tags in bulk")}
            </div>
            <div className={"text-sm opacity-70"}>
              <div>
                {t<string>(
                  "Colon can be added between group and name, and tags will be separated by line breaks.",
                )}
              </div>
              <div>
                {t<string>(
                  "Once you click the submit button, new tags will be added to the list, and missing tags will be deleted.",
                )}
              </div>
              <div>
                {t<string>(
                  "Be cautions: once you modify the text in one line, it will be treated as a new tag, and the original tag will be deleted.",
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
                  setTags(buildTagsFromBulkText(editInBulkText));
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
      setTags((items) => {
        const oldIndex = items.indexOf(active.value);
        const newIndex = items.indexOf(over.value);

        return arrayMove(items, oldIndex, newIndex);
      });
    }
  }
}
