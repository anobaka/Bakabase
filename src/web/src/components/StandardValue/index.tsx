/**
 * This StandardValue is a collection of components that are used to render and edit values in GUI. It's different to the StandardValue in back-end.
 */

import Icon from "./Icon";
import ChoiceValueEditor from "./ValueEditor/Editors/ChoiceValueEditor";
import NumberValueEditor from "./ValueEditor/Editors/NumberValueEditor";
import MultilevelValueEditor from "./ValueEditor/Editors/MultilevelValueEditor";
import BooleanValueRenderer from "./ValueRenderer/Renderers/BooleanValueRenderer";
import StringValueRenderer from "./ValueRenderer/Renderers/StringValueRenderer";
import NumberValueRenderer from "./ValueRenderer/Renderers/NumberValueRenderer";
import DateTimeValueRenderer from "./ValueRenderer/Renderers/DateTimeValueRenderer";
import ChoiceValueRenderer from "./ValueRenderer/Renderers/ChoiceValueRenderer";
import FormulaValueRenderer from "./ValueRenderer/Renderers/FormulaValueRenderer";
import AttachmentValueRenderer from "./ValueRenderer/Renderers/AttachmentValueRenderer";
import RatingValueRenderer from "./ValueRenderer/Renderers/RatingValueRenderer";
import MultilevelValueRenderer from "./ValueRenderer/Renderers/MultilevelValueRenderer";
import LinkValueRenderer from "./ValueRenderer/Renderers/LinkValueRenderer";
import TagsValueRenderer from "./ValueRenderer/Renderers/TagsValueRenderer";
import TimeValueRenderer from "./ValueRenderer/Renderers/TimeValueRenderer";

export {
  Icon as StandardValueIcon,
  NumberValueEditor,
  ChoiceValueEditor,
  MultilevelValueEditor,
  AttachmentValueRenderer,
  BooleanValueRenderer,
  ChoiceValueRenderer,
  DateTimeValueRenderer,
  FormulaValueRenderer,
  MultilevelValueRenderer,
  NumberValueRenderer,
  RatingValueRenderer,
  StringValueRenderer,
  TagsValueRenderer,
  TimeValueRenderer,
  LinkValueRenderer,
};
