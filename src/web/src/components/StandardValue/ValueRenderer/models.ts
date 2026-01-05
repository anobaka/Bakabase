import type { ValueEditorProps } from "../ValueEditor/models";

export type ValueRendererProps<TBizValue, TDbValue = TBizValue> = {
  // db value
  value?: TBizValue;
  // onClick?: () => any;
  variant?: "default" | "light";
  defaultEditing?: boolean;
  /**
   * Whether the value is readonly. Defaults to true if editor is not provided.
   */
  isReadonly?: boolean;
  /**
   * When true, always show the editing UI (input/textarea/picker) instead of
   * toggling between display and edit modes. Useful for filter panels where
   * the input should always be visible and editable.
   */
  isEditing?: boolean;

  editor?: ValueEditorProps<TDbValue, TBizValue>;
};
