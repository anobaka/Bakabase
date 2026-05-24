import type {
  ChoicePropertyOptions,
  IProperty,
  MultilevelPropertyOptions,
  NumberPropertyOptions,
  RatingPropertyOptions,
  TagsPropertyOptions,
} from "../models";

import { TbSelect } from "react-icons/tb";
import { DatabaseOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";
import { AiOutlineStar } from "react-icons/ai";
import { LuRuler } from "react-icons/lu";

import { Tooltip } from "@/components/bakaui";
import { PropertyPool, PropertyType } from "@/sdk/constants";

type Props = {
  property: IProperty;
};

const PropertyExtra = ({ property }: Props) => {
  const { t } = useTranslation();

  const components = [];

  switch (property.type) {
    case PropertyType.SingleLineText:
      break;
    case PropertyType.MultilineText:
      break;
    case PropertyType.SingleChoice:
    case PropertyType.MultipleChoice: {
      const options = property.options as ChoicePropertyOptions;

      if (options) {
        components.push(
          <div key={"choices-count"} className={"flex gap-0.5 items-center text-sm"}>
            <TbSelect className={"text-base"} />
            {options.choices?.length ?? 0}
          </div>,
        );
      }
      break;
    }
    case PropertyType.Number:
    case PropertyType.Percentage: {
      const options = property.options as NumberPropertyOptions;

      if (options) {
        components.push(
          <div key={"number-count"} className={"flex gap-0.5 items-center text-sm"}>
            <LuRuler className={"text-base"} />
            {options.precision ?? 0}
          </div>,
        );
      }
      break;
    }
    case PropertyType.Rating: {
      const options = property.options as RatingPropertyOptions;

      if (options) {
        components.push(
          <div key={"rating-count"} className={"flex gap-0.5 items-center text-sm"}>
            <AiOutlineStar className={"text-base"} />
            {options.maxValue ?? 5}
          </div>,
        );
      }
      break;
    }
    case PropertyType.Boolean:
      break;
    case PropertyType.Link:
      break;
    case PropertyType.Attachment:
      break;
    case PropertyType.Date:
      break;
    case PropertyType.DateTime:
      break;
    case PropertyType.Time:
      break;
    case PropertyType.Formula:
      break;
    case PropertyType.Multilevel: {
      const options = property.options as MultilevelPropertyOptions;

      if (options) {
        components.push(
          <div key={"multilevel-count"} className={"flex gap-0.5 items-center text-sm"}>
            <TbSelect className={"text-base"} />
            {options.data?.length ?? 0}
          </div>,
        );
      }
      break;
    }
    case PropertyType.Tags: {
      const options = property.options as TagsPropertyOptions;

      if (options) {
        components.push(
          <div key={"tags-count"} className={"flex gap-0.5 items-center text-sm"}>
            <TbSelect className={"text-base"} />
            {options.tags?.length ?? 0}
          </div>,
        );
      }
      break;
    }
  }

  if (property.pool == PropertyPool.Custom && property.valueCount && property.valueCount > 0) {
    components.push(
      <Tooltip
        key={"value-count"}
        content={t<string>("property.tip.resourceValueCount", {
          count: property.valueCount,
        })}
        placement={"bottom"}
      >
        <div className={"flex gap-0.5 items-center text-sm"}>
          <DatabaseOutlined className={"text-base"} />
          {property.valueCount}
        </div>
      </Tooltip>,
    );
  }

  if (components.length == 0) {
    return null;
  }

  return <div className={"flex items-center gap-1"}>{components}</div>;
};

export default PropertyExtra;
