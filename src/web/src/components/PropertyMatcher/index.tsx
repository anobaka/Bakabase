"use client";

import type { IProperty } from "../Property/models";
import type { PropertyType } from "@/sdk/constants";

import { MdAutoFixHigh } from "react-icons/md";
import { useTranslation } from "react-i18next";
import { useCallback, useEffect, useState } from "react";
import { AiOutlineDisconnect, AiOutlineSearch } from "react-icons/ai";
import { IoLocate } from "react-icons/io5";

import { Button, Card, Modal, Tooltip } from "@/components/bakaui";
import { PropertyPool } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertySelector from "@/components/PropertySelector";
import BriefProperty from "@/components/Chips/Property/BriefProperty";

type Props = {
  matchedProperty?: IProperty;
  name?: string;
  type: PropertyType;
  options?: any;
  isClearable?: boolean;
  onValueChanged?: (property?: IProperty) => any;
};
const PropertyMatcher = ({
  matchedProperty,
  type,
  name,
  options,
  isClearable,
  onValueChanged: propsOnValueChanged,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [property, setProperty] = useState<IProperty>();
  const [isFindingBestMatch, setIsFindingBestMatch] = useState(false);

  useEffect(() => {
    setProperty(matchedProperty);
  }, [matchedProperty]);

  const onValueChange = useCallback(
    (property?: IProperty) => {
      setProperty(property);
      console.log(property);
      propsOnValueChanged?.(property);
    },
    [propsOnValueChanged],
  );

  return (
    <div className={"inline-flex items-center gap-1"}>
      {property && (
        <>
          <Button
            size={"sm"}
            // color={'primary'}
            variant={"light"}
            onPress={() => {
              createPortal(PropertySelector, {
                pool: PropertyPool.Custom | PropertyPool.Reserved,
                multiple: false,
                onSubmit: async (selection) => {
                  onValueChange(selection[0]!);
                },
                v2: true,
              });
            }}
          >
            <BriefProperty property={property} />
          </Button>
          {isClearable && (
            <Button
              isIconOnly
              color={"warning"}
              size={"sm"}
              variant={"light"}
              onPress={() => {
                onValueChange();
              }}
            >
              <AiOutlineDisconnect className={"text-base"} />
            </Button>
          )}
        </>
      )}
      <Tooltip content={t<string>("Automatically match property")}>
        <Button
          isIconOnly
          color={"primary"}
          isLoading={isFindingBestMatch}
          size={"sm"}
          variant={"light"}
          onPress={async () => {
            if (name == undefined || name.length == 0) {
              createPortal(
                Modal, {
                  defaultVisible: true,
                  title: t<string>("No proper property name provided"),
                  children: (
                    <div className={"flex flex-col gap-2"}>
                      {t<string>("Please provide a property name")}
                    </div>
                  ),
                  footer: {
                    actions: ['cancel']
                  }
                }
              )
              return;
            }
            setIsFindingBestMatch(true);
            try {
              const candidate = await BApi.property.findBestMatchingProperty({
                type,
                name,
              });

              if (candidate.data) {
                onValueChange(candidate.data);
              } else {
                const modal = createPortal(Modal, {
                  defaultVisible: true,
                  title: t<string>("No proper property found"),
                  children: (
                    <div className={"flex flex-col gap-2"}>
                      {t<string>("You can")}:
                      <div className={"grid grid-cols-2 gap-2"}>
                        <Card
                          isPressable
                          className={"flex flex-col items-center gap-2 justify-center py-2"}
                          onPress={async () => {
                            const r = await BApi.customProperty.addCustomProperty({
                              name: name,
                              type: type,
                              options: options ? JSON.stringify(options) : undefined,
                            });

                            if (r.code) {
                              throw new Error(r.message);
                            }
                            const np = r.data!;

                            onValueChange(np);
                            modal.destroy();
                          }}
                        >
                          <MdAutoFixHigh className={"text-2xl"} />
                          {t<string>("Automatically create a new property")}
                        </Card>
                        <Card
                          isPressable
                          className={"flex flex-col items-center gap-2 justify-center py-2"}
                          onPress={() => {
                            modal.destroy();
                            createPortal(PropertySelector, {
                              pool: PropertyPool.Custom | PropertyPool.Reserved,
                              multiple: false,
                              onSubmit: async (selection) => {
                                onValueChange(selection[0]!);
                              },
                              v2: true,
                            });
                          }}
                        >
                          <AiOutlineSearch className={"text-2xl"} />
                          {t<string>("Select manually")}
                        </Card>
                      </div>
                    </div>
                  ),
                  footer: false,
                });
              }
            } finally {
              setIsFindingBestMatch(false);
            }
          }}
        >
          <IoLocate className={"text-base"} />
        </Button>
      </Tooltip>
    </div>
  );
};

PropertyMatcher.displayName = "PropertyMatcher";

export default PropertyMatcher;
