"use client";

import { useState } from "react";
import dayjs from "dayjs";

import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { PropertyType, PropertyPool, PropertyTypeLabel, PropertyPoolLabel } from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";
import { serializeStandardValue } from "@/components/StandardValue";
import { getDbValueType, getBizValueType } from "@/components/Property/PropertySystem";
import { Tabs, Tab } from "@/components/bakaui";

// Helper to create test property with all required fields
const createTestProperty = (
  type: PropertyType,
  options: any = {},
): IProperty => ({
  id: type,
  name: PropertyTypeLabel[type],
  type,
  pool: PropertyPool.Custom,
  dbValueType: getDbValueType(type),
  bizValueType: getBizValueType(type),
  typeName: PropertyTypeLabel[type],
  poolName: PropertyPoolLabel[PropertyPool.Custom],
  order: type,
  options,
});

// Test properties with sample data
const testProperties: IProperty[] = [
  createTestProperty(PropertyType.SingleLineText),
  createTestProperty(PropertyType.MultilineText),
  createTestProperty(PropertyType.SingleChoice, {
    choices: [
      { label: "Option A", value: "opt-a", color: "#ff5733" },
      { label: "Option B", value: "opt-b", color: "#33ff57" },
      { label: "Option C", value: "opt-c", color: "#3357ff" },
    ],
  }),
  createTestProperty(PropertyType.MultipleChoice, {
    choices: [
      { label: "Choice 1", value: "ch-1", color: "#e91e63" },
      { label: "Choice 2", value: "ch-2", color: "#9c27b0" },
      { label: "Choice 3", value: "ch-3", color: "#673ab7" },
    ],
  }),
  createTestProperty(PropertyType.Number, { precision: 2 }),
  createTestProperty(PropertyType.Percentage, { precision: 1, showProgressbar: true }),
  createTestProperty(PropertyType.Rating, { maxValue: 5 }),
  createTestProperty(PropertyType.Boolean),
  createTestProperty(PropertyType.Link),
  createTestProperty(PropertyType.Attachment),
  createTestProperty(PropertyType.Date),
  createTestProperty(PropertyType.DateTime),
  createTestProperty(PropertyType.Time),
  createTestProperty(PropertyType.Formula),
  createTestProperty(PropertyType.Multilevel, {
    data: [
      {
        value: "ml-1",
        label: "Level 1",
        children: [
          {
            value: "ml-1-1",
            label: "Level 1-1",
            color: "#4caf50",
            children: [
              { value: "ml-1-1-1", label: "Level 1-1-1" },
            ],
          },
          { value: "ml-1-2", label: "Level 1-2" },
        ],
      },
      { value: "ml-2", label: "Level 2", color: "#2196f3" },
    ],
  }),
  createTestProperty(PropertyType.Tags, {
    tags: [
      { value: "tag-1", group: "Group A", name: "Tag 1" },
      { value: "tag-2", group: "Group A", name: "Tag 2" },
      { value: "tag-3", group: "Group B", name: "Tag 3" },
      { value: "tag-4", name: "Ungrouped Tag" },
    ],
  }),
];

// Sample values for each property type (raw values before serialization)
const getSampleValues = (propertyType: PropertyType): { dbValue: any; bizValue?: any } => {
  switch (propertyType) {
    case PropertyType.SingleLineText:
      return { dbValue: "Sample single line text" };
    case PropertyType.MultilineText:
      return { dbValue: "Line 1\nLine 2\nLine 3" };
    case PropertyType.SingleChoice:
      return { dbValue: "opt-a", bizValue: "Option A" };
    case PropertyType.MultipleChoice:
      return { dbValue: ["ch-1", "ch-2"], bizValue: ["Choice 1", "Choice 2"] };
    case PropertyType.Number:
      return { dbValue: 12345.67 };
    case PropertyType.Percentage:
      return { dbValue: 75.5 };
    case PropertyType.Rating:
      return { dbValue: 4 };
    case PropertyType.Boolean:
      return { dbValue: true };
    case PropertyType.Link:
      return { dbValue: { text: "Example Link", url: "https://example.com" } };
    case PropertyType.Attachment:
      return { dbValue: ["/path/to/file1.jpg", "/path/to/file2.png"] };
    case PropertyType.Date:
      return { dbValue: dayjs("2024-06-15") };
    case PropertyType.DateTime:
      return { dbValue: dayjs("2024-06-15T14:30:00") };
    case PropertyType.Time:
      return { dbValue: dayjs.duration({ hours: 2, minutes: 30, seconds: 45 }) };
    case PropertyType.Formula:
      return { dbValue: "=SUM(A1:A10)" };
    case PropertyType.Multilevel:
      return { dbValue: ["ml-1-1-1"], bizValue: [["Level 1", "Level 1-1", "Level 1-1-1"]] };
    case PropertyType.Tags:
      return {
        dbValue: ["tag-1", "tag-3"],
        bizValue: [
          { group: "Group A", name: "Tag 1" },
          { group: "Group B", name: "Tag 3" },
        ],
      };
    default:
      return { dbValue: undefined };
  }
};

// Serialize values for the component
const getSerializedValues = (property: IProperty, hasValue: boolean): { dbValue?: string; bizValue?: string } => {
  if (!hasValue) {
    return { dbValue: undefined, bizValue: undefined };
  }

  const { dbValue, bizValue } = getSampleValues(property.type);
  const dbValueType = getDbValueType(property.type);
  const bizValueType = getBizValueType(property.type);

  return {
    dbValue: serializeStandardValue(dbValue, dbValueType),
    bizValue: bizValue !== undefined ? serializeStandardValue(bizValue, bizValueType) : undefined,
  };
};

