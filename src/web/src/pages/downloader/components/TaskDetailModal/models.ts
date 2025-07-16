import type { components } from "@/sdk/BApi2";

type Form =
  components["schemas"]["Bakabase.InsideWorld.Models.RequestModels.DownloadTaskAddInputModel"];

export type ThirdPartyFormComponentProps<TEnumType> = {
  type: TEnumType;
  form?: Partial<Form>;
  onChange: (form: Partial<Form>) => void;
  isReadOnly?: boolean;
};
