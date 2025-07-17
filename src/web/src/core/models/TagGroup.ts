import type { Tag } from "@/core/models/Tag";

import { AliasCommon } from "@/core/models/AliasCommon";

export class TagGroup extends AliasCommon {
  order: number;
  tags: Tag[];

  constructor(init?: Partial<TagGroup>) {
    super(init);
    Object.assign(this, init);
  }
}
