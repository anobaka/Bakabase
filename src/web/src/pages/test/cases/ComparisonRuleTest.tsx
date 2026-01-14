"use client";

import { useState } from "react";
import { useTranslation } from "react-i18next";

import { Card, CardBody, CardHeader } from "@/components/bakaui";
import { PropertyPool, PropertyType, ComparisonMode, NullValueBehavior } from "@/sdk/constants";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import RuleConfigPanel from "@/pages/comparison/components/RuleConfigPanel";
import type { RuleConfig } from "@/pages/comparison/components/RuleConfigPanel";
import { getAvailableModesForPropertyType } from "@/pages/comparison/components/RuleConfigPanel";

// All property types to test
const AllPropertyTypes = [
  { type: PropertyType.SingleLineText, name: "SingleLineText" },
  { type: PropertyType.MultilineText, name: "MultilineText" },
  { type: PropertyType.SingleChoice, name: "SingleChoice" },
  { type: PropertyType.MultipleChoice, name: "MultipleChoice" },
  { type: PropertyType.Number, name: "Number" },
  { type: PropertyType.Percentage, name: "Percentage" },
  { type: PropertyType.Rating, name: "Rating" },
  { type: PropertyType.Boolean, name: "Boolean" },
  { type: PropertyType.Link, name: "Link" },
  { type: PropertyType.Attachment, name: "Attachment" },
  { type: PropertyType.Date, name: "Date" },
  { type: PropertyType.DateTime, name: "DateTime" },
  { type: PropertyType.Time, name: "Time" },
  { type: PropertyType.Formula, name: "Formula" },
  { type: PropertyType.Multilevel, name: "Multilevel" },
  { type: PropertyType.Tags, name: "Tags" },
];

const RuleTestPanel = ({ propertyType, propertyName }: { propertyType: PropertyType; propertyName: string }) => {
  const { t } = useTranslation();
  const availableModes = getAvailableModesForPropertyType(propertyType);

  // Create a fake property for testing (cast to any for test purposes)
  const fakeProperty = {
    id: 1,
    pool: PropertyPool.Custom,
    name: propertyName,
    type: propertyType,
  } as any;

  const [config, setConfig] = useState<RuleConfig>({
    propertyPool: PropertyPool.Custom,
    propertyId: 1,
    property: fakeProperty,
    mode: availableModes[0],
    weight: 1,
    isVeto: false,
    vetoThreshold: 1.0,
    oneNullBehavior: NullValueBehavior.Skip,
    bothNullBehavior: NullValueBehavior.Skip,
  });

  return (
    <Card className="mb-4">
      <CardHeader className="flex items-center gap-2 pb-2">
        <BriefProperty
          property={{ name: propertyName, pool: PropertyPool.Custom, type: propertyType }}
        />
        <span className="text-xs text-default-400 ml-auto">
          Modes: {availableModes.map(m => ComparisonMode[m]).join(", ")}
        </span>
      </CardHeader>
      <CardBody className="pt-0">
        <RuleConfigPanel
          value={config}
          onChange={setConfig}
          showPropertySelector={false}
        />
      </CardBody>
    </Card>
  );
};

const ComparisonRuleTest = () => {
  const { t } = useTranslation();

  return (
    <div className="flex flex-col gap-4 p-4">
      <h2 className="text-xl font-bold">Comparison Rule Configuration Test</h2>
      <p className="text-default-500">
        This page shows the real RuleConfigPanel component for all property types.
        Each property type has different available comparison modes.
      </p>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        {AllPropertyTypes.map((pt) => (
          <RuleTestPanel
            key={pt.type}
            propertyName={pt.name}
            propertyType={pt.type}
          />
        ))}
      </div>
    </div>
  );
};

ComparisonRuleTest.displayName = "ComparisonRuleTest";

export default ComparisonRuleTest;
