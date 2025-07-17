import { AliasCommon } from "@/core/models/AliasCommon";

export class Tag extends AliasCommon {
  color: string;
  order: number;
  groupId: number;
  groupName: string;

  constructor(init?: Partial<Tag>) {
    super(init);
    Object.assign(this, init);
  }
}
