import type { IProperty } from "@/components/Property/models";
import type { PropertyType, StandardValueType } from "@/sdk/constants";

import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { isReferenceValueType } from "@/components/Property/PropertySystem";
import StandardValueRenderer from "@/components/StandardValue/ValueRenderer";
import { convertFromApiValue, serializeStandardValue } from "@/components/StandardValue/helpers";

type Props = {
  value: any;
  dbValue?: any;
  type: StandardValueType;
  propertyType?: PropertyType;
  definition?: IProperty;
};

export default function CoverPropertyValue({
  value,
  dbValue,
  type,
  propertyType,
  definition,
}: Props) {
  if (definition && definition.type === propertyType && isReferenceValueType(definition.type)) {
    return (
      <PropertyValueRenderer
        isReadonly
        bizValue={serializeStandardValue(value, type)}
        dbValue={serializeStandardValue(
          convertFromApiValue(dbValue, definition.dbValueType),
          definition.dbValueType,
        )}
        property={definition}
        variant="light"
      />
    );
  }

  return (
    <StandardValueRenderer propertyType={propertyType} type={type} value={value} variant="light" />
  );
}
