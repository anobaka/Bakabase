"use client";

import { StringValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/StringValueProcess";
import { ListStringValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/ListStringValueProcess";
import { DecimalValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/DecimalValueProcess";
import { BooleanValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/BooleanValueProcess";
import { DateTimeValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/DateTimeValueProcess";
import { TimeValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/TimeValueProcess";
import { LinkValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/LinkValueProcess";
import { ListListStringValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/ListListStringValueProcess";
import { ListTagValueProcessEditor } from "@/pages/bulk-modification/components/BulkModification/Processes/ListTagValueProcess";
import { Card, CardBody, CardHeader, Divider } from "@/components/bakaui";
import {
  BulkModificationProcessorValueType,
  PropertyPool,
  PropertyType,
  StandardValueType,
} from "@/sdk/constants";
import type { IProperty } from "@/components/Property/models";
import type { BulkModificationVariable } from "@/pages/bulk-modification/components/BulkModification/models";

const createMockProperty = (
  type: PropertyType,
  name: string,
  bizValueType: StandardValueType,
  dbValueType: StandardValueType,
): IProperty => ({
  id: Math.random(),
  pool: PropertyPool.Custom,
  poolName: "Custom",
  type,
  typeName: PropertyType[type],
  name,
  bizValueType,
  dbValueType,
  order: 0,
});

const availableValueTypes = [
  BulkModificationProcessorValueType.ManuallyInput,
  BulkModificationProcessorValueType.Variable,
];

const mockVariables: BulkModificationVariable[] = [
  {
    key: "var1",
    name: "Test Variable 1",
    propertyPool: PropertyPool.Custom,
    propertyId: 1,
    property: createMockProperty(
      PropertyType.SingleLineText,
      "Source Property",
      StandardValueType.String,
      StandardValueType.String,
    ),
    scope: 1,
  },
];

const noop = () => {};

const BulkModificationProcessEditorsTest = () => {
  const multipleChoiceProperty = createMockProperty(
    PropertyType.MultipleChoice,
    "Test Multiple Choice",
    StandardValueType.ListString,
    StandardValueType.ListString,
  );

  return (
    <div className="flex flex-col gap-4 p-4">
      <h1 className="text-xl font-bold">Bulk Modification Process Editors Test</h1>
      <p className="text-default-500">
        This page demonstrates all available process editors for bulk modification.
      </p>
      <Divider />
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
        <Card>
          <CardHeader className="font-semibold">String (SingleLineText)</CardHeader>
          <CardBody>
            <StringValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.SingleLineText}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">Number</CardHeader>
          <CardBody>
            <DecimalValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.Number}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">Boolean</CardHeader>
          <CardBody>
            <BooleanValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.Boolean}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">DateTime</CardHeader>
          <CardBody>
            <DateTimeValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.DateTime}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">Time</CardHeader>
          <CardBody>
            <TimeValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.Time}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">Link</CardHeader>
          <CardBody>
            <LinkValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.Link}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">ListString (MultipleChoice)</CardHeader>
          <CardBody>
            <ListStringValueProcessEditor
              availableValueTypes={availableValueTypes}
              property={multipleChoiceProperty}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">ListListString (Multilevel)</CardHeader>
          <CardBody>
            <ListListStringValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.Multilevel}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>

        <Card>
          <CardHeader className="font-semibold">ListTag (Tags)</CardHeader>
          <CardBody>
            <ListTagValueProcessEditor
              availableValueTypes={availableValueTypes}
              propertyType={PropertyType.Tags}
              variables={mockVariables}
              onChange={noop}
            />
          </CardBody>
        </Card>
      </div>
    </div>
  );
};

BulkModificationProcessEditorsTest.displayName = "BulkModificationProcessEditorsTest";

export default BulkModificationProcessEditorsTest;
