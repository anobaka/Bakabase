export type ValueEditorProps<TDbValue = any, TBizValue = TDbValue> = {
  value?: TDbValue;
  onValueChange?: (dbValue?: TDbValue, bizValue?: TBizValue) => any;
  onCancel?: () => any;
};
