"use client";

import React, { useEffect, useRef, useState } from "react";

import { Button, Icon, Input } from "@/components/bakaui";

import "./index.scss";
import i18n from "i18next";
const EditableTree = ({ defaultValue = [], onChange = (tree) => {} }) => {
  const [value, setValue] = useState(JSON.parse(JSON.stringify(defaultValue)));
  const [editingKey, setEditingKey] = useState();
  const [editingValue, setEditingValue] = useState();
  const initializedRef = useRef(false);

  useEffect(() => {
    initializedRef.current = true;
  }, []);

  useEffect(() => {
    if (initializedRef.current) {
      onChange(value);
    }
  }, [value]);

  const exitEditingMode = () => {
    setEditingValue(undefined);
    setEditingKey(undefined);
  };

  const renderNodes = (
    dataSource = [],
    layerPrefix = "",
    parent = undefined,
  ) => {
    const nodes = [];

    for (let i = 0; i <= dataSource.length; i++) {
      const key = layerPrefix ? `${layerPrefix}-${i}` : i.toString();
      const isAddBtn = i == dataSource.length;
      const d = dataSource[i];
      const idx = i;
      const editing = editingKey == key;

      // console.log(d, key);
      nodes.push(
        <Tree.Node
          key={key}
          label={
            <div className={"tree-node"}>
              <div className="label">
                {editing ? (
                  <Input
                    size={"small"}
                    value={editingValue}
                    onChange={(v) => setEditingValue(v)}
                  />
                ) : isAddBtn ? (
                  <Button
                    text
                    type={"primary"}
                    onClick={() => {
                      setEditingKey(key);
                    }}
                  >
                    {i18n.t<string>("Add")}
                  </Button>
                ) : (
                  d.label
                )}
              </div>
              {(!isAddBtn || editing) && (
                <div className={"opt"}>
                  {editingKey == key ? (
                    <Icon
                      type={"select"}
                      onClick={() => {
                        if (isAddBtn) {
                          const item = {
                            label: editingValue,
                          };

                          if (parent) {
                            parent.children ??= [];
                            parent.children.push(item);
                          } else {
                            value.push(item);
                          }
                          // console.log(parent, value);
                        } else {
                          d.label = editingValue;
                        }
                        setValue([...value]);
                        exitEditingMode();
                      }}
                    />
                  ) : (
                    <Icon
                      type={"edit"}
                      onClick={() => {
                        setEditingKey(key);
                        setEditingValue(isAddBtn ? "" : d.label);
                      }}
                    />
                  )}
                  <Icon
                    type={"close"}
                    onClick={() => {
                      if (editing) {
                        exitEditingMode();
                      } else if (confirm("Sure to delete and its children?")) {
                        dataSource.splice(idx, 1);
                        setValue([...value]);
                      }
                    }}
                  />
                </div>
              )}
            </div>
          }
        >
          {!isAddBtn && renderNodes(d.children, key, d)}
        </Tree.Node>,
      );
    }

    return nodes;
  };

  return (
    <Tree
      defaultExpandAll
      showLine
      className={"editable-tree"}
      selectable={false}
      onChange={(v) => console.log(v)}
    >
      {renderNodes(value)}
    </Tree>
  );
};

EditableTree.displayName = "EditableTree";

export default EditableTree;
