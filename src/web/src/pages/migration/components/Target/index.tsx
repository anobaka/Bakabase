"use client";

import type { PropertyType } from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { FileSearchOutlined, InfoCircleOutlined } from "@ant-design/icons";

import SimplePropertySelector from "../SimplePropertySelector";

import { ResourceProperty } from "@/sdk/constants";
import { Button, Chip, Modal, Tab, Tabs } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

export interface MigrationTarget {
  subTargets?: MigrationTarget[];
  property: ResourceProperty;
  propertyKey?: string;
  dataCount: number;
  data?: any;
  dataForDisplay?: string[];
  targetCandidates?: {
    type: PropertyType;
    lossData?: Record<string, string[]>;
  }[];
}

interface IProps {
  target: MigrationTarget;
  isLeaf: boolean;
  onMigrated?: () => any;
}

const Target = ({ target, isLeaf, onMigrated }: IProps) => {
  const { t } = useTranslation();

  const [label, setLabel] = useState<string>();
  const [dataDialogVisible, setDataDialogVisible] = useState(false);
  const [confirmDialogVisible, setConfirmDialogVisible] = useState(false);
  const [lossDataDialogVisible, setLossDataDialogVisible] = useState(false);

  const [selectedProperty, setSelectedProperty] = useState<IProperty>();

  const lossData = target.targetCandidates?.find(
    (d) => d.type == (selectedProperty?.dbValueType as unknown as PropertyType),
  )?.lossData;
  const hasLossData = lossData && Object.keys(lossData).length > 0;

  useEffect(() => {
    if (
      target.property == ResourceProperty.Volume &&
      target.propertyKey != undefined
    ) {
      setLabel(
        t<string>(`${ResourceProperty[target.property]}.${target.propertyKey}`),
      );
    } else {
      setLabel(
        target.propertyKey ?? t<string>(ResourceProperty[target.property!]),
      );
    }
  }, []);

  return (
    <div className={`flex ${isLeaf ? "" : "flex-col"} gap-2 mt-1 mb-1`}>
      {isLeaf && (
        <Modal
          footer={{
            actions: ["ok"],
          }}
          size={"xl"}
          title={t<string>("Data")}
          visible={dataDialogVisible}
          onClose={() => setDataDialogVisible(false)}
        >
          <div>
            <div className={"italic mb-2"}>
              {t<string>("Data not relative to resource will not be migrated.")}
            </div>
            <div className={"flex flex-wrap gap-1"}>
              {target.dataForDisplay?.map((d) => <Chip>{d}</Chip>)}
            </div>
          </div>
        </Modal>
      )}
      {hasLossData && (
        <Modal
          footer={{
            actions: ["ok"],
          }}
          size={"xl"}
          title={t<string>("Some data will be lost")}
          visible={lossDataDialogVisible}
          onClose={() => setLossDataDialogVisible(false)}
        >
          <Tabs>
            {Object.keys(lossData).map((k) => {
              const lossType = StandardValueConversionLoss[parseInt(k, 10)];

              return (
                <Tab
                  key={k}
                  title={`${t<string>(`StandardValueConversionLoss.${lossType}`)}(${lossData[k].length})`}
                >
                  <div className={"flex flex-wrap gap-1"}>
                    {lossData[k].map((d) => (
                      <Chip>{d}</Chip>
                    ))}
                  </div>
                </Tab>
              );
            })}
          </Tabs>
        </Modal>
      )}
      <div className={"flex items-center gap-2 text-base"}>
        {label}
        {isLeaf && (
          <Button
            className={"cursor-pointer"}
            color={"primary"}
            size={"sm"}
            variant={"light"}
            onClick={() => {
              setDataDialogVisible(true);
            }}
          >
            <FileSearchOutlined className={"text-small"} />
            {target.dataCount}
          </Button>
        )}
      </div>
      {target.subTargets ? (
        <div className={"ml-4"}>
          {target.subTargets.map((subTarget, i) => {
            return (
              <div key={i} className={"flex items-center gap-2"}>
                <Target
                  isLeaf={!subTarget?.subTargets}
                  target={subTarget}
                  onMigrated={onMigrated}
                />
              </div>
            );
          })}
        </div>
      ) : (
        <div className={"flex gap-2 items-center ml-4"}>
          <Modal
            visible={confirmDialogVisible}
            onClose={() => setConfirmDialogVisible(false)}
            onOk={async () => {
              await BApi.migration.migrateTarget({
                property: target.property,
                propertyKey: target.propertyKey,
                targetPropertyId: selectedProperty?.id,
              });
              onMigrated?.();
            }}
          >
            {t<string>(
              "Are you sure to migrate [{{target}}] to [{{property}}]?",
              { target: label, property: selectedProperty?.name },
            )}
          </Modal>
          {t<string>("Convert to")}
          <SimplePropertySelector
            valueTypes={target.targetCandidates?.map((tc) => tc.type)}
            onSelected={(p) => setSelectedProperty(p)}
          />
          {hasLossData && (
            <Button
              color={"danger"}
              size={"sm"}
              variant={"light"}
              onClick={() => {
                setLossDataDialogVisible(true);
              }}
            >
              <InfoCircleOutlined className={"text-small"} />
              {t<string>("Some data will be lost")},{" "}
              {t<string>("check them here")}
            </Button>
          )}
          {selectedProperty && (
            <Button
              color={"primary"}
              size={"sm"}
              onClick={() => {
                setConfirmDialogVisible(true);
              }}
            >
              {t<string>("Migrate")}
            </Button>
          )}
        </div>
      )}
    </div>
  );
};

export default Target;