type Variant = "default" | "light";
type Size = "sm" | "md" | "lg";

const variants: Variant[] = ["default", "light"];
const sizes: Size[] = ["sm", "md", "lg"];
const hasValueOptions = [true, false];
const isReadonlyOptions = [false, true];
const isEditingOptions: (boolean | undefined)[] = [undefined, true, false];

const PropertyValueRendererTestPage = () => {
  const [changedValues, setChangedValues] = useState<Record<string, { dbValue?: string; bizValue?: string }>>({});

  const handleValueChange = (key: string) => (dbValue?: string, bizValue?: string) => {
    setChangedValues((prev) => ({
      ...prev,
      [key]: { dbValue, bizValue },
    }));
    console.log(`Value changed for ${key}:`, { dbValue, bizValue });
  };

  const renderCell = (
    property: IProperty,
    variant: Variant,
    size: Size,
    isReadonly: boolean,
    isEditing: boolean | undefined,
    hasValue: boolean,
  ) => {
    const key = `${property.type}-${variant}-${size}-${isReadonly}-${isEditing}-${hasValue}`;

    // Use changed values if key exists in changedValues, otherwise use initial values
    const hasChangedValue = key in changedValues;
    const changedValue = changedValues[key];
    const initialValues = getSerializedValues(property, hasValue);
    const dbValue = hasChangedValue ? changedValue.dbValue : initialValues.dbValue;
    const bizValue = hasChangedValue ? changedValue.bizValue : initialValues.bizValue;

    const props: any = {
      property,
      dbValue,
      bizValue,
      variant,
      size,
      isReadonly,
    };

    if (isEditing !== undefined) {
      props.isEditing = isEditing;
    }

    // Only add onValueChange if not readonly
    if (!isReadonly) {
      props.onValueChange = handleValueChange(key);
    }

    return <PropertyValueRenderer key={key} {...props} />;
  };

  // Render all 3 sizes stacked vertically in one cell
  const renderSizeStack = (
    property: IProperty,
    variant: Variant,
    isReadonly: boolean,
    isEditing: boolean | undefined,
    hasValue: boolean,
  ) => {
    return (
      <div className="flex flex-col gap-2">
        {sizes.map((size) => (
          <div key={size} className="flex items-center gap-2">
            <span className="text-gray-400 w-6 text-right">{size}</span>
            <div className="flex-1">
              {renderCell(property, variant, size, isReadonly, isEditing, hasValue)}
            </div>
          </div>
        ))}
      </div>
    );
  };

  const renderPropertyTable = (property: IProperty) => (
    <div className="overflow-x-auto">
      <table className="border-collapse text-xs">
        <thead>
          <tr className="bg-default-100">
            <th className="border p-1 text-left">hasValue</th>
            <th className="border p-1 text-left">isReadonly</th>
            <th className="border p-1 text-left">isEditing</th>
            {variants.map((variant) => (
              <th key={variant} className="border p-1 text-center min-w-[200px]">
                {variant}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {hasValueOptions.map((hasValue) =>
            isReadonlyOptions.map((isReadonly, isReadonlyIdx) =>
              isEditingOptions.map((isEditing, isEditingIdx) => (
                <tr key={`${hasValue}-${isReadonly}-${isEditing}`}>
                  {isReadonlyIdx === 0 && isEditingIdx === 0 && (
                    <td
                      className="border p-1 align-top font-medium"
                      rowSpan={isReadonlyOptions.length * isEditingOptions.length}
                    >
                      <span className={hasValue ? "text-blue-600" : "text-gray-400"}>
                        {hasValue ? "value" : "undefined"}
                      </span>
                    </td>
                  )}
                  {isEditingIdx === 0 && (
                    <td
                      className="border p-1 align-top"
                      rowSpan={isEditingOptions.length}
                    >
                      <span className={isReadonly ? "text-green-600" : "text-orange-600"}>
                        {isReadonly ? "true" : "false"}
                      </span>
                    </td>
                  )}
                  <td className="border p-1">
                    <span className={isEditing === undefined ? "text-purple-500" : ""}>
                      {isEditing === undefined ? "undefined" : String(isEditing)}
                    </span>
                  </td>
                  {variants.map((variant) => (
                    <td
                      key={variant}
                      className={`border p-2 ${variant === "light" ? "bg-default-50" : ""}`}
                    >
                      {renderSizeStack(property, variant, isReadonly, isEditing, hasValue)}
                    </td>
                  ))}
                </tr>
              )),
            ),
          )}
        </tbody>
      </table>
    </div>
  );

  return (
    <div className="flex flex-col gap-4 p-4">
      <h1 className="text-2xl font-bold">PropertyValueRenderer Test Cases</h1>
      <p className="text-sm text-gray-500">
        Dimensions: hasValue x isReadonly x isEditing x size x variant
      </p>
      <p className="text-xs text-gray-400">
        Default values: variant="default", size="md", isReadonly=false
      </p>

      <Tabs aria-label="Property Types">
        {testProperties.map((property) => (
          <Tab key={property.type} title={property.name}>
            <div className="pt-4">
              {renderPropertyTable(property)}
            </div>
          </Tab>
        ))}
      </Tabs>

      {/* Changed values log */}
      {Object.keys(changedValues).length > 0 && (
        <div className="fixed bottom-4 right-4 bg-default-100 p-4 rounded-lg shadow-lg max-w-md max-h-60 overflow-auto z-20">
          <h3 className="font-semibold mb-2">Changed Values:</h3>
          <pre className="text-xs">{JSON.stringify(changedValues, null, 2)}</pre>
        </div>
      )}
    </div>
  );
};

PropertyValueRendererTestPage.displayName = "PropertyValueRendererTestPage";

export default PropertyValueRendererTestPage;
