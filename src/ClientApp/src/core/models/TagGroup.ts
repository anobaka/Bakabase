import { AliasCommon } from '@/core/models/AliasCommon';
import { Tag } from '@/core/models/Tag';
import i18n from 'i18next';

export class TagGroup extends AliasCommon {
  order: number;
  tags: Tag[];

  constructor(init?: Partial<TagGroup>) {
    super(init);
    Object.assign(this, init);
  }
}
